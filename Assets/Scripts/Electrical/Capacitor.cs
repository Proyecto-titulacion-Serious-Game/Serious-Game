using UnityEngine;

/// <summary>
/// Capacitor electrolítico para el Reto 3 con Carga Exponencial Dinámica.
/// Simula fallo por polaridad invertida (humo + vibración háptica).
/// </summary>
public class Capacitor : ElectricalComponent
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Configuración eléctrica")]
    [Tooltip("Capacitancia en Faradios.")]
    public float capacitance = 0.0001f;    // 100µF típico

    [Header("Falla de polaridad")]
    public bool polarityInverted = false;
    public float shortCircuitResistance = 0.1f;
    public float normalDCResistance = 1_000_000f;

    [Header("Carga dinámica (T = 5·R·C)")]
    [HideInInspector] 
    public float realSeriesResistance = 100f; // ⚡ Inyectado dinámicamente por el CircuitManager

    public float tauMinimoSegundos = 1.6f;
    public float voltajeAlmacenado;
    public Color colorCargado = new Color(0.3f, 0.7f, 1f);

    public float NivelDeCarga01 { get; private set; }

    float _targetV;          
    float _vRef = 9f;        
    bool  _logCargaHecho;

    [Header("Efectos visuales de falla")]
    public ParticleSystem smokeEffect;
    public float smokeCurrentThreshold = 5f;

    [Header("Colores educativos")]
    public Color colorNormal      = new Color(0.6f, 0.6f, 0.65f);   
    public Color colorReversed    = Color.yellow;                     
    public Color colorShortCircuit = new Color(1f, 0.3f, 0f);       

    [Header("Estado (solo lectura)")]
    public CapacitorState state = CapacitorState.Normal;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private static readonly int _colorID    = Shader.PropertyToID("_BaseColor");
    private static readonly int _emissionID = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) { _renderer = r; break; }
        _mpb = new MaterialPropertyBlock();
    }

    public override float GetResistance()
    {
        if (isOpenCircuit) return 1_000_000f;
        return polarityInverted ? shortCircuitResistance : normalDCResistance;
    }

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;

        float voltageDiff = nodeA.voltage - nodeB.voltage;
        current     = voltageDiff / GetResistance();
        voltageDrop = voltageDiff;

        _targetV = polarityInverted ? 0f : Mathf.Max(0f, voltageDiff);

        if (polarityInverted && Mathf.Abs(current) > smokeCurrentThreshold)
            SetState(CapacitorState.ShortCircuit);
        else if (polarityInverted)
            SetState(CapacitorState.Reversed);
        else
            SetState(CapacitorState.Normal);
    }

    void Update()
    {
        if (polarityInverted)
        {
            voltajeAlmacenado = 0f;
            NivelDeCarga01    = 0f;
            return;
        }

        // ⚡ NUEVO: Se calcula el TAU en función de la resistencia física real conectada al circuito
        float tau = Mathf.Max(realSeriesResistance * capacitance, tauMinimoSegundos);
        float k   = 1f - Mathf.Exp(-Time.deltaTime / tau);   
        
        voltajeAlmacenado += (_targetV - voltajeAlmacenado) * k;

        if (_targetV > 0.5f)
        {
            _vRef = Mathf.Max(1f, _targetV);
            if (!_logCargaHecho)
            {
                _logCargaHecho = true;
                Debug.Log($"[Capacitor] '{name}' cargando dinámicamente: R real = {realSeriesResistance}Ω, τ = {tau:0.0}s");
            }
        }
        NivelDeCarga01 = Mathf.Clamp01(voltajeAlmacenado / _vRef);

        if (state == CapacitorState.Normal)
            current = (_targetV - voltajeAlmacenado) / Mathf.Max(1f, realSeriesResistance);

        if (state == CapacitorState.Normal && _renderer != null)
            ApplyColor(Color.Lerp(colorNormal, colorCargado, NivelDeCarga01), NivelDeCarga01 > 0.15f);
    }

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

public enum CapacitorState { Normal, Reversed, ShortCircuit }