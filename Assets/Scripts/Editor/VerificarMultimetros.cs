using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerificarMultimetros
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Verificar Multimetros en escena")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in todos)
            if (t.name.Contains("Multimeter") || t.name.Contains("Multimetro"))
            {
                var mm = t.GetComponent<Multimeter>();
                Debug.Log($"[VerifMM] '{t.name}' path={Path(t)} activeSelf={t.gameObject.activeSelf} tieneMultimeterScript={mm != null}");
            }

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        Debug.Log($"[VerifMM] GameManager.multimeter = {(gm.multimeter != null ? Path(gm.multimeter.transform) : "NULL")}");

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string Path(Transform t)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
