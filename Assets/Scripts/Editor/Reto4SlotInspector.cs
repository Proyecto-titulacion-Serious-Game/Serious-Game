using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico de SOLO LECTURA: imprime la jerarquía, posiciones locales y railId reales de los
/// slots del protoboard del Reto 4 en la escena, para diseñar el riel GND sin adivinar geometría.
/// No modifica nada.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4SlotInspector.Run -logFile -
/// </summary>
public static class Reto4SlotInspector
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Inspeccionar slots (solo lectura)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var sims = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsSortMode.None);
        Debug.Log($"[Reto4SlotInspector] {sims.Length} ProtoboardSimulator(es) en la escena.");

        foreach (var sim in sims)
        {
            string path = GetPath(sim.transform);
            var slots = sim.todosLosSlots?.Where(s => s != null).ToList() ?? new System.Collections.Generic.List<ProtoboardSlot>();
            Debug.Log($"##SIM## path=\"{path}\" name=\"{sim.gameObject.name}\" slots={slots.Count}");

            var byRail = slots.GroupBy(s => s.railId).OrderBy(g => g.Key);
            foreach (var g in byRail)
            {
                var first = g.First();
                Debug.Log($"  railId=\"{g.Key}\" count={g.Count()} row={first.row} col={first.col}");
            }

            // Detalle de posiciones locales (relativas al propio ProtoboardSimulator) de cada slot.
            foreach (var s in slots.OrderBy(s => s.railId).ThenBy(s => s.col))
            {
                Vector3 localPos = sim.transform.InverseTransformPoint(s.transform.position);
                Debug.Log($"##SLOT## railId=\"{s.railId}\" row={s.row} col={s.col} name=\"{s.gameObject.name}\" " +
                          $"localPos=({localPos.x:F5},{localPos.y:F5},{localPos.z:F5}) " +
                          $"worldPos=({s.transform.position.x:F5},{s.transform.position.y:F5},{s.transform.position.z:F5}) " +
                          $"parent=\"{s.transform.parent.name}\"");
            }

            // Prefab del slot (para clonar el visual correcto).
            if (slots.Count > 0)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(slots[0].gameObject);
                Debug.Log($"  Prefab de origen del primer slot: {(src != null ? AssetDatabase.GetAssetPath(src) : "NINGUNO (no es instancia de prefab)")}");
            }
        }

        // Nodo GND real del Arduino (para saber a qué se debería conectar el riel nuevo).
        var arduinos = Object.FindObjectsByType<ArduinoCore>(FindObjectsSortMode.None);
        foreach (var a in arduinos)
        {
            string gndName = a.nodoGND != null ? a.nodoGND.name : "NULL";
            string gndPath = a.nodoGND != null ? GetPath(a.nodoGND.transform) : "";
            Debug.Log($"##ARDUINO## \"{GetPath(a.transform)}\" nodoGND=\"{gndName}\" path=\"{gndPath}\"");
        }
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
