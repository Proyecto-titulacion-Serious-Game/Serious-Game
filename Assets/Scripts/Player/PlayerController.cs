using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Controlador del Explorador VR (Meta Quest 3 + KAT VR).
/// VERSIÓN CORREGIDA: Incluye validaciones matemáticas estrictas contra valores NaN/Infinity
/// para prevenir el error 'Screen position out of view frustum'.
/// </summary>
public class PlayerController : MonoBehaviour
{
    //      
    //  Inspector     
    //      
    [Header("Modo de locomocion")]
    [Tooltip("True = caminadora KAT VR.  False = joystick Meta Quest (fallback).")]
    public bool useKatVR = true;
    [Tooltip("True = SOLO caminadora: nunca se usa el joystick para trasladarse. " +
             "Si la KAT no esta disponible/conectada, el jugador no se mueve (no hay fallback a mando).")]
    public bool katOnly = false;

    [Header("Velocidad (joystick VR)")]
    public float walkSpeed = 2.0f;

    [Header("Giro con el joystick derecho (CONTINUO, sensibilidad normal)")]
    [Tooltip("False = el joystick derecho NO rota la c mara.")]
    public bool enableSnapTurn = true;
    [Tooltip("Velocidad de giro en grados/segundo con el stick derecho a fondo.")]
    [Range(30f, 180f)] public float turnSpeed = 120f;
    [Tooltip("Zona muerta del eje X del stick derecho antes de empezar a girar.")]
    [Range(0.05f, 0.5f)] public float turnDeadzone = 0.15f;
    [Tooltip("Cuanto sube/baja turnSpeed con el atajo de ajuste en vivo (sin caminadora KAT): " +
             "sujeta GRIP derecho + A para subir, GRIP derecho + B para bajar.")]
    [Range(5f, 30f)] public float turnSpeedStep = 15f;
    [Tooltip("GIRO con el joystick IZQUIERDO en SNAP (pasos), en vez del giro continuo del derecho. " +
             "Pensado para la caminadora KAT: al caminar físicamente, el stick izquierdo queda libre " +
             "y sirve de 'giroscopio'. Con mando normal (sin KAT) déjalo en false: mover = izquierdo, " +
             "girar = derecho.")]
    public bool giroJoystickIzquierdo = false;
    [Tooltip("Angulo de giro por snap (grados). Solo aplica si giroJoystickIzquierdo = true.")]
    [Range(0f, 90f)] public float snapTurnAngle = 45f;
    [Tooltip("Umbral del thumbstick izquierdo para activar snap turn (modo giroJoystickIzquierdo).")]
    [Range(0.2f, 0.9f)] public float snapTurnThreshold = 0.5f;

    [Header("Rotar objeto en mano (joystick derecho)")]
    [Tooltip("Con un objeto agarrado en la mano DERECHA, el stick derecho ROTA el objeto (X = giro " +
             "horizontal, Y = inclinarlo hacia/desde la cámara) en vez de girar al jugador. Al soltar " +
             "el objeto, el stick vuelve a girar al jugador.")]
    public bool rotarObjetoEnMano = true;
    [Tooltip("Velocidad de rotación del objeto sostenido, en grados/segundo con el stick a fondo.")]
    [Range(45f, 360f)] public float velocidadRotarObjeto = 180f;

    [Header("KAT VR")]
    [Tooltip("Numero de serie del dispositivo. Dejar vacio para detectar automaticamente.")]
    public string katSerialNumber = "";
    [Tooltip("Multiplicador sobre la velocidad reportada por la caminadora. Subir si los pasos se sienten lentos.")]
    [Range(0.1f, 6f)] public float katSpeedMultiplier = 1.8f;
    [Tooltip("Correccion de giro manual en grados. Usar si para ir RECTO toca caminar en diagonal: " +
             "ajusta hasta que caminar de frente avance derecho. + gira el avance a la derecha, - a la izquierda.")]
    [Range(-90f, 90f)] public float katYawOffset = 0f;
    [Tooltip("Segundos a esperar tras iniciar la KAT antes de auto-calibrar la orientacion, " +
             "para dar tiempo a que el visor empiece a rastrear (evita la calibracion en pose invalida).")]
    [Range(0f, 5f)] public float katAutoCalibDelay = 1.0f;
    [Tooltip("Si está activo, NO se lee la caminadora KAT localmente (útil si el standalone del " +
             "Quest no la empareja bien) — en su lugar se usan los datos que retransmite el " +
             "Técnico por red (GameSession.KatRed*), leídos de una KAT conectada por USB a SU PC. " +
             "La calibración de orientación sigue siendo local (usa headCamera de este Explorador).")]
    public bool katViaRed = false;

    [Header("Referencia VR")]
    [Tooltip("XR Origin o CameraOffset de la escena VR.")]
    public Transform xrRig;
    [Tooltip("Camara principal del visor (hijo de XR Origin).")]
    public Camera headCamera;
    [Tooltip("CharacterController a mover. Vacío = se busca en este GO o en el xrRig.\n" +
             "En el setup consolidado apunta al CC del XR Origin (XR Rig).")]
    public CharacterController characterController;

    [Header("Input Actions (New Input System)")]
    [Tooltip("Accion de movimiento del joystick izquierdo (Vector2). Asignar desde InputSystem_Actions.")]
    public InputActionReference moveAction;

