using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Arregla la punta ROJA (positiva) del multímetro, que quedó imposible de agarrar en la
/// instancia de escena (Explorador.unity) — el cuerpo (Probe_Red_Tip) y sus 2 SphereCollider
/// tienen overrides de Transform con valores que no son mirror de la punta negra (que sí
/// funciona): posición/rotación descuadradas y los colliders con centro/radio corridos muy
/// lejos del origen (probablemente arrastrados sin querer en el editor).
///
/// Fix: en vez de adivinar valores "correctos", ESPEJA la punta negra (que funciona) hacia la
/// roja — mismo Y/Z, X con el signo invertido, misma rotación (espejada sobre el eje X, que es
/// invariante para una rotación pura de X), y copia los colliders 1:1 (mismo radio/centro que
/// Black, que usa los defaults del prefab). Así cualquier ajuste futuro que se le haga a la
/// punta negra (que ya se sabe que funciona) se puede volver a espejar corriendo este mismo tool.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod MultimeterProbeRedMirrorFix.RunBatch -logFile -
///           Editor: Tools → TITA → Multímetro → Espejar punta roja desde la negra (fix agarre)
/// </summary>
public static class MultimeterProbeRedMirrorFix
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Multímetro/Espejar punta roja desde la negra (fix agarre)")]
    public static void RunMenu()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty)
        {
            bool save = EditorUtility.DisplayDialog("Multímetro — fix punta roja",
                "Explorador.unity tiene cambios sin guardar. Hay que recargar la escena para aplicar el fix; " +
                "¿guardar esos cambios primero?", "Guardar y continuar", "Cancelar");
            if (!save) return;
            EditorSceneManager.SaveScene(active);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool ok = Run(out string msg);
        if (ok)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        EditorUtility.DisplayDialog("Multímetro — fix punta roja", msg, "OK");
    }

    public static void RunBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool ok = Run(out string msg);
        if (ok)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log($"[MultimeterProbeRedMirrorFix] {msg}");
    }

    static bool Run(out string msg)
    {
        var redGO   = GameObject.Find("Probe_Red_Tip");
        var blackGO = GameObject.Find("Probe_Black_Tip");
        if (redGO == null || blackGO == null)
        {
            msg = $"No se encontró {(redGO == null ? "Probe_Red_Tip" : "Probe_Black_Tip")} en la escena.";
            return false;
        }

        Undo.RegisterFullObjectHierarchyUndo(redGO, "Espejar punta roja del multímetro");

        // ── Transform: espejo sobre X (Cable_Red y Cable_Black cuelgan del mismo padre en origen) ──
        var bt = blackGO.transform;
        var rt = redGO.transform;
        rt.localPosition = new Vector3(-bt.localPosition.x, bt.localPosition.y, bt.localPosition.z);
        // Mirror de un quaternion sobre el plano YZ (invierte el eje X): (x,-y,-z,w).
        var bq = bt.localRotation;
        rt.localRotation = new Quaternion(bq.x, -bq.y, -bq.z, bq.w);

        // ── Colliders: copiar 1:1 de Black (usa los defaults del prefab, sin overrides raros) ──
        var redCols   = redGO.GetComponents<SphereCollider>();
        var blackCols = blackGO.GetComponents<SphereCollider>();
        int colsCopied = 0;
        foreach (var rc in redCols)
        {
            foreach (var bc in blackCols)
            {
                if (rc.isTrigger != bc.isTrigger) continue;
                rc.radius = bc.radius;
                rc.center = new Vector3(-bc.center.x, bc.center.y, bc.center.z);
                colsCopied++;
                break;
            }
        }

        // ── MultimeterProbe: mismo detectionRadius/triggerThreshold que la negra ──
        var redProbe   = redGO.GetComponent<MultimeterProbe>();
        var blackProbe = blackGO.GetComponent<MultimeterProbe>();
        if (redProbe != null && blackProbe != null)
        {
            redProbe.detectionRadius  = blackProbe.detectionRadius;
            redProbe.triggerThreshold = blackProbe.triggerThreshold;
        }

        EditorUtility.SetDirty(redGO);
        msg = $"Punta roja espejada desde la negra: pos={rt.localPosition}, rot={rt.localRotation.eulerAngles}, " +
              $"{colsCopied} collider(s) copiados.";
        return true;
    }
}
