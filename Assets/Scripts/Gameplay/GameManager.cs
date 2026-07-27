using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Controlador principal del juego (Modo Sandbox).
/// Gestiona los 4 retos del Serious Game VR evaluando la electrónica mediante validación física.
///
/// Retos 1-3: motor CircuitSimulator (ComponentSlot-based).
/// Reto 4:    motor ProtoboardSimulator (ProtoboardSlot-based, Arduino + Protoboard).
/// </summary>
public class GameManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias principales")]
    public CircuitSimulator    circuit;           // Motor Retos 1-3
    public ProtoboardSimulator protoSim;          // Motor Reto 4 (Arduino + Protoboard)
    public Multimeter          multimeter;
    public PerformanceTracker  performance;
    public InstructionSystem   instructionSystem;
    public HapticFeedback      haptics;
    
    [Header("Zonas de Reto")]
    public GameObject reto1Zone;
    public GameObject reto2Zone;
    public GameObject reto3Zone;
    public GameObject reto4Zone;
    [Tooltip("GO PC_Arduino (raíz de escena). Se activa solo durante Reto 4.")]
    public GameObject pcArduino;

    [Header("Transición entre retos")]
    [Tooltip("Segundos de pausa entre reto completado y carga del siguiente.")]
    public float zoneTransitionDelay = 3f;

    [Header("Debug")]
    [Tooltip("Permite usar GoToLevel() en builds de prueba.")]
    [SerializeField] private bool _debugMode = false;

    [Header("Configuración de niveles")]
    [Tooltip("Tiempo límite en segundos para cada reto (0 = sin límite).")]
    public float[] timeLimits = { 600f, 600f, 600f, 900f };

    [Tooltip("Reto 4: exigir ADEMÁS medir el resistor con el multímetro en modo OHMS antes de " +
             "aceptar la validación. Apagado por defecto: el reto se completa apenas el circuito " +
             "cumple el código del Técnico (pedido de diseño 2026-07-18); la medición queda como " +
             "práctica recomendada del manual, no como candado.")]
    public bool exigirMedicionOhmsReto4 = false;

    // ─────────────────────────────────────────────
    //  Estado
    // ─────────────────────────────────────────────
    [Header("Estado actual (solo lectura)")]
    [SerializeField] private LevelType _currentLevel    = LevelType.OhmLaw;
    [SerializeField] private int       _currentIndex    = 0;
    [SerializeField] private bool      _levelCompleted  = false;
    [SerializeField] private bool      _repairPerformed = false;
    private bool _vistoIncorrectoEnReto = false;   // el reto 1-3 estuvo incorrecto (para auto-completar al repararlo)
    private bool? _lastCorrectoLogged    = null;    // diagnóstico: último valor de 'correcto' logueado (evita spam)
    private string _diagOhm = "";                   // diagnóstico Reto 1: desglose de por qué no cumple victoria
    private string _diagParallel = "";              // diagnóstico Reto 2: estado de cada LED del paralelo
    private float  _lastParallelDiag = -10f;        // throttle del log de diagnóstico del Reto 2
    [SerializeField] private int       _wrongAttempts   = 0;
    [SerializeField] private float     _remainingTime   = 0f;
    [SerializeField] private bool      _timerActive     = false;
    private int   _lastTimerTickSecond = -1; // throttle de OnTimerTick a 1 vez por segundo, ver Update()
    private float _tiempoInicioReto = 0f;

    /// <summary>Segundos tras cargar un reto durante los que se ignora un "0 s restantes" que
    /// venga del reloj de RED — es el timer EXPIRADO del reto anterior mientras el deadline nuevo
    /// del Host todavía no se replica al cliente. Ver Update().</summary>
    private const float GRACIA_TIMER_RED = 3f;

    public LevelType currentLevel     => _currentLevel;
    public bool      levelCompleted   => _levelCompleted;
    public float     currentTimeLimit => _currentIndex < timeLimits.Length ? timeLimits[_currentIndex] : 600f;
    public float     remainingTime    => _remainingTime;
    public bool      timerActive      => _timerActive;

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────
    public static event Action<LevelType>       OnLevelLoaded;
    public static event Action<LevelType, bool> OnLevelCompleted;
    public static event Action<string>          OnFaultDetected;
    public static event Action                  OnGameCompleted;
    public static event Action<float>           OnTimerTick;
    public static event Action<LevelType>       OnTimerExpired;
    public static event Action<int>             OnZoneActivated;
    public static event Action<LevelType, bool> OnZoneTransitionStart;

    public bool HasPerformedRepair()  => _repairPerformed;
    public int  GetWrongAttempts()    => _wrongAttempts;
    public static void RaiseFaultDetected(string description) => OnFaultDetected?.Invoke(description);

    private const float RETO1_TARGET_VOLTAGE = 9f;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        ValidateZones();
    }

    // Resultado de la última validación sandbox — actualizado por OnSandboxValidated
    private SandboxValidationResult _lastSandboxResult;

    void OnSandboxResult(SandboxValidationResult result)
    {
        _lastSandboxResult = result;

        // AUTO-COMPLETAR Reto 4 (igual que la auto-evaluación de los Retos 1-3): apenas el
        // circuito CUMPLE el código del Técnico (validación del sandbox en éxito), el reto se
        // completa solo — sin exigir el botón físico. El botón sigue existiendo como chequeo
        // manual con feedback. La ventana de 2 s evita un auto-éxito espurio al cargar el reto.
        if (result.success && _currentLevel == LevelType.Arduino && !_levelCompleted
            && Time.time - _tiempoInicioReto > 2f
            && (!exigirMedicionOhmsReto4 || multimeter == null || multimeter.wasUsedInResistanceMode))
        {
            Debug.Log("[GameManager] ✅ Reto 4 auto-completado: el circuito cumple el código del Técnico.");
            PublicarDiagnosticoReto4(exito: true, nivel: 0, result);
            CompleteLevel(true);
        }
    }

    void Start()
    {
        // Suscribir eventos de red (GameSession)
        GameSession.OnRetoChanged          += OnNetworkRetoChanged;
        GameSession.OnCableFixed           += OnNetworkCableFixed;
        GameSession.OnValidacionSolicitada += OnNetworkValidacionSolicitada;

        // Suscribir validador dinámico del Reto 4 sandbox
        ProtoboardSimulator.OnSandboxValidated += OnSandboxResult;

        // Auto-evaluación de Retos 1-3: al cambiar el circuito, si ya está correcto, completa el reto.
        CircuitManager.OnCircuitChanged += OnCircuitChangedAutoCheck;
        // Reto 2 protoboard libre: el ProtoboardSimulator (MNA) también dispara la auto-evaluación,
        // así colocar/mover un componente en la placa reevalúa la victoria (LED encendido correcto).
        ProtoboardSimulator.OnCircuitChanged += OnCircuitChangedAutoCheck;

        if (FindAnyObjectByType<ExplorerOnboarding>() != null)
            ExplorerOnboarding.OnOnboardingComplete += OnOnboardingDone;
        else
            LoadLevel(0);
    }

    void OnOnboardingDone()
    {
        ExplorerOnboarding.OnOnboardingComplete -= OnOnboardingDone;
        LoadLevel(0);
    }

    // ── Callbacks de red ─────────────────────────────────────────────────

    /// <summary>El Técnico (Host) avanzó de reto — sincroniza el Explorador.</summary>
    void OnNetworkRetoChanged(int retoIndex)
    {
        // Solo actuar si el índice difiere del estado local (evitar bucle Host→RPC→Host)
        if (retoIndex != _currentIndex)
        {
            // El reto saliente terminó en el OTRO proceso (la victoria se evaluó allá y aquí solo
            // llega el cambio de reto). Sin esto, este cliente nunca dispara OnLevelCompleted para
            // ese reto → su PerformanceTracker queda SIN registro → el desglose por reto se corría
            // de fila (el Reto 4 se mostraba como "Reto 3 — Mixto"). Solo para el avance natural
            // +1 (no saltos F1-F3 de debug) y solo si aquí no se completó ya.
            if (!_levelCompleted && retoIndex == _currentIndex + 1)
                OnLevelCompleted?.Invoke(_currentLevel, true);
            LoadLevel(retoIndex);
        }
    }

    /// <summary>
    /// Evento de red legacy (paradigma lineal). En el sandbox del Reto 4 ya no hay
    /// cable suelto predefinido — se deja como no-op para no romper compatibilidad de red.
    /// </summary>
    void OnNetworkCableFixed()
    {
        protoSim?.MarkDirty();
        Debug.Log("[GameManager] OnNetworkCableFixed recibido (sandbox: no-op, solo marca dirty).");
    }

    /// <summary>Se solicitó validación en red (botón físico del Explorador en Reto 4, o F8 de
    /// precaución del Técnico en cualquier reto — ver TecnicoValidarPrecaucion).</summary>
    void OnNetworkValidacionSolicitada()
    {
        // El RPC llega a TODOS los clientes, pero solo debe evaluar el Explorador (dueño real de
        // los motores del reto). El Técnico también tiene GameManager (dashboard) y, para Retos
        // 1-3, su copia de NoonA trae sus propios CircuitManager/LED/Resistor — así que "¿existe
        // un motor local?" ya NO distingue Técnico de Explorador (antes se comprobaba contra
        // CircuitSimulator, motor que quedó sin ninguna instancia en la escena tras migrar a piezas
        // fijas — ver retos123_componentes_fijos.md — dejando este guard permanentemente cerrado y
        // el botón F8 mudo en Retos 1-3). Usamos el mismo chequeo de autoridad de red que ya usa
        // CompleteLevel(): HasStateAuthority=true → soy el Host (Técnico) → no evalúo.
        var gs = GameSession.Instance;
        bool soyTecnicoEnRed = gs != null && gs.Object != null && gs.Object.IsValid && gs.Object.HasStateAuthority;
        if (soyTecnicoEnRed) return;

        if (_currentLevel == LevelType.Arduino)
        {
            if (protoSim == null) protoSim = FindProtoSim();
            if (protoSim == null) return;   // el Explorador aún no cargó su sandbox del Reto 4
        }

        bool paso = EvaluacionManualBotonFisico();
        int  cod  = paso ? 0 : _wrongAttempts;
        GameSession.Instance?.ReportarResultado(paso, cod);
    }

    void Update()
    {
        if (!_timerActive || _levelCompleted) return;

        // TIMER EN RED: si hay sesión Fusion, AMBOS roles leen el tiempo restante del MISMO
        // timer de red (GameSession.RetoTimer, publicado por el Host). Antes cada GameManager
        // contaba su propio _remainingTime local, arrancado en momentos distintos en cada
        // proceso → al Explorador se le acababa el tiempo antes que al Técnico. El Host además
        // publica el timer aquí (lazy) por si GameSession spawneó después del LoadLevel inicial.
        var gsTimer = GameSession.Instance;
        bool enRed = gsTimer != null && gsTimer.Object != null && gsTimer.Object.IsValid;
        if (enRed && gsTimer.Object.HasStateAuthority && gsTimer.TiempoRestanteReto() == null)
            gsTimer.IniciarTimerReto(_remainingTime);

        float? restanteRed = enRed ? gsTimer.TiempoRestanteReto() : null;

        // GUARDA DE CARRERA AL CAMBIAR DE RETO: el cliente carga el reto nuevo por RPC_CambiarReto
        // ANTES de que el deadline nuevo del Host se replique en RetoTimer. En esa ventana
        // TiempoRestanteReto() todavía devuelve 0 — el timer EXPIRADO del reto anterior
        // (TickTimer.Expired → 0f) — así que el reto recién cargado nacía con 0:00 y se daba por
        // agotado en su primer Update(). Durante los primeros segundos de un reto se ignora un 0
        // que viene de red y se cuenta local hasta que llegue el deadline nuevo.
        if (restanteRed != null && restanteRed.Value <= 0f &&
            Time.time - _tiempoInicioReto < GRACIA_TIMER_RED)
            restanteRed = null;

        if (restanteRed != null) _remainingTime  = restanteRed.Value;
        else                     _remainingTime -= Time.deltaTime;

        // OnTimerTick solo alimenta HUDs de texto "min:seg" (TechnicianHUDController,
        // ExplorerTaskClipboard) — no necesitan más de 1 actualización por segundo, pero se
        // invocaba cada Update() (decenas de veces por segundo en el Técnico sin vsync),
        // reconstruyendo texto TMP en ambos roles todo el tiempo que corre un timer de reto.
        int currentSecond = Mathf.CeilToInt(Mathf.Max(_remainingTime, 0f));
        if (currentSecond != _lastTimerTickSecond)
        {
            _lastTimerTickSecond = currentSecond;
            OnTimerTick?.Invoke(_remainingTime);
        }

        if (_remainingTime <= 0f)
        {
            // El tiempo del reto es de REFERENCIA, no un límite duro: al agotarse solo se avisa
            // (OnTimerExpired) y la nota baja sola — PerformanceTracker.CalcularNota10 usa el
            // tiempo REAL transcurrido y su factor de tiempo ya cae a 0 al pasar el límite.
            // Antes acá se llamaba CompleteLevel(false), que cerraba el reto como FALLO aunque el
            // equipo estuviera a punto de resolverlo (y, en red, arrastraba al reto siguiente por
            // la carrera del RetoTimer — ver la guarda de arriba). El reto sigue jugable: lo
            // completa el auto-chequeo normal (CumpleVictoriaRetos123 / validación del sandbox)
            // cuando el equipo realmente lo resuelva.
            // _remainingTime se congela en 0 (la ventana de referencia se acabó); el tiempo real
            // de sobra lo mide PerformanceTracker.GetTime(), que sigue corriendo aparte.
            _remainingTime = 0f;
            _timerActive   = false;
            OnTimerExpired?.Invoke(_currentLevel);
        }
    }

    void OnDestroy()
    {
        ExplorerOnboarding.OnOnboardingComplete -= OnOnboardingDone;
        GameSession.OnRetoChanged              -= OnNetworkRetoChanged;
        GameSession.OnCableFixed               -= OnNetworkCableFixed;
        GameSession.OnValidacionSolicitada     -= OnNetworkValidacionSolicitada;
        ProtoboardSimulator.OnSandboxValidated -= OnSandboxResult;
        CircuitManager.OnCircuitChanged        -= OnCircuitChangedAutoCheck;
        ProtoboardSimulator.OnCircuitChanged   -= OnCircuitChangedAutoCheck;
    }

    // ─────────────────────────────────────────────
    //  API Pública
    // ─────────────────────────────────────────────
    public void RegisterRepairAction()
    {
        _repairPerformed = true;
        circuit?.MarkDirty();
        protoSim?.MarkDirty();   // Reto 4: sucia ProtoboardSimulator también
        Debug.Log("[GameManager] Modificación en la matriz Sandbox registrada.");
    }

    public void RegisterWrongAttempt(string reason = "")
    {
        _wrongAttempts++;
        string categoria = ClasificarError();
        string detalle   = DescribirError(categoria, reason);
        performance?.AddError(categoria, detalle);

        // Si este proceso es un CLIENTE (Explorador), reenviar el error al Host: su tracker es el
        // que alimenta el dashboard localhost y la subida a Sheets — sin esto, el docente veía
        // "0 errores" aunque el Explorador se hubiera equivocado (p.ej. LED con polaridad invertida).
        var gsErr = GameSession.Instance;
        if (gsErr != null && gsErr.Object != null && gsErr.Object.IsValid && !gsErr.Object.HasStateAuthority)
            gsErr.RPC_RegistrarErrorRemoto(categoria, detalle);

        Debug.Log($"[GameManager] Intento incorrecto #{_wrongAttempts} [{categoria}]: {reason}");

        // CHOQUE ELÉCTRICO: El jugador se equivocó al validar el circuito
        if (haptics != null) haptics.PlayError();
    }

    /// <summary>
    /// Clasifica el tipo de error vigente en el reto actual inspeccionando el estado del
    /// circuito y las fallas presentes. Alimenta el desglose "tipo de errores" del dashboard
    /// docente (categorías del documento: cortocircuito, polaridad, valor, voltaje, abierto, sobrecarga).
    /// </summary>
    /// <summary>
    /// Explicación humana del error para la columna "Qué pasó" del dashboard docente.
    /// En el Reto 4 el validador del sandbox ya dice exactamente qué pasó ("el circuito no
    /// llega a GND", "LED invertido"...) → se usa ese mensaje tal cual; en los Retos 1-3 se
    /// traduce la categoría a una frase entendible por el docente/estudiante.
    /// </summary>
    string DescribirError(string categoria, string reason)
    {
        if (_currentLevel == LevelType.Arduino && !string.IsNullOrEmpty(reason))
            return reason;

        switch (categoria)
        {
            case "Cortocircuito":        return "Cortocircuito: la resistencia del circuito quedó demasiado baja y la corriente se disparó.";
            case "Polaridad":            return "Un LED o capacitor está conectado con la polaridad invertida.";
            case "Valor de resistencia": return "La resistencia instalada no es el valor que el circuito necesita.";
            case "Voltaje de fuente":    return "La fuente de voltaje tiene una falla.";
            case "Conexión abierta":     return "El circuito no cierra: hay un cable o componente desconectado.";
            case "Conexión/pin":         return "El cableado físico no corresponde al pin que usa el código.";
            case "Sobrecarga":           return "Corriente excesiva: un componente recibe más de lo que soporta.";
            default: return string.IsNullOrEmpty(reason) ? "Intento de validación incorrecto." : reason;
        }
    }

    string ClasificarError()
    {
        // Reto 4 (sandbox): clasificar por el mensaje de validación.
        if (_currentLevel == LevelType.Arduino)
        {
            string m = (_lastSandboxResult.message ?? "").ToLowerInvariant();
            if (m.Contains("corto"))                       return "Cortocircuito";
            if (m.Contains("resist") || m.Contains("ohm")) return "Valor de resistencia";
            if (m.Contains("pin"))                         return "Conexión/pin";
            if (m.Contains("led") || m.Contains("cable") ||
                m.Contains("conect") || m.Contains("abiert")) return "Conexión abierta";
            return "Otro";
        }

        var c = circuit != null ? circuit : FindAnyObjectByType<CircuitSimulator>();
        if (c != null)
        {
            if (c.isShortCircuited) return "Cortocircuito";
            if (c.components != null)
            {
                foreach (var comp in c.components)
                {
                    if (comp is LED led && led.polarityInverted)       return "Polaridad";
                    if (comp is Capacitor cap && cap.polarityInverted) return "Polaridad";
                    if (comp is VoltageSource vs && vs.hasFault)       return "Voltaje de fuente";
                    if (comp is Resistor r)
                    {
                        if (r.isOverloaded) return "Sobrecarga";
                        if (r.hasFault)     return "Valor de resistencia";
                    }
                }
            }
            if (c.totalCurrent <= 0.0001f) return "Conexión abierta";
        }
        return "Otro";
    }

    public (bool pass, string motivo) EvaluacionManualBotonFisicoConResultado()
    {
        bool paso = EvaluacionManualBotonFisico();
        string msg = paso ? "Circuito correcto" : "Conexion invalida o valores fuera de rango";
        return (paso, msg);
    }

    /// <summary>
    /// Evalúa el circuito desde el botón físico.
    /// Retos 1-3: usa CircuitSimulator. Reto 4: usa ProtoboardSimulator + estados de ArduinoPin/Resistor.
    /// </summary>
    public bool EvaluacionManualBotonFisico()
    {
        switch (_currentLevel)
        {
            case LevelType.OhmLaw:
            case LevelType.Parallel:
            case LevelType.Mixed:
                return EvaluarCircuitSimulator();

            case LevelType.Arduino:
                return EvaluarReto4();
        }
        return false;
    }

    // ─────────────────────────────────────────────
    //  Evaluación Retos 1-3 (CircuitSimulator)
    // ─────────────────────────────────────────────
    bool EvaluarCircuitSimulator()
    {
        ForzarSimulacionRetos123();

        if (CumpleVictoriaRetos123()) { CompleteLevel(true); return true; }

        RegisterWrongAttempt("Error de circuito: conexion invalida o valores fuera de rango.");
        return false;
    }

    /// <summary>Fuerza el recálculo de ambos motores (Gameplay + Electrical) para evaluar con estados frescos.</summary>
    void ForzarSimulacionRetos123()
    {
        if (circuit == null) circuit = FindAnyObjectByType<CircuitSimulator>();
        circuit?.ForceSimulate();

        // CircuitManager (Electrical) es quien pinta el LED en Retos 1-3.
        foreach (var cm in FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
            if (cm != null) cm.ForceSimulate();
    }

    /// <summary>
    /// Comprueba SIN efectos secundarios si el circuito del reto actual (1-3) está correcto.
    /// Lo usan la evaluación manual (botón) y la auto-evaluación al cambiar el circuito.
    /// Mira las piezas FIJAS de la escena (no en slots).
    /// </summary>
    bool CumpleVictoriaRetos123()
    {
        if (circuit == null) circuit = FindAnyObjectByType<CircuitSimulator>();

        switch (_currentLevel)
        {
            case LevelType.OhmLaw:
            {
                bool resistorOk = false;
                foreach (var r in FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
                    if (r != null && r.nodeA != null && r.nodeB != null && !r.hasFault) { resistorOk = true; break; }

                bool ledOn = false;
                foreach (var l in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
                    if (l != null && l.nodeA != null && l.nodeB != null && l.isOn && l.state != LEDState.Overload)
                    { ledOn = true; break; }

                // Diagnóstico: si falla, deja claro QUÉ sub-condición no se cumple (resistor vs LED).
                _diagOhm = $"resistorOk={resistorOk}, ledOn={ledOn}";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Detalle costoso (StringBuilder a cada OnCircuitChanged ≈ 20 Hz): solo en dev,
                // para no generar presión de GC en la Quest durante la clase.
                if (!resistorOk || !ledOn)
                {
                    var sbR = new System.Text.StringBuilder();
                    foreach (var r in FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
                        if (r != null) sbR.Append($" [R '{r.name}' {r.resistance:0}Ω hasFault={r.hasFault}]");
                    var sbL = new System.Text.StringBuilder();
                    foreach (var l in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
                        if (l != null)
                        {
                            float va = l.nodeA != null ? l.nodeA.voltage : 0f;
                            float vb = l.nodeB != null ? l.nodeB.voltage : 0f;
                            sbL.Append($" [LED '{l.name}' isOn={l.isOn} state={l.state} I={l.current*1000f:0.#}mA Va={va:0.##} Vb={vb:0.##} invertido={l.polarityInverted}]");
                        }
                    // Estado del/los CircuitManager activos: dice si el reto tiene fuente/corriente y
                    // si el LED/resistor están realmente en la lista simulada (clave para I=0mA).
                    var sbC = new System.Text.StringBuilder();
                    foreach (var cm in FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
                        if (cm != null && cm.components != null && cm.components.Count > 0)
                        {
                            sbC.Append($" [CM '{cm.name}' top={cm.topology} Vsrc={cm.sourceVoltage:0.##} I={cm.totalCurrent*1000f:0.#}mA comps:");
                            foreach (var c in cm.components)
                                if (c != null) sbC.Append($" {c.GetType().Name}'{c.name}'={c.GetResistance():0}Ω");
                            sbC.Append("]");
                        }
                    _diagOhm += " |R:" + sbR + " |LED:" + sbL + " |CM:" + sbC;
                }
#endif

                return resistorOk && ledOn;
            }
            case LevelType.Parallel:
            {
                // Reto 2: TODOS los LEDs del paralelo (piezas fijas, scene-wide) deben estar
                // encendidos en estado SEGURO (verde/Correct). Chequeo inline —en vez de
                // AreAllLEDsOn, que mira slots primero— para evitar que slots ajenos (p.ej. el
                // protoboard del Reto 4) rompan el conteo, y con diagnóstico del estado real.
                int total = 0, ok = 0;
                var sbP = new System.Text.StringBuilder();
                foreach (var l in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
                {
                    if (l == null || l.nodeA == null || l.nodeB == null) continue;
                    total++;
                    bool ledOk = l.isOn && l.state == LEDState.Correct;
                    if (ledOk) ok++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    sbP.Append($" [LED '{l.name}' isOn={l.isOn} state={l.state} " +
                               $"I={l.current * 1000f:0.#}mA inv={l.polarityInverted} " +
                               $"Va={(l.nodeA != null ? l.nodeA.voltage : 0):0.#} " +
                               $"Vb={(l.nodeB != null ? l.nodeB.voltage : 0):0.#} R={l.resistance:0}]");
#endif
                }
                _diagParallel = $"LEDs total={total} ok={ok} →{sbP}";
                return total > 0 && ok == total;
            }

            case LevelType.Mixed:
            {
                bool ok = true; int cnt = 0;
                foreach (var r in FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
                {   if (r == null || r.nodeA == null || r.nodeB == null) continue;
                    cnt++; if (r.hasFault) ok = false; }
                foreach (var led in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
                {   if (led == null || led.nodeA == null || led.nodeB == null) continue;
                    cnt++; if (led.polarityInverted || led.state == LEDState.Overload || !led.isOn) ok = false; }
                foreach (var cap in FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
                {   if (cap == null || cap.nodeA == null || cap.nodeB == null) continue;
                    cnt++; if (cap.polarityInverted) ok = false; }

                return ok && cnt >= 2;
            }
        }
        return false;
    }

    /// <summary>
    /// Auto-evaluación de Retos 1-3 al cambiar el circuito: si ya quedó correcto, completa el reto
    /// (sin penalizar). Así NO hace falta un botón para los retos de reparación — al arreglar el
    /// circuito se completa solo. (Reto 4 usa su propio botón de validación.)
    /// </summary>
    void OnCircuitChangedAutoCheck()
    {
        if (_levelCompleted) return;
        if (_currentLevel == LevelType.Arduino) return;

        bool correcto = CumpleVictoriaRetos123();

        // Diagnóstico: loguear SOLO cuando cambia (no spam a 20 Hz). Dice si el circuito llegó a
        // "correcto" y el estado de los gates — para saber dónde se corta la victoria.
        if (_lastCorrectoLogged != correcto)
        {
            _lastCorrectoLogged = correcto;
            Debug.Log($"[GameManager] AutoCheck Reto {(int)_currentLevel + 1}: correcto={correcto} " +
                      $"(vistoIncorrecto={_vistoIncorrectoEnReto}, repair={_repairPerformed})." +
                      (_currentLevel == LevelType.OhmLaw   ? "  " + _diagOhm      :
                       _currentLevel == LevelType.Parallel ? "  " + _diagParallel : ""));
        }

        // Reto 2: mientras siga incompleto, loguear el estado de los LEDs cada ~1.5s para depurar
        // (por qué "ambos prendidos" no cuenta como victoria: overload, polaridad, conteo, etc.).
        if (_currentLevel == LevelType.Parallel && !correcto && Time.unscaledTime - _lastParallelDiag > 1.5f)
        {
            _lastParallelDiag = Time.unscaledTime;
            Debug.Log($"[GameManager] Reto 2 incompleto → {_diagParallel}");
        }

        if (!correcto) { _vistoIncorrectoEnReto = true; return; }   // recuerda que estuvo mal

        // Completar si el reto ESTUVO incorrecto antes O si el jugador hizo una reparación
        // (RegisterRepairAction → _repairPerformed). Ambos descartan el auto-completar en el
        // instante de carga (ahí los dos son false). Más robusto que depender solo del primero.
        if (_vistoIncorrectoEnReto || _repairPerformed)
        {
        // Bloqueamos la victoria durante los primeros 2 segundos de carga.
        // Esto le da tiempo al CircuitSimulator de detectar que faltan los cables
        // y apagar los LEDs antes de que se evalúe como correcto por error.
        if (Time.time - _tiempoInicioReto > 2.0f)
            {
            CompleteLevel(true);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Evaluación Reto 4 (Arduino + Protoboard)
    // ─────────────────────────────────────────────
    bool EvaluarReto4()
    {
        // Forzar validación síncrona AHORA para que un solo toque del botón refleje el circuito
        // actual. ForzarValidacion() dispara OnSandboxValidated → OnSandboxResult actualiza
        // _lastSandboxResult antes de que lo leamos aquí abajo.
        if (protoSim == null) protoSim = FindProtoSim();
        protoSim?.ForzarValidacion();

        // Circuito eléctricamente correcto Y (si el docente activó el candado) el Explorador ya
        // midió la resistencia con el multímetro en modo OHMS. Por defecto el candado está
        // apagado: cumplir el código basta para completar (ver exigirMedicionOhmsReto4).
        bool resistenciaMedida = !exigirMedicionOhmsReto4
                              || multimeter == null || multimeter.wasUsedInResistanceMode;

        if (_lastSandboxResult.success && resistenciaMedida)
        {
            PublicarDiagnosticoReto4(exito: true, nivel: 0, _lastSandboxResult);
            CompleteLevel(true);
            return true;
        }

        string motivo;
        if (_lastSandboxResult.success && !resistenciaMedida)
        {
            motivo = "Circuito correcto, pero falta medir la resistencia del resistor con el " +
                     "multimetro en modo OHMS antes de validar. (Presiona el boton fisico del panel " +
                     "del multimetro para cambiar de modo, y acerca la punta a un nodo o slot.)";
        }
        else
        {
            motivo = string.IsNullOrEmpty(_lastSandboxResult.message)
                ? "Circuito incompleto. Revisa que el circuito salga del pin programado y cierre en GND."
                : _lastSandboxResult.message;
        }

        RegisterWrongAttempt("Reto 4 — " + motivo);

        // Feedback graduado al Técnico (síntoma → pista → diagnóstico) según intentos fallidos.
        int nivel = Reto4Feedback.NivelPorIntentos(_wrongAttempts);
        if (_lastSandboxResult.success && !resistenciaMedida)
            PublicarDiagnosticoReto4(exito: false, nivel: nivel, _lastSandboxResult, Reto4Diagnostico.ResistenciaSinMedir);
        else
            PublicarDiagnosticoReto4(exito: false, nivel: nivel, _lastSandboxResult);
        return false;
    }

    /// <summary>
    /// Envía el resultado de validar el circuito del Reto 4 al Técnico vía GameSession
    /// (mismo canal que la telemetría). El Técnico lo muestra en la consola del IDE.
    /// En modo offline sin red (GameSession null) no hace nada.
    /// <paramref name="motivoOverride"/> permite forzar un diagnóstico que no se deriva de
    /// <paramref name="r"/> (p.ej. "falta medir en modo OHMS", que no es un fallo de simulación).
    /// </summary>
    void PublicarDiagnosticoReto4(bool exito, int nivel, SandboxValidationResult r, Reto4Diagnostico? motivoOverride = null)
    {
        if (GameSession.Instance == null) return;
        var motivo = motivoOverride ?? Reto4Feedback.Clasificar(r);
        GameSession.Instance.RPC_PublicarDiagnostico(exito, nivel, r.activatedPin, (int)motivo);

        // También al clipboard del Técnico: éxito, o el mismo feedback GRADUADO (síntoma→pista→causa).
        string resumen = exito
            ? "✅ Circuito Arduino correcto."
            : Reto4Feedback.Construir(nivel, r.activatedPin, motivo);
        GameSession.ReportarDiagnosticoReto(4, resumen);
    }

    // ─────────────────────────────────────────────
    //  Carga de niveles
    // ─────────────────────────────────────────────
    void LoadLevel(int index)
    {
        if (index >= 4) { CompleteGame(); return; }

        _currentIndex    = index;
        _currentLevel    = (LevelType)index;
        _levelCompleted  = false;
        _repairPerformed = false;
        _wrongAttempts   = 0;
        _vistoIncorrectoEnReto = false;
        _lastCorrectoLogged    = null;
        _tiempoInicioReto      = Time.time;

        float limit = (index < timeLimits.Length) ? timeLimits[index] : 0f;
        _remainingTime = limit;
        _timerActive   = limit > 0f;
        _lastTimerTickSecond = -1; // fuerza que el primer OnTimerTick del reto dispare de inmediato

        // Host: publicar el deadline del reto en el reloj de RED para que el Explorador cuente
        // con el mismo timer (ver Update). En el cliente esto es no-op (sin StateAuthority).
        GameSession.Instance?.IniciarTimerReto(_timerActive ? limit : 0f);
        
        performance?.ResetTracker();
        multimeter?.ResetProbes();
        multimeter?.ResetResistanceModeTracking();
        instructionSystem?.ResetInstructions();
        instructionSystem?.BuildInstructions();

        ActivateComponentsForLevel(_currentLevel);
        SetupLevel();

        // Retos 1-3: forzar simulación inicial en AMBOS motores. Esto hace que la auto-evaluación
        // vea el circuito YA con la falla aplicada (CumpleVictoria=false → _vistoIncorrectoEnReto=true),
        // garantizando que luego se complete al repararlo. Reto 4: marcar protoboard sucia.
        if (_currentLevel != LevelType.Arduino)
        {
            ForzarSimulacionRetos123();
        }
        else
        {
            protoSim?.MarkDirty();

            // En modo offline (sin Fusion) el ArduinoNetworkBridge nunca recibe Spawned().
            // Simulamos el spawn para que TechnicianTelemetryUI y ArduinoIDEUI se conecten.
            bool offline = ConnectionManager.Instance == null || ConnectionManager.Instance.modoOffline;
            if (offline)
            {
                var bridge = FindAnyObjectByType<ArduinoNetworkBridge>();
                bridge?.SimularSpawnOffline();
            }
        }

        OnZoneActivated?.Invoke(_currentIndex);
        OnLevelLoaded?.Invoke(_currentLevel);

        // Sincronizar cambio de reto a todos los clientes (solo el Host tiene StateAuthority)
        GameSession.Instance?.AvanzarReto(_currentIndex);
    }

    public void NextLevel()         => LoadLevel(_currentIndex + 1);
    public void RestartCurrentLevel() => LoadLevel(_currentIndex);
    public void GoToLevel(int index)
    {
        if (!_debugMode) return;
        LoadLevel(Mathf.Clamp(index, 0, 3));
    }

    /// <summary>DEBUG (tecla F4): marca el reto ACTUAL como completado con éxito —igual que si el
    /// jugador lo hubiera ganado— disparando OnLevelCompleted (métrica en PerformanceTracker,
    /// ¡FELICIDADES!, congelado de piezas) y la transición automática al siguiente reto.
    /// El guard _levelCompleted evita doble conteo.</summary>
    public void DebugCompleteCurrentLevel()
    {
        _debugMode = true;   // auto-habilita debug (por si se llega vía RPC desde un cliente)
        CompleteLevel(true);
    }

    /// <summary>Host-autoritativo: aplica el resultado REAL (éxito o fallo/timeout) que ya se
    /// evaluó en el cliente que realmente jugó el reto — a diferencia de DebugCompleteCurrentLevel
    /// (que siempre fuerza éxito, para el atajo F4), esto sincroniza también un timeout para que
    /// el Host no se quede esperando un reto que el cliente ya dio por terminado.</summary>
    public void CompleteLevelFromNetwork(bool success)
    {
        _debugMode = true;
        CompleteLevel(success);
    }

    // ─────────────────────────────────────────────
    //  Gestión de Zonas
    // ─────────────────────────────────────────────
    void ActivateComponentsForLevel(LevelType level)
    {
        if (reto1Zone != null) reto1Zone.SetActive(level == LevelType.OhmLaw);
        if (reto2Zone != null) reto2Zone.SetActive(level == LevelType.Parallel);
        if (reto3Zone != null) reto3Zone.SetActive(level == LevelType.Mixed);
        if (reto4Zone != null) reto4Zone.SetActive(level == LevelType.Arduino);
        if (pcArduino  != null) pcArduino.SetActive(level == LevelType.Arduino);

        // Panel de medición fijo (2026-07-24, "Panel de Medición"): hay UN multímetro por
        // Reto_Zone, hijo de esa zona — solo el de la zona recién activada queda activeInHierarchy.
        // Re-resolver acá después del SetActive de arriba mantiene 'multimeter' apuntando siempre
        // al panel correcto (lo usan CumpleVictoriaRetos1/EvaluarReto4 más abajo); si queda
        // apuntando al de una zona ya desactivada, measuredVoltage se congela y esas victorias
        // dejan de poder cumplirse.
        if (multimeter == null || !multimeter.gameObject.activeInHierarchy)
            multimeter = FindAnyObjectByType<Multimeter>();

        switch (level)
        {
            case LevelType.OhmLaw:
                circuit  = reto1Zone != null ? reto1Zone.GetComponentInChildren<CircuitSimulator>(true) : null;
                protoSim = null;
                break;
            case LevelType.Parallel:
                circuit  = reto2Zone != null ? reto2Zone.GetComponentInChildren<CircuitSimulator>(true) : null;
                protoSim = null;
                break;
            case LevelType.Mixed:
                circuit  = reto3Zone != null ? reto3Zone.GetComponentInChildren<CircuitSimulator>(true) : null;
                protoSim = null;
                break;
            case LevelType.Arduino:
                circuit  = null;           // Reto 4 usa ProtoboardSimulator, no CircuitSimulator
                protoSim = FindProtoSim();
                break;
        }
    }

    /// <summary>Busca el ProtoboardSimulator primero dentro de reto4Zone, luego en toda la escena.</summary>
    ProtoboardSimulator FindProtoSim()
    {
        if (reto4Zone != null)
        {
            var s = reto4Zone.GetComponentInChildren<ProtoboardSimulator>(true);
            if (s != null) return s;
        }
        return FindAnyObjectByType<ProtoboardSimulator>();
    }

    void SetupLevel()
    {
        switch (_currentLevel)
        {
            case LevelType.OhmLaw:
                OnFaultDetected?.Invoke("Reto 1: Circuito con falla.\nArma la red usando Ley de Ohm y valida con el boton fisico.");
                break;
            case LevelType.Parallel:
                OnFaultDetected?.Invoke("Reto 2: Rama abierta.\nCompleta el circuito paralelo para energizar los LEDs.");
                break;
            case LevelType.Mixed:
                OnFaultDetected?.Invoke("Reto 3: Multiples fallas.\nRevisa polaridades y codigos de colores.");
                break;
            case LevelType.Arduino:
                OnFaultDetected?.Invoke(
                    "Reto 4: Sandbox Arduino + Protoboard.\n" +
                    "  TECNICO: Escribe el sketch en el IDE (digitalWrite, analogWrite/PWM, blink, " +
                    "varios pines D2-D13 — lo que tu codigo pida).\n" +
                    "  EXPLORADOR: Arma el circuito que ese codigo necesita, desde el/los pines " +
                    "hasta GND (con LED o solo resistencia, segun el codigo).\n" +
                    "El reto se completa SOLO cuando el circuito cumple el codigo. El boton fisico " +
                    "sirve para comprobar y recibir diagnostico.");
                break;
        }
    }

    // ─────────────────────────────────────────────
    //  Finalización de Retos
    // ─────────────────────────────────────────────
    void CompleteLevel(bool success)
    {
        if (_levelCompleted) return;
        _levelCompleted = true;

        Debug.Log($"[GameManager] ✅ CompleteLevel(success={success}) — Reto {(int)_currentLevel + 1}. " +
                  "Disparando OnLevelCompleted (PlayerFeedbackUI → ¡FELICIDADES!) y transición.");

        if (success)
        {
            // 🎉 VIBRACIÓN DE VICTORIA: El jugador completó el reto correctamente
            if (haptics != null) haptics.PlayStrong();

            // Retos 1-3: congelar ComponentSlots instalados
            if (circuit != null)
            {
                foreach (var slot in circuit.todosLosSlots)
                {
                    if (slot == null || slot.InstalledObject == null) continue;
                    if (slot.InstalledObject.TryGetComponent<XRGrabInteractable>(out var grab)) grab.enabled = false;
                    if (slot.InstalledObject.TryGetComponent<Collider>(out var col))            col.enabled  = false;
                }
            }

            // Reto 4: congelar todos los XRGrabInteractable dentro de reto4Zone
            if (_currentLevel == LevelType.Arduino && reto4Zone != null)
            {
                foreach (var grab in reto4Zone.GetComponentsInChildren<XRGrabInteractable>(true))
                    grab.enabled = false;
            }
        }

        OnLevelCompleted?.Invoke(_currentLevel, success);
        OnZoneTransitionStart?.Invoke(_currentLevel, success);

        // ONLINE: si quien completó es el CLIENTE (Explorador), avisar al Host para que sincronice
        // el avance en AMBOS lados — antes solo se avisaba "if (success)": un timeout del reto en
        // el Explorador (CompleteLevel(false) por el timer) nunca llegaba al Host, que se quedaba
        // esperando ese reto para siempre mientras el Explorador ya había avanzado/terminado solo
        // (bug real reportado: "no apareció juego finalizado ni en el técnico ni en el explorador").
        var gs = GameSession.Instance;
        if (gs != null && gs.Object != null && gs.Object.IsValid && !gs.Object.HasStateAuthority)
            gs.RPC_SolicitarCompletarReto(success);

        StartCoroutine(TransitionToNextLevel());
    }

    IEnumerator TransitionToNextLevel()
    {
        yield return new WaitForSeconds(zoneTransitionDelay);
        NextLevel();
    }

    void CompleteGame()
    {
        OnGameCompleted?.Invoke();
        Debug.Log("[GameManager] Juego completado.");
    }

    // ─────────────────────────────────────────────
    //  Utilidades
    // ─────────────────────────────────────────────
    public bool IsVoltageCorrect()
    {
        if (multimeter == null) return false;
        const float tol = 0.5f;
        return Mathf.Abs(multimeter.measuredVoltage - RETO1_TARGET_VOLTAGE) <= tol;
    }

    void ValidateZones()
    {
        if (reto1Zone == null) Debug.LogWarning("[GameManager] reto1Zone no asignado.");
        if (reto2Zone == null) Debug.LogWarning("[GameManager] reto2Zone no asignado.");
        if (reto3Zone == null) Debug.LogWarning("[GameManager] reto3Zone no asignado.");
        if (reto4Zone == null) Debug.LogWarning("[GameManager] reto4Zone no asignado.");
        if (pcArduino  == null) Debug.LogWarning("[GameManager] pcArduino no asignado — PC_Arduino no se mostrará en Reto 4.");
    }
}
