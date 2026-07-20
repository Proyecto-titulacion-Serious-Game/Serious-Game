using UnityEngine;

/// <summary>
/// LED con simulación educativa: muestra verde (correcto), rojo (sobrecarga),
/// negro (sin corriente / polaridad invertida).
/// Usa MaterialPropertyBlock para evitar crear materiales duplicados en cada frame.
/// </summary>
public class LED : ElectricalComponent
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Configuración eléctrica")]
    public float resistance = 50f;

    [Tooltip("Caída de voltaje directa del diodo (Vf). Un LED rojo típico ≈ 1.8–2.2 V. " +
             "El MNA la modela para que la corriente sea (V − Vf) / R, no V / R.")]
    public float forwardVoltage = 2f;

    [Tooltip("Si true, el LED tiene la polaridad invertida (falla del Reto 3).")]
    public bool polarityInverted = false;

    // ─────────────────────────────────────────────
    //  Topología del diodo (ánodo → cátodo)
    // ─────────────────────────────────────────────
    /// <summary>Nodo ánodo (patita larga). Por convención <c>nodeA</c>, salvo polaridad invertida.</summary>
    public ElectricalNode AnodeNode   => polarityInverted ? nodeB : nodeA;
    /// <summary>Nodo cátodo (patita corta).</summary>
    public ElectricalNode CathodeNode => polarityInverted ? nodeA : nodeB;

    [Header("Umbrales de corriente (Amperes) — realistas para un LED de 20 mA")]
    public float minOperatingCurrent = 0.005f;   // Corriente mínima para encender
    public float maxSafeCurrent      = 0.02f;    // Corriente máxima segura (LED típico: 20 mA)
    public float overloadCurrent     = 0.05f;    // ≥50 mA = sobrecarga (rojo) — antes 100 mA, irreal

    /// <summary>Normaliza umbrales viejos serializados en escenas/prefabs (overload 100 mA irreal)
    /// al estándar realista. Así no hay que reeditar cada LED existente.</summary>
    void NormalizarUmbrales()
    {
        if (overloadCurrent > 0.05f) overloadCurrent = 0.05f;
    }

    [Header("Colores educativos")]
    public Color colorOff      = Color.black;
    public Color colorCorrect  = Color.green;
    public Color colorOverload = Color.red;

    [Header("Cristal (color físico del LED)")]
    [Tooltip("Color del CRISTAL del LED (su color real: verde/rojo/amarillo). El cuerpo transparente " +
             "se tiñe con este color y brilla con él al encender. Lo setea por variante el tool 'LED Cristal'.")]
    public Color cristalTint = new Color(0.30f, 1.00f, 0.40f, 1f);   // verde por defecto (LED típico)

    [Header("Brillo de victoria (cuando el LED está correcto)")]
    [Tooltip("Pulsa el brillo del LED mientras está correcto, para resaltar que todo quedó bien.")]
    public bool  victoryGlow = true;
    [Tooltip("Velocidad del pulso de brillo.")]
    public float glowSpeed   = 4f;
    [Tooltip("Cuánto sube/baja el brillo en el pulso.")]
    public float glowAmount  = 0.8f;
    [Tooltip("Multiplicador de brillo del LED encendido. Súbelo si quieres que el glow se note más " +
             "(aprovecha el Bloom del post-processing si está activo).")]
    public float glowIntensity = 2.5f;

    [Header("Render de victoria (al COMPLETAR el reto)")]
    [Tooltip("Material que se aplica al LED cuando el reto se completa (asigna el verde 'led_Green' / " +
             "Mat_LED_Verde). Si queda vacío, el LED simplemente conserva su verde pulsante.")]
    public Material victoryMaterial;

    // ─────────────────────────────────────────────
    //  Estado
    // ─────────────────────────────────────────────
    [Header("Estado (solo lectura)")]
    public bool isOn = false;
    public LEDState state = LEDState.Off;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private Renderer       _renderer;
    private MaterialPropertyBlock _mpb;      // ← Evita memory leak
    private Material       _matInst;         // instancia propia del material (color directo, sin MPB)
    private static readonly int   _colorID     = Shader.PropertyToID("_BaseColor");
    private static readonly int   _colorLegacy = Shader.PropertyToID("_Color");   // fallback shaders viejos
    private static readonly int   _emissionID  = Shader.PropertyToID("_EmissionColor");

    // Si el LED es agarrable (token del sandbox/entrega), congelamos su visual mientras se
    // sostiene para que no parpadee de color por la simulación a 20 Hz al moverlo entre nodos.
    private GrabbableComponent _grab;
    private bool IsHeld => _grab != null && _grab.IsGrabbed;

    // Brillo continuo 0..1 (corriente normalizada). Permite ver fades de analogWrite (PWM).
    private float _lit01;

    // Render de victoria: al completar el reto, el LED cambia al material verde y se congela.
    private bool     _victoryRender;
    private Material _origSharedMaterial;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        NormalizarUmbrales();

        // Renderer del cuerpo del LED. Preferir uno activo; si no hay (Delivered_LED trae el
        // renderer desactivado hasta colocarse), tomar el primero no-partícula igual.
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r != null && r.enabled && !(r is ParticleSystemRenderer)) { _renderer = r; break; }
        if (_renderer == null)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null && !(r is ParticleSystemRenderer)) { _renderer = r; break; }
        _mpb = new MaterialPropertyBlock();
        if (_renderer != null)
        {
            // Forzar el material de CRISTAL en runtime, sin importar qué material traiga el modelo
            // (los LED del circuito conservaban 'main'/textura). Lo crea el tool 'LED Cristal' en Resources.
            var cristal = Resources.Load<Material>("LED_Cristal");
            if (cristal != null) _renderer.sharedMaterial = cristal;
            _origSharedMaterial = _renderer.sharedMaterial;
            _matInst = _renderer.material;   // instancia propia → le seteo el color DIRECTO (infalible)
        }

        _grab = GetComponentInParent<GrabbableComponent>();

        // Pintar el cristal DESDE EL INICIO (apagado) — si no, arranca mostrando el material neutro
        // porque SetState solo pinta al CAMBIAR de estado y ya arranca en Off.
        ApplyColor(colorOff, false);
    }

    void OnEnable()
    {
        GameManager.OnLevelCompleted += OnLevelCompletedHandler;
        GameManager.OnLevelLoaded    += OnLevelLoadedHandler;
        // Re-pintar por si el renderer se activó después del Awake (Delivered_LED).
        if (_renderer != null && state == LEDState.Off) ApplyColor(colorOff, false);
    }

    void OnDisable()
    {
        GameManager.OnLevelCompleted -= OnLevelCompletedHandler;
        GameManager.OnLevelLoaded    -= OnLevelLoadedHandler;
    }

    // Al completar el reto con éxito NO cambiamos a un material fijo: eso bloqueaba el latido
    // intenso de CircuitVictoriaEffect (Update() corta temprano si _victoryRender está activo).
    // El pulso de BoostVictoria (mucho más vistoso) es ahora el único efecto de victoria del LED.
    void OnLevelCompletedHandler(LevelType _, bool success) { }

    // Al cargar un nuevo reto, restaura el material original.
    void OnLevelLoadedHandler(LevelType _) => RestoreRender();

    /// <summary>Cambia el LED al material verde de victoria y congela su visual.</summary>
    public void ApplyVictoryRender()
    {
        _victoryRender = true;
        if (_renderer != null && victoryMaterial != null)
            _renderer.material = victoryMaterial;
    }

    void RestoreRender()
    {
        if (!_victoryRender) return;
        _victoryRender = false;
        if (_renderer != null && _origSharedMaterial != null)
            _renderer.sharedMaterial = _origSharedMaterial;
    }

    /// <summary>Multiplicador GLOBAL de emisión para el flash de victoria del reto (1 = normal).
    /// Lo anima <see cref="CircuitVictoryEffect"/> al completarse un reto: todos los LEDs
    /// encendidos laten con luz intensa unos segundos — "¡esto funcionó!".</summary>
    public static float BoostVictoria = 1f;

    /// <summary>Brillo mínimo (0-1) que recibe un LED APAGADO durante el flash de victoria, para
    /// que se note que "algo encendió" incluso si ese LED en concreto nunca tuvo corriente real —
    /// deliberadamente bajo: un LED con corriente real siempre se ve claramente más brillante.</summary>
    const float VICTORY_MIN_LIT = 0.18f;

    // Brillo pulsante cuando el LED está CORRECTO (todo el circuito bien). Refuerzo visual de éxito.
    void Update()
    {
        if (_victoryRender) return;   // render de victoria fijo: no pulsar

        // El flash de victoria ahora enciende TODOS los LEDs de la escena, no solo los que ya
        // tenían corriente real — así el jugador ve claramente "esto se puso bonito al ganar" en
        // vez de que algunos LEDs se queden negros durante la celebración. La diferencia entre un
        // LED sin corriente real y uno con corriente real se conserva vía _lit01 (ver abajo): el
        // apagado sube solo a VICTORY_MIN_LIT, el que sí tiene corriente sube a su brillo pleno.
        bool flashVictoria = BoostVictoria > 1.01f;
        if ((state != LEDState.Correct && !flashVictoria) || IsHeld) return;
        if (_matInst == null) return;

        // Brillo base proporcional a la corriente (fade de PWM/analogWrite) + pulso de victoria
        // que solo se nota cuando el LED está cerca de su brillo pleno. Durante el flash, un LED
        // sin corriente real (_lit01=0) se levanta a VICTORY_MIN_LIT en vez de quedar a oscuras.
        float lit    = flashVictoria ? Mathf.Max(_lit01, VICTORY_MIN_LIT) : _lit01;
        float baseI  = Mathf.Lerp(0.35f, 1.5f, lit) * glowIntensity;
        float pulse  = victoryGlow ? Mathf.Sin(Time.time * glowSpeed) * glowAmount * 0.25f * lit * glowIntensity : 0f;
        _matInst.SetColor(_emissionID, cristalTint * (Mathf.Max(0.1f, baseI + pulse) * BoostVictoria));
    }

    // ─────────────────────────────────────────────
    //  ElectricalComponent
    // ─────────────────────────────────────────────
    public override float GetResistance() => isOpenCircuit ? 1_000_000f : resistance;

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;

        // Mientras se sostiene el LED, no parpadear: estado estable apagado.
        if (IsHeld)
        {
            current = 0f; voltageDrop = 0f; isOn = false; _lit01 = 0f;
            SetState(LEDState.Off);
            return;
        }

        if (resistance <= 0f)
        {
            current     = 0f;
            voltageDrop = 0f;
            isOn        = false;
            _lit01      = 0f;
            SetState(LEDState.Off);
            return;
        }

        float voltageDiff = nodeA.voltage - nodeB.voltage;

        // Polaridad: si está invertida el voltaje efectivo es negativo
        if (polarityInverted) voltageDiff = -voltageDiff;

        // Sin polaridad directa → LED apagado
        if (voltageDiff <= 0f)
        {
            current     = 0f;
            voltageDrop = 0f;
            isOn        = false;
            _lit01      = 0f;
            SetState(LEDState.Off);
            return;
        }

        // Ley de Ohm
        current     = voltageDiff / resistance;
        voltageDrop = voltageDiff;

        // Brillo continuo 0..1 (igual fórmula que ApplyResolvedCurrent, usada por el Reto 4) —
        // antes SOLO se actualizaba ahí, así que un LED "Correcto" de los Retos 1-3 pulsaba
        // siempre al mínimo (_lit01 nunca subía de 0) sin importar la corriente real: nunca se
        // veía la diferencia entre un LED recién encendido y uno a corriente plena.
        _lit01 = Mathf.Clamp01(Mathf.Abs(current) / Mathf.Max(1e-4f, maxSafeCurrent));

        // Clasificar estado educativo
        if (current < minOperatingCurrent)
        {
            isOn = false;
            _lit01 = 0f;
            SetState(LEDState.Off);
        }
        else if (current <= maxSafeCurrent)
        {
            isOn = true;
            SetState(LEDState.Correct);
        }
        else if (current < overloadCurrent)
        {
            isOn = true;
            SetState(LEDState.NearOverload);
        }
        else
        {
            isOn = true;
            SetState(LEDState.Overload);
        }
    }

    /// <summary>
    /// Actualiza el estado visual (color/encendido) a partir de la corriente YA resuelta
    /// en <see cref="ElectricalComponent.current"/>, sin recalcularla desde los voltajes.
    ///
    /// Lo usa el sandbox del Reto 4 (<see cref="ProtoboardSimulator"/>) tras el MNA
    /// diodo-consciente: ahí los voltajes de nodo incluyen la caída directa Vf, así que
    /// recalcular con <see cref="Calculate"/> (modelo resistivo puro) daría una corriente
    /// inflada. El MNA ya entrega la corriente correcta (0 A en inversa) → solo clasificamos.
    /// </summary>
    public void ApplyResolvedCurrent()
    {
        // Mientras se sostiene el LED, no parpadear: estado estable apagado.
        if (IsHeld) { isOn = false; _lit01 = 0f; SetState(LEDState.Off); return; }

        float i = Mathf.Abs(current);
        _lit01 = Mathf.Clamp01(i / Mathf.Max(1e-4f, maxSafeCurrent));

        if (i < minOperatingCurrent)      { isOn = false; _lit01 = 0f; SetState(LEDState.Off); }
        else if (i <= maxSafeCurrent)     { isOn = true;  SetState(LEDState.Correct); }
        else if (i <  overloadCurrent)    { isOn = true;  SetState(LEDState.NearOverload); }
        else                              { isOn = true;  SetState(LEDState.Overload); }
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>Invierte la polaridad del LED (útil para GameManager en Reto 3).</summary>
    public void SetPolarityInverted(bool inverted)
    {
        polarityInverted = inverted;
    }

    void SetState(LEDState newState)
    {
        if (state == newState) return;   // Sin cambio → no redibujar
        state = newState;

        if (_victoryRender) return;      // render de victoria fijo: no aplicar colores

        // El brillo usa el COLOR FÍSICO del LED (cristalTint) cuando funciona; rojo solo en sobrecarga.
        Color displayColor = newState switch
        {
            LEDState.Correct      => cristalTint,
            LEDState.NearOverload => cristalTint,
            LEDState.Overload     => colorOverload,
            _                     => colorOff
        };

        ApplyColor(displayColor, newState != LEDState.Off);
    }

    void ApplyColor(Color color, bool emissive)
    {
        if (_matInst == null) return;

        // LED de CRISTAL: el cuerpo se tiñe con el color FÍSICO del LED (cristalTint), DIRECTO en la
        // instancia del material (no MPB, que no aplicaba). Apagado = cristal tenue de su color;
        // encendido = cuerpo más sólido + glow fuerte.
        // Opción B: plástico de color casi sólido (alpha alto) → el cristal se ve pero los PINES
        // (mismo material) quedan sólidos, no fantasmales. Un toque translúcido al encender.
        Color baseClr = emissive
            ? new Color(cristalTint.r, cristalTint.g, cristalTint.b, 0.97f)
            : new Color(cristalTint.r, cristalTint.g, cristalTint.b, 0.85f);
        _matInst.SetColor(_colorID, baseClr);
        if (_matInst.HasProperty(_colorLegacy)) _matInst.SetColor(_colorLegacy, baseClr);

        Color em = emissive ? color * 3.5f : Color.black;
        _matInst.SetColor(_emissionID, em);
        if (emissive) _matInst.EnableKeyword("_EMISSION");
    }
}

public enum LEDState
{
    Off,
    Correct,
    NearOverload,
    Overload
}
