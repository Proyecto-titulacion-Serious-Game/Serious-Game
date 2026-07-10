using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel de monitoreo en tiempo real para el Técnico (PC).
/// Extrae datos de la red y del motor matemático para visualizar la física del circuito.
/// </summary>
public class TechnicianTelemetryUI : MonoBehaviour
{
    [Header("Monitores de Pantalla (TextMeshPro)")]
    public TextMeshProUGUI txtVoltage;
    public TextMeshProUGUI txtCurrent;
    public TextMeshProUGUI txtPower;
    public TextMeshProUGUI txtSensorA0;
    public TextMeshProUGUI txtSystemStatus;

    // Aliases de compatibilidad con scripts de editor (aceptan TMP_Text base)
    public TMP_Text lblVoltaje   { get => txtVoltage;      set => txtVoltage      = value as TextMeshProUGUI ?? txtVoltage; }
    public TMP_Text lblCorriente { get => txtCurrent;      set => txtCurrent      = value as TextMeshProUGUI ?? txtCurrent; }
    public TMP_Text lblPotencia  { get => txtPower;        set => txtPower        = value as TextMeshProUGUI ?? txtPower; }
    public TMP_Text lblAdc       { get => txtSensorA0;     set => txtSensorA0     = value as TextMeshProUGUI ?? txtSensorA0; }
    public TMP_Text lblAlerta    { get => txtSystemStatus; set => txtSystemStatus = value as TextMeshProUGUI ?? txtSystemStatus; }

    [Header("Telemetría detallada (HUD Builder)")]
    public TMP_Text txtValorVoltaje;
    public TMP_Text txtValorCorriente;
    public TMP_Text txtValorPotencia;
    public TMP_Text txtValorAdc;
    public Image    imgCardVoltaje;
    public Image    imgCardCorriente;
    public Image    bannerStripe;

    [Header("Referencias (auto-detectadas — Opción A)")]
    [Tooltip("Asignado automáticamente por evento OnBridgeReady. Puedes pre-asignar en Inspector " +
             "para modo offline o editor; si queda vacío se detecta en runtime.")]
    public CircuitSimulator circuit;
    public ArduinoNetworkBridge arduinoBridge;

    public Image panelAlerta;

    // ─────────────────────────────────────────────
    //  Lifecycle — Opción A: suscripción a evento de red
    // ─────────────────────────────────────────────

    void OnEnable()
    {
        ArduinoNetworkBridge.OnBridgeReady     += OnBridgeConnected;
        ArduinoNetworkBridge.OnBridgeDestroyed += OnBridgeDisconnected;

        // Si ya hay un bridge en escena (modo offline / ya spawneado antes del enable)
        if (arduinoBridge == null)
            arduinoBridge = FindAnyObjectByType<ArduinoNetworkBridge>();
    }

    void OnDisable()
    {
        ArduinoNetworkBridge.OnBridgeReady     -= OnBridgeConnected;
        ArduinoNetworkBridge.OnBridgeDestroyed -= OnBridgeDisconnected;
    }

    void OnBridgeConnected(ArduinoNetworkBridge bridge)
    {
        arduinoBridge = bridge;
        Debug.Log("[TechnicianTelemetryUI] ArduinoNetworkBridge conectado automáticamente.");
        if (txtSensorA0 != null) txtSensorA0.text = "CONECTADO — ADC: 0";
    }

    void OnBridgeDisconnected(ArduinoNetworkBridge _)
    {
        arduinoBridge = null;
        if (txtSensorA0 != null) txtSensorA0.text = "SIN SEÑAL";
    }

    // ─────────────────────────────────────────────
    //  Update — telemetría en tiempo real
    // ─────────────────────────────────────────────

    void Update()
    {
        ActualizarTelemetriaGeneral();
        ActualizarTelemetriaArduino();
    }

