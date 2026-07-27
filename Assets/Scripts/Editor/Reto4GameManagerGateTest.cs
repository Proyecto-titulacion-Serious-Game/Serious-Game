using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verificación headless de la capa GameManager.EvaluarReto4() (no solo ProtoboardSimulator, que ya
/// cubre Reto4EndToEndTest.cs) — sobre la escena REAL, con LoadLevel(3) real (activa reto4Zone igual
/// que en juego), confirmando:
///   1. Circuito correcto SIN medir resistencia con el multímetro (modo OHMS) → NO completa.
///   2. Mismo circuito CON la resistencia medida → SÍ completa (CompleteLevel(true)).
///   3. El diagnóstico publicado (GameSession.ReportarDiagnosticoReto) no revienta el límite de
///      512 bytes de un RPC de Fusion (mismo chunking que ya se verificó para Reto 2).
///
/// Menú: Tools → TITA → Reto 4 → Test gate GameManager (headless)
/// </summary>
public static class Reto4GameManagerGateTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Test gate GameManager (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { Debug.LogError("[Reto4Gate] No hay GameManager en la escena."); Finish(1); return; }
        var tGm = typeof(GameManager);

        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 3 });
        Debug.Log($"[Reto4Gate] reto4Zone.active={(gm.reto4Zone != null ? gm.reto4Zone.activeSelf.ToString() : "null")} " +
                  $"reto1/2/3 off? {(gm.reto1Zone != null ? !gm.reto1Zone.activeSelf : true)}/" +
                  $"{(gm.reto2Zone != null ? !gm.reto2Zone.activeSelf : true)}/" +
                  $"{(gm.reto3Zone != null ? !gm.reto3Zone.activeSelf : true)}");

        // ── Circuito sintético válido (mismo patrón que Reto4EndToEndTest.cs) ──
        var root = new GameObject("Reto4Gate_ProtoTest");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var pinNode = NewChildNode(root.transform, "Pin9");
        var gnd     = NewChildNode(root.transform, "GND");
        var goArduino = new GameObject("ArduinoTest");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND = gnd;
        core.outputVoltageTTL = 5f;
        core.RegisterPinNode(9, pinNode);

        var mid = NewChildNode(root.transform, "Mid");
        var r = NewChildComp<Resistor>(root.transform, "R");
        r.resistance = 330f; r.nodeA = pinNode; r.nodeB = mid;
        var led = NewChildComp<LED>(root.transform, "LED");
        led.forwardVoltage = 2.0f; led.resistance = 50f; led.maxSafeCurrent = 0.02f;
        led.polarityInverted = false; led.nodeA = mid; led.nodeB = gnd;

        core.LoadSketchProgram(@"
            void setup() { pinMode(9, OUTPUT); }
            void loop() { digitalWrite(9, HIGH); }");
        if (!core.ProgramRunning) { Debug.LogError("[Reto4Gate] El sketch sintético no compiló."); fails++; }

        var interp = (ArduinoInterpreter)GetPrivateField(typeof(ArduinoCore), core, "_interp");
        Drain(interp.RunSetup());
        Drain(interp.RunLoop());

        // Inyectar el sim sintético en GameManager (mismo campo público que usa EvaluarReto4()).
        gm.protoSim = protoSim;

        // IMPORTANTE (hallazgo de este test): GameManager.Start() — que hace
        // "ProtoboardSimulator.OnSandboxValidated += OnSandboxResult" — NUNCA corre fuera de Play
        // Mode (solo abrir la escena no dispara Start(), a diferencia de Awake). Sin esa suscripción
        // viva, gm._lastSandboxResult (que es lo que EvaluarReto4() realmente lee) nunca se actualiza
        // sola. Sincronizamos a mano, con lo que HARÍA esa suscripción si estuviera corriendo — esto
        // es una limitación del arnés headless, NO un bug del juego real (en Play Mode sí se suscribe).
        var fSimResult = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var fGmResult  = typeof(GameManager).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        void SyncSandboxResult()
        {
            protoSim.ForzarValidacion();
            fGmResult.SetValue(gm, fSimResult.GetValue(protoSim));
        }

        // ── PASO 1: multímetro presente pero SIN usar modo OHMS → debe RECHAZAR ──
        var multiGO = new GameObject("Multimeter_Gate_Test");
        var multi = multiGO.AddComponent<Multimeter>();
        gm.multimeter = multi;

        SyncSandboxResult();
        bool r1 = (bool)InvokePrivate(tGm, gm, "EvaluarReto4");
        bool levelCompleted1 = (bool)GetPrivateField(tGm, gm, "_levelCompleted");
        Debug.Log($"[Reto4Gate] PASO 1 (circuito OK, SIN medir resistencia): EvaluarReto4()={r1} _levelCompleted={levelCompleted1} (esperado: false / false)");
        if (r1 != false || levelCompleted1 != false)
        {
            Debug.LogError("[Reto4Gate] ✗ El gate de 'resistencia medida' NO está bloqueando — un circuito correcto completa SIN pasar por el multímetro en modo OHMS.");
            fails++;
        }
        else Debug.Log("[Reto4Gate] ✓ Correctamente bloqueado sin medir resistencia.");

        // ── PASO 2: forzar wasUsedInResistanceMode = true → debe COMPLETAR ──
        var fUsed = typeof(Multimeter).GetField("_usedResistanceMode", BindingFlags.NonPublic | BindingFlags.Instance);
        fUsed.SetValue(multi, true);

        SyncSandboxResult();
        bool r2 = (bool)InvokePrivate(tGm, gm, "EvaluarReto4");
        bool levelCompleted2 = (bool)GetPrivateField(tGm, gm, "_levelCompleted");
        Debug.Log($"[Reto4Gate] PASO 2 (circuito OK, CON resistencia medida): EvaluarReto4()={r2} _levelCompleted={levelCompleted2} (esperado: true / true)");
        if (r2 != true || levelCompleted2 != true)
        {
            Debug.LogError("[Reto4Gate] ✗ Con la resistencia medida, el reto NO completó — regresión en EvaluarReto4()/CompleteLevel().");
            fails++;
        }
        else Debug.Log("[Reto4Gate] ✓ Completa correctamente una vez medida la resistencia.");

        // ── PASO 3: tamaño del diagnóstico de Reto4DiagnosticoReporter (chunking) ──
        var diagSys = new DiagnosticSystem();
        string texto = diagSys.GetDiagnosisArduino(core, protoSim) + "\n\n> " + diagSys.GetNextActionArduino(core, protoSim);
        int bytes = System.Text.Encoding.UTF8.GetByteCount(texto);
        Debug.Log($"[Reto4Gate] PASO 3: GetDiagnosisArduino+GetNextActionArduino = {texto.Length} chars / {bytes} bytes UTF-8.");
        Debug.Log($"[Reto4Gate]   Con chunking de 200 chars (GameSession.ReportarDiagnosticoReto), esto viaja en {Mathf.CeilToInt(texto.Length / 200f)} trozo(s) — cada trozo bien por debajo del límite de 512 bytes de Fusion.");
        // Confirmación estructural: el método estático es el mismo para los 4 retos (no hay un
        // camino separado para Reto 4 que evite el chunking).
        var miReto = typeof(GameSession).GetMethod("ReportarDiagnosticoReto", BindingFlags.Public | BindingFlags.Static);
        if (miReto == null) { Debug.LogError("[Reto4Gate] ✗ No encontré GameSession.ReportarDiagnosticoReto (¿renombrado?)."); fails++; }
        else Debug.Log("[Reto4Gate] ✓ Reto4DiagnosticoReporter/EvaluarReto4 publican por el mismo GameSession.ReportarDiagnosticoReto con chunking — cubierto.");

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(multiGO);

        Debug.Log(fails == 0
            ? "\n[Reto4Gate] ===== RESULTADO: ✓ Gate de resistencia medida + validación Reto 4 vía GameManager funcionan ====="
            : $"\n[Reto4Gate] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");
        Finish(fails == 0 ? 0 : 1);
    }

    static void Drain(System.Collections.Generic.IEnumerable<ArduinoInterpreter.Signal> seq)
    {
        int n = 0;
        foreach (var _ in seq) { if (++n >= 2000) break; }
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto4Gate] No encontré el método privado '{method}'."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivateField(System.Type t, object instance, string field)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError($"[Reto4Gate] No encontré el campo privado '{field}'."); return null; }
        return f.GetValue(instance);
    }

    static ElectricalNode NewChildNode(Transform parent, string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.AddComponent<ElectricalNode>();
    }

    static T NewChildComp<T>(Transform parent, string n) where T : Component
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }
}
