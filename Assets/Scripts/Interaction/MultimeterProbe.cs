using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Punta del multímetro (Probe_Red_Tip / Probe_Black_Tip).
///
/// MODOS DE ASIGNACIÓN (los dos funcionan a la vez):
///
/// 1. APUNTADO + TRIGGER (nuevo):
///      Apunta la punta hacia un disco dorado (NodeInteractable).
///      Presiona el trigger del controlador correspondiente:
///        → Probe_Red   (ProbeType.Red)   escucha el trigger DERECHO
///        → Probe_Black (ProbeType.Black) escucha el trigger IZQUIERDO
///      El disco apuntado se ilumina amarillo para confirmar que está en rango.
///
/// 2. CONTACTO FÍSICO (fallback):
///      Toca la punta físicamente con el disco → asigna sin botón.
///
/// SETUP:
///   El Collider de este GO debe ser isTrigger = true (para el fallback físico).
///   detectionRadius: distancia máxima de búsqueda desde la punta (default 0.25 m).
[RequireComponent(typeof(Collider))]
public class MultimeterProbe : MonoBehaviour
{
    public Multimeter multimeter;
    public ProbeType  probeType = ProbeType.Red;

    [Header("Controlador que activa esta punta")]
    [Tooltip("Qué mano escucha el trigger para asignar este probe.\n" +
             "Cambia aquí sin tocar el código.")]
    public XRNode controllerNode = XRNode.RightHand;

    [Header("Detección por apuntado")]
    [Tooltip("Radio de búsqueda hacia adelante desde la punta (metros).")]
    public float detectionRadius = 0.25f;
    [Tooltip("Umbral del trigger para activar (0-1). Default 0.6.")]
    [Range(0.1f, 0.95f)]
    public float triggerThreshold = 0.6f;

    [Header("Feedback visual")]
    [Tooltip("El renderer de la propia punta se pone de este color cuando hay un nodo en rango.")]
    public Color probeAimColor  = new Color(1f, 0.9f, 0.1f);  // amarillo
    public Color probeIdleColor = Color.white;

    // ─── Internos ────────────────────────────────────────────
    private XRNode   _xrNode;
    private bool     _prevTrigger;
    private NodeInteractable _aimTarget;      // nodo apuntado actualmente
    private Renderer         _probeRenderer;  // renderer de esta punta
    private MaterialPropertyBlock _mpb;
    private static readonly int   _colorID = Shader.PropertyToID("_BaseColor");

    // ─── Lifecycle ───────────────────────────────────────────
    void Awake()
    {
        if (multimeter == null)
            multimeter = GetComponentInParent<Multimeter>(true);

        _xrNode        = controllerNode;
        _probeRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        _mpb           = new MaterialPropertyBlock();

        // Mismo bug ya corregido en Multimeter.Awake() para el cuerpo: un XRGrabInteractable con
        // 'colliders' VACÍO auto-recolecta TODOS los colliders del GO (incluido el isTrigger de
        // contacto, además de cualquiera en hijos). Esta punta tiene 2 colliders propios (uno para
        // agarrar, otro isTrigger para el contacto físico) — acotar el grab al NO-trigger evita que
        // el agarre quede indeciso entre ambos y deja el isTrigger libre para OnTriggerEnter.
        var grab = GetComponent<XRGrabInteractable>();
        if (grab != null && grab.colliders.Count == 0)
        {
            foreach (var col in GetComponents<Collider>())
                if (!col.isTrigger) grab.colliders.Add(col);
        }
    }

    void Update()
    {
        // ── Detectar nodo apuntado ────────────────────────
        NodeInteractable detected = FindNearestAhead();

        if (detected != _aimTarget)
        {
            SetNodeHighlight(_aimTarget, false);
            _aimTarget = detected;
            SetNodeHighlight(_aimTarget, true);
            SetProbeColor(_aimTarget != null ? probeAimColor : probeIdleColor);
        }

        // ── Leer trigger del controlador correspondiente ──
        var    device    = InputDevices.GetDeviceAtXRNode(_xrNode);
        bool   triggered = device.TryGetFeatureValue(CommonUsages.trigger, out float tv)
                        && tv > triggerThreshold;

        if (triggered && !_prevTrigger && _aimTarget?.nodeTarget != null)
            Assign(_aimTarget.nodeTarget);

        _prevTrigger = triggered;
    }

    // ─── Búsqueda ────────────────────────────────────────────

    NodeInteractable FindNearestAhead()
    {
        // SphereCast desde la punta en la dirección que apunta el GO.
        // Esferas de 0.05 m de radio para compensar imprecisión al apuntar.
        var hits = Physics.SphereCastAll(
            origin:    transform.position,
            radius:    0.05f,
            direction: transform.forward,
            maxDistance: detectionRadius);

        NodeInteractable best    = null;
        float            bestDist = float.MaxValue;

        foreach (var h in hits)
        {
            var ni = h.collider.GetComponent<NodeInteractable>()
                  ?? h.collider.GetComponentInParent<NodeInteractable>();
            if (ni == null || ni.nodeTarget == null) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = ni; }
        }
        return best;
    }

    // ─── Asignación ──────────────────────────────────────────

    void Assign(ElectricalNode node)
    {
        if (multimeter == null) return;
        if (probeType == ProbeType.Red) multimeter.SetRedNode(node);
        else                            multimeter.SetBlackNode(node);

        // Vibración breve en el controlador
        var device = InputDevices.GetDeviceAtXRNode(_xrNode);
        device.SendHapticImpulse(0, 0.3f, 0.08f);
    }

    // ─── Contacto físico (fallback — funciona sin input XR) ──

    void OnTriggerEnter(Collider other)
    {
        if (multimeter == null) return;

        // Protoboard sandbox (Reto 4): medir tocando un slot de la protoboard.
        // El ProtoboardSimulator asigna el ElectricalNode por railId a cada slot.
        var slot = other.GetComponent<ProtoboardSlot>()
                ?? other.GetComponentInParent<ProtoboardSlot>();
        if (slot != null && slot.assignedNode != null)
        {
            Assign(slot.assignedNode);
            return;
        }

        // Nodos clásicos (Retos 1-3): discos NodeInteractable.
        var ni = other.GetComponent<NodeInteractable>()
              ?? other.GetComponentInParent<NodeInteractable>();
        if (ni?.nodeTarget == null) return;
        Assign(ni.nodeTarget);
    }

    // ─── Feedback visual ─────────────────────────────────────

    void SetProbeColor(Color c)
    {
        if (_probeRenderer == null) return;
        _probeRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_colorID, c);
        _probeRenderer.SetPropertyBlock(_mpb);
    }

    // Ilumina / apaga el highlight del disco dorado apuntado
    void SetNodeHighlight(NodeInteractable ni, bool on)
    {
        if (ni == null) return;
        ni.SetAimHighlight(on, probeType == ProbeType.Red
            ? new Color(1f, 0.3f, 0.3f)   // rojo tenue para la punta roja
            : new Color(0.3f, 0.3f, 1f));  // azul tenue para la punta negra
    }

    void OnDisable()
    {
        SetNodeHighlight(_aimTarget, false);
        _aimTarget = null;
        SetProbeColor(probeIdleColor);
    }
}
