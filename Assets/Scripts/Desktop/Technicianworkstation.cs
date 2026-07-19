using System;
using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// Controlador maestro de la estación de trabajo del Técnico.
/// Coordina: mesa física, manual, componentes, bandeja, mini HUD y panel de diagnóstico.
///
/// JERARQUÍA DE LA ESCENA:
///
/// [EM] TechnicianWorkstation   ← este script
///   ├─ [3D Cube]  Desk_Surface
///   ├─ [3D Cube]  Manual_Book
///   │   └─ [Canvas WS] Manual_Canvas
///   ├─ [EM] ComponentsArea
///   │   ├─ [3D Cyl] Comp_R100  ← DeskComponent + XRSimpleInteractable (VR opcional)
///   │   ├─ [3D Cyl] Comp_R220
///   │   ├─ [3D Cyl] Comp_R330
///   │   ├─ [3D Sph] Comp_LED
///   │   └─ [3D Cyl] Comp_Cap
///   ├─ [3D Cube]  SendingTray
///   │   └─ [Canvas WS] Tray_Canvas
///   ├─ [Canvas SS] MiniHUD              ← hudVoltaje / hudCorriente / hudReto
///   └─ [Canvas WS] DiagnosticPanel      ← txtDiagnostico / txtAccionSiguiente
///       (World Space frente al Técnico, visible en pantalla o en visor VR)
/// </summary>
public class TechnicianWorkstation : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector — Referencias
    // ─────────────────────────────────────────────

    [Header("Sistemas")]
    public GameManager       gameManager;
    public CircuitManager    circuit;
    public TechnicianManual  manual;
    public TechnicianActions technicianActions;

    [Header("Objetos de la mesa")]
    public Transform deskSurface;
    public Transform manualBook;
    public Transform sendingTray;
    public Transform componentsArea;

    [Header("Mini HUD — esquina de pantalla (Screen Space Overlay)")]
    public TMP_Text hudVoltaje;
    public TMP_Text hudCorriente;
    public TMP_Text hudReto;

    [Header("Panel de diagnóstico — World Space frente al Técnico")]
    [Tooltip("Muestra fallas activas y estado de cada componente.")]
    public TMP_Text txtDiagnostico;
    [Tooltip("Siguiente acción priorizada que el Técnico debe comunicar al Explorador.")]
    public TMP_Text txtAccionSiguiente;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private float            _hudTimer;
    private const float      HUD_INTERVAL = 0.2f;
    private DiagnosticSystem _diagnostic  = new DiagnosticSystem();

    // Referencias a las lambdas bridge guardadas para poder desuscribirlas en OnDestroy
    private Action           _bridgeCircuit;
    private Action<LevelType> _bridgeLevel;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    void Start()
    {
        if (gameManager       == null) gameManager       = FindAnyObjectByType<GameManager>();
        if (manual            == null) manual            = FindAnyObjectByType<TechnicianManual>();
        if (technicianActions == null) technicianActions = FindAnyObjectByType<TechnicianActions>();

        // circuit NO se busca aquí: con 4 CircuitManagers en escena FindFirstObjectByType
        // devolvería el primero encontrado (posiblemente inactivo). OnLevelLoaded lo asigna
        // desde gameManager.circuit, que siempre apunta al reto activo.
        if (circuit == null && gameManager != null)
            circuit = gameManager.circuit != null ? gameManager.circuit.GetCompanionCircuitManager() : null;

        // Observer Pattern: suscribirse a GameEvents
        GameEvents.OnCircuitUpdated += RefreshDiagnosticPanel;
        GameEvents.OnLevelChanged   += OnLevelLoaded;

        // Bridges almacenados como campos para poder desuscribirlos en OnDestroy
        _bridgeCircuit = () => GameEvents.RaiseCircuitUpdated();
        _bridgeLevel   = l  => GameEvents.RaiseLevelChanged(l);
        CircuitManager.OnCircuitChanged    += _bridgeCircuit;
        // Reto 2 apaga el CircuitManager viejo y corre todo por ProtoboardSimulator (MNA) — sin
        // esto el panel de diagnóstico se pintaba una sola vez al cargar el reto y quedaba
        // congelado, sin enterarse de cables/LEDs conectados después.
        ProtoboardSimulator.OnCircuitChanged += _bridgeCircuit;
        GameManager.OnLevelLoaded       += _bridgeLevel;
    }

    void OnDestroy()
    {
        GameEvents.OnCircuitUpdated     -= RefreshDiagnosticPanel;
        GameEvents.OnLevelChanged       -= OnLevelLoaded;
        CircuitManager.OnCircuitChanged    -= _bridgeCircuit;
        ProtoboardSimulator.OnCircuitChanged -= _bridgeCircuit;
        GameManager.OnLevelLoaded       -= _bridgeLevel;
    }

    void Update()
    {
        _hudTimer += Time.deltaTime;
        if (_hudTimer < HUD_INTERVAL) return;
        _hudTimer = 0f;
        RefreshMiniHUD();
    }

    // ─────────────────────────────────────────────
    //  Panel de diagnóstico (event-driven)
    // ─────────────────────────────────────────────

    // Diagnóstico COHERENTE por reto: cada uno mira los datos que realmente explican su falla
    // (Reto 1 = valor de resistencia vs. objetivo; Reto 2 = voltaje por rama del paralelo;
    // Reto 3 = polaridad por componente; Reto 4 = cableado de la protoboard). Todos leen los
    // componentes scene-wide (igual que GameManager.CumpleVictoriaRetos123), no circuit.components
    // — ese CircuitManager queda inactivo en el Reto 2 (Reto2CircuitGuard lo apaga) y daría datos
    // obsoletos.
    void RefreshDiagnosticPanel()
    {
        // Asimétrico en red: el Técnico NO tiene acceso al circuito 3D real (eso es exclusivo
        // del Explorador — regla inquebrantable del diseño, ver CLAUDE.md). gameManager.circuit/
        // protoSim en el cliente del Técnico son su copia LOCAL de la escena — nunca reflejan lo
        // que el Explorador realmente conectó, así que este panel quedaba pintando un diagnóstico
        // desactualizado (el estado inicial de falla) que competía visualmente con el diagnóstico
        // real que llega por red (ver TecnicoDiagnosticoUI, ahora en el mismo Clipboard_Canvas).
        // Con sesión de red activa, dejamos estos campos en manos de TecnicoDiagnosticoUI; el
        // cálculo local solo aplica offline (IntegratedDemo.unity, un solo cliente sin Fusion).
        if (GameSession.Instance != null) return;

        if (gameManager != null) circuit = gameManager.circuit != null ? gameManager.circuit.GetCompanionCircuitManager() : null;
        if (gameManager == null) return;

        switch (gameManager.currentLevel)
        {
            case LevelType.Arduino:
                var arduino  = UnityEngine.Object.FindAnyObjectByType<ArduinoCore>();
                var protoSim = gameManager.protoSim;
                Set(txtDiagnostico,     _diagnostic.GetDiagnosisArduino(arduino, protoSim));
                Set(txtAccionSiguiente, _diagnostic.GetNextActionArduino(arduino, protoSim));
                break;

            case LevelType.OhmLaw:
                Set(txtDiagnostico,     _diagnostic.GetDiagnosisOhmLaw());
                Set(txtAccionSiguiente, _diagnostic.GetNextActionOhmLaw());
                break;

            case LevelType.Parallel:
                Set(txtDiagnostico,     _diagnostic.GetDiagnosisParallel());
                Set(txtAccionSiguiente, _diagnostic.GetNextActionParallel());
                break;

            case LevelType.Mixed:
                Set(txtDiagnostico,     _diagnostic.GetDiagnosisMixed());
                Set(txtAccionSiguiente, _diagnostic.GetNextActionMixed());
                break;
        }
    }

    // ─────────────────────────────────────────────
    //  Mini HUD (polling cada 0.2 s)
    // ─────────────────────────────────────────────

    void RefreshMiniHUD()
    {
        if (gameManager == null) return;

        // Reto 4: mini HUD con datos Arduino
        if (gameManager.currentLevel == LevelType.Arduino)
        {
            var arduino  = UnityEngine.Object.FindAnyObjectByType<ArduinoCore>();
            var protoSim = gameManager.protoSim;
            bool  pinOk  = arduino != null && arduino.activePinMode == PinMode.OUTPUT && arduino.PinsRecentlyDriven().Any();
            float ardMA  = protoSim != null ? protoSim.totalCurrentmA : 0f;

            Set(hudVoltaje,
                $"Pin: D{(arduino != null ? arduino.activePinNumber : 0)}  " +
                $"{(pinOk ? "OK" : "FALLA")}  |  I: {ardMA:F1}mA  " +
                $"{(protoSim != null && protoSim.isOpenCircuit ? "ABIERTO" : ardMA > 25f ? "SOBRECARGA" : "OK")}");

            var sb2 = new StringBuilder();
            if (arduino != null)
            {
                sb2.AppendLine(pinOk ? $"Sketch: D{arduino.activePinNumber} OK" : $"Sketch: D{arduino.activePinNumber} incompleto");
                sb2.AppendLine($"Modo: {arduino.activePinMode} | {arduino.activePinState}");
                sb2.AppendLine($"Vout: {arduino.OutputVoltage:F2}V");
            }
            Set(hudCorriente, sb2.Length > 0 ? sb2.ToString().TrimEnd() : "Arduino no iniciado");
            Set(hudReto, $"RETO 4  —  Tiempo: {gameManager.remainingTime:F0}s");
            return;
        }

        if (circuit == null) return;

        float  vSource = 0f;
        float  mA      = circuit.totalCurrent * 1000f;
        string estado  = circuit.isShortCircuited ? "CORTOCIRCUITO"
                       : mA > 20f                 ? "SOBRECARGA"
                       : mA < 5f                  ? "MUY BAJA"
                       :                            "OK";

        // Acumular estado de todos los componentes (Reto 2: 2 LEDs, Reto 3: R+LED+Cap)
        var sb = new StringBuilder();
        foreach (var c in circuit.components)
        {
            if (c is VoltageSource vs)
            {
                vSource = vs.GetEffectiveVoltage();
            }
            else if (c is Resistor r)
            {
                sb.AppendLine(r.hasFault
                    ? $"R {r.resistance:F0}Ω  FALLA"
                    : $"R {r.resistance:F0}Ω  OK");
            }
            else if (c is LED led)
            {
                sb.AppendLine(led.isOn
                    ? $"LED  ON ({led.current*1000f:F0}mA)"
                    : $"LED  OFF ({led.current*1000f:F0}mA)");
            }
            else if (c is Capacitor cap)
            {
                sb.AppendLine(cap.polarityInverted ? "CAP  INVERTIDO" : "CAP  OK");
            }
            else if (c is ArduinoPin pin)
            {
                sb.AppendLine(pin.hasFault
                    ? $"D{pin.pinNumber}  FALLA (correcto: D{pin.correctPinNumber})"
                    : $"D{pin.pinNumber}  OK");
            }
        }

        Set(hudVoltaje,   $"V: {vSource:F1}V  |  I: {mA:F1}mA  {estado}");
        Set(hudCorriente, sb.Length > 0 ? sb.ToString().TrimEnd() : "—");
        Set(hudReto,      $"RETO {(int)gameManager.currentLevel + 1}  —  " +
                          $"Tiempo: {gameManager.remainingTime:F0}s");
    }

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────

    void OnLevelLoaded(LevelType level)
    {
        // GameManager cambia la referencia circuit al activar cada zona
        if (gameManager != null)
            circuit = gameManager.circuit != null ? gameManager.circuit.GetCompanionCircuitManager() : null;

        RefreshDiagnosticPanel();
        Debug.Log($"[TechnicianWorkstation] Reto {(int)level + 1} cargado.");
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    void Set(TMP_Text t, string s) { if (t != null) t.text = s; }
}