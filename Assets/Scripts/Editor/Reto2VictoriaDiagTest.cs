using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reproduce, sobre la escena REAL, el escenario "LED de reemplazo bien colocado" y llama al
/// MISMO chequeo de victoria que usa el botón físico (GameManager.CumpleVictoriaRetos123) para ver
/// EXACTAMENTE por qué el Reto 2 no completa aunque a simple vista esté todo bien — el propio
/// chequeo ya arma un diagnóstico rico (_diagParallel) que dice LED por LED qué falla.
///
/// Menú: Tools → TITA → Reto 2 → Diagnosticar por qué no completa (headless)
/// </summary>
public static class Reto2VictoriaDiagTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 2/Diagnosticar por qué no completa (headless)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { Debug.LogError("[Reto2VictoriaDiag] No hay GameManager en la escena."); Finish(1); return; }
        var tGm = typeof(GameManager);

        // LoadLevel(1) real (no solo setear _currentLevel a mano): activa/desactiva las 4 zonas
        // igual que en juego real (reto2Zone ON, reto1Zone/reto3Zone/reto4Zone OFF) — si no, los
        // LEDs de OTROS retos quedan "activos" según como haya quedado guardada la escena y
        // contaminan el conteo de CumpleVictoriaRetos123 (falso positivo de este test, no del juego).
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 1 });
        Debug.Log($"[Reto2VictoriaDiag] reto1Zone.active={(gm.reto1Zone != null ? gm.reto1Zone.activeSelf.ToString() : "null")} " +
                  $"reto2Zone.active={(gm.reto2Zone != null ? gm.reto2Zone.activeSelf.ToString() : "null")} " +
                  $"reto3Zone.active={(gm.reto3Zone != null ? gm.reto3Zone.activeSelf.ToString() : "null")} " +
                  $"reto4Zone.active={(gm.reto4Zone != null ? gm.reto4Zone.activeSelf.ToString() : "null")}");

        // Reto2CircuitGuard: instanciar a mano (el Bootstrap real no corre fuera de Play Mode).
        var guard = Object.FindAnyObjectByType<Reto2CircuitGuard>();
        if (guard == null)
        {
            var go = new GameObject("Reto2CircuitGuard_Diag");
            guard = go.AddComponent<Reto2CircuitGuard>();
            typeof(Reto2CircuitGuard).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, guard);
        }
        var tGuard = typeof(Reto2CircuitGuard);
        InvokePrivate(tGuard, guard, "Activar");
        InvokePrivate(tGuard, guard, "ForzarLedDanado");   // normalmente corre 0.4s después vía Invoke() — no dispara fuera de Play Mode

        var sim = (ProtoboardSimulator)GetPrivateField(tGuard, guard, "_sim");
        if (sim == null) { Debug.LogError("[Reto2VictoriaDiag] _sim quedó null tras Activar()."); Finish(1); return; }

        // ── Simular los CABLES de batería→rieles (CableElectricalBridge solo evalúa en Update(),
        // que no corre fuera de Play Mode — sin esto, la fuente queda desconectada de los rieles y
        // NINGÚN LED tiene voltaje aunque esté perfectamente cableado, dando un falso "no completa"). ──
        var vs = Object.FindAnyObjectByType<VoltageSource>();
        if (vs == null)
        {
            Debug.LogWarning("[Reto2VictoriaDiag] No encontré ningún VoltageSource en la escena — no puedo simular los cables de batería.");
        }
        else
        {
            Debug.Log($"[Reto2VictoriaDiag] VoltageSource '{vs.name}' nodeA={(vs.nodeA != null ? vs.nodeA.name : "null")} nodeB={(vs.nodeB != null ? vs.nodeB.name : "null")}");
            var vccRail = sim.NodeForRail("VCC");
            var gndRail = sim.NodeForRail("GND");
            Debug.Log($"[Reto2VictoriaDiag] Rieles: VCC={(vccRail != null ? vccRail.name : "null")} GND={(gndRail != null ? gndRail.name : "null")}");

            if (vs.nodeA != null && vccRail != null && vs.nodeA != vccRail)
                CrearJumper(sim, "CableDiag_Bateria_VCC", vs.nodeA, vccRail);
            if (vs.nodeB != null && gndRail != null && vs.nodeB != gndRail)
                CrearJumper(sim, "CableDiag_Bateria_GND", vs.nodeB, gndRail);

            sim.ForzarValidacion();
        }

        // ── Estado ANTES de reemplazar nada ──
        ReportarLEDs(sim, "ANTES de entregar el reemplazo");

        // ── Simular la ENTREGA de un LED correcto, colocado en la posición del dañado ──
        var ledGO = new GameObject("LED_Reemplazo_Diag", typeof(LED));
        var led = ledGO.GetComponent<LED>();
        InvokePrivate(tGuard, guard, "CablearEnRamaDanada", new object[] { ledGO });

        ReportarLEDs(sim, "DESPUÉS de cablear el reemplazo");

        // ── Chequeo de victoria REAL (el mismo que usa el botón físico) ──
        var resultado = (bool)InvokePrivate(tGm, gm, "CumpleVictoriaRetos123");
        string diagParallel = (string)GetPrivateField(tGm, gm, "_diagParallel");

        Debug.Log($"[Reto2VictoriaDiag] CumpleVictoriaRetos123() = {resultado}");
        Debug.Log($"[Reto2VictoriaDiag] _diagParallel = {diagParallel}");

        Debug.Log(resultado
            ? "\n[Reto2VictoriaDiag] ===== RESULTADO: ✓ CumpleVictoriaRetos123() da TRUE con el reemplazo bien colocado ====="
            : "\n[Reto2VictoriaDiag] ===== RESULTADO: ✗ CumpleVictoriaRetos123() da FALSE — ver _diagParallel arriba para la causa exacta =====");

        Finish(resultado ? 0 : 1);
    }

    static void CrearJumper(ProtoboardSimulator sim, string nombre, ElectricalNode a, ElectricalNode b)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(sim.transform, false);
        var j = go.AddComponent<Jumper>();
        j.resistance = 0.01f;
        j.nodeA = a;
        j.nodeB = b;
        Debug.Log($"[Reto2VictoriaDiag] Jumper simulado '{nombre}': {a.name} ↔ {b.name}");
    }

    static void ReportarLEDs(ProtoboardSimulator sim, string etiqueta)
    {
        Debug.Log($"[Reto2VictoriaDiag] --- LEDs {etiqueta} ---");
        foreach (var l in Object.FindObjectsByType<LED>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l == null) continue;
            float va = l.nodeA != null ? l.nodeA.voltage : 0f;
            float vb = l.nodeB != null ? l.nodeB.voltage : 0f;
            Debug.Log($"[Reto2VictoriaDiag]   '{l.name}' activeSelf={l.gameObject.activeSelf} " +
                      $"nodeA={(l.nodeA != null ? l.nodeA.name : "null")} nodeB={(l.nodeB != null ? l.nodeB.name : "null")} " +
                      $"isOn={l.isOn} state={l.state} I={l.current * 1000f:0.##}mA Va={va:0.##} Vb={vb:0.##} " +
                      $"R={l.resistance:0} invertido={l.polarityInverted}");
        }
    }

    static void Finish(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto2VictoriaDiag] No encontré el método privado '{method}' en {t.Name}."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivateField(System.Type t, object instance, string field)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError($"[Reto2VictoriaDiag] No encontré el campo privado '{field}' en {t.Name}."); return null; }
        return f.GetValue(instance);
    }

    static void SetPrivateField(System.Type t, object instance, string field, object value)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        f?.SetValue(instance, value);
    }
}
