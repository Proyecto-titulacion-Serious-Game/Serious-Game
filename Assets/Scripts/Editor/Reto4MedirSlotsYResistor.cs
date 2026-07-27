using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Diagnóstico de solo lectura: distancia real entre slots ADYACENTES de la misma fila
/// (no VCC-a-ROW, que cruza el canal aislante) en la protoboard del Reto 4, y el tamaño base del
/// mesh del prefab Delivered_Resistor a escala 1 — datos para calcular la escala del resistor a
/// partir de la geometría real en vez de un valor fijo a mano.</summary>
public static class Reto4MedirSlotsYResistor
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string ResistorPrefabPath = "Assets/Prefabs/Delivered/Delivered_Resistor.prefab";

    [MenuItem("Tools/TITA/Reto 4/Medir slots y prefab resistor (solo lectura)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var sim = gm.protoSim;

        var buildNodeMap = typeof(ProtoboardSimulator).GetMethod("BuildNodeMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        buildNodeMap.Invoke(sim, null);

        // OJO: railId agrupa por NET ELÉCTRICO, no por posición física — el riel GND, por ejemplo,
        // son 4 slots FÍSICAMENTE separados que comparten un solo nodo (ver memoria del proyecto).
        // Medir "mismo railId, col consecutiva" mide saltos entre huecos lejanos del mismo net, no
        // huecos VECINOS reales. La separación física real de "hueco a hueco" es la distancia
        // MÍNIMA entre cualquier par de slots DISTINTOS — así se mide de verdad, sin depender del
        // agrupamiento eléctrico.
        var slots = sim.todosLosSlots.Where(s => s != null).ToList();
        float minDist = float.MaxValue;
        ProtoboardSlot minA = null, minB = null;
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
            {
                float d = Vector3.Distance(slots[i].transform.position, slots[j].transform.position);
                if (d < minDist) { minDist = d; minA = slots[i]; minB = slots[j]; }
            }
        Debug.Log($"[MedirSlots] Separación FÍSICA mínima real entre 2 slots distintos = {minDist*100f:F3} cm " +
                  $"('{minA?.name}' railId={minA?.railId} <-> '{minB?.name}' railId={minB?.railId})");

        // Para contexto: las 5 distancias más chicas (para confirmar que no es un caso aislado/raro).
        var todasLasDistancias = new System.Collections.Generic.List<(float d, string a, string b)>();
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
                todasLasDistancias.Add((Vector3.Distance(slots[i].transform.position, slots[j].transform.position), slots[i].name, slots[j].name));
        foreach (var t in todasLasDistancias.OrderBy(t => t.d).Take(5))
            Debug.Log($"[MedirSlots]   {t.d*100f:F3} cm : {t.a} <-> {t.b}");

        var scaler = Object.FindObjectsByType<ZoneProximityScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(z => z.name.Contains("Reto4") || z.transform.IsChildOf(sim.transform.root));
        if (scaler != null)
        {
            var so = new SerializedObject(scaler);
            Debug.Log($"[MedirSlots] ZoneProximityScaler en '{scaler.name}': factorMinimo={scaler.factorMinimo} factorMaximo={scaler.factorMaximo} " +
                      $"localScale ACTUAL guardada en la escena={scaler.transform.localScale} (esto es _escalaOriginal x 1, porque Start() nunca corrió)");
        }
        else Debug.Log("[MedirSlots] No until ZoneProximityScaler relacionado al Reto 4.");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResistorPrefabPath);
        if (prefab == null) { Debug.LogError($"[MedirSlots] No until {ResistorPrefabPath}"); return; }

        var rend = prefab.GetComponentInChildren<Renderer>();
        Debug.Log($"[MedirSlots] Prefab '{prefab.name}' — localScale del root={prefab.transform.localScale}");
        if (rend != null)
            Debug.Log($"[MedirSlots] Renderer '{rend.name}' bounds.size (mundo, a la escala actual del prefab asset)={rend.bounds.size}");
        else
            Debug.LogWarning("[MedirSlots] El prefab no tiene Renderer directo (¿está en un hijo más profundo o usa mesh distinto?).");

        foreach (var r in prefab.GetComponentsInChildren<Renderer>())
            Debug.Log($"[MedirSlots]   hijo '{r.name}' localScale={r.transform.localScale} lossyScale={r.transform.lossyScale} bounds.size={r.bounds.size}");
    }
}
