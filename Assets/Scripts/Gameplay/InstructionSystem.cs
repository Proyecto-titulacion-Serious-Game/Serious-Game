using UnityEngine;

/// <summary>
/// Sistema de pasos e instrucciones del reto activo.
/// Valida automáticamente las condiciones de cada paso y avanza
/// cuando el Explorador o el Técnico cumplen el requisito.
/// </summary>
/// <remarks>
/// Flujo por reto:
/// <list type="bullet">
///   <item>Reto 1: medir → verificar voltaje → seleccionar resistencia → reemplazar</item>
///   <item>Reto 2: medir → identificar LED con polaridad invertida → enviar LED bien orientado</item>
///   <item>Reto 3: capacitor → LED → resistencia</item>
///   <item>Reto 4: Sandbox — Tecnico escribe sketch (pin libre) → Explorador arma circuito → DFS valida</item>
/// </list>
/// </remarks>
public class InstructionSystem : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    /// <summary>Paso actual del reto (0-based). Solo lectura en runtime.</summary>
    [Header("Estado")]
    public int currentStep = 0;

    /// <summary>Multímetro del Explorador — se consulta para validar mediciones.</summary>
    [Header("Referencias")]
    public Multimeter multimeter;

    /// <summary>Gestor del juego — provee el nivel activo.</summary>
    public GameManager gameManager;

    /// <summary>Acciones del Técnico — se consulta para validar selecciones.</summary>
    public TechnicianActions technicianActions;

    // ─────────────────────────────────────────────
    //  Flags de progreso
    // ─────────────────────────────────────────────

    /// <summary>True cuando el Explorador realizó al menos una medición válida.</summary>
    [Header("Flags de progreso")]
    public bool hasMeasuredCorrectly = false;

    /// <summary>True cuando el Técnico seleccionó el componente correcto.</summary>
    public bool hasSelectedCorrectComponent = false;

    /// <summary>True cuando se aplicó la reparación al circuito.</summary>
    public bool hasAppliedFix = false;

    // ─────────────────────────────────────────────
    //  Instrucciones
    // ─────────────────────────────────────────────

    /// <summary>
    /// Array de instrucciones del reto activo.
    /// Se construye en BuildInstructions() al cargar cada nivel.
    /// </summary>
    [Header("Instrucciones generadas")]
    public string[] instructions;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    /// <summary>Inicializa y construye las instrucciones al arrancar.</summary>
    private void Start()
    {
        // El campo 'multimeter' quedó sin asignar en el Inspector del prefab GameManager_System:
        // sin esto, ValidateOhmLaw()/ValidateParallel() nunca pasan del paso 0 (la comprobación
        // "multimeter?.probeA != null" siempre da null), y el HUD del Explorador (PlayerFeedbackUI)
        // se queda mostrando el hint del primer paso durante TODO el reto, aunque ya lo completó.
        // Hay 2 Multimeter en la escena (uno colgado del propio GameManager_System, sin usar en
        // juego, y el prop VR real que sostiene el Explorador): FindObjectsByType + Exclude
        // inactivos prefiere el que esté realmente activo en la jerarquía.
        if (multimeter == null)
            foreach (var m in FindObjectsByType<Multimeter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                { multimeter = m; break; }

        ResetInstructions();
        BuildInstructions();
    }

    private bool _needsValidation = true;

    void OnEnable()
    {
        CircuitManager.OnCircuitChanged          += OnCircuitChanged;
        ProtoboardSimulator.OnCircuitChanged     += OnCircuitChanged;
        ProtoboardSimulator.OnSandboxValidated   += OnSandboxValidatedHandler;
        ArduinoNetworkBridge.OnSketchReceived    += OnSketchDataReceived;
    }

    void OnDisable()
    {
        CircuitManager.OnCircuitChanged          -= OnCircuitChanged;
        ProtoboardSimulator.OnCircuitChanged     -= OnCircuitChanged;
        ProtoboardSimulator.OnSandboxValidated   -= OnSandboxValidatedHandler;
        ArduinoNetworkBridge.OnSketchReceived    -= OnSketchDataReceived;
    }

    private void OnCircuitChanged() => _needsValidation = true;

    private void OnSketchDataReceived(int pin, PinMode mode, PinState state, bool blink, int blinkOnMs, int blinkOffMs)        => _needsValidation = true;

    /// <summary>Valida el paso actual solo cuando el circuito cambia.</summary>
    private void Update()
    {
        if (gameManager == null || !_needsValidation) return;
        ValidateCurrentStep();
        _needsValidation = false;
    }

    // ─────────────────────────────────────────────
    //  Construcción de instrucciones
    // ─────────────────────────────────────────────

    /// <summary>
    /// Genera el array de instrucciones según el nivel activo.
    /// Llamar al cargar cada nivel (GameManager.OnLevelLoaded).
    /// </summary>
    public void BuildInstructions()
    {
        if (gameManager == null) return;

        switch (gameManager.currentLevel)
        {
            case LevelType.OhmLaw:
                instructions = new[]
                {
                    "Paso 1: Pide al Explorador ENCENDER el interruptor de energía (rojo→verde) " +
                    "para cerrar el circuito, y conectar el multímetro a dos nodos.",
                    "Paso 2: Lee el voltaje medido e identifica la anomalía.",
                    "Paso 3: Selecciona la resistencia defectuosa en el panel.",
                    "Paso 4: Calcula el valor correcto y envíalo al Explorador. " +
                    "Si el LED no enciende, verifica que el interruptor esté encendido (verde)."
                };
                break;

            case LevelType.Parallel:
                instructions = new[]
                {
                    "Paso 1: El Explorador conecta los cables de cada rama (VCC -> R -> LED -> GND).",
                    "Paso 2: Pide medir el LED dañado: 0V / sin corriente = polaridad invertida.",
                    "Paso 3: Envía un LED NUEVO. El Explorador debe soltarlo EXACTAMENTE en el slot " +
                    "marcado de la protoboard (fila 3, columna 5) — ahí encaja solo y ya no se puede " +
                    "quitar; en cualquier otro sitio sigue agarrable. Luego conecta los cables de esa " +
                    "rama. Los 2 LED en verde = reto superado."
                };
                break;

            case LevelType.Mixed:
                instructions = new[]
                {
                    "Paso 1: Localiza el capacitor con humo — prioridad máxima.",
                    "Paso 2: Indica al Explorador girar el capacitor 180°.",
                    "Paso 3: Indica girar el LED invertido 180°.",
                    "Paso 4: Calcula la resistencia correcta (220Ω) y envíala."
                };
                break;

            case LevelType.Arduino:
                instructions = new[]
                {
                    "Paso 1: Escribe el sketch en el IDE. Objetivo: hacer parpadear un LED " +
                    "de forma segura. Elige cualquier pin digital (D2-D13), configura OUTPUT " +
                    "y usa HIGH + delay + LOW + delay en loop().",
                    "Paso 2: Pide al Explorador conectar el LED y una resistencia de 330 Ohm " +
                    "desde el pin elegido hasta GND en la protoboard. El sistema validara " +
                    "automaticamente cuando el circuito sea correcto."
                };
                break;

            default:
                instructions = new[] { "Nivel en desarrollo." };
                break;
        }
    }

    // ─────────────────────────────────────────────
    //  Validación automática de pasos
    // ─────────────────────────────────────────────

    /// <summary>Delega la validación al método correspondiente del reto activo.</summary>
    private void ValidateCurrentStep()
    {
        switch (gameManager.currentLevel)
        {
            case LevelType.OhmLaw:   ValidateOhmLaw();   break;
            case LevelType.Parallel: ValidateParallel();  break;
            case LevelType.Mixed:    ValidateMixed();     break;
            case LevelType.Arduino:  ValidateArduino();   break;
        }
    }

    // ── Reto 1 ─────────────────────────────────

    /// <summary>Valida los 4 pasos del Reto 1 — Ley de Ohm.</summary>
    private void ValidateOhmLaw()
    {
        switch (currentStep)
        {
            // Paso 0 → Explorador conecta ambas puntas
            case 0:
                if (multimeter?.probeA != null && multimeter?.probeB != null)
                    NextStep();
                break;

            // Paso 1 → Se lee un voltaje real
            case 1:
                if (multimeter != null && multimeter.measuredVoltage > 0f)
                {
                    hasMeasuredCorrectly = true;
                    NextStep();
                }
                break;

            // Paso 2 → Técnico selecciona la resistencia
            case 2:
                if (technicianActions != null && technicianActions.HasSelectedResistor())
                {
                    hasSelectedCorrectComponent = true;
                    NextStep();
                }
                break;

            // Paso 3 → Se aplica la reparación
            case 3:
                if (gameManager.HasPerformedRepair())
                    hasAppliedFix = true;
                break;
        }
    }

    // ── Reto 2 ─────────────────────────────────

    /// <summary>Valida los 3 pasos del Reto 2 — Circuito Paralelo.</summary>
    private void ValidateParallel()
    {
        switch (currentStep)
        {
            case 0:
                if (multimeter?.probeA != null && multimeter?.probeB != null)
                    NextStep();
                break;

            case 1:
                if (multimeter != null && multimeter.measuredVoltage > 0.1f)
                {
                    hasMeasuredCorrectly = true;
                    NextStep();
                }
                break;

            case 2:
                if (gameManager.HasPerformedRepair())
                    hasAppliedFix = true;
                break;
        }
    }

    // ── Reto 3 ─────────────────────────────────

    /// <summary>Valida los 4 pasos del Reto 3 — Circuito Mixto y Polaridad.</summary>
    private void ValidateMixed()
    {
        if (gameManager?.circuit == null) return;

        switch (currentStep)
        {
            // Paso 0 → Técnico detecta el capacitor humeante.
            // Auto-avanza en el primer tick: el capacitor ya está invertido al inicio.
            case 0:
                if (gameManager.circuit.components.Count == 0) { _needsValidation = true; break; }
                foreach (var comp in gameManager.circuit.components)
                    if (comp is Capacitor cap && cap.polarityInverted)
                    { NextStep(); return; }
                break;

            // Paso 1 → Explorador voltea el capacitor (prioridad: humo = riesgo crítico)
            case 1:
                foreach (var comp in gameManager.circuit.components)
                    if (comp is Capacitor cap && !cap.polarityInverted)
                    { NextStep(); return; }
                break;

            // Paso 2 → Explorador voltea el LED invertido
            case 2:
                bool ledCorregido = true;
                foreach (var comp in gameManager.circuit.components)
                    if (comp is LED led && led.polarityInverted)
                    { ledCorregido = false; break; }
                if (ledCorregido) NextStep();
                break;

            // Paso 3 → Técnico envió 220Ω y Explorador instaló la resistencia
            case 3:
                bool resCorregida = true;
                foreach (var comp in gameManager.circuit.components)
                    if (comp is Resistor r && (r.hasFault || !Mathf.Approximately(r.resistance, 220f)))
                    { resCorregida = false; break; }
                if (resCorregida && gameManager.circuit.components.Count > 0)
                    hasAppliedFix = true;
                break;
        }
    }

    // ── Reto 4 ─────────────────────────────────

    // Flag de validación dinámica del ProtoboardSimulator
    /// <summary>
    /// True cuando el DFS del ProtoboardSimulator confirmó que hay
    /// LED + resistencia >= 100 Ω + GND cerrado desde el pin activo.
    /// </summary>
    public bool sandboxCircuitValidated = false;

    void OnSandboxValidatedHandler(SandboxValidationResult result)
    {
        sandboxCircuitValidated = result.success;
        _needsValidation = true;
    }

    /// <summary>Valida los 2 pasos del Reto 4 — Sandbox Arduino (modo libre).</summary>
    private void ValidateArduino()
    {
        switch (currentStep)
        {
            // Paso 0 → Técnico escribe y sube sketch con BLINK + OUTPUT en cualquier pin
            case 0:
            {
                var arduino = FindAnyObjectByType<ArduinoCore>();
                if (arduino != null &&
                    arduino.blinkEnabled &&
                    arduino.activePinMode == PinMode.OUTPUT)
                    NextStep();
                break;
            }

            // Paso 1 → Explorador arma el circuito: DFS confirma LED + R >= 100Ω + GND
            case 1:
                if (sandboxCircuitValidated)
                    hasAppliedFix = true;
                break;
        }
    }

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────

    /// <summary>
    /// Devuelve el texto de la instrucción del paso actual.
    /// Retorna "Procedimiento completado." si se superaron todos los pasos.
    /// </summary>
    public string GetCurrentInstruction()
    {
        if (instructions == null || instructions.Length == 0)
            return "Sin instrucciones.";

        return currentStep < instructions.Length
            ? instructions[currentStep]
            : "Procedimiento completado.";
    }

    /// <summary>
    /// Avanza al siguiente paso y lo registra en consola.
    /// </summary>
    public void NextStep()
    {
        currentStep++;
        Debug.Log($"[InstructionSystem] ➡ Paso {currentStep}");
    }

    /// <summary>
    /// Reinicia todos los flags y el contador de pasos.
    /// Llamar al cargar un nuevo nivel.
    /// </summary>
    public void ResetInstructions()
    {
        currentStep                 = 0;
        hasMeasuredCorrectly        = false;
        hasSelectedCorrectComponent = false;
        hasAppliedFix               = false;
        sandboxCircuitValidated     = false;
    }

    /// <summary>
    /// True cuando el Técnico puede enviar la resistencia al Explorador.
    /// Requiere haber medido y seleccionado el componente correcto (paso 3+).
    /// </summary>
    public bool CanRepairResistor()
    {
        return gameManager != null && gameManager.currentLevel == LevelType.OhmLaw;
    }

    /// <summary>
    /// True cuando el Técnico puede autorizar la reparación del paralelo.
    /// Requiere haber completado las mediciones (paso 2+).
    /// </summary>
    public bool CanRepairParallel()
        => currentStep >= 2 && hasMeasuredCorrectly;
}