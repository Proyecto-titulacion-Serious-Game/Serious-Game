using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verificación HEADLESS de la ENTREGA de componentes (Técnico → Explorador) que
/// <see cref="Reto4EndToEndTest"/> no cubre: esa prueba arma el circuito ya colocado a mano.
/// Esta entra un paso antes — en <see cref="ExplorerComponentReceiver"/>, el mismo punto donde
/// aterriza <c>GameSession.OnComponenteRecibido</c> tras el RPC — y usa los prefabs REALES
/// configurados en <c>Assets/Prefabs/ComponentReceiver.prefab</c> (no copias ni mocks).
///
/// Cubre: selección de prefab por variante (color de LED / orientación de resistor), que el
/// signo de <c>valor</c> fija la polaridad del LED, acumulación en Reto 4 vs reemplazo en
/// Retos 1-3, el enganche físico real (<see cref="ProtoboardConnector.Bind"/> por proximidad,
/// no hardcodeado), y una cadena completa: entrega → colocación → código C++ real → EvaluarReto4().
///
/// Ejecutar:
///   Editor:     Tools → TITA → Reto 4 → Test de entrega de componentes (headless)
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4DeliveryPipelineTest.Run -logFile -
/// </summary>
public static class Reto4DeliveryPipelineTest
{
    const string PATH_RESISTOR   = "Assets/Prefabs/Delivered/Delivered_Resistor.prefab";
    const string PATH_RESISTOR_V = "Assets/Prefabs/Delivered/Delivered_Resistor_Vertical.prefab";
    const string PATH_LED_GREEN  = "Assets/Prefabs/Delivered/Delivered_LED_Green.prefab";
    const string PATH_LED_RED    = "Assets/Prefabs/Delivered/Delivered_LED_Red.prefab";
    const string PATH_LED_YELLOW = "Assets/Prefabs/Delivered/Delivered_LED_Yellow.prefab";

