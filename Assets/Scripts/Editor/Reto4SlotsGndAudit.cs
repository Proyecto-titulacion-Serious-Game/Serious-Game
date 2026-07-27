using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// AUDITORÍA del Reto 4 pedida tras el reporte "no se puede completar el reto":
///   A) Escena real — Bareboard: slots (railIds, huecos por net, separaciones), riel GND,
///      pinNodeMap del ArduinoCore, headers GND del modelo, y SI EXISTE algún puente pre-cableado
///      entre el riel GND del protoboard y el nodoGND del Arduino (si no, el jugador DEBE
///      cerrar ese tramo con un jumper — de lo contrario "no llega a GND").
///   B) Motor — validación sintética de un circuito SIN LED (pin→R330→GND directo): debe PASAR
///      (el sandbox acepta corriente continua sin LED). Control: mismo circuito CON LED, y un
///      caso "riel GND flotante" que debe FALLAR con mensaje de conexión abierta (sin pedir LED).
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4SlotsGndAudit.Run -logFile
/// </summary>
public static class Reto4SlotsGndAudit
{
    const string ScenePath     = "Assets/Scenes/Explorador.unity";
    const string BareboardPath = "GameZones/Reto4_Zone/Reto4_TiltGroup/Bareboard";

    [MenuItem("Tools/TITA/Reto 4/Auditoria slots + GND + circuito sin LED (headless)")]
    public static void Run()
    {
        bool ok = true;
        ok &= AuditarEscena();
        ok &= ValidarSinLedSintetico();

        Debug.Log(ok
            ? "##AUDIT## ===== RESULTADO GLOBAL: ✓ auditoria sin fallas ====="
            : "##AUDIT## ===== RESULTADO GLOBAL: ✗ hay hallazgos (ver arriba) =====");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    // ─────────────────────────────────────────────
    //  A) Auditoría de la escena real
    // ─────────────────────────────────────────────
    static bool AuditarEscena()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool ok = true;

        var sim = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => GetPath(s.transform) == BareboardPath);
        if (sim == null) { Debug.LogError("##AUDIT## No encontré el Bareboard del Reto 4."); return false; }

        var slots = sim.todosLosSlots.Where(s => s != null).ToList();
        var sinRail = slots.Where(s => string.IsNullOrEmpty(s.railId)).ToList();
        var grupos  = slots.Where(s => !string.IsNullOrEmpty(s.railId))
                           .GroupBy(s => s.railId).OrderBy(g => g.Key).ToList();

        Debug.Log($"##AUDIT## SLOTS: total={slots.Count}  nets={grupos.Count}  sinRailId={sinRail.Count}");
        if (sinRail.Count > 0) { ok = false; Debug.LogError($"##AUDIT## ✗ {sinRail.Count} slots SIN railId: {string.Join(", ", sinRail.Select(s => s.name))}"); }

        foreach (var g in grupos)
        {
            var lista = g.ToList();
            float dMin = float.MaxValue, dMax = 0f;
            for (int i = 0; i < lista.Count; i++)
                for (int j = i + 1; j < lista.Count; j++)
                {
                    float d = Vector3.Distance(lista[i].transform.position, lista[j].transform.position);
                    if (d < dMin) dMin = d;
                    if (d > dMax) dMax = d;
                }
            string sep = lista.Count > 1 ? $"sepIntraNet min={dMin * 100f:F2}cm max={dMax * 100f:F2}cm" : "1 solo hueco";
            Debug.Log($"##AUDIT##   net '{g.Key}': {lista.Count} hueco(s) — {sep}");
        }

        // Nodos por rail (BuildNodeMap vía NodeForRail)
        foreach (var g in grupos)
        {
            var node = sim.NodeForRail(g.Key);
            if (node == null) { ok = false; Debug.LogError($"##AUDIT## ✗ NodeForRail('{g.Key}') = null"); }
            bool todosIguales = g.All(s => s.assignedNode == node);
            if (!todosIguales) { ok = false; Debug.LogError($"##AUDIT## ✗ net '{g.Key}': slots apuntan a nodos DISTINTOS"); }
        }

