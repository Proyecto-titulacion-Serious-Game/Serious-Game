using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build del APK del Explorador (Meta Quest 3) desde Serious-Game.
///
/// Uso:
///   - Editor: Tools → TITA → Build → APK Quest (Explorador)  (un clic, Unity ya abierto)
///   - Batch:  Unity.exe -quit -batchmode -projectPath "...Serious-Game"
///             -buildTarget Android -executeMethod BuildQuest.BuildQuestBatch -logFile build.log
///
/// Solo incluye Explorador.unity (el laboratorio VR es autocontenido). Hereda los
/// PlayerSettings de Android del proyecto (IL2CPP + ARM64), no los modifica.
/// Salida: &lt;ProjectRoot&gt;/Explorador/Explorador.apk
/// </summary>
public static class BuildQuest
{
    const string ExploradorScene = "Assets/Scenes/Explorador.unity";
    const string OutputDir       = "Explorador";
    const string ApkName         = "Explorador.apk";

    [MenuItem("Tools/TITA/Build/APK Quest (Explorador)")]
    public static void BuildQuestMenu()
    {
        bool ok = BuildCore();
        if (ok)
            EditorUtility.DisplayDialog("Build Quest",
                "APK generado correctamente en Explorador/Explorador.apk", "OK");
        else
            EditorUtility.DisplayDialog("Build Quest",
                "El build FALLÓ. Revisa la consola / Editor.log.", "Cerrar");
    }

    /// <summary>Punto de entrada para build por línea de comandos (CI / batch).</summary>
    public static void BuildQuestBatch()
    {
        bool ok = BuildCore();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool BuildCore()
    {
        // 1) Validar escena
        if (!File.Exists(ExploradorScene))
        {
            Debug.LogError($"[BuildQuest] No se encuentra la escena {ExploradorScene}");
            return false;
        }

        // 2) La plataforma activa DEBE ser Android ANTES de construir. Cambiarla dispara una
        //    recompilación de scripts (con UNITY_ANDROID definido); construir en el mismo frame
        //    deja el editor con un layout de clase distinto al del player —p.ej. el campo
        //    condicional 'playerInput' de MobileDisableAutoSwitchControls (#if UNITY_ANDROID)—
        //    y el build aborta con "script class layout is incompatible".
        //    Por eso cambiamos de plataforma y PARAMOS: el usuario reintenta ya en Android.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[BuildQuest] La plataforma activa no es Android. Cambiando...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[BuildQuest] No se pudo cambiar a Android. " +
                               "¿Está instalado el módulo 'Android Build Support' (SDK/NDK/JDK) en este Editor?");
                return false;
            }
            EditorUtility.DisplayDialog("Build Quest — paso 1 de 2",
                "Plataforma cambiada a Android.\n\nEspera a que Unity termine de recompilar " +
                "(spinner abajo a la derecha) y vuelve a ejecutar\n" +
                "'Tools → TITA → Build → APK Quest (Explorador)'\npara construir el APK.", "OK");
            return false;
        }

        // 3) Ruta de salida: <ProjectRoot>/Explorador/Explorador.apk
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outDir      = Path.Combine(projectRoot, OutputDir);
        Directory.CreateDirectory(outDir);
        string apkPath     = Path.Combine(outDir, ApkName);

        // 4) Build (hereda IL2CPP/ARM64 + XR de PlayerSettings; no los toca)
        var opts = new BuildPlayerOptions
        {
            scenes           = new[] { ExploradorScene },
            locationPathName = apkPath,
            target           = BuildTarget.Android,
            targetGroup      = BuildTargetGroup.Android,
            // Development: activa DebugLevelSkipper (F1-F4 + combo de control VR) para poder saltar
            // retos probando en el Quest standalone. Revertir a BuildOptions.None para el APK final de aula.
            options          = BuildOptions.Development,
        };

        Debug.Log($"[BuildQuest] Iniciando build → {apkPath}\n" +
                  $"  Escena: {ExploradorScene}\n" +
                  $"  Backend: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}  " +
                  $"Arch: {PlayerSettings.Android.targetArchitectures}");

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildQuest] OK ✅  {apkPath}\n" +
                      $"  Tamaño: {summary.totalSize / (1024f * 1024f):0.0} MB   " +
                      $"Tiempo: {summary.totalTime}");
            return true;
        }

        Debug.LogError($"[BuildQuest] FALLÓ ❌  result={summary.result}  " +
                       $"errores={summary.totalErrors}. Revisa la consola / Editor.log.");
        return false;
    }
}
