using System;
using System.Text;
using UnityEngine;
using Fusion;

/// <summary>
/// Enlace de red asimétrico. Comunica la interfaz del Técnico (PC) con el Arduino físico en VR.
///
/// OPCIÓN A — Auto-detección en runtime:
///   Cuando Fusion spawnea este NetworkBehaviour en cualquier cliente, dispara
///   OnBridgeReady. TechnicianTelemetryUI y ArduinoIDEUI se suscriben a ese evento
///   y se conectan solos sin necesitar referencias en el Inspector.
/// </summary>
public class ArduinoNetworkBridge : NetworkBehaviour
{
    [SerializeField] private ArduinoCore _arduinoCore;

    // ─── EVENTOS ESTÁTICOS ───────────────────────────────────────────────
    /// <summary>Dispara en todos los clientes cuando el bridge está listo en red.</summary>
    public static event Action<ArduinoNetworkBridge> OnBridgeReady;
    /// <summary>Dispara en todos los clientes cuando el bridge se despawnea.</summary>
    public static event Action<ArduinoNetworkBridge> OnBridgeDestroyed;
    
    // CORRECCIÓN: Se actualizó el Action para aceptar delayOnMs y delayOffMs
    /// <summary>Dispara en todos los clientes cuando el Técnico sube un nuevo sketch.</summary>
    public static event Action<int, PinMode, PinState, bool, int, int> OnSketchReceived;

    // ─── TELEMETRÍA CONTINUA (VR → PC) ──────────────────────────────────
    [Networked]
    public int NetworkedAnalogValue { get; set; }

    // ─────────────────────────────────────────────
    //  Fusion lifecycle
    // ─────────────────────────────────────────────

    void Awake()
    {
        _arduinoCore = GetComponent<ArduinoCore>();
    }

