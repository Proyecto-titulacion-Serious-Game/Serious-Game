using Fusion;
using UnityEngine;

/// <summary>
/// Objeto de red compartido entre Técnico y Explorador.
///
/// SETUP EN UNITY:
///   1. Crear un GameObject vacío llamado "GameSession" en AMBAS escenas
///      (Tecnico.unity y Explorador.unity).
///   2. Añadirle este script + NetworkObject (Fusion).
///   3. Guardarlo como prefab en Resources/GameSession.prefab
///      (Fusion lo usa para sincronizarlo automáticamente).
///
/// FLUJO:
///   Técnico llama RPC_EnviarComponente → Explorador recibe OnComponenteRecibido
///   Explorador instala → llama RPC_ComponenteInstalado → Técnico recibe OnComponenteInstalado
///   Técnico avanza reto → llama RPC_CambiarReto → ambos reciben OnRetoChanged
///   Técnico repara cable → llama RPC_FixLooseCable → ambos reciben OnCableFixed
/// </summary>
public class GameSession : NetworkBehaviour
{
    // ─────────────────────────────────────────────
    //  Singleton de red
    // ─────────────────────────────────────────────
    public static GameSession Instance { get; private set; }

    // ─────────────────────────────────────────────
    //  Estado compartido
    // ─────────────────────────────────────────────
    [Networked] public int          RetoActual              { get; set; }
    [Networked] public NetworkBool  HayComponentePendiente  { get; set; }
    [Networked] public int          TipoComponentePendiente { get; set; }
    [Networked] public float        ValorComponentePendiente { get; set; }
    [Networked] public int          VarianteComponentePendiente { get; set; }
    [Networked] public TickTimer    HeartbeatTimer          { get; set; }

    // Host resetea el timer cada N segundos; clientes detectan si supera el timeout.
    private const float HeartbeatInterval = 5f;
    private const float HeartbeatTimeout  = 10f;

    // ─────────────────────────────────────────────
    //  Eventos locales
    // ─────────────────────────────────────────────

    /// <summary>Explorador: el Técnico envió un componente.</summary>
    public static event System.Action<ComponentType, float, int> OnComponenteRecibido;

    /// <summary>Técnico: el Explorador instaló (o falló).</summary>
    public static event System.Action<bool>                 OnComponenteInstalado;

    /// <summary>Ambos: el reto cambió.</summary>
    public static event System.Action<int>                  OnRetoChanged;

    /// <summary>Reto 4: el Técnico reparó el cable suelto.</summary>
    public static event System.Action                       OnCableFixed;

    /// <summary>El Host no ha respondido en más de HeartbeatTimeout segundos.</summary>
    public static event System.Action                       OnHeartbeatTimeout;

    /// <summary>El Explorador solicitó validar el circuito.</summary>
    public static event System.Action                       OnValidacionSolicitada;

    /// <summary>El sistema reportó el resultado de la validación (paso, codigoMotivo).</summary>
    public static event System.Action<bool, int>            OnResultadoValidacion;

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    public override void Spawned()
    {
        Instance = this;
        Debug.Log($"[GameSession] Spawned. IsMine={Object.HasStateAuthority}  Reto={RetoActual}");

        if (Object.HasStateAuthority)
            HeartbeatTimer = TickTimer.CreateFromSeconds(Runner, HeartbeatTimeout);

        // Señal "Explorador (Arduino VR) listo": el ArduinoNetworkBridge solo existe en la escena
        // del Explorador, así que su OnBridgeReady solo se dispara allí. Lo reportamos por red para
        // que el IDE del Técnico pueda gatear el botón "Subir" y no enviar sketches al vacío.
        ArduinoNetworkBridge.OnBridgeReady     += HandleBridgeReady;
        ArduinoNetworkBridge.OnBridgeDestroyed += HandleBridgeGone;
        if (FindAnyObjectByType<ArduinoNetworkBridge>() != null)
            ReportarExploradorListo(true); // el bridge ya estaba spawneado antes que GameSession
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ArduinoNetworkBridge.OnBridgeReady     -= HandleBridgeReady;
        ArduinoNetworkBridge.OnBridgeDestroyed -= HandleBridgeGone;
        if (Instance == this) Instance = null;
    }

    void HandleBridgeReady(ArduinoNetworkBridge _) => ReportarExploradorListo(true);
    void HandleBridgeGone (ArduinoNetworkBridge _) => ReportarExploradorListo(false);

