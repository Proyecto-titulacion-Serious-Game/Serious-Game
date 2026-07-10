using UnityEngine;

/// <summary>
/// Hace que la LED "explote y salga volando" cuando recibe una sobrecarga catastrófica:
/// corriente por encima de <see cref="blowCurrentThreshold"/> (default 1 A) O cuando está
/// energizada SIN una resistencia de protección adecuada (>= <see cref="minProtectionOhms"/>).
///
/// Por diseño (pedido del juego):
///   • Con resistencia presente (ej. 10 Ω) la LED NO explota — se comporta como siempre.
///   • Solo el caso extremo (sin resistencia / corriente exagerada) la lanza por los aires.
///
/// Tras explotar, la LED queda "quemada" (deja de conducir) y dispara <see cref="OnLEDBlown"/>
/// para que el flujo de reemplazo (el Técnico re-entrega una LED por red) reaccione.
///
/// Se auto-añade a cada LED desde AutoSmokeSetup, así que no requiere wiring por componente.
/// </summary>
[RequireComponent(typeof(LED))]
[DisallowMultipleComponent]
public class LEDBlowEffect : MonoBehaviour
{
    [Header("Condición de explosión")]
    [Tooltip("Corriente (A) a partir de la cual la LED explota SIEMPRE, sin importar la protección. " +
             "Default 0.1 A (100 mA): un LED real de 20 mA no sobrevive 5× su corriente nominal.")]
    public float blowCurrentThreshold = 0.1f;

    [Tooltip("Si la LED está energizada y NO hay una resistencia de protección >= este valor, explota. " +
             "Default 5 Ω → con 10 Ω es seguro; sin resistencia (o ~0 Ω) explota.")]
    public float minProtectionOhms = 5f;

    [Tooltip("Si true, la falta de resistencia de protección hace explotar la LED.")]
    public bool blowOnMissingResistor = true;

    [Header("Lanzamiento (salir volando)")]
    [Tooltip("Fuerza del impulso con el que la LED sale disparada.")]
    public float launchImpulse = 2.5f;
    [Tooltip("Torque (giro) al salir volando.")]
    public float launchTorque  = 6f;
    [Tooltip("Sesgo de dirección en espacio LOCAL (Y = hacia arriba, Z = hacia afuera).")]
    public Vector3 launchDirBias = new Vector3(0f, 1f, 0.4f);

    [Header("Efecto (opcional)")]
    [Tooltip("Estallido de chispas/explosión. Si queda vacío, reúsa el ComponentSmokeEffect del componente.")]
    public ParticleSystem burstEffect;

    /// <summary>Se dispara cuando una LED explota. La escucha el flujo de reemplazo por red.</summary>
    public static event System.Action<LED> OnLEDBlown;

    // ─────────────────────────────────────────────
    private LED   _led;
    private GameManager _gm;
    private bool  _blown;
    private float _scanCd;
    private bool  _hasProtectionCached;
    private bool  _energizedCached;
    private float _enabledTime;   // para dar una gracia tras cargar el reto (explosión no en frame 0)

    [Header("Gracia al energizar (Reto 2)")]
    [Tooltip("Segundos tras activar el LED antes de permitir la explosión por polaridad, para que " +
             "el jugador VEA el estallido y no ocurra instantáneo al cargar la escena.")]
    public float blowGraceSeconds = 2f;

    // Transform original, para devolver la LED a su sitio del circuito tras un reemplazo.
    private Transform  _origParent;
    private Vector3    _origLocalPos;
    private Quaternion _origLocalRot;
    private bool       _origCaptured;

    void Awake() => _led = GetComponent<LED>();

    void OnEnable()
    {
        _enabledTime = Time.time;
        CircuitManager.OnCircuitChanged      += Evaluate;
        ProtoboardSimulator.OnCircuitChanged += Evaluate;
    }

    void OnDisable()
    {
        CircuitManager.OnCircuitChanged      -= Evaluate;
        ProtoboardSimulator.OnCircuitChanged -= Evaluate;
    }

    // ─────────────────────────────────────────────
    //  Detección
    // ─────────────────────────────────────────────
    void Evaluate()
    {
        if (_blown || _led == null) return;
        if (_led.nodeA == null || _led.nodeB == null) return; // LED sin cablear (bandeja) → no aplica

        if (_gm == null) _gm = FindAnyObjectByType<GameManager>();

        AssessCircuit(); // throttled: refresca _energizedCached y _hasProtectionCached

        // IMPORTANTE: no dependemos de _led.current. Cuando falta la resistencia, la sim del Reto 1
        // APAGA el LED (current 0), así que aquí miramos la fuente + el switch directamente.
        if (!_energizedCached) return;

        // Reto 2 (Parallel, diseño de piezas fijas): NO explota — la falla es la POLARIDAD, que se
        // resuelve corrigiendo el LED (invertido = apagado, no estalla). Los LEDs fijos no tienen
        // resistencia física de protección (la limita SimulateParallel), así que sin esta exclusión
        // explotarían al energizar.
        if (_gm != null && _gm.currentLevel == LevelType.Parallel) return;

        if (Time.time - _enabledTime < blowGraceSeconds) return;

        // Resto de retos: el LED explota por SOBRECORRIENTE o energizado SIN resistencia de protección.
        bool overCurrent  = Mathf.Abs(_led.current) >= blowCurrentThreshold;
        bool noProtection = blowOnMissingResistor && !_hasProtectionCached;

        if (overCurrent || noProtection)
            Blow();
    }

