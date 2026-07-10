using UnityEngine;

/// <summary>
/// Capacitor electrolítico para el Reto 3.
/// Simula fallo por polaridad invertida (humo + vibración háptica).
/// En CC actúa como circuito abierto (resistencia muy alta) excepto en cortocircuito.
/// </summary>
public class Capacitor : ElectricalComponent
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Configuración eléctrica")]
    [Tooltip("Capacitancia en Faradios (valor educativo, no afecta simulación DC).")]
    public float capacitance = 0.0001f;    // 100µF típico

    [Header("Falla de polaridad")]
    public bool polarityInverted = false;
    [Tooltip("Resistencia simulada en cortocircuito (polaridad invertida).")]
    public float shortCircuitResistance = 0.1f;
    [Tooltip("Resistencia en operación normal DC (casi circuito abierto).")]
    public float normalDCResistance = 1_000_000f;

    [Header("Carga/descarga educativa (T = 5·R·C)")]
    [Tooltip("R en serie usada para la constante de tiempo educativa: τ = R·C. " +
             "El condensador se considera cargado a las 5τ (regla del tutorial: T = 5·R·C).")]
    public float resistenciaDeCargaOhms = 2200f;
    [Tooltip("τ mínimo en segundos, para que la carga siempre sea VISIBLE aunque R·C real dé milisegundos.")]
    public float tauMinimoSegundos = 1.6f;
    [Tooltip("Voltaje almacenado actual (solo lectura — sube/baja exponencialmente).")]
    public float voltajeAlmacenado;
    [Tooltip("Color del condensador cargado (interpola desde el normal según el nivel de carga).")]
    public Color colorCargado = new Color(0.3f, 0.7f, 1f);

    /// <summary>Nivel de carga 0..1 (para HUDs/efectos).</summary>
    public float NivelDeCarga01 { get; private set; }

    float _targetV;          // voltaje aplicado según la última simulación
    float _vRef = 9f;        // referencia para normalizar NivelDeCarga01
    bool  _logCargaHecho;

    [Header("Efectos visuales de falla")]
    public ParticleSystem smokeEffect;
    public float smokeCurrentThreshold = 5f;

    [Header("Colores educativos")]
    public Color colorNormal      = new Color(0.6f, 0.6f, 0.65f);   // gris plateado
    public Color colorReversed    = Color.yellow;                     // advertencia
    public Color colorShortCircuit = new Color(1f, 0.3f, 0f);       // naranja-rojo

    // ─────────────────────────────────────────────
    //  Estado
    // ─────────────────────────────────────────────
    [Header("Estado (solo lectura)")]
    public CapacitorState state = CapacitorState.Normal;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private static readonly int _colorID    = Shader.PropertyToID("_BaseColor");
    private static readonly int _emissionID = Shader.PropertyToID("_EmissionColor");

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) { _renderer = r; break; }
        _mpb = new MaterialPropertyBlock();
    }

    // ─────────────────────────────────────────────
    //  ElectricalComponent
    // ─────────────────────────────────────────────
    public override float GetResistance()
    {
        if (isOpenCircuit) return 1_000_000f;
        return polarityInverted ? shortCircuitResistance : normalDCResistance;
    }

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;

        float voltageDiff = nodeA.voltage - nodeB.voltage;

        // En polaridad correcta, en DC casi no pasa corriente
        current     = voltageDiff / GetResistance();
        voltageDrop = voltageDiff;

        // Objetivo de carga: el voltaje aplicado (si la polaridad es correcta). La integración
        // temporal corre en Update() — Calculate solo llega cuando la sim está sucia (20 Hz).
        _targetV = polarityInverted ? 0f : Mathf.Max(0f, voltageDiff);

        // Clasificar estado
        if (polarityInverted && Mathf.Abs(current) > smokeCurrentThreshold)
        {
            SetState(CapacitorState.ShortCircuit);
        }
        else if (polarityInverted)
        {
            SetState(CapacitorState.Reversed);
        }
        else
        {
            SetState(CapacitorState.Normal);
        }
    }

    // ─────────────────────────────────────────────
    //  Carga/descarga exponencial (τ = R·C, cargado a las 5τ)
    //  CAPA VISUAL/MEDIBLE: no modifica GetResistance(), así el solver DC (y el
    //  comportamiento de los retos) queda EXACTAMENTE igual que antes.
    // ─────────────────────────────────────────────
    void Update()
    {
        if (polarityInverted)
        {
            voltajeAlmacenado = 0f;
            NivelDeCarga01    = 0f;
            return;
        }

        float tau = Mathf.Max(resistenciaDeCargaOhms * capacitance, tauMinimoSegundos);
        float k   = 1f - Mathf.Exp(-Time.deltaTime / tau);   // paso exponencial exacto
        voltajeAlmacenado += (_targetV - voltajeAlmacenado) * k;

        if (_targetV > 0.5f)
        {
            _vRef = Mathf.Max(1f, _targetV);
            if (!_logCargaHecho)
            {
                _logCargaHecho = true;
                Debug.Log($"[Capacitor] '{name}' cargando: τ = R·C = {tau:0.0}s → carga completa " +
                          $"T = 5·R·C ≈ {5f * tau:0.0}s (objetivo {_targetV:0.0} V).");
            }
        }
        NivelDeCarga01 = Mathf.Clamp01(voltajeAlmacenado / _vRef);

        // Corriente de carga realista (solo informativa): i = (V - Vc) / R → decae a 0 al cargarse.
        if (state == CapacitorState.Normal)
            current = (_targetV - voltajeAlmacenado) / Mathf.Max(1f, resistenciaDeCargaOhms);

        // Visual: el condensador "se llena" (tinte azul + emisión creciente). Solo en estado
        // Normal — los estados de falla (Reversed/ShortCircuit) conservan sus colores de alerta.
        if (state == CapacitorState.Normal && _renderer != null)
            ApplyColor(Color.Lerp(colorNormal, colorCargado, NivelDeCarga01), NivelDeCarga01 > 0.15f);
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    public void SetPolarityInverted(bool inverted)
    {
        polarityInverted = inverted;
    }

    void SetState(CapacitorState newState)
    {
        if (state == newState) return;
        state = newState;

        if (smokeEffect != null)
        {
            if (newState == CapacitorState.ShortCircuit)
            { if (!smokeEffect.isPlaying) smokeEffect.Play(); }
            else
            { smokeEffect.Stop(); }
        }

        Color c = newState switch
        {
            CapacitorState.Reversed     => colorReversed,
            CapacitorState.ShortCircuit => colorShortCircuit,
            _                           => colorNormal
        };
        ApplyColor(c, newState != CapacitorState.Normal);
    }

    void ApplyColor(Color color, bool emissive)
    {
        if (_renderer == null || _mpb == null) return;
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_colorID,    color);
        _mpb.SetColor(_emissionID, emissive ? color * 1.5f : Color.black);
        _renderer.SetPropertyBlock(_mpb);
    }
}

public enum CapacitorState
{
    Normal,
    Reversed,
    ShortCircuit
}