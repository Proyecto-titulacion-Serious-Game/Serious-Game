using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico de SOLO LECTURA: compara los GameObjects "[ProtoboardSlots]" vs "Slots_Matriz" en
/// Explorador.unity — jerarquía, cuántos ProtoboardSlot tiene cada uno, y cuáles de ellos están
/// REALMENTE referenciados en el todosLosSlots de algún ProtoboardSimulator activo (el único que
/// importa: si un grupo de slots no está en esa lista, es huérfano visualmente aunque exista en
/// la escena — no lo usa el motor eléctrico).
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4SlotsMatrizVsProtoboardSlots.Run -logFile -
/// </summary>
public static class Reto4SlotsMatrizVsProtoboardSlots
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Comparar [ProtoboardSlots] vs Slots_Matriz (solo lectura)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var todosLosSims = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[Compare] {todosLosSims.Length} ProtoboardSimulator(es) en la escena.");
        foreach (var sim in todosLosSims)
        {
            var slotsRegistrados = sim.todosLosSlots?.Where(s => s != null).ToList() ?? new System.Collections.Generic.List<ProtoboardSlot>();
            Debug.Log($"##SIM## path=\"{GetPath(sim.transform)}\" activo={sim.gameObject.activeInHierarchy} " +
                      $"todosLosSlots.Count={slotsRegistrados.Count}");
        }

        // Buscar TODOS los GameObjects candidatos por nombre (activos e inactivos).
        var candidatos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name == "[ProtoboardSlots]" || t.name == "Slots_Matriz")
            .ToList();

        Debug.Log($"[Compare] {candidatos.Count} GameObjects candidatos encontrados por nombre.");

        foreach (var c in candidatos)
        {
            var misSlots = c.GetComponentsInChildren<ProtoboardSlot>(true);
            var railGroups = misSlots.GroupBy(s => s.railId).OrderBy(g => g.Key)
                .Select(g => $"{g.Key}x{g.Count()}");

            // ¿Está alguno de estos slots en el todosLosSlots de ALGÚN ProtoboardSimulator?
            bool referenciado = false;
            ProtoboardSimulator simQueLoUsa = null;
            foreach (var sim in todosLosSims)
            {
                if (sim.todosLosSlots == null) continue;
                if (misSlots.Any(s => sim.todosLosSlots.Contains(s))) { referenciado = true; simQueLoUsa = sim; break; }
            }

            int hijosTotal = c.childCount;
            var renderersHijos = c.GetComponentsInChildren<Renderer>(true);
            var nombresHijos = System.Linq.Enumerable.Range(0, c.childCount)
                .Select(i => c.GetChild(i).name).ToList();

            Debug.Log($"##GO## nombre=\"{c.name}\" path=\"{GetPath(c)}\" activo={c.gameObject.activeInHierarchy} " +
                      $"hijosDirectos={hijosTotal} nombresHijos=[{string.Join(", ", nombresHijos)}] " +
                      $"rendererEnAlgunHijo={renderersHijos.Length} " +
                      $"hijosProtoboardSlot={misSlots.Length} rails=[{string.Join(", ", railGroups)}] " +
                      $"REFERENCIADO_en_todosLosSlots={referenciado}" +
                      (referenciado ? $" (por {GetPath(simQueLoUsa.transform)})" : ""));
        }
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
