using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// HUD mínimo del Explorador — muestra SOLO la lectura del multímetro.
/// Aparece en el casco VR (World Space Canvas pegado a Main Camera).
///
/// El Explorador NO ve manuales ni fórmulas — solo el valor medido
/// y el color de los LEDs del circuito. Debe comunicar lo que ve
/// al Técnico verbalmente para que este pueda diagnosticar.
///
/// JERARQUÍA EN UNITY:
///   XR Origin
///     └─ Camera Offset
///         └─ Main Camera
///             └─ ExplorerHUD  [Canvas World Space]
///                 ├─ Panel_Multimetro    ← este script va aquí
///                 └─ Panel_Instruccion  ← PlayerFeedbackUI va aquí
/// </summary>
public class MultimeterUI : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias")]
    public Multimeter multimeter;

    [Header("Textos del HUD (TMP)")]
    public TMP_Text txtVoltaje;        // Valor grande central: "8.18 V" (o resistencia/corriente según modo)
    public TMP_Text txtCorriente;      // Corriente, siempre visible junto al valor principal: "7.8 mA"
    public TMP_Text txtModo;           // Modo activo: "DC VOLTAGE" / "DC CURRENT" / "RESISTANCE"
    public TMP_Text txtProbeRoja;      // "🔴 Nodo_Positivo"
    public TMP_Text txtProbeNegra;     // "⚫ Nodo_Medio"
    public TMP_Text txtEstado;         // "Midiendo..." / "Sin conexión"

    [Header("Indicadores visuales")]
    public Image  iconoProbeRoja;      // Ícono de punta roja (Image UI)
    public Image  iconoProbeNegra;     // Ícono de punta negra
    public Image  fondoVoltaje;        // Fondo que cambia color según la lectura

    [Header("Colores del fondo según estado")]
    public Color colorSinConexion = new Color(0.1f, 0.1f, 0.15f, 0.85f);
    public Color colorMidiendo   = new Color(0.05f, 0.2f, 0.05f, 0.85f);
    public Color colorAlerta     = new Color(0.25f, 0.1f, 0.0f, 0.85f);

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private float _updateTimer;
    private const float INTERVAL = 0.1f;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Update()
    {
        // Igual que NodeInteractable/GameManager/MultimeterModeButton: si la referencia serializada
        // quedó null (o apunta a una instancia desactivada), resolvemos la única activa. Sin esto
        // el panel nunca se actualizaba — multimeter quedaba en {fileID: 0} para siempre.
        if (multimeter == null || !multimeter.gameObject.activeInHierarchy)
            multimeter = FindAnyObjectByType<Multimeter>();

        _updateTimer += Time.deltaTime;
        if (_updateTimer < INTERVAL) return;
        _updateTimer = 0f;
        Refresh();
    }

    // ─────────────────────────────────────────────
    //  Actualización
    // ─────────────────────────────────────────────
    void Refresh()
    {
        if (multimeter == null) return;

        bool probeAok = multimeter.probeA != null;
        bool probeBok = multimeter.probeB != null;
        bool midiendo = probeAok && probeBok;

        // El modo se muestra siempre, incluso sin lectura — el jugador debe saber qué modo tiene
        // seleccionado antes de apuntar a un nodo.
        SetTMP(txtModo, multimeter.ModeLabel());

        // Textos de sondas
        SetTMP(txtProbeRoja,   probeAok ? multimeter.probeA.name : "—");
        SetTMP(txtProbeNegra,  probeBok ? multimeter.probeB.name : "—");

        if (!midiendo)
        {
            SetTMP(txtVoltaje,   "—");
            SetTMP(txtCorriente, "—");
            SetTMP(txtEstado,  "Conecta ambas puntas");
            SetFondo(colorSinConexion);
            return;
        }

        // Formato según el modo ACTIVO del multímetro (antes esto asumía Voltaje siempre, así que
        // una lectura real en Ohms —p.ej. 330Ω del Reto 4— se mostraba como "330.00 V").
        // En modo Resistencia, Multimeter ya deja el valor R=V/I resuelto en measuredVoltage.
        float valorPrincipal = multimeter.mode == MultimeterMode.DCCurrent
                             ? multimeter.measuredCurrent
                             : multimeter.measuredVoltage;

        SetTMP(txtVoltaje, multimeter.mode switch
        {
            MultimeterMode.DCCurrent  => Multimeter.FormatCurrent(valorPrincipal),
            MultimeterMode.Resistance => Multimeter.FormatResistance(valorPrincipal),
            _                         => Multimeter.FormatVoltage(valorPrincipal),
        });

        // Corriente: visible en los 3 modos (igual que la pantalla física original), no solo en DCCurrent.
        SetTMP(txtCorriente, Multimeter.FormatCurrent(multimeter.measuredCurrent));

        // "Voltaje alto" solo tiene sentido midiendo Voltaje; en Corriente/Resistencia solo
        // distinguimos "hay lectura" de "sin lectura".
        if (multimeter.mode == MultimeterMode.DCVoltage && valorPrincipal > 8.5f)
        {
            SetTMP(txtEstado, "Voltaje alto");
            SetFondo(colorAlerta);
        }
        else if (Mathf.Abs(valorPrincipal) > 0.1f)
        {
            SetTMP(txtEstado, "Midiendo");
            SetFondo(colorMidiendo);
        }
        else
        {
            SetTMP(txtEstado, multimeter.mode == MultimeterMode.DCVoltage ? "Sin voltaje" : "Sin lectura");
            SetFondo(colorSinConexion);
        }
    }

    void SetTMP(TMP_Text t, string s) { if (t != null) t.text = s; }
    void SetFondo(Color c)            { if (fondoVoltaje != null) fondoVoltaje.color = c; }
}