    void ActualizarTelemetriaGeneral()
    {
        // Reto 4 asimétrico: si llega telemetría por red (GameSession), usarla. El motor del
        // sandbox corre en el Explorador y el Host/Técnico no lo tiene localmente.
        if (GameSession.Instance != null && GameSession.Instance.TelemHasData)
        {
            var gs = GameSession.Instance;
            if (txtVoltage != null) txtVoltage.text = gs.TelemVoltage.ToString("F2") + " V";
            if (txtCurrent != null) txtCurrent.text = gs.TelemCurrentmA.ToString("F1") + " mA";
            if (txtPower   != null) txtPower.text   = gs.TelemPowerW.ToString("F3") + " W";
            AplicarEstadoSistema(gs.TelemStatus);

            // Realismo didáctico: desglose de la caída de tensión (LED ~2 V, el resto en la R).
            if (txtSystemStatus != null && gs.TelemStatus == 0 && gs.TelemVLed > 0.1f)
                txtSystemStatus.text += $"\nLED cae {gs.TelemVLed:F1} V - la resistencia absorbe " +
                                        $"{Mathf.Max(0f, gs.TelemVoltage - gs.TelemVLed):F1} V";
            return;
        }

        // Fallback escena única (IntegratedDemo / offline): leer el simulador local.
        if (circuit == null)
        {
            circuit = FindAnyObjectByType<CircuitSimulator>();
            if (circuit == null) return;
        }

        if (txtVoltage != null)
            txtVoltage.text = circuit.sourceVoltage.ToString("F2") + " V";

        if (txtCurrent != null)
            txtCurrent.text = (circuit.totalCurrent * 1000f).ToString("F1") + " mA";

        if (txtPower != null)
            txtPower.text = circuit.totalPower.ToString("F3") + " W";

        if (txtSystemStatus != null)
        {
            if (circuit.isShortCircuited)
            {
                txtSystemStatus.color = Color.red;
                txtSystemStatus.text  = "¡ALERTA: CORTOCIRCUITO DETECTADO!";
            }
            else if (circuit.totalCurrent == 0)
            {
                txtSystemStatus.color = new Color(1f, 0.5f, 0f);
                txtSystemStatus.text  = "ESTADO: CIRCUITO ABIERTO (0 mA)";
            }
            else
            {
                txtSystemStatus.color = Color.green;
                txtSystemStatus.text  = "ESTADO: OPERACIÓN SEGURA";
            }
        }
    }

    void ActualizarTelemetriaArduino()
    {
        // Reto 4 asimétrico: ADC por red (publicado por el Explorador) tiene prioridad.
        if (GameSession.Instance != null && GameSession.Instance.TelemHasData)
        {
            if (txtSensorA0 != null) txtSensorA0.text = $"ADC A0: {GameSession.Instance.TelemAdc}";
            return;
        }

        // arduinoBridge se asigna por evento — si aún es null mostramos estado
        if (arduinoBridge == null)
        {
            if (txtSensorA0 != null) txtSensorA0.text = "Esperando conexión...";
            return;
        }

        if (txtSensorA0 != null)
            txtSensorA0.text = $"ADC A0: {arduinoBridge.NetworkedAnalogValue}";
    }

    // ─────────────────────────────────────────────
    //  Estado del sistema (código → texto/color)
    // ─────────────────────────────────────────────
    //  0 = operación segura, 1 = cortocircuito, 2 = circuito abierto.
    void AplicarEstadoSistema(int code)
    {
        if (txtSystemStatus == null) return;
        switch (code)
        {
            case 1:
                txtSystemStatus.color = Color.red;
                txtSystemStatus.text  = "¡ALERTA: CORTOCIRCUITO DETECTADO!";
                break;
            case 2:
                txtSystemStatus.color = new Color(1f, 0.5f, 0f);
                txtSystemStatus.text  = "ESTADO: CIRCUITO ABIERTO (0 mA)";
                break;
            default:
                txtSystemStatus.color = Color.green;
                txtSystemStatus.text  = "ESTADO: OPERACIÓN SEGURA";
                break;
        }
    }
}
