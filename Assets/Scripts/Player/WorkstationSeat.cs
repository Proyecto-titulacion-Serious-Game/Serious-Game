using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Trigger de silla. Al entrar el Walker:
///   - Desactiva ThirdPersonCamera (libera cursor via OnDisable)
///   - Activa el GO de la seatCamera asignada (Pc_Camera o Recep_Camera)
///   - Asigna seatCamera como worldCamera en todos los canvases WorldSpace
///   - Garantiza GraphicRaycaster en cada canvas WorldSpace
///   - Garantiza InputSystemUIInputModule en el EventSystem
///
/// Pulsar E → restaura movimiento y cámara de primera persona.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Collider))]
public class WorkstationSeat : MonoBehaviour
{
    [Header("Tecla para levantarse")]
    public KeyCode exitKey = KeyCode.E;

    [Header("Reingreso a la silla")]
    [Tooltip("Segundos que la silla queda NO interactiva tras levantarte (E). Durante este tiempo no " +
             "puedes volver a sentarte; al terminar, vuelve a ser interactiva.")]
    public float reentryCooldown = 10f;

    [Header("Cámara del puesto")]
    [Tooltip("Cámara que se activa al sentarse. Se auto-busca por nombre si queda vacío.")]
    public Camera seatCamera;

    [Header("Nombre de la cámara (si no está asignada arriba)")]
    [Tooltip("Nombre exacto del GO (sin importar mayúsculas). Ej: 'PC_Camera' para la silla técnico.")]
    public string seatCameraName = "PC_Camera";

    [Header("Avatar del técnico")]
    [Tooltip("GameObject del robot. Se busca automáticamente si queda vacío.")]
    public GameObject technicianRobot;

    private TechnicianMover   _mover;
    private ThirdPersonCamera _tpc;
    private bool              _seated;
    private float             _cooldownUntil;   // Time.time hasta el cual la silla NO acepta sentarse

    void Start()
    {
        GetComponent<Collider>().isTrigger = true;

        _tpc = FindAnyObjectByType<ThirdPersonCamera>();

        if (technicianRobot == null)
        {
            var go = GameObject.Find("TechnicianRobot");
            if (go != null) technicianRobot = go;
        }

        if (seatCamera == null && !string.IsNullOrEmpty(seatCameraName))
            seatCamera = FindCameraByNameFuzzy(seatCameraName);

        if (seatCamera != null && seatCamera.GetComponent<PhysicsRaycaster>() == null)
            seatCamera.gameObject.AddComponent<PhysicsRaycaster>();

        // MODIFICADO: Ahora desactiva el GameObject completo al inicio
        DisableAllSeatCameras();

        Camera activeCam = Camera.main;                                    
        if (activeCam == null)
            activeCam = FindAnyObjectByType<Camera>();                     
        if (activeCam == null && seatCamera != null)
        {
            // MODIFICADO: Activa el GameObject completo para pruebas standalone
            seatCamera.gameObject.SetActive(true);
            activeCam = seatCamera;
        }
        SetCanvasCamera(activeCam);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_seated) return;

        // Cooldown tras levantarte: la silla no es interactiva durante 'reentryCooldown' segundos.
        if (Time.time < _cooldownUntil) return;

        _mover = other.GetComponent<TechnicianMover>() ?? other.GetComponentInParent<TechnicianMover>();
        if (_mover == null) return;

        if (_tpc == null) _tpc = FindAnyObjectByType<ThirdPersonCamera>();

        _mover.LockPosition(true);
        _mover.enabled = false;
        if (technicianRobot != null) technicianRobot.SetActive(false);

        if (_tpc != null)
            _tpc.enabled = false;   
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        if (seatCamera == null && !string.IsNullOrEmpty(seatCameraName))
            seatCamera = FindCameraByNameFuzzy(seatCameraName);

        if (seatCamera != null)
        {
            // MODIFICADO: Primero apagamos las otras y luego encendemos el GO de esta
            DisableOtherSeatCameras(seatCamera);
            seatCamera.gameObject.SetActive(true); 
            SetCanvasCamera(seatCamera);
            Debug.Log($"[WorkstationSeat] GameObject de cámara activado: {seatCamera.gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"[WorkstationSeat] seatCamera es null — verifica seatCameraName='{seatCameraName}' en el Inspector.");
        }

        EnsureGraphicRaycasters();
        EnsureInputSystemModule();
        _seated = true;

        StartCoroutine(ReapplyUISetup());
    }

    void Update()
    {
        if (!_seated) return;
        if (PauseMenu.IsPaused) return;
        if (IsAnyInputFieldFocused()) return;

        var kb = Keyboard.current;
        bool pressed = kb != null && (exitKey switch
        {
            KeyCode.E      => kb.eKey.wasPressedThisFrame,
            KeyCode.Escape => kb.escapeKey.wasPressedThisFrame,
            KeyCode.Space  => kb.spaceKey.wasPressedThisFrame,
            KeyCode.Return => kb.enterKey.wasPressedThisFrame,
            _              => kb.eKey.wasPressedThisFrame
        });

        if (pressed) Rise();
    }

