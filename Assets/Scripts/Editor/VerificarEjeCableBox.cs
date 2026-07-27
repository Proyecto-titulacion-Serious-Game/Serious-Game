using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Calcula, en coordenadas de MUNDO, hacia dónde apuntan los ejes forward/up/right del
/// CableBox_VR del Reto 4 — para saber matemáticamente cuál eje es realmente "horizontal" (poca
/// componente Y) en vez de asumirlo.</summary>
public static class VerificarEjeCableBox
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Verificar eje del CableBox (mundo)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var box = all.FirstOrDefault(t => t.name == "CableBox_VR");
        if (box == null) { Debug.LogError("[VerifEje] No until CableBox_VR."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        Debug.Log($"[VerifEje] CableBox_VR worldPos={box.position} worldRot(euler)={box.rotation.eulerAngles}");
        Debug.Log($"[VerifEje] CableBox_VR.forward (mundo) = {box.forward}  (componente Y = {box.forward.y:F3})");
        Debug.Log($"[VerifEje] CableBox_VR.up      (mundo) = {box.up}       (componente Y = {box.up.y:F3})");
        Debug.Log($"[VerifEje] CableBox_VR.right   (mundo) = {box.right}    (componente Y = {box.right.y:F3})");

        string masHorizontal = "forward";
        float mejorY = Mathf.Abs(box.forward.y);
        if (Mathf.Abs(box.up.y) < mejorY) { masHorizontal = "up"; mejorY = Mathf.Abs(box.up.y); }
        if (Mathf.Abs(box.right.y) < mejorY) { masHorizontal = "right"; mejorY = Mathf.Abs(box.right.y); }
        Debug.Log($"[VerifEje] Eje MÁS HORIZONTAL (menor componente Y absoluta) = '{masHorizontal}' (|Y|={mejorY:F3})");

        // Además: ¿hacia dónde apunta cada eje respecto al centro de la protoboard? El que se aleje
        // de la superficie del tablero (no hacia adentro/hacia el suelo) es el candidato correcto
        // para el arco "hacia el jugador".
        var bb = all.FirstOrDefault(t => t.name == "Bareboard" && t.parent != null && t.parent.name == "Reto4_TiltGroup");
        if (bb != null)
        {
            Vector3 haciaCaja = (box.position - bb.position).normalized;
            Debug.Log($"[VerifEje] Dirección Bareboard→CableBox (mundo) = {haciaCaja}");
            Debug.Log($"[VerifEje]   dot(forward,haciaCaja)={Vector3.Dot(box.forward, haciaCaja):F3}  dot(up,haciaCaja)={Vector3.Dot(box.up, haciaCaja):F3}  dot(right,haciaCaja)={Vector3.Dot(box.right, haciaCaja):F3}");
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
