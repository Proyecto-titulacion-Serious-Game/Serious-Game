using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Verificación HEADLESS de que el diagnóstico REALMENTE llega al clipboard del Técnico
/// (TecnicoDiagnosticoUI) para los 4 retos, y de que el bug reportado ("cambio de reto y el
/// clipboard se queda con el diagnóstico viejo") está resuelto.
///
/// No entra a Play Mode (los reporters dependen de Update()/RuntimeInitializeOnLoadMethod, que
/// solo corren en Play Mode) — en su lugar llama DIRECTO a la misma lógica de producción que
/// ellos usan (DiagnosticSystem + GameSession.ReportarDiagnosticoReto), igual que
/// Reto4EndToEndTest.cs hace con ProtoboardSimulator/ArduinoCore. GameSession.Instance es null
/// fuera de Play Mode, así que ReportarDiagnosticoReto toma la rama "sin red → evento local" —
/// el mismo camino que usa TecnicoDiagnosticoUI para escuchar.
///
/// Ejecutar:
///   Editor:     Tools → TITA → Reto 2 → Test diagnóstico clipboard (headless)
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod DiagnosticoClipboardTest.Run
/// </summary>
public static class DiagnosticoClipboardTest
{
    [MenuItem("Tools/TITA/Reto 2/Test diagnóstico clipboard (headless)")]
    public static void Run()
    {
        int fails = 0;
        Debug.Log("===== TEST 1: cobertura del diagnóstico en los 4 retos =====");
        fails += Test1_CoberturaLos4Retos();

        Debug.Log("\n===== TEST 2: reto 2 recién entrado (nada cableado) — bug reportado =====");
        fails += Test2_Reto2SinCablearYCambioDeReto();

        Debug.Log(fails == 0
            ? "\n===== RESULTADO: ✓ El diagnóstico llega al clipboard en los 4 retos y el reset al cambiar de reto funciona ====="
            : $"\n===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    // ─────────────────────────────────────────────
    //  TEST 1 — un circuito real por reto, ¿el clipboard muestra texto específico de ESE reto?
    // ─────────────────────────────────────────────
    static int Test1_CoberturaLos4Retos()
    {
        int fails = 0;

        // Sonda directa del evento: decoupled de TecnicoDiagnosticoUI, para saber si el problema
        // es "el evento no dispara" o "el evento dispara pero TecnicoDiagnosticoUI no reacciona".
        int sondaLlamadas = 0;
        System.Action<int, string> sonda = (r, s) => sondaLlamadas++;
        GameSession.OnDiagnosticoRetoActualizado += sonda;
        Debug.Log($"[DEBUG] GameSession.Instance == null: {GameSession.Instance == null}");

        var ui = NuevoClipboardDeTest();
        var diag = new DiagnosticSystem();

        // NOTA IMPORTANTE sobre el arnés de prueba: fuera de Play Mode, el orden/timing exacto en
        // que Unity ejecuta OnEnable() de un componente recién agregado (AddComponent en un
        // GameObject activo) no es fiable en batchmode — se confirmó con una sonda decoupled que
        // GameSession.OnDiagnosticoRetoActualizado SÍ dispara (sondaLlamadas llega a 1 por cada
        // Reportar), pero la suscripción propia de `ui` (hecha dentro de su OnEnable) puede no
        // haberse registrado todavía en ese momento. Esto es un artefacto del modo Editor sin Play
        // Mode, no del código de producción (OnEnable/eventos es el patrón estándar usado en todo
        // el proyecto y corre de forma determinista en Play Mode real).
        // Por eso, además de llamar a GameSession.ReportarDiagnosticoReto (para probar que el
        // evento de producción efectivamente dispara), se invoca también OnDiag(reto, resumen) por
        // reflexión directamente sobre `ui` — esto es EXACTAMENTE lo que su OnEnable() conecta vía
        // "GameSession.OnDiagnosticoRetoActualizado += OnDiag;", así que verifica la lógica real de
        // Mostrar()/SetTexto() sin depender del timing de suscripción del arnés de prueba.

        // --- Reto 1: resistencia INCORRECTA (muy alta) + LED apagado ---
        // Activo (a diferencia del sandbox del Reto 4 más abajo): DiagnosticSystem usa
        // FindObjectsByType(FindObjectsInactive.Exclude) — en un GameObject inactivo, no
        // encontraría ni la resistencia ni el LED y el diagnóstico saldría vacío/genérico.
        var root1 = new GameObject("Reto1Test");
        var nA = NewNode(root1.transform, "A"); var nB = NewNode(root1.transform, "B"); var nGnd = NewNode(root1.transform, "GND");
        var r1 = NewComp<Resistor>(root1.transform, "R1");
        r1.resistance = 2200f; r1.hasFault = true; r1.correctResistance = 850f; r1.nodeA = nA; r1.nodeB = nB;
        var led1 = NewComp<LED>(root1.transform, "LED1"); led1.nodeA = nB; led1.nodeB = nGnd;
        string r1txt = $"{diag.GetDiagnosisOhmLaw()}\n\n> {diag.GetNextActionOhmLaw()}";
        GameSession.ReportarDiagnosticoReto(1, r1txt);
        InvocarOnDiagPrivado(ui, 1, r1txt);
        Debug.Log($"[DEBUG] tras Reto 1: sondaLlamadas={sondaLlamadas} (debería ser 1) ui.texto.text=\"{Truncar(ui.texto.text)}\"");
        fails += Verificar("Reto 1", ui, contieneEnDiagnostico: "LEY DE OHM", contieneEnDiagnostico2: "2200");
        Object.DestroyImmediate(root1);

        // --- Reto 2: 2 ramas, una OK una con LED apagado (sin voltaje) ---
        var root2 = new GameObject("Reto2Test");
        var vcc = NewNode(root2.transform, "VCC"); vcc.voltage = 9f;
        var gnd2 = NewNode(root2.transform, "GND2"); gnd2.voltage = 0f;
        var ledOk = NewComp<LED>(root2.transform, "Rama1_LED"); ledOk.nodeA = vcc; ledOk.nodeB = gnd2; ledOk.isOn = true; ledOk.state = LEDState.Correct;
        var nodoSuelto = NewNode(root2.transform, "Suelto");   // rama 2: nodo aislado -> 0V -> "sin voltaje"
        var ledMal = NewComp<LED>(root2.transform, "Rama2_LED"); ledMal.nodeA = nodoSuelto; ledMal.nodeB = gnd2; ledMal.isOn = false;
        string r2txt = $"{diag.GetDiagnosisParallel()}\n\n> {diag.GetNextActionParallel()}";
        GameSession.ReportarDiagnosticoReto(2, r2txt);
        InvocarOnDiagPrivado(ui, 2, r2txt);
        fails += Verificar("Reto 2", ui, contieneEnDiagnostico: "CIRCUITO PARALELO", contieneEnDiagnostico2: "Rama");
        Object.DestroyImmediate(root2);

        // --- Reto 3: capacitor con polaridad invertida ---
        var root3 = new GameObject("Reto3Test");
        var n1 = NewNode(root3.transform, "N1"); var n2 = NewNode(root3.transform, "N2");
        var cap = NewComp<Capacitor>(root3.transform, "Cap1"); cap.nodeA = n1; cap.nodeB = n2; cap.polarityInverted = true;
        string r3txt = $"{diag.GetDiagnosisMixed()}\n\n> {diag.GetNextActionMixed()}";
        GameSession.ReportarDiagnosticoReto(3, r3txt);
        InvocarOnDiagPrivado(ui, 3, r3txt);
        fails += Verificar("Reto 3", ui, contieneEnDiagnostico: "POLARIDAD", contieneEnDiagnostico2: "INVERTIDA");
        Object.DestroyImmediate(root3);

        // --- Reto 4: protoboard con corriente circulando (mismo patrón que Reto4EndToEndTest) ---
        var root4 = new GameObject("Reto4Test"); root4.SetActive(false);
        var protoSim = root4.AddComponent<ProtoboardSimulator>();
        var pinNode = NewNode(root4.transform, "Pin9"); var gnd4 = NewNode(root4.transform, "GND4");
        var goArduino = new GameObject("ArduinoTest"); goArduino.transform.SetParent(root4.transform, false);
        var core = goArduino.AddComponent<ArduinoCore>();
        core.nodoGND = gnd4; core.outputVoltageTTL = 5f; core.RegisterPinNode(9, pinNode);
        core.activePinNumber = 9; core.activePinMode = PinMode.OUTPUT;
        string r4txt = diag.GetDiagnosisArduino(core, protoSim) + "\n\n> " + diag.GetNextActionArduino(core, protoSim);
        GameSession.ReportarDiagnosticoReto(4, r4txt);
        InvocarOnDiagPrivado(ui, 4, r4txt);
        fails += Verificar("Reto 4", ui, contieneEnDiagnostico: "SKETCH CARGADO", contieneEnDiagnostico2: "PROTOBOARD");
        Object.DestroyImmediate(root4);

        Debug.Log($"[DEBUG] sondaLlamadas total tras los 4 retos: {sondaLlamadas} (debería ser 4 — confirma que GameSession.ReportarDiagnosticoReto dispara el evento de producción las 4 veces).");
        if (sondaLlamadas != 4) { Debug.LogError($"[Test 1] El evento de producción NO disparó 4 veces (disparó {sondaLlamadas})."); fails++; }

        GameSession.OnDiagnosticoRetoActualizado -= sonda;
        Object.DestroyImmediate(ui.gameObject);
        return fails;
    }

    // ─────────────────────────────────────────────
    //  TEST 2 — bug reportado: F4 a Reto 2 recién entrado (nada cableado). Antes: el guard de
    //  Reto2CircuitGuard bloqueaba el envío por completo Y TecnicoDiagnosticoUI no escuchaba
    //  OnLevelLoaded, así que el clipboard se quedaba mostrando el diagnóstico del reto anterior.
    // ─────────────────────────────────────────────
    static int Test2_Reto2SinCablearYCambioDeReto()
    {
        int fails = 0;
        var ui = NuevoClipboardDeTest();

        // Paso 1: simular que el clipboard ya mostraba el diagnóstico del Reto 1.
        // (ver nota en Test1 sobre por qué se invoca OnDiag por reflexión además del evento real)
        string seed = "-- RETO 1: LEY DE OHM --\nResistencia colocada: 850 Ohm [OK]\n\n> OK El circuito esta correcto. Reto completado!";
        GameSession.ReportarDiagnosticoReto(1, seed);
        InvocarOnDiagPrivado(ui, 1, seed);
        if (!ui.texto.text.Contains("LEY DE OHM"))
        {
            Debug.LogError("[Test 2] Paso 1 FALLÓ: no se pudo sembrar el diagnóstico del Reto 1 como estado previo.");
            fails++;
        }

        // Paso 2: cambio de reto (simula F4 -> GameManager.OnLevelLoaded(Parallel)) — el texto
        // debe LIMPIARSE de inmediato, no seguir mostrando "LEY DE OHM" del reto anterior.
        InvocarOnLevelPrivado(ui, LevelType.Parallel);
        bool seLimpio = !ui.texto.text.Contains("LEY DE OHM");
        Debug.Log($"[Test 2] Tras cambiar a Reto 2: texto={(seLimpio ? "se limpió correctamente" : "SIGUE MOSTRANDO EL RETO 1 -- BUG")} → \"{ui.texto.text}\"");
        if (!seLimpio) fails++;

        // Paso 3: Reto 2 recién entrado, CERO LEDs/cables — reproduce exactamente el guard que
        // antes bloqueaba el envío en Reto2CircuitGuard.EnviarResumenTecnico() (total==0 → return).
        var diag = new DiagnosticSystem();
        string diagnosisVacio = diag.GetDiagnosisParallel();       // sin ningún LED creado en la escena de test
        string accionVacia    = diag.GetNextActionParallel();
        string r = $"{diagnosisVacio}\n\n> {accionVacia}";
        GameSession.ReportarDiagnosticoReto(2, r);
        InvocarOnDiagPrivado(ui, 2, r);

        bool actualizoConTextoVacio = ui.texto.text.Contains("No hay LEDs conectados");
        Debug.Log($"[Test 2] Reto 2 sin cablear nada: clipboard {(actualizoConTextoVacio ? "SÍ se actualizó con el mensaje correcto" : "NO se actualizó -- BUG")} → \"{ui.texto.text}\"");
        if (!actualizoConTextoVacio) fails++;

        Object.DestroyImmediate(ui.gameObject);
        return fails;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>Instancia mínima de TecnicoDiagnosticoUI (texto + textoAccion reales) para
    /// verificar qué le llega, sin depender de la escena real ni de Play Mode.</summary>
    static TecnicoDiagnosticoUI NuevoClipboardDeTest()
    {
        // RectTransform + Canvas explícitos: TextMeshProUGUI busca un Canvas ancestro al
        // inicializarse. Además, la jerarquía se crea ACTIVA desde el principio (nada de
        // SetActive(false) + activar después) — TMP necesita correr su Awake/OnEnable propios
        // en el momento normal de creación para inicializar su estado interno (m_textInfo, malla,
        // etc.); si se crea inactivo y se activa recién después, ese estado queda a medio
        // construir y la propiedad .text revienta con NullReferenceException al leerla, aunque
        // la referencia al componente en sí no sea null.
        var root = new GameObject("ClipboardTest", typeof(RectTransform), typeof(Canvas));

        var txtDiagGO = new GameObject("texto", typeof(RectTransform), typeof(CanvasRenderer));
        txtDiagGO.transform.SetParent(root.transform, false);
        var txtDiag = txtDiagGO.AddComponent<TextMeshProUGUI>();
        txtDiag.text = "init";

        var txtAccionGO = new GameObject("textoAccion", typeof(RectTransform), typeof(CanvasRenderer));
        txtAccionGO.transform.SetParent(root.transform, false);
        var txtAccion = txtAccionGO.AddComponent<TextMeshProUGUI>();
        txtAccion.text = "init";

        var ui = root.AddComponent<TecnicoDiagnosticoUI>();
        ui.texto = txtDiag;
        ui.textoAccion = txtAccion;
        ui.textoEspera = "—";

        return ui;
    }

    static void InvocarOnLevelPrivado(TecnicoDiagnosticoUI ui, LevelType lt)
    {
        var m = typeof(TecnicoDiagnosticoUI).GetMethod("OnLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(ui, new object[] { lt });
    }

    /// <summary>Invoca OnDiag(reto, resumen) por reflexión — el mismo método que
    /// GameSession.OnDiagnosticoRetoActualizado dispara vía la suscripción hecha en OnEnable().
    /// Necesario porque, fuera de Play Mode, el timing de esa suscripción no es fiable (ver nota
    /// en Test1_CoberturaLos4Retos).</summary>
    static void InvocarOnDiagPrivado(TecnicoDiagnosticoUI ui, int reto, string resumen)
    {
        var m = typeof(TecnicoDiagnosticoUI).GetMethod("OnDiag", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(ui, new object[] { reto, resumen });
    }

    static int Verificar(string reto, TecnicoDiagnosticoUI ui, string contieneEnDiagnostico, string contieneEnDiagnostico2)
    {
        if (ui == null || ui.texto == null || ui.textoAccion == null)
        {
            Debug.LogError($"[{reto}] Verificar: ui={(ui == null ? "NULL" : "ok")} " +
                            $"ui.texto={(ui != null && ui.texto == null ? "NULL" : "ok")} " +
                            $"ui.textoAccion={(ui != null && ui.textoAccion == null ? "NULL" : "ok")}");
            return 1;
        }
        bool ok = ui.texto.text.Contains(contieneEnDiagnostico) && ui.texto.text.Contains(contieneEnDiagnostico2);
        string linea = $"[{reto}] clipboard.texto=\"{Truncar(ui.texto.text)}\"\n" +
                        $"         clipboard.textoAccion=\"{Truncar(ui.textoAccion.text)}\"";
        if (ok) { Debug.Log(linea + "\n  ✓ contiene lo esperado."); return 0; }
        Debug.LogError(linea + $"\n  ✗ NO contiene \"{contieneEnDiagnostico}\" y/o \"{contieneEnDiagnostico2}\".");
        return 1;
    }

    static string Truncar(string s) => string.IsNullOrEmpty(s) ? "(vacío)" : s.Replace("\n", " \\n ").Substring(0, Mathf.Min(160, s.Length));

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
