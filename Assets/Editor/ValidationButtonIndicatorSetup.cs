#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Agrega la flecha holográfica (<see cref="ValidationButtonIndicator"/>) sobre props interactivos
/// del Reto 4 que el Explorador debe encontrar en VR: el botón físico de validación
/// ("ValidationButton_VR") y la caja dispensadora de cables ("CableBox_VR").
///
/// Menú: Tools → TITA → Reto 4 → Agregar flechas (botón + caja de cables)
/// Batch: ValidationButtonIndicatorSetup.RunBatch()
/// </summary>
public static class ValidationButtonIndicatorSetup
{
    const string ExploradorScenePath = "Assets/Scenes/Explorador.unity";
    const string TmpFontGuid = "8f586378b4e144a9851e7b34d9b748ee"; // fuente TMP ya usada en toda la escena

    [MenuItem("Tools/TITA/Reto 4/Agregar flechas (botón + caja de cables)")]
    public static void RunMenu()
    {
        RunBatch();
        EditorUtility.DisplayDialog("Flechas de Reto 4",
            "Listo (o ver consola si falló). Revisá en el Editor que las flechas cyan floten arriba " +
            "del botón y de la caja de cables, sin quedar enterradas. La escena ya quedó guardada.", "OK");
    }

    public static void RunBatch()
    {
        var scene = EditorSceneManager.OpenScene(ExploradorScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[ValidationIndicator] No se pudo abrir {ExploradorScenePath}.");
            return;
        }

        TMP_FontAsset font = null;
        string fontPath = AssetDatabase.GUIDToAssetPath(TmpFontGuid);
        if (!string.IsNullOrEmpty(fontPath))
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);

        AddIndicator(scene, "ValidationButton_VR", "VALIDAR", 0.15f, font);
        AddIndicator(scene, "CableBox_VR", "CABLES", 0.25f, font);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ValidationIndicator] Completado y escena guardada.");
    }

    static void AddIndicator(Scene scene, string targetName, string label, float heightAbove, TMP_FontAsset font)
    {
        GameObject targetGO = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == targetName);
            if (t != null) { targetGO = t.gameObject; break; }
        }

        if (targetGO == null)
        {
            Debug.LogError($"[ValidationIndicator] No se encontró '{targetName}' en la escena. Salteado.");
            return;
        }

        var existing = targetGO.GetComponent<ValidationButtonIndicator>();
        if (existing != null)
        {
            Debug.Log($"[ValidationIndicator] '{targetName}' ya tiene el indicador — nada que hacer (idempotente).");
            return;
        }

        var indicator = targetGO.AddComponent<ValidationButtonIndicator>();
        indicator.labelText   = label;
        indicator.heightAbove = heightAbove;
        indicator.font        = font;

        EditorUtility.SetDirty(targetGO);
        Debug.Log($"[ValidationIndicator] Flecha \"{label}\" agregada sobre '{targetName}' (heightAbove={heightAbove}).");
    }
}
#endif
