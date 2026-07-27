using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// A diferencia de Reto3VictoriaDiagTest (que solo llama CumpleVictoriaRetos123() directo), este
/// test ejercita la lógica REAL que decide completar el reto — GameManager.OnCircuitChangedAutoCheck(),
/// con sus 3 condiciones (_vistoIncorrectoEnReto, _repairPerformed, grace period de 2s) — y usa los
/// mismos métodos que el jugador real dispara (PlayerInteraction.CorrectCapacitorPolarity/
/// CorrectPolarity, Resistor.Repair()+RegisterRepairAction), no asignación directa de campos.
///
/// Objetivo: reproducir el reporte "cambié todo correcto y el reto nunca completó" para encontrar
/// en cuál de las 3 condiciones de auto-completar se está cayendo.
///
/// Menú: Tools → TITA → Reto 3 → Test gate de auto-completar (headless)
/// </summary>
public static class Reto3AutoCompleteGateTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 3/Test gate de auto-completar (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { Debug.LogError("[Reto3Gate] No hay GameManager."); Finish(1); return; }
        var t = typeof(GameManager);

        InvokePrivate(t, gm, "LoadLevel", new object[] { 2 });
        Debug.Log($"[Reto3Gate] Tras LoadLevel(2): _levelCompleted={GetPrivate(t, gm, "_levelCompleted")} " +
                  $"_vistoIncorrectoEnReto={GetPrivate(t, gm, "_vistoIncorrectoEnReto")} " +
                  $"_repairPerformed={GetPrivate(t, gm, "_repairPerformed")} " +
                  $"_tiempoInicioReto={GetPrivate(t, gm, "_tiempoInicioReto")} Time.time={Time.time}");

        var cmReto3 = gm.reto3Zone != null ? gm.reto3Zone.GetComponent<CircuitManager>() : null;
        if (cmReto3 == null) { Debug.LogError("[Reto3Gate] No hay CircuitManager en reto3Zone."); Finish(1); return; }
        cmReto3.AutoDetectComponents();
        cmReto3.ForceSimulate();

        // ── Paso 1: confirmar que el estado ROTO (inicial) se ve como "incorrecto" al menos una vez ──
        InvokePrivate(t, gm, "OnCircuitChangedAutoCheck");
        bool vistoIncorrectoTrasRoto = (bool)GetPrivate(t, gm, "_vistoIncorrectoEnReto");
        Debug.Log($"[Reto3Gate] Paso 1 (estado roto, 1er autocheck): _vistoIncorrectoEnReto={vistoIncorrectoTrasRoto} (esperado True)");
        if (!vistoIncorrectoTrasRoto) { Debug.LogError("[Reto3Gate] ✗ El reto NO arrancó viéndose incorrecto — revisar fallas iniciales de la escena."); fails++; }

        // ── Paso 2: reparar los 3 componentes por las RUTAS REALES (no asignación directa) ──
        var pi = Object.FindAnyObjectByType<PlayerInteraction>();
        if (pi == null) { Debug.LogError("[Reto3Gate] No hay PlayerInteraction en la escena — no puedo probar la ruta real."); Finish(1); return; }

        Resistor resistorFaulty = null;
        foreach (var r in Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
            if (r != null && r.nodeA != null && r.nodeB != null && r.hasFault) { resistorFaulty = r; break; }
        LED ledInvertido = null;
        foreach (var l in Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            if (l != null && l.nodeA != null && l.nodeB != null && l.polarityInverted) { ledInvertido = l; break; }
        Capacitor capInvertido = null;
        foreach (var c in Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
            if (c != null && c.nodeA != null && c.nodeB != null && c.polarityInverted) { capInvertido = c; break; }

        Debug.Log($"[Reto3Gate] Encontrados: resistorFaulty={(resistorFaulty != null ? resistorFaulty.name : "NINGUNO")} " +
                  $"ledInvertido={(ledInvertido != null ? ledInvertido.name : "NINGUNO")} " +
                  $"capInvertido={(capInvertido != null ? capInvertido.name : "NINGUNO")}");

        if (capInvertido != null) pi.CorrectCapacitorPolarity(capInvertido);
        if (ledInvertido != null) pi.CorrectPolarity(ledInvertido);

        // Resistor: por la ruta de ENTREGA REAL (ComponentDeliverySystem.ValidateValueForRepair →
        // BuscarResistorDelReto → ApplyRepairToCircuit), NO Resistor.Repair() directo — así se prueba
        // el mismo camino que usa un envío real del Técnico, no el atajo de debug (F8).
        if (resistorFaulty != null)
        {
            var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>(FindObjectsInactive.Include);
            if (delivery == null)
            {
                Debug.LogError("[Reto3Gate] No hay ComponentDeliverySystem en la escena — no puedo probar la ruta de entrega real del resistor.");
                fails++;
            }
            else
            {
                bool entregaOk = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, resistorFaulty.correctResistance);
                Debug.Log($"[Reto3Gate] Entrega REAL del resistor ({resistorFaulty.correctResistance}Ω) por ComponentDeliverySystem → " +
                          $"{(entregaOk ? "REPARADO ✅" : "RECHAZADO ❌ (revisar BuscarResistorDelReto/ValidateValueForRepair para Reto 3)")}");
                if (!entregaOk) fails++;
            }
        }

        Debug.Log($"[Reto3Gate] Tras reparar por rutas reales: _repairPerformed={GetPrivate(t, gm, "_repairPerformed")}");

        // ── Paso 3: recalcular (mismo forzado que el botón físico) y volver a evaluar ──
        InvokePrivate(t, gm, "ForzarSimulacionRetos123");
        cmReto3.ForceSimulate();

        bool correctoAhora = (bool)InvokePrivate(t, gm, "CumpleVictoriaRetos123");
        Debug.Log($"[Reto3Gate] CumpleVictoriaRetos123() tras reparar = {correctoAhora} (esperado True)");

        // ── Paso 4: el momento de la verdad — ¿el gate de auto-completar realmente completa el reto? ──
        float tiempoInicio = (float)GetPrivate(t, gm, "_tiempoInicioReto");
        float elapsed = Time.time - tiempoInicio;
        Debug.Log($"[Reto3Gate] Tiempo transcurrido desde _tiempoInicioReto = {elapsed:0.###}s (necesita > 2.0s para completar)");
        if (elapsed <= 2.0f)
        {
            Debug.LogWarning("[Reto3Gate] Menos de 2s reales transcurrieron en este test headless (se ejecuta casi instantáneo) — " +
                              "esto NO sería un bug del juego real (un jugador tarda mucho más de 2s en reparar 3 piezas a mano), " +
                              "pero para que el test sea representativo, fuerzo _tiempoInicioReto al pasado.");
            SetPrivate(t, gm, "_tiempoInicioReto", Time.time - 3f);
        }

        InvokePrivate(t, gm, "OnCircuitChangedAutoCheck");
        bool completado = (bool)GetPrivate(t, gm, "_levelCompleted");
        Debug.Log($"[Reto3Gate] TRAS OnCircuitChangedAutoCheck (post-reparación, post-grace-period): _levelCompleted={completado}");

        if (!completado)
        {
            Debug.LogError("[Reto3Gate] ✗ BUG REPRODUCIDO: el circuito está correcto (CumpleVictoriaRetos123=True) " +
                            "y las 3 condiciones de gate deberían cumplirse, pero _levelCompleted sigue False.");
            fails++;
        }
        else
        {
            Debug.Log("[Reto3Gate] ✓ El reto SÍ completó correctamente por la ruta real de reparación.");
        }

        Debug.Log(fails == 0
            ? "\n[Reto3Gate] ===== RESULTADO: ✓ El gate de auto-completar funciona con reparaciones por ruta real ====="
            : $"\n[Reto3Gate] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        Finish(fails == 0 ? 0 : 1);
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto3Gate] No encontré el método privado '{method}'."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivate(System.Type t, object instance, string field)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError($"[Reto3Gate] No encontré el campo privado '{field}'."); return null; }
        return f.GetValue(instance);
    }

    static void SetPrivate(System.Type t, object instance, string field, object value)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        f?.SetValue(instance, value);
    }
}