        Debug.Log($"##AUDIT## Separación mínima entre nets distintos: {sim.SepararacionMinimaEntreNetsDistintos() * 100f:F2} cm");

        // Arduino
        var arduino = Object.FindObjectsByType<ArduinoCore>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(a => Vector3.Distance(a.transform.position, sim.transform.position)).FirstOrDefault();
        if (arduino == null) { Debug.LogError("##AUDIT## ✗ No hay ArduinoCore en la escena."); return false; }

        int pinesMapeados = arduino.pinNodeMap?.Count(m => m.node != null) ?? 0;
        var pines = arduino.pinNodeMap != null ? string.Join(",", arduino.pinNodeMap.Where(m => m.node != null).Select(m => "D" + m.pin)) : "";
        Debug.Log($"##AUDIT## ARDUINO '{arduino.name}': pines mapeados={pinesMapeados} [{pines}]  nodoGND={(arduino.nodoGND != null ? arduino.nodoGND.name : "NULL ✗")}");
        if (arduino.nodoGND == null) ok = false;

        int headersGnd = arduino.GetComponentsInChildren<ElectricalNode>(true).Count(n => n.name.StartsWith("Nodo_GND"));
        Debug.Log($"##AUDIT## Headers GND enchufables del modelo (Nodo_GND*): {headersGnd}");

        // ¿El riel GND del protoboard está pre-puenteado al nodoGND del Arduino?
        var railGnd = sim.NodeForRail("GND");
        if (railGnd == null)
        {
            Debug.LogWarning("##AUDIT## ⚠ El Bareboard no tiene net 'GND' (riel GND).");
        }
        else
        {
            bool puenteado = false;
            foreach (var c in Object.FindObjectsByType<ElectricalComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null || c.nodeA == null || c.nodeB == null) continue;
                bool tocaRail = c.nodeA == railGnd || c.nodeB == railGnd;
                bool tocaGnd  = c.nodeA == arduino.nodoGND || c.nodeB == arduino.nodoGND;
                if (tocaRail && tocaGnd) { puenteado = true; Debug.Log($"##AUDIT## Riel GND ↔ nodoGND puenteado por '{c.name}' ({c.GetType().Name})"); break; }
            }
            if (!puenteado)
                Debug.LogWarning("##AUDIT## ⚠ El riel GND del protoboard NO está pre-conectado al GND del Arduino: " +
                                 "el jugador DEBE cerrar ese tramo con un jumper (riel GND → pin GND del Arduino) " +
                                 "o el circuito 'no llega a GND' aunque todo lo demás esté bien.");
        }

        // Distancia de cada pin del Arduino al slot más cercano (alcance de jumpers/resistor-puente)
        if (arduino.pinNodeMap != null)
        {
            float dMinPin = float.MaxValue, dMaxPin = 0f;
            foreach (var m in arduino.pinNodeMap)
            {
                if (m.node == null) continue;
                float d = slots.Min(s => Vector3.Distance(s.transform.position, m.node.transform.position));
                if (d < dMinPin) dMinPin = d;
                if (d > dMaxPin) dMaxPin = d;
            }
            Debug.Log($"##AUDIT## Distancia pin-header → slot más cercano: min={dMinPin * 100f:F1}cm max={dMaxPin * 100f:F1}cm");
        }

        return ok;
    }

    // ─────────────────────────────────────────────
    //  B) Validación sintética: circuito SIN LED debe pasar
    // ─────────────────────────────────────────────
    static bool ValidarSinLedSintetico()
    {
        bool ok = true;

        // Caso 1: pin9 → R330 → GND directo, sketch HIGH fijo. DEBE pasar.
        var r1 = Validar(conLed: false, gndFlotante: false);
        Debug.Log($"##AUDIT## SIN LED (pin→R330→GND): success={r1.success} msg=\"{r1.message}\"");
        if (!r1.success) { ok = false; Debug.LogError("##AUDIT## ✗ Un circuito válido SIN LED fue rechazado."); }

        // Caso 2 (control): mismo circuito CON LED. DEBE pasar.
        var r2 = Validar(conLed: true, gndFlotante: false);
        Debug.Log($"##AUDIT## CON LED (pin→R330→LED→GND): success={r2.success} msg=\"{r2.message}\"");
        if (!r2.success) { ok = false; Debug.LogError("##AUDIT## ✗ El circuito de control CON LED fue rechazado."); }

        // Caso 3: riel GND FLOTANTE (la R termina en un nodo que NO llega al GND del Arduino).
        // DEBE fallar con "no llega a GND" y SIN pedir un LED.
        var r3 = Validar(conLed: false, gndFlotante: true);
        bool msgNeutral = string.IsNullOrEmpty(r3.message) ||
                          !r3.message.ToLowerInvariant().Contains("led");
        Debug.Log($"##AUDIT## GND FLOTANTE: success={r3.success} msg=\"{r3.message}\" (menciona LED: {(!msgNeutral ? "SÍ ✗" : "no ✓")})");
        if (r3.success) { ok = false; Debug.LogError("##AUDIT## ✗ Un circuito con GND flotante fue aceptado."); }
        if (!msgNeutral) { ok = false; Debug.LogError("##AUDIT## ✗ El mensaje de GND flotante le pide un LED a un circuito sin LED."); }

        // Clasificación del diagnóstico para el Técnico en el caso sin-LED fallido:
        var motivo = Reto4Feedback.Clasificar(r3);
        Debug.Log($"##AUDIT## Clasificar(GND flotante) = {motivo} (esperado: SinCamino)");
        if (motivo != Reto4Diagnostico.SinCamino) { ok = false; Debug.LogError("##AUDIT## ✗ Clasificación inesperada para GND flotante."); }

        return ok;
    }

    static SandboxValidationResult Validar(bool conLed, bool gndFlotante)
    {
        var root = new GameObject("AuditReto4Rig");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var gnd = NewNode(root.transform, "GND");
        var goArduino = new GameObject("ArduinoAudit");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND = gnd;
        core.outputVoltageTTL = 5f;

        var pinNode = NewNode(root.transform, "Pin9");
        core.RegisterPinNode(9, pinNode);

        // Extremo final: GND real, o un "riel" flotante que no conecta a nada.
        var fin = gndFlotante ? NewNode(root.transform, "RielGndFlotante") : gnd;

        var res = NewComp<Resistor>(root.transform, "R330");
        res.resistance = 330f;
        res.nodeA = pinNode;

        if (conLed)
        {
            var mid = NewNode(root.transform, "Mid");
            res.nodeB = mid;
            var led = NewComp<LED>(root.transform, "LED");
            led.forwardVoltage = 2f; led.resistance = 50f; led.maxSafeCurrent = 0.02f;
            led.nodeA = mid; led.nodeB = fin;
        }
        else
        {
            res.nodeB = fin;
        }

        core.LoadSketchProgram("void setup() { pinMode(9, OUTPUT); } void loop() { digitalWrite(9, HIGH); }");
        if (!core.ProgramRunning)
        {
            Object.DestroyImmediate(root);
            return new SandboxValidationResult { success = false, message = "El sketch no compiló." };
        }

        var interpField = typeof(ArduinoCore).GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance);
        var interp = (ArduinoInterpreter)interpField.GetValue(core);
        Drain(interp.RunSetup());
        Drain(interp.RunLoop());

        protoSim.ForzarValidacion();

        var resultField = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (SandboxValidationResult)resultField.GetValue(protoSim);

        Object.DestroyImmediate(root);
        return result;
    }

    static void Drain(IEnumerable<ArduinoInterpreter.Signal> seq)
    {
        int n = 0;
        foreach (var _ in seq) { if (++n >= 2000) break; }
    }

    static ElectricalNode NewNode(Transform parent, string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.AddComponent<ElectricalNode>();
    }

    static T NewComp<T>(Transform parent, string n) where T : Component
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
