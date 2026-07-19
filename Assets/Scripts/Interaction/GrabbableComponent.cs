using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Va en cada prefab de componente entregable (Comp_Resistor, Comp_LED, Comp_Capacitor, ArduinoPin).
/// Gestiona el ciclo grab→soltar con física y feedback háptico.
///
/// SETUP EN CADA PREFAB (hacerlo una vez, se aplica a todas las instancias):
///   1. XRGrabInteractable  — Movement Type: Kinematic  — Track Position: ON — Track Rotation: ON
///   2. Rigidbody           — Is Kinematic: TRUE (empieza quieto en la bandeja)
///   3. Collider            — Is Trigger: FALSE (para detección física y slots)
///   4. Este script
///
/// FLUJO:
///   Componente spawneado en toolbox (kinematic, quieto)
///   → Explorador aprieta gatillo cerca del componente
///   → OnGrabbed: se desparentea de la caja, kinematic OFF, sigue la mano
///   → Explorador lo mete en el ComponentSlot del circuito
///   → ComponentSlot.OnTriggerEnter: instala, kinematic ON otra vez
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class GrabbableComponent : MonoBehaviour
{
    [Header("Feedback (auto-detectado si queda vacío)")]
    public HapticFeedback haptics;

    [Header("SelectableComponent asociado (opcional — para highlight)")]
    public SelectableComponent selectable;

    [Header("PlayerInteraction (auto-detectado)")]
    public PlayerInteraction playerInteraction;

    private XRGrabInteractable _grab;
    private Rigidbody          _rb;

    void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _rb   = GetComponent<Rigidbody>();

        if (haptics          == null) haptics          = FindAnyObjectByType<HapticFeedback>();
        if (playerInteraction == null) playerInteraction = FindAnyObjectByType<PlayerInteraction>();
        if (selectable        == null) selectable        = GetComponent<SelectableComponent>();
    }

    void OnEnable()
    {
        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        _grab.selectEntered.RemoveListener(OnGrabbed);
        _grab.selectExited.RemoveListener(OnReleased);
        CancelInvoke(nameof(OnReleasedNextFrame));
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        // Desparentar de la caja para que la física sea independiente
        transform.SetParent(null);

        // XRGrabInteractable (MovementType.Kinematic) gestiona isKinematic internamente.
        // No sobreescribir aquí: si lo ponemos false, el objeto cae y XRI no puede moverlo.

        haptics?.PlayMedium();
        playerInteraction?.OnGrabComponent(selectable);

        Debug.Log($"[GrabbableComponent] Agarrado: {name}");
    }

    void OnReleased(SelectExitEventArgs args)
    {
        // XRGrabInteractable.OnSelectExited dispara este evento y luego llama Drop(),
        // que restaura isKinematic al valor previo al grab (true). Diferir un frame
        // garantiza que nuestro cambio llegue después de Drop() y que el slot pueda
        // instalar antes de que la gravedad tome efecto.
        Invoke(nameof(OnReleasedNextFrame), 0f);

        // Sonido de "colocado en slot": SOLO al soltar Y si la pieza quedó encajada en un slot
        // del protoboard (patas con nodo). Se difiere para dar tiempo al simulador (20 Hz) a
        // re-enganchar las patas. Al AGARRAR no suena (esto solo corre al soltar). Los slots
        // fijos (ComponentSlot) avisan por su cuenta al instalar.
        Invoke(nameof(AnunciarSiEncajoEnSlot), 0.2f);

        haptics?.PlayLight();
        playerInteraction?.OnReleaseComponent(selectable);

        Debug.Log($"[GrabbableComponent] Soltado: {name}");
    }

    void AnunciarSiEncajoEnSlot()
    {
        var comp = GetComponent<ElectricalComponent>() ?? GetComponentInChildren<ElectricalComponent>(true);
        var conn = GetComponent<ProtoboardConnector>() ?? GetComponentInChildren<ProtoboardConnector>(true);
        bool encajado = conn != null && comp != null && (comp.nodeA != null || comp.nodeB != null);
        if (encajado) RaisePlacedInSlot();
    }

    void OnReleasedNextFrame()
    {
        Released?.Invoke(this);  // el slot instala aquí si el componente está dentro
        EnableGravity();         // si DisableGrab() fue llamado, retorna temprano
    }

    void EnableGravity()
    {
        if (!_grab.enabled) return;
        _rb.isKinematic = false;
        _rb.useGravity  = true;
    }

    /// <summary>True mientras el componente está siendo sostenido por un interactor.</summary>
    public bool IsGrabbed => _grab != null && _grab.isSelected;

    /// <summary>Se dispara un frame después de soltar. ComponentSlot se suscribe para snap-on-release.</summary>
    public event System.Action<GrabbableComponent> Released;

    /// <summary>Se dispara cuando una pieza queda COLOCADA en un slot (protoboard o ComponentSlot).
    /// Lo usa CircuitAudioManager para el sonido de "colocado" — que ya NO suena al agarrar/mover.</summary>
    public static event System.Action PlacedInSlot;

    /// <summary>Notifica una colocación en slot (lo llaman ComponentSlot al instalar y el release-si-encajó).</summary>
    public static void RaisePlacedInSlot() => PlacedInSlot?.Invoke();

    /// <summary>Llamado por ComponentSlot tras instalación exitosa. El componente queda fijo.</summary>
    public void DisableGrab()
    {
        if (_grab == null) _grab = GetComponent<XRGrabInteractable>();   // lazy: Awake() puede no haber corrido aún
        if (_grab != null) _grab.enabled = false;
    }

    /// <summary>Re-habilita el grab (usado cuando el slot permite remover el componente instalado).</summary>
    public void EnableGrab()
    {
        if (_grab == null) _grab = GetComponent<XRGrabInteractable>();
        if (_grab != null) _grab.enabled = true;
    }
}
