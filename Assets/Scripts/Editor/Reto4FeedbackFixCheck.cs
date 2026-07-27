using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verificación puntual del fix de Reto4Feedback (2026-07-16): confirma que un LED presente
/// pero mal protegido ya NO se clasifica como "SinLED" (bug original), y explora los 3 sub-casos
/// de badLed (Off / NearOverload / Overload) con y sin resistencia >=100 Ω en el camino.
///
/// Ejecutar:
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4FeedbackFixCheck.Run -logFile -
/// </summary>
public static class Reto4FeedbackFixCheck
{
    struct Scenario
    {
        public string name;
        public int    pin;
        public float  resistance;
        public float  ledForwardVoltage;
    }

    [MenuItem("Tools/TITA/Reto 4/Check fix diagnóstico (headless)")]
    public static void Run()
    {
        Debug.Log("===== RETO 4 — CHECK FIX Reto4Feedback (LED mal protegido ya NO debe salir como SinLED) =====");

        var scenarios = new List<Scenario>
        {
            new Scenario { name = "R=20ohm (muy baja, <100) -> Overload",      pin = 4, resistance = 20f,   ledForwardVoltage = 2.0f },
            new Scenario { name = "R=180ohm (>=100 pero insuficiente) -> NearOverload/Overload leve", pin = 7, resistance = 180f, ledForwardVoltage = 1.8f },
            new Scenario { name = "R=100000ohm (excesiva) -> Off (no enciende)", pin = 8, resistance = 100000f, ledForwardVoltage = 2.0f },
        };

        foreach (var sc in scenarios)
        {
            var r = RunScenarioFull(sc);
            var motivo = Reto4Feedback.Clasificar(r);
            string nivel3 = Reto4Feedback.Construir(3, r.activatedPin, motivo);
            Debug.Log(
                $"##FIXCHECK## {sc.name} pin=D{sc.pin} R={sc.resistance:F0}ohm | success={r.success} " +
                $"hasLED={r.hasLED} hasProtection={r.hasProtection} mensaje_simulador=\"{r.message}\" | " +
                $"clasificado_como={motivo} | texto_tecnico=\"{nivel3}\"");
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static SandboxValidationResult RunScenarioFull(Scenario sc)
    {
        var root = new GameObject("ProtoTest");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var pinNode = NewChildNode(root.transform, $"Pin{sc.pin}");
        var gnd     = NewChildNode(root.transform, "GND");

        var goArduino = new GameObject("ArduinoTest");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND          = gnd;
        core.outputVoltageTTL = 5f;
        core.RegisterPinNode(sc.pin, pinNode);

        var mid = NewChildNode(root.transform, "Mid");
        var res = NewChildComp<Resistor>(root.transform, "R");
        res.resistance = sc.resistance; res.nodeA = pinNode; res.nodeB = mid;

        var led = NewChildComp<LED>(root.transform, "LED");
        led.forwardVoltage   = sc.ledForwardVoltage;
        led.resistance       = 50f;
        led.maxSafeCurrent   = 0.02f;
        led.polarityInverted = false;
        led.nodeA = mid; led.nodeB = gnd;

        core.LoadSketchProgram($@"
            void setup() {{ pinMode({sc.pin}, OUTPUT); }}
            void loop() {{ digitalWrite({sc.pin}, HIGH); }}");
        if (!core.ProgramRunning)
        {
            Object.DestroyImmediate(root);
            return new SandboxValidationResult { success = false, message = "El sketch no compiló." };
        }

        var interp = GetPrivateInterp(core);
        Drain(interp.RunSetup());
        Drain(interp.RunLoop());

        protoSim.ForzarValidacion();
        var result = GetLastResult(protoSim);

        Object.DestroyImmediate(root);
        return result;
    }

    static void Drain(IEnumerable<ArduinoInterpreter.Signal> seq)
    {
        int n = 0;
        foreach (var _ in seq) { if (++n >= 2000) break; }
    }

    static ArduinoInterpreter GetPrivateInterp(ArduinoCore core)
    {
        var f = typeof(ArduinoCore).GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance);
        return (ArduinoInterpreter)f.GetValue(core);
    }

    static SandboxValidationResult GetLastResult(ProtoboardSimulator sim)
    {
        var f = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        return (SandboxValidationResult)f.GetValue(sim);
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
