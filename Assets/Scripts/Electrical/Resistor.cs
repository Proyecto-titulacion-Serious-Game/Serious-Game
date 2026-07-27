using UnityEngine;

/// <summary>
/// Resistencia con soporte de código de colores (Reto 3) y Estrés Térmico realista.
/// </summary>
public class Resistor : ElectricalComponent
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Valor de resistencia")]
    public float resistance = 100f;

    [Header("Código de colores (educativo)")]
    public float correctResistance = 100f;
    public float faultyResistance  = 10f;
    public bool  hasFault          = false;

    [Header("Tolerancia (%)")]
    [Range(1f, 20f)]
    public float tolerancePercent  = 5f;

    [Header("Potencia nominal y Estrés Térmico")]
    [Tooltip("Potencia máxima que puede disipar sin quemarse. Típico: 0.25 W (1/4 W).")]
    public float powerRatingWatts = 0.25f;
    [Tooltip("Tiempo en segundos que soporta la sobrecarga antes de quemarse.")]
    public float timeToBurn = 3.0f;

    [Header("Colores educativos")]
    public Color colorNormal    = new Color(0.76f, 0.60f, 0.42f);  
    public Color colorFault     = new Color(1.00f, 0.55f, 0.00f);  
    public Color colorOverload  = new Color(1.00f, 0.10f, 0.05f);  
    public Color colorOpen      = new Color(0.25f, 0.25f, 0.25f);  

    [Header("Efectos de sobrecarga")]
    public ParticleSystem smokeEffect;

    // ─────────────────────────────────────────────
    //  Estado de potencia (solo lectura)
    // ─────────────────────────────────────────────
    [Header("Potencia disipada (solo lectura)")]
    [SerializeField] private float _dissipatedPower;
    [SerializeField] private bool  _isOverloaded;
    private float _overloadTimer = 0f;

    public float dissipatedPower => _dissipatedPower;
    public bool  isOverloaded    => _isOverloaded;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private Renderer             _renderer;
    private MaterialPropertyBlock _mpb;
    private ResistorVisualState  _lastState = ResistorVisualState.Normal;

    private static readonly int _colorID    = Shader.PropertyToID("_BaseColor");
    private static readonly int _emissionID = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) { _renderer = r; break; }
        _mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        // ⚡ NUEVO: Lógica de degradación térmica
        if (_isOverloaded && !isOpenCircuit)
        {
            _overloadTimer += Time.deltaTime;
            
            if (_overloadTimer >= timeToBurn)
            {
                isOpenCircuit = true;     // Se rompió el componente
                _isOverloaded = false;
                current = 0f;
                voltageDrop = 0f;
                
                Debug.LogWarning($"[Resistor] ¡Resistencia {name} quemada por exceso de potencia ({_dissipatedPower:F2}W)!");
                
                if (smokeEffect != null && !smokeEffect.isPlaying) smokeEffect.Play();
                UpdateVisual();
                
                // Forzar al manager a recalcular porque el circuito se abrió
                var circuit = GetComponentInParent<CircuitManager>();
                if (circuit != null) circuit.MarkDirty();
            }
        }
        else if (_overloadTimer > 0f)
        {
            // Si la carga vuelve a la normalidad, se "enfría" poco a poco
            _overloadTimer = Mathf.Max(0f, _overloadTimer - Time.deltaTime);
        }
    }

    public override float GetResistance() => isOpenCircuit ? 1_000_000f : resistance;

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;
        
        if (resistance <= 0f || isOpenCircuit)
        {
            current          = 0f;
            voltageDrop      = 0f;
            _dissipatedPower = 0f;
            _isOverloaded    = false;
            UpdateVisual();
            return;
        }

        float voltageDiff = nodeA.voltage - nodeB.voltage;
        current          = voltageDiff / resistance;
        voltageDrop      = voltageDiff;

        // P = I² × R
        _dissipatedPower = Mathf.Abs(current * current * resistance);
        _isOverloaded    = _dissipatedPower > powerRatingWatts;

        UpdateVisual();
    }

    public void ApplyFault()
    {
        resistance = faultyResistance;
        hasFault   = true;
    }

    public void Repair()
    {
        resistance = correctResistance;
        hasFault   = false;
        isOpenCircuit = false;
        _overloadTimer = 0f;
    }

    public bool IsValueCorrect(float proposedValue)
    {
        const float tolerancePisoPct = 12f;
        float effTol  = Mathf.Max(tolerancePercent, tolerancePisoPct);
        float margin  = correctResistance * (effTol / 100f);
        return Mathf.Abs(proposedValue - correctResistance) <= margin;
    }

    public string GetColorBandString()
    {
        return ResistorColorCode.GetBandString((int)correctResistance, tolerancePercent);
    }

    void UpdateVisual()
    {
        ResistorVisualState newState;
        if (isOpenCircuit)           newState = ResistorVisualState.Open;
        else if (_isOverloaded)      newState = ResistorVisualState.Overloaded;
        else if (hasFault)           newState = ResistorVisualState.Fault;
        else                         newState = ResistorVisualState.Normal;

        if (newState == _lastState) return;
        _lastState = newState;

        Color c = newState switch
        {
            ResistorVisualState.Overloaded => colorOverload,
            ResistorVisualState.Fault      => colorFault,
            ResistorVisualState.Open       => colorOpen,
            _                              => colorNormal
        };

        bool emissive = newState != ResistorVisualState.Normal;
        ApplyColor(c, emissive);

        if (smokeEffect != null)
        {
            if (newState == ResistorVisualState.Overloaded)
            { if (!smokeEffect.isPlaying) smokeEffect.Play(); }
            else if (newState != ResistorVisualState.Open)
            { smokeEffect.Stop(); }
        }
    }

    void ApplyColor(Color color, bool emissive)
    {
        if (_renderer == null || _mpb == null) return;
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_colorID,    color);
        _mpb.SetColor(_emissionID, emissive ? color * 1.8f : Color.black);
        _renderer.SetPropertyBlock(_mpb);
    }
}

public enum ResistorVisualState { Normal, Fault, Overloaded, Open }

public static class ResistorColorCode
{
    private static readonly string[] bands =
        { "Negro", "Marrón", "Rojo", "Naranja", "Amarillo",
          "Verde",  "Azul",   "Violeta", "Gris",  "Blanco" };

    public static string GetBandString(int value, float tolerance)
    {
        if (value <= 0) return "Valor inválido";
        int digits    = value;
        int multiplier = 0;
        while (digits >= 100) { digits /= 10; multiplier++; }
        int band1 = digits / 10;
        int band2 = digits % 10;
        string tolBand = tolerance <= 1f  ? "Marrón" :
                         tolerance <= 2f  ? "Rojo"   :
                         tolerance <= 5f  ? "Oro"    :
                         tolerance <= 10f ? "Plata"  : "Sin banda";

        if (band1 >= bands.Length || band2 >= bands.Length || multiplier >= bands.Length)
            return "Fuera de rango";
        return $"{bands[band1]} – {bands[band2]} – {bands[multiplier]} – {tolBand}";
    }
}