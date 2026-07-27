using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MATRIZ DE SKETCHES del Reto 4: verifica que la validación acepte los tipos de código que un
/// Técnico real puede escribir — no solo el blink clásico con LED. Cada caso arma el circuito
/// que corresponde a SU sketch (con o sin LED, una o varias ramas) y espera success=true:
///
///   1. HIGH fijo + SOLO resistencia (corriente continua, sin LED)          — el caso reportado.
///   2. Multi-pin: dos digitalWrite a ramas de resistencia DISTINTA (220/1k) — sin LED.
///   3. PWM con expresión matemática (analogWrite + map())                   — sin LED.
///   4. Blink con delay() + LED                                              — el clásico.
///   5. Función de usuario + for                                             — sin LED.
///   6. Mixto: un pin con LED+R y otro pin solo-R, ambos HIGH.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4SketchMatrixTest.Run -logFile
/// </summary>
public static class Reto4SketchMatrixTest
{
    class Rama { public int pin; public float r; public bool led; }

    [MenuItem("Tools/TITA/Reto 4/Matriz de sketches (headless)")]
    public static void Run()
    {
        bool ok = true;

        ok &= Caso("1. HIGH fijo solo-R",
            "void setup() { pinMode(4, OUTPUT); } void loop() { digitalWrite(4, HIGH); }",
            new List<Rama> { new Rama { pin = 4, r = 330f, led = false } });

        ok &= Caso("2. Multi-pin solo-R (R distintas)",
            "void setup() { pinMode(3, OUTPUT); pinMode(5, OUTPUT); } " +
            "void loop() { digitalWrite(3, HIGH); digitalWrite(5, HIGH); }",
            new List<Rama> { new Rama { pin = 3, r = 220f, led = false },
                             new Rama { pin = 5, r = 1000f, led = false } });

        ok &= Caso("3. PWM con expresion (map)",
            "void setup() { pinMode(6, OUTPUT); } " +
            "void loop() { int v = map(50, 0, 100, 0, 255); analogWrite(6, v); }",
            new List<Rama> { new Rama { pin = 6, r = 330f, led = false } });

        ok &= Caso("4. Blink con delay + LED",
            "void setup() { pinMode(9, OUTPUT); } " +
            "void loop() { digitalWrite(9, HIGH); delay(200); digitalWrite(9, LOW); delay(200); }",
            new List<Rama> { new Rama { pin = 9, r = 330f, led = true } });

        ok &= Caso("5. Funcion de usuario + for",
            "void prende(int p) { digitalWrite(p, HIGH); } " +
            "void setup() { pinMode(8, OUTPUT); } " +
            "void loop() { for (int i = 0; i < 2; i = i + 1) { prende(8); } }",
            new List<Rama> { new Rama { pin = 8, r = 470f, led = false } });

        ok &= Caso("6. Mixto LED + solo-R",
            "void setup() { pinMode(9, OUTPUT); pinMode(5, OUTPUT); } " +
            "void loop() { digitalWrite(9, HIGH); digitalWrite(5, HIGH); }",
            new List<Rama> { new Rama { pin = 9, r = 330f, led = true },
                             new Rama { pin = 5, r = 560f, led = false } });

        Debug.Log(ok
            ? "##MATRIZ## ===== RESULTADO: ✓ los 6 tipos de sketch validan ====="
            : "##MATRIZ## ===== RESULTADO: ✗ hay sketches que no validan =====");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Caso(string nombre, string sketch, List<Rama> ramas)
    {
        var r = Validar(sketch, ramas);
        Debug.Log($"##MATRIZ## {nombre}: success={r.success} msg=\"{r.message}\"");
        if (!r.success) Debug.LogError($"##MATRIZ## ✗ FALLA: {nombre}");
        return r.success;
    }

    static SandboxValidationResult Validar(string sketch, List<Rama> ramas)
    {
        var root = new GameObject("MatrixRig");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var gnd = NewNode(root.transform, "GND");
        var goArduino = new GameObject("ArduinoMatrix");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND = gnd;
        core.outputVoltageTTL = 5f;

        foreach (var rama in ramas)
        {
            var pinNode = NewNode(root.transform, $"Pin{rama.pin}");
            core.RegisterPinNode(rama.pin, pinNode);

            var res = NewComp<Resistor>(root.transform, $"R{rama.pin}");
            res.resistance = rama.r;
            res.nodeA = pinNode;

            if (rama.led)
            {
                var mid = NewNode(root.transform, $"Mid{rama.pin}");
                res.nodeB = mid;
                var led = NewComp<LED>(root.transform, $"LED{rama.pin}");
                led.forwardVoltage = 2f; led.resistance = 50f; led.maxSafeCurrent = 0.02f;
                led.nodeA = mid; led.nodeB = gnd;
            }
            else
            {
                res.nodeB = gnd;
            }
        }

        core.LoadSketchProgram(sketch);
        if (!core.ProgramRunning)
        {
            Object.DestroyImmediate(root);
            return new SandboxValidationResult { success = false, message = "El sketch no compiló." };
        }

        var interpField = typeof(ArduinoCore).GetField("_interp", BindingFlags.NonPublic | BindingFlags.Instance);
        var interp = (ArduinoInterpreter)interpField.GetValue(core);
        Drain(interp.RunSetup());

        // Ejecutar loop() validando ENTRE cada suspensión (delay): emula la validación real a
        // 20 Hz — un blink pasa por fase ON y OFF; el juego (y el auto-completar) capturan la
        // fase en que el circuito cumple. Sin esto, drenar el loop entero podía terminar justo
        // en fase LOW y "fallar" un blink perfectamente válido (artefacto del test, no del juego).
        var resultField = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        SandboxValidationResult result = default;
        int pasos = 0;
        var en = interp.RunLoop().GetEnumerator();
        while (true)
        {
            bool hay = en.MoveNext();
            protoSim.ForzarValidacion();
            result = (SandboxValidationResult)resultField.GetValue(protoSim);
            if (result.success || !hay || ++pasos >= 60) break;
        }

        Object.DestroyImmediate(root);
        return result;
    }

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
