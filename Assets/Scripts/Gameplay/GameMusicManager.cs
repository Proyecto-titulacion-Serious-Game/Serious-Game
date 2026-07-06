using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Música de fondo en bucle, distinta por rol:
///   • Técnico (PC plano, escena Tecnico)  → música de ambiente de oficina.
///   • Explorador (VR, escena Explorador)   → "Space shuttle of the future".
///
/// AUTO-BOOTSTRAP: no va en ninguna escena, se crea solo al arrancar. Detecta el rol y
/// reproduce el loop correspondiente.
///
/// DÓNDE PONER LA MÚSICA (sin tocar el Inspector):
///   Suelta los archivos en  Assets/Resources/Audio/  con estos nombres EXACTOS
///   (la extensión .mp3/.ogg/.wav la resuelve Unity sola, NO la pongas en el nombre):
///       music_office          → ambiente de oficina (Técnico)
///       music_space_explorer  → "Space shuttle of the future" (Explorador)
///   Si el archivo no está, simplemente no suena (sin error).
///
/// Volumen ajustable abajo. La música es 2D (spatialBlend 0) y loopea.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class GameMusicManager : MonoBehaviour
{
    static GameMusicManager _instance;

    [Range(0f, 1f)] public float volumeOffice = 0.35f;   // ambiente: discreto, de fondo
    [Range(0f, 1f)] public float volumeSpace  = 0.45f;

    const string OfficeClip = "music_office";
    const string SpaceClip  = "music_space_explorer";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        if (FindAnyObjectByType<GameMusicManager>() != null) return;
        var go = new GameObject("[GameMusicManager]");
        go.AddComponent<AudioSource>();
        go.AddComponent<GameMusicManager>();
        DontDestroyOnLoad(go);
    }

    AudioSource _src;
    float       _baseVolume = 0.4f;   // volumen "100%" del rol (antes del factor de ajustes)

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _src = GetComponent<AudioSource>();
        _src.loop          = true;
        _src.playOnAwake   = false;
        _src.spatialBlend  = 0f;     // 2D
        _src.bypassEffects = true;
    }

    void OnEnable()  { GameSettings.Changed += ApplyVolume; }
    void OnDisable() { GameSettings.Changed -= ApplyVolume; }

    void Start() => PlayForRole();

    /// <summary>Recalcula el volumen real = volumen base del rol × ajuste de música del jugador.</summary>
    void ApplyVolume()
    {
        if (_src != null) _src.volume = Mathf.Clamp01(_baseVolume) * GameSettings.MusicVolume;
    }

    void PlayForRole()
    {
        bool esTecnico = EsTecnico();
        string clipName = esTecnico ? OfficeClip : SpaceClip;
        float  vol      = esTecnico ? volumeOffice : volumeSpace;

        var clip = Resources.Load<AudioClip>($"Audio/{clipName}");
        if (clip == null)
        {
            Debug.Log($"[GameMusicManager] Falta el clip Resources/Audio/{clipName} " +
                      $"(rol={(esTecnico ? "Técnico" : "Explorador")}). Suelta el archivo ahí para que suene.");
            return;
        }

        _src.clip   = clip;
        _baseVolume = Mathf.Clamp01(vol);
        ApplyVolume();              // volumen base del rol × ajuste de música
        _src.Play();
        Debug.Log($"[GameMusicManager] Reproduciendo '{clipName}' (rol={(esTecnico ? "Técnico" : "Explorador")}).");
    }

    /// <summary>Técnico = escena "Tecnico" cargada y sin XR. Explorador = VR / escena Explorador / Android.</summary>
    static bool EsTecnico()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return false;   // la APK es siempre el Explorador (Quest)
#else
        if (UnityEngine.XR.XRSettings.isDeviceActive) return false;  // VR activo → Explorador (PCVR)
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == "Tecnico") return true;
        return false;
#endif
    }

    /// <summary>Ajusta el volumen de la música en runtime (0-1).</summary>
    public void SetVolume(float v) { if (_src != null) _src.volume = Mathf.Clamp01(v); }
}
