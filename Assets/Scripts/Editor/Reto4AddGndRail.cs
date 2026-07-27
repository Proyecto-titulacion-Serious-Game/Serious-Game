using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Agrega un riel GND real y bien calibrado al protoboard del Reto 4.
///
/// Diagnóstico previo (Reto4SlotInspector): el Bareboard del Reto 4 (GameZones/Reto4_Zone/
/// Reto4_TiltGroup/Bareboard) ya tenía 4 slots con railId="GND" (y 4 "VCC") en <c>todosLosSlots</c>
/// — sobras de una corrida vieja de ProtoboardSlotGenerator (row=100/99, su convención para rieles
/// de poder) — pero mal ubicados: Y≈0.095-0.098 y X/Z dispersos, muy por fuera del plano real
/// Y=0.073 donde vive la grilla ROW_A..ROW_F actual (recalibrada a mano sobre el Bareboard real).
/// En la práctica esos 4 GND viejos no son alcanzables/visibles para el jugador en VR.
///
/// Este script los reemplaza: clona los 4 slots de ROW_F (visual + escala idénticos a los que ya
/// se ven bien en el board), y los reposiciona extrapolando el paso ROW_E→ROW_F por columna —
/// o sea, la nueva fila GND queda a la misma distancia de ROW_F que ROW_F lo está de ROW_E,
/// como una 7ma fila "por debajo" (ROW_F es la fila con menor Y mundial = la más baja del board).
/// No toca VCC (fuera de alcance: el Reto 4 no tiene una fuente constante, así que un riel VCC no
/// representa nada eléctrico real ahí — ver conversación).
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4AddGndRail.RunBatch -logFile -
///           Editor: Tools → TITA → Reto 4 → Agregar riel GND (recalibrar)
/// </summary>
public static class Reto4AddGndRail
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string BareboardPath = "GameZones/Reto4_Zone/Reto4_TiltGroup/Bareboard";

    [MenuItem("Tools/TITA/Reto 4/Agregar riel GND (recalibrar)")]
    public static void RunMenu()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Run();
        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        EditorUtility.DisplayDialog("Reto 4 — Riel GND",
            n > 0 ? $"{n} slots GND recreados y guardados como 7ma fila (más allá de ROW_F)."
                  : "No se encontró el Bareboard del Reto 4 o faltan ROW_E/ROW_F. Revisa la consola.", "OK");
    }

    public static void RunBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Run();
        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log(n > 0 ? $"[Reto4AddGndRail] {n} slots GND listos. Guardado."
                        : "[Reto4AddGndRail] FALLÓ: revisa la consola.");
    }

    static int Run()
    {
        var sims = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsSortMode.None);
        ProtoboardSimulator sim = sims.FirstOrDefault(s => GetPath(s.transform) == BareboardPath);
        if (sim == null)
        {
            Debug.LogError($"[Reto4AddGndRail] No encontré ProtoboardSimulator en '{BareboardPath}'.");
            return 0;
        }

        var slots = sim.todosLosSlots.Where(s => s != null).ToList();
        var rowF = slots.Where(s => s.railId == "ROW_F").OrderBy(s => s.col).ToList();
        var rowE = slots.Where(s => s.railId == "ROW_E").OrderBy(s => s.col).ToList();
        if (rowF.Count != 4 || rowE.Count != 4)
        {
            Debug.LogError($"[Reto4AddGndRail] Esperaba 4+4 slots en ROW_E/ROW_F, encontré {rowE.Count}/{rowF.Count}.");
            return 0;
        }

        var oldGnd = slots.Where(s => s.railId == "GND").ToList();
        Debug.Log($"[Reto4AddGndRail] {oldGnd.Count} slots GND viejos encontrados (se reemplazan por posiciones calibradas).");

        var newSlots = new List<ProtoboardSlot>();
        for (int c = 0; c < 4; c++)
        {
            var template = rowF[c];
            Vector3 step = rowF[c].transform.position - rowE[c].transform.position; // paso E→F, por columna

            var go = Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = $"Slot_GND_{c}";
            go.transform.position = template.transform.position + step;
            go.transform.rotation = template.transform.rotation;

            var node = go.GetComponent<ElectricalNode>();
            if (node != null) Object.DestroyImmediate(node);

            var slot = go.GetComponent<ProtoboardSlot>();
            slot.railId = "GND";
            slot.row = 100;
            slot.col = c;
            slot.assignedNode = null;
            newSlots.Add(slot);
        }

        foreach (var old in oldGnd)
        {
            sim.todosLosSlots.Remove(old);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        sim.todosLosSlots.AddRange(newSlots);
        EditorUtility.SetDirty(sim);

        Debug.Log($"[Reto4AddGndRail] {newSlots.Count} slots GND creados como 7ma fila (railId=\"GND\", row=100), " +
                  $"clonados de ROW_F y extrapolados con el paso ROW_E→ROW_F por columna. " +
                  $"{oldGnd.Count} slots GND viejos (mal calibrados) eliminados. Total slots ahora: {sim.todosLosSlots.Count}.");
        return newSlots.Count;
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
