using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PORTAPAPELES DE OBJETIVOS — UI diegética WorldSpace para el Explorador VR.
///
/// Posicionamiento: hijo de un GO "Clipboard_VR" sobre la mesa, ligeramente
/// inclinado hacia el jugador (localEuler.x ≈ −15°).
/// Canvas 1/1000: 400 × 260 px → 40 × 26 cm en world space.
///
/// Suscripciones de red activas:
///   • GameManager.OnLevelLoaded          → actualiza header del reto
///   • GameManager.OnTimerTick            → muestra temporizador
///   • ArduinoNetworkBridge.OnSketchReceived → "Técnico programó pin Dxx"
///   • ComponentDeliverySystem.OnComponentSent → "Componente recibido"
///   • GameSession.OnValidacionSolicitada → "Evaluando circuito..."
///   • GameSession.OnResultadoValidacion  → resultado final
///   • InstructionSystem (polling cada INTERVAL)
/// </summary>
public class ExplorerTaskClipboard : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias de sistema")]
    public GameManager      gameManager;
    public InstructionSystem instructionSystem;

    [Header("TMP — Header")]
    [Tooltip("Texto del reto activo. Ej: 'RETO 4: SINCRONIZACIÓN HARDWARE/SOFTWARE'")]
    public TMP_Text txtRetoHeader;
    [Tooltip("Ej: 'Paso 2 de 5 · 12:34'")]
    public TMP_Text txtPasoTimer;

    [Header("TMP — Estado dinámico")]
    [Tooltip("Texto grande central de estado actual.")]
    public TMP_Text txtStatus;
    [Tooltip("Texto secundario con detalle adicional.")]
    public TMP_Text txtDetalle;

    [Header("Imagen — Barra de progreso")]
    public Image barraProgreso;

    [Header("Panel de notificación de red (aparece brevemente)")]
    public GameObject  panelNetEvento;
    public TMP_Text    txtNetEvento;
    public Image       imgNetEventoBg;

    [Header("Fondo del clipboard")]
    public Image fondoPanel;

    // ─────────────────────────────────────────────
    //  Colores semánticos
    // ─────────────────────────────────────────────
    static Color Col(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    static readonly Color _bgIdle    = Col("#08111E");
    static readonly Color _bgAccion  = Col("#0A1A10");
    static readonly Color _bgAlerta  = Col("#1A0E08");
    static readonly Color _bgPass    = Col("#051A10");
    static readonly Color _bgFail    = Col("#1A0808");

    static readonly Color _cyan      = Col("#00E5FF");
    static readonly Color _green     = Col("#00E676");
    static readonly Color _amber     = Col("#FFB300");
    static readonly Color _red       = Col("#FF3D3D");
    static readonly Color _muted     = Col("#6A8FA8");
    static readonly Color _textMain  = Col("#D8EEFF");

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private float  _refreshTimer;
    private const float INTERVAL = 0.15f;
    private float  _remainingTime;
    private int    _totalPasos = 5;

    private Coroutine _netEventCoroutine;

    // ─────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────
    void Awake()
    {
        if (gameManager       == null) gameManager       = FindAnyObjectByType<GameManager>();
        if (instructionSystem == null) instructionSystem = FindAnyObjectByType<InstructionSystem>();
    }

    void OnEnable()
    {
        GameManager.OnLevelLoaded                      += OnLevelLoaded;
        GameManager.OnTimerTick                        += OnTimerTick;
        GameManager.OnLevelCompleted                   += OnLevelCompleted;
        GameSession.OnValidacionSolicitada             += OnValidacionSolicitada;
        GameSession.OnResultadoValidacion              += OnResultadoValidacion;
        ArduinoNetworkBridge.OnSketchReceived          += OnSketchReceived;
        ComponentDeliverySystem.OnComponentSent        += OnComponentSent;

        // Robusto al toggle por zona (ZoneHUDTrigger lo activa/desactiva): al (re)aparecer,
        // re-sincroniza reto/paso/timer actuales que pudieron cambiar mientras estaba oculto.
        // Las suscripciones de arriba solo reciben eventos NUEVOS; esto recupera el estado vigente.
        RefreshAll();
    }

    void OnDisable()
    {
        GameManager.OnLevelLoaded                      -= OnLevelLoaded;
        GameManager.OnTimerTick                        -= OnTimerTick;
        GameManager.OnLevelCompleted                   -= OnLevelCompleted;
        GameSession.OnValidacionSolicitada             -= OnValidacionSolicitada;
        GameSession.OnResultadoValidacion              -= OnResultadoValidacion;
        ArduinoNetworkBridge.OnSketchReceived          -= OnSketchReceived;
        ComponentDeliverySystem.OnComponentSent        -= OnComponentSent;
    }

    void Start()
    {
        if (panelNetEvento != null) panelNetEvento.SetActive(false);
        RefreshAll();
    }

    void Update()
    {
        _refreshTimer += Time.deltaTime;
        if (_refreshTimer < INTERVAL) return;
        _refreshTimer = 0f;
        RefreshStatus();
        RefreshPasoTimer();
    }

    // ─────────────────────────────────────────────
    //  Eventos de red
    // ─────────────────────────────────────────────

    void OnLevelLoaded(LevelType level)
    {
        _totalPasos = level switch
        {
            LevelType.OhmLaw   => 4,
            LevelType.Parallel => 3,
            LevelType.Mixed    => 4,
            LevelType.Arduino  => 5,
            _                  => 4
        };

        string header = level switch
        {
            LevelType.OhmLaw   => "RETO 1: LEY DE OHM",
            LevelType.Parallel => "RETO 2: CIRCUITO PARALELO",
            LevelType.Mixed    => "RETO 3: CIRCUITO MIXTO",
            LevelType.Arduino  => "RETO 4: SINCRONIZACIÓN HARDWARE/SOFTWARE",
            _                  => "RETO EN CURSO"
        };

        if (txtRetoHeader != null)
        {
            txtRetoHeader.text  = header;
            txtRetoHeader.color = _cyan;
        }

        SetFondo(_bgIdle);
        RefreshStatus();
    }

    void OnTimerTick(float remaining)
    {
        _remainingTime = remaining;
        RefreshPasoTimer();
    }

    void OnLevelCompleted(LevelType level, bool success)
    {
        if (success)
        {
            SetStatus("¡RETO COMPLETADO!", "Excelente trabajo en equipo.", _green, _bgPass);
            SetFondo(_bgPass);
        }
        else
        {
            SetStatus("TIEMPO AGOTADO", "El reto no fue completado.", _red, _bgFail);
            SetFondo(_bgFail);
        }
        SetBarra(1f, success ? _green : _red);
    }

    void OnValidacionSolicitada()
    {
        SetStatus("EVALUANDO CIRCUITO...", "El sistema verifica tu montaje.", _amber, _bgAlerta);
        SetFondo(_bgAlerta);
        ShowNetEvento("⏳  Validación enviada al servidor...", _amber);
    }

    void OnResultadoValidacion(bool paso, int codigoMotivo)
    {
        if (paso)
        {
            SetStatus("✓  CIRCUITO APROBADO", "¡El montaje es correcto!", _green, _bgPass);
            SetFondo(_bgPass);
            ShowNetEvento("✓  APROBADO por el sistema", _green);
        }
        else
        {
            string motivo = ValidationMotivo.Texto(codigoMotivo);
            SetStatus($"✕  {motivo}", "Revisa el circuito e inténtalo de nuevo.", _red, _bgFail);
            SetFondo(_bgFail);
            ShowNetEvento($"✕  {motivo}", _red);
        }
    }

    void OnSketchReceived(int pin, PinMode mode, PinState state, bool blink, int delayOnMs, int delayOffMs)
    {
        string modeStr  = mode  == PinMode.OUTPUT  ? "SALIDA" : "ENTRADA";
        string blinkStr = blink ? $" · BLINK {delayOnMs}ms" : "";
        string detail   = $"Pin D{pin} · {modeStr}{blinkStr}";
        ShowNetEvento($"Arduino programado: {detail}", _cyan);

        // Actualizar detalle del clipboard si estamos en Reto 4
        if (gameManager != null && gameManager.currentLevel == LevelType.Arduino)
        {
            if (txtDetalle != null)
            {
                txtDetalle.text  = $"Código activo: {detail}";
                txtDetalle.color = _cyan;
            }
        }
    }

    void OnComponentSent(ComponentType tipo, float valor)
    {
        string nombre = tipo switch
        {
            ComponentType.Resistor  => $"Resistencia {valor:F0} Ω",
            ComponentType.LED       => "LED",
            ComponentType.Capacitor => "Capacitor",
            ComponentType.ArduinoPin => "Pin Arduino",
            _                       => "Componente"
        };
        ShowNetEvento($"Recibiste: {nombre} — ¡Recógelo!", _cyan);
    }

    // ─────────────────────────────────────────────
    //  Refresh periódico
    // ─────────────────────────────────────────────

    void RefreshAll()
    {
        if (gameManager != null) OnLevelLoaded(gameManager.currentLevel);
        RefreshStatus();
        RefreshPasoTimer();
    }

    void RefreshStatus()
    {
        if (instructionSystem == null || gameManager == null) return;

        int step = instructionSystem.currentStep;
        float progress = _totalPasos > 0 ? (float)step / _totalPasos : 0f;
        SetBarra(Mathf.Clamp01(progress), _cyan);

        if (gameManager.levelCompleted) return;

        (string main, string detail, Color col, Color bg) = GetStepText(
            gameManager.currentLevel, step);

        SetStatus(main, detail, col, bg);
    }

    void RefreshPasoTimer()
    {
        if (txtPasoTimer == null || instructionSystem == null) return;

        int    step = instructionSystem.currentStep;
        int    min  = Mathf.FloorToInt(_remainingTime / 60f);
        int    sec  = Mathf.FloorToInt(_remainingTime % 60f);
        string time = $"{min}:{sec:00}";

        txtPasoTimer.text  = $"Paso {step + 1} de {_totalPasos}  ·  {time}";
        txtPasoTimer.color = _remainingTime < 60f ? _red : _muted;
    }

    // ─────────────────────────────────────────────
    //  Textos de pasos por reto
    // ─────────────────────────────────────────────

    (string main, string detail, Color col, Color bg) GetStepText(LevelType level, int step)
    {
        return level switch
        {
            LevelType.OhmLaw => step switch
            {
                0 => ("Enciende el interruptor (rojo→verde)\ny pon la punta ROJA en el nodo +", "Tócalo con el control · Mano der + Trigger", _textMain, _bgIdle),
                1 => ("Coloca la punta NEGRA\nen el nodo negativo",       "Mano izquierda + Trigger",                  _textMain, _bgIdle),
                2 => ("Lee el voltaje.\nDíselo al Técnico",               "Espera el componente correcto",             _amber,    _bgAlerta),
                3 => ("Instala el componente\nen el slot indicado",        "Grip → arrastra al slot",                   _green,    _bgAccion),
                _ => ("Esperando instrucción...", "", _muted, _bgIdle)
            },
            LevelType.Parallel => step switch
            {
                0 => ("Identifica el LED\nque está apagado",              "Mide con el multímetro (0V = invertido)",   _amber,    _bgAlerta),
                1 => ("Reporta al Técnico:\nel LED está al revés",        "Espera el LED bien orientado",              _textMain, _bgIdle),
                2 => ("Coloca el LED con\nla polaridad correcta",         "Ánodo (+) al positivo, cátodo (−) a tierra", _green,   _bgAccion),
                _ => ("Esperando instrucción...", "", _muted, _bgIdle)
            },
            LevelType.Mixed => step switch
            {
                0 => ("HAY HUMO en el panel.\nLocaliza el capacitor",     "Busca el componente con símbolo de humo",   _red,      _bgAlerta),
                1 => ("Gira el capacitor 180°\npara corregir polaridad",  "Botón B (mano derecha)",                    _amber,    _bgAlerta),
                2 => ("Localiza el LED apagado\ny reporta su orientación","Observa la flecha en el cuerpo del LED",    _textMain, _bgIdle),
                3 => ("Instala el componente\nque envíe el Técnico",       "Grip → slot correcto",                      _green,    _bgAccion),
                _ => ("Esperando instrucción...", "", _muted, _bgIdle)
            },
            LevelType.Arduino => step switch
            {
                0 => ("Espera el sketch\ndel Tecnico",                    "El Tecnico elegira el pin. Escucha por radio.",  _textMain, _bgIdle),
                1 => ("Toma un LED de la bandeja\ny conectalo al pin",    "Grip → LED → inserta anodo en el pin indicado",  _amber,    _bgAccion),
                _ => ("Conecta resistencia\n>= 100 Ohm y cierra a GND",  "Grip → resistencia → protoboard → GND",          _green,    _bgAccion)
            },
            _ => ("Esperando instrucción...", "", _muted, _bgIdle)
        };
    }

    // ─────────────────────────────────────────────
    //  Helpers de UI
    // ─────────────────────────────────────────────

    void SetStatus(string main, string detail, Color textColor, Color bgColor)
    {
        if (txtStatus  != null) { txtStatus.text  = main;   txtStatus.color  = textColor; }
        if (txtDetalle != null) { txtDetalle.text = detail; txtDetalle.color = _muted;    }
        SetFondo(bgColor);
    }

    void SetFondo(Color c)
    {
        if (fondoPanel != null) fondoPanel.color = c;
    }

    void SetBarra(float t, Color color)
    {
        if (barraProgreso == null) return;
        barraProgreso.fillAmount = t;
        barraProgreso.color      = color;
    }

    void ShowNetEvento(string msg, Color color)
    {
        if (panelNetEvento == null) return;
        if (_netEventCoroutine != null) StopCoroutine(_netEventCoroutine);
        _netEventCoroutine = StartCoroutine(AnimNetEvento(msg, color));
    }

    IEnumerator AnimNetEvento(string msg, Color color)
    {
        panelNetEvento.SetActive(true);
        if (txtNetEvento   != null) { txtNetEvento.text   = msg;   txtNetEvento.color   = color; }
        if (imgNetEventoBg != null)   imgNetEventoBg.color = new Color(color.r, color.g, color.b, 0.12f);

        yield return new WaitForSeconds(3.5f);
        panelNetEvento.SetActive(false);
    }
}

/// <summary>
/// Convierte el código numérico de motivo de fallo (enviado por GameManager.ReportarResultado)
/// a un texto legible para el Explorador.
/// cod = 0  → éxito (no se usa en la rama de fallo)
/// cod > 0  → número de intentos incorrectos acumulados
/// </summary>
public static class ValidationMotivo
{
    public static string Texto(int cod)
    {
        if (cod <= 0)  return "Conexión inválida";
        if (cod == 1)  return "Conexión inválida — revisa el circuito";
        if (cod == 2)  return "2° intento fallido — mide con el multímetro";
        if (cod >= 3)  return $"Intento {cod} — pide ayuda al Técnico";
        return "Error de circuito";
    }
}
