using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// "Juego completo" — corre los 4 retos EN SECUENCIA sobre la escena REAL (Explorador.unity),
/// dos veces (Configuración A y B), variando el circuito de los Retos 2 y 4 entre ambas corridas
/// (Retos 1 y 3 usan el mismo valor objetivo real de la escena en las dos, tal como se pidió).
///
/// Usa las clases REALES del juego, no mocks:
///   - Retos 1/3: ComponentDeliverySystem.DebugSimularEntregaEInstalacion() — la misma ruta de
///     validación+reparación que usa el botón físico/F9 (ValidateValueForRepair → ApplyRepairToCircuit).
///   - Reto 2: mismo camino (LED por polaridad), + variación real de la resistencia de protección
///     de la rama (Circuit_R1/R2) entre config A/B — el mecanismo de "colocar la pieza en VR" en sí
///     (Reto2CircuitGuard, grab+drop) es físico y no se puede accionar headless; se documenta como
///     limitación.
///   - Reto 4: mismo patrón que Reto4FiveCircuitsDemo/SecondBatchTest (circuito sintético real:
///     ArduinoCore+ArduinoInterpreter+ProtoboardSimulator.ForzarValidacion, la misma llamada que el
///     botón físico), con 2 circuitos y códigos C++ distintos.
///
/// GameManager.LoadLevel es privado → se invoca por reflexión (activa/desactiva zonas real).
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod FullGameTwoConfigsTest.Run -logFile -
/// </summary>
public static class FullGameTwoConfigsTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Juego completo - 2 configuraciones (headless)")]
    public static void Run()
    {
        var resultadosA = RunConfig("A", brancheR2: 470f, ledColorReto2: "Verde (default)");
        var resultadosB = RunConfig("B", brancheR2: 330f, ledColorReto2: "Amarillo (protección distinta)");

        Debug.Log("===== RESUMEN FINAL =====");
        foreach (var r in resultadosA) Debug.Log($"##RESUMEN## config=A {r}");
        foreach (var r in resultadosB) Debug.Log($"##RESUMEN## config=B {r}");

        bool allOk = resultadosA.All(r => r.EndsWith("OK")) && resultadosB.All(r => r.EndsWith("OK"));
        if (Application.isBatchMode) EditorApplication.Exit(allOk ? 0 : 1);
    }

    static List<string> RunConfig(string configLabel, float brancheR2, string ledColorReto2)
    {
        var resultados = new List<string>();
        Debug.Log($"\n===== CONFIGURACIÓN {configLabel} =====");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();
        var multimetro = Object.FindAnyObjectByType<Multimeter>();
        if (gm == null || delivery == null)
        {
            Debug.LogError($"[FullGame] Config {configLabel}: falta GameManager o ComponentDeliverySystem.");
            resultados.Add("SETUP=FALLO");
            return resultados;
        }
        if (multimetro == null)
            Debug.LogWarning($"[FullGame] Config {configLabel}: no encontré Multimeter en la escena — se omite su verificación.");

        bool ultimoResultado = false;
        System.Action<LevelType, bool> onCompleted = (lvl, ok) => { ultimoResultado = ok; };
        GameManager.OnLevelCompleted += onCompleted;

        try
        {
            // Abrir la escena vía OpenScene NO llama Awake()/OnEnable() de los objetos YA GUARDADOS
            // en ella (solo pasa al entrar Play Mode) — a diferencia de AddComponent() en runtime,
            // que sí dispara Awake al instante. Sin esto, CircuitManager.components queda vacío
            // (AutoDetectComponents nunca corrió) y Multimeter._indicatorMpb queda null. Forzamos
            // ambos manualmente, como lo haría el motor real al arrancar.
            foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cm.AutoDetectComponents();
            if (multimetro != null)
                multimetro.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            // GameManager.Start() (NO OnEnable — ojo, ya me equivoqué una vez) suscribe
            // CircuitManager.OnCircuitChanged → OnCircuitChangedAutoCheck (el chequeo de victoria
            // automático). Sin esto, ForceSimulate() sí recalcula el circuito pero NADIE evalúa si
            // ya ganó — CompleteLevel nunca se llama. Start() también llama LoadLevel(0) al final
            // si no hay ExplorerOnboarding, así que este SendMessage YA carga el Reto 1 — el
            // InvokeLoadLevel(gm,0) de abajo lo vuelve a cargar limpio, no hay problema.
            gm.SendMessage("Start", SendMessageOptions.DontRequireReceiver);

            // ── Reto 1 (OhmLaw) — resistor 850Ω (correctResistance real de la escena) ──
            InvokeLoadLevel(gm, 0);
            // El Reto 1 tiene un CircuitSwitch (Switch_Series) que el Explorador debe cerrar en VR
            // (isOn=false por defecto → 1MΩ, circuito abierto). Sin esto el resistor mide ~0V aunque
            // esté reparado. Lo cerramos aquí simulando esa acción física del Explorador.
            var switchReto1 = Object.FindObjectsByType<CircuitSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault();
            if (switchReto1 != null) { switchReto1.isOn = true; }
            ultimoResultado = false;
            bool r1entregado = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, 850f);
            bool r1victoria = ChequearVictoriaDirecta(gm);
            string multi1 = ProbarMultimetro(multimetro, "Resistor_Faulty");
            resultados.Add($"Reto1(OhmLaw) R=850ohm entregaValida={r1entregado} completado(evento)={ultimoResultado} " +
                           $"completado(directo)={r1victoria} multimetro=[{multi1}] " +
                           (r1entregado && r1victoria ? "OK" : "FALLO"));

            // ── Reto 2 (Parallel) — LED2 dañado (polaridad) + resistencia de rama variable ──
            InvokeLoadLevel(gm, 1);
            var todosLosLED = Object.FindObjectsByType<LED>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[FullGame] Config {configLabel} tras LoadLevel(1): {todosLosLED.Length} LED en escena: " +
                      string.Join(", ", todosLosLED.Select(l => $"\"{l.name}\"(activo={l.gameObject.activeInHierarchy},invertido={l.polarityInverted})")));
            // El Reto 2 usa Protoboard_Reto2 (ProtoboardSimulator) para cablear nodeA/nodeB de sus
            // piezas (ProtoboardConnector.lockNodes por railId) — CircuitManager de esa zona DEFIERE
            // a él (_deferToProtoboard) y no hace nada. Sin correr su ForzarValidacion(), Circuit_LED2
            // nunca tiene nodeA/nodeB asignados y toda búsqueda "wired" lo ignora.
            //
            // ProtoboardConnector.Active (lista estática que BindConnectors usa para saber qué piezas
            // cablear) solo se llena en OnEnable(). Los conectores YA GUARDADOS en la escena (Circuit_
            // LED2, lockNodes=true→COL_1C/GND) nunca corrieron su OnEnable. IMPORTANTE: hay que forzarlo
            // DESPUÉS de activar reto2Zone (InvokeLoadLevel arriba) — antes su GameObject seguía
            // inactivo y SendMessage no hacía nada. Y TAMBIÉN Awake() ANTES de OnEnable (orden real de
            // Unity): Bind() empieza con "if (_comp == null) return" y _comp se asigna en Awake(), no
            // en OnEnable — sin este paso Bind() salía en silencio sin hacer nada pese a estar en Active.
            foreach (var conn in Object.FindObjectsByType<ProtoboardConnector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                conn.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                conn.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
            }
            Debug.Log($"[FullGame] Config {configLabel} ProtoboardConnector.Active.Count tras forzar OnEnable={ProtoboardConnector.Active.Count}");

            // Fuente_9V (la batería del Reto 2) estaba activeInHierarchy=False según el diagnóstico —
            // sin ella activa, SimulateSingleSource no tiene fuente y todo el circuito queda en 0V.
            var fuenteReto2 = Object.FindObjectsByType<VoltageSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => v.gameObject.name == "Fuente_9V");
            if (fuenteReto2 != null) fuenteReto2.gameObject.SetActive(true);
            Debug.Log($"[FullGame] Config {configLabel} Fuente_9V encontrada={fuenteReto2 != null} " +
                      $"activeAhora={(fuenteReto2 != null ? fuenteReto2.gameObject.activeInHierarchy : (bool?)null)}");

            var protoReto2 = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.name == "Protoboard_Reto2");
            protoReto2?.ForzarValidacion();
            Debug.Log($"[FullGame] Config {configLabel} protoReto2 encontrado={protoReto2 != null}");

            // ÚLTIMO eslabón real (no un artefacto de testing): este Reto 2 es el diseño "Puzzle
            // REALISTA (6 cables)" documentado en Reto2ProtoboardSetup.cs — el jugador cablea 6
            // jumpers físicos: batería+→VCC, batería−→GND, VCC→COL_0A, COL_0B→COL_0C (R1→LED1),
            // VCC→COL_1A, COL_1B→COL_1C (R2→LED2). Sin esto, R1/R2/LED1/LED2 quedan en 0V aunque
            // la batería y las piezas estén bien — es gameplay real, no un bug. Se simulan los 6.
            if (protoReto2 != null && fuenteReto2 != null)
            {
                void Puentear(string nombre, ElectricalNode a, string railB)
                {
                    var nb = protoReto2.NodeForRail(railB);
                    if (a == null || nb == null)
                    {
                        Debug.LogWarning($"[FullGame] Config {configLabel} jumper '{nombre}' NO creado: a={a != null} railB('{railB}')={nb != null}");
                        return;
                    }
                    var j = new GameObject(nombre).AddComponent<Jumper>();
                    j.transform.SetParent(protoReto2.transform, false);
                    j.nodeA = a; j.nodeB = nb;
                }
                void PuentearRail(string nombre, string railA, string railB)
                    => Puentear(nombre, protoReto2.NodeForRail(railA), railB);

                Puentear("TestJumper_Bat_VCC", fuenteReto2.nodeA, "VCC");
                Puentear("TestJumper_Bat_GND", fuenteReto2.nodeB, "GND");
                PuentearRail("TestJumper_VCC_COL0A", "VCC", "COL_0A");
                PuentearRail("TestJumper_COL0B_COL0C", "COL_0B", "COL_0C");
                PuentearRail("TestJumper_VCC_COL1A", "VCC", "COL_1A");
                PuentearRail("TestJumper_COL1B_COL1C", "COL_1B", "COL_1C");

                Debug.Log($"[FullGame] Config {configLabel} 6 jumpers del puzzle realista creados. Re-simulando...");
                protoReto2.ForzarValidacion();
            }

            AjustarResistenciasReto2(brancheR2);
            ultimoResultado = false;
            bool r2entregado = delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
            // ResimularCircuitos() (dentro de DebugSimularEntregaEInstalacion) solo llama
            // CircuitManager.ForceSimulate() — el de esta zona DEFIERE (_deferToProtoboard) y no hace
            // nada. Sin re-correr el ProtoboardSimulator del Reto 2 después de arreglar la polaridad,
            // el LED queda simulado con su estado VIEJO (invertido) para siempre.
            protoReto2?.ForzarValidacion();
            bool r2victoria = ChequearVictoriaDirecta(gm);
            string multi2 = ProbarMultimetro(multimetro, "Circuit_LED2");
            resultados.Add($"Reto2(Parallel) ramaR={brancheR2}ohm LEDcolor=\"{ledColorReto2}\" " +
                           $"entregaValida={r2entregado} completado(evento)={ultimoResultado} " +
                           $"completado(directo)={r2victoria} multimetro=[{multi2}] " +
                           (r2entregado && r2victoria ? "OK" : "FALLO"));

            // ── Reto 3 (Mixed) — resistor 470Ω + LED polaridad + capacitor polaridad ──
            InvokeLoadLevel(gm, 2);
            ultimoResultado = false;
            bool r3a = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, 470f);
            bool r3b = delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
            bool r3c = delivery.DebugSimularEntregaEInstalacion(ComponentType.Capacitor, 1f);
            string multi3 = ProbarMultimetro(multimetro, "Resistor_Serie_Faulty");
            var ledReto3 = Object.FindObjectsByType<LED>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(l => l.name == "LED_Paralelo");
            var capReto3 = Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "Capacitor_Invertido");
            var rReto3 = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(r => r.name == "Resistor_Serie_Faulty");
            Debug.Log($"[FullGame] Config {configLabel} Reto3 estado real tras reparar: " +
                      $"R hasFault={rReto3?.hasFault} resistance={rReto3?.resistance} | " +
                      $"LED state={ledReto3?.state} isOn={ledReto3?.isOn} polInv={ledReto3?.polarityInverted} current={ledReto3?.current} | " +
                      $"CAP polInv={capReto3?.polarityInverted}");
            bool r3victoria = ChequearVictoriaDirecta(gm);
            resultados.Add($"Reto3(Mixed) R=470ohm+LEDpol+CAPpol entregasValidas=[{r3a},{r3b},{r3c}] " +
                           $"completado(evento)={ultimoResultado} completado(directo)={r3victoria} multimetro=[{multi3}] " +
                           (r3a && r3b && r3c && r3victoria ? "OK" : "FALLO"));

            // ── Reto 4 (Arduino) — circuito sintético real, distinto por config ──
            InvokeLoadLevel(gm, 3);
            var r4 = configLabel == "A" ? CorrerReto4CircuitoA() : CorrerReto4CircuitoB();
            resultados.Add($"Reto4(Arduino) circuito={(configLabel == "A" ? "simple 1 LED" : "3 LEDs+capacitor")} " +
                           $"completado={r4.success} multimetro=[{r4.multi}] " + (r4.success ? "OK" : "FALLO"));
        }
        finally
        {
            GameManager.OnLevelCompleted -= onCompleted;
        }

        return resultados;
    }

    /// <summary>
    /// Prueba el Multimeter REAL de la escena: pone las 2 puntas en los nodos del componente
    /// nombrado (nodeA=roja, nodeB=negra) e invoca TakeReading() (privado, driven normalmente por
    /// Update() — que no tickea en batch/edit mode) para leer V/I reales tal como lo haría el
    /// jugador tocando el circuito reparado.
    /// </summary>
    static string ProbarMultimetro(Multimeter multimetro, string nombreComponente)
    {
        if (multimetro == null) return "sin Multimeter en escena";

        ElectricalComponent comp = Object.FindObjectsByType<ElectricalComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(c => c != null && c.name == nombreComponente);
        if (comp == null || comp.nodeA == null || comp.nodeB == null)
            return $"componente '{nombreComponente}' no encontrado o sin nodos";

        multimetro.SetMode(MultimeterMode.DCVoltage);
        multimetro.SetProbeA(comp.nodeA);
        multimetro.SetProbeB(comp.nodeB);

        var takeReading = typeof(Multimeter).GetMethod("TakeReading", BindingFlags.NonPublic | BindingFlags.Instance);
        takeReading.Invoke(multimetro, null);

        return $"sobre '{nombreComponente}': V={multimetro.measuredVoltage:F2}V I={multimetro.measuredCurrent*1000f:F2}mA " +
               $"isReading={multimetro.isReading}";
    }

    static void AjustarResistenciasReto2(float valor)
    {
        var all = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in all)
            if (r != null && (r.name == "Circuit_R1" || r.name == "Circuit_R2"))
                r.resistance = valor;
    }

    static void InvokeLoadLevel(GameManager gm, int index)
    {
        var m = typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(gm, new object[] { index });
    }

    /// <summary>
    /// Chequea la condición de victoria REAL (CumpleVictoriaRetos123, privado, "SIN efectos
    /// secundarios" según su propio doc-comment) por reflexión, sin depender de que la cadena de
    /// eventos automática (CircuitManager.OnCircuitChanged → GameManager.OnCircuitChangedAutoCheck)
    /// haya quedado bien suscrita — que es justamente lo que está fallando en este arnés headless.
    /// </summary>
    static bool ChequearVictoriaDirecta(GameManager gm)
    {
        var m = typeof(GameManager).GetMethod("CumpleVictoriaRetos123", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)m.Invoke(gm, null);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Reto 4 — mismo patrón que Reto4FiveCircuitsDemo/SecondBatchTest
    // ═══════════════════════════════════════════════════════════════════

    static (bool success, string multi) CorrerReto4CircuitoA()
    {
        string sketch = @"
            void setup() { pinMode(9, OUTPUT); }
            void loop() { digitalWrite(9, HIGH); }";
        Debug.Log("##RETO4_CODIGO_A##\n" + sketch);
        var r = RunReto4Branches(new List<(int pin, float r, float vf, string color)> {
            (9, 330f, 2.0f, "Verde")
        }, capacitorEnBranch: -1, sketch);
        Debug.Log($"##RETO4_RESULT_A## success={r.success} message=\"{r.message}\"");
        // El "multimetro" del Reto 4 = la telemetría real del sandbox (I medido por el propio
        // ProtoboardSimulator, mismo valor que vería el Técnico en el HUD): no hay un par de nodos
        // fijo para sondear como en Retos 1-3 (el circuito lo arma libremente el jugador).
        return (r.success, $"I≈{r.currentMa:F2}mA (telemetría real del sandbox) — \"{r.message}\"");
    }

    static (bool success, string multi) CorrerReto4CircuitoB()
    {
        string sketch = @"
            void setup() { pinMode(9, OUTPUT); pinMode(6, OUTPUT); pinMode(11, OUTPUT); }
            void loop() { digitalWrite(9, HIGH); digitalWrite(6, HIGH); digitalWrite(11, HIGH); }";
        Debug.Log("##RETO4_CODIGO_B##\n" + sketch);
        var r = RunReto4Branches(new List<(int pin, float r, float vf, string color)> {
            (9, 330f, 2.0f, "Verde"),
            (6, 270f, 1.8f, "Rojo"),
            (11, 390f, 2.1f, "Ámbar"),
        }, capacitorEnBranch: 0, sketch);
        Debug.Log($"##RETO4_RESULT_B## success={r.success} message=\"{r.message}\"");
        return (r.success, $"I≈{r.currentMa:F2}mA (telemetría real del sandbox) — \"{r.message}\"");
    }

    static SandboxValidationResult RunReto4Branches(
        List<(int pin, float r, float vf, string color)> branches, int capacitorEnBranch, string sketch)
    {
        var root = new GameObject("FullGameReto4Test");
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
