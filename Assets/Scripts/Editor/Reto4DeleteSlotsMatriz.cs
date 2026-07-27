using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Borra el GameObject "Slots_Matriz" bajo el Bareboard del Reto 4 — confirmado inerte por
/// Reto4SlotsMatrizVsProtoboardSlots (24 hijos vacíos, sin ProtoboardSlot, sin Renderer, no
/// referenciado en ningún ProtoboardSimulator.todosLosSlots). El grid real que usa el motor
/// eléctrico es "[ProtoboardSlots]", que NO se toca.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4DeleteSlotsMatriz.RunBatch -logFile -
/// </summary>
public static class Reto4DeleteSlotsMatriz
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string TargetPath = "GameZones/Reto4_Zone/Reto4_TiltGroup/Bareboard/Slots_Matriz";

    [MenuItem("Tools/TITA/Reto 4/Borrar Slots_Matriz muerto")]
    public static void RunMenu()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool ok = Run();
        if (ok)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        EditorUtility.DisplayDialog("Reto 4 — Borrar Slots_Matriz",
            ok ? "Slots_Matriz eliminado y escena guardada." : "No lo encontré. Revisa la consola.", "OK");
    }

    public static void RunBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool ok = Run();
        if (ok)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log(ok ? "[Reto4DeleteSlotsMatriz] Slots_Matriz eliminado. Guardado."
                     : "[Reto4DeleteSlotsMatriz] FALLÓ: no encontré el GameObject. Revisa la consola.");
    }

    static bool Run()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var target = all.FirstOrDefault(t => GetPath(t) == TargetPath);

        if (target == null)
        {
            Debug.LogError($"[Reto4DeleteSlotsMatriz] No encontré '{TargetPath}'.");
            return false;
        }

        // Verificación de seguridad (repite el chequeo del diagnóstico): abortar si por alguna
        // razón SÍ tuviera algún ProtoboardSlot real, para no borrar algo que el motor use.
        var slotsReales = target.GetComponentsInChildren<ProtoboardSlot>(true);
        if (slotsReales.Length > 0)
        {
            Debug.LogError($"[Reto4DeleteSlotsMatriz] ABORTADO: '{TargetPath}' tiene {slotsReales.Length} " +
                            "ProtoboardSlot real(es) — ya no coincide con el diagnóstico previo, no lo borro.");
            return false;
        }

        int hijos = target.childCount;
        Object.DestroyImmediate(target.gameObject);
        Debug.Log($"[Reto4DeleteSlotsMatriz] '{TargetPath}' eliminado ({hijos} hijos vacíos con él).");
        return true;
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
