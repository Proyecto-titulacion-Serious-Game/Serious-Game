using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verificación HEADLESS end-to-end del Reto 4: arma circuitos DISTINTOS (protoboard + Arduino),
/// les sube código C++ REAL (compilado y ejecutado por el intérprete real, no un mock), y llama
/// exactamente la misma función que usa el botón "Comprobar circuito" en el juego
/// (<c>ProtoboardSimulator.ForzarValidacion</c>, la que invoca <c>GameManager.EvaluarReto4</c>).
///
/// Objetivo: confirmar que el Reto 4 se puede completar de MÁS DE UNA forma válida (no depende
/// de un único circuito/código hardcodeado) y que sigue rechazando un circuito inválido.
///
/// Ejecutar:
///   Editor:     Tools → TITA → Reto 4 → Test end-to-end (headless)
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4EndToEndTest.Run -logFile -
/// </summary>
public static class Reto4EndToEndTest
{
    struct Scenario
    {
        public string name;
        public int    pin;
        public float  resistance;
        public bool   hasLed;
        public float  ledForwardVoltage;
        public bool   ledInverted;
        public string sketch;
        public bool   expectSuccess;
    }

    [MenuItem("Tools/TITA/Reto 4/Test end-to-end (headless)")]
    public static void Run()
    {
        int fails = 0;
        Debug.Log("===== RETO 4 — TEST END-TO-END (circuitos distintos + código C++ real vía EvaluarReto4) =====");

        var scenarios = new List<Scenario>
        {
            new Scenario {
                name = "Ronda 1: digitalWrite HIGH fijo, LED verde 330Ω en pin D9",
                pin = 9, resistance = 330f, hasLed = true, ledForwardVoltage = 2.0f, ledInverted = false,
                sketch = @"
                    void setup() { pinMode(9, OUTPUT); }
                    void loop() { digitalWrite(9, HIGH); }",
                expectSuccess = true
            },
            new Scenario {
                name = "Ronda 2: analogWrite PWM, LED distinto (Vf 1.8V) 300Ω en pin D6",
                pin = 6, resistance = 300f, hasLed = true, ledForwardVoltage = 1.8f, ledInverted = false,
                sketch = @"
                    void setup() { pinMode(6, OUTPUT); }
                    void loop() { analogWrite(6, 220); }",
                expectSuccess = true
            },
            new Scenario {
                name = "Ronda 3: SIN LED, corriente continua segura (1000Ω) en pin D11 — otra forma válida de resolver el reto",
                pin = 11, resistance = 1000f, hasLed = false, ledForwardVoltage = 0f, ledInverted = false,
                sketch = @"
                    void setup() { pinMode(11, OUTPUT); }
                    void loop() { digitalWrite(11, HIGH); }",
                expectSuccess = true
            },
            new Scenario {
                name = "Ronda 4: código C++ con variable + if (no HIGH literal) 390Ω en pin D3",
                pin = 3, resistance = 390f, hasLed = true, ledForwardVoltage = 2.0f, ledInverted = false,
                sketch = @"
                    int contador = 0;
                    void setup() { pinMode(3, OUTPUT); }
                    void loop() {
                        contador = contador + 1;
                        if (contador >= 1) { digitalWrite(3, HIGH); }
                    }",
                expectSuccess = true
            },
            new Scenario {
                name = "Control negativo: LED con polaridad invertida en pin D5 (debe FALLAR)",
                pin = 5, resistance = 330f, hasLed = true, ledForwardVoltage = 2.0f, ledInverted = true,
                sketch = @"
                    void setup() { pinMode(5, OUTPUT); }
                    void loop() { digitalWrite(5, HIGH); }",
                expectSuccess = false
            },
        };

        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var sc in scenarios)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = RunScenarioFull(sc);
            sw.Stop();
            string linea =
                $"[{sc.name}]\n" +
                $"    esperado={sc.expectSuccess} obtenido={r.success}  |  tiempo={sw.Elapsed.TotalMilliseconds:F2} ms\n" +
                $"    mensaje=\"{r.message}\"\n" +
                $"    activatedPin=D{r.activatedPin}  pathFound={r.pathFound}  hasLED={r.hasLED}  " +
                $"ledForwardBiased={r.ledForwardBiased}  hasProtection={r.hasProtection}  " +
                $"blinkEnabled={r.blinkEnabled}  I≈{r.currentMa:F2} mA";
            if (r.success == sc.expectSuccess) Debug.Log(linea);
            else { fails++; Debug.LogError(linea + "\n    <-- NO COINCIDE CON LO ESPERADO"); }
        }
        totalSw.Stop();

        Debug.Log($"===== TIEMPO TOTAL DE LOS {scenarios.Count} ESCENARIOS (solo lógica, sin arranque de Unity): {totalSw.Elapsed.TotalMilliseconds:F2} ms =====");
        Debug.Log(fails == 0
            ? "===== RESULTADO: ✓ El Reto 4 se completa con circuitos y códigos C++ distintos, y sigue rechazando uno inválido ====="
            : $"===== RESULTADO: ✗ {fails} escenario(s) no coincidieron con lo esperado =====");

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static SandboxValidationResult RunScenarioFull(Scenario sc)
    {
        // Raíz del sandbox (inactiva: evita OnEnable/StartCoroutine de ProtoboardSimulator/ArduinoCore
        // en modo Editor sin Play Mode — mismo patrón que Reto4VoltageTest.cs).
        var root = new GameObject("ProtoTest");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var pinNode = NewChildNode(root.transform, $"Pin{sc.pin}");
        var gnd     = NewChildNode(root.transform, "GND");

        var goArduino = new GameObject("ArduinoTest");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND         = gnd;
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
            led.polarityInverted = sc.ledInverted;
            led.nodeA = mid; led.nodeB = gnd;
        }
        else
        {
            var r = NewChildComp<Resistor>(root.transform, "R");
            r.resistance = sc.resistance; r.nodeA = pinNode; r.nodeB = gnd;
        }

        // ── Código C++ REAL del Técnico: la misma ruta de producción (ArduinoIDEUI → ArduinoCore.
        // LoadSketchProgram → ArduinoInterpreter real). Solo reemplazamos el "tick" automático de
        // la corrutina (no corre fuera de Play Mode) por un drenado manual de RunSetup()/RunLoop(),
        // igual que hace Reto4InterpreterTest.cs con su MockBoard — aquí en cambio el intérprete
        // escribe sobre el ArduinoCore real, así que los nodos eléctricos quedan con voltaje real.
        core.LoadSketchProgram(sc.sketch);
        if (!core.ProgramRunning)
        {
            Object.DestroyImmediate(root);
            return new SandboxValidationResult { success = false, message = "El sketch no compiló." };
        }

        var interp = GetPrivateInterp(core);
        Drain(interp.RunSetup());
        Drain(interp.RunLoop());

        // ── Validación real: MISMA llamada que GameManager.EvaluarReto4() hace desde el botón físico ──
        protoSim.ForzarValidacion();
        var result = GetLastResult(protoSim);

        Object.DestroyImmediate(root);
        return result;
    }

    static void Drain(IEnumerable<ArduinoInterpreter.Signal> seq)
    {
        int n = 0;
        foreach (var _ in seq) { if (++n >= 2000) break; } // tope de seguridad ante un loop mal formado
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
