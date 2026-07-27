using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pedido explícito del usuario (misma sesión del fix de agarre por Rod_Visual, 2026-07-24):
/// verificar que los 3 modos del multímetro (DCVoltage, DCCurrent, Resistance) midan valores
/// correctos sobre circuitos REALES de Reto 1 y Reto 3.
///
/// Mismo patrón que Reto1VictoriaDiagTest/Reto3VictoriaDiagTest: abre Explorador.unity, activa la
/// zona con LoadLevel() real, fuerza el estado "circuito correcto" (mismo código que esos tests),
/// y en vez de solo chequear victoria, pone las puntas del multímetro sobre el resistor de esa
/// zona y compara measuredVoltage/measuredCurrent (y la resistencia en modo Resistance) contra los
/// valores que el PROPIO resistor ya tiene resueltos por el circuito (nodeA.voltage, nodeB.voltage,
/// current, resistance) — así el test aísla bugs de MEDICIÓN/DISPLAY del multímetro (SetRedNode/
/// SetBlackNode/TakeReading/UpdateDisplay) de bugs del solver eléctrico (ya cubiertos por otros
/// tests).
///
/// Desde el rediseño a panel fijo (2026-07-24, sesión "Panel de Medición"): ya no hay
/// puertoVmA/puertoCOM/sockets que verificar — los jacks quedaron soldados al panel — así que el
/// test puede enfocarse directo en la lógica de medición por modo.
///
/// Menú: Tools → TITA → Pruebas → Multímetro - 3 modos en Reto1+Reto3 (headless)
/// </summary>
public static class MultimeterModesReto1Reto3Test
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Pruebas/Multímetro - 3 modos en Reto1+Reto3 (headless)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { Debug.LogError("[MultimeterModesTest] No hay GameManager en la escena."); Finish(1); return; }

        var mm = Object.FindAnyObjectByType<Multimeter>();
        if (mm == null) { Debug.LogError("[MultimeterModesTest] No hay Multimeter en la escena."); Finish(1); return; }

        bool ok = true;
        ok &= ProbarReto1(gm, mm);
        ok &= ProbarReto3(gm, mm);

        Debug.Log(ok
            ? "\n[MultimeterModesTest] ===== TODO OK: los 3 modos miden correcto en Reto1 y Reto3 ====="
            : "\n[MultimeterModesTest] ===== FALLÓ — ver detalle arriba =====");
        Finish(ok ? 0 : 1);
    }

    static bool ProbarReto1(GameManager gm, Multimeter mm)
    {
        InvokePrivate(typeof(GameManager), gm, "LoadLevel", new object[] { 0 });

        var resistor = gm.reto1Zone != null ? gm.reto1Zone.GetComponentInChildren<Resistor>(true) : null;
        var led      = gm.reto1Zone != null ? gm.reto1Zone.GetComponentInChildren<LED>(true) : null;
        var cm       = gm.reto1Zone != null ? gm.reto1Zone.GetComponentInChildren<CircuitManager>(true) : null;
        if (resistor == null || cm == null)
        {
            Debug.LogError("[MultimeterModesTest] Reto1: no encontré Resistor/CircuitManager bajo reto1Zone.");
            return false;
        }

        resistor.Repair();
        if (led != null) led.polarityInverted = false;
        var sw = gm.reto1Zone.GetComponentInChildren<CircuitSwitch>(true);
        if (sw != null) sw.isOn = true;

        cm.AutoDetectComponents();
        cm.ForceSimulate();

        bool ok = ProbarSobreResistor("Reto1", mm, resistor);
        ok &= ProbarBornesDeLaFuente("Reto1", mm, cm);
        return ok;
    }

    /// <summary>
    /// Regresión del bug de playtest 2026-07-26: "voltaje bien, CORRIENTE y RESISTENCIA en 0 con
    /// las 2 puntas sobre nodos reales". Los puntos de medición marcados en la escena del Reto 1
    /// son los BORNES DE LA BATERÍA (NP_R1_VCC lleva probeType=Red, Node_R1_GND probeType=Black),
    /// y ahí el único componente que "puentea" ambos nodos es el VoltageSource — al que
    /// CircuitManager.ForceSimulate() excluye del MNA, así que su .current queda en 0 siempre.
    /// ProbarSobreResistor() no lo detectaba porque siempre mide sobre un componente PASIVO.
    ///
    /// Ahora el multímetro debe reportar en esos bornes la corriente TOTAL del circuito.
    /// </summary>
    static bool ProbarBornesDeLaFuente(string etiqueta, Multimeter mm, CircuitManager cm)
    {
        var fuente = cm.FindCircuitComponent<VoltageSource>();
        if (fuente == null || fuente.nodeA == null || fuente.nodeB == null)
        {
            Debug.LogError($"[MultimeterModesTest] {etiqueta}: no encontré VoltageSource con nodos en el CircuitManager.");
            return false;
        }

        float vEsperado = fuente.GetEffectiveVoltage();
        float iEsperado = Mathf.Abs(cm.totalCurrent);
        Debug.Log($"[MultimeterModesTest] {etiqueta} bornes de la fuente '{fuente.name}': " +
                  $"V={vEsperado:F2}V I_total={iEsperado * 1000f:F2}mA " +
                  $"(nodeA='{fuente.nodeA.name}' nodeB='{fuente.nodeB.name}')");

        if (iEsperado <= 0.0001f)
        {
            Debug.LogError($"[MultimeterModesTest] {etiqueta}: el circuito no lleva corriente — el test no puede validar el amperímetro.");
            return false;
        }

        bool ok = true;

        mm.SetMode(MultimeterMode.DCVoltage);
        mm.SetRedNode(fuente.nodeA);
        mm.SetBlackNode(fuente.nodeB);
        InvokePrivate(typeof(Multimeter), mm, "TakeReading");
        bool vOk = mm.isReading && Mathf.Abs(mm.measuredVoltage - vEsperado) <= Mathf.Max(0.05f, vEsperado * 0.05f);
        Debug.Log($"[MultimeterModesTest] {etiqueta} bornes DCVoltage: esperado={vEsperado:F3}V medido={mm.measuredVoltage:F3}V → {(vOk ? "OK" : "FALLA")}");
        ok &= vOk;

        mm.SetMode(MultimeterMode.DCCurrent);
        mm.SetRedNode(fuente.nodeA);
        mm.SetBlackNode(fuente.nodeB);
        InvokePrivate(typeof(Multimeter), mm, "TakeReading");
        bool iOk = mm.isReading && mm.measuredCurrent > 0.0001f &&
                   Mathf.Abs(mm.measuredCurrent - iEsperado) <= Mathf.Max(0.0005f, iEsperado * 0.05f);
        Debug.Log($"[MultimeterModesTest] {etiqueta} bornes DCCurrent: esperado={iEsperado * 1000f:F2}mA medido={mm.measuredCurrent * 1000f:F2}mA " +
                  $"→ {(iOk ? "OK" : "FALLA (era 0 mA antes del fix del 2026-07-26)")}");
        ok &= iOk;

        mm.SetMode(MultimeterMode.Resistance);
        mm.SetRedNode(fuente.nodeA);
        mm.SetBlackNode(fuente.nodeB);
        InvokePrivate(typeof(Multimeter), mm, "TakeReading");
        // En los bornes de la fuente el óhmetro entrega V/I = resistencia EQUIVALENTE vista por la
        // batería (no la de un componente suelto) — lo que importa aquí es que ya no sea 0.
        float rEsperado = vEsperado / iEsperado;
        bool rOk = mm.isReading && mm.measuredVoltage > 1f &&
                   Mathf.Abs(mm.measuredVoltage - rEsperado) <= Mathf.Max(1f, rEsperado * 0.05f);
        Debug.Log($"[MultimeterModesTest] {etiqueta} bornes Resistance: esperado~{rEsperado:F0}Ω (V/I equivalente) medido={mm.measuredVoltage:F1}Ω " +
                  $"→ {(rOk ? "OK" : "FALLA (era 0 Ω antes del fix del 2026-07-26)")}");
        ok &= rOk;

        return ok;
    }

    static bool ProbarReto3(GameManager gm, Multimeter mm)
    {
        InvokePrivate(typeof(GameManager), gm, "LoadLevel", new object[] { 2 }); // Mixed

        // .Repair() (no solo hasFault=false): pone resistance=correctResistance también. Poner
        // solo hasFault=false (como hacía el Reto3VictoriaDiagTest viejo) deja la resistencia en
        // su valor DE FALLA (2200Ω) — el mismo Reto3VictoriaDiagTest ya falla CumpleVictoriaRetos123()
        // por esto mismo; RetosLedEncendidoTest (el arnés que sí pasa) corrige vía ComponentDeliverySystem,
        // que sí resetea la resistencia real. .Repair() logra el mismo resultado sin needing el
        // pipeline completo de entrega.
        foreach (var r in Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
            if (r != null && r.nodeA != null && r.nodeB != null) r.Repair();
        foreach (var led in Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            if (led != null && led.nodeA != null && led.nodeB != null) led.polarityInverted = false;
        foreach (var cap in Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
            if (cap != null && cap.nodeA != null && cap.nodeB != null) cap.polarityInverted = false;

        InvokePrivate(typeof(GameManager), gm, "ForzarSimulacionRetos123");

        var cmReto3 = gm.reto3Zone != null ? gm.reto3Zone.GetComponent<CircuitManager>() : null;
        if (cmReto3 != null)
        {
            // Causa real del "V=0/I=0" en los intentos anteriores: components.Count==0 — a
            // diferencia de ProbarReto1 (que sí llama AutoDetectComponents()), acá nunca se había
            // llamado, así que ForceSimulate() no tenía NADA que resolver (lista vacía), sin
            // importar cuántas veces se llamara ni el estado de _dirty.
            cmReto3.AutoDetectComponents();
            cmReto3.MarkDirty();
            cmReto3.ForceSimulate();

            Debug.Log($"[MultimeterModesTest] DIAG Reto3 CircuitManager '{cmReto3.name}': " +
                      $"topology={cmReto3.topology} sourceVoltage={cmReto3.sourceVoltage:F2} " +
                      $"totalCurrent={cmReto3.totalCurrent * 1000f:F2}mA isShortCircuited={cmReto3.isShortCircuited} " +
                      $"components.Count={cmReto3.components.Count}");
            foreach (var c in cmReto3.components)
            {
                if (c == null) { Debug.Log("[MultimeterModesTest] DIAG   - null"); continue; }
                string nodeA = c.nodeA != null ? $"'{c.nodeA.name}'={c.nodeA.voltage:F3}V" : "NULL";
                string nodeB = c.nodeB != null ? $"'{c.nodeB.name}'={c.nodeB.voltage:F3}V" : "NULL";
                if (c is VoltageSource vs)
                    Debug.Log($"[MultimeterModesTest] DIAG   VoltageSource '{c.name}' voltage={vs.voltage:F2} hasFault={vs.hasFault} nodeA={nodeA} nodeB={nodeB}");
                else if (c is LED cled)
                    Debug.Log($"[MultimeterModesTest] DIAG   LED '{c.name}' isOn={cled.isOn} state={cled.state} invertido={cled.polarityInverted} isOpenCircuit={cled.isOpenCircuit} current={cled.current * 1000f:F2}mA nodeA={nodeA} nodeB={nodeB}");
                else if (c is Capacitor ccap)
                    Debug.Log($"[MultimeterModesTest] DIAG   Capacitor '{c.name}' invertido={ccap.polarityInverted} current={c.current * 1000f:F2}mA nodeA={nodeA} nodeB={nodeB}");
                else
                    Debug.Log($"[MultimeterModesTest] DIAG   {c.GetType().Name} '{c.name}' current={c.current * 1000f:F2}mA hasFault={(c is Resistor rr ? rr.hasFault.ToString() : "n/a")} nodeA={nodeA} nodeB={nodeB}");
            }
        }

        var resistor = gm.reto3Zone != null ? gm.reto3Zone.GetComponentInChildren<Resistor>(true) : null;
        if (resistor == null)
        {
            Debug.LogError("[MultimeterModesTest] Reto3: no encontré Resistor bajo reto3Zone.");
            return false;
        }

        return ProbarSobreResistor("Reto3", mm, resistor);
    }

    /// <summary>Pone las puntas sobre nodeA/nodeB del resistor dado y compara los 3 modos contra
    /// los valores YA resueltos por el circuito en ese mismo resistor (ground truth).</summary>
    static bool ProbarSobreResistor(string etiqueta, Multimeter mm, Resistor resistor)
    {
        if (resistor.nodeA == null || resistor.nodeB == null)
        {
            Debug.LogError($"[MultimeterModesTest] {etiqueta}: resistor '{resistor.name}' sin nodeA/nodeB.");
            return false;
        }

        float vEsperado = resistor.nodeA.voltage - resistor.nodeB.voltage;
        float iEsperado = Mathf.Abs(resistor.current);
        float rEsperado = resistor.resistance;
        Debug.Log($"[MultimeterModesTest] {etiqueta} ground truth — resistor '{resistor.name}': " +
                  $"V={vEsperado:F3}V I={iEsperado * 1000f:F2}mA R={rEsperado:F0}Ω " +
                  $"(nodeA='{resistor.nodeA.name}' nodeB='{resistor.nodeB.name}')");

        bool ok = true;

        // ── DC Voltage ──
        mm.SetMode(MultimeterMode.DCVoltage);
        mm.SetRedNode(resistor.nodeA);
        mm.SetBlackNode(resistor.nodeB);
        InvokePrivate(typeof(Multimeter), mm, "TakeReading");
        bool vOk = mm.isReading && Mathf.Abs(mm.measuredVoltage - vEsperado) <= Mathf.Max(0.05f, Mathf.Abs(vEsperado) * 0.05f);
        Debug.Log($"[MultimeterModesTest] {etiqueta} DCVoltage: esperado={vEsperado:F3}V medido={mm.measuredVoltage:F3}V " +
                  $"isReading={mm.isReading} → {(vOk ? "OK" : "FALLA")}");
        ok &= vOk;

        // ── DC Current ──
        mm.SetMode(MultimeterMode.DCCurrent);
        mm.SetRedNode(resistor.nodeA);
        mm.SetBlackNode(resistor.nodeB);
        InvokePrivate(typeof(Multimeter), mm, "TakeReading");
        bool iOk = mm.isReading && Mathf.Abs(mm.measuredCurrent - iEsperado) <= Mathf.Max(0.0005f, iEsperado * 0.05f);
        Debug.Log($"[MultimeterModesTest] {etiqueta} DCCurrent: esperado={iEsperado * 1000f:F2}mA medido={mm.measuredCurrent * 1000f:F2}mA " +
                  $"isReading={mm.isReading} → {(iOk ? "OK" : "FALLA")}");
        ok &= iOk;

        // ── Resistance ── (en este modo, measuredVoltage guarda directamente el valor en Ω — ver TakeReading())
        mm.SetMode(MultimeterMode.Resistance);
        mm.SetRedNode(resistor.nodeA);
        mm.SetBlackNode(resistor.nodeB);
        InvokePrivate(typeof(Multimeter), mm, "TakeReading");
        bool rOk = mm.isReading && Mathf.Abs(mm.measuredVoltage - rEsperado) <= Mathf.Max(1f, rEsperado * 0.05f);
        Debug.Log($"[MultimeterModesTest] {etiqueta} Resistance: esperado={rEsperado:F0}Ω medido={mm.measuredVoltage:F1}Ω " +
                  $"isReading={mm.isReading} → {(rOk ? "OK" : "FALLA")}");
        ok &= rOk;

        return ok;
    }

    static void Finish(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[MultimeterModesTest] No encontré el método privado '{method}' en {t.Name}."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }
}