    void ReportarExploradorListo(bool listo)
    {
        if (Object == null || !Object.IsValid) return;
        RPC_ReportarExploradorListo(listo);
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {
            float? remaining = HeartbeatTimer.RemainingTime(Runner);
            if (remaining == null || remaining < HeartbeatTimeout - HeartbeatInterval)
                HeartbeatTimer = TickTimer.CreateFromSeconds(Runner, HeartbeatTimeout);
        }
        else
        {
            if (HeartbeatTimer.Expired(Runner))
            {
                Debug.LogWarning("[GameSession] Heartbeat timeout — Host no responde.");
                OnHeartbeatTimeout?.Invoke();
                // Silenciar hasta el próximo ciclo real para no spamear
                HeartbeatTimer = TickTimer.CreateFromSeconds(Runner, HeartbeatTimeout * 100f);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Técnico → Explorador: enviar componente
    // ─────────────────────────────────────────────

    public void EnviarComponente(ComponentType tipo, float valor,
                                 int variante = (int)ComponentVariant.Default)
    {
        if (!Object.HasStateAuthority) return;
        RPC_EnviarComponente((int)tipo, valor, variante);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
public void RPC_EnviarComponente(int tipo, float valor, int variante)
{
        if (Object.HasStateAuthority)
        {
            HayComponentePendiente      = true;
            TipoComponentePendiente     = tipo;
            ValorComponentePendiente    = valor;
            VarianteComponentePendiente = variante;
        }
        OnComponenteRecibido?.Invoke((ComponentType)tipo, valor, variante);
        Debug.Log($"[GameSession] Componente enviado: {(ComponentType)tipo} = {valor} variante={(ComponentVariant)variante}");
    }

    // ─────────────────────────────────────────────
    //  Explorador → Técnico: instalación
    // ─────────────────────────────────────────────

    public void ReportarInstalacion(bool exito)
    {
        RPC_ComponenteInstalado(exito);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ComponenteInstalado(NetworkBool exito)
    {
        if (Object.HasStateAuthority)
            HayComponentePendiente = false;
        OnComponenteInstalado?.Invoke(exito);
        Debug.Log($"[GameSession] Instalación: {(exito ? "correcta" : "incorrecta")}");
    }

    // ─────────────────────────────────────────────
    //  Reto 4: cable suelto
    // ─────────────────────────────────────────────

    /// <summary>Solo el Técnico (Host/StateAuthority) puede reparar el cable remotamente.</summary>
    public void ReportarCableReparado()
    {
        if (!Object.HasStateAuthority) return;
        RPC_FixLooseCable();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_FixLooseCable()
    {
        OnCableFixed?.Invoke();
        Debug.Log("[GameSession] Cable suelto reparado (Reto 4).");
    }

    // ─────────────────────────────────────────────
    //  Cambio de reto
    // ─────────────────────────────────────────────

    public void AvanzarReto(int nuevoReto)
    {
        if (!Object.HasStateAuthority) return;
        RPC_CambiarReto(nuevoReto);
    }

    /// <summary>
    /// Permite que un cliente (p.ej. el Explorador) PIDA al Host cambiar de reto.
    /// El cambio real es host-autoritativo: el Host aplica AvanzarReto y lo propaga a
    /// todos vía RPC_CambiarReto. Usado por el DebugLevelSkipper para que F1-F4 funcionen
    /// también desde el Explorador sin que el Host lo revierta.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SolicitarCambioReto(int nuevoReto)
    {
        AvanzarReto(nuevoReto);
    }

    /// <summary>
    /// Un cliente (p.ej. el Explorador) PIDE al Host completar el reto actual como si se hubiera
    /// ganado (tecla F4 debug). Host-autoritativo: el Host corre CompleteLevel en su GameManager,
    /// que registra la métrica y propaga el avance al resto vía AvanzarReto → RPC_CambiarReto.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SolicitarCompletarReto()
    {
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null) gm.DebugCompleteCurrentLevel();
        else Debug.LogWarning("[GameSession] RPC_SolicitarCompletarReto: no hay GameManager en el Host.");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_CambiarReto(int reto)
    {
        if (Object.HasStateAuthority)
        {
            RetoActual             = reto;
            HayComponentePendiente = false;
        }
        OnRetoChanged?.Invoke(reto);
        Debug.Log($"[GameSession] Nuevo reto: {reto}");
    }

    // ─────────────────────────────────────────────
    //  Validación del circuito
    // ─────────────────────────────────────────────

    /// <summary>Explorador solicita validación — notifica a todos los clientes.</summary>
    public void SolicitarValidacion() => RPC_SolicitarValidacion();

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_SolicitarValidacion()
    {
        OnValidacionSolicitada?.Invoke();
        Debug.Log("[GameSession] Validación solicitada.");
    }

    /// <summary>Reporta el resultado de la validación a todos los clientes.</summary>
    public void ReportarResultado(bool paso, int codigoMotivo) =>
        RPC_ReportarResultado(paso, codigoMotivo);

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_ReportarResultado(NetworkBool paso, int codigoMotivo)
    {
        OnResultadoValidacion?.Invoke(paso, codigoMotivo);
        Debug.Log($"[GameSession] Resultado validación: {(paso ? "✅" : "❌")} cod={codigoMotivo}");
    }

    // ─────────────────────────────────────────────
    //  Reto 4: código Arduino (canal COMPARTIDO)
    // ─────────────────────────────────────────────

    /// <summary>
    /// El Técnico sube un sketch. Viaja por GameSession (objeto spawneado por el Host y
    /// replicado al Explorador) en lugar del ArduinoNetworkBridge de escena, que no se
    /// replica entre escenas distintas. El Explorador, que tiene el ArduinoCore real, lo
    /// aplica vía ArduinoNetworkBridge.DeliverSketch (que además dispara OnSketchReceived,
    /// por lo que telemetría/validación/monitor siguen reaccionando sin cambios).
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SubirCodigoArduino(int pin, NetworkBool isOutput, NetworkBool isHigh, int delayOnMs, int delayOffMs, NetworkBool isBlink)
    {
        ArduinoNetworkBridge.DeliverSketch(pin, isOutput, isHigh, delayOnMs, delayOffMs, isBlink);
        Debug.Log($"[GameSession] Sketch RPC — pin D{pin}, output={isOutput}, blink={isBlink}.");
    }

    /// <summary>
    /// MULTI-PIN: el Técnico sube el SKETCH COMPLETO como texto; el Explorador lo parsea y
    /// soporta varios pines de salida independientes (semáforos, secuencias, LEDs selectivos).
    /// El texto se limita a ~480 caracteres para caber en un RPC de Fusion.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SubirSketchTexto(string codigo)
    {
        ArduinoNetworkBridge.DeliverSketchText(codigo);
        Debug.Log($"[GameSession] Sketch TEXTO RPC ({(codigo != null ? codigo.Length : 0)} chars).");
    }

    /// <summary>
    /// PROGRAMA LIBRE: el Técnico sube el sketch COMPLETO por TROZOS (un programa real puede
    /// superar el límite de caracteres de un solo RPC de Fusion). El Explorador reensambla los
    /// trozos y, al recibir el último, lo entrega al intérprete del ArduinoCore.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SubirSketchChunk(int idx, int total, string chunk)
    {
        ArduinoNetworkBridge.ReceiveChunk(idx, total, chunk);
    }

    // ─────────────────────────────────────────────
    //  Reto 4: telemetría Explorador → Técnico
    // ─────────────────────────────────────────────
    //  La simulación del sandbox (ProtoboardSimulator + ArduinoCore) corre en el Explorador.
    //  El Técnico (Host) NO tiene esos motores localmente, así que recibe la telemetría por
    //  RPC. Lo publica TelemetryPublisher desde el Explorador a ~5 Hz.

    /// <summary>Último voltaje de fuente del sandbox (V).</summary>
    public float TelemVoltage   { get; private set; }
    /// <summary>Última corriente total (mA).</summary>
    public float TelemCurrentmA { get; private set; }
    /// <summary>Última potencia total (W).</summary>
    public float TelemPowerW    { get; private set; }
    /// <summary>Última lectura ADC del A0 (0–1023).</summary>
    public int   TelemAdc       { get; private set; }
    /// <summary>0 = operación segura, 1 = cortocircuito, 2 = circuito abierto.</summary>
    public int   TelemStatus    { get; private set; }
    /// <summary>True tras recibir al menos una muestra por red (distingue "sin datos" de 0 V real).</summary>
    public bool  TelemHasData   { get; private set; }
    /// <summary>
    /// Momento (Time.unscaledTime) de la última telemetría recibida. La telemetría llega a ~5 Hz
    /// mientras el Explorador está despierto en el Reto 4; un corte = posible suspensión del visor.
    /// Lo usa <see cref="ExplorerLinkOverlay"/> como heartbeat para avisar al Técnico.
    /// </summary>
    public float LastTelemetryRealtime { get; private set; }

    /// <summary>El Explorador publica la telemetría del sandbox; llega a todos (incl. Host/Técnico).</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_PublicarTelemetria(float voltage, float currentmA, float powerW, int adc, int status)
    {
        TelemVoltage   = voltage;
        TelemCurrentmA = currentmA;
        TelemPowerW    = powerW;
        TelemAdc       = adc;
        TelemStatus    = status;
        TelemHasData   = true;
        LastTelemetryRealtime = Time.unscaledTime;
    }

    // ─────────────────────────────────────────────
    //  Reto 4: feedback graduado del circuito (Explorador → Técnico)
    // ─────────────────────────────────────────────
    //  El Explorador valida el circuito (ProtoboardSimulator vive en su escena) y publica
    //  el diagnóstico por RPC. El Técnico (ArduinoIDEUI) lo muestra en la consola del IDE.
    //  Ver Reto4Feedback para la lógica de niveles y los textos.

    /// <summary>True si la última validación fue exitosa.</summary>
    public bool DiagExito  { get; private set; }
    /// <summary>Nivel de ayuda: 1=síntoma, 2=pista de zona, 3=diagnóstico explícito.</summary>
    public int  DiagNivel  { get; private set; }
    /// <summary>Pin digital activado por el código del Técnico.</summary>
    public int  DiagPin    { get; private set; }
    /// <summary>Código de <see cref="Reto4Diagnostico"/> (motivo del fallo).</summary>
    public int  DiagMotivo { get; private set; }
    /// <summary>Se incrementa en cada diagnóstico nuevo; el Técnico lo usa para detectar cambios.</summary>
    public int  DiagSeq    { get; private set; }

    /// <summary>El Explorador publica el resultado de validar el circuito; llega a todos (incl. Técnico).</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_PublicarDiagnostico(NetworkBool exito, int nivel, int pin, int motivo)
    {
        DiagExito  = exito;
        DiagNivel  = nivel;
        DiagPin    = pin;
        DiagMotivo = motivo;
        DiagSeq++;
    }

    // ─────────────────────────────────────────────
    //  Diagnóstico por RETO en texto (Explorador → clipboard/HUD del Técnico)
    // ─────────────────────────────────────────────
    //  Resumen corto del estado del circuito de cada reto (LEDs, qué falta) para que el Técnico
    //  lo lea en su clipboard y ambos sepan qué hacer. Respeta la asimetría: es un RESUMEN (dato 2D),
    //  no el volcado completo del circuito.

    private readonly string[] _diagReto = new string[5];   // índice 1..4

    /// <summary>Último resumen de diagnóstico recibido para ese reto (1..4). "" si no hay.</summary>
    public string UltimoDiagnosticoReto(int reto) => (reto >= 1 && reto <= 4 && _diagReto[reto] != null) ? _diagReto[reto] : "";

    /// <summary>(reto, resumen). El Técnico (UI del clipboard) se suscribe para mostrarlo.</summary>
    public static event System.Action<int, string> OnDiagnosticoRetoActualizado;

    /// <summary>Publica el resumen del reto actual (llamar desde el Explorador). Funciona en solo/offline (local).</summary>
    public static void ReportarDiagnosticoReto(int reto, string resumen)
    {
        resumen ??= "";
        if (Instance != null && Instance.Object != null && Instance.Object.IsValid)
            Instance.RPC_ReportarDiagnosticoReto(reto, resumen);
        else
            OnDiagnosticoRetoActualizado?.Invoke(reto, resumen);   // sin red → evento local
    }

    /// <summary>El Explorador publica el resumen del reto; llega a todos (incl. Host/Técnico).</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_ReportarDiagnosticoReto(int reto, string resumen)
    {
        if (reto >= 1 && reto <= 4) _diagReto[reto] = resumen;
        OnDiagnosticoRetoActualizado?.Invoke(reto, resumen);
    }

    // ─────────────────────────────────────────────
    //  Reto 4: handshake "Explorador listo" (para gatear el botón Subir del Técnico)
    // ─────────────────────────────────────────────

    /// <summary>
    /// True cuando el Explorador tiene su Arduino (ArduinoNetworkBridge) vivo en VR.
    /// El IDE del Técnico (<see cref="ArduinoIDEUI"/>) gatea el botón "Subir" con esto para no
    /// enviar sketches al vacío antes de que el visor termine de cargar su escena.
    /// </summary>
    public bool ExploradorListo { get; private set; }

    /// <summary>El Explorador reporta si su Arduino VR está vivo; llega a todos (incl. Host/Técnico).</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_ReportarExploradorListo(NetworkBool listo)
    {
        ExploradorListo = listo;
        Debug.Log($"[GameSession] Explorador {(listo ? "LISTO" : "NO listo")} (Arduino VR).");
    }
}
