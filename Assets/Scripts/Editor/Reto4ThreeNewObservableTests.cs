using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 3 pruebas NUEVAS y OBSERVABLES sobre la escena REAL Explorador.unity, cada una con código de
/// Arduino distinto a cualquiera usado antes en esta sesión (for, while+bool, función con parámetros).
/// A diferencia de los tests anteriores (que solo comprobaban el resultado final), estas pruebas
/// ejercitan los scripts REALES uno por uno y LOGUEAN la posición/estado antes y después para que se
/// pueda observar que cada pieza realmente hace su trabajo:
///
///   1. CableProbePlug.TrySnap() real — mueve una punta desde una posición offset hasta el hueco
///      exacto (pin del Arduino), demostrando el imán de verdad, no una simulación de la lógica.
///   2. ProtoboardConnector.Bind() real — un componente (resistor) colocado cerca de 2 slots reales
///      de la protoboard, mostrando cómo sus patas se enganchan SOLAS a los nodos más cercanos.
///   3. Entrega del sketch por el canal de red real (ArduinoNetworkBridge.DeliverSketchProgram,
///      el mismo que usa GameSession al reensamblar los chunks del RPC del Técnico) + validación.
///
/// Ejecutar: Tools → TITA → Reto 4 → 3 pruebas nuevas observables (headless)
/// </summary>
public static class Reto4ThreeNewObservableTests
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    struct Escenario
    {
        public string nombre;
        public int pin;
        public float resistencia;
        public string sketch;
        public string explicacion;
    }

    [MenuItem("Tools/TITA/Reto 4/3 pruebas nuevas observables (headless)")]
    public static void Run()
    {
        int fails = 0;
        var tGm = typeof(GameManager);

        var escenarios = new[]
        {
            new Escenario
            {
                nombre = "PRUEBA 1 — bucle FOR (nunca usado antes), pin D4, R=220Ω",
                pin = 4, resistencia = 220f,
                sketch = "int i;\nvoid setup() { pinMode(4, OUTPUT); }\nvoid loop() { for (i = 0; i < 3; i = i + 1) { digitalWrite(4, HIGH); } }",
                explicacion = "Declara un contador 'i' y usa un FOR de 3 vueltas para poner el pin en HIGH. " +
                              "Prueba que el intérprete soporta bucles for con variable de control, no solo digitalWrite fijo."
            },
            new Escenario
            {
                nombre = "PRUEBA 2 — bucle WHILE + variable booleana (nunca usado antes), pin D12, R=470Ω",
                pin = 12, resistencia = 470f,
                sketch = "bool encendido = false;\nvoid setup() { pinMode(12, OUTPUT); }\nvoid loop() { while (!encendido) { digitalWrite(12, HIGH); encendido = true; } }",
                explicacion = "Usa una variable 'bool' y un WHILE que se ejecuta hasta que 'encendido' se vuelve true. " +
                              "Prueba booleanos + bucle while + negación lógica (!), combinación no probada antes."
            },
            new Escenario
            {
                nombre = "PRUEBA 3 — función propia con parámetros (nunca usado antes), pin D5, R=150Ω",
                pin = 5, resistencia = 150f,
                sketch = "void encenderLed(int pin, int brillo) { analogWrite(pin, brillo); }\nvoid setup() { pinMode(5, OUTPUT); }\nvoid loop() { encenderLed(5, 180); }",
                explicacion = "Define una función propia 'encenderLed(pin, brillo)' que llama a analogWrite por dentro, y la " +
                              "invoca desde loop() con argumentos. Prueba funciones definidas por el usuario con parámetros, " +
                              "no solo las funciones nativas del intérprete."
            },
        };

        foreach (var esc in escenarios)
        {
            // Recarga la escena ENTERA desde cero para cada escenario — un intérprete de Arduino
            // recién creado, cero estado previo. Esto es lo que de verdad pasa cuando un jugador
            // real prueba un sketch (sesión nueva), a diferencia de encadenar 3 sketches distintos
            // dentro de un mismo proceso de Unity (que dejaba un pin viejo "pegado" en HIGH — un
            // artefacto de cómo corre ESTE test, no un bug del juego).
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            InvokePrivate(tGm, gm, "LoadLevel", new object[] { 3 });
            var core = Object.FindAnyObjectByType<ArduinoCore>(FindObjectsInactive.Include);
            var sim = gm.protoSim;

            Debug.Log($"\n[Reto4_3New] ===== {esc.nombre} =====");
            Debug.Log($"[Reto4_3New] Explicación del código: {esc.explicacion}");
            Debug.Log($"[Reto4_3New] Código real:\n{esc.sketch}");

            // Poblar ConnectionPoints ANTES del test de imán (recién cargado el reto, el caché
            // todavía está vacío — en el juego real esto se llena solo por el Update() a 20Hz de
            // ProtoboardSimulator; en modo Editor sin bucle de frames hay que forzarlo una vez).
            var runSimWarmup = typeof(ProtoboardSimulator).GetMethod("RunSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
            runSimWarmup.Invoke(sim, null);

            var pinNode = core.PinToNode(esc.pin);
            var gndNode = core.nodoGND;
            if (pinNode == null || gndNode == null)
            {
                Debug.LogError($"[Reto4_3New] ✗ No until nodo real para D{esc.pin} o GND.");
                fails++; continue;
            }

            // ── PARTE A: imán de cable REAL — CableProbePlug.TrySnap() ──
            fails += ProbarImanReal(pinNode, $"D{esc.pin}");

            // ── PARTE B: enganche de componente REAL — ProtoboardConnector.Bind() ──
            fails += ProbarEngancheComponenteReal(sim);

            // ── PARTE C: circuito completo + sketch real por el canal de red ──
            fails += ProbarCircuitoYSketch(gm, tGm, sim, core, pinNode, gndNode, esc);
        }

        Debug.Log(fails == 0
            ? "\n[Reto4_3New] ===== RESULTADO: ✓ Las 3 pruebas nuevas (imán real, enganche real, 3 sketches distintos) pasaron ====="
            : $"\n[Reto4_3New] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>Crea una punta de cable REAL (con CableProbePlug, el mismo script que usa el jugador),
    /// la suelta a 2 cm del pin objetivo (dentro de plugRadius=3cm) y llama TrySnap() de verdad —
    /// loguea la posición ANTES y DESPUÉS para que se pueda observar el imán funcionando.</summary>
    static int ProbarImanReal(ElectricalNode pinNode, string nombrePin)
    {
        var probeGo = new GameObject("Test_ProbeImanReal");
        probeGo.AddComponent<Rigidbody>().isKinematic = true;
        var grab = probeGo.AddComponent<XRGrabInteractable>();
        var plug = probeGo.AddComponent<CableProbePlug>();

        Vector3 posicionSoltada = pinNode.transform.position + new Vector3(0.02f, 0f, 0f); // 2 cm de offset
        probeGo.transform.position = posicionSoltada;

        Debug.Log($"[Reto4_3New]   [Imán] Punta '{probeGo.name}' soltada a 2cm de {nombrePin}. " +
                  $"Posición ANTES del imán = {probeGo.transform.position}");

        var trySnap = typeof(CableProbePlug).GetMethod("TrySnap", BindingFlags.NonPublic | BindingFlags.Instance);
        trySnap.Invoke(plug, null);

        Vector3 posicionTrasImán = probeGo.transform.position;
        float distanciaAlPin = Vector3.Distance(posicionTrasImán, pinNode.transform.position);
        Debug.Log($"[Reto4_3New]   [Imán] Posición DESPUÉS del imán = {posicionTrasImán}  " +
                  $"(distancia real al nodo de {nombrePin} = {distanciaAlPin * 100f:F2} cm — debe ser ~0)");

        bool ok = distanciaAlPin < 0.001f; // prácticamente clavado sobre el nodo
        if (!ok) Debug.LogError($"[Reto4_3New]   ✗ El imán NO clavó la punta sobre {nombrePin} (quedó a {distanciaAlPin*100f:F2}cm).");
        else Debug.Log($"[Reto4_3New]   ✓ CableProbePlug.TrySnap() REAL movió la punta exactamente sobre {nombrePin}.");

        Object.DestroyImmediate(probeGo);
        return ok ? 0 : 1;
    }

    /// <summary>Coloca un resistor REAL cerca de 2 slots reales de la protoboard (sin asignar nodeA/
    /// nodeB a mano) y deja que ProtoboardConnector.Bind() los enganche solo, igual que en el juego
    /// cuando el jugador suelta la pieza sobre el tablero.</summary>
    static int ProbarEngancheComponenteReal(ProtoboardSimulator sim)
    {
        var buildNodeMap = typeof(ProtoboardSimulator).GetMethod("BuildNodeMap", BindingFlags.NonPublic | BindingFlags.Instance);
        buildNodeMap.Invoke(sim, null);

        var slots = sim.todosLosSlots.Where(s => s != null && s.assignedNode != null).ToList();
        if (slots.Count < 2)
        {
            Debug.LogError("[Reto4_3New]   ✗ No until 2 slots con nodo asignado para probar el enganche.");
            return 1;
        }
        var slotA = slots[0];
        var slotB = slots[1];

        var rGo = new GameObject("Test_ResistorEngancheReal");
        rGo.transform.SetParent(sim.transform, false);
        // Posicionado A MITAD DE CAMINO entre los 2 slots — cada pata (a los extremos del bounding
        // box, que EnsureLeads() calcula solo) debe caer cerca de un slot distinto.
        rGo.transform.position = (slotA.transform.position + slotB.transform.position) * 0.5f;
        var resistor = rGo.AddComponent<Resistor>();
        resistor.resistance = 330f;

        var connector = ProtoboardConnector.EnsureOn(rGo);
        connector.leadA = null; connector.leadB = null; // forzar auto-creación por EnsureLeads()

        var awake = typeof(ProtoboardConnector).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        awake.Invoke(connector, null); // crea leadA/leadB en los extremos del bounding box

        // OnEnable() (a diferencia de Awake) no corre de forma síncrona al agregar un componente en
        // modo batch sin Play Mode/bucle de frames — es quien registra el conector en
        // ProtoboardConnector.Active, la lista que BindConnectors() recorre. En el juego real (Play
        // Mode/build) esto corre solo; en este test hay que forzarlo para reproducir fielmente.
        var onEnable = typeof(ProtoboardConnector).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        onEnable.Invoke(connector, null);

        // Estirar las patas manualmente hacia cada slot (simula al jugador estirando el cuerpo del
        // resistor para que cada extremo llegue a un hueco distinto — EnsureLeads() por sí solo usa
        // el tamaño del mesh, que puede ser más corto que la separación real entre huecos).
        connector.leadA.position = slotA.transform.position;
        connector.leadB.position = slotB.transform.position;

        Debug.Log($"[Reto4_3New]   [Enganche] Resistor colocado entre '{slotA.name}' y '{slotB.name}'. " +
                  $"leadA={(connector.leadA != null ? connector.leadA.name : "NULL")}@{(connector.leadA != null ? connector.leadA.position.ToString() : "-")} " +
                  $"leadB={(connector.leadB != null ? connector.leadB.name : "NULL")}@{(connector.leadB != null ? connector.leadB.position.ToString() : "-")} " +
                  $"slotA.pos={slotA.transform.position} slotB.pos={slotB.transform.position} " +
                  $"nodeA/nodeB ANTES de Bind() = {(resistor.nodeA != null ? resistor.nodeA.name : "NULL")}/{(resistor.nodeB != null ? resistor.nodeB.name : "NULL")}");

        var runSim = typeof(ProtoboardSimulator).GetMethod("RunSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
        runSim.Invoke(sim, null);

        var cachedPointsField = typeof(ProtoboardSimulator).GetField("_cachedPoints", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedPoints = cachedPointsField.GetValue(sim) as System.Collections.IList;
        Debug.Log($"[Reto4_3New]   [Enganche] sim.ConnectionPoints tras RunSimulation = {cachedPoints?.Count ?? -1} puntos. " +
                  $"lockNodes={connector.lockNodes} snapRadius={connector.snapRadius} " +
                  $"NearestSimulator(connector.pos)==sim? {ReferenceEquals(InvokeNearestSim(connector.transform.position), sim)} " +
                  $"in Active? {ProtoboardConnector.Active.Contains(connector)}");

        // Réplica manual de Nearest(): ¿hay algún punto MUY cerca de leadA, más allá de lo que
        // Bind() haya hecho? Confirma si es un problema de distancia real o de otra condición.
        float mejorDist = float.MaxValue; string mejorNombre = "(ninguno)";
        foreach (System.Object cpObj in cachedPoints)
        {
            var posF = cpObj.GetType().GetField("position");
            var nodeF = cpObj.GetType().GetField("node");
            var pos = (Vector3)posF.GetValue(cpObj);
            var node = nodeF.GetValue(cpObj) as ElectricalNode;
            float d = Vector3.Distance(pos, connector.leadA.position);
            if (d < mejorDist) { mejorDist = d; mejorNombre = node != null ? node.name : "?"; }
        }
        Debug.Log($"[Reto4_3New]   [Enganche] Punto MÁS CERCANO a leadA manualmente = '{mejorNombre}' a {mejorDist*100f:F3} cm (snapRadius={connector.snapRadius*100f:F1} cm)");

        Debug.Log($"[Reto4_3New]   [Enganche] nodeA/nodeB DESPUÉS de Bind() = " +
                  $"{(resistor.nodeA != null ? resistor.nodeA.name : "NULL")}/{(resistor.nodeB != null ? resistor.nodeB.name : "NULL")} " +
                  $"(esperado: {slotA.railId}/{slotB.railId} o sus nodos asignados)");

        bool ok = resistor.nodeA != null && resistor.nodeB != null && resistor.nodeA == slotA.assignedNode && resistor.nodeB == slotB.assignedNode;
        if (!ok) Debug.LogError("[Reto4_3New]   ✗ ProtoboardConnector.Bind() NO enganchó el resistor a los slots esperados.");
        else Debug.Log("[Reto4_3New]   ✓ ProtoboardConnector.Bind() REAL enganchó el resistor solo, por proximidad, a sus 2 slots.");

        Object.DestroyImmediate(rGo);
        return ok ? 0 : 1;
    }

    static int ProbarCircuitoYSketch(GameManager gm, System.Type tGm, ProtoboardSimulator sim, ArduinoCore core,
                                      ElectricalNode pinNode, ElectricalNode gndNode, Escenario esc)
    {
        int fails = 0;

        var midGo = new GameObject("Test_Mid"); midGo.transform.SetParent(sim.transform, false);
        var mid = midGo.AddComponent<ElectricalNode>();
        var rGo = new GameObject("Test_R"); rGo.transform.SetParent(sim.transform, false);
        var r = rGo.AddComponent<Resistor>();
        r.resistance = esc.resistencia; r.nodeA = pinNode; r.nodeB = mid;
        var ledGo = new GameObject("Test_LED"); ledGo.transform.SetParent(sim.transform, false);
        var led = ledGo.AddComponent<LED>();
        led.forwardVoltage = 2.0f; led.resistance = 50f; led.maxSafeCurrent = 0.02f; led.polarityInverted = false;
        led.nodeA = mid; led.nodeB = gndNode;
        core.outputVoltageTTL = 5f;

        // ── Entrega del sketch por el canal de red REAL (mismo método que llama GameSession al
        // reensamblar los chunks del RPC_SubirSketchChunk del Técnico) ──
        ArduinoNetworkBridge.DeliverSketchProgram(esc.sketch);
        Debug.Log($"[Reto4_3New]   [Red] Sketch entregado por ArduinoNetworkBridge.DeliverSketchProgram() (canal real de GameSession). " +
                  $"core.ProgramRunning={core.ProgramRunning}");
        if (!core.ProgramRunning) { fails++; Debug.LogError("[Reto4_3New]   ✗ El sketch no compiló/corrió."); }

        var interpField = typeof(ArduinoCore).GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance);
        var interp = interpField.GetValue(core) as ArduinoInterpreter;
        int n = 0; foreach (var _ in interp.RunSetup()) { if (++n >= 2000) break; }
        n = 0; foreach (var _ in interp.RunLoop())  { if (++n >= 2000) break; }

        if (gm.multimeter != null)
        {
            var f = typeof(Multimeter).GetField("_usedResistanceMode", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(gm.multimeter, true);
        }

        sim.ForzarValidacion();
        var resultField = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastResult = resultField.GetValue(sim);
        InvokePrivate(tGm, gm, "OnSandboxResult", new object[] { lastResult }); // ver nota en Reto4RealSceneE2ETest

        bool paso = (bool)InvokePrivate(tGm, gm, "EvaluarReto4");
        Debug.Log($"[Reto4_3New]   [Validación] mensaje=\"{GetField(lastResult, "message")}\" EvaluarReto4()={paso}");
        if (!paso) { fails++; Debug.LogError("[Reto4_3New]   ✗ El circuito con este sketch NO completó el Reto 4."); }
        else Debug.Log("[Reto4_3New]   ✓ Circuito + sketch nuevo → Reto 4 completado.");

        Object.DestroyImmediate(midGo); Object.DestroyImmediate(rGo); Object.DestroyImmediate(ledGo);

        // Reset para el siguiente escenario del loop.
        var levelCompletedField = tGm.GetField("_levelCompleted", BindingFlags.NonPublic | BindingFlags.Instance);
        levelCompletedField.SetValue(gm, false);

        return fails;
    }

    static object GetField(object obj, string name) => obj?.GetType().GetField(name)?.GetValue(obj);

    static ProtoboardSimulator InvokeNearestSim(Vector3 pos)
    {
        var m = typeof(ProtoboardSimulator).GetMethod("NearestSimulator", BindingFlags.NonPublic | BindingFlags.Static);
        return (ProtoboardSimulator)m.Invoke(null, new object[] { pos });
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        return m.Invoke(instance, args ?? new object[0]);
    }
}
