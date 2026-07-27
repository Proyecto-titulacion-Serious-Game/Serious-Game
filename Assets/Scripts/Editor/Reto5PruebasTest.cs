using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// "5 pruebas por reto" (20 escenarios) sobre la escena REAL, con diagnóstico rico
/// (DiagnosticSystem) + multímetro real por escenario. Reto 1/3 varían el valor de resistencia
/// contra el objetivo real; Reto 2 varía la completitud del cableado (puzzle de 6 cables); Reto 4
/// varía circuito+código Arduino (patrón sintético ya usado en FullGameTwoConfigsTest).
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto5PruebasTest.Run -logFile -
/// </summary>
public static class Reto5PruebasTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    static readonly DiagnosticSystem Diag = new DiagnosticSystem();

    [MenuItem("Tools/TITA/5 pruebas por reto - 20 escenarios (headless)")]
    public static void Run()
    {
        var all = new List<string>();
        all.AddRange(RunReto1());
        all.AddRange(RunReto3());
        all.AddRange(RunReto2());
        all.AddRange(RunReto4());

        Debug.Log("===== RESUMEN 20 ESCENARIOS =====");
        foreach (var r in all) Debug.Log("##P5## " + r);

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RETO 1 — 5 valores de resistencia (objetivo real 850 Ohm)
    // ═══════════════════════════════════════════════════════════════════
    static List<string> RunReto1()
    {
        var res = new List<string>();
        var vals = new (float ohm, string tag)[] {
            (500f,  "muy_bajo_sobrecarga"),
            (748f,  "borde_inferior_tolerancia"),
            (850f,  "exacto_correcto"),
            (950f,  "borde_superior_tolerancia"),
            (1200f, "muy_alto_no_enciende"),
        };

        // Escena FRESCA por valor: una vez que el resistor se repara con éxito (hasFault=false),
        // ComponentDeliverySystem.ValidateValueForRepair ya NO vuelve a validar/aplicar valores
        // nuevos (BuscarResistorDelReto no encuentra nada "con falla" → auto-pasa sin tocar nada).
        // Reusar la misma sesión de escena para los 5 valores hacía que solo el PRIMERO se aplicara
        // de verdad — bug real descubierto al correr esto la primera vez.
        int i = 1;
        foreach (var (ohm, tag) in vals)
        {
            OpenAndBoot();
            var gm = Object.FindAnyObjectByType<GameManager>();
            var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();
            var multimetro = Object.FindAnyObjectByType<Multimeter>();

            InvokeLoadLevel(gm, 0);
            var sw = Object.FindObjectsByType<CircuitSwitch>(FindObjectsInactive.Include).FirstOrDefault();
            if (sw != null) sw.isOn = true;

            bool entregado = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, ohm);
            bool victoria = ChequearVictoriaDirecta(gm);
            string diag = Diag.GetDiagnosisOhmLaw() + "\n> " + Diag.GetNextActionOhmLaw();
            string multi = ProbarMultimetro(multimetro, "Resistor_Faulty");
            res.Add($"Reto1#{i} tag={tag} R={ohm}ohm entregaValida={entregado} completado={victoria} " +
                    $"multimetro=[{multi}]\nDIAG:\n{diag}\n{(victoria ? "OK" : "ESPERADO_NO_COMPLETAR")}");
            i++;
        }
        return res;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RETO 3 — 5 valores de resistencia (objetivo real 470 Ohm), LED/CAP OK
    // ═══════════════════════════════════════════════════════════════════
    static List<string> RunReto3()
    {
        var res = new List<string>();
        var vals = new (float ohm, string tag)[] {
            (100f, "muy_bajo"),
            (430f, "cerca_por_debajo"),
            (470f, "exacto_correcto"),
            (510f, "cerca_por_encima"),
            (2200f, "valor_averiado_original"),
        };

        int i = 1;
        foreach (var (ohm, tag) in vals)
        {
            OpenAndBoot();
            var gm = Object.FindAnyObjectByType<GameManager>();
            var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();
            var multimetro = Object.FindAnyObjectByType<Multimeter>();

            InvokeLoadLevel(gm, 2);
            // Polaridad de LED y capacitor correctas siempre, para aislar el efecto de la resistencia.
            delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
            delivery.DebugSimularEntregaEInstalacion(ComponentType.Capacitor, 1f);

            bool entregado = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, ohm);
            bool victoria = ChequearVictoriaDirecta(gm);
            string diag = Diag.GetDiagnosisMixed() + "\n> " + Diag.GetNextActionMixed();
            string multi = ProbarMultimetro(multimetro, "Resistor_Serie_Faulty");
            res.Add($"Reto3#{i} tag={tag} R={ohm}ohm entregaValida={entregado} completado={victoria} " +
                    $"multimetro=[{multi}]\nDIAG:\n{diag}\n{(victoria ? "OK" : "ESPERADO_NO_COMPLETAR")}");
            i++;
        }
        return res;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RETO 2 — 5 alternativas de cableado (puzzle realista de 6 cables)
    // ═══════════════════════════════════════════════════════════════════
    static List<string> RunReto2()
    {
        var res = new List<string>();
        var variantes = new (string tag, bool batVcc, bool batGnd, bool r1Jumper, bool r2Jumper, bool repararLed2)[] {
            ("completo_correcto",        true,  true,  true,  true,  true),
            ("falta_cable_bateria",      false, true,  true,  true,  true),
            ("falta_cable_rama1",        true,  true,  false, true,  true),
            ("faltan_ambas_ramas",       true,  true,  false, false, true),
            ("completo_pero_led_invertido", true, true, true, true,  false),
        };

        int i = 1;
        foreach (var v in variantes)
        {
            OpenAndBoot();
            var gm = Object.FindAnyObjectByType<GameManager>();
            var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();
            var multimetro = Object.FindAnyObjectByType<Multimeter>();

            InvokeLoadLevel(gm, 1);
            foreach (var conn in Object.FindObjectsByType<ProtoboardConnector>(FindObjectsInactive.Include))
            {
                conn.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                conn.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
            }
            var fuente = Object.FindObjectsByType<VoltageSource>(FindObjectsInactive.Include)
                .FirstOrDefault(x => x.gameObject.name == "Fuente_9V");
            if (fuente != null) fuente.gameObject.SetActive(true);
            var proto = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include)
                .FirstOrDefault(p => p.gameObject.name == "Protoboard_Reto2");
            proto?.ForzarValidacion();

            var cables = new List<string>();
            if (proto != null && fuente != null)
            {
                void Puentear(string nombre, ElectricalNode a, string railB)
                {
                    var nb = proto.NodeForRail(railB);
                    if (a == null || nb == null) return;
                    var j = new GameObject(nombre).AddComponent<Jumper>();
                    j.transform.SetParent(proto.transform, false);
                    j.nodeA = a; j.nodeB = nb;
                }
                void PuentearRail(string nombre, string railA, string railB)
                    => Puentear(nombre, proto.NodeForRail(railA), railB);

                if (v.batVcc)    { Puentear("J_Bat_VCC", fuente.nodeA, "VCC"); cables.Add("Bateria(+) -> VCC"); }
                if (v.batGnd)    { Puentear("J_Bat_GND", fuente.nodeB, "GND"); cables.Add("Bateria(-) -> GND"); }
                if (v.r1Jumper)  { PuentearRail("J_VCC_C0A", "VCC", "COL_0A"); PuentearRail("J_C0B_C0C", "COL_0B", "COL_0C"); cables.Add("VCC->COL_0A, COL_0B->COL_0C (Rama 1)"); }
                if (v.r2Jumper)  { PuentearRail("J_VCC_C1A", "VCC", "COL_1A"); PuentearRail("J_C1B_C1C", "COL_1B", "COL_1C"); cables.Add("VCC->COL_1A, COL_1B->COL_1C (Rama 2)"); }
                proto.ForzarValidacion();
            }

            bool r2entregado = true;
            if (v.repararLed2)
                r2entregado = delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
            proto?.ForzarValidacion();

            bool victoria = ChequearVictoriaDirecta(gm);
            string diag = Diag.GetDiagnosisParallel() + "\n> " + Diag.GetNextActionParallel();
            string multi = ProbarMultimetro(multimetro, "Circuit_LED2");
            res.Add($"Reto2#{i} tag={v.tag} cablesConectados=[{string.Join(" | ", cables)}] " +
                    $"led2Reparado={v.repararLed2} entregaValida={r2entregado} completado={victoria} " +
                    $"multimetro=[{multi}]\nDIAG:\n{diag}\n{(victoria == (v.tag == "completo_correcto") ? "OK" : "REVISAR")}");
            i++;
        }
        return res;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RETO 4 — 5 combinaciones circuito + código Arduino (sintético)
    // ═══════════════════════════════════════════════════════════════════
    static List<string> RunReto4()
    {
        var res = new List<string>();

        // 1) 1 LED simple correcto
        res.Add(CorrerReto4("1", "led_simple_correcto",
            "void setup() { pinMode(9, OUTPUT); }\nvoid loop() { digitalWrite(9, HIGH); }",
            new List<(int, float, float, string)> { (9, 330f, 2.0f, "Verde") }, -1));

        // 2) Resistencia muy baja -> sobrecarga
        res.Add(CorrerReto4("2", "resistencia_muy_baja_sobrecarga",
            "void setup() { pinMode(9, OUTPUT); }\nvoid loop() { digitalWrite(9, HIGH); }",
            new List<(int, float, float, string)> { (9, 47f, 2.0f, "Verde") }, -1));

        // 3) Resistencia muy alta -> no enciende / corriente insuficiente
        res.Add(CorrerReto4("3", "resistencia_muy_alta_no_enciende",
            "void setup() { pinMode(9, OUTPUT); }\nvoid loop() { digitalWrite(9, HIGH); }",
            new List<(int, float, float, string)> { (9, 4700f, 2.0f, "Verde") }, -1));

        // 4) 3 LEDs + capacitor
        res.Add(CorrerReto4("4", "tres_leds_mas_capacitor",
            "void setup() { pinMode(9, OUTPUT); pinMode(6, OUTPUT); pinMode(11, OUTPUT); }\n" +
            "void loop() { digitalWrite(9, HIGH); digitalWrite(6, HIGH); digitalWrite(11, HIGH); }",
            new List<(int, float, float, string)> {
                (9, 330f, 2.0f, "Verde"), (6, 270f, 1.8f, "Rojo"), (11, 390f, 2.1f, "Ambar")
            }, 0));

        // 5) Sin resistencia -> cortocircuito directo
        res.Add(CorrerReto4("5", "sin_resistencia_cortocircuito",
            "void setup() { pinMode(9, OUTPUT); }\nvoid loop() { digitalWrite(9, HIGH); }",
            new List<(int, float, float, string)> { (9, 0.01f, 2.0f, "Verde") }, -1));

        return res;
    }

    static string CorrerReto4(string idx, string tag, string sketch,
        List<(int pin, float r, float vf, string color)> branches, int capBranch)
    {
        Debug.Log($"##RETO4_CODIGO_{idx}##\n{sketch}");
        var r = RunReto4Branches(branches, capBranch, sketch);
        string motivo = "";
        try
        {
            var motivoEnum = Reto4Feedback.Clasificar(r);
            motivo = r.success ? "" : ("\n> " + Reto4Feedback.Construir(3, r.activatedPin, motivoEnum));
        }
        catch { /* Clasificar/Construir pueden requerir estado no disponible fuera de escena real */ }

        return $"Reto4#{idx} tag={tag} pines=[{string.Join(",", branches.Select(b => $"D{b.pin}={b.r}ohm"))}] " +
               $"completado={r.success} I≈{r.currentMa:F2}mA\nCODIGO:\n{sketch}\nDIAG:\n{r.message}{motivo}\n" +
               (r.success ? "OK" : "FALLO_ESPERADO_O_REAL");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers compartidos (mismo patrón que FullGameTwoConfigsTest.cs)
    // ═══════════════════════════════════════════════════════════════════

    static Scene OpenAndBoot()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gm = Object.FindAnyObjectByType<GameManager>();
        var multimetro = Object.FindAnyObjectByType<Multimeter>();

        foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Include))
            cm.AutoDetectComponents();
        if (multimetro != null)
            multimetro.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
        gm.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        return scene;
    }

    static string ProbarMultimetro(Multimeter multimetro, string nombreComponente)
    {
        if (multimetro == null) return "sin Multimeter en escena";
        ElectricalComponent comp = Object.FindObjectsByType<ElectricalComponent>(FindObjectsInactive.Include)
            .FirstOrDefault(c => c != null && c.name == nombreComponente);
        if (comp == null || comp.nodeA == null || comp.nodeB == null)
            return $"componente '{nombreComponente}' no encontrado o sin nodos";

        multimetro.SetMode(MultimeterMode.DCVoltage);
        multimetro.SetProbeA(comp.nodeA);
        multimetro.SetProbeB(comp.nodeB);
        var takeReading = typeof(Multimeter).GetMethod("TakeReading", BindingFlags.NonPublic | BindingFlags.Instance);
        takeReading.Invoke(multimetro, null);

        return $"modo=DCVoltage sobre '{nombreComponente}': V={multimetro.measuredVoltage:F2}V " +
               $"I={multimetro.measuredCurrent * 1000f:F2}mA isReading={multimetro.isReading}";
    }

    static void InvokeLoadLevel(GameManager gm, int index)
    {
        var m = typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(gm, new object[] { index });
    }

    static bool ChequearVictoriaDirecta(GameManager gm)
    {
        var m = typeof(GameManager).GetMethod("CumpleVictoriaRetos123", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)m.Invoke(gm, null);
    }

    static SandboxValidationResult RunReto4Branches(
        List<(int pin, float r, float vf, string color)> branches, int capacitorEnBranch, string sketch)
    {
        var root = new GameObject("P5Reto4Test");
        root.SetActive(false);
        var protoSim = root.AddComponent<ProtoboardSimulator>();

        var gnd = NewNode(root.transform, "GND");
        var goArduino = new GameObject("ArduinoTest");
        goArduino.transform.SetParent(root.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND = gnd;
        core.outputVoltageTTL = 5f;

        for (int i = 0; i < branches.Count; i++)
        {
            var (pin, res, vf, color) = branches[i];
            var pinNode = NewNode(root.transform, $"Pin{pin}");
            core.RegisterPinNode(pin, pinNode);

            var mid = NewNode(root.transform, $"Mid{pin}");
            var r = NewComp<Resistor>(root.transform, $"R{pin}");
            r.resistance = res; r.nodeA = pinNode; r.nodeB = mid;

            var led = NewComp<LED>(root.transform, $"LED{pin}");
            led.forwardVoltage = vf; led.resistance = 50f; led.maxSafeCurrent = 0.02f;
            led.nodeA = mid; led.nodeB = gnd;

            if (capacitorEnBranch == i)
            {
                var cap = NewComp<Capacitor>(root.transform, $"Cap{pin}");
                cap.capacitance = 0.0001f;
                cap.nodeA = mid; cap.nodeB = gnd;
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
        Drain(interp.RunLoop());

        protoSim.ForzarValidacion();

        var resultField = typeof(ProtoboardSimulator).GetField("_lastSandboxResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (SandboxValidationResult)resultField.GetValue(protoSim);

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
