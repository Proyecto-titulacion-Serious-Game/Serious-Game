using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Corre 5 circuitos + códigos C++ REALES y distintos a través de la MISMA ruta de producción que
/// el botón físico de validación (ProtoboardSimulator.ForzarValidacion → GameManager.EvaluarReto4),
/// e imprime el diagnóstico completo de cada uno (JSON de una línea por escenario) para generar un
/// reporte. Basado en el mismo patrón que Reto4EndToEndTest.cs, pero con 5 circuitos DISTINTOS que
/// deben completar el reto (no incluye el control negativo).
///
/// Ejecutar:
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4FiveCircuitsDemo.Run -logFile -
/// </summary>
public static class Reto4FiveCircuitsDemo
{
    struct Scenario
    {
        public string name;
        public int    pin;
        public float  resistance;
        public bool   hasLed;
        public float  ledForwardVoltage;
        public string ledColorLabel;
        public string sketch;
        public string codeStyle;
    }

    [MenuItem("Tools/TITA/Reto 4/Demo 5 circuitos (headless, con diagnóstico)")]
    public static void Run()
    {
        Debug.Log("===== RETO 4 — DEMO 5 CIRCUITOS DISTINTOS (código C++ real vía EvaluarReto4) =====");

        var scenarios = new List<Scenario>
        {
            new Scenario {
                name = "Circuito 1 — HIGH fijo",
                pin = 9, resistance = 330f, hasLed = true, ledForwardVoltage = 2.0f, ledColorLabel = "Verde",
                codeStyle = "digitalWrite literal",
                sketch = @"
                    void setup() { pinMode(9, OUTPUT); }
                    void loop() { digitalWrite(9, HIGH); }"
            },
            new Scenario {
                name = "Circuito 2 — PWM (analogWrite)",
                pin = 6, resistance = 220f, hasLed = true, ledForwardVoltage = 1.8f, ledColorLabel = "Rojo",
                codeStyle = "analogWrite PWM",
                sketch = @"
                    void setup() { pinMode(6, OUTPUT); }
                    void loop() { analogWrite(6, 220); }"
            },
            new Scenario {
                name = "Circuito 3 — Sin LED, corriente continua",
                pin = 11, resistance = 1000f, hasLed = false, ledForwardVoltage = 0f, ledColorLabel = "(sin LED)",
                codeStyle = "digitalWrite literal",
                sketch = @"
                    void setup() { pinMode(11, OUTPUT); }
                    void loop() { digitalWrite(11, HIGH); }"
            },
            new Scenario {
                name = "Circuito 4 — Variable + condicional",
                pin = 3, resistance = 390f, hasLed = true, ledForwardVoltage = 2.1f, ledColorLabel = "Ámbar",
                codeStyle = "variable + if",
                sketch = @"
                    int contador = 0;
                    void setup() { pinMode(3, OUTPUT); }
                    void loop() {
                        contador = contador + 1;
                        if (contador >= 1) { digitalWrite(3, HIGH); }
                    }"
            },
            new Scenario {
                name = "Circuito 5 — Función propia del usuario",
                pin = 5, resistance = 270f, hasLed = true, ledForwardVoltage = 1.9f, ledColorLabel = "Naranja",
                codeStyle = "función definida por el usuario",
                sketch = @"
                    void encenderLED() { digitalWrite(5, HIGH); }
                    void setup() { pinMode(5, OUTPUT); }
                    void loop() { encenderLED(); }"
            },
        };

        int fails = 0;
        foreach (var sc in scenarios)
        {
            var r = RunScenarioFull(sc);
            string polaridad = sc.hasLed ? "ánodo→pin, cátodo→GND (correcta)" : "n/a";
            Debug.Log(
                $"##DIAG## name=\"{sc.name}\" pin=D{sc.pin} R={sc.resistance:F0}ohm hasLED={sc.hasLed} " +
                $"ledColor=\"{sc.ledColorLabel}\" ledVf={sc.ledForwardVoltage:F2}V polaridad=\"{polaridad}\" " +
                $"codeStyle=\"{sc.codeStyle}\" | success={r.success} message=\"{r.message}\" " +
                $"pathFound={r.pathFound} hasLED_result={r.hasLED} ledForwardBiased={r.ledForwardBiased} " +
                $"hasProtection={r.hasProtection} blinkEnabled={r.blinkEnabled} currentMa={r.currentMa:F2}");

            if (!r.success) fails++;
        }

        Debug.Log(fails == 0
            ? "===== RESULTADO: ✓ Los 5 circuitos completaron el Reto 4 ====="
            : $"===== RESULTADO: ✗ {fails} circuito(s) NO completaron el reto =====");

        // ── Chequeo aparte: ¿el diagnóstico que ve el Técnico es preciso en un caso de FALLA?
        // Circuito con LED presente pero resistencia demasiado baja (sobrecarga) — el LED SÍ está,
        // el problema es la corriente, no que falte.
        RunOverloadDiagnosticCheck();

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static void RunOverloadDiagnosticCheck()
    {
        var sc = new Scenario
        {
            name = "Chequeo de diagnóstico — LED presente pero SOBRECARGADO (R muy baja)",
            pin = 4, resistance = 20f, hasLed = true, ledForwardVoltage = 2.0f, ledColorLabel = "Verde",
            codeStyle = "digitalWrite literal",
            sketch = @"
                void setup() { pinMode(4, OUTPUT); }
                void loop() { digitalWrite(4, HIGH); }"
        };
        var r = RunScenarioFull(sc);
        var motivo   = Reto4Feedback.Clasificar(r);
        string nivel3 = Reto4Feedback.Construir(3, r.activatedPin, motivo);

        Debug.Log(
            $"##DIAGCHECK## {sc.name} | R={sc.resistance:F0}ohm (deliberadamente muy baja) success={r.success} " +
            $"mensaje_real_del_simulador=\"{r.message}\" | clasificado_como={motivo} | " +
            $"texto_que_veria_el_tecnico=\"{nivel3}\"");
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

        if (sc.hasLed)
        {
            var mid = NewChildNode(root.transform, "Mid");
            var r = NewChildComp<Resistor>(root.transform, "R");
            r.resistance = sc.resistance; r.nodeA = pinNode; r.nodeB = mid;

            var led = NewChildComp<LED>(root.transform, "LED");
            led.forwardVoltage   = sc.ledForwardVoltage;
            led.resistance       = 50f;
            led.maxSafeCurrent   = 0.02f;
            led.polarityInverted = false;
            led.nodeA = mid; led.nodeB = gnd;
        }
        else
        {
            var r = NewChildComp<Resistor>(root.transform, "R");
            r.resistance = sc.resistance; r.nodeA = pinNode; r.nodeB = gnd;
        }

        core.LoadSketchProgram(sc.sketch);
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
