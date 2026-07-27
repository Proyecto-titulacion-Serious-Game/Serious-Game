using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Segunda tanda de pruebas del Reto 4 (2026-07-16), pedida tras el reporte anterior:
///   A) 5 circuitos NUEVOS y distintos (mismo tipo de chequeo que Reto4FiveCircuitsDemo, con
///      estilos de código C++ que aún no se habían probado: for, while, expresión matemática en
///      PWM, if/else, y un segundo caso "sin LED").
///   B) 2 pruebas con VARIOS componentes: (B1) 2 LEDs + 2 resistencias en 2 pines simultáneos
///      ("semáforo", ya documentado como caso soportado); (B2) 3 LEDs + 3 resistencias + 1
///      capacitor decorativo en paralelo a uno de los LEDs, para ver empíricamente cómo lo trata
///      el validador (no hay suposición: se corre y se reporta lo que realmente pasa).
///
/// Mismo patrón que Reto4FiveCircuitsDemo.cs: código C++ real vía ArduinoInterpreter,
/// ProtoboardSimulator.ForzarValidacion() real (misma ruta que el botón físico).
///
/// Ejecutar:
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4SecondBatchTest.RunAll -logFile -
/// </summary>
public static class Reto4SecondBatchTest
{
    struct Branch
    {
        public int    pin;
        public float  resistance;
        public bool   hasLed;
        public float  ledForwardVoltage;
        public string ledColorLabel;
    }

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

    [MenuItem("Tools/TITA/Reto 4/Segunda tanda (5 nuevos + 2 multi-componente, headless)")]
    public static void RunAll()
    {
        RunFiveNew();
        RunMultiComponentA();
        RunMultiComponentB();
    }

    // ── A) 5 circuitos nuevos ──────────────────────────────────────────────
    static void RunFiveNew()
    {
        Debug.Log("===== RETO 4 — SEGUNDA TANDA: 5 CIRCUITOS NUEVOS =====");

        var scenarios = new List<Scenario>
        {
            new Scenario {
                name = "Circuito 6 — bucle for con contador", pin = 10, resistance = 220f,
                hasLed = true, ledForwardVoltage = 3.0f, ledColorLabel = "Azul", codeStyle = "for",
                sketch = @"
                    void setup() { pinMode(10, OUTPUT); }
                    void loop() {
                        int suma = 0;
                        for (int i = 0; i < 5; i = i + 1) { suma = suma + i; }
                        if (suma >= 10) { digitalWrite(10, HIGH); }
                    }"
            },
            new Scenario {
                name = "Circuito 7 — bucle while con bandera", pin = 12, resistance = 220f,
                hasLed = true, ledForwardVoltage = 3.2f, ledColorLabel = "Blanco", codeStyle = "while",
                sketch = @"
                    void setup() { pinMode(12, OUTPUT); }
                    void loop() {
                        int n = 0;
                        bool listo = false;
                        while (n < 3) { n = n + 1; if (n == 3) { listo = true; } }
                        if (listo) { digitalWrite(12, HIGH); }
                    }"
            },
            new Scenario {
                name = "Circuito 8 — PWM con expresión matemática", pin = 13, resistance = 300f,
                hasLed = true, ledForwardVoltage = 1.8f, ledColorLabel = "Rojo", codeStyle = "PWM + expresión",
                sketch = @"
                    void setup() { pinMode(13, OUTPUT); }
                    void loop() {
                        int base = 150;
                        int extra = 20 * 2;
                        analogWrite(13, base + extra);
                    }"
            },
            new Scenario {
                name = "Circuito 9 — if/else con múltiples pinMode", pin = 8, resistance = 390f,
                hasLed = true, ledForwardVoltage = 2.0f, ledColorLabel = "Amarillo", codeStyle = "if/else",
                sketch = @"
                    void setup() { pinMode(8, OUTPUT); pinMode(9, OUTPUT); }
                    void loop() {
                        int modo = 1;
                        if (modo == 0) { digitalWrite(8, LOW); } else { digitalWrite(8, HIGH); }
                    }"
            },
            new Scenario {
                name = "Circuito 10 — sin LED, corriente continua (R distinta)", pin = 4, resistance = 680f,
                hasLed = false, ledForwardVoltage = 0f, ledColorLabel = "(sin LED)", codeStyle = "digitalWrite literal",
                sketch = @"
                    void setup() { pinMode(4, OUTPUT); }
                    void loop() { digitalWrite(4, HIGH); }"
            },
        };

        int fails = 0;
        foreach (var sc in scenarios)
        {
            var r = RunSingleBranch(sc.pin, sc.resistance, sc.hasLed, sc.ledForwardVoltage, sc.sketch);
            string polaridad = sc.hasLed ? "ánodo→pin, cátodo→GND (correcta)" : "n/a";
            Debug.Log(
                $"##DIAG2## name=\"{sc.name}\" pin=D{sc.pin} R={sc.resistance:F0}ohm hasLED={sc.hasLed} " +
                $"ledColor=\"{sc.ledColorLabel}\" ledVf={sc.ledForwardVoltage:F2}V polaridad=\"{polaridad}\" " +
                $"codeStyle=\"{sc.codeStyle}\" | success={r.success} message=\"{r.message}\" " +
                $"currentMa={r.currentMa:F2}");
            if (!r.success) fails++;
        }

        Debug.Log(fails == 0
            ? "===== RESULTADO SEGUNDA TANDA (A): ✓ Los 5 circuitos nuevos completaron el Reto 4 ====="
            : $"===== RESULTADO SEGUNDA TANDA (A): ✗ {fails} circuito(s) NO completaron el reto =====");
    }

