using UnityEngine;
using DigitalRuby.LightningBolt;

/// <summary>
/// Dispara un RAYO visual (asset Lightning Bolt) cuando el circuito está mal (cortocircuito o
/// falla detectada) o cuando un LED explota por sobrevoltaje (LEDBlowEffect.OnLEDBlown).
/// Funciona en los 4 retos: escucha tanto CircuitManager (Retos 1-3) como ProtoboardSimulator
/// (Reto 4). El sonido lo maneja CircuitAudioManager.
///
/// AUTO-BOOTSTRAP: no hace falta ponerlo en la escena. Se crea solo al cargar la escena.
/// El prefab del rayo se carga desde Resources/FX/RayoFalla (usa el menú
/// Tools → TITA → FX → Instalar Rayo de Falla en Resources para copiarlo ahí una sola vez).
/// Si no encuentra el prefab, el componente queda inactivo silenciosamente (el resto de FX va igual).
///
/// El LED solo explota en todos los retos MENOS el Reto 2 (eso lo gobierna LEDBlowEffect), así
/// que este FX hereda esa regla para el rayo del LED.
/// </summary>
public class CircuitFaultFX : MonoBehaviour
{
    static CircuitFaultFX _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        if (FindAnyObjectByType<CircuitFaultFX>() != null) return; // ya hay uno en la escena
        var go = new GameObject("[CircuitFaultFX]");
        _instance = go.AddComponent<CircuitFaultFX>();
        DontDestroyOnLoad(go);
    }

    [Header("Rayo (asset Lightning Bolt)")]
    [Tooltip("SimpleLightningBoltPrefab del asset LightningBolt. Si queda vacío se carga de Resources/FX/RayoFalla.")]
    public GameObject lightningPrefab;

    [Tooltip("Altura (m) desde la que cae el rayo sobre el componente afectado.")]
    public float alturaRayo = 0.4f;

    [Tooltip("Cuánto vive el rayo instanciado (s).")]
    public float duracion = 0.5f;

    [Tooltip("Tiempo mínimo entre rayos (s) para no spamear.")]
    public float cooldown = 0.4f;

    GameManager         _gm;
    ProtoboardSimulator _proto;
    float _cd;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        if (lightningPrefab == null)
            lightningPrefab = Resources.Load<GameObject>("FX/RayoFalla");
    }

    void OnEnable()
    {
        LEDBlowEffect.OnLEDBlown             += OnLedBlown;
        CircuitManager.OnCircuitChanged      += OnCircuitChanged;
        ProtoboardSimulator.OnCircuitChanged += OnCircuitChanged;
        GameManager.OnFaultDetected          += OnFault;
    }

    void OnDisable()
    {
        LEDBlowEffect.OnLEDBlown             -= OnLedBlown;
        CircuitManager.OnCircuitChanged      -= OnCircuitChanged;
        ProtoboardSimulator.OnCircuitChanged -= OnCircuitChanged;
        GameManager.OnFaultDetected          -= OnFault;
    }

    // LED explotado (ya excluye el Reto 2 desde LEDBlowEffect) → rayo sobre el LED.
    void OnLedBlown(LED led)
    {
        if (led != null) Flash(led.transform.position);
    }

    // Circuito mal = cortocircuito, en cualquiera de los dos motores.
    void OnCircuitChanged()
    {
        if (_gm == null) _gm = FindAnyObjectByType<GameManager>();
        bool isShort = (_gm != null && _gm.circuit != null && _gm.circuit.isShortCircuited);

        if (!isShort)
        {
            if (_proto == null) _proto = FindAnyObjectByType<ProtoboardSimulator>();
            isShort = _proto != null && _proto.isShortCircuited;
        }

        if (isShort) Flash(PosicionCircuito());
    }

    // Falla detectada por el GameManager.
    void OnFault(string _) => Flash(PosicionCircuito());

    Vector3 PosicionCircuito()
    {
        var src = FindAnyObjectByType<VoltageSource>();
        if (src != null) return src.transform.position;
        var cam = Camera.main;
        return cam != null ? cam.transform.position + cam.transform.forward * 0.8f : transform.position;
    }

    void Flash(Vector3 pos)
    {
        if (lightningPrefab == null || Time.time < _cd) return;
        _cd = Time.time + cooldown;

        var go = Instantiate(lightningPrefab, pos, Quaternion.identity);

        var lb = go.GetComponent<LightningBoltScript>();
        if (lb == null) lb = go.GetComponentInChildren<LightningBoltScript>();
        if (lb != null)
        {
            lb.StartObject   = null;
            lb.EndObject     = null;
            lb.StartPosition = pos + Vector3.up * alturaRayo;   // cae desde arriba
            lb.EndPosition   = pos;                              // hasta el componente
            lb.Trigger();
        }

        Destroy(go, duracion);
    }
}
