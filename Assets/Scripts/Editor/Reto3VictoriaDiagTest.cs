using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Mismo método que Reto2VictoriaDiagTest, aplicado al Reto 3 (Mixto: capacitor + LED + resistor,
/// piezas FIJAS de escena, sin protoboard/MNA ni cables eléctricamente funcionales — a diferencia
/// del Reto 2, los cables de Reto3_Zone son puramente decorativos, VRCableRenderer sin componente
/// eléctrico). Abre Explorador.unity REAL, activa la zona con LoadLevel(2) real (no a mano), fuerza
/// el estado "todo perfecto" en los 3 componentes fijos, recalcula el circuito con el mismo forzado
/// que usa el botón físico, y llama CumpleVictoriaRetos123() para confirmar que da true.
///
/// Menú: Tools → TITA → Reto 3 → Diagnosticar por qué no completa (headless)
/// </summary>
public static class Reto3VictoriaDiagTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 3/Diagnosticar por qué no completa (headless)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { Debug.LogError("[Reto3VictoriaDiag] No hay GameManager en la escena."); Finish(1); return; }
        var tGm = typeof(GameManager);

        // LoadLevel(2) real = Mixed (OhmLaw=0, Parallel=1, Mixed=2, Arduino=3): activa/desactiva las
        // 4 zonas igual que en juego real — sin esto, componentes de OTROS retos con sus propias
        // fallas podrían quedar "activos" según el último guardado de la escena y contaminar el
        // conteo de CumpleVictoriaRetos123 (mismo riesgo que se confirmó real en Reto 2).
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 2 });
        Debug.Log($"[Reto3VictoriaDiag] reto1Zone.active={(gm.reto1Zone != null ? gm.reto1Zone.activeSelf.ToString() : "null")} " +
                  $"reto2Zone.active={(gm.reto2Zone != null ? gm.reto2Zone.activeSelf.ToString() : "null")} " +
                  $"reto3Zone.active={(gm.reto3Zone != null ? gm.reto3Zone.activeSelf.ToString() : "null")} " +
                  $"reto4Zone.active={(gm.reto4Zone != null ? gm.reto4Zone.activeSelf.ToString() : "null")}");

        // Agarrar el CircuitManager de Reto3_Zone DIRECTO (no via FindObjectsByType(Exclude) scene-wide,
        // que en batchmode sin Play Mode puede no reflejar a tiempo el SetActive recién hecho) y forzar
        // su ciclo de vida a mano — así separamos "bug real de simulación" de "artefacto de timing del
        // arnés de prueba headless".
        CircuitManager cmReto3 = gm.reto3Zone != null ? gm.reto3Zone.GetComponent<CircuitManager>() : null;
        if (cmReto3 == null)
            Debug.LogError("[Reto3VictoriaDiag] No encontré CircuitManager en reto3Zone.");
        else
        {
            Debug.Log($"[Reto3VictoriaDiag] CircuitManager '{cmReto3.name}' components.Count (antes de AutoDetect)={cmReto3.components.Count} topology={cmReto3.topology}");
            cmReto3.AutoDetectComponents();
            Debug.Log($"[Reto3VictoriaDiag] components.Count (después de AutoDetect)={cmReto3.components.Count}: " +
                      string.Join(", ", cmReto3.components.ConvertAll(c => c != null ? $"{c.GetType().Name}'{c.name}'" : "null")));
            var vs = cmReto3.FindCircuitComponent<VoltageSource>();
            Debug.Log($"[Reto3VictoriaDiag] VoltageSource encontrado en components: {(vs != null ? vs.name : "NINGUNO")}");
        }

        // ── Estado ANTES de forzar nada ──
        ReportarComponentes("ANTES de corregir");

        // ── Forzar el estado "todo perfecto": resistor sin falla, LED y capacitor con polaridad OK ──
        int forzados = 0;
        foreach (var r in Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
        { if (r == null || r.nodeA == null || r.nodeB == null) continue; r.hasFault = false; forzados++; }
        foreach (var led in Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
        { if (led == null || led.nodeA == null || led.nodeB == null) continue; led.polarityInverted = false; forzados++; }
        foreach (var cap in Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
        { if (cap == null || cap.nodeA == null || cap.nodeB == null) continue; cap.polarityInverted = false; forzados++; }
        Debug.Log($"[Reto3VictoriaDiag] Componentes forzados a estado correcto: {forzados}");

        // Mismo forzado de recálculo que usa el botón físico (EvaluarCircuitSimulator →
        // ForzarSimulacionRetos123): CircuitSimulator.ForceSimulate() + CircuitManager.ForceSimulate()
        // de cada CircuitManager activo — sin esto, LED.isOn/state quedan con el último valor calculado
        // ANTES de forzar las fallas a false, y el chequeo vería un LED "apagado" desactualizado.
        InvokePrivate(tGm, gm, "ForzarSimulacionRetos123");

        // Redundante a propósito: forzar TAMBIÉN directo sobre la referencia ya obtenida, por si el
        // scan FindObjectsByType(Exclude) de ForzarSimulacionRetos123 no lo alcanzó a tiempo en batchmode.
        if (cmReto3 != null) cmReto3.ForceSimulate();

        ReportarComponentes("DESPUÉS de corregir + recalcular");

        // ── Chequeo de victoria REAL (el mismo que usa el botón físico) ──
        var resultado = (bool)InvokePrivate(tGm, gm, "CumpleVictoriaRetos123");
        Debug.Log($"[Reto3VictoriaDiag] CumpleVictoriaRetos123() = {resultado}");

        Debug.Log(resultado
            ? "\n[Reto3VictoriaDiag] ===== RESULTADO: ✓ CumpleVictoriaRetos123() da TRUE con los 3 componentes corregidos ====="
            : "\n[Reto3VictoriaDiag] ===== RESULTADO: ✗ CumpleVictoriaRetos123() da FALSE — ver estado de componentes arriba =====");

        Finish(resultado ? 0 : 1);
    }

    static void ReportarComponentes(string etiqueta)
    {
        Debug.Log($"[Reto3VictoriaDiag] --- Componentes {etiqueta} ---");
        foreach (var r in Object.FindObjectsByType<Resistor>(FindObjectsInactive.Include))
        {
            if (r == null) continue;
            Debug.Log($"[Reto3VictoriaDiag]   Resistor '{r.name}' activeSelf={r.gameObject.activeSelf} " +
                      $"nodeA={(r.nodeA != null ? r.nodeA.name : "null")} nodeB={(r.nodeB != null ? r.nodeB.name : "null")} " +
                      $"hasFault={r.hasFault} R={r.resistance:0}");
        }
        foreach (var led in Object.FindObjectsByType<LED>(FindObjectsInactive.Include))
        {
            if (led == null) continue;
            Debug.Log($"[Reto3VictoriaDiag]   LED '{led.name}' activeSelf={led.gameObject.activeSelf} " +
                      $"nodeA={(led.nodeA != null ? led.nodeA.name : "null")} nodeB={(led.nodeB != null ? led.nodeB.name : "null")} " +
                      $"isOn={led.isOn} state={led.state} invertido={led.polarityInverted}");
        }
        foreach (var cap in Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Include))
        {
            if (cap == null) continue;
            Debug.Log($"[Reto3VictoriaDiag]   Capacitor '{cap.name}' activeSelf={cap.gameObject.activeSelf} " +
                      $"nodeA={(cap.nodeA != null ? cap.nodeA.name : "null")} nodeB={(cap.nodeB != null ? cap.nodeB.name : "null")} " +
                      $"invertido={cap.polarityInverted}");
        }
    }

    static void Finish(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto3VictoriaDiag] No encontré el método privado '{method}' en {t.Name}."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }
}