    // Cachea el GetComponent<TMP_InputField> de la selección del EventSystem — antes se repetía
    // cada Update() mientras el jugador está sentado (casi toda la sesión del Técnico); ahora
    // solo se recalcula cuando la selección realmente cambia. Estático porque la selección del
    // EventSystem es un estado global de la escena, no por instancia de WorkstationSeat.
    private static GameObject _lastSelChecked;
    private static bool       _lastSelHasInputField;

    static bool IsAnyInputFieldFocused()
    {
        var sel = EventSystem.current?.currentSelectedGameObject;
        if (sel != _lastSelChecked)
        {
            _lastSelChecked       = sel;
            _lastSelHasInputField = sel != null && sel.GetComponent<TMP_InputField>() != null;
        }
        return _lastSelHasInputField;
    }

    public void Rise()
    {
        if (_mover == null) return;

        if (seatCamera != null)
        {
            // MODIFICADO: Desactiva el GameObject al levantarse
            seatCamera.gameObject.SetActive(false);
            SetCanvasCamera(null);
        }

        if (_tpc != null)
            _tpc.enabled = true;   
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        _mover.LockPosition(false);
        _mover.enabled = true;
        if (technicianRobot != null) technicianRobot.SetActive(true);

        _seated = false;
        _mover  = null;

        // Arrancar el cooldown: la silla queda NO interactiva por 'reentryCooldown' s.
        _cooldownUntil = Time.time + reentryCooldown;
        Debug.Log($"[WorkstationSeat] Te levantaste. Silla no interactiva por {reentryCooldown:0}s.");
    }

    System.Collections.IEnumerator ReapplyUISetup()
    {
        yield return null;   
        if (!_seated) yield break;
        if (seatCamera != null)
        {
            // MODIFICADO: Asegura el GO activo
            seatCamera.gameObject.SetActive(true);
            SetCanvasCamera(seatCamera);
        }
        EnsureGraphicRaycasters();
        EnsureInputSystemModule();
    }

    // ─── Helpers estáticos modificados ────────────────────────────────────────

    static Camera FindCameraByNameFuzzy(string name)
    {
        string[] candidates =
        {
            name,
            name.Replace("Camara", "Camera").Replace("camara", "camera"),
            name.Replace("Camera", "Camara").Replace("camera", "camara"),
        };

        // NOTA: Nos saltamos GameObject.Find porque los GOs van a estar inactivos (SetActive(false))
        // Vamos directo al fallback que busca en TODO (activos e inactivos) por nombre de forma insensible a mayúsculas.
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            foreach (var n in candidates)
                if (string.Equals(cam.gameObject.name, n, System.StringComparison.OrdinalIgnoreCase))
                    return cam;
        }
        return null;
    }

    /// <summary>Apaga el GAMEOBJECT de todas las seat cameras al inicio.</summary>
    static void DisableAllSeatCameras()
    {
        foreach (var seat in FindObjectsByType<WorkstationSeat>(FindObjectsInactive.Include))
            if (seat.seatCamera != null)
                seat.seatCamera.gameObject.SetActive(false); // MODIFICADO
    }

    /// <summary>Apaga el GAMEOBJECT de las otras WorkstationSeat.</summary>
    static void DisableOtherSeatCameras(Camera keep)
    {
        foreach (var seat in FindObjectsByType<WorkstationSeat>(FindObjectsInactive.Include))
        {
            if (seat.seatCamera != null && seat.seatCamera != keep)
                seat.seatCamera.gameObject.SetActive(false); // MODIFICADO
        }
    }

    static void SetCanvasCamera(Camera cam)
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            if (canvas.renderMode == RenderMode.WorldSpace)
                canvas.worldCamera = cam;
    }

    static void EnsureGraphicRaycasters()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (canvas.renderMode != RenderMode.WorldSpace) continue;

            var tdgr = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (tdgr != null) tdgr.enabled = false;

            var gr = canvas.GetComponent<GraphicRaycaster>();
            if (gr == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            else
                gr.enabled = true;
        }
    }

    static void EnsureInputSystemModule()
    {
        var es = FindAnyObjectByType<EventSystem>();
        if (es == null) return;

#if ENABLE_INPUT_SYSTEM
        if (es.GetComponent<InputSystemUIInputModule>() != null) return;

        var standalone = es.GetComponent<StandaloneInputModule>();
        if (standalone != null) Object.Destroy(standalone);
        es.gameObject.AddComponent<InputSystemUIInputModule>();
        Debug.Log("[WorkstationSeat] InputSystemUIInputModule instalado en EventSystem.");
#endif
    }

    public bool IsPositionLocked => _mover != null && _mover.IsPositionLocked;
}