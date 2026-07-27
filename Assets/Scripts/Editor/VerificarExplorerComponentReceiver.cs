using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Verifica si ExplorerComponentReceiver realmente existe activo en Explorador.unity, y si
/// está suscrito a GameSession.OnComponenteRecibido (un fork de auditoría reportó "0 instancias",
/// pero grep de assets muestra que SÍ hay 1 PrefabInstance de ComponentReceiver.prefab en la escena
/// — verificar cuál es correcto con la API real de Unity, no con grep de YAML).</summary>
public static class VerificarExplorerComponentReceiver
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Verificar ExplorerComponentReceiver en escena")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var instancias = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[VerifECR] ExplorerComponentReceiver instancias en Explorador.unity = {instancias.Length}");
        foreach (var i in instancias)
            Debug.Log($"[VerifECR]   '{i.name}' path={Path(i.transform)} activeSelf={i.gameObject.activeSelf} activeInHierarchy={i.gameObject.activeInHierarchy} enabled={i.enabled}");

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string Path(Transform t)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
