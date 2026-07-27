using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build del ejecutable del Técnico para Linux 64-bit (mismo rol que BuildTecnico.cs, distinta
/// plataforma). El Técnico no depende de VR/KAT, así que no hay nada específico de Windows que
/// migrar salvo el BuildTarget y la extensión del binario de salida.
///
/// Uso:
///   - Editor: Tools → TITA → Build → EXE Técnico (Linux)  (un clic, Unity ya abierto)
///   - Batch:  Unity.exe -quit -batchmode -projectPath "...Serious-Game"
///             -buildTarget Linux64 -executeMethod BuildTecnicoLinux.BuildTecnicoLinuxBatch -logFile build.log
///
/// Incluye Tecnico.unity (índice 0) + NoonA.unity. NoonA es OBLIGATORIA porque
/// TecnicoBootstrapper la carga aditiva por nombre en runtime; si no está en la lista
/// de escenas del build, el entorno 3D no carga.
/// Salida: &lt;ProjectRoot&gt;/Build-Tecnico-Linux/Tecnico
/// </summary>
public static class BuildTecnicoLinux
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Tecnico/Tecnico.unity", // índice 0
        "Assets/Scenes/Tecnico/NoonA.unity",   // aditiva por nombre
    };
    const string OutputDir = "Build-Tecnico-Linux";
    const string ExeName   = "Tecnico";

    [MenuItem("Tools/TITA/Build/EXE Técnico (Linux)")]
    public static void BuildTecnicoLinuxMenu()
    {
        bool ok = BuildCore();
        EditorUtility.DisplayDialog("Build Técnico (Linux)",
            ok ? "Binario generado correctamente en Build-Tecnico-Linux/Tecnico"
               : "El build FALLÓ. Revisa la consola / Editor.log.",
            ok ? "OK" : "Cerrar");
    }

    /// <summary>Punto de entrada para build por línea de comandos (CI / batch).</summary>
    public static void BuildTecnicoLinuxBatch()
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
                Debug.LogError($"[BuildTecnicoLinux] No se encuentra la escena {s}");
                return false;
            }
        }

        // 2) La plataforma activa debe ser Linux Standalone. En batch se fija con
        //    -buildTarget Linux64 al arrancar; aquí solo avisamos si no coincide.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
        {
            Debug.Log("[BuildTecnicoLinux] La plataforma activa no es Linux64. Cambiando...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                Debug.LogError("[BuildTecnicoLinux] No se pudo cambiar a Linux Standalone.");
                return false;
            }
        }

        // 3) Ruta de salida: <ProjectRoot>/Build-Tecnico-Linux/Tecnico
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outDir      = Path.Combine(projectRoot, OutputDir);
        Directory.CreateDirectory(outDir);
        string exePath     = Path.Combine(outDir, ExeName);

        var opts = new BuildPlayerOptions
        {
            scenes           = Scenes,
            locationPathName = exePath,
            target           = BuildTarget.StandaloneLinux64,
            targetGroup      = BuildTargetGroup.Standalone,
            // VERSIÓN FINAL (aula/defensa): sin teclas de debug (F1-F4, F8-F11) ni marca de agua.
            options          = BuildOptions.None,
        };

        Debug.Log($"[BuildTecnicoLinux] Iniciando build → {exePath}\n  Escenas: {string.Join(", ", Scenes)}");

        // productName propio: mismo motivo que BuildTecnico.cs — no compartir carpeta de
        // logs/PlayerPrefs con Explorador si se prueban en la misma máquina.
        string prevProduct = PlayerSettings.productName;
        PlayerSettings.productName = "Tecnico";
        BuildReport report;
        try { report = BuildPipeline.BuildPlayer(opts); }
        finally { PlayerSettings.productName = prevProduct; }
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildTecnicoLinux] OK ✅  {exePath}\n" +
                      $"  Tamaño: {summary.totalSize / (1024f * 1024f):0.0} MB   Tiempo: {summary.totalTime}");
            return true;
        }

        Debug.LogError($"[BuildTecnicoLinux] FALLÓ ❌  result={summary.result}  errores={summary.totalErrors}.");
        return false;
    }
}
