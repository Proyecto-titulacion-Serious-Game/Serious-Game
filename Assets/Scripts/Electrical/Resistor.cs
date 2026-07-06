using UnityEngine;

/// <summary>
/// Resistencia con soporte de código de colores (Reto 3).
/// Permite establecer el valor "correcto" vs el valor "defectuoso" para gamificación.
/// Incluye feedback visual de sobrecarga y falla visible para el Explorador.
/// </summary>
public class Resistor : ElectricalComponent
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Valor de resistencia")]
    public float resistance = 100f;

    [Header("Código de colores (educativo)")]
    [Tooltip("Valor correcto que el Técnico debe calcular e indicar.")]
    public float correctResistance = 100f;
    [Tooltip("Valor defectuoso que aparece al inicio del reto.")]
    public float faultyResistance  = 10f;
    [Tooltip("True si la resistencia tiene el valor incorrecto (falla activa).")]
    public bool  hasFault          = false;

    [Header("Tolerancia (%)")]
    [Range(1f, 20f)]
    public float tolerancePercent  = 5f;

    [Header("Potencia nominal")]
    [Tooltip("Potencia máxima que puede disipar sin quemarse. Típico: 0.25 W (1/4 W).")]
    public float powerRatingWatts = 0.25f;

    [Header("Colores educativos")]
    public Color colorNormal    = new Color(0.76f, 0.60f, 0.42f);  // beige/tan — resistor físico
    public Color colorFault     = new Color(1.00f, 0.55f, 0.00f);  // naranja — valor incorrecto
    public Color colorOverload  = new Color(1.00f, 0.10f, 0.05f);  // rojo — sobrecarga
    public Color colorOpen      = new Color(0.25f, 0.25f, 0.25f);  // gris oscuro — circuito abierto

    [Header("Efectos de sobrecarga")]
    public ParticleSystem smokeEffect;

    // ─────────────────────────────────────────────
    //  Estado de potencia (solo lectura)
    // ─────────────────────────────────────────────
    [Header("Potencia disipada (solo lectura)")]
    [SerializeField] private float _dissipatedPower;
    [SerializeField] private bool  _isOverloaded;

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
    public override float GetResistance() => isOpenCircuit ? 1_000_000f : resistance;

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;
        if (resistance <= 0f)
        {
            current          = 0f;
            voltageDrop      = 0f;
            _dissipatedPower = 0f;
            _isOverloaded    = false;
            UpdateVisual();
            return;
        }

        float voltageDiff = nodeA.voltage - nodeB.voltage;
        current          = isOpenCircuit ? 0f : voltageDiff / resistance;
        voltageDrop      = voltageDiff;

        // P = I² × R  (potencia disipada real)
        _dissipatedPower = Mathf.Abs(current * current * resistance);
        _isOverloaded    = _dissipatedPower > powerRatingWatts;

        UpdateVisual();
    }

    // ─────────────────────────────────────────────
    //  API de juego
    // ─────────────────────────────────────────────

    /// <summary>Aplica la falla: pone el valor defectuoso.</summary>
    public void ApplyFault()
    {
        resistance = faultyResistance;
        hasFault   = true;
    }

    /// <summary>Repara la resistencia con el valor correcto.</summary>
    public void Repair()
    {
        resistance = correctResistance;
        hasFault   = false;
    }

    /// <summary>
    /// Verifica si el valor propuesto por el Técnico es correcto (dentro de tolerancia).
    /// </summary>
    public bool IsValueCorrect(float proposedValue)
    {
        // Piso de tolerancia: ningún reto exige al estudiante más precisión que esto. Antes el valor
        // por-instancia (p. ej. 8.5% en Reto 1 → solo 778–922 Ω) rechazaba valores razonables que ya
        // encendían bien el LED, dejando el reto "visualmente resuelto" pero sin completar.
        const float tolerancePisoPct = 12f;
        float effTol  = Mathf.Max(tolerancePercent, tolerancePisoPct);
        float margin  = correctResistance * (effTol / 100f);
        return Mathf.Abs(proposedValue - correctResistance) <= margin;
    }

    /// <summary>
    /// Devuelve las bandas de colores como string educativo.
    /// Ejemplo: "Marrón-Negro-Marrón-Oro" para 100Ω 5%
    /// </summary>
    public string GetColorBandString()
    {
        return ResistorColorCode.GetBandString((int)correctResistance, tolerancePercent);
    }

    // ─────────────────────────────────────────────
    //  Feedback visual
    // ─────────────────────────────────────────────

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
            else
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

/// <summary>
/// Utilidad para calcular el código de colores de una resistencia.
/// </summary>
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
