using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerificarMultimetroArt
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Verificar Multimeter_VR_Art (jerarquia)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var root = all.FirstOrDefault(t => t.name == "Multimeter_VR_Art");
        if (root == null) { Debug.LogError("[VerifMMArt] No until Multimeter_VR_Art."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        Debug.Log($"[VerifMMArt] ── Jerarquía de Multimeter_VR_Art ──");
        Dump(root, 0);

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Dump(Transform t, int depth)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
        Debug.Log($"[VerifMMArt] {new string(' ', depth * 2)}{t.name}  [{string.Join(",", comps)}]  active={t.gameObject.activeSelf}");
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
    }
}
