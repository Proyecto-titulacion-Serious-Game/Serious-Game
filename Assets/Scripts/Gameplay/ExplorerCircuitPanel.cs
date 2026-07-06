using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Panel WorldSpace para el Explorador VR que resume el estado observable del circuito.
///
/// PROPÓSITO (dinámica de comunicación):
///   Refuerza la terminología técnica que el Explorador debe usar al hablar con el Técnico.
///   NO reemplaza la observación física del circuito ni al multímetro.
///   Muestra NOMBRES y ESTADOS, no diagnósticos — el jugador debe interpretar.
///
/// Ejemplos de lo que el Explorador puede leer y comunicar:
///   "el LED rojo dice SOBRECARGA"
///   "hay voltaje en Nodo A (9V) pero no en Nodo B (0V)"
///   "el capacitor tiene estado POLARIDAD INVERTIDA"
///
/// SETUP:
///   1. Crear Canvas WorldSpace en la escena del Explorador.
///   2. Agregar este componente al Canvas o a un hijo.
///   3. Asignar txtLEDStates, txtNodeVoltages, txtResistors, txtFaultBanner desde el Inspector.
///   4. El panel se actualiza automáticamente con cada simulación del circuito.
/// </summary>
public class ExplorerCircuitPanel : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias")]
    public GameManager    gameManager;
    public CircuitManager circuitManager;

    [Header("Campos de texto (UI WorldSpace)")]
    [Tooltip("Banner superior: 'FALLA DETECTADA' o estado general.")]
    public TextMeshProUGUI txtFaultBanner;

    [Tooltip("Estados de los LEDs del circuito.")]
    public TextMeshProUGUI txtLEDStates;

    [Tooltip("Voltajes en nodos clave.")]
    public TextMeshProUGUI txtNodeVoltages;

    [Tooltip("Estado de resistencias (valor, sobrecarga).")]
    public TextMeshProUGUI txtResistors;

    [Tooltip("Lectura actual del multímetro.")]
    public TextMeshProUGUI txtMultimeter;

    [Header("Referencia opcional al multímetro")]
    public Multimeter multimeter;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        if (gameManager    == null) gameManager    = FindAnyObjectByType<GameManager>();
        if (circuitManager == null) circuitManager = FindAnyObjectByType<CircuitManager>();
        if (multimeter     == null) multimeter     = FindAnyObjectByType<Multimeter>();
    }

    void OnEnable()
    {
        CircuitManager.OnCircuitChanged += Refresh;
        GameManager.OnFaultDetected     += ShowFaultBanner;
        GameManager.OnLevelLoaded       += OnLevelLoaded;
    }

    void OnDisable()
    {
        CircuitManager.OnCircuitChanged -= Refresh;
        GameManager.OnFaultDetected     -= ShowFaultBanner;
        GameManager.OnLevelLoaded       -= OnLevelLoaded;
    }

    void OnLevelLoaded(LevelType _)
    {
        // Solo re-fetch si no hay circuitManager fijo asignado desde el Inspector.
        // Cuando el panel es hijo de una zona, ya apunta al CM correcto.
        if (circuitManager == null && gameManager != null)
            circuitManager = gameManager.circuit != null ? gameManager.circuit.GetCompanionCircuitManager() : null;
        Refresh();
    }

    // ─────────────────────────────────────────────
    //  Refresh principal
    // ─────────────────────────────────────────────
    void Refresh()
    {
        if (circuitManager == null) return;
        RefreshLEDStates();
        RefreshNodeVoltages();
        RefreshResistors();
        RefreshMultimeter();
    }

    // ─────────────────────────────────────────────
    //  Secciones
    // ─────────────────────────────────────────────

    void RefreshLEDStates()
    {
        if (txtLEDStates == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("<b>— LEDs —</b>");

        bool found = false;
        foreach (var comp in circuitManager.components)
        {
            if (comp is not LED led) continue;
            found = true;

            string estado;
            string hex;
            if (led.polarityInverted)
            {
                estado = "POLARIDAD INVERTIDA";
                hex    = "#FFA500";
            }
            else
            {
                (estado, hex) = led.state switch
                {
                    LEDState.Correct      => ("ENCENDIDO",            "#00FF88"),
                    LEDState.NearOverload => ("CASI EN SOBRECARGA",   "#FFFF00"),
                    LEDState.Overload     => ("SOBRECARGA",           "#FF4444"),
                    _                     => ("APAGADO",              "#888888")
                };
            }

            sb.AppendLine($"<color={hex}>{led.name}: {estado}</color>");
        }

        if (!found) sb.AppendLine("<color=#888888>Sin LEDs</color>");
        txtLEDStates.text = sb.ToString();
    }

    void RefreshNodeVoltages()
    {
        if (txtNodeVoltages == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("<b>— Nodos —</b>");

        if (circuitManager.isShortCircuited)
        {
            sb.AppendLine("<color=#FF4444><b>¡CORTOCIRCUITO!</b></color>");
            txtNodeVoltages.text = sb.ToString();
            return;
        }

        bool any = false;
        foreach (var comp in circuitManager.components)
        {
            if (comp is VoltageSource) continue;

            if (comp.nodeA != null)
            {
                any = true;
                string c = comp.nodeA.voltage >= 1f ? "#00FF88" : "#FF6666";
                sb.AppendLine($"{comp.name} <b>A</b>: <color={c}>{comp.nodeA.voltage:F1} V</color>");
            }
            if (comp.nodeB != null)
            {
                string c = comp.nodeB.voltage >= 1f ? "#00FF88" : "#FF6666";
                sb.AppendLine($"{comp.name} <b>B</b>: <color={c}>{comp.nodeB.voltage:F1} V</color>");
            }
        }

        if (!any) sb.AppendLine("<color=#888888>Sin nodos</color>");

        // Fuente de alimentación
        foreach (var comp in circuitManager.components)
        {
            if (comp is VoltageSource vs)
            {
                string faultTag = vs.hasFault
                    ? $" <color=#FF4444>[{vs.faultMode}]</color>"
                    : "";
                sb.AppendLine($"Fuente: <b>{vs.GetEffectiveVoltage():F1} V</b>{faultTag}");
                break;
            }
        }

        txtNodeVoltages.text = sb.ToString();
    }

    void RefreshResistors()
    {
        if (txtResistors == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("<b>— Resistencias —</b>");

        bool found = false;
        foreach (var comp in circuitManager.components)
        {
            if (comp is not Resistor r) continue;
            found = true;

            string estado;
            string hex;
            if (r.isOpenCircuit)
            {
                estado = "CONEXIÓN ABIERTA";
                hex    = "#FF4444";
            }
            else if (r.isOverloaded)
            {
                estado = $"SOBRECARGA ({r.dissipatedPower:F2} W > {r.powerRatingWatts} W)";
                hex    = "#FF4444";
            }
            else if (r.hasFault)
            {
                estado = $"VALOR INCORRECTO ({r.resistance:F0} Ω)";
                hex    = "#FFA500";
            }
            else
            {
                estado = $"OK ({r.resistance:F0} Ω)";
                hex    = "#00FF88";
            }

            sb.AppendLine($"<color={hex}>{r.name}: {estado}</color>");
        }

        if (!found) sb.AppendLine("<color=#888888>Sin resistencias</color>");
        txtResistors.text = sb.ToString();
    }

    void RefreshMultimeter()
    {
        if (txtMultimeter == null || multimeter == null) return;

        if (multimeter.isReading)
        {
            txtMultimeter.text =
                $"<b>Multímetro</b>\n" +
                $"{multimeter.measuredVoltage:F2} V  |  {multimeter.measuredCurrent * 1000f:F1} mA";
            txtMultimeter.color = Color.white;
        }
        else
        {
            txtMultimeter.text  = "<b>Multímetro</b>\n<color=#888888>Sin contacto</color>";
            txtMultimeter.color = Color.white;
        }
    }

    // ─────────────────────────────────────────────
    //  Banner de falla
    // ─────────────────────────────────────────────
    void ShowFaultBanner(string msg)
    {
        if (txtFaultBanner == null) return;
        txtFaultBanner.text  = "⚠  FALLA DETECTADA";
        txtFaultBanner.color = new Color(1f, 0.3f, 0.1f);
    }

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────
    public void ForceRefresh() => Refresh();
}
