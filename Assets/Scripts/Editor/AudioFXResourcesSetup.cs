#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prepara los recursos que CircuitAudioManager y CircuitFaultFX cargan por código en runtime:
///   • Copia el prefab del rayo (asset Lightning Bolt) a Resources/FX/RayoFalla.prefab.
///   • Crea la carpeta Resources/Audio donde van los .wav de los efectos de sonido.
///   • Reporta en consola qué clips de audio faltan todavía.
/// Ejecutar una sola vez desde  Tools → TITA → FX.
/// </summary>
public static class AudioFXResourcesSetup
{
    const string LIGHTNING_SRC = "Assets/LightningBolt/SimpleLightningBoltPrefab.prefab";
    const string FX_DIR        = "Assets/Resources/FX";
    const string RAYO_DST      = "Assets/Resources/FX/RayoFalla.prefab";
    const string AUDIO_DIR     = "Assets/Resources/Audio";

    static readonly string[] ClipsEsperados =
    {
        "sfx_component_installed", "sfx_short_circuit", "sfx_fault",
        "sfx_level_start", "sfx_success", "sfx_failure",
        "sfx_led_blown", "sfx_victory", "sfx_fanfarria_mision",
        "sfx_reto1_start", "sfx_reto2_start", "sfx_reto3_start", "sfx_reto4_start",
    };

    [MenuItem("Tools/TITA/FX/Setup Audio + Rayo (Resources)")]
    public static void Run()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder(FX_DIR);
        EnsureFolder(AUDIO_DIR);

        InstalarRayo();
        ReportarClips();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AudioFXResourcesSetup] Listo. Revisa la consola para los clips de audio pendientes.");
    }

    [MenuItem("Tools/TITA/FX/Instalar Rayo de Falla en Resources")]
    public static void InstalarRayoMenu()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder(FX_DIR);
        InstalarRayo();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void InstalarRayo()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RAYO_DST) != null)
        {
            Debug.Log($"[AudioFXResourcesSetup] El rayo ya existe en {RAYO_DST}.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LIGHTNING_SRC) == null)
        {
            Debug.LogWarning($"[AudioFXResourcesSetup] No se encontró {LIGHTNING_SRC}. " +
                             "Copia manualmente cualquier prefab de rayo a " + RAYO_DST + ".");
            return;
        }
        if (AssetDatabase.CopyAsset(LIGHTNING_SRC, RAYO_DST))
            Debug.Log($"[AudioFXResourcesSetup] Rayo copiado a {RAYO_DST}. CircuitFaultFX lo cargará solo.");
        else
            Debug.LogError("[AudioFXResourcesSetup] Falló la copia del prefab del rayo.");
    }

    static void ReportarClips()
    {
        int faltan = 0;
        foreach (var nombre in ClipsEsperados)
        {
            if (Resources.Load<AudioClip>($"Audio/{nombre}") == null)
            {
                Debug.Log($"[Audio] FALTA: arrastra un sonido a {AUDIO_DIR}/{nombre}.wav (.ogg/.mp3 también valen).");
                faltan++;
            }
        }
        if (faltan == 0)
            Debug.Log("[Audio] Todos los clips esperados están presentes. 🎵");
        else
            Debug.Log($"[Audio] Faltan {faltan}/{ClipsEsperados.Length} clips. Los slots vacíos simplemente no suenan.");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf   = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
