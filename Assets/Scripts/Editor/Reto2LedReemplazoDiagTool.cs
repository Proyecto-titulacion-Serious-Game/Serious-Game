using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico ESTÁTICO (sin Play Mode) de por qué el LED de reemplazo del Reto 2 puede aparecer
/// "en medio de las 2 ramas" en vez de en el slot/posición del LED dañado. Abre Explorador.unity,
/// busca el board del Reto 2 (mismo criterio que Reto2CircuitGuard.LocalizarSim/BuscarLedDanado/
/// BuscarSlotCorrecto) e imprime posiciones reales para comparar contra lo que el guard usaría.
///
/// Menú: Tools → TITA → Reto 2 → Diagnosticar posición LED de reemplazo
/// </summary>
public static class Reto2LedReemplazoDiagTool
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 2/Diagnosticar posición LED de reemplazo")]
    public static void Diagnosticar()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        ProtoboardSimulator sim = null;
        foreach (var s in Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include))
        {
            if (s == null) continue;
            if (s.name == "Protoboard_Reto2" || TieneAncestro(s.transform, "Reto2_Zone")) { sim = s; break; }
        }

        if (sim == null)
        {
            Debug.LogError("[Reto2LedDiag] No encontré el ProtoboardSimulator del Reto 2 (busqué name=='Protoboard_Reto2' o ancestro 'Reto2_Zone').");
            return;
        }
        Debug.Log($"[Reto2LedDiag] Sim encontrado: '{sim.name}' en {Ruta(sim.transform)}  pos={sim.transform.position}");

        // ── LED dañado (mismo criterio que BuscarLedDanado) ──
        GameObject danado = null;
        var leds = sim.GetComponentsInChildren<LED>(true);
        Debug.Log($"[Reto2LedDiag] LEDs bajo el sim: {leds.Length}");
        foreach (var l in leds)
        {
            if (l == null) continue;
            Debug.Log($"[Reto2LedDiag]   LED '{l.name}' activeSelf={l.gameObject.activeSelf} " +
                      $"polarityInverted={l.polarityInverted} pos={l.transform.position} " +
                      $"parent='{(l.transform.parent != null ? l.transform.parent.name : "null")}'");
            if (danado == null && l.gameObject.activeSelf && EsNombreDanado(l.name)) danado = l.gameObject;
        }
        if (danado == null)
            foreach (var l in leds)
                if (danado == null && l != null && l.gameObject.activeSelf && l.polarityInverted) danado = l.gameObject;

        if (danado != null)
            Debug.Log($"[Reto2LedDiag] ✓ LED dañado detectado: '{danado.name}' pos={danado.transform.position}");
        else
            Debug.LogWarning("[Reto2LedDiag] ✗ NO se detectó ningún LED dañado por nombre/polaridad — " +
                              "CablearEnRamaDanada() caería al fallback de slot (o a NADA si tampoco hay slot).");

        // ── Slot de fallback (row=3, col=5 — defaults de Reto2CircuitGuard) ──
        ProtoboardSlot slotFallback = null;
        var slots = sim.GetComponentsInChildren<ProtoboardSlot>(true);
        Debug.Log($"[Reto2LedDiag] ProtoboardSlots bajo el sim: {slots.Length}");
        foreach (var s in slots)
            if (s != null && s.row == 3 && s.col == 5) { slotFallback = s; break; }

        if (slotFallback != null)
            Debug.Log($"[Reto2LedDiag] ✓ Slot fallback (row=3,col=5): '{slotFallback.name}' pos={slotFallback.transform.position} railId={slotFallback.railId}");
        else
            Debug.LogWarning("[Reto2LedDiag] ✗ NO existe ProtoboardSlot con row=3,col=5 en este board — " +
                              "si tampoco hay LED dañado, CablearEnRamaDanada() NO TOCA LA POSICIÓN del LED nuevo " +
                              "(se queda donde el jugador/física lo soltó).");

        // ── Listar TODOS los slots con su row/col real para comparar contra los defaults ──
        Debug.Log("[Reto2LedDiag] Slots reales del board (row,col,railId,pos):");
        foreach (var s in slots)
        {
            if (s == null) continue;
            Debug.Log($"[Reto2LedDiag]   row={s.row} col={s.col} rail='{s.railId}' pos={s.transform.position}");
        }

        // ── Las 2 ramas: LEDs existentes y sus railes (para ver el centro geométrico real) ──
        foreach (var l in leds)
        {
            if (l == null) continue;
            var conn = l.GetComponent<ProtoboardConnector>();
            if (conn != null)
                Debug.Log($"[Reto2LedDiag] LED '{l.name}' conn: lockNodes={conn.lockNodes} lockRailA='{conn.lockRailA}' lockRailB='{conn.lockRailB}' leadA={(conn.leadA!=null?conn.leadA.position.ToString():"null")} leadB={(conn.leadB!=null?conn.leadB.position.ToString():"null")}");
        }

        // ── Dónde entrega/spawnea el Técnico el LED de reemplazo (bandeja / ComponentReceiver) ──
        var receiver = Object.FindAnyObjectByType<ExplorerComponentReceiver>();
        if (receiver != null)
            Debug.Log($"[Reto2LedDiag] ExplorerComponentReceiver '{receiver.name}' pos={receiver.transform.position} en {Ruta(receiver.transform)}");
        else
            Debug.LogWarning("[Reto2LedDiag] No encontré ExplorerComponentReceiver en la escena.");

        Debug.Log("[Reto2LedDiag] ===== FIN DIAGNÓSTICO =====");

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static bool EsNombreDanado(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        n = n.ToLowerInvariant();
        return n.Contains("damaged") || n.Contains("dañad") || n.Contains("danad") || n.Contains("faulty")
            || n.Contains("circuit_led2");
    }

    static bool TieneAncestro(Transform t, string nombre)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == nombre) return true;
        return false;
    }

    static string Ruta(Transform t)
    {
        string r = t.name;
        for (var p = t.parent; p != null; p = p.parent) r = p.name + "/" + r;
        return r;
    }
}
