using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Controlador del Técnico — soporta DOS modos de entrada:
///
///   MODO PC (TechnicianMode.PC):
///     • Mouse para hacer clic en botones del Canvas
///     • Teclado para escribir valores en el InputField
///     • Canvas en Screen Space - Camera
///     • Cámara fija apuntando al panel
///
///   MODO VR ESTÁTICO (TechnicianMode.VR):
///     • Sin locomoción (el Técnico no se mueve)
///     • Ray Interactor de la mano derecha apunta al Canvas World Space
///     • Trigger derecho = clic en botones
///     • Canvas en World Space frente al Técnico
///
/// El modo se detecta AUTOMÁTICAMENTE al inicio:
///   si hay un XRController conectado → VR, sino → PC.
/// También se puede forzar desde el inspector.
/// </summary>
public class TechnicianController : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Modo de entrada")]
    public TechnicianMode mode = TechnicianMode.Auto;

    [Header("Referencias — PC")]
    [Tooltip("Cámara fija del Técnico en modo PC")]
    public Camera  pcCamera;
    [Tooltip("Canvas del Técnico (Screen Space en PC, World Space en VR)")]
    public Canvas  technicianCanvas;

    [Header("Referencias — VR estático")]
    [Tooltip("XR Origin del Técnico (sin PlayerController, sin locomoción)")]
    public GameObject xrOriginTechnician;
    [Tooltip("Mano derecha del Técnico VR para apuntar al canvas")]
    public GameObject rightHandVR;

    [Header("Posición del canvas VR (World Space)")]
    [Tooltip("Distancia del canvas al frente del técnico")]
    public float canvasDistanceVR = 1.2f;

    [Header("Debug / Override")]
    [Tooltip("Fuerza modo PC aunque haya dispositivos XR en escena (útil al probar sin headset)")]
    public bool forcePCMode = false;

    [Tooltip("En modo PC, desactiva el subsistema XR para que no interfiera con el cursor ni la cámara.")]
    public bool desactivarXRSiEsPC = true;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private TechnicianMode _activeMode;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Start()
    {
        _activeMode = DetectMode();
        ApplyMode();
        Debug.Log($"[TechnicianController] Modo activo: {_activeMode}");
    }

    void LateUpdate()
    {
        if (_activeMode != TechnicianMode.PC) return;

        // Mientras se CAMINA (ThirdPersonCamera activa) y sin pausa, el cursor lo
        // bloquea la cámara para el mouse-look; NO lo forzamos libre aquí (antes esto
        // peleaba con el mouse-look y cortaba el giro). Sentado o en pausa: cursor libre.
        if (ThirdPersonCamera.IsActive && !PauseMenu.IsPaused) return;

        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible)                          Cursor.visible   = true;
    }

    // ─────────────────────────────────────────────
    //  Detección automática de modo
    // ─────────────────────────────────────────────

    TechnicianMode DetectMode()
    {
        if (forcePCMode)          return TechnicianMode.PC;
        if (mode != TechnicianMode.Auto) return mode;

        // El Técnico es SIEMPRE PC plano en el setup asimétrico de 2 builds (el rol VR es el
        // Explorador, en otro ejecutable/escena). La auto-detección de HMD daba un falso
        // positivo con el Quest LINK activo → arrancaba en modo VR: la UI se configuraba para
        // mando VR (TrackedDeviceGraphicRaycaster) y el IDE NO recibía teclado (el campo se
        // enfocaba pero no escribía), además de que el Técnico tomaba el visor y se cerraba
        // solo (XR EXITING → ApplicationQuit). Por eso forzamos PC: SetupPC desinicializa XR.
        Debug.Log("[TechnicianController] Modo forzado a PC (rol Técnico = PC plano; se ignora HMD del Link).");
        return TechnicianMode.PC;
    }

    // ─────────────────────────────────────────────
    //  Configuración según modo
    // ─────────────────────────────────────────────

    void ApplyMode()
    {
        switch (_activeMode)
        {
            case TechnicianMode.PC:
                SetupPC();
                break;
            case TechnicianMode.VR:
                SetupVR();
                break;
        }
    }

    void SetupPC()
    {
        if (desactivarXRSiEsPC) DesinicializarXR();

        // NO activar pcCamera aquí — WorkstationSeat la activa al sentarse.
        // Activarla aquí re-habilitaría la cámara que está desactivada por defecto
        // y causaría pantalla gris o conflicto con WalkerCamera.
        if (xrOriginTechnician != null) xrOriginTechnician.SetActive(false);

        // Usar Camera.main (WalkerCamera) para setup inicial; WorkstationSeat
        // sobrescribe worldCamera al sentarse con la cámara de su puesto.
        Camera cam = Camera.main;
        if (cam == null) cam = pcCamera;

        if (cam != null)
        {
            // Para cada Canvas WorldSpace: asignar cámara y GraphicRaycaster si faltan.
            // Sin GraphicRaycaster el EventSystem no puede rutear clicks al canvas.
            // Sin worldCamera el GraphicRaycaster no puede convertir coordenadas de pantalla.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;

                if (canvas.worldCamera == null)
                    canvas.worldCamera = cam;

                if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                // Desactivar raycastTarget en fondos/decoraciones de TODOS los canvases
                // WorldSpace para que los clicks pasen al PhysicsRaycaster y lleguen a
                // los DeskComponents 3D situados detrás del canvas.
                foreach (var img in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (img.GetComponentInParent<UnityEngine.UI.Selectable>() == null)
                        img.raycastTarget = false;
                }
            }

            // El canvas principal del técnico se convierte a ScreenSpaceCamera para
            // que sea siempre visible y legible desde la cámara fija PC.
            if (technicianCanvas != null)
            {
                technicianCanvas.renderMode    = RenderMode.ScreenSpaceCamera;
                technicianCanvas.worldCamera   = cam;
                technicianCanvas.planeDistance = 1f;
            }

            // PhysicsRaycaster enruta eventos de puntero del EventSystem a objetos 3D.
            if (cam.GetComponent<PhysicsRaycaster>() == null)
                cam.gameObject.AddComponent<PhysicsRaycaster>();
        }

        // Activar EventSystem para mouse
        EnsureEventSystem();

        // Cursor visible
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        Debug.Log("[TechnicianController] Modo PC configurado.");
    }

    void SetupVR()
    {
        // Desactivar cámara PC, activar XR Origin del Técnico
        if (pcCamera           != null) pcCamera.gameObject.SetActive(false);
        if (xrOriginTechnician != null) xrOriginTechnician.SetActive(true);

        // Canvas en World Space frente al técnico
        if (technicianCanvas != null)
        {
            technicianCanvas.renderMode = RenderMode.WorldSpace;

            // Posicionar el canvas frente al XR Origin
            if (xrOriginTechnician != null)
            {
                Transform t = xrOriginTechnician.transform;
                technicianCanvas.transform.position = t.position + t.forward * canvasDistanceVR + Vector3.up * 0.3f;
                technicianCanvas.transform.rotation = Quaternion.LookRotation(
                    technicianCanvas.transform.position - t.position);
            }

            // Escala del canvas World Space (0.001 = 1mm por unidad UI)
            technicianCanvas.transform.localScale = Vector3.one * 0.001f;
        }

        // Añadir TrackedDeviceGraphicRaycaster a todos los canvases WorldSpace
        // para que el XRRayInteractor pueda interactuar con botones y campos de texto.
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (canvas.renderMode != RenderMode.WorldSpace) continue;
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        Debug.Log("[TechnicianController] Modo VR Estático configurado.");
    }

    void DesinicializarXR()
    {
        var xrSettings = XRGeneralSettings.Instance;
        if (xrSettings == null || xrSettings.Manager == null) return;
        if (!xrSettings.Manager.isInitializationComplete) return;
        if (xrSettings.Manager.activeLoader == null) return;
        // DeinitializeLoader() llama StopSubsystems internamente y luego pone
        // isInitializationComplete=false, de modo que XRManagerSettings.OnDisable()
        // encuentra el manager ya desinicializado y no intenta parar los subsistemas de nuevo.
        xrSettings.Manager.DeinitializeLoader();
        Debug.Log("[TechnicianController] XR desinicializado — modo PC.");
    }

    void EnsureEventSystem()
    {
        var es = FindAnyObjectByType<EventSystem>();

#if ENABLE_INPUT_SYSTEM
        if (es == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[TechnicianController] EventSystem creado con InputSystemUIInputModule.");
            return;
        }

        // Si ya existe un EventSystem pero tiene StandaloneInputModule, reemplazarlo.
        // StandaloneInputModule no procesa teclado con New Input System activo,
        // por eso TMP_InputField no responde al teclado.
        var standalone = es.GetComponent<StandaloneInputModule>();
        if (standalone != null)
        {
            Destroy(standalone);
            if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[TechnicianController] StandaloneInputModule reemplazado por InputSystemUIInputModule.");
        }
#else
        if (es != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
        Debug.Log("[TechnicianController] EventSystem creado con StandaloneInputModule.");
#endif
    }

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────

    public TechnicianMode GetActiveMode() => _activeMode;
    public bool IsPC() => _activeMode == TechnicianMode.PC;
    public bool IsVR() => _activeMode == TechnicianMode.VR;

    /// <summary>
    /// Forzar cambio de modo en runtime (p.ej. botón de opciones).
    /// </summary>
    public void SwitchMode(TechnicianMode newMode)
    {
        _activeMode = newMode;
        ApplyMode();
    }
}

public enum TechnicianMode { Auto, PC, VR }