    /// <summary>
    /// Refresca (con throttle ~0.3 s) si el circuito está ENERGIZADO y si hay una resistencia de
    /// PROTECCIÓN en serie con esta LED. Scene-wide porque en Retos 1-3 los componentes son piezas
    /// fijas, no siempre hijos de un CircuitManager.
    /// </summary>
    void AssessCircuit()
    {
        if (Time.time < _scanCd) return;
        _scanCd = Time.time + 0.3f;

        // ── Energizado: hay fuente con voltaje y, si existe un switch, está encendido ──
        bool srcOn = false;
        foreach (var s in FindObjectsByType<VoltageSource>(FindObjectsInactive.Exclude))
            if (s != null && !s.isOpenCircuit && s.GetEffectiveVoltage() > 0.1f) { srcOn = true; break; }

        bool anySwitch = false, switchOn = false;
        foreach (var sw in FindObjectsByType<CircuitSwitch>(FindObjectsInactive.Exclude))
        {
            if (sw == null) continue;
            anySwitch = true;
            if (sw.isOn) { switchOn = true; break; }
        }
        _energizedCached = srcOn && (!anySwitch || switchOn);

        // ── Protección: un Resistor cableado, >= minProtectionOhms, que comparta nodo con la LED
        //    (i.e. está en serie con ella). Si lo quitan/desconectan, deja de compartir nodo. ──
        _hasProtectionCached = false;
        foreach (var r in FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
        {
            if (r == null || r.nodeA == null || r.nodeB == null) continue;
            if (r.GetResistance() < minProtectionOhms) continue;
            if (SharesNode(r, _led)) { _hasProtectionCached = true; break; }
        }
    }

    static bool SharesNode(ElectricalComponent a, ElectricalComponent b) =>
        a.nodeA == b.nodeA || a.nodeA == b.nodeB || a.nodeB == b.nodeA || a.nodeB == b.nodeB;

    /// <summary>Prueba la explosión a mano: selecciona la LED en runtime → clic derecho en este componente.</summary>
    [ContextMenu("💥 Probar explosión (Blow)")]
    void DebugBlow() => Blow();

    // ─────────────────────────────────────────────
    //  Explosión
    // ─────────────────────────────────────────────
    /// <summary>Quema la LED, dispara el efecto y la lanza por los aires. Idempotente.</summary>
    public void Blow()
    {
        if (_blown) return;
        _blown = true;

        // 1. Eléctricamente "quemada": deja de conducir (GetResistance → 1 MΩ).
        _led.isOpenCircuit = true;
        _led.isOn = false;

        // 2. Efecto visual: burst dedicado o, en su defecto, el humo/chispas del componente.
        if (burstEffect != null) burstEffect.Play(true);
        var smoke = GetComponent<ComponentSmokeEffect>();
        if (smoke != null) smoke.ForcePlay();
        var spark = GetComponent<ShortCircuitSparkEffect>();
        if (spark != null) spark.ForcePlay();

        // 3. Sale volando. Guardamos su sitio original y la desemparentamos para que la física
        //    no pelee con el transform padre. Al reemplazarla, ResetBlown la devuelve a su lugar.
        if (!_origCaptured)
        {
            _origParent   = transform.parent;
            _origLocalPos = transform.localPosition;
            _origLocalRot = transform.localRotation;
            _origCaptured = true;
        }
        transform.SetParent(null, true);

        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity  = true;

        Vector3 dir = (transform.up      * launchDirBias.y +
                       transform.forward * launchDirBias.z +
                       Random.insideUnitSphere * 0.4f).normalized;
        rb.AddForce(dir * launchImpulse, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * launchTorque, ForceMode.Impulse);

        Debug.LogWarning($"[LEDBlowEffect] LED '{name}' QUEMADA y lanzada " +
                         $"(I={_led.current * 1000f:F0} mA). El Técnico debe re-entregar una LED.");

        OnLEDBlown?.Invoke(_led);
    }

    /// <summary>True si la LED ya explotó (la usa el flujo de reemplazo).</summary>
    public bool IsBlown => _blown;

    /// <summary>
    /// Restaura TODAS las LEDs quemadas de la escena a su sitio y estado operativo.
    /// La llama <see cref="ComponentDeliverySystem"/> cuando el Técnico re-entrega una LED.
    /// </summary>
    public static void RestoreAllBlown(bool inverted = false)
    {
        foreach (var b in FindObjectsByType<LEDBlowEffect>(FindObjectsInactive.Include))
            if (b != null && b.IsBlown) b.ResetBlown(inverted);
    }

    /// <summary>Restaura esta LED: la devuelve a su posición del circuito y la reactiva.</summary>
    public void ResetBlown(bool inverted = false)
    {
        _blown = false;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
            rb.useGravity      = false;
        }

        // Volver a su sitio en el circuito.
        if (_origCaptured)
        {
            transform.SetParent(_origParent, false);
            transform.localPosition = _origLocalPos;
            transform.localRotation = _origLocalRot;
        }

        if (_led != null)
        {
            _led.isOpenCircuit    = false;   // ya no está "quemada"
            _led.polarityInverted = inverted;
        }
    }
}
