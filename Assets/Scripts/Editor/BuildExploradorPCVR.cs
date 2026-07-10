using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;

/// <summary>
/// Build del ejecutable PCVR del Explorador (Windows + OpenXR, Quest via Link/Air Link).
///
/// Uso:
///   - Editor: Tools → TITA → Build → EXE PCVR (Explorador)
///   - Batch:  Unity.exe -quit -batchmode -projectPath "...Serious-Game"
///             -buildTarget Win64 -executeMethod BuildExploradorPCVR.BuildBatch -logFile build.log
///
/// IMPORTANTE — manejo de XR:
///   El target Standalone comparte config XR con el Técnico (PC plano), que necesita
///   XR SIN auto-iniciar (automaticLoading/Running = false) para no disparar el muro de
///   errores [MetaXRFeature]. Para el PCVR del Explorador hace falta lo contrario.
///   Este script ACTIVA automaticLoading/Running para Standalone justo para el build y los
///   RESTAURA a su valor previo al terminar (éxito o fallo), dejando el proyecto otra vez
///   listo para buildear el Técnico plano. El loader de OpenXR ya está asignado a Standalone
///   y InitManagerOnStart=1, así que con esto el .exe inicializa VR al arrancar.
///
/// Salida: &lt;ProjectRoot&gt;/Build-Explorador/Explorador.exe
/// </summary>
public static class BuildExploradorPCVR
{
    const string Scene   = "Assets/Scenes/Explorador.unity";
    const string OutDir  = "Build-Explorador";
    const string ExeName = "Explorador.exe";

    [MenuItem("Tools/TITA/Build/EXE PCVR (Explorador)")]
    public static void BuildMenu()
    {
        bool ok = Core();
        EditorUtility.DisplayDialog("Build PCVR Explorador",
            ok ? "EXE PCVR generado en Build-Explorador/Explorador.exe"
               : "El build FALLÓ. Revisa la consola / Editor.log.",
            ok ? "OK" : "Cerrar");
    }

    public static void BuildBatch()
    {
        bool ok = Core();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Core()
    {
        if (!File.Exists(Scene))
        {
            Debug.LogError($"[BuildPCVR] No se encuentra la escena {Scene}");
            return false;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("[BuildPCVR] La plataforma activa no es Win64. Cambiando...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[BuildPCVR] No se pudo cambiar a Windows Standalone.");
                return false;
            }
        }

        // ── Activar auto-init de XR para Standalone (guardando el estado previo) ──
        XRManagerSettings mgr = null;
        bool prevLoading = false, prevRunning = false, toggled = false;
        var xr = XRGeneralSettingsForStandalone();
        if (xr != null) mgr = xr.AssignedSettings;
        if (mgr != null)
        {
            prevLoading = mgr.automaticLoading;
            prevRunning = mgr.automaticRunning;
            mgr.automaticLoading = true;
            mgr.automaticRunning = true;
            toggled = true;
            EditorUtility.SetDirty(mgr);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildPCVR] XR Standalone auto-init ACTIVADO para el build " +
                      $"(previo: loading={prevLoading}, running={prevRunning}).");
        }
        else
        {
            Debug.LogWarning("[BuildPCVR] No pude obtener XRManagerSettings de Standalone. " +
                "Sigo con el build; si el .exe sale plano, activa OpenXR en XR Plug-in " +
                "Management → Desktop y revisa 'Initialize XR on Startup'.");
        }

        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, OutDir);
            Directory.CreateDirectory(outDir);
            string exePath = Path.Combine(outDir, ExeName);

            var opts = new BuildPlayerOptions
            {
                scenes           = new[] { Scene },
                locationPathName = exePath,
                target           = BuildTarget.StandaloneWindows64,
                targetGroup      = BuildTargetGroup.Standalone,
                // VERSIÓN FINAL (aula/defensa): sin teclas de debug ni marca de agua.
                // Para depurar, cambiar temporalmente a BuildOptions.Development.
                options          = BuildOptions.None,
            };

            Debug.Log($"[BuildPCVR] Iniciando build → {exePath}");
            // productName propio → carpeta de logs/PlayerPrefs separada de Tecnico.exe
            // (LocalLow/DefaultCompany/Explorador); sin esto los Player.log se pisan al
            // probar ambos roles en una misma laptop.
            string prevProduct = PlayerSettings.productName;
            PlayerSettings.productName = "Explorador";
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(opts); }
            finally { PlayerSettings.productName = prevProduct; }
            BuildSummary s = report.summary;

            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildPCVR] OK ✅  {exePath}\n" +
                          $"  Tamaño: {s.totalSize / (1024f * 1024f):0.0} MB   Tiempo: {s.totalTime}");
                return true;
            }
            Debug.LogError($"[BuildPCVR] FALLÓ ❌  result={s.result}  errores={s.totalErrors}.");
            return false;
        }
        finally
        {
            // Restaurar SIEMPRE el estado de XR para que el Técnico siga buildeando plano.
            if (toggled && mgr != null)
            {
                mgr.automaticLoading = prevLoading;
                mgr.automaticRunning = prevRunning;
                EditorUtility.SetDirty(mgr);
                AssetDatabase.SaveAssets();
                Debug.Log($"[BuildPCVR] XR Standalone restaurado a loading={prevLoading}, running={prevRunning}.");
            }
        }
    }

    static XRGeneralSettings XRGeneralSettingsForStandalone()
    {
        // API de UnityEditor.XR.Management: devuelve la config XR del grupo Standalone.
        return XRGeneralSettingsPerBuildTarget
            .XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
    }
}