    /// <summary>
    /// Llamado por Fusion en TODOS los clientes cuando el objeto entra en la red.
    /// Notifica a los listeners (TechnicianTelemetryUI, ArduinoIDEUI, etc.) para
    /// que se conecten dinámicamente sin depender del Inspector.
    /// </summary>
    public override void Spawned()
    {
        _arduinoCore ??= GetComponent<ArduinoCore>();
        Debug.Log($"[ArduinoNetworkBridge] Spawned en {(HasStateAuthority ? "Host" : "Client")}. " +
                  "Notificando listeners...");
        OnBridgeReady?.Invoke(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        OnBridgeDestroyed?.Invoke(this);
    }

    // ─────────────────────────────────────────────
    //  Fusion update
    // ─────────────────────────────────────────────

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && _arduinoCore != null)
            NetworkedAnalogValue = _arduinoCore.GetAnalogReadA0();
    }

    // ─────────────────────────────────────────────
    //  RPC: Técnico → todos los clientes
    // ─────────────────────────────────────────────

    // CORRECCIÓN: RPC ahora soporta pulsos asimétricos (delayOnMs y delayOffMs separados)
    /// <summary>
    /// El parámetro <paramref name="pin"/> es el número de pin seleccionado por el Técnico
    /// en el ArduinoIDEUI. Se usa int en lugar de NetworkInt porque Fusion requiere tipos básicos.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SubirCodigoArduino(int pin, NetworkBool isOutput, NetworkBool isHigh, int delayOnMs, int delayOffMs, NetworkBool isBlink)
    {
        Debug.Log($"[NetworkBridge] Sketch recibido — pin D{pin}, output={isOutput}, blink={isBlink}, on={delayOnMs}ms, off={delayOffMs}ms.");
        DeliverSketch(pin, isOutput, isHigh, delayOnMs, delayOffMs, isBlink);
    }

    /// <summary>
    /// Aplica un sketch al ArduinoCore de la escena y notifica a los listeners (OnSketchReceived).
    /// Punto ÚNICO de entrega: lo usa tanto el RPC de este bridge (escena única / IntegratedDemo)
    /// como GameSession.RPC_SubirCodigoArduino, que es el canal COMPARTIDO del setup asimétrico
    /// — el bridge es un objeto de escena que solo existe en Explorador.unity y NO se replica al
    /// Host, así que en escenas separadas el código debe viajar por GameSession (spawneada por el
    /// Host y replicada a ambos).
    /// </summary>
    public static void DeliverSketch(int pin, bool isOutput, bool isHigh, int delayOnMs, int delayOffMs, bool isBlink)
    {
        // UnityEngine.Object completo: NetworkBehaviour tiene una propiedad de instancia
        // 'Object' (el NetworkObject de Fusion) que ocultaría a UnityEngine.Object aquí.
        var core = UnityEngine.Object.FindAnyObjectByType<ArduinoCore>();
        if (core != null)
            core.RecibirCodigoDePC(pin, isOutput, isHigh, delayOnMs, delayOffMs, isBlink);

        PinMode  mode    = isOutput ? PinMode.OUTPUT : PinMode.INPUT;
        PinState state   = isHigh   ? PinState.HIGH  : PinState.LOW;

        // Disparamos el evento con ambos tiempos para que las interfaces (IDE, Telemetría) sean precisas
        OnSketchReceived?.Invoke(pin, mode, state, isBlink, delayOnMs, delayOffMs);
    }

    /// <summary>
    /// MULTI-PIN: aplica un sketch entregado como TEXTO. Lo parsea (varios pines OUTPUT con
    /// blink independiente) y lo carga en el ArduinoCore real. Es el canal que permite
    /// semáforos / secuencias / varios LEDs selectivos. Notifica con el pin principal para que
    /// telemetría / IDE / validación sigan reaccionando igual.
    /// </summary>
    public static void DeliverSketchText(string code)
    {
        var data = ArduinoCodeParser.Parse(code);
        int offMs = data.blinkOffMs == 0 ? data.blinkMs : data.blinkOffMs;

        var core = UnityEngine.Object.FindAnyObjectByType<ArduinoCore>();
        if (core != null)
        {
            if (data.pins != null && data.pins.Count > 0)
                core.LoadSketchMulti(data.pins, data.pinNumber, data.mode, data.state,
                                     data.blink, data.blinkMs, offMs);
            else
                core.RecibirCodigoDePC(data.pinNumber, data.mode == PinMode.OUTPUT,
                                       data.state == PinState.HIGH, data.blinkMs, offMs, data.blink);
        }

        OnSketchReceived?.Invoke(data.pinNumber, data.mode, data.state, data.blink, data.blinkMs, offMs);
        Debug.Log($"[ArduinoNetworkBridge] Sketch TEXTO aplicado: " +
                  $"{(data.pins != null ? data.pins.Count : 0)} pin(es) de salida.");
    }

    /// <summary>
    /// PROGRAMA LIBRE: entrega el sketch COMPLETO al intérprete del ArduinoCore (Reto 4 libre).
    /// El intérprete ejecuta setup()/loop() reales (variables, for/while, analogWrite, …).
    /// Dispara OnSketchReceived con un pin best-effort para que telemetría/monitor reaccionen.
    /// </summary>
    public static void DeliverSketchProgram(string code)
    {
        var core = UnityEngine.Object.FindAnyObjectByType<ArduinoCore>();
        if (core != null) core.LoadSketchProgram(code);

        var data = ArduinoCodeParser.Parse(code);   // solo para la notificación (no ejecuta)
        int offMs = data.blinkOffMs == 0 ? data.blinkMs : data.blinkOffMs;
        OnSketchReceived?.Invoke(data.pinNumber, data.mode, data.state, data.blink, data.blinkMs, offMs);
        Debug.Log($"[ArduinoNetworkBridge] Programa libre entregado ({code?.Length ?? 0} chars).");
    }

    // Reensamblado de sketches largos enviados por trozos (supera el límite de un RPC).
    static readonly StringBuilder _rxChunks = new StringBuilder();

    /// <summary>Recibe un trozo del sketch; al llegar el último, lo entrega completo al intérprete.</summary>
    public static void ReceiveChunk(int idx, int total, string chunk)
    {
        if (idx <= 0) _rxChunks.Clear();
        _rxChunks.Append(chunk);
        if (idx >= total - 1)
        {
            DeliverSketchProgram(_rxChunks.ToString());
            _rxChunks.Clear();
        }
    }

    // ─────────────────────────────────────────────
    //  Modo offline: simular spawn para pruebas sin Fusion
    // ─────────────────────────────────────────────

    /// <summary>
    /// Llama esto manualmente en modoOffline para que los listeners se conecten
    /// aunque Fusion no haya spawnado el objeto.
    /// </summary>
    public void SimularSpawnOffline()
    {
        _arduinoCore ??= GetComponent<ArduinoCore>();
        Debug.Log("[ArduinoNetworkBridge] Simulating spawn (offline mode).");
        OnBridgeReady?.Invoke(this);
    }
}