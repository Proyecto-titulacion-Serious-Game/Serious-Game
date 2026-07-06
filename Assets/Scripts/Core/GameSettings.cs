using System;
using UnityEngine;

/// <summary>
/// Preferencias del jugador (sensibilidad de mouse y volúmenes), persistidas en PlayerPrefs.
/// Fuente única de verdad: los controladores y los sistemas de audio la consultan.
///
///   • MouseSensitivity → MULTIPLICADOR (1 = normal) que aplican TechnicianMover y
///     ThirdPersonCamera sobre su sensibilidad base.
///   • SfxVolume / MusicVolume → 0..1, multiplican el volumen de los efectos y la música.
///
/// El panel de Ajustes (en PauseMenu) escribe estos valores; <see cref="Changed"/> avisa a
/// quien necesite reaccionar en vivo (p.ej. la música ajusta su volumen al instante).
/// </summary>
public static class GameSettings
{
    const string K_Sens  = "TITA.MouseSensitivity";
    const string K_Sfx   = "TITA.SfxVolume";
    const string K_Music = "TITA.MusicVolume";

    public const float SensMin = 0.2f, SensMax = 3f;

    static float _sens = 1f, _sfx = 1f, _music = 1f;
    static bool  _loaded;

    /// <summary>Se dispara cuando cambia cualquier ajuste.</summary>
    public static event Action Changed;

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _sens   = PlayerPrefs.GetFloat(K_Sens,  1f);
        _sfx    = PlayerPrefs.GetFloat(K_Sfx,   1f);
        _music  = PlayerPrefs.GetFloat(K_Music, 1f);
        _loaded = true;
    }

    public static float MouseSensitivity
    {
        get { EnsureLoaded(); return _sens; }
        set { Set(ref _sens, Mathf.Clamp(value, SensMin, SensMax), K_Sens); }
    }

    public static float SfxVolume
    {
        get { EnsureLoaded(); return _sfx; }
        set { Set(ref _sfx, Mathf.Clamp01(value), K_Sfx); }
    }

    public static float MusicVolume
    {
        get { EnsureLoaded(); return _music; }
        set { Set(ref _music, Mathf.Clamp01(value), K_Music); }
    }

    static void Set(ref float field, float v, string key)
    {
        EnsureLoaded();
        if (Mathf.Approximately(field, v)) return;
        field = v;
        PlayerPrefs.SetFloat(key, v);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
