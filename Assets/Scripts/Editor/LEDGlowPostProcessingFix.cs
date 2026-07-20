using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Diagnóstico real: por más que se afine la emisión del LED en código (glowIntensity,
/// BoostVictoria, etc.), en Explorador.unity el brillo NUNCA se puede ver, por dos razones
/// independientes, ambas necesarias:
///
///   1. El GameObject "Global Volume" (Volume, m_IsGlobal=1) tiene sharedProfile = None. Sin
///      perfil, no hay Bloom ni ningún otro post-proceso activo en la escena.
///   2. La cámara XR (UniversalAdditionalCameraData, m_AllowXRRendering=1) tiene
///      renderPostProcessing = false. Aunque hubiera un perfil con Bloom, la cámara no lo
///      aplicaría igual.
///
/// Este tool: (a) crea/reutiliza un VolumeProfile mínimo SOLO con Bloom (para no arrastrar
/// Vignette/Tonemapping/MotionBlur de otros perfiles del proyecto — MotionBlur en particular es
/// mala idea en VR, causa mareo) y lo asigna al Global Volume; (b) activa Post Processing en la(s)
/// cámara(s) XR de la escena.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod LEDGlowPostProcessingFix.RunBatch -logFile -
///           Editor: Tools → TITA → Efectos → Activar Bloom (brillo de LEDs)
/// </summary>
public static class LEDGlowPostProcessingFix
{
    const string ScenePath   = "Assets/Scenes/Explorador.unity";
    const string ProfilePath = "Assets/Settings/TITA_LEDGlow_VolumeProfile.asset";

    [MenuItem("Tools/TITA/Efectos/Activar Bloom (brillo de LEDs)")]
    public static void RunMenu()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty)
        {
            bool save = EditorUtility.DisplayDialog("TITA — Bloom de LEDs",
                "Explorador.unity tiene cambios sin guardar. Hay que recargar la escena para aplicar el fix; " +
                "¿guardar esos cambios primero?", "Guardar y continuar", "Cancelar");
            if (!save) return;
            EditorSceneManager.SaveScene(active);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string msg = Run();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("TITA — Bloom de LEDs", msg, "OK");
    }

    public static void RunBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string msg = Run();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[LEDGlowPostProcessingFix] {msg}");
    }

    static string Run()
    {
        // ── 1) Perfil de Volume con Bloom (crear si no existe, reutilizar si ya está) ──
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        if (!profile.TryGet(out Bloom bloom))
            bloom = profile.Add<Bloom>(true);

        bloom.active = true;
        bloom.threshold.overrideState = true;  bloom.threshold.value  = 1.0f;
        bloom.intensity.overrideState = true;  bloom.intensity.value  = 0.55f;
        bloom.scatter.overrideState   = true;  bloom.scatter.value    = 0.6f;
        bloom.tint.overrideState      = true;  bloom.tint.value       = Color.white;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // ── 2) Asignar el perfil al Global Volume de la escena ──
        int volumesFixed = 0;
        foreach (var vol in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (vol == null || !vol.isGlobal) continue;
            if (vol.sharedProfile == null)
            {
                vol.sharedProfile = profile;
                EditorUtility.SetDirty(vol);
                volumesFixed++;
            }
        }

        // ── 3) Activar Post Processing en las cámaras XR ──
        int camsFixed = 0;
        foreach (var camData in Object.FindObjectsByType<UniversalAdditionalCameraData>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (camData == null) continue;
            if (!camData.renderPostProcessing)
            {
                camData.renderPostProcessing = true;
                EditorUtility.SetDirty(camData);
                camsFixed++;
            }
        }

        return $"Perfil Bloom: '{ProfilePath}' (intensity=0.55, threshold=1.0).\n" +
               $"Global Volume(es) con perfil asignado: {volumesFixed}.\n" +
               $"Cámara(s) con Post Processing activado: {camsFixed}.\n\n" +
               (volumesFixed == 0 && camsFixed == 0
                   ? "Nada que arreglar — ya estaba todo configurado."
                   : "Guardado. Los LEDs ahora deberían verse brillar de verdad en VR.");
    }
}