    // ── B1) 2 LEDs + 2 resistencias, 2 pines simultáneos ("semáforo") ──────
    static void RunMultiComponentA()
    {
        Debug.Log("===== RETO 4 — MULTI-COMPONENTE B1: 2 LEDs + 2 resistencias (2 pines) =====");

        var branches = new List<Branch>
        {
            new Branch { pin = 9, resistance = 330f, hasLed = true, ledForwardVoltage = 2.0f, ledColorLabel = "Verde" },
            new Branch { pin = 6, resistance = 270f, hasLed = true, ledForwardVoltage = 1.8f, ledColorLabel = "Rojo" },
        };
        string sketch = @"
            void setup() { pinMode(9, OUTPUT); pinMode(6, OUTPUT); }
            void loop() { digitalWrite(9, HIGH); digitalWrite(6, HIGH); }";

        var r = RunMultiBranch(branches, null, sketch);
        Debug.Log(
            $"##MULTI## name=\"B1: semáforo 2 pines\" ramas=\"D9(330Ω,Verde) + D6(270Ω,Rojo)\" | " +
            $"success={r.success} message=\"{r.message}\"");
    }

    // ── B2) 3 LEDs + 3 resistencias + 1 capacitor decorativo en paralelo a un LED ──
    static void RunMultiComponentB()
    {
        Debug.Log("===== RETO 4 — MULTI-COMPONENTE B2: 3 LEDs + 3 resistencias + capacitor en paralelo =====");

        var branches = new List<Branch>
        {
            new Branch { pin = 9,  resistance = 330f, hasLed = true, ledForwardVoltage = 2.0f, ledColorLabel = "Verde" },
            new Branch { pin = 6,  resistance = 270f, hasLed = true, ledForwardVoltage = 1.8f, ledColorLabel = "Rojo" },
            new Branch { pin = 11, resistance = 390f, hasLed = true, ledForwardVoltage = 2.1f, ledColorLabel = "Ámbar" },
        };
        string sketch = @"
            void setup() { pinMode(9, OUTPUT); pinMode(6, OUTPUT); pinMode(11, OUTPUT); }
            void loop() { digitalWrite(9, HIGH); digitalWrite(6, HIGH); digitalWrite(11, HIGH); }";

        // Capacitor decorativo (100 µF, polaridad correcta) en paralelo al LED de la rama D9
        // (mismos 2 nodos que el LED: mid_D9 <-> GND), como un capacitor de filtrado real.
        var r = RunMultiBranch(branches, capacitorAcrossBranchIndex: 0, sketch, logComponentStates: true);
        Debug.Log(
            $"##MULTI## name=\"B2: 3 LEDs+3R + capacitor 100uF en paralelo al LED de D9\" | " +
            $"success={r.success} message=\"{r.message}\"");
    }

    // ── Runners ─────────────────────────────────────────────────────────

    static SandboxValidationResult RunSingleBranch(int pin, float resistance, bool hasLed, float ledVf, string sketch)
        => RunMultiBranch(new List<Branch> {
               new Branch { pin = pin, resistance = resistance, hasLed = hasLed, ledForwardVoltage = ledVf }
           }, null, sketch);

    static SandboxValidationResult RunMultiBranch(List<Branch> branches, int? capacitorAcrossBranchIndex, string sketch, bool logComponentStates = false)
    {
        var root = new GameObject("ProtoTest");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var gnd = NewChildNode(root.transform, "GND");
        var goArduino = new GameObject("ArduinoTest");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND          = gnd;
        core.outputVoltageTTL = 5f;

        for (int i = 0; i < branches.Count; i++)
        {
            var b = branches[i];
            var pinNode = NewChildNode(root.transform, $"Pin{b.pin}");
            core.RegisterPinNode(b.pin, pinNode);

            if (b.hasLed)
            {
                var mid = NewChildNode(root.transform, $"Mid{b.pin}");
                var r = NewChildComp<Resistor>(root.transform, $"R{b.pin}");
                r.resistance = b.resistance; r.nodeA = pinNode; r.nodeB = mid;

                var led = NewChildComp<LED>(root.transform, $"LED{b.pin}");
                led.forwardVoltage   = b.ledForwardVoltage;
                led.resistance       = 50f;
                led.maxSafeCurrent   = 0.02f;
                led.polarityInverted = false;
                led.nodeA = mid; led.nodeB = gnd;

                if (capacitorAcrossBranchIndex.HasValue && capacitorAcrossBranchIndex.Value == i)
                {
                    var cap = NewChildComp<Capacitor>(root.transform, $"Cap{b.pin}");
                    cap.capacitance        = 0.0001f; // 100 µF
                    cap.polarityInverted   = false;
                    cap.nodeA = mid; cap.nodeB = gnd;  // en paralelo con el LED de esta rama
                }
            }
            else
            {
                var r = NewChildComp<Resistor>(root.transform, $"R{b.pin}");
                r.resistance = b.resistance; r.nodeA = pinNode; r.nodeB = gnd;
            }
        }

        core.LoadSketchProgram(sketch);
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

        if (logComponentStates)
        {
            foreach (var led in root.GetComponentsInChildren<LED>(true))
                Debug.Log($"##LEDSTATE## {led.name} state={led.state} current={led.current * 1000f:F2}mA isOn={led.isOn}");
            foreach (var cap in root.GetComponentsInChildren<Capacitor>(true))
                Debug.Log($"##CAPSTATE## {cap.name} state={cap.state} current={cap.current * 1000f:F3}mA voltageDrop={cap.voltageDrop:F2}V");
        }

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
