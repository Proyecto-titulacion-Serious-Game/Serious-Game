using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verifica, sobre la escena REAL (no un rig sintético), que el riel GND nuevo del Reto 4
/// (Reto4AddGndRail) realmente colapsa sus 4 slots en UN solo ElectricalNode vía
/// ProtoboardSimulator.NodeForRail("GND") — la prueba real de que sirve como riel compartido.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4GndRailVerify.Run -logFile -
/// </summary>
public static class Reto4GndRailVerify
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string BareboardPath = "GameZones/Reto4_Zone/Reto4_TiltGroup/Bareboard";

    [MenuItem("Tools/TITA/Reto 4/Verificar riel GND (headless)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var sim = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsSortMode.None)
            .FirstOrDefault(s => GetPath(s.transform) == BareboardPath);
        if (sim == null) { Debug.LogError("[GndVerify] No encontré el Bareboard del Reto 4."); Exit(1); return; }

        var gndSlots = sim.todosLosSlots.Where(s => s != null && s.railId == "GND").ToList();
        Debug.Log($"[GndVerify] Slots GND encontrados: {gndSlots.Count}");

        var node = sim.NodeForRail("GND");
        if (node == null) { Debug.LogError("[GndVerify] NodeForRail(\"GND\") devolvió null."); Exit(1); return; }

        // Todos los slots GND deben terminar apuntando al MISMO ElectricalNode tras BuildNodeMap.
        bool allSame = true;
        foreach (var s in gndSlots)
        {
            if (s.assignedNode != node)
            {
                allSame = false;
                Debug.LogWarning($"[GndVerify] Slot_GND_{s.col} apunta a un nodo distinto: {(s.assignedNode != null ? s.assignedNode.name : "null")}");
            }
        }

        Debug.Log($"##GNDVERIFY## slots={gndSlots.Count} nodeForRail=\"{node.name}\" todosApuntanAlMismoNodo={allSame}");

        // Distancia entre columnas del riel GND, para confirmar que el snapRadius por defecto de
        // ProtoboardConnector (1.2cm) alcanza cualquier slot del riel al colocar un componente cerca.
        if (gndSlots.Count >= 2)
        {
            var ordered = gndSlots.OrderBy(s => s.col).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                float d = Vector3.Distance(ordered[i - 1].transform.position, ordered[i].transform.position);
                Debug.Log($"[GndVerify] separación col {ordered[i-1].col}->{ordered[i].col}: {d*100f:F2} cm");
            }
        }

        Exit(allSame && node != null ? 0 : 1);
    }

    static void Exit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