    [Tooltip("Atajo OPCIONAL para recentrar la orientacion de la KAT a mano. " +
             "Si se deja vacio, por defecto: boton B/Y de cualquiera de los dos controles, o tecla R (PCVR/editor).")]
    public InputActionReference recenterAction;

    [Header("Interaccion")]
    public PlayerInteraction interaction;

    //      
    //  Estado interno     
    //      
    private CharacterController _cc;
    private Vector3 _velocity;
    private bool    _isGrounded;
    private bool    _frozen;

    // KAT VR
    private float   _yawCorrection;
    private Vector3 _lastKatPosition;
    private bool    _usedSimpleMove;
    private bool    _katActive;
    private bool    _katBtnWasPressed;
    private float   _lastKatDiag;
    private double  _lastKatUpdateTime;
    private string  _resolvedSerial = "";
    private bool    _needsInitialCalib;   // calibrar al primer frame con pose de visor valida
    private float   _katInitTime;         // momento en que se inicio la KAT (para el delay de auto-calib)

    // KAT VR remota (katViaRed): últimos Seq vistos de GameSession, para detectar cambios.
    private int     _lastKatRedDataSeq;
    private int     _lastKatRedBtnSeq;

    // Snap turn
    private InputAction _snapTurnAct;
    private bool        _snapTurnHeld;

    // Rotar objeto en mano: interactores cacheados + attach original a restaurar al soltar
    private XRBaseInteractor[] _interactoresCacheados;
    private float      _proximoScanInteractores;
    private Transform  _attachRotado;          // attachTransform que estamos rotando ahora
    private Quaternion _attachRotOriginal;     // su rotación local original (se restaura al soltar)

    // Ajuste en vivo de turnSpeed (sin caminadora KAT): Grip derecho + A/B
    private InputAction _turnFasterAct;
    private InputAction _turnSlowerAct;

    // Recentrar manual (atajo)
    private InputAction _recenterFallback;
    InputAction RecenterAction => recenterAction?.action ?? _recenterFallback;

    // Fallback when moveAction reference is unassigned in Inspector
    private InputAction _moveFallback;
    InputAction MoveAction => moveAction?.action ?? _moveFallback;

    //      
    //  Unity Lifecycle     
    //      
    void Awake()
    {
        EnsureCharacterController();
        _lastKatPosition = transform.position;
        if (headCamera == null)
            headCamera = Camera.main;

        if (moveAction?.action == null)
            TryAutoFindMoveAction();
    }

    void TryAutoFindMoveAction()
    {
        foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
        {
            var act = asset.FindAction("XRI Left Locomotion/Move")
                   ?? asset.FindAction("Move");
            if (act != null)
            {
                _moveFallback = act;
                Debug.Log($"[PlayerController] moveAction auto-asignado desde '{asset.name}'.");
                return;
            }
        }
        Debug.LogWarning("[PlayerController] moveAction no encontrado autom ticamente. " +
                         "Asigna 'XRI Left Locomotion/Move' en el Inspector.");
    }

    void OnEnable()
    {
        var act = MoveAction;
        if (act != null)
        {
            act.actionMap?.Enable();
            act.Enable();
        }
        _snapTurnAct?.Enable();
        RecenterAction?.Enable();
        _turnFasterAct?.Enable();
        _turnSlowerAct?.Enable();
    }

    void OnDisable()
    {
        MoveAction?.Disable();
        _snapTurnAct?.Disable();
        RecenterAction?.Disable();
        _turnFasterAct?.Disable();
        _turnSlowerAct?.Disable();
    }

    void Start()
    {
        EnsureCharacterController(); 

#if !UNITY_EDITOR
        if (!XRSettings.isDeviceActive)
        {
            Debug.LogError("[PlayerController] No hay dispositivo XR activo. El Explorador requiere Meta Quest 3.");
            enabled = false;
            return;
        }
#endif

        if (useKatVR)
        {
            if (katViaRed) InitKatVRRemota();
            else InitKatVR();
        }

        DisableConflictingXRILocomotion();

        var moveAct = MoveAction;
        if (moveAct != null)
        {
            moveAct.actionMap?.Enable();
            moveAct.Enable();
        }
        else
        {
            Debug.LogError("[PlayerController] moveAction NO est  asignado.");
        }

        // PARCHE DE SEGURIDAD START: Forzamos calibración posicional inicial en coordenadas limpias
        _lastKatPosition = transform.position;

        DiagnosticarMovimiento();
        InitSnapTurn();
        InitRecenter();
        InitAjusteSensibilidadGiro();
    }

