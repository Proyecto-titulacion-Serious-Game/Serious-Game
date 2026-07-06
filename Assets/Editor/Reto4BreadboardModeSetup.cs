using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Engancha Reto4BreadboardMode al GameManager para activar el "modo protoboard" del Reto 4
/// (cada pata del componente en un hueco distinto). Ejecutable con Unity abierto.
/// </summary>
public static class Reto4BreadboardModeSetup
{
    [MenuItem("Tools/TITA/Reto 4/Modo Protoboard (patas en 2 slots)")]
    public static void Setup()
    {
        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            EditorUtility.DisplayDialog("Modo Protoboard",
                "No encontré un GameManager en la escena abierta (abrí Explorador.unity).", "OK");
            return;
        }

        var go = gm.gameObject;
        var comp = go.GetComponent<Reto4BreadboardMode>();
        if (comp == null)
        {
            comp = Undo.AddComponent<Reto4BreadboardMode>(go);
            Debug.Log($"[BreadboardSetup] Reto4BreadboardMode añadido a '{go.name}'.", go);
        }
        else
        {
            Debug.Log($"[BreadboardSetup] '{go.name}' ya tenía Reto4BreadboardMode.", go);
        }

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        EditorUtility.DisplayDialog("Modo Protoboard",
            "Listo. El GameManager tiene Reto4BreadboardMode.\n\n" +
            "Al entrar al Reto 4 (F4): se apagan los imanes de slot único y cada pata del " +
            "componente se engancha a su propio hueco (separación mín. configurable en el Inspector).\n\n" +
            "GUARDÁ la escena (Ctrl+S) y probá en Play.", "OK");
    }
}
