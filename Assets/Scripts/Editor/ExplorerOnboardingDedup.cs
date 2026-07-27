#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Hay DOS GameObjects "OnboardingController" en Explorador.unity, ambos siempre activos, ambos con
/// ExplorerOnboarding — construyen 2 canvases de bienvenida superpuestos ("Bienvenido al Laboratorio
/// Virtual..."). Se quita el componente ExplorerOnboarding del segundo (deja su GameObject y la
/// jerarquía que tiene debajo intacta — no es una copia limpia, tiene un prefab de escenografía
/// anidado sin relación con el onboarding, no conviene borrar el GameObject entero).
/// </summary>
public static class ExplorerOnboardingDedup
{
    [MenuItem("Tools/TITA/Diagnóstico/Deduplicar OnboardingController (headless-safe)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Explorador.unity", OpenSceneMode.Single);

        var all = Object.FindObjectsByType<ExplorerOnboarding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[ExplorerOnboardingDedup] Instancias de ExplorerOnboarding encontradas: {all.Length}");

        int removidas = 0;
        for (int i = 1; i < all.Length; i++)   // conserva la primera, quita el resto
        {
            Debug.Log($"[ExplorerOnboardingDedup] Quitando componente duplicado en '{all[i].gameObject.name}' (GameObject se conserva).");
            Object.DestroyImmediate(all[i]);
            removidas++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[ExplorerOnboardingDedup] ✓ Listo. Componentes duplicados removidos={removidas}");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
#endif
