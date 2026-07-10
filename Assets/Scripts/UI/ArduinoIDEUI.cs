using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;   // New Input System (activeInputHandler = 1)
using TMPro;

/// <summary>
/// Interfaz de programación para el Técnico (Reto 4 Sandbox).
///
/// Dos modos:
///   - Modo Bloques: 4 dropdowns (pin, mode, state, extra) → generan preview de código.
///   - Modo Código Libre: TMP_InputField con parser Regex (<see cref="ArduinoCodeParser"/>).
///
/// Experiencia de IDE (todo con los campos ya cableados — codeEditor, btnUploadCode,
/// txtConsoleOutput, txtStatus):
///   - Consola acumulativa con historial (no sobrescribe una sola línea).
///   - Secuencia "Compilando… → Subiendo…" con feedback por pasos.
///   - Muestra todas las advertencias pedagógicas del parser, no solo el log principal.
///   - Atajos: Ctrl+Enter = subir, Ctrl+L = limpiar consola.
///   - El sketch se guarda en PlayerPrefs (no se pierde al cerrar).
///
/// Al compilar, extrae (pin, mode, state, blink, blinkMs) y lo envía al Explorador por el
/// canal de red COMPARTIDO (GameSession), con fallback a bridge directo y a ArduinoCore local.
/// </summary>
public class ArduinoIDEUI : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector — Modo Bloques
    // ─────────────────────────────────────────────
    [Header("Modo Bloques — Dropdowns")]
    public TMP_Dropdown pinDropdown;
    public TMP_Dropdown modeDropdown;
    public TMP_Dropdown actionDropdown;
    public TMP_Dropdown extraDropdown;

    [Header("Modo Bloques — Controles")]
    public Button         btnUploadCode;
    public TMP_InputField blinkMsInput;

    [Header("Modo Bloques — Textos")]
    public TMP_Text txtCodePreview;
    public TMP_Text txtStatus;
    public TMP_Text txtFileNameLabel;
    public TMP_Text txtConsoleOutput;

    // ─────────────────────────────────────────────
    //  Inspector — Modo Texto Libre (Sandbox)
    // ─────────────────────────────────────────────
    [Header("Modo Texto Libre (Sandbox)")]
    [Tooltip("Campo de texto multi-línea donde el técnico escribe código Arduino libre.")]
    public TMP_InputField codeEditor;
    [Tooltip("Botón que alterna entre Modo Bloques y Modo Texto Libre.")]
    public Button         btnToggleFreeText;
    [Tooltip("Label en btnToggleFreeText — se actualiza al cambiar modo.")]
    public TMP_Text       txtToggleLabel;

    [Header("Validación del circuito (Reto 4)")]
    [Tooltip("Botón opcional del Técnico para COMPROBAR el circuito que arma el Explorador. " +
             "Si funciona → misión cumplida; si no → seguir construyendo. Atajo: tecla F5.")]
    public Button btnComprobarCircuito;

    [Header("Red (opcional — auto-detectado si vacío)")]
    public ArduinoNetworkBridge bridge;

    [Header("Experiencia de IDE")]
    [Tooltip("Líneas máximas que conserva la consola antes de descartar las más viejas.")]
    public int maxConsoleLines = 14;
    [Tooltip("Retraso simulado de compilación (s) para que se sienta como un compilador real.")]
    public float compileDelay = 0.45f;
    [Tooltip("Retraso simulado de subida a la placa (s).")]
    public float uploadDelay = 0.35f;
    [Tooltip("Guardar/recuperar el último sketch entre sesiones (PlayerPrefs).")]
    public bool persistSketch = true;
    [Tooltip("Resaltado de sintaxis en el editor (overlay construido en runtime).")]
    public bool enableSyntaxHighlight = true;
    [Tooltip("Numeración de líneas a la izquierda del editor.")]
    public bool enableLineNumbers = true;

    // ─────────────────────────────────────────────
    //  Aliases para compatibilidad con scripts de editor
    // ─────────────────────────────────────────────
    public TMP_Dropdown   dropdownPin    { get => pinDropdown;    set => pinDropdown    = value; }
    public TMP_Dropdown   dropdownMode   { get => modeDropdown;   set => modeDropdown   = value; }
    public TMP_Dropdown   dropdownState  { get => actionDropdown; set => actionDropdown = value; }
    public TMP_Dropdown   dropdownExtra  { get => extraDropdown;  set => extraDropdown  = value; }
    public Button         btnCompilar    { get => btnUploadCode;  set => btnUploadCode  = value; }
    public TMP_InputField inputBlinkMs   { get => blinkMsInput;   set => blinkMsInput   = value; }
    public TMP_Text       txtFileName    { get => txtFileNameLabel; set => txtFileNameLabel  = value; }
    public TMP_Text       txtConsole     { get => txtConsoleOutput; set => txtConsoleOutput  = value; }

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private bool _freeTextMode = true;   // Reto 4 sandbox: arrancar siempre en código libre
    private bool _isCompiling   = false; // anti doble-click / doble Ctrl+Enter
    private int  _lastDiagSeq   = -1;    // -1 = aún sin sincronizar con GameSession

    // Gating del botón "Subir": solo habilitado cuando hay una placa que reciba el sketch.
    private float _readyCheckCd    = 0f;     // throttle del chequeo (no cada frame)
    private bool? _lastReadyLogged = null;   // para avisar solo al cambiar de estado

    private readonly List<string> _console = new List<string>();
    private const string PrefKey = "TITA.Reto4.LastSketch";

    // Colores reutilizables
    static readonly Color CInfo  = new Color(0.67f, 1f, 1f);     // cian claro
    static readonly Color COk    = new Color(0f, 1f, 0.5f);
    static readonly Color CWarn  = new Color(1f, 0.67f, 0f);
    static readonly Color CErr   = new Color(1f, 0.27f, 0.27f);

    // ─────────────────────────────────────────────
    //  Unity — suscripción a evento de red
    // ─────────────────────────────────────────────

    void OnEnable()
    {
        ArduinoNetworkBridge.OnBridgeReady     += OnBridgeConnected;
        ArduinoNetworkBridge.OnBridgeDestroyed += OnBridgeDisconnected;

        // Consola del IDE: salida de Serial.print y errores de ejecución del programa libre.
        // Local (ArduinoCore en la misma escena/offline) + REMOTO por red (setup asimétrico:
        // el Arduino corre en la Quest y TelemetryPublisher relaya por GameSession).
        ArduinoCore.OnProgramSerial += OnProgramSerial;
        ArduinoCore.OnProgramError  += OnProgramError;
        GameSession.OnSerialRemoto  += OnSerialRemoto;
        GameSession.OnErrorRemoto   += OnErrorRemoto;

        if (bridge == null)
            bridge = FindAnyObjectByType<ArduinoNetworkBridge>();
    }

    void OnDisable()
    {
        ArduinoNetworkBridge.OnBridgeReady     -= OnBridgeConnected;
        ArduinoNetworkBridge.OnBridgeDestroyed -= OnBridgeDisconnected;

        ArduinoCore.OnProgramSerial -= OnProgramSerial;
        ArduinoCore.OnProgramError  -= OnProgramError;
        GameSession.OnSerialRemoto  -= OnSerialRemoto;
        GameSession.OnErrorRemoto   -= OnErrorRemoto;

        if (persistSketch && codeEditor != null)
            SaveSketch();
    }

    void OnProgramSerial(string s) => LogLine($"<color=#CFE8FF>{s.TrimEnd('\n')}</color>");
    void OnProgramError(string s)  => LogLine($"<color=#FF6666>> {s}</color>");

    // Serial/errores que llegan POR RED desde el Arduino del Explorador. El RPC llega a TODOS los
    // peers: si esta máquina tiene su propio ArduinoCore (offline/escena única), el evento local
    // ya imprimió esta salida — ignorar el eco para no duplicar líneas en la consola.
    void OnSerialRemoto(string batch)
    {
        if (FindAnyObjectByType<ArduinoCore>() != null) return;
        foreach (var line in batch.Split('\n'))
            if (line.Length > 0) OnProgramSerial(line);
    }

    void OnErrorRemoto(string err)
    {
        if (FindAnyObjectByType<ArduinoCore>() != null) return;
        OnProgramError(err);
    }

    void OnBridgeConnected(ArduinoNetworkBridge b)
    {
        bridge = b;
        RefreshBoardReady(); // habilita el botón y avisa al cambiar de estado
    }

    void OnBridgeDisconnected(ArduinoNetworkBridge _)
    {
        bridge = null;
        RefreshBoardReady(); // re-gatea según si aún queda canal (red) hacia la placa
    }

    void Start()
    {
        if (pinDropdown    != null) pinDropdown.onValueChanged.AddListener(_ => UpdateCodePreview());
        if (modeDropdown   != null) modeDropdown.onValueChanged.AddListener(_ => UpdateCodePreview());
        if (actionDropdown != null) actionDropdown.onValueChanged.AddListener(_ => UpdateCodePreview());
        if (extraDropdown  != null) extraDropdown.onValueChanged.AddListener(_ => UpdateCodePreview());

        if (btnUploadCode    != null) btnUploadCode.onClick.AddListener(CompileAndSend);
        if (btnToggleFreeText != null) btnToggleFreeText.onClick.AddListener(ToggleFreeTextMode);
        if (btnComprobarCircuito != null) btnComprobarCircuito.onClick.AddListener(ComprobarCircuito);

        // El editor nunca arranca vacío: recupera el último sketch o carga el template.
        if (codeEditor != null && string.IsNullOrWhiteSpace(codeEditor.text))
        {
            string saved = persistSketch ? PlayerPrefs.GetString(PrefKey, "") : "";
            codeEditor.text = !string.IsNullOrWhiteSpace(saved)
                ? saved
                : ArduinoCodeParser.StarterTemplate;
        }

        // Resaltado de sintaxis + números de línea (overlay construido en runtime, sin tocar prefab).
        if (codeEditor != null && (enableSyntaxHighlight || enableLineNumbers))
        {
            var hl = codeEditor.GetComponent<ArduinoSyntaxHighlighter>();
            if (hl == null) hl = codeEditor.gameObject.AddComponent<ArduinoSyntaxHighlighter>();
            hl.Initialize(codeEditor, enableLineNumbers, enableSyntaxHighlight);
        }

        ApplyModeVisibility();
        UpdateCodePreview();

        // Banner de bienvenida en la consola (estilo IDE).
        LogLine("<color=#7FDBFF>TITA Arduino IDE - Reto 4</color>");
        LogLine("<color=#888888>Ctrl+Enter = Subir   |   Ctrl+L = Limpiar consola</color>");
        SetStatus("Esperando conexión de placa...", Color.grey);

        // El botón "Subir" arranca deshabilitado hasta que haya placa que reciba el sketch.
        if (btnUploadCode != null) btnUploadCode.interactable = false;
        RefreshBoardReady();
    }

    // ─────────────────────────────────────────────
    //  Atajos de teclado (New Input System)
    // ─────────────────────────────────────────────
    void Update()
    {
        PollDiagnosticoReto4();

        // Re-evaluar (con throttle) si la placa está lista → habilita/deshabilita "Subir".
        _readyCheckCd -= Time.unscaledDeltaTime;
        if (_readyCheckCd <= 0f) { _readyCheckCd = 0.25f; RefreshBoardReady(); }

        var kb = Keyboard.current;
        if (kb == null) return;

        // FIX: si el técnico teclea con el editor visible pero SIN foco (p.ej. tras enviar un
        // componente, que roba la selección del EventSystem) y la tecla NO va a otro campo de texto,
        // le devolvemos el foco al editor → la pulsación no se pierde y puede seguir editando.
        if (kb.anyKey.wasPressedThisFrame && !_isCompiling && codeEditor != null &&
            codeEditor.gameObject.activeInHierarchy && !codeEditor.isFocused)
        {
            var es  = UnityEngine.EventSystems.EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            // Solo cede el teclado si el otro campo REALMENTE está recibiendo input (activo y con
            // foco). Una selección colgada en un TMP_InputField inactivo o desenfocado (p.ej. el
            // campo de ohms de la bandeja tras cambiar de componente) se traga las teclas: nadie
            // las recibe y "no se puede escribir" — en ese caso recuperamos el editor.
            var otroField  = sel != null ? sel.GetComponent<TMP_InputField>() : null;
            bool otroCampo = otroField != null && sel.activeInHierarchy && otroField.isFocused;
            if (!otroCampo) codeEditor.ActivateInputField();
        }

        // F5 = comprobar el circuito (sin Ctrl).
        if (kb.f5Key.wasPressedThisFrame) { ComprobarCircuito(); return; }

        bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
        if (!ctrl) return;

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            CompileAndSend();
        else if (kb.lKey.wasPressedThisFrame)
            ClearConsole();
    }

    // ─────────────────────────────────────────────
    //  Feedback graduado del Reto 4 (Explorador → consola del Técnico)
    // ─────────────────────────────────────────────
    void PollDiagnosticoReto4()
    {
        var gs = GameSession.Instance;
        if (gs == null) return;

        // Sincronización inicial: no mostrar diagnósticos previos a esta sesión de UI.
        if (_lastDiagSeq < 0) { _lastDiagSeq = gs.DiagSeq; return; }
        if (gs.DiagSeq == _lastDiagSeq) return;
        _lastDiagSeq = gs.DiagSeq;

        if (gs.DiagExito)
        {
            LogLine("<color=#00FF7F>> Circuito validado por el Explorador. Reto completado.</color>");
            SetStatus("Circuito validado.", COk);
            return;
        }

        string txt = Reto4Feedback.Construir(gs.DiagNivel, gs.DiagPin, (Reto4Diagnostico)gs.DiagMotivo);
        if (!string.IsNullOrEmpty(txt)) LogLine(txt);
    }

    // ─────────────────────────────────────────────
    //  Toggle de modo
    // ─────────────────────────────────────────────

    public void ToggleFreeTextMode()
    {
        _freeTextMode = !_freeTextMode;

        if (_freeTextMode && codeEditor != null &&
            string.IsNullOrWhiteSpace(codeEditor.text))
        {
            codeEditor.text = ArduinoCodeParser.StarterTemplate;
        }

        ApplyModeVisibility();
        LogLine(_freeTextMode
            ? "<color=#AAFFFF>> Modo Texto Libre activado. Edita el sketch directamente.</color>"
            : "<color=#AAFFFF>> Modo Bloques activado.</color>");
    }

    void ApplyModeVisibility()
    {
        SetActiveIfNotNull(pinDropdown?.gameObject,    !_freeTextMode);
        SetActiveIfNotNull(modeDropdown?.gameObject,   !_freeTextMode);
        SetActiveIfNotNull(actionDropdown?.gameObject, !_freeTextMode);
        SetActiveIfNotNull(extraDropdown?.gameObject,  !_freeTextMode);
        SetActiveIfNotNull(blinkMsInput?.gameObject,   !_freeTextMode);
        SetActiveIfNotNull(txtCodePreview?.gameObject, !_freeTextMode);

        SetActiveIfNotNull(codeEditor?.gameObject, _freeTextMode);

        if (txtToggleLabel != null)
            txtToggleLabel.text = _freeTextMode ? "Modo Bloques" : "Codigo Libre";
    }

    static void SetActiveIfNotNull(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }

    // ─────────────────────────────────────────────
    //  Preview de código (solo Modo Bloques)
    // ─────────────────────────────────────────────
    void UpdateCodePreview()
    {
        if (txtCodePreview == null || _freeTextMode) return;

        string pin    = GetOption(pinDropdown, "13");
        string mode   = GetOption(modeDropdown, "OUTPUT");
        string action = GetOption(actionDropdown, "HIGH");
        bool   blink  = action == "BLINK" ||
                        (extraDropdown != null && GetOption(extraDropdown, "NONE") == "BLINK");

        int blinkMs = 500;
        if (blinkMsInput != null && int.TryParse(blinkMsInput.text, out int parsed)) blinkMs = parsed;

        string loopCode = blink
            ? $"  digitalWrite({pin}, HIGH);\n  delay({blinkMs});\n  digitalWrite({pin}, LOW);\n  delay({blinkMs});"
            : $"  digitalWrite({pin}, {action});";

        txtCodePreview.text =
            $"void setup() {{\n" +
            $"  pinMode({pin}, {mode});\n" +
            $"}}\n\n" +
            $"void loop() {{\n" +
            $"{loopCode}\n" +
            $"}}";
    }

    // ─────────────────────────────────────────────
    //  Compilar y enviar  (kick → corrutina)
    // ─────────────────────────────────────────────
    void CompileAndSend()
    {
        if (_isCompiling) return;
        if (!isActiveAndEnabled) return;   // no arrancar corrutina si el GO está inactivo

        // Gate: no enviar al vacío. Cubre también el atajo Ctrl+Enter (no solo el botón gris).
        if (!IsBoardReady())
        {
            SetStatus("Esperando al Explorador (VR)...", CWarn);
            LogLine("<color=#FFAA00>> Aún no hay placa conectada. Espera a que el Explorador " +
                    "cargue su escena VR antes de subir.</color>");
            return;
        }

        StartCoroutine(CompileRoutine());
    }

    // ─────────────────────────────────────────────
    //  Gating del botón "Subir"
    // ─────────────────────────────────────────────

    /// <summary>
    /// La placa está lista para recibir el sketch si:
    ///   • hay un ArduinoNetworkBridge local (escena única / IntegratedDemo), o
    ///   • el Explorador reportó su Arduino vivo por red (<see cref="GameSession.ExploradorListo"/>), o
    ///   • existe un ArduinoCore local (modo offline / escena única).
    /// En el setup asimétrico el bridge NO llega al PC del Técnico, de ahí la vía de red.
    /// </summary>
    bool IsBoardReady()
    {
        if (bridge != null) return true;
        if (GameSession.Instance != null && GameSession.Instance.ExploradorListo) return true;
        return FindAnyObjectByType<ArduinoCore>() != null;
    }

    /// <summary>Refresca el estado interactable del botón "Subir" y avisa solo al cambiar de estado.</summary>
    void RefreshBoardReady()
    {
        bool ready = IsBoardReady();

        if (btnUploadCode != null)
            btnUploadCode.interactable = ready && !_isCompiling;

        if (_lastReadyLogged != ready)
        {
            _lastReadyLogged = ready;
            if (ready)
            {
                SetStatus("Placa lista para subir.", COk);
                LogLine("<color=#00FF7F>> Placa Arduino lista. Ya puedes subir el sketch.</color>");
            }
            else
            {
                SetStatus("Subir bloqueado: falta el Arduino del Explorador.", Color.grey);
                LogLine("<color=#FFAA00>> Para SUBIR el sketch necesitas el Arduino del Explorador. " +
                        "Opciones: 1) conecta al Explorador (VR) y espera a que su placa quede lista, o " +
                        "2) corre en modo offline (ConnectionManager.modoOffline) para una placa de prueba local.</color>");
            }
        }
    }

    IEnumerator CompileRoutine()
    {
        _isCompiling = true;
        if (btnUploadCode != null) btnUploadCode.interactable = false;

        // ── Validación temprana de modo libre ───────────────────────────────
        if (_freeTextMode && (codeEditor == null || string.IsNullOrWhiteSpace(codeEditor.text)))
        {
            SetStatus("El editor está vacío.", CErr);
            LogLine("<color=#FF4444>> ERROR: Escribe un sketch antes de compilar.</color>");
            FinishCompile();
            yield break;
        }

        if (persistSketch) SaveSketch();

        // ── Fase 1: compilar ────────────────────────────────────────────────
        LogLine($"<color=#666666>------------ {DateTime.Now:HH:mm:ss} ------------</color>");
        LogLine("<color=#AAFFFF>> Compilando sketch...</color>");
        SetStatus("Compilando...", CWarn);
        if (compileDelay > 0f) yield return new WaitForSeconds(compileDelay);

        int  pinNum   = 13;
        bool isOutput = true;
        bool isHigh   = true;
        bool blink    = false;
        int  blinkMs  = 500;

        if (_freeTextMode)
        {
            // Programa libre: lo compila el INTÉRPRETE real (variables, for/while, analogWrite, …),
            // no el lector regex. Así cualquier sketch válido de Arduino compila.
            if (!ArduinoInterpreter.TryCompile(codeEditor.text, out string err))
            {
                LogLine($"<color=#FF4444>> {err}</color>");
                SetStatus("Error de compilación.", CErr);
                FinishCompile();
                yield break;
            }
            LogLine("<color=#00FF7F>> Compilado sin errores.</color>");
            // El envío usa el TEXTO COMPLETO; pinNum/blink quedan como pista para telemetría.
        }
        else
        {
            string pinStr = GetOption(pinDropdown, "13");
            string mode   = GetOption(modeDropdown, "OUTPUT");
            string action = GetOption(actionDropdown, "HIGH");
            blink = action == "BLINK" ||
                    (extraDropdown != null && GetOption(extraDropdown, "NONE") == "BLINK");

            if (!int.TryParse(pinStr, out pinNum))
            {
                string digits = Regex.Replace(pinStr, @"[^\d]", "");
                int.TryParse(digits, out pinNum);
            }

            if (blinkMsInput != null && int.TryParse(blinkMsInput.text, out int parsed)) blinkMs = parsed;
            isOutput = mode   == "OUTPUT";
            isHigh   = action == "HIGH";
            LogLine($"<color=#00FF7F>> Bloques compilados - Pin D{pinNum}.</color>");
        }

        // ── Fase 2: subir a la placa ────────────────────────────────────────
        LogLine("<color=#AAFFFF>> Subiendo a la placa...</color>");
        SetStatus("Subiendo...", CWarn);
        if (uploadDelay > 0f) yield return new WaitForSeconds(uploadDelay);

        string channel = SendToBoard(pinNum, isOutput, isHigh, blink, blinkMs);
        if (channel == null)
        {
            Debug.LogWarning("[ArduinoIDEUI] No hay GameSession, ArduinoNetworkBridge ni ArduinoCore en escena.");
            SetStatus("Sin conexión con Arduino.", CErr);
            LogLine("<color=#FF4444>> ERROR: Arduino no encontrado en escena.</color>");
            FinishCompile();
            yield break;
        }

        string suffix = channel == "offline" ? " (offline)" : "";
        SetStatus($"Código subido - Pin D{pinNum}.", COk);
        LogLine($"<color=#00FF7F>> Upload OK{suffix} - Pin D{pinNum}. No errors.</color>");

        FinishCompile();
    }

    void FinishCompile()
    {
        _isCompiling = false;
        RefreshBoardReady(); // restaura interactable según si la placa sigue lista (no a ciegas)
    }

    /// <summary>
    /// Comprueba el circuito del Reto 4 (lo arma el Explorador). Si funciona → misión cumplida
    /// (dispara el flujo de victoria: ¡FELICIDADES!); si no → mensaje para seguir construyendo.
    /// Lo llama el botón 'btnComprobarCircuito' o la tecla F5.
    /// </summary>
    public void ComprobarCircuito()
    {
        var gm = FindAnyObjectByType<GameManager>();

        // ONLINE asimétrico: el Técnico SÍ tiene un GameManager local (alimenta su dashboard),
        // pero los motores del Reto 4 (ProtoboardSimulator/ArduinoCore) viven en el Explorador.
        // Evaluar aquí daría siempre "incorrecto" contra un protoboard vacío → si hay red y no
        // hay motor local, la validación se pide POR RED aunque exista GameManager.
        bool sinMotorLocal = FindAnyObjectByType<ProtoboardSimulator>() == null;
        if (gm != null && sinMotorLocal && GameSession.Instance != null)
            gm = null;   // fuerza la rama de red de abajo

        if (gm == null)
        {
            // ONLINE (setup asimétrico de 2 escenas): el GameManager + ProtoboardSimulator viven en
            // la escena del Explorador, NO en la del Técnico. Aquí no hay GameManager local, así que
            // pedimos la validación POR RED. El Explorador la corre (GameManager.OnNetworkValidacion-
            // Solicitada → EvaluacionManualBotonFisico → EvaluarReto4) y publica el resultado vía
            // RPC_PublicarDiagnostico, que PollDiagnosticoReto4 muestra en esta misma consola.
            if (GameSession.Instance != null)
            {
                LogLine("<color=#AAFFFF>> Solicitando validación al Explorador (red)...</color>");
                SetStatus("Comprobando circuito en el Explorador...", CWarn);
                GameSession.Instance.SolicitarValidacion();
                return;
            }

            SetStatus("Verificar bloqueado: falta el Explorador.", CErr);
            LogLine("<color=#FF4444>> No se puede VERIFICAR sin el Arduino del Explorador. " +
                    "Conecta al Explorador (VR), o corre en modo offline (ConnectionManager.modoOffline) " +
                    "para probar el Técnico solo.</color>");
            return;
        }

        LogLine("<color=#AAFFFF>> Comprobando circuito del Explorador...</color>");
        bool ok = gm.EvaluacionManualBotonFisico();   // dispara CompleteLevel + ¡FELICIDADES! si pasa

        if (ok)
        {
            SetStatus("¡Circuito validado! Misión cumplida.", COk);
            LogLine("<color=#00FF7F>> ¡EL CIRCUITO FUNCIONA! Misión cumplida.</color>");
        }
        else
        {
            SetStatus("Circuito incompleto — sigan construyendo.", CWarn);
            LogLine("<color=#FFAA00>> Aún no funciona. Sigan construyendo: LED + resistencia (>=100Ω) + " +
                    "cierre a GND, y que el sketch tenga BLINK.</color>");
        }
    }

    /// <summary>
    /// Envía el sketch por la cascada de canales y devuelve cuál se usó
    /// ("red" / "bridge" / "offline"), o null si no hay ninguno.
    ///
    /// Se prioriza GameSession (objeto spawneado por el Host y replicado al Explorador) porque
    /// el ArduinoNetworkBridge es de escena y solo existe en Explorador.unity — no llega al
    /// Técnico en el setup asimétrico de 2 escenas.
    /// </summary>
    string SendToBoard(int pin, bool isOutput, bool isHigh, bool blink, int blinkMs)
    {
        // MODO TEXTO LIBRE: enviamos el SKETCH COMPLETO; el Explorador lo ejecuta con el INTÉRPRETE
        // (variables, for/while, analogWrite, varios LEDs, efectos…). El texto viaja por TROZOS
        // porque un programa real supera el límite de un RPC. En modo Bloques se usa el canal 1-pin.
        bool useText = _freeTextMode && codeEditor != null && !string.IsNullOrWhiteSpace(codeEditor.text);
        if (useText) return SendProgram(codeEditor.text);

        if (GameSession.Instance != null)
        {
            GameSession.Instance.RPC_SubirCodigoArduino(pin, isOutput, isHigh, blinkMs, blinkMs, blink);
            return "red";
        }

        var nb = bridge != null ? bridge : FindAnyObjectByType<ArduinoNetworkBridge>();
        if (nb != null)
        {
            nb.RPC_SubirCodigoArduino(pin, isOutput, isHigh, blinkMs, blinkMs, blink);
            return "bridge";
        }

        var core = FindAnyObjectByType<ArduinoCore>();
        if (core != null)
        {
            core.RecibirCodigoDePC(pin, isOutput, isHigh, blinkMs, blinkMs, blink);
            return "offline";
        }

        return null;
    }

    /// <summary>Envía el sketch completo: por red en trozos (GameSession) o directo al intérprete local.</summary>
    string SendProgram(string code)
    {
        if (GameSession.Instance != null)
        {
            const int CHUNK = 400;
            int total = Mathf.Max(1, Mathf.CeilToInt(code.Length / (float)CHUNK));
            for (int i = 0; i < total; i++)
            {
                int start = i * CHUNK;
                GameSession.Instance.RPC_SubirSketchChunk(i, total, code.Substring(start, Mathf.Min(CHUNK, code.Length - start)));
            }
            return "red";
        }

        var nb = bridge != null ? bridge : FindAnyObjectByType<ArduinoNetworkBridge>();
        if (nb != null) { ArduinoNetworkBridge.DeliverSketchProgram(code); return "bridge"; }

        var core = FindAnyObjectByType<ArduinoCore>();
        if (core != null) { ArduinoNetworkBridge.DeliverSketchProgram(code); return "offline"; }

        return null;
    }

    // ─────────────────────────────────────────────
    //  Persistencia del sketch
    // ─────────────────────────────────────────────
    void SaveSketch()
    {
        if (codeEditor == null) return;
        PlayerPrefs.SetString(PrefKey, codeEditor.text);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────
    //  Consola acumulativa (estilo IDE)
    // ─────────────────────────────────────────────
    void LogLine(string richText)
    {
        if (txtConsoleOutput == null)
        {
            Debug.Log($"[ArduinoIDEUI] {richText}");
            return;
        }

        _console.Add(richText);
        int max = Mathf.Max(1, maxConsoleLines);
        while (_console.Count > max) _console.RemoveAt(0);

        txtConsoleOutput.text = string.Join("\n", _console);
    }

    /// <summary>Limpia la consola (Ctrl+L o desde botón).</summary>
    public void ClearConsole()
    {
        _console.Clear();
        if (txtConsoleOutput != null) txtConsoleOutput.text = string.Empty;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    static string GetOption(TMP_Dropdown dd, string fallback)
    {
        if (dd == null || dd.options.Count == 0) return fallback;
        return dd.options[dd.value].text;
    }

    void SetStatus(string msg, Color c)
    {
        if (txtStatus != null)
        {
            txtStatus.text  = $"Estado: {msg}";
            txtStatus.color = c;
        }
        Debug.Log($"[ArduinoIDEUI] {msg}");
    }
}
