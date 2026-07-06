using UnityEngine;

/// <summary>
/// Reproduce efectos de sonido para los eventos del circuito en los 4 retos.
///
/// AUTO-BOOTSTRAP: no hace falta ponerlo en ninguna escena. Se crea solo al cargar la
/// escena (RuntimeInitializeOnLoadMethod, igual que CircuitVictoryEffect) y persiste con
/// DontDestroyOnLoad. Si ya existe una instancia (puesta a mano en una escena) no se duplica.
///
/// DÓNDE PONER LOS SONIDOS (lo más fácil — sin tocar el Inspector):
///   Suelta archivos de audio (.wav/.ogg/.mp3) en  Assets/Resources/Audio/  con ESTOS nombres:
///       sfx_component_installed   → al instalar/cambiar un componente
///       sfx_short_circuit         → cortocircuito (zumbido/chispa)   [suena en los 4 retos]
///       sfx_fault                 → falla detectada (alarma)
///       sfx_led_blown             → LED quemado por sobrevoltaje
///       sfx_level_start           → inicio de un reto (genérico)
///       sfx_success               → reto completado bien
///       sfx_failure               → reto fallado / tiempo agotado
///       sfx_victory               → victoria de un reto
///       sfx_fanfarria_mision      → fanfarria final (los 4 retos)
///   SONIDO DISTINTO POR RETO (opcional): si existe sfx_reto1_start … sfx_reto4_start
///   se usa ese en lugar de sfx_level_start al entrar a ese reto.
///   El nombre del archivo NO lleva extensión en el código; Unity la resuelve sola.
///   Los campos del Inspector (si pones este componente a mano) tienen prioridad sobre Resources.
///
/// ASSETS RECOMENDADOS (todos gratuitos / CC0):
///   • Kenney Interface Sounds   – kenney.nl/assets/interface-sounds     (clicks, beeps UI)
///   • Kenney Sci-Fi Sounds      – kenney.nl/assets/sci-fi-sounds        (futurista/laboratorio)
///   • Kenney Music Jingles      – kenney.nl/assets/music-jingles        (stings victoria/error)
///   • freesound.org "multimeter beep"  – ID 263133 (CC0)
///   • freesound.org "electric spark"   – ID 253173 (CC0)
///   • freesound.org "capacitor charge" – ID 399095 (CC0)
///   • freesound.org "circuit click"    – ID 220206 (CC0)
///
/// Eventos escuchados:
///   CircuitManager.OnCircuitChanged       → instalación / cortocircuito (Retos 1-3)
///   ProtoboardSimulator.OnCircuitChanged  → instalación / cortocircuito (Reto 4)
///   GameManager.OnFaultDetected           → sfxFault
///   GameManager.OnLevelLoaded             → sfxLevelStart / sfx_retoN_start
///   GameManager.OnLevelCompleted          → sfxSuccess / sfxFailure
///   GameManager.OnGameCompleted           → sfxFanfarriaMision / sfxVictory
///   LEDBlowEffect.OnLEDBlown              → sfxLedBlown
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CircuitAudioManager : MonoBehaviour
{
    // ── Auto-bootstrap ──────────────────────────────────────────────────
    static CircuitAudioManager _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        if (FindAnyObjectByType<CircuitAudioManager>() != null) return; // ya hay uno en la escena
        var go = new GameObject("[CircuitAudioManager]");
        go.AddComponent<AudioSource>();          // satisface [RequireComponent]
        go.AddComponent<CircuitAudioManager>();
        DontDestroyOnLoad(go);
    }

    [Header("Sonidos de circuito")]
    [Tooltip("Sonido al instalar o cambiar un componente en el circuito.")]
    public AudioClip sfxComponentInstalled;

    [Tooltip("Chispa / zumbido al detectar cortocircuito.")]
    public AudioClip sfxShortCircuit;

    [Tooltip("Alarma al detectar una falla en el circuito.")]
    public AudioClip sfxFault;

    [Header("Sonidos de progreso")]
    [Tooltip("Sting corto al iniciar un nuevo reto (genérico).")]
    public AudioClip sfxLevelStart;

    [Tooltip("Jingle de éxito al completar un reto.")]
    public AudioClip sfxSuccess;

    [Tooltip("Sonido de fallo al agotar el tiempo o completar con errores.")]
    public AudioClip sfxFailure;

    [Tooltip("Chispazo/explosión cuando un LED se quema por sobrevoltaje (LEDBlowEffect.OnLEDBlown). " +
             "Si queda vacío, usa sfxShortCircuit. No suena en el Reto 2 (el LED no explota ahí).")]
    public AudioClip sfxLedBlown;

    [Tooltip("Jingle de victoria al completar un reto importante.")]
    public AudioClip sfxVictory;

    [Tooltip("FANFARRIA ESPECIAL al completar la MISIÓN (los 4 retos). Si queda vacío, usa sfxVictory.")]
    public AudioClip sfxFanfarriaMision;

    [Header("Sonido de inicio POR RETO (opcional)")]
    [Tooltip("Índice 0 = Reto 1 … 3 = Reto 4. Si un slot está vacío se usa Resources/Audio/sfx_retoN_start " +
             "y, en su defecto, sfxLevelStart.")]
    public AudioClip[] sfxRetoStart = new AudioClip[4];

    [Header("Volúmenes (0-1)")]
    [Range(0f, 1f)] public float volumeCircuit   = 1f;
    [Range(0f, 1f)] public float volumeProgress  = 1f;
    [Tooltip("Volumen de la fanfarria final (la más alta para que destaque).")]
    [Range(0f, 1f)] public float volumeFanfarria = 1f;

    private AudioSource          _src;
    private GameManager          _gm;
    private ProtoboardSimulator  _proto;
    private bool                 _shortCircuitPlaying;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        _src = GetComponent<AudioSource>();
        _src.playOnAwake   = false;
        _src.spatialBlend  = 0f;     // 2D: se oye igual de fuerte en cualquier posición
        _src.volume        = 1f;     // el volumen real lo controla cada PlayOneShot
        _src.bypassEffects = true;

        _gm = FindAnyObjectByType<GameManager>();

        // Cargar clips faltantes desde Resources/Audio por convención de nombres.
        CargarDesdeResources();
    }

    void CargarDesdeResources()
    {
        if (sfxComponentInstalled == null) sfxComponentInstalled = Load("sfx_component_installed");
        if (sfxShortCircuit       == null) sfxShortCircuit       = Load("sfx_short_circuit");
        if (sfxFault              == null) sfxFault              = Load("sfx_fault");
        if (sfxLevelStart         == null) sfxLevelStart         = Load("sfx_level_start");
        if (sfxSuccess            == null) sfxSuccess            = Load("sfx_success");
        if (sfxFailure            == null) sfxFailure            = Load("sfx_failure");
        if (sfxLedBlown           == null) sfxLedBlown           = Load("sfx_led_blown");
        if (sfxVictory            == null) sfxVictory            = Load("sfx_victory");
        if (sfxFanfarriaMision    == null) sfxFanfarriaMision    = Load("sfx_fanfarria_mision");

        for (int i = 0; i < sfxRetoStart.Length; i++)
            if (sfxRetoStart[i] == null) sfxRetoStart[i] = Load($"sfx_reto{i + 1}_start");
    }

    static AudioClip Load(string nombre) => Resources.Load<AudioClip>($"Audio/{nombre}");

    void OnEnable()
    {
        CircuitManager.OnCircuitChanged      += OnCircuitChanged;
        ProtoboardSimulator.OnCircuitChanged += OnProtoChanged;
        GrabbableComponent.PlacedInSlot      += OnComponentPlaced;
        GameManager.OnFaultDetected          += OnFaultDetected;
        GameManager.OnLevelLoaded            += OnLevelLoaded;
        GameManager.OnLevelCompleted         += OnLevelCompleted;
        GameManager.OnGameCompleted          += OnGameCompleted;
        LEDBlowEffect.OnLEDBlown             += OnLedBlown;
    }

    void OnDisable()
    {
        CircuitManager.OnCircuitChanged      -= OnCircuitChanged;
        ProtoboardSimulator.OnCircuitChanged -= OnProtoChanged;
        GrabbableComponent.PlacedInSlot      -= OnComponentPlaced;
        GameManager.OnFaultDetected          -= OnFaultDetected;
        GameManager.OnLevelLoaded            -= OnLevelLoaded;
        GameManager.OnLevelCompleted         -= OnLevelCompleted;
        GameManager.OnGameCompleted          -= OnGameCompleted;
        LEDBlowEffect.OnLEDBlown             -= OnLedBlown;
    }

    // ── Handlers ────────────────────────────────────────────────────────

    // Retos 1-3 (CircuitManager). Aquí OnCircuitChanged se dispara al REPARAR (cambiar el valor de
    // una pieza fija), no al agarrar → mantener el sonido de "cambio" aquí es correcto.
    void OnCircuitChanged()
    {
        bool isShort = _gm?.circuit?.isShortCircuited ?? false;
        ReaccionarACircuito(isShort, playInstall: true);
    }

    // Reto 2/4 (ProtoboardSimulator). Este evento se dispara al MOVER un componente (agarrarlo marca
    // el sim dirty), así que NO reproducir aquí el sonido de "colocado" — solo el cortocircuito. El
    // sonido de colocado lo dispara GrabbableComponent.PlacedInSlot (al soltar encajado en un slot).
    void OnProtoChanged()
    {
        if (_proto == null) _proto = FindAnyObjectByType<ProtoboardSimulator>();
        bool isShort = _proto != null && _proto.isShortCircuited;
        ReaccionarACircuito(isShort, playInstall: false);
    }

    // Colocación real de una pieza en un slot (protoboard o ComponentSlot) → sonido de "colocado".
    void OnComponentPlaced() => Play(sfxComponentInstalled, volumeCircuit);

    void ReaccionarACircuito(bool isShort, bool playInstall)
    {
        if (isShort)
        {
            if (!_shortCircuitPlaying)
            {
                Play(sfxShortCircuit, volumeCircuit);
                _shortCircuitPlaying = true;
            }
        }
        else
        {
            _shortCircuitPlaying = false;
            if (playInstall) Play(sfxComponentInstalled, volumeCircuit);
        }
    }

    void OnFaultDetected(string _) => Play(sfxFault, volumeCircuit);

    // LED quemado por sobrevoltaje. El LEDBlowEffect ya NO dispara esto en el Reto 2.
    void OnLedBlown(LED _) => Play(sfxLedBlown != null ? sfxLedBlown : sfxShortCircuit, volumeCircuit);

    void OnLevelLoaded(LevelType level)
    {
        int idx = (int)level;
        AudioClip clip = (idx >= 0 && idx < sfxRetoStart.Length && sfxRetoStart[idx] != null)
                       ? sfxRetoStart[idx]
                       : sfxLevelStart;
        Play(clip, volumeProgress);
    }

    void OnLevelCompleted(LevelType _, bool success)
        => Play(success ? sfxSuccess : sfxFailure, volumeProgress);

    void OnGameCompleted()
    {
        // Fanfarria especial de fin de misión (los 4 retos). Respaldo a sfxVictory si no se asignó.
        AudioClip fanfarria = sfxFanfarriaMision != null ? sfxFanfarriaMision : sfxVictory;
        Play(fanfarria, volumeFanfarria);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || _src == null) return;
        _src.PlayOneShot(clip, Mathf.Clamp01(volume) * GameSettings.SfxVolume);
    }
}