    /// <summary>
    /// Atajo para subir/bajar turnSpeed EN VIVO (sin necesitar el Editor/Inspector), pensado para
    /// cuando se prueba sin la caminadora KAT: sujeta GRIP derecho + A (sube) o + B (baja).
    /// Requiere el grip como modificador para no chocar con el combo F4 (A+X ambos controles,
    /// DebugLevelSkipper) ni con el recentrado (B solo, InitRecenter) — ninguno de esos usa grip.
    /// </summary>
    void InitAjusteSensibilidadGiro()
    {
        if (giroJoystickIzquierdo || !enableSnapTurn) return;

        _turnFasterAct = new InputAction("PlayerControllerGiroMasRapido", InputActionType.Button);
        _turnFasterAct.AddCompositeBinding("ButtonWithOneModifier")
            .With("Modifier", "<XRController>{RightHand}/gripButton")
            .With("Button", "<XRController>{RightHand}/primaryButton");
        _turnFasterAct.performed += _ => AjustarSensibilidadGiro(turnSpeedStep);
        _turnFasterAct.Enable();

        _turnSlowerAct = new InputAction("PlayerControllerGiroMasLento", InputActionType.Button);
        _turnSlowerAct.AddCompositeBinding("ButtonWithOneModifier")
            .With("Modifier", "<XRController>{RightHand}/gripButton")
            .With("Button", "<XRController>{RightHand}/secondaryButton");
        _turnSlowerAct.performed += _ => AjustarSensibilidadGiro(-turnSpeedStep);
        _turnSlowerAct.Enable();
    }

    void AjustarSensibilidadGiro(float delta)
    {
        turnSpeed = Mathf.Clamp(turnSpeed + delta, 30f, 180f);
        Debug.Log($"[PlayerController] Sensibilidad de giro ajustada a {turnSpeed:0}°/s " +
                   "(Grip derecho + A sube, Grip derecho + B baja).");
    }

    void InitRecenter()
    {
        // Si el usuario asignó una acción en el Inspector, basta con habilitarla.
        if (recenterAction?.action != null) { recenterAction.action.Enable(); return; }

        // Fallback en código: botón B/Y de cualquiera de los dos controles + tecla R (PCVR/editor).
        _recenterFallback = new InputAction("KAT Recenter", InputActionType.Button);
        _recenterFallback.AddBinding("<XRController>{RightHand}/secondaryButton");
        _recenterFallback.AddBinding("<XRController>{LeftHand}/secondaryButton");
        _recenterFallback.AddBinding("<Keyboard>/r");
        _recenterFallback.Enable();
    }

    void DisableConflictingXRILocomotion()
    {
        if (!useKatVR) return;

        Transform searchRoot = xrRig != null ? xrRig : transform;
        var moveProviders = searchRoot.GetComponentsInChildren<ContinuousMoveProvider>(true);
        foreach (var p in moveProviders)
        {
            if (p.enabled) p.enabled = false;
        }

        var continuousTurn = searchRoot.GetComponentsInChildren<ContinuousTurnProvider>(true);
        foreach (var p in continuousTurn)
        {
            if (p.enabled) p.enabled = false;
        }

        var snapTurn = searchRoot.GetComponentsInChildren<SnapTurnProvider>(true);
        foreach (var p in snapTurn)
        {
            if (p.enabled) p.enabled = false;
        }
    }

    [ContextMenu("Diagnosticar movimiento")]
    public void DiagnosticarMovimiento()
    {
        Debug.Log("== [PlayerController] DIAGN STICO DE MOVIMIENTO ==");
        EnsureCharacterController(); 
        Debug.Log($"  headCamera      = {(headCamera != null ? headCamera.name : "== NULL ==")}");
        Debug.Log($"  xrRig           = {(xrRig != null ? xrRig.name : "== NULL ==")}");
        Debug.Log("=================================================");
    }

    void EnsureCharacterController()
    {
        if (_cc != null) return;
        if (characterController != null) { _cc = characterController; return; }
        _cc = GetComponent<CharacterController>();
        if (_cc == null && xrRig != null)
            _cc = xrRig.GetComponent<CharacterController>() ?? xrRig.GetComponentInChildren<CharacterController>();
    }

    void Update()
    {
        EnsureCharacterController();
        if (_cc == null) return; 

        _isGrounded = _cc.isGrounded;
        _usedSimpleMove = false;
        _katActive = false;

        if (!_frozen)
        {
            if (useKatVR)
                HandleKatVRLocomotion();
            else if (!katOnly)
                HandleJoystickLocomotion();
            // katOnly y KAT no disponible → sin traslado (solo caminadora, sin joystick).

            // Gravedad SIEMPRE que no se haya usado SimpleMove (KAT), que ya la aplica
            // internamente. Sin esto, en modo joystick el rig nunca cae al suelo → flota.
            if (!_usedSimpleMove)
                ApplyGravity();

            HandleSnapTurn();
        }

        // El recentrado se atiende aun congelado (p.ej. usando el multímetro) por si quedó torcido.
        if (useKatVR && RecenterAction != null && RecenterAction.WasPressedThisFrame())
            RecenterOrientation();
    }

    void LateUpdate()
    {
        if (!_katActive || xrRig == null) return;

        Vector3 offset = transform.position - _lastKatPosition;
        offset.y = 0f;

        // ─── PARCHE CRÍTICO: VALIDACIÓN DE LIMBO DE FLOTANTES (ANTI-NAN) ───
        if (float.IsNaN(offset.x) || float.IsNaN(offset.z) || float.IsInfinity(offset.x) || float.IsInfinity(offset.z))
        {
            _lastKatPosition = transform.position;
            return;
        }

        // Si el cálculo genera un salto anormal (un pico infinito de teletransporte en el frame 1)
        if (offset.sqrMagnitude > 50f)
        {
            _lastKatPosition = transform.position;
            return;
        }

        xrRig.position += offset;
        _lastKatPosition = transform.position;
    }