    [MenuItem("Tools/TITA/Reto 4/Test de entrega de componentes (headless)")]
    public static void Run()
    {
        int fails = 0;
        Debug.Log("===== RETO 4 — TEST DE ENTREGA DE COMPONENTES (pipeline real Técnico → Explorador) =====");

        fails += TestVariante("LED rojo, valor positivo (polaridad normal)", ComponentType.LED, 0f, ComponentVariant.LedRed,
            obj =>
            {
                var led = obj.GetComponentInChildren<LED>(true);
                bool prefabOk = obj.name.StartsWith("Delivered_LED_Red");
                bool valOk = led != null && led.polarityInverted == false;
                return (prefabOk, valOk, $"prefab={obj.name} polarityInverted={(led != null ? led.polarityInverted.ToString() : "null")}");
            });

        fails += TestVariante("LED verde con valor NEGATIVO (el signo debe invertir la polaridad)", ComponentType.LED, -1f, ComponentVariant.LedGreen,
            obj =>
            {
                var led = obj.GetComponentInChildren<LED>(true);
                bool prefabOk = obj.name.StartsWith("Delivered_LED_Green");
                bool valOk = led != null && led.polarityInverted == true;
                return (prefabOk, valOk, $"prefab={obj.name} polarityInverted={(led != null ? led.polarityInverted.ToString() : "null")}");
            });

        fails += TestVariante("Resistor vertical, 470 Ω", ComponentType.Resistor, 470f, ComponentVariant.ResistorVertical,
            obj =>
            {
                var r = obj.GetComponentInChildren<Resistor>(true);
                bool prefabOk = obj.name.StartsWith("Delivered_Resistor_Vertical");
                bool valOk = r != null && Mathf.Approximately(r.resistance, 470f);
                return (prefabOk, valOk, $"prefab={obj.name} resistance={(r != null ? r.resistance.ToString("F0") : "null")}Ω");
            });

        fails += TestAccumulationVsReplacement();
        fails += TestFullChain();

        Debug.Log(fails == 0
            ? "===== RESULTADO: ✓ Entrega (variante+valor), acumulación y conexión física real funcionan de punta a punta ====="
            : $"===== RESULTADO: ✗ {fails} prueba(s) fallaron =====");

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    // ── Escenarios ──────────────────────────────────────────────────────────

    static int TestVariante(string name, ComponentType tipo, float valor, ComponentVariant variante,
        System.Func<GameObject, (bool prefabOk, bool valOk, string detail)> check)
    {
        GameObject rootGO = null;
        try
        {
            var receiver = BuildReceiver(LevelType.OhmLaw, out rootGO);
            var spawned = InvokeSpawn(receiver, tipo, valor, variante);
            if (spawned == null) { Debug.LogError($"[{name}] FALLO: SpawnComponente no instanció nada."); return 1; }

            var (prefabOk, valOk, detail) = check(spawned);
            bool ok = prefabOk && valOk;
            string line = $"[{name}] prefabOk={prefabOk} valorOk={valOk} | {detail}";
            if (ok) Debug.Log(line); else Debug.LogError(line + "  <-- FALLO");
            return ok ? 0 : 1;
        }
        finally { if (rootGO != null) Object.DestroyImmediate(rootGO); }
    }

    static int TestAccumulationVsReplacement()
    {
        int fails = 0;
        GameObject rootGO = null;
        try
        {
            var receiver = BuildReceiver(LevelType.Arduino, out rootGO);
            InvokeSpawn(receiver, ComponentType.Resistor, 220f, ComponentVariant.Default);
            InvokeSpawn(receiver, ComponentType.Resistor, 680f, ComponentVariant.Default);
            int count = ((List<GameObject>)GetPrivate(receiver, "_componentesRecibidos")).Count(g => g != null);
            bool ok = count == 2;
            string line = $"[Acumulación en Reto 4: 2 resistores distintos] piezas en mesa={count} (esperado 2 — no se reemplazan)";
            if (ok) Debug.Log(line); else Debug.LogError(line + "  <-- FALLO");
            fails += ok ? 0 : 1;
        }
        finally { if (rootGO != null) Object.DestroyImmediate(rootGO); }

        rootGO = null;
        try
        {
            var receiver = BuildReceiver(LevelType.OhmLaw, out rootGO);
            InvokeSpawn(receiver, ComponentType.Resistor, 220f, ComponentVariant.Default);
            InvokeSpawn(receiver, ComponentType.Resistor, 680f, ComponentVariant.Default);
            int count = ((List<GameObject>)GetPrivate(receiver, "_componentesRecibidos")).Count(g => g != null);
            bool ok = count == 1;
            string line = $"[Reemplazo en Retos 1-3: 2 resistores distintos] piezas en mesa={count} (esperado 1 — el 2º reemplaza al 1º)";
            if (ok) Debug.Log(line); else Debug.LogError(line + "  <-- FALLO");
            fails += ok ? 0 : 1;
        }
        finally { if (rootGO != null) Object.DestroyImmediate(rootGO); }

        return fails;
    }

    /// <summary>
    /// La prueba completa: el Técnico entrega un LED y una resistencia (prefabs y valores REALES,
    /// vía la misma función que procesa el RPC), el Explorador los "coloca" — enganche físico real
    /// por proximidad (<see cref="ProtoboardConnector.Bind"/>, con un nodo señuelo lejano para
    /// probar que de verdad elige el más cercano) — y luego sube un sketch C++ real que
    /// <see cref="GameManager.EvaluarReto4"/> debe aceptar.
    /// </summary>
    static int TestFullChain()
    {
        var cleanup = new List<GameObject>();
        GameObject rootGO = null, protoRoot = null;
        ProtoboardConnector resConn = null, ledConn = null;
        try
        {
            var receiver = BuildReceiver(LevelType.Arduino, out rootGO);

            var ledGO = InvokeSpawn(receiver, ComponentType.LED, 0f, ComponentVariant.LedGreen);
            var led = ledGO != null ? ledGO.GetComponentInChildren<LED>(true) : null;
            if (led == null) { Debug.LogError("[Cadena completa] FALLO: no se pudo entregar el LED."); return 1; }

            // Resistor calculado a partir de las specs REALES del LED entregado (no un valor fijo a
            // ciegas) — apunta al punto medio de su rango seguro de corriente.
            float targetA = (led.minOperatingCurrent + led.maxSafeCurrent) / 2f;
            float rOhms = Mathf.Round((5f - led.forwardVoltage) / targetA / 10f) * 10f;
            Debug.Log($"[Cadena completa] LED real entregado: Vf={led.forwardVoltage:F2}V, rango seguro=" +
                      $"[{led.minOperatingCurrent * 1000:F1},{led.maxSafeCurrent * 1000:F1}] mA → resistor calculado={rOhms:F0}Ω");

            var resGO = InvokeSpawn(receiver, ComponentType.Resistor, rOhms, ComponentVariant.Default);
            var res = resGO != null ? resGO.GetComponentInChildren<Resistor>(true) : null;
            if (res == null) { Debug.LogError("[Cadena completa] FALLO: no se pudo entregar el resistor."); return 1; }

            resConn = res.GetComponent<ProtoboardConnector>();
            ledConn = led.GetComponent<ProtoboardConnector>();
            if (resConn == null || ledConn == null)
            { Debug.LogError("[Cadena completa] FALLO: ProtoboardConnector.EnsureOn no se aplicó a las piezas entregadas."); return 1; }

            // Unity NO ejecuta Awake()/OnEnable() en objetos instanciados desde un prefab cargado
            // por AssetDatabase.LoadAssetAtPath fuera de Play Mode (a diferencia de un
            // `new GameObject()` + AddComponent hecho directamente en el script, que sí las dispara
            // de inmediato — por eso Reto4VoltageTest/Reto4EndToEndTest evitan el problema
            // construyendo todo a mano). Replicamos aquí lo que esas dos hacen en producción:
            // Awake() asigna _comp, y OnEnable() registra el conector en la lista estática Active
            // (que es como ProtoboardSimulator.AllSandboxComponents() y BindConnectors() encuentran
            // y enganchan las piezas sueltas en cada simulación real).
            SetPrivate(resConn, "_comp", res);
            SetPrivate(ledConn, "_comp", led);
            if (!ProtoboardConnector.Active.Contains(resConn)) ProtoboardConnector.Active.Add(resConn);
            if (!ProtoboardConnector.Active.Contains(ledConn)) ProtoboardConnector.Active.Add(ledConn);

            // Protoboard real: Arduino (pin D9 + GND) y UN slot físico para el nodo intermedio
            // "Mid" — el mismo tipo de objeto (ProtoboardSlot) que arma el generador de cuadrícula
            // en la escena real, no un ElectricalNode inventado a mano.
            protoRoot = new GameObject("ProtoTest_Cadena");
            protoRoot.SetActive(false);
            var protoSim = protoRoot.AddComponent<ProtoboardSimulator>();

            var arduinoGO = new GameObject("ArduinoTest");
            arduinoGO.transform.SetParent(protoRoot.transform, false);
            var core = arduinoGO.AddComponent<ArduinoCore>();
            var pinPos = new Vector3(0f, 0f, 0f);
            var gndPos = new Vector3(0.04f, 0f, 0f);
            var midPos = new Vector3(0.02f, 0f, 0f);
            var decoyPos = new Vector3(5f, 5f, 5f);
            var pinNode = NewNode("PinD9Node", pinPos); cleanup.Add(pinNode.gameObject);
            var gndNode = NewNode("GNDNode",   gndPos); cleanup.Add(gndNode.gameObject);
            core.nodoGND = gndNode;
            core.outputVoltageTTL = 5f;
            core.RegisterPinNode(9, pinNode);

            var midSlotGO = new GameObject("Slot_Mid");
            midSlotGO.transform.position = midPos;
            var midSlot = midSlotGO.AddComponent<ProtoboardSlot>();
            midSlot.railId = "COL_MID";
            protoSim.todosLosSlots.Add(midSlot);
            cleanup.Add(midSlotGO);

            // Slot señuelo lejos de todo: si BindConnectors() enganchara "lo primero que encuentra"
            // en vez de lo más cercano, esto lo delataría.
            var decoySlotGO = new GameObject("Slot_Decoy");
            decoySlotGO.transform.position = decoyPos;
            var decoySlot = decoySlotGO.AddComponent<ProtoboardSlot>();
            decoySlot.railId = "COL_DECOY";
            protoSim.todosLosSlots.Add(decoySlot);
            cleanup.Add(decoySlotGO);

            var leadPin  = NewLead("LeadPin",  pinPos); cleanup.Add(leadPin.gameObject);
            var leadMidA = NewLead("LeadMidA", midPos); cleanup.Add(leadMidA.gameObject);
            var leadMidB = NewLead("LeadMidB", midPos); cleanup.Add(leadMidB.gameObject);
            var leadGnd  = NewLead("LeadGnd",  gndPos); cleanup.Add(leadGnd.gameObject);
            resConn.SetLeads(leadPin, leadMidA);   // resistor: pin → mid
            ledConn.SetLeads(leadMidB, leadGnd);   // LED: mid (ánodo) → GND (cátodo)

            core.LoadSketchProgram("void setup(){ pinMode(9, OUTPUT); } void loop(){ digitalWrite(9, HIGH); }");
            if (!core.ProgramRunning) { Debug.LogError("[Cadena completa] FALLO: el sketch no compiló."); return 1; }
            var interp = GetInterp(core);
            Drain(interp.RunSetup());
            Drain(interp.RunLoop());

            // ÚNICA llamada de aquí en adelante: la misma que dispara el botón físico. Internamente
            // hace BuildNodeMap() + BindConnectors() — el enganche físico real por proximidad de
            // TODOS los ProtoboardConnector.Active contra los slots/pines reales — y luego valida.
            protoSim.ForzarValidacion();

            bool bindOk = res.nodeA == pinNode && res.nodeA != null && res.nodeB == midSlot.assignedNode &&
                          led.nodeA == midSlot.assignedNode && led.nodeB == gndNode;
            Debug.Log($"[Cadena completa] BindConnectors() real (con slot señuelo a 7m incluido en la grilla): " +
                      $"resistor {Nm(res.nodeA)}→{Nm(res.nodeB)} | LED {Nm(led.nodeA)}→{Nm(led.nodeB)} (bindOk={bindOk})");
            if (!bindOk) { Debug.LogError("[Cadena completa] FALLO: el enganche físico no conectó los nodos esperados."); return 1; }

            var result = GetLastResult(protoSim);
            Debug.Log($"[Cadena completa] EvaluarReto4 → success={result.success} \"{result.message}\" I≈{result.currentMa:F2} mA");
            if (!result.success) { Debug.LogError("[Cadena completa] FALLO: el reto no se completó pese a entrega+colocación+código válidos."); return 1; }

            Debug.Log("[Cadena completa] ✓ Técnico entrega LED+resistor (prefabs/valores reales) → Explorador los coloca " +
                      "(BindConnectors físico real contra slots reales) → sube código C++ real → Reto 4 se completa.");
            return 0;
        }
        finally
        {
            // OnDisable() nunca corrió (mismo motivo que Awake/OnEnable), así que nadie más va a
            // sacar estos dos de la lista estática — hay que hacerlo a mano o quedan colgando.
            if (resConn != null) ProtoboardConnector.Active.Remove(resConn);
            if (ledConn != null) ProtoboardConnector.Active.Remove(ledConn);
            if (rootGO != null) Object.DestroyImmediate(rootGO);
            if (protoRoot != null) Object.DestroyImmediate(protoRoot);
            foreach (var go in cleanup) if (go != null) Object.DestroyImmediate(go);
        }
    }

    static string Nm(ElectricalNode n) => n != null ? n.name : "∅";

    // ── Infraestructura ──────────────────────────────────────────────────────

    static ExplorerComponentReceiver BuildReceiver(LevelType level, out GameObject rootGO)
    {
        rootGO = new GameObject("Receiver_Test");
        rootGO.SetActive(false);   // configurar todo antes de que Awake/OnEnable corran

        var slotGO = new GameObject("Slot");
        slotGO.transform.SetParent(rootGO.transform, false);

        var receiver = rootGO.AddComponent<ExplorerComponentReceiver>();
        receiver.puntoDeEntrega          = slotGO.transform;
        receiver.resistorPrefab          = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_RESISTOR);
        receiver.resistorVerticalPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_RESISTOR_V);
        receiver.ledPrefab               = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_LED_GREEN);
        receiver.ledGreenPrefab          = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_LED_GREEN);
        receiver.ledRedPrefab            = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_LED_RED);
        receiver.ledYellowPrefab         = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_LED_YELLOW);

        var gmGO = new GameObject("GM_Test");
        gmGO.transform.SetParent(rootGO.transform, false);
        gmGO.SetActive(false);
        var gm = gmGO.AddComponent<GameManager>();
        SetPrivate(gm, "_currentLevel", level);
        SetPrivate(receiver, "_gm", gm);

        rootGO.SetActive(true);
        return receiver;
    }

    static GameObject InvokeSpawn(ExplorerComponentReceiver receiver, ComponentType tipo, float valor, ComponentVariant variante)
    {
        var m = typeof(ExplorerComponentReceiver).GetMethod("SpawnComponente", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(receiver, new object[] { tipo, valor, null, variante });
        var dict = (Dictionary<ComponentType, GameObject>)GetPrivate(receiver, "_ultimoPorTipo");
        return dict.TryGetValue(tipo, out var go) ? go : null;
    }

    static void Drain(IEnumerable<ArduinoInterpreter.Signal> seq)
    {
        int n = 0;
        foreach (var _ in seq) { if (++n >= 2000) break; }
    }

    static ArduinoInterpreter GetInterp(ArduinoCore core)
    {
        var f = typeof(ArduinoCore).GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance);
        return (ArduinoInterpreter)f.GetValue(core);
    }

    static SandboxValidationResult GetLastResult(ProtoboardSimulator sim)
    {
        var f = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        return (SandboxValidationResult)f.GetValue(sim);
    }

    static void SetPrivate(object obj, string field, object value)
        => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);

    static object GetPrivate(object obj, string field)
        => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

    static ElectricalNode NewNode(string n, Vector3 pos)
    {
        var go = new GameObject(n);
        go.transform.position = pos;
        return go.AddComponent<ElectricalNode>();
    }

    static Transform NewLead(string n, Vector3 pos)
    {
        var go = new GameObject(n);
        go.transform.position = pos;
        return go.transform;
    }
}
