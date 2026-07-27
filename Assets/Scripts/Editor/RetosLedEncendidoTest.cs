using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verifica RETO POR RETO (escena real, Explorador.unity) que:
///   1. El reto es completable (misma ruta de reparación que el juego real —
///      ComponentDeliverySystem.DebugSimularEntregaEInstalacion / sandbox del Reto 4).
///   2. Al completarse, el LED del reto queda ENCENDIDO: isOn=true, state=Correct.
///   3. La capa VISUAL responde: la emisión del material queda no-negra (el LED "brilla").
///      Para eso se fuerza Awake() (edit mode no lo corre) y se repinta Off→Correct.
///   4. CircuitVictoryEffect dispara su estallido por cada LED encendido al completar.
///
/// Lo que NO puede verificar headless: el latido de 3 s (LED.BoostVictoria animado por
/// corrutina — el tiempo no avanza en batch) y el Bloom en pantalla (requiere render + VR).
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod RetosLedEncendidoTest.Run -logFile -
/// </summary>
public static class RetosLedEncendidoTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    static readonly FieldInfo MatInstField =
        typeof(LED).GetField("_matInst", BindingFlags.NonPublic | BindingFlags.Instance);

    [MenuItem("Tools/TITA/Pruebas/Retos + LED encendido (headless)")]
    public static void Run()
    {
        var resultados = new List<string>();
        bool allOk = false;
        try
        {
            allOk = RunAll(resultados);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LedTest] EXCEPCIÓN: {ex}");
            resultados.Add("EXCEPCION=FALLO");
        }
        finally
        {
            Debug.Log("===== RESUMEN LED POR RETO =====");
            foreach (var r in resultados) Debug.Log($"##LEDTEST## {r}");
            LED.BoostVictoria = 1f;
            if (Application.isBatchMode) EditorApplication.Exit(allOk ? 0 : 1);
        }
    }

    static bool RunAll(List<string> resultados)
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();
        if (gm == null || delivery == null)
        {
            resultados.Add("SETUP=FALLO (falta GameManager o ComponentDeliverySystem)");
            return false;
        }

        // OpenScene no corre Awake/OnEnable de objetos ya guardados (solo Play Mode los corre):
        // despertar los motores manualmente, igual que FullGameTwoConfigsTest.
        foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            cm.AutoDetectComponents();
        gm.SendMessage("Start", SendMessageOptions.DontRequireReceiver);

        // ── Reto 1 (OhmLaw): resistor 850Ω + switch cerrado ─────────────────
        InvokeLoadLevel(gm, 0);
        var sw = Object.FindObjectsByType<CircuitSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (sw != null) sw.isOn = true;
        DespertarLedsActivos();
        bool r1entrega = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, 850f);
        bool r1win = ChequearVictoriaDirecta(gm);
        string r1led = RepintarYReportarLeds(ResimularRetos123, minimoEncendidos: 1, out bool r1ledOk);
        int r1bursts = ProbarEstallido(gm, LevelType.OhmLaw);
        resultados.Add($"Reto1(OhmLaw) entrega={r1entrega} victoria={r1win} leds=[{r1led}] estallidos={r1bursts} " +
                       (r1entrega && r1win && r1ledOk && r1bursts >= 1 ? "OK" : "FALLO"));

        // ── Reto 2 (Parallel): polaridad del LED2 + 6 jumpers del puzzle ────
        InvokeLoadLevel(gm, 1);
        foreach (var conn in Object.FindObjectsByType<ProtoboardConnector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            conn.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            conn.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        }
        var fuente = Object.FindObjectsByType<VoltageSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(v => v.gameObject.name == "Fuente_9V");
        if (fuente != null) fuente.gameObject.SetActive(true);
        var proto2 = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(p => p.gameObject.name == "Protoboard_Reto2");
        proto2?.ForzarValidacion();
        if (proto2 != null && fuente != null)
        {
            void Puentear(string nombre, ElectricalNode a, string railB)
            {
                var nb = proto2.NodeForRail(railB);
                if (a == null || nb == null) return;
                var j = new GameObject(nombre).AddComponent<Jumper>();
                j.transform.SetParent(proto2.transform, false);
                j.nodeA = a; j.nodeB = nb;
            }
            Puentear("TestJumper_Bat_VCC", fuente.nodeA, "VCC");
            Puentear("TestJumper_Bat_GND", fuente.nodeB, "GND");
            Puentear("TestJumper_VCC_COL0A", proto2.NodeForRail("VCC"), "COL_0A");
            Puentear("TestJumper_COL0B_COL0C", proto2.NodeForRail("COL_0B"), "COL_0C");
            Puentear("TestJumper_VCC_COL1A", proto2.NodeForRail("VCC"), "COL_1A");
            Puentear("TestJumper_COL1B_COL1C", proto2.NodeForRail("COL_1B"), "COL_1C");
            proto2.ForzarValidacion();
        }
        DespertarLedsActivos();
        bool r2entrega = delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
        proto2?.ForzarValidacion();
        bool r2win = ChequearVictoriaDirecta(gm);
        string r2led = RepintarYReportarLeds(() => { ResimularRetos123(); proto2?.ForzarValidacion(); },
                                             minimoEncendidos: 2, out bool r2ledOk);
        int r2bursts = ProbarEstallido(gm, LevelType.Parallel);
        resultados.Add($"Reto2(Parallel) entrega={r2entrega} victoria={r2win} leds=[{r2led}] estallidos={r2bursts} " +
                       (r2entrega && r2win && r2ledOk && r2bursts >= 2 ? "OK" : "FALLO"));

        // ── Reto 3 (Mixed): resistor 470Ω (zona verde del LED) + LED polaridad + capacitor ──
        InvokeLoadLevel(gm, 2);
        DespertarLedsActivos();
        bool r3a = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, 470f);
        bool r3b = delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
        bool r3c = delivery.DebugSimularEntregaEInstalacion(ComponentType.Capacitor, 1f);
        bool r3win = ChequearVictoriaDirecta(gm);
        string r3led = RepintarYReportarLeds(ResimularRetos123, minimoEncendidos: 1, out bool r3ledOk);
        int r3bursts = ProbarEstallido(gm, LevelType.Mixed);
        resultados.Add($"Reto3(Mixed) entregas=[{r3a},{r3b},{r3c}] victoria={r3win} leds=[{r3led}] estallidos={r3bursts} " +
                       (r3a && r3b && r3c && r3win && r3ledOk && r3bursts >= 1 ? "OK" : "FALLO"));

        // ── Reto 4 (Arduino): sandbox real con LED con renderer (visual) ────
        InvokeLoadLevel(gm, 3);
        var r4 = CorrerReto4ConLedVisual();
        resultados.Add($"Reto4(Arduino) completado={r4.ok} led=[{r4.led}] " + (r4.ok && r4.ledOk ? "OK" : "FALLO"));

        return resultados.All(r => r.EndsWith("OK"));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  LED: despertar (Awake), repintar Off→Correct y leer la emisión real
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Fuerza Awake() en los LEDs activos: crea _matInst y pinta el estado apagado,
    /// para que la transición a Correct durante la simulación pinte la emisión de verdad.</summary>
    static void DespertarLedsActivos()
    {
        foreach (var led in Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            led.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
    }

    /// <summary>
    /// Fuerza el repintado (state=Off → resimular → Correct vuelve a pintar) y reporta cada LED
    /// activo y cableado: isOn, state y si la EMISIÓN del material quedó no-negra (brilla).
    /// ledOk = al menos <paramref name="minimoEncendidos"/> LEDs encendidos, y TODOS los
    /// encendidos con emisión pintada.
    /// </summary>
    static string RepintarYReportarLeds(System.Action resimular, int minimoEncendidos, out bool ledOk)
    {
        var leds = Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude)
            .Where(l => l.nodeA != null && l.nodeB != null).ToArray();
        foreach (var l in leds) l.state = LEDState.Off;   // obliga a SetState a repintar
        resimular();

        var partes = new List<string>();
        int encendidos = 0, brillan = 0;
        foreach (var l in leds)
        {
            string emision = EmisionDe(l, out bool brilla);
            // Misma regla que la victoria real (GameManager.CumpleVictoriaRetos123):
            // un LED cuenta como encendido si isOn y NO está en Overload — NearOverload
            // es válido para el juego y pinta el mismo color físico que Correct.
            if (l.isOn && l.state != LEDState.Overload)
            {
                encendidos++;
                if (brilla) brillan++;
            }
            partes.Add($"'{l.name}' isOn={l.isOn} state={l.state} {emision}");
        }
        ledOk = encendidos >= minimoEncendidos && brillan == encendidos;
        return string.Join(" | ", partes);
    }

    static string EmisionDe(LED led, out bool brilla)
    {
        brilla = false;
        var m = (Material)MatInstField.GetValue(led);
        if (m == null) return "emision=SIN-MATERIAL";
        if (!m.HasProperty("_EmissionColor")) return "emision=SHADER-SIN-EMISION";
        var c = m.GetColor("_EmissionColor");
        brilla = c.maxColorComponent > 0.05f;
        return $"emision={(brilla ? "ENCENDIDA" : "APAGADA")}(max={c.maxColorComponent:F2})";
    }

    /// <summary>
    /// Dispara el handler REAL de CircuitVictoryEffect (OnLevelCompleted success=true) y cuenta
    /// los estallidos "VictoryBurst" creados — debe haber uno por LED encendido. El Destroy
    /// diferido del efecto no corre en edit mode: se limpian con DestroyImmediate para no
    /// contaminar el conteo del siguiente reto.
    /// </summary>
    static int ProbarEstallido(GameManager gm, LevelType nivel)
    {
        var go = new GameObject("[LedTest_VictoryEffect]");
        int bursts = 0;
        try
        {
            var fx = go.AddComponent<CircuitVictoryEffect>();
            var handler = typeof(CircuitVictoryEffect).GetMethod("OnLevelCompleted",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try { handler.Invoke(fx, new object[] { nivel, true }); }
            catch (System.Exception ex)
            {
                // Destroy(go, 2.5f) no es válido en edit mode — si revienta ahí, los bursts
                // previos ya se crearon y cuentan igual.
                Debug.LogWarning($"[LedTest] Estallido con excepción tolerada (edit mode): {ex.InnerException?.Message ?? ex.Message}");
            }

            var restos = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude)
                .Where(p => p != null && p.gameObject.name == "VictoryBurst")
                .Select(p => p.gameObject).Distinct().ToArray();
            bursts = restos.Length;
            foreach (var b in restos) Object.DestroyImmediate(b);
        }
        finally
        {
            LED.BoostVictoria = 1f;
            Object.DestroyImmediate(go);
        }
        return bursts;
    }

    static void ResimularRetos123()
    {
        foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
            cm.ForceSimulate();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Reto 4: sandbox real + LED con renderer para verificar la emisión
    // ─────────────────────────────────────────────────────────────────────

    static (bool ok, bool ledOk, string led) CorrerReto4ConLedVisual()
    {
        var root = new GameObject("LedTestReto4");
        try
        {
            var protoSim = root.AddComponent<ProtoboardSimulator>();

            var gnd = NewNode(root.transform, "GND");
            var goArduino = new GameObject("ArduinoTest");
            goArduino.transform.SetParent(root.transform, false);
            var core = goArduino.AddComponent<ArduinoCore>();
            core.nodoGND = gnd;
            core.outputVoltageTTL = 5f;

            var pinNode = NewNode(root.transform, "Pin9");
            core.RegisterPinNode(9, pinNode);
            var mid = NewNode(root.transform, "Mid9");
            var r = NewComp<Resistor>(root.transform, "R9");
            r.resistance = 330f; r.nodeA = pinNode; r.nodeB = mid;

            // LED con renderer hijo (cubo) para que Awake cree la instancia de material
            // y podamos verificar la EMISIÓN igual que en los Retos 1-3.
            var ledGo = new GameObject("LED9");
            ledGo.transform.SetParent(root.transform, false);
            var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cuerpo.transform.SetParent(ledGo.transform, false);
            var led = ledGo.AddComponent<LED>();
            led.forwardVoltage = 2.0f; led.resistance = 50f; led.maxSafeCurrent = 0.02f;
            led.nodeA = mid; led.nodeB = gnd;
            led.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

            string sketch = @"
                void setup() { pinMode(9, OUTPUT); }
                void loop() { digitalWrite(9, HIGH); }";
            core.LoadSketchProgram(sketch);
            if (!core.ProgramRunning) return (false, false, "sketch no compiló");

            var interp = (ArduinoInterpreter)typeof(ArduinoCore)
                .GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(core);
            Drain(interp.RunSetup());
            Drain(interp.RunLoop());

            protoSim.ForzarValidacion();

            var result = (SandboxValidationResult)typeof(ProtoboardSimulator)
                .GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(protoSim);

            string emision = EmisionDe(led, out bool brilla);
            bool ledOk = led.isOn && led.state == LEDState.Correct && brilla;
            string detalle = $"'LED9' isOn={led.isOn} state={led.state} I={led.current * 1000f:F1}mA {emision} — \"{result.message}\"";
            return (result.success, ledOk, detalle);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers compartidos (mismo patrón que FullGameTwoConfigsTest)
    // ─────────────────────────────────────────────────────────────────────

    static void InvokeLoadLevel(GameManager gm, int index)
        => typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(gm, new object[] { index });

    static bool ChequearVictoriaDirecta(GameManager gm)
        => (bool)typeof(GameManager).GetMethod("CumpleVictoriaRetos123", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(gm, null);

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
}
