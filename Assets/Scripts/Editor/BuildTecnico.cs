using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build del ejecutable del Técnico (Windows, PC plano host) desde Serious-Game.
///
/// Uso:
///   - Editor: Tools → TITA → Build → EXE Técnico (Windows)  (un clic, Unity ya abierto)
///   - Batch:  Unity.exe -quit -batchmode -projectPath "...Serious-Game"
///             -buildTarget Win64 -executeMethod BuildTecnico.BuildTecnicoBatch -logFile build.log
///
/// Incluye Tecnico.unity (índice 0) + NoonA.unity. NoonA es OBLIGATORIA porque
/// TecnicoBootstrapper la carga aditiva por nombre en runtime; si no está en la lista
/// de escenas del build, el entorno 3D no carga.
/// Salida: &lt;ProjectRoot&gt;/Build-Tecnico/Tecnico.exe
/// </summary>
public static class BuildTecnico
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Tecnico/Tecnico.unity", // índice 0
        "Assets/Scenes/Tecnico/NoonA.unity",   // aditiva por nombre
    };
    const string OutputDir = "Build-Tecnico";
    const string ExeName   = "Tecnico.exe";

    [MenuItem("Tools/TITA/Build/EXE Técnico (Windows)")]
    public static void BuildTecnicoMenu()
    {
        bool ok = BuildCore();
        EditorUtility.DisplayDialog("Build Técnico",
            ok ? "EXE generado correctamente en Build-Tecnico/Tecnico.exe"
               : "El build FALLÓ. Revisa la consola / Editor.log.",
            ok ? "OK" : "Cerrar");
    }

    /// <summary>Punto de entrada para build por línea de comandos (CI / batch).</summary>
    public static void BuildTecnicoBatch()
    {
        bool ok = BuildCore();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool BuildCore()
    {
        // 1) Validar escenas
        foreach (var s in Scenes)
        {
            if (!File.Exists(s))
            {
                Debug.LogError($"[BuildTecnico] No se encuentra la escena {s}");
                return false;
            }
        }

        // 2) La plataforma activa debe ser Windows Standalone. En batch se fija con
        //    -buildTarget Win64 al arrancar; aquí solo avisamos si no coincide.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("[BuildTecnico] La plataforma activa no es Win64. Cambiando...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[BuildTecnico] No se pudo cambiar a Windows Standalone.");
                return false;
            }
        }

        // 3) Ruta de salida: <ProjectRoot>/Build-Tecnico/Tecnico.exe
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outDir      = Path.Combine(projectRoot, OutputDir);
        Directory.CreateDirectory(outDir);
        string exePath     = Path.Combine(outDir, ExeName);

        var opts = new BuildPlayerOptions
        {
            scenes           = Scenes,
            locationPathName = exePath,
            target           = BuildTarget.StandaloneWindows64,
            targetGroup      = BuildTargetGroup.Standalone,
            options          = BuildOptions.None,
        };

        Debug.Log($"[BuildTecnico] Iniciando build → {exePath}\n  Escenas: {string.Join(", ", Scenes)}");

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildTecnico] OK ✅  {exePath}\n" +
                      $"  Tamaño: {summary.totalSize / (1024f * 1024f):0.0} MB   Tiempo: {summary.totalTime}");
            return true;
        }

        Debug.LogError($"[BuildTecnico] FALLÓ ❌  result={summary.result}  errores={summary.totalErrors}.");
        return false;
    }
}