    void InitKatVR()
    {
        try
        {
            int deviceCount = KATNativeSDK.DeviceCount();
            Debug.Log($"[PlayerController/KAT] DeviceCount = {deviceCount}");
            if (deviceCount == 0)
            {
                Debug.LogWarning("[PlayerController/KAT] No se detectó ninguna caminadora KAT. " +
                                 "Verifica KAT Gateway abierto y la caminadora conectada/encendida. " +
                                 (katOnly ? "→ Modo SOLO caminadora: se reintenta cada frame (sin joystick)."
                                          : "→ Fallback a joystick."));
                if (!katOnly) useKatVR = false;
                return;
            }

            // Resolver el SERIAL real del dispositivo. Llamar GetWalkStatus("") suele devolver una
            // estructura congelada (lastUpdateTimePoint no avanza) → hay que pasar el serial real.
            ResolveKatSerial(deviceCount);

            // Enganchar el streaming de datos en vivo del dispositivo.
            try { KATNativeSDK.ForceConnect(_resolvedSerial); Debug.Log($"[PlayerController/KAT] ForceConnect('{_resolvedSerial}')"); }
            catch (System.Exception e) { Debug.LogWarning("[PlayerController/KAT] ForceConnect falló: " + e.Message); }

            KATNativeSDK.TreadMillData data = KATNativeSDK.GetWalkStatus(KatSerial());
            Debug.Log($"[PlayerController/KAT] GetWalkStatus(sn='{KatSerial()}') → connected={data.connected}, device='{data.deviceName}', updT={data.lastUpdateTimePoint:0.000}");
            if (!data.connected)
            {
                Debug.LogWarning("[PlayerController/KAT] La caminadora aparece pero NO está 'connected'. " +
                                 (katOnly ? "→ Modo SOLO caminadora: se reintenta cada frame (sin joystick)."
                                          : "→ Fallback a joystick."));
                if (!katOnly) useKatVR = false;
                return;
            }

            // NO calibrar aquí: en Start el visor casi nunca tiene una pose válida todavía, así que
            // _yawCorrection quedaría en 0 y caminar de frente avanzaría en diagonal. Diferimos la
            // calibración hasta que el HMD esté rastreando (ver HandleKatVRLocomotion).
            _needsInitialCalib = true;
            _katInitTime = Time.unscaledTime;
            Debug.Log("[PlayerController/KAT] ✓ Caminadora KAT inicializada. Auto-calibración de orientación pendiente (esperando pose del visor).");
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError("[PlayerController/KAT] No se cargó KATSDKWarpper.dll: " + e.Message +
                           (katOnly ? "\n→ Modo SOLO caminadora: sin locomoción (revisa la DLL de KAT)."
                                    : "\n→ Fallback a joystick."));
            useKatVR = false;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[PlayerController/KAT] Error inicializando KAT: " + e.Message +
                           (katOnly ? "\n→ Modo SOLO caminadora: sin locomoción." : "\n→ Fallback a joystick."));
            useKatVR = false;
        }
    }

    /// <summary>Modo katViaRed: no hay SDK local que inicializar — solo se arma el delay de
    /// auto-calibración, igual que InitKatVR, y se espera a que lleguen datos por GameSession.</summary>
    void InitKatVRRemota()
    {
        _needsInitialCalib = true;
        _katInitTime = Time.unscaledTime;
        Debug.Log("[PlayerController/KAT] Modo REMOTO activado — esperando datos de la caminadora vía Técnico (GameSession.KatRed*).");
    }

    /// <summary>Serial a usar en GetWalkStatus: el del Inspector si se puso, si no el auto-resuelto.</summary>
    string KatSerial() => string.IsNullOrEmpty(katSerialNumber) ? _resolvedSerial : katSerialNumber;

    /// <summary>
    /// Resuelve el número de serie real de la caminadora (deviceType==1) recorriendo los
    /// dispositivos del SDK. GetWalkStatus necesita el serial real para entregar datos en vivo;
    /// con "" suele devolver una estructura congelada.
    /// </summary>
    void ResolveKatSerial(int deviceCount)
    {
        if (!string.IsNullOrEmpty(katSerialNumber)) { _resolvedSerial = katSerialNumber; return; }

        for (uint i = 0; i < deviceCount; i++)
        {
            try
            {
                var desc = KATNativeSDK.GetDevicesDesc(i);
                Debug.Log($"[PlayerController/KAT] device[{i}]: name='{desc.device}' sn='{desc.serialNumber}' " +
                          $"type={desc.deviceType} (1=caminadora, 2=tracker)");
                if (desc.deviceType == 1 && !string.IsNullOrEmpty(desc.serialNumber))
                {
                    _resolvedSerial = desc.serialNumber;
                    Debug.Log($"[PlayerController/KAT] Serial de caminadora resuelto: '{_resolvedSerial}'");
                    return;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PlayerController/KAT] GetDevicesDesc({i}) falló: {e.Message}");
            }
        }

        // Fallback: serial del primer dispositivo.
        try { _resolvedSerial = KATNativeSDK.GetDevicesDesc(0).serialNumber; } catch { }
        Debug.Log($"[PlayerController/KAT] Serial fallback: '{_resolvedSerial}'");
    }

    void HandleKatVRLocomotion()
    {
        if (katViaRed) { HandleKatVRLocomotionRemota(); return; }

        EnsureCharacterController();
        if (_cc == null) return;

        KATNativeSDK.TreadMillData data = KATNativeSDK.GetWalkStatus(KatSerial());

        // SOLO se exige 'connected'. NO exigir deviceDatas: ese array de structs se marshala por
        // P/Invoke (ByValArray) y con frecuencia vuelve NULL aunque la caminadora esté conectada;
        // si lo exigíamos, la KAT siempre caía a joystick y nunca movía.
        if (!data.connected)
        {
            if (katOnly)
                DiagKat("NO conectada (connected=false) → sin movimiento (modo SOLO caminadora). ¿KAT Gateway abierto y caminadora encendida?");
            else
            {
                DiagKat("NO conectada (connected=false) → joystick. ¿KAT Gateway abierto y caminadora encendida?");
                HandleJoystickLocomotion();
            }
            return;
        }

        // Botón de calibración: solo si el SDK reportó deviceDatas. Calibrar en el FLANCO de
        // pulsación (no mientras se mantiene), para que un botón "siempre presionado" no bloquee.
        bool btn = false;
        if (data.deviceDatas != null && data.deviceDatas.Length > 0)
        {
            btn = data.deviceDatas[0].btnPressed;
            if (btn && !_katBtnWasPressed)
            {
                _katBtnWasPressed = true;
                CalibrateOrientation(data);
                Debug.Log("[PlayerController/KAT] Recentrado (botón de calibración).");
                return;
            }
            if (!btn) _katBtnWasPressed = false;
        }

        // ¿El SDK está entregando datos VIVOS? (se calcula antes para usarlo en la auto-calibración)
        bool datosFrescosTmp = data.lastUpdateTimePoint != _lastKatUpdateTime;

        // Auto-calibración diferida: en cuanto el visor tenga una pose válida y haya pasado el delay,
        // calibramos UNA vez. Esto corrige la diagonal de "para ir recto hay que caminar de lado",
        // que ocurría porque la calibración de Start se hacía con el visor aún sin rastrear.
        if (_needsInitialCalib && datosFrescosTmp
            && Time.unscaledTime - _katInitTime >= katAutoCalibDelay
            && HeadPoseValid())
        {
            CalibrateOrientation(data);
            _needsInitialCalib = false;
            Debug.Log("[PlayerController/KAT] Auto-calibración de orientación aplicada (visor ya rastreando). " +
                      "Mira al frente y, si hace falta, pulsa el botón de la KAT para recentrar.");
        }

        Quaternion bodyRot = data.bodyRotationRaw
                           * Quaternion.Inverse(Quaternion.Euler(0f, _yawCorrection, 0f))
                           * Quaternion.Euler(0f, katYawOffset, 0f);

        Vector3 moveVelocity = bodyRot * data.moveSpeed * katSpeedMultiplier;

        // PARCHE DE SEGURIDAD: Evitar velocidades de caminadora corruptas al inicio
        if (float.IsNaN(moveVelocity.x) || float.IsNaN(moveVelocity.z)) moveVelocity = Vector3.zero;

        // ¿El SDK está entregando datos VIVOS? Si lastUpdateTimePoint avanza pero moveSpeed=0,
        // el dispositivo reporta bien pero NO detecta pasos → calibración/sensores de la KAT.
        // Si lastUpdateTimePoint NO avanza, los datos están congelados/mal marshalados.
        bool datosFrescos = datosFrescosTmp;
        _lastKatUpdateTime = data.lastUpdateTimePoint;

        DiagKat($"moveSpeed raw=({data.moveSpeed.x:0.000}, {data.moveSpeed.y:0.000}, {data.moveSpeed.z:0.000}) " +
                $"|{data.moveSpeed.magnitude:0.00}|  body={data.bodyRotationRaw.eulerAngles}  " +
                $"updT={data.lastUpdateTimePoint:0.000} fresco={datosFrescos}  btn={btn}  cc={(_cc != null ? _cc.name : "NULL")}");

        _cc.SimpleMove(moveVelocity);
        _usedSimpleMove = true;
        _katActive = true;
    }

    /// <summary>
    /// Igual que HandleKatVRLocomotion pero leyendo GameSession.KatRed* (publicado por
    /// TecnicoKatBridge desde la PC del Técnico) en vez de KATNativeSDK local. La calibración de
    /// orientación es la MISMA lógica (usa el headCamera de ESTE Explorador) — solo cambia de
    /// dónde sale el dato crudo de velocidad/rotación de la caminadora.
    /// </summary>
    void HandleKatVRLocomotionRemota()
    {
        EnsureCharacterController();
        if (_cc == null) return;

        var gs = GameSession.Instance;
        if (gs == null || gs.Object == null || !gs.Object.IsValid)
        {
            DiagKat("KAT remota: sin GameSession en red todavía.");
            return;
        }

        if (!gs.KatRedConectada)
        {
            if (katOnly)
                DiagKat("KAT remota: el Técnico reporta caminadora NO conectada → sin movimiento (modo SOLO caminadora).");
            else
            {
                DiagKat("KAT remota: el Técnico reporta caminadora NO conectada → joystick.");
                HandleJoystickLocomotion();
            }
            return;
        }

        // Botón de calibración: cada incremento de KatRedBtnSeq es un flanco de pulsación
        // detectado ya en la PC del Técnico (TecnicoKatBridge).
        if (gs.KatRedBtnSeq != _lastKatRedBtnSeq)
        {
            _lastKatRedBtnSeq = gs.KatRedBtnSeq;
            CalibrateOrientationRaw(gs.KatRedBodyRotation);
            Debug.Log("[PlayerController/KAT] Recentrado (botón de calibración, remoto vía Técnico).");
            return;
        }

        bool datosFrescosTmp = gs.KatRedDataSeq != _lastKatRedDataSeq;
        _lastKatRedDataSeq = gs.KatRedDataSeq;

        if (_needsInitialCalib && datosFrescosTmp
            && Time.unscaledTime - _katInitTime >= katAutoCalibDelay
            && HeadPoseValid())
        {
            CalibrateOrientationRaw(gs.KatRedBodyRotation);
            _needsInitialCalib = false;
            Debug.Log("[PlayerController/KAT] Auto-calibración (remota) aplicada. Mira al frente y, " +
                      "si hace falta, pulsa el botón de la KAT para recentrar.");
        }

        Quaternion bodyRot = gs.KatRedBodyRotation
                           * Quaternion.Inverse(Quaternion.Euler(0f, _yawCorrection, 0f))
                           * Quaternion.Euler(0f, katYawOffset, 0f);

        Vector3 moveVelocity = bodyRot * gs.KatRedMoveSpeed * katSpeedMultiplier;
        if (float.IsNaN(moveVelocity.x) || float.IsNaN(moveVelocity.z)) moveVelocity = Vector3.zero;

        DiagKat($"[remota] moveSpeed raw=({gs.KatRedMoveSpeed.x:0.000}, {gs.KatRedMoveSpeed.y:0.000}, {gs.KatRedMoveSpeed.z:0.000}) " +
                $"|{gs.KatRedMoveSpeed.magnitude:0.00}|  body={gs.KatRedBodyRotation.eulerAngles}  fresco={datosFrescosTmp}");

        _cc.SimpleMove(moveVelocity);
        _usedSimpleMove = true;
        _katActive = true;
    }

    // Diagnóstico KAT throttleado (~1 vez por segundo) para no inundar la consola.
    void DiagKat(string msg)
    {
        if (Time.unscaledTime - _lastKatDiag < 1f) return;
        _lastKatDiag = Time.unscaledTime;
        Debug.Log("[PlayerController/KAT] " + msg);
    }

    /// <summary>
    /// Recentra a mano la orientación de la KAT: tras esto, caminar de frente avanza hacia donde
    /// mira el visor AHORA. Llamar mirando al frente. Atajo por defecto: botón B/Y del mando o tecla R.
    /// También invocable desde el menú contextual del componente o desde un botón de UI.
    /// </summary>
    [ContextMenu("Recentrar orientación KAT")]
    public void RecenterOrientation()
    {
        if (!useKatVR) return;

        if (katViaRed)
        {
            var gs = GameSession.Instance;
            if (gs == null || gs.Object == null || !gs.Object.IsValid || !gs.KatRedConectada)
            {
                Debug.LogWarning("[PlayerController/KAT] Recentrar manual (remoto) ignorado: sin datos de la caminadora vía Técnico.");
                return;
            }
            CalibrateOrientationRaw(gs.KatRedBodyRotation);
            _needsInitialCalib = false;
            Debug.Log("[PlayerController/KAT] Recentrado MANUAL (remoto) aplicado.");
            return;
        }

        try
        {
            var data = KATNativeSDK.GetWalkStatus(KatSerial());
            if (!data.connected)
            {
                Debug.LogWarning("[PlayerController/KAT] Recentrar manual ignorado: caminadora no conectada.");
                return;
            }
            CalibrateOrientation(data);
            _needsInitialCalib = false;
            Debug.Log("[PlayerController/KAT] Recentrado MANUAL aplicado (se tomó la dirección del visor al pulsar).");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PlayerController/KAT] Recentrar manual falló: " + e.Message);
        }
    }

    /// <summary>True si la cámara del visor ya entrega una pose razonable (no NaN y no pegada al origen).</summary>
    bool HeadPoseValid()
    {
        if (headCamera == null) return false;
        Vector3 p = headCamera.transform.position;
        if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) return false;
        // En el frame 0 la cámara suele estar en el origen exacto hasta que el HMD empieza a rastrear.
        return p.sqrMagnitude > 0.0001f;
    }

    void CalibrateOrientation(KATNativeSDK.TreadMillData data) => CalibrateOrientationRaw(data.bodyRotationRaw);

    /// <summary>Núcleo de la calibración, compartido entre KAT local (CalibrateOrientation) y KAT
    /// remota vía Técnico (HandleKatVRLocomotionRemota) — solo necesita la rotación cruda del
    /// cuerpo que reporta la caminadora, sea local o recibida por red.</summary>
    void CalibrateOrientationRaw(Quaternion bodyRotationRaw)
    {
        if (headCamera == null) return;

        // Evitar que la calibración reciba transformadas nulas de la cámara en el frame 0
        if (float.IsNaN(headCamera.transform.position.x) || headCamera.transform.position.sqrMagnitude < 0.001f) return;

        _yawCorrection = bodyRotationRaw.eulerAngles.y - headCamera.transform.eulerAngles.y;

        Vector3 pos  = transform.position;
        pos.x        = headCamera.transform.position.x;
        pos.z        = headCamera.transform.position.z;

        if (!float.IsNaN(pos.x) && !float.IsNaN(pos.z))
        {
            transform.position = pos;
        }
        _lastKatPosition = transform.position;
    }

    void HandleJoystickLocomotion()
    {
        EnsureCharacterController();
        if (_cc == null) return;

        if (headCamera == null)
        {
            headCamera = Camera.main;
            if (headCamera == null) return;
        }

        Vector2 stick = MoveAction != null ? MoveAction.ReadValue<Vector2>() : Vector2.zero;

        // Cambiamos la validación para que detecte movimiento en CUALQUIER dirección (X o Y)
        if (stick.sqrMagnitude < 0.001f) return;

        // Calculamos hacia dónde es "adelante" y hacia dónde es "derecha" según a dónde mire el jugador
        Vector3 forward = Vector3.ProjectOnPlane(headCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(headCamera.transform.right, Vector3.up).normalized;

        // Condición de seguridad: Si 'giroJoystickIzquierdo' está activo, anulamos el movimiento lateral 
        // para que el jugador no camine de lado mientras intenta rotar la cámara.
        float inputX = giroJoystickIzquierdo ? 0f : stick.x;
        float inputY = stick.y;

        // Sumamos ambos vectores para permitir movimiento en diagonal, adelante, atrás y a los lados
        Vector3 moveDir = (forward * inputY) + (right * inputX);

        if (!float.IsNaN(moveDir.x) && !float.IsNaN(moveDir.z))
        {
            _cc.Move(moveDir * walkSpeed * Time.deltaTime);
        }
    }

    void InitSnapTurn()
    {
        // La acción del stick DERECHO se crea SIEMPRE que algo la use: el giro continuo
        // (modo mando: enableSnapTurn && !giroJoystickIzquierdo) o la rotación de objeto
        // en mano (rotarObjetoEnMano, en cualquier modo — también con la KAT).
        bool laUsaGiro  = !giroJoystickIzquierdo && enableSnapTurn && xrRig != null;
        if (!laUsaGiro && !rotarObjetoEnMano) return;

        // Acción PROPIA (no la del asset XRI compartido): "Turn"/"Snap Turn" del asset por
        // defecto usan interacciones Sector pensadas para snap-turn discreto, y viven en el mismo
        // asset/mapa que los ContinuousTurnProvider/SnapTurnProvider que DisableConflictingXRILocomotion()
        // desactiva más arriba en Start() — depender de ese estado compartido resultó poco fiable
        // (bug reportado: "con el joystick tampoco me deja mover la camara"). Con una acción propia,
        // igual que _recenterFallback más abajo, el giro no depende de nada más en la escena.
        _snapTurnAct = new InputAction("PlayerControllerGiroDerecho", InputActionType.Value,
            expectedControlType: "Vector2");
        _snapTurnAct.AddBinding("<XRController>{RightHand}/{Primary2DAxis}").WithProcessor("StickDeadzone");
        _snapTurnAct.Enable();
    }

    void HandleSnapTurn()
    {
        if (xrRig == null) return;

        // Objeto sostenido con la MANO DERECHA: el stick derecho rota el OBJETO (girarlo, ponerlo
        // de frente a la cámara) y queda CONSUMIDO — el jugador no gira mientras sostiene algo.
        bool stickDerechoConsumido = RotarObjetoSostenido();

        // Modo KAT: giro en SNAP con el stick IZQUIERDO (queda libre entero al caminar en la
        // caminadora). Mando normal (sin KAT): giro CONTINUO con el stick DERECHO, ver abajo.
        if (giroJoystickIzquierdo)
        {
            HandleSnapTurnLeftStick();
            return;
        }

        if (stickDerechoConsumido) return;

        if (!enableSnapTurn || _snapTurnAct == null) return;

        Vector2 val = _snapTurnAct.ReadValue<Vector2>();
        if (Mathf.Abs(val.x) < turnDeadzone) return;

        Vector3 pivotContinuo = headCamera
            ? new Vector3(headCamera.transform.position.x, xrRig.position.y, headCamera.transform.position.z)
            : xrRig.position;
        if (float.IsNaN(pivotContinuo.x) || float.IsNaN(pivotContinuo.z)) return;

        float angulo = val.x * turnSpeed * Time.deltaTime;
        xrRig.RotateAround(pivotContinuo, Vector3.up, angulo);
    }

    /// <summary>Giro histórico en pasos (snap), con el eje X del stick IZQUIERDO — pensado para
    /// cuando useKatVR está activo y el stick queda libre mientras se camina en la caminadora.</summary>
    void HandleSnapTurnLeftStick()
    {
        if (snapTurnAngle <= 0f) return;

        var moveAct = MoveAction;
        if (moveAct == null) return;
        Vector2 val = moveAct.ReadValue<Vector2>();

        if (Mathf.Abs(val.x) > snapTurnThreshold)
        {
            if (!_snapTurnHeld)
            {
                Vector3 pivot = headCamera
                    ? new Vector3(headCamera.transform.position.x, xrRig.position.y, headCamera.transform.position.z)
                    : xrRig.position;

                if (!float.IsNaN(pivot.x) && !float.IsNaN(pivot.z))
                {
                    xrRig.RotateAround(pivot, Vector3.up, Mathf.Sign(val.x) * snapTurnAngle);
                }
                _snapTurnHeld = true;
            }
        }
        else
        {
            _snapTurnHeld = false;
        }
    }

    // ─────────────────────────────────────────────
    //  Rotar objeto sostenido con el stick derecho
    // ─────────────────────────────────────────────

    /// <summary>
    /// Si la mano DERECHA sostiene un XRGrabInteractable: el stick derecho ROTA el objeto
    /// (X = giro horizontal/yaw, Y = inclinarlo hacia/desde la cámara) y devuelve true — el
    /// stick queda consumido y el jugador NO gira. Rota el attachTransform del interactor, así
    /// XRI (grab kinemático con Track Rotation) arrastra el objeto suavemente hacia esa pose.
    /// Al soltar, el attach se restaura para que el próximo agarre no herede la rotación.
    /// </summary>
    bool RotarObjetoSostenido()
    {
        if (!rotarObjetoEnMano || _snapTurnAct == null) { RestaurarAttachRotado(); return false; }

        var interactor = InteractorDerechoConObjeto();
        if (interactor == null) { RestaurarAttachRotado(); return false; }

        XRGrabInteractable grab = null;
        foreach (var sel in interactor.interactablesSelected)
            if (sel is XRGrabInteractable g) { grab = g; break; }
        if (grab == null) { RestaurarAttachRotado(); return false; }

        Transform attach = interactor.GetAttachTransform(grab);
        if (attach == null) return true;   // sostiene algo: consumir el stick igual

        if (_attachRotado != attach)
        {
            RestaurarAttachRotado();       // veníamos rotando otro attach → restaurarlo primero
            _attachRotado      = attach;
            _attachRotOriginal = attach.localRotation;
        }

        Vector2 stick = _snapTurnAct.ReadValue<Vector2>();
        if (stick.sqrMagnitude < turnDeadzone * turnDeadzone) return true;   // quieto, pero consumido

        // Eje de "inclinar hacia la cámara": la derecha de la cámara proyectada al plano horizontal.
        Vector3 ejePitch = headCamera != null
            ? Vector3.ProjectOnPlane(headCamera.transform.right, Vector3.up).normalized
            : Vector3.right;
        if (ejePitch.sqrMagnitude < 0.5f) ejePitch = Vector3.right;   // cámara mirando al techo/piso

        float paso = velocidadRotarObjeto * Time.deltaTime;
        Quaternion delta = Quaternion.AngleAxis(stick.x * paso, Vector3.up)
                         * Quaternion.AngleAxis(-stick.y * paso, ejePitch);
        attach.rotation = delta * attach.rotation;
        return true;
    }

    /// <summary>Interactor de la MANO DERECHA que sostenga un XRGrabInteractable, o null.</summary>
    XRBaseInteractor InteractorDerechoConObjeto()
    {
        // Rescan barato cada 2 s: los interactores se crean/consolidan en runtime (RuntimeHandConsolidator).
        if (_interactoresCacheados == null || Time.unscaledTime >= _proximoScanInteractores)
        {
            _interactoresCacheados   = FindObjectsByType<XRBaseInteractor>(FindObjectsSortMode.None);
            _proximoScanInteractores = Time.unscaledTime + 2f;
        }

        foreach (var it in _interactoresCacheados)
        {
            if (it == null || !it.isActiveAndEnabled || !it.hasSelection) continue;
            if (it is XRSocketInteractor) continue;   // sockets/slots no son la mano
            if (!EsManoDerecha(it)) continue;
            foreach (var sel in it.interactablesSelected)
                if (sel is XRGrabInteractable) return it;
        }
        return null;
    }

    static bool EsManoDerecha(XRBaseInteractor it)
    {
        if (it.handedness == InteractorHandedness.Right) return true;
        if (it.handedness == InteractorHandedness.Left)  return false;
        // handedness sin configurar → inferir del nombre de la jerarquía (RightHand / Right Controller)
        for (var t = it.transform; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("right") || n.Contains("derech"))   return true;
            if (n.Contains("left")  || n.Contains("izquierd")) return false;
        }
        return false;
    }

    void RestaurarAttachRotado()
    {
        if (_attachRotado == null) return;
        _attachRotado.localRotation = _attachRotOriginal;
        _attachRotado = null;
    }

    void ApplyGravity()
    {
        EnsureCharacterController();
        if (_cc == null) return;

        if (_isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;   
        else
            _velocity.y += Physics.gravity.y * Time.deltaTime;

        if (!float.IsNaN(_velocity.y))
        {
            _cc.Move(_velocity * Time.deltaTime);
        }
    }

    public void FreezeMovement(bool freeze)
    {
        _frozen = freeze;
    }
}