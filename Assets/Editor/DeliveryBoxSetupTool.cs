using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Engancha DeliveryBoxRecipient al GO "box" de Explorador.unity para convertirlo en
/// recipiente fijo hueco y unificar las bandejas. Ejecutable con Unity abierto.
/// </summary>
public static class DeliveryBoxSetupTool
{
    [MenuItem("Tools/TITA/Explorador/Caja como Recipiente (unificar bandejas)")]
    public static void Setup()
    {
        var box = EncontrarCaja();
        if (box == null)
        {
            EditorUtility.DisplayDialog("Caja Recipiente",
                "No encontré un GO 'box' con Rigidbody + BoxCollider en la escena abierta.\n\n" +
                "Abrí Explorador.unity y asegurate de que la caja se llame 'box'.", "OK");
            return;
        }

        var comp = box.GetComponent<DeliveryBoxRecipient>();
        if (comp == null)
        {
            comp = Undo.AddComponent<DeliveryBoxRecipient>(box);
            Debug.Log($"[DeliveryBoxSetup] DeliveryBoxRecipient añadido a '{box.name}'.", box);
        }
        else
        {
            Debug.Log($"[DeliveryBoxSetup] '{box.name}' ya tenía DeliveryBoxRecipient.", box);
        }

        EditorUtility.SetDirty(box);
        EditorSceneManager.MarkSceneDirty(box.scene);
        Selection.activeGameObject = box;
        EditorGUIUtility.PingObject(box);

        EditorUtility.DisplayDialog("Caja Recipiente",
            "Listo. 'box' tiene DeliveryBoxRecipient.\n\n" +
            "Se configura al entrar en Play: caja fija + hueca, componentes caen dentro " +
            "y se agarran; todas las bandejas/receivers se reapuntan a la caja.\n\n" +
            "GUARDÁ la escena (Ctrl+S) y probá en Play (F4 para Reto 4 si aplica).", "OK");
    }

    static GameObject EncontrarCaja()
    {
        // Prioridad: GO llamado exactamente 'box' con Rigidbody + BoxCollider.
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (!string.Equals(t.name, "box", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (t.GetComponent<BoxCollider>() != null && t.GetComponent<Rigidbody>() != null)
                return t.gameObject;
        }
        // Fallback: cualquier GO llamado 'box'.
        var any = GameObject.Find("box");
        return any;
    }
}
