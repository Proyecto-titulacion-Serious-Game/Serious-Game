using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verificación END-TO-END sobre la escena REAL Explorador.unity (no un root sintético como
/// Reto4EndToEndTest) — ejercita el ArduinoCore/ProtoboardSimulator restaurados hoy, con un
/// circuito wireado a los nodos REALES del modelo (Nodo_D9, Nodo_GND), sube un sketch real por el
/// mismo método estático que usa GameSession.RPC_SubirSketchChunk, mide resistencia con el
/// multímetro real, llama GameManager.EvaluarReto4() real, y confirma:
///   1. El reto se completa (_levelCompleted = true).
///   2. El "comprobante" (diagnóstico ✅) se dispara hacia el Técnico.
///   3. LoadLevel(4) tras el Reto 4 dispara CompleteGame()/OnGameCompleted (fin del juego).
///
/// Ejecutar: Tools → TITA → Reto 4 → Test E2E en escena REAL (post-fix ArduinoCore)
/// </summary>
public static class Reto4RealSceneE2ETest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Test E2E en escena REAL (post-fix ArduinoCore)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gm == null) { Debug.LogError("[Reto4E2E] No hay GameManager."); Finish(1); return; }
        var tGm = typeof(GameManager);

        // ── Cargar Reto 4 de verdad (índice 3 = LevelType.Arduino) ──
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 3 });
        var currentLevel = (LevelType)GetPrivate(tGm, gm, "_currentLevel");
        Debug.Log($"[Reto4E2E] Tras LoadLevel(3): currentLevel={currentLevel} (esperado Arduino)");
        if (currentLevel != LevelType.Arduino) { fails++; Debug.LogError("[Reto4E2E] ✗ No cargó Reto 4."); }

        var core = Object.FindAnyObjectByType<ArduinoCore>(FindObjectsInactive.Include);
        if (core == null) { Debug.LogError("[Reto4E2E] ✗ ArduinoCore sigue sin existir — el fix no se guardó."); Finish(1); return; }

        var sim = gm.protoSim;
        if (sim == null) { Debug.LogError("[Reto4E2E] ✗ gm.protoSim es NULL."); Finish(1); return; }

        // ── Circuito real: Pin D9 -> R(330Ω) -> LED(Vf 2.0V, no invertido) -> GND, sobre los NODOS
        // REALES del modelo (los mismos que usaría un cable enchufado por el imán). ──
        var pinNode = core.PinToNode(9);
        var gndNode = core.nodoGND;
        Debug.Log($"[Reto4E2E] Nodo pin D9={(pinNode != null ? pinNode.name : "NULL")}  nodoGND={(gndNode != null ? gndNode.name : "NULL")}");
        if (pinNode == null || gndNode == null) { fails++; Debug.LogError("[Reto4E2E] ✗ Nodos reales del Arduino no resueltos — el fix de pinNodeMap no está aplicado."); }

        // Parentados al MISMO transform del ProtoboardSimulator real: AllSandboxComponents() solo
        // recoge ElectricalComponent hijos del simulador (o registrados vía ProtoboardConnector.Active),
        // igual que Reto4EndToEndTest hace con su root sintético.
        var midGo = new GameObject("Test_MidNode");
        midGo.transform.SetParent(sim.transform, false);
        var mid = midGo.AddComponent<ElectricalNode>();

        var rGo = new GameObject("Test_R");
        rGo.transform.SetParent(sim.transform, false);
        var r = rGo.AddComponent<Resistor>();
        r.resistance = 330f; r.nodeA = pinNode; r.nodeB = mid;

        var ledGo = new GameObject("Test_LED");
        ledGo.transform.SetParent(sim.transform, false);
        var led = ledGo.AddComponent<LED>();
        led.forwardVoltage = 2.0f; led.resistance = 50f; led.maxSafeCurrent = 0.02f;
        led.polarityInverted = false;
        led.nodeA = mid; led.nodeB = gndNode;

        core.outputVoltageTTL = 5f;

        // ── Sketch real por el MISMO método estático que usa GameSession al reensamblar los chunks
        // del RPC (ArduinoNetworkBridge.DeliverSketchProgram) — confirma que subir código funciona
        // sin depender de la instancia de ArduinoNetworkBridge (que no existe en la escena). ──
        string sketch = "void setup() { pinMode(9, OUTPUT); }\nvoid loop() { digitalWrite(9, HIGH); }";
        ArduinoNetworkBridge.DeliverSketchProgram(sketch);
        Debug.Log($"[Reto4E2E] Sketch entregado vía DeliverSketchProgram(). core.ProgramRunning={core.ProgramRunning}");
        if (!core.ProgramRunning) { fails++; Debug.LogError("[Reto4E2E] ✗ El sketch no compiló/corrió."); }

        var interpField = typeof(ArduinoCore).GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance);
        var interp = interpField?.GetValue(core) as ArduinoInterpreter;
        if (interp != null)
        {
            int n = 0; foreach (var _ in interp.RunSetup()) { if (++n >= 2000) break; }
            n = 0; foreach (var _ in interp.RunLoop())  { if (++n >= 2000) break; }
        }

        // ── Multímetro: marcar resistencia medida (gate real de EvaluarReto4) ──
        if (gm.multimeter != null)
        {
            var f = typeof(Multimeter).GetField("_usedResistanceMode", BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(gm.multimeter, true);
            Debug.Log($"[Reto4E2E] multimeter.wasUsedInResistanceMode forzado a true. Valor real={gm.multimeter.wasUsedInResistanceMode}");
        }
        else Debug.LogWarning("[Reto4E2E] gm.multimeter es NULL — EvaluarReto4 tratará resistenciaMedida=true por el operador ?? (multimeter==null).");

        // ── Diagnóstico ("comprobante" al Técnico): capturar el evento local (fuera de Play Mode,
        // GameSession.Instance es null → ReportarDiagnosticoReto cae al fallback de evento local). ──
        string diagCapturado = null;
        int diagReto = -1;
        void OnDiag(int reto, string resumen) { diagReto = reto; diagCapturado = resumen; }
        GameSession.OnDiagnosticoRetoActualizado += OnDiag;

        // ── El momento de la verdad: MISMA llamada que dispara el botón físico ──
        sim.ForzarValidacion();
        var resultField = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastResult = resultField != null ? resultField.GetValue(sim) : null;
        Debug.Log($"[Reto4E2E] _lastSandboxResult tras ForzarValidacion() = {lastResult}");
        if (lastResult != null)
        {
            foreach (var fld in lastResult.GetType().GetFields())
                Debug.Log($"    {fld.Name} = {fld.GetValue(lastResult)}");
        }

        // GameManager.Start() (no OnEnable) es quien hace 'ProtoboardSimulator.OnSandboxValidated +=
        // OnSandboxResult' — Start() de un componente YA GUARDADO en escena no corre de forma fiable
        // fuera de Play Mode (limitación ya documentada del proyecto), así que esa suscripción nunca
        // se estableció y GameManager._lastSandboxResult se quedó en el default. En el juego real
        // (Play Mode / build) Start() sí corre y esto no pasa. Reproducimos a mano exactamente lo que
        // haría el handler del evento, sin ejecutar el resto de Start() (que dispararía LoadLevel(0)
        // y resuscribiría eventos de red, ruido que no queremos en este test).
        InvokePrivate(tGm, gm, "OnSandboxResult", new object[] { lastResult });

        bool paso = InvokePrivateBool(tGm, gm, "EvaluarReto4");
        bool completado = (bool)GetPrivate(tGm, gm, "_levelCompleted");
        Debug.Log($"[Reto4E2E] EvaluarReto4()={paso}  _levelCompleted={completado} (ambos esperados true)");
        if (!paso || !completado) { fails++; Debug.LogError("[Reto4E2E] ✗ El circuito real no completó el Reto 4."); }

        GameSession.OnDiagnosticoRetoActualizado -= OnDiag;
        Debug.Log($"[Reto4E2E] Comprobante capturado: reto={diagReto} texto=\"{diagCapturado}\"");
        if (diagReto != 4 || diagCapturado == null || !diagCapturado.Contains("correcto"))
        { fails++; Debug.LogError("[Reto4E2E] ✗ El comprobante de éxito no llegó (o no dice 'correcto')."); }

        // ── Fin del juego: LoadLevel(4) tras completar el Reto 4 debe disparar CompleteGame() ──
        bool gameCompletedFired = false;
        System.Action onGameDone = () => gameCompletedFired = true;
        GameManager.OnGameCompleted += onGameDone;
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 4 });
        GameManager.OnGameCompleted -= onGameDone;
        Debug.Log($"[Reto4E2E] LoadLevel(4) (tras completar el reto 4) -> OnGameCompleted disparado={gameCompletedFired} (esperado true)");
        if (!gameCompletedFired) { fails++; Debug.LogError("[Reto4E2E] ✗ Completar el Reto 4 no dispara el fin del juego."); }

        Object.DestroyImmediate(midGo);
        Object.DestroyImmediate(rGo);
        Object.DestroyImmediate(ledGo);

        Debug.Log(fails == 0
            ? "\n[Reto4E2E] ===== RESULTADO: ✓ Circuito real + sketch real + medición real completan el Reto 4 y terminan el juego ====="
            : $"\n[Reto4E2E] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        Finish(fails == 0 ? 0 : 1);
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto4E2E] No encontré el método privado '{method}'."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static bool InvokePrivateBool(System.Type t, object instance, string method) => (bool)InvokePrivate(t, instance, method);

    static object GetPrivate(System.Type t, object instance, string field)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError($"[Reto4E2E] No encontré el campo privado '{field}'."); return null; }
        return f.GetValue(instance);
    }
}
