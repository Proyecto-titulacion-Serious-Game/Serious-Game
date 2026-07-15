using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Objetivo")]
    public Transform target;

    [Header("Cabeza del robot")]
    [Tooltip("Arrastra aquí el Transform del hueso 'Head' del TechnicianRobot.")]
    public Transform headBone;
    public string headBoneName = "Head";
    public Vector3 headOffset = new Vector3(0f, 0.08f, 0.05f);

    [Header("Anclaje de cámara")]
    [Tooltip("Punto al que se ancla la cámara (estable, a la altura de la cabeza). Si se deja vacío " +
             "busca 'PlayerCameraRoot' dentro del rig. Es MEJOR que el hueso de la cabeza porque NO " +
             "se balancea con la animación de caminar.")]
    public Transform cameraRoot;
    public string cameraRootName = "PlayerCameraRoot";

    [Header("Fallback (sin hueso)")]
    public float eyeHeight = 1.65f;

    [Header("Cámara")]
    public float sensitivity = 2f;
    public float minPitch    = -80f;
    public float maxPitch    =  80f;

    [Header("Foco forzado (cutscene, ej. bienvenida del Robot-Guía)")]
    [Tooltip("Si está asignado, la cámara gira sola para mirar este punto y el mouse-look queda " +
             "bloqueado. Ponlo en null para devolver el control al jugador — la vista donde quedó " +
             "mirando se vuelve el nuevo punto de partida, sin salto brusco.")]
    public Transform focoForzado;

    private float _pitch       = 0f;
    private float _yaw         = 0f;    // yaw acumulado (gira el CUERPO); el mouse-look vive aquí
    private bool  _yawInit     = false;
    private bool  _cursorFree  = false;
    private bool  _started     = false;

    private Renderer[] _bodyRenderers;

    /// <summary>True mientras la cámara de caminar está activa (el Técnico NO está sentado).
    /// Lo usan TechnicianController (para no liberar el cursor al caminar) y PauseMenu.</summary>
    public static bool IsActive { get; private set; }

    public float Yaw => target != null ? target.eulerAngles.y : transform.eulerAngles.y;

    void Awake()
    {
        // NO desacoplar la cámara. Si es la PlayerCameraRoot (hija del rig RobotKyle), DEBE seguir
        // siendo hija para moverse y girar junto al personaje. Antes 'transform.SetParent(null)' la
        // arrancaba del rig → la PlayerCameraRoot aparecía FUERA del TechnicianRobot al dar Play y la
        // cámara quedaba estática mientras el personaje caminaba (bug 2026-06-28).
        CacheBodyRenderers();
    }

    void OnEnable()  { IsActive = true;  if (_started && !_cursorFree) LockCursor(true); }
    void OnDisable() { IsActive = false; }

    void Start()
    {
        LockCursor(true);

        // El rig de NoonA viene con target=null y un mover DUPLICADO (WalkerPC +
        // TechnicianMover en el mismo GO). Sin target, la cámara —desacoplada en Awake—
        // no sigue el yaw del cuerpo: el robot gira pero la vista no. Lo resolvemos aquí.
        ResolveWalkerRig();

        if (headBone == null && target != null)
            headBone = FindChildByName(target, headBoneName);

        // Anclaje preferido: PlayerCameraRoot del rig (StarterAssets) — estable, a la altura de la
        // cabeza (y≈1.375), hijo directo del rig (no un hueso) → NO se balancea con la animación.
        if (cameraRoot == null && target != null)
            cameraRoot = FindChildByName(target, cameraRootName);

        // 1ª persona viendo el CUERPO: ocultar SOLO la cabeza para no tapar la cámara. RobotKyle es
        // malla única → se "decapita" colapsando el hueso Head a escala 0 (el cuerpo queda visible).
        // Si no hay hueso Head, fallback: ocultar todo el cuerpo (comportamiento anterior).
        if (headBone != null)
            headBone.localScale = Vector3.zero;
        else
            SetBodyVisible(false);

        _started = true;
    }

    /// <summary>
    /// Asigna el cuerpo del walker como <see cref="target"/> (para que la cámara siga su
    /// yaw) y elimina el mover redundante para que no se peleen dos locomociones.
    /// TechnicianMover es el dueño del giro+movimiento (lo usa WorkstationSeat); WalkerPC
    /// se desactiva y su Rigidbody se vuelve kinemático para que no estorbe.
    /// </summary>
    void ResolveWalkerRig()
    {
        var mover  = FindAnyObjectByType<TechnicianMover>(FindObjectsInactive.Include);
        var walker = FindAnyObjectByType<WalkerPC>(FindObjectsInactive.Include);

        if (mover != null && walker != null && walker.gameObject == mover.gameObject)
        {
            walker.enabled = false;
            var rb = mover.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;   // no pelear con el CharacterController
        }

        if (target == null)
        {
            if (mover != null)       target = mover.transform;
            else if (walker != null) target = walker.transform;
        }
    }

    // CORRECCIÓN: Cambiado de Update a LateUpdate para evitar Jitter (temblor)
    // al seguir los huesos de la animación del Técnico.
    void LateUpdate()
    {
        if (!_started) return;

        // El rig puede cargar DESPUÉS que esta cámara (NoonA es aditiva). Si falta target/cameraRoot,
        // re-resolverlos cada frame hasta encontrarlos → la cámara "engancha" al rig al aparecer.
        if (target == null) ResolveWalkerRig();
        if (cameraRoot == null && target != null) cameraRoot = FindChildByName(target, cameraRootName);
        if (headBone   == null && target != null) headBone   = FindChildByName(target, headBoneName);

        // Inicializar el yaw con la orientación actual del cuerpo (1 vez, al aparecer el rig).
        if (!_yawInit && target != null) { _yaw = target.eulerAngles.y; _yawInit = true; }

        // Mantener la CABEZA colapsada (1ª persona viendo el cuerpo), por si el Animator la restaura.
        if (headBone != null) headBone.localScale = Vector3.zero;

        // Esc lo gestiona PauseMenu. Aquí, F alterna cursor libre/bloqueado al caminar
        // (útil para clicar algo sin sentarse). En pausa no se procesa input.
        var kb = Keyboard.current;
        if (!PauseMenu.IsPaused && kb != null && kb.fKey.wasPressedThisFrame)
        {
            _cursorFree = !_cursorFree;
            LockCursor(!_cursorFree);
        }

        if (focoForzado != null && cameraRoot != null)
        {
            Vector3 dir = focoForzado.position - cameraRoot.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Vector3 euler = Quaternion.LookRotation(dir).eulerAngles;
                _pitch = Mathf.Clamp(NormalizeAngle(euler.x), minPitch, maxPitch);
                _yaw   = euler.y;
                if (target != null) target.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = cameraRoot.position;
            return;   // bloquea el mouse-look y el resto del posicionamiento mientras dura el foco
        }

        if (!_cursorFree && !PauseMenu.IsPaused)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                // El mouse-look COMPLETO vive aquí (TechnicianMover es solo movimiento):
                //   YAW  → gira el CUERPO (target); el modelo y esta cámara giran con él.
                //   PITCH → gira solo la cámara.
                _yaw += delta.x * sensitivity * GameSettings.MouseSensitivity * 0.1f;
                if (target != null) target.rotation = Quaternion.Euler(0f, _yaw, 0f);

                _pitch -= delta.y * sensitivity * GameSettings.MouseSensitivity * 0.1f;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }
            // Mantener el cursor bloqueado mientras se camina para que el mouse-look
            // no se corte al tocar el borde de la pantalla.
            if (Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
        }

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        if (cameraRoot != null)
        {
            // Anclaje ESTABLE a PlayerCameraRoot (se mueve con el rig, no se balancea con el walk).
            transform.position = cameraRoot.position;
        }
        else if (headBone != null)
        {
            transform.position = headBone.position
                               + headBone.up      * headOffset.y
                               + headBone.forward * headOffset.z
                               + headBone.right   * headOffset.x;
        }
        else if (target != null)
        {
            transform.position = target.position + new Vector3(0f, eyeHeight, 0f);
        }
    }

    void CacheBodyRenderers()
    {
        var robot = GameObject.Find("TechnicianRobot");
        if (robot != null)
            _bodyRenderers = robot.GetComponentsInChildren<Renderer>(true);
    }

    void SetBodyVisible(bool visible)
    {
        if (_bodyRenderers == null) return;
        var mode = visible
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        foreach (var r in _bodyRenderers)
            if (r != null) r.shadowCastingMode = mode;
    }

    static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;

    static Transform FindChildByName(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }
}
