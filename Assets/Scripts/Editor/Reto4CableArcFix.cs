using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Activa <see cref="CableBoxSpawner.useForwardArc"/> en el CableBox_VR del Reto 4, para que el arco
/// de los jumpers se aleje de la protoboard inclinada usando el eje +Z (forward) de la caja en vez de
/// +Y (up) — a pedido del usuario, para que el arco quede horizontal y a la vista del Explorador en
/// vez de apuntar hacia arriba/de canto por la inclinación del atril.
///
/// Ejecutar: Tools → TITA → Reto 4 → Fix arco de cables (horizontal/Z)
/// </summary>
public static class Reto4CableArcFix
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string BoxName   = "CableBox_VR";

    [MenuItem("Tools/TITA/Reto 4/Fix arco de cables (horizontal-Z)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var (ok, msg) = DoFix();
        if (ok) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        Debug.Log("[Reto4CableArcFix] " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static (bool, string) DoFix()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();

        Transform boxT = all.FirstOrDefault(t => t.name == BoxName);
        if (boxT == null) return (false, $"No encontré '{BoxName}'.");

        var box = boxT.GetComponent<CableBoxSpawner>();
        if (box == null) return (false, $"'{BoxName}' no tiene CableBoxSpawner.");

        // Medido con VerificarEjeCableBox: forward (Y mundo≈0.055) apunta en PROFUNDIDAD hacia/desde
        // el jugador (el arco se ve casi plano por escorzo al mirar el tablero de frente); right
        // (Y mundo≈-0.030, igual de horizontal) queda de LADO A LADO en su campo visual — el que
        // realmente se lee como un arco. Cambiado de Forward a Right tras el reporte de que seguía
        // sin verse horizontal en VR.
        Undo.RecordObject(box, "arcAxis");
        box.arcAxis = VRCableRenderer.ArcAxis.Right;
        EditorUtility.SetDirty(box);

        return (true, $"'{BoxName}'.arcAxis = Right (antes: Forward). Los próximos cables dispensados arquearán de lado a lado, no en profundidad.");
    }
}
