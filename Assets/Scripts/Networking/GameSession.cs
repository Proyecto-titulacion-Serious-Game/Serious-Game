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
    /// <summary>Deadline del reto actual en el reloj de RED (lo publica el Host al cargar cada
    /// reto). Ambos roles leen el tiempo restante de este MISMO timer de Fusion — sin esto, cada
    /// GameManager contaba su propio timer local (arrancado en momentos distintos por proceso) y
    /// al Explorador se le acababa el tiempo antes que al Técnico.</summary>
    [Networked] public TickTimer    RetoTimer               { get; set; }

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
    //  Errores → métricas del Host (dashboard docente)
    // ─────────────────────────────────────────────
    //  Los errores del Explorador (colocar un LED con la polaridad invertida, valor incorrecto,
    //  validación fallida del Reto 4…) se registraban SOLO en su PerformanceTracker local — pero el
    //  dashboard localhost y la subida a Sheets leen el tracker del HOST (Técnico), que mostraba
    //  "0 errores" aunque el Explorador sí se hubiera equivocado. El cliente los reenvía por aquí.

    /// <summary>Cliente → Host: registra un error en el PerformanceTracker del Host (métricas),
    /// con su mensaje descriptivo para la columna "Qué pasó" del dashboard.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RegistrarErrorRemoto(string categoria, string detalle)
    {
        var tracker = FindAnyObjectByType<PerformanceTracker>();
        if (tracker != null) tracker.AddError(string.IsNullOrEmpty(categoria) ? "General" : categoria, detalle);
        else Debug.LogWarning("[GameSession] RPC_RegistrarErrorRemoto: no hay PerformanceTracker en el Host.");
    }

    // ─────────────────────────────────────────────
    //  Timer del reto (host-autoritativo, reloj de red)
    // ─────────────────────────────────────────────

    /// <summary>Host: arranca el timer del reto en el reloj de red (0 o negativo = sin límite).</summary>
    public void IniciarTimerReto(float segundos)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        RetoTimer = segundos > 0f ? TickTimer.CreateFromSeconds(Runner, segundos) : TickTimer.None;
    }

    /// <summary>Tiempo restante del reto según el reloj de RED: null si el Host aún no publicó
    /// timer para este reto, 0 si ya expiró. Ambos roles ven el mismo valor.</summary>
    public float? TiempoRestanteReto()
    {
        if (Object == null || !Object.IsValid) return null;
        if (RetoTimer.Expired(Runner)) return 0f;
        return RetoTimer.RemainingTime(Runner);   // null si nunca se seteó (TickTimer.None)
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
    /// Un cliente (p.ej. el Explorador) avisa al Host que el reto actual terminó — con el
    /// resultado REAL (éxito, o fallo/timeout del temporizador). Host-autoritativo: el Host corre
    /// CompleteLevel en su GameManager con ESE resultado, registra la métrica y propaga el avance
    /// al resto vía AvanzarReto → RPC_CambiarReto.
    ///
    /// Antes esta RPC no tenía parámetro y SIEMPRE forzaba éxito (vía DebugCompleteCurrentLevel) —
    /// además CompleteLevel del cliente solo la llamaba "if (success)". Resultado: un timeout en
    /// el Explorador nunca llegaba al Host, que se quedaba esperando ese reto para siempre
    /// mientras el Explorador ya había avanzado/terminado solo (bug real reportado: "no apareció
    /// juego finalizado ni en el técnico ni en el explorador").
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SolicitarCompletarReto(bool success)
    {
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null) gm.CompleteLevelFromNetwork(success);
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
    /// <summary>Caída de voltaje real en el LED encendido (V, ~2 V típico). 0 = sin LED activo.
    /// Didáctico: "el LED consume ~2 V; el resto lo absorbe la resistencia en serie".</summary>
    public float TelemVLed      { get; private set; }
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
    public void RPC_PublicarTelemetria(float voltage, float currentmA, float powerW, int adc, int status, float vLed)
    {
        TelemVoltage   = voltage;
        TelemCurrentmA = currentmA;
        TelemPowerW    = powerW;
        TelemAdc       = adc;
        TelemStatus    = status;
        TelemVLed      = vLed;
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

    /// <summary>Publica el resumen del reto actual (llamar desde el Explorador). Funciona en solo/offline (local).
    /// Se manda POR TROZOS (mismo patrón que RPC_SubirSketchChunk): el resumen "rico" (nombre + estado
    /// de cada rama/cable + próxima acción) supera fácilmente los 512 bytes que Fusion permite por RPC
    /// —más aún con acentos/ñ, que en UTF-8 ocupan 2 bytes cada uno—. Reto 2 con sus 2 ramas llegó a
    /// 984 bytes; Fusion lo rechazaba SILENCIOSAMENTE (un warning en el log del emisor, sin excepción
    /// visible ni de vuelta al llamador) y el clipboard del Técnico quedaba vacío para siempre en ese
    /// reto, porque el resumen ya se había marcado como "enviado" en el reporter antes de saber que el
    /// RPC había fallado. Confirmado con un test de red real (2 procesos, Fusion log: "payload is too
    /// large (984 bytes). Max allowed: 512 bytes").</summary>
    public static void ReportarDiagnosticoReto(int reto, string resumen)
    {
        resumen ??= "";
        if (Instance != null && Instance.Object != null && Instance.Object.IsValid)
        {
            const int Chunk = 200;   // caracteres; conservador por el UTF-8 de acentos/ñ (hasta 2 bytes c/u)
            int total = Mathf.Max(1, Mathf.CeilToInt(resumen.Length / (float)Chunk));
            for (int i = 0; i < total; i++)
            {
                int start = i * Chunk;
                string trozo = resumen.Substring(start, Mathf.Min(Chunk, resumen.Length - start));
                Instance.RPC_ReportarDiagnosticoRetoChunk(reto, i, total, trozo);
            }
        }
        else
            OnDiagnosticoRetoActualizado?.Invoke(reto, resumen);   // sin red → evento local
    }

    readonly System.Text.StringBuilder _diagChunkBuffer = new System.Text.StringBuilder();
    int _diagChunkReto = -1;

    /// <summary>El Explorador publica un TROZO del resumen del reto; llega a todos (incl. Host/Técnico).
    /// Al llegar el último trozo, reensambla y dispara el evento igual que antes.</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_ReportarDiagnosticoRetoChunk(int reto, int idx, int total, string chunk)
    {
        if (idx == 0 || _diagChunkReto != reto) { _diagChunkBuffer.Clear(); _diagChunkReto = reto; }
        _diagChunkBuffer.Append(chunk);
        if (idx < total - 1) return;   // esperar el resto de los trozos

        string resumen = _diagChunkBuffer.ToString();
        _diagChunkBuffer.Clear();
        if (reto >= 1 && reto <= 4) _diagReto[reto] = resumen;
        Debug.Log($"[GameSession] Diagnóstico reto={reto} reensamblado ({total} trozo(s), {resumen.Length} chars) — " +
                  $"suscriptores={OnDiagnosticoRetoActualizado?.GetInvocationList().Length ?? 0}");
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

    // ─────────────────────────────────────────────
    //  Reto 4: Serial online (Arduino del Explorador → consola del IDE del Técnico)
    // ─────────────────────────────────────────────
    //  El ArduinoCore corre en la Quest: sus Serial.print y errores de runtime solo disparaban
    //  eventos locales, así que en el setup asimétrico el Técnico subía un sketch con
    //  Serial.println y su consola quedaba muda (y nunca se enteraba de un error de ejecución).
    //  TelemetryPublisher (Explorador) los batchea y publica por aquí (~5 Hz, ≤380 chars por RPC).

    public static event System.Action<string> OnSerialRemoto;
    public static event System.Action<string> OnErrorRemoto;

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_PublicarSerial(string batch)
    {
        OnSerialRemoto?.Invoke(batch);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_PublicarSerialError(string error)
    {
        OnErrorRemoto?.Invoke(error);
        Debug.Log($"[GameSession] Error de sketch reportado por red: {error}");
    }

    // ─────────────────────────────────────────────
    //  Caminadora KAT remota: Técnico (PC con la KAT por USB) → Explorador
    // ─────────────────────────────────────────────
    //  Caso: no se quiere/puede emparejar la KAT directo al visor Quest standalone. La KAT se
    //  conecta por USB a la PC del Técnico (que ya tiene soporte KAT completo vía KAT Gateway) y
    //  TecnicoKatBridge retransmite ahí la lectura cruda del SDK. El PlayerController del
    //  Explorador, con katViaRed=true, usa estos campos en vez de leer KATNativeSDK localmente —
    //  la calibración de orientación sigue siendo LOCAL (usa el headCamera del Explorador), solo
    //  el dato crudo de la caminadora viaja por red. [Networked] en vez de RPC por frame: son
    //  valores continuos (actualizan ~cada tick), no eventos discretos.

    [Networked] public Vector3     KatRedMoveSpeed    { get; set; }
    [Networked] public Quaternion  KatRedBodyRotation { get; set; }
    [Networked] public NetworkBool KatRedConectada    { get; set; }
    /// <summary>Se incrementa cada vez que el SDK del Técnico entrega datos frescos (equivalente
    /// networked de TreadMillData.lastUpdateTimePoint, que es un double y Fusion no sincroniza).</summary>
    [Networked] public int         KatRedDataSeq      { get; set; }
    /// <summary>Se incrementa en cada flanco de pulsación del botón de calibración de la KAT.</summary>
    [Networked] public int         KatRedBtnSeq       { get; set; }

    /// <summary>Solo el Técnico (StateAuthority) llama esto — lee la KAT conectada a SU PC.</summary>
    public void PublicarKatRemota(Vector3 moveSpeed, Quaternion bodyRotation, bool conectada,
                                   bool datosFrescos, bool botonPulsado)
    {
        if (!Object.HasStateAuthority) return;
        KatRedMoveSpeed    = moveSpeed;
        KatRedBodyRotation = bodyRotation;
        KatRedConectada    = conectada;
        if (datosFrescos)  KatRedDataSeq++;
        if (botonPulsado)  KatRedBtnSeq++;
    }
}
