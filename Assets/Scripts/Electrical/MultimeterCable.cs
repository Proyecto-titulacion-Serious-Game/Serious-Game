using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Physics-simulated cable between a multimeter jack anchor and a grabbable probe tip.
///
/// Visual  : cubic-bezier LineRenderer (sag increases when slack, decreases when taut).
/// Physics : SpringJoint on the probe limits extension to maxCableLength.
///           The connected body is the multimeter's Rigidbody (kinematic anchor).
///
/// Lifecycle:
///   – Probe starts isKinematic=true at anchor position (jack, not hanging).
///   – On XRGrabInteractable.selectEntered: XRI sets isKinematic=false → probe goes dynamic.
///   – SpringJoint resists extension beyond maxCableLength (600 N/m spring, soft stop).
///   – On selectExited: one-frame coroutine snaps probe back to anchor (kinematic restored).
///
/// Setup (handled by MultimeterArtSetupTool):
///   anchorPoint    = empty GO at jack exit, child of multimeter root
///   probeRigidbody = Rigidbody on the sphere probe tip
[RequireComponent(typeof(LineRenderer))]
public class MultimeterCable : MonoBehaviour
{
    [Header("Endpoints")]
    [Tooltip("Empty transform at the jack exit on the multimeter body.")]
    public Transform anchorPoint;
    [Tooltip("Rigidbody of the grabbable probe tip.")]
    public Rigidbody probeRigidbody;

    [Header("Cable")]
    [Min(0.1f)]          public float maxCableLength = 0.6f;
    [Range(6, 32)]       public int   segments       = 16;
    [Range(0.001f, 0.015f)] public float cableWidth  = 0.003f;
    [Range(0f, 0.3f)]    public float sagAmount      = 0.08f;

    [Header("Reposo")]
    [Tooltip("Offset en WORLD SPACE desde anchorPoint donde descansa la punta al soltarla. " +
             "El jack/nub visual se queda exactamente en anchorPoint (alineado con el modelo 3D); " +
             "solo la punta AGARRABLE se aleja, para que su collider de agarre no quede pegado/adentro " +
             "del collider del cuerpo del multímetro (causaba que la mano agarrara el cuerpo por error " +
             "al intentar tomar la punta). Hacia ARRIBA (+Y), no hacia abajo: un offset hacia abajo puede " +
             "meter la punta dentro de la mesa donde se apoya el multímetro — al mover el cuerpo, la " +
             "física intenta sacarla de la geometría con un impulso fuerte que empuja al jugador.")]
    public Vector3 restOffset = new Vector3(0f, 0.02f, 0f);

    LineRenderer _lr;

    void Awake() => _lr = GetComponent<LineRenderer>();

    void Start()
    {
        if (probeRigidbody == null || anchorPoint == null) return;

        probeRigidbody.isKinematic = true;
        probeRigidbody.useGravity  = true;
        probeRigidbody.transform.position = anchorPoint.position + restOffset;

        SetupSpringJoint();

        var grab = probeRigidbody.GetComponent<XRGrabInteractable>();
        if (grab != null)
            grab.selectExited.AddListener(_ => StartCoroutine(ReturnToAnchor()));
    }

    void LateUpdate()
    {
        if (anchorPoint == null || probeRigidbody == null) return;
        DrawCable(anchorPoint.position, probeRigidbody.transform.position);
    }

    // ── Physics ──────────────────────────────────────────────────────────

    void SetupSpringJoint()
    {
        var sj = probeRigidbody.gameObject.AddComponent<SpringJoint>();

        var multimeterRb = anchorPoint.GetComponentInParent<Rigidbody>();
        sj.connectedBody = multimeterRb;
        sj.autoConfigureConnectedAnchor = false;
        sj.connectedAnchor = multimeterRb != null
            ? multimeterRb.transform.InverseTransformPoint(anchorPoint.position)
            : anchorPoint.position;

        sj.anchor      = Vector3.zero;
        sj.minDistance = 0f;
        sj.maxDistance = maxCableLength;
        sj.spring      = 600f;
        sj.damper      = 12f;
        sj.tolerance   = 0.005f;
        sj.enableCollision = false;
    }

    IEnumerator ReturnToAnchor()
    {
        yield return null;                       // wait: let XRI finish selectExited cleanup
        probeRigidbody.isKinematic = true;
        probeRigidbody.transform.position = anchorPoint.position + restOffset;
    }

    // ── Visual cable ─────────────────────────────────────────────────────

    void DrawCable(Vector3 start, Vector3 end)
    {
        float dist  = Vector3.Distance(start, end);
        // More sag when slack (probe near anchor), less when taut (probe far away)
        float slack = Mathf.Max(0f, maxCableLength - dist);
        float sag   = Mathf.Clamp(slack * 0.5f, 0.01f, sagAmount);

        Vector3 mid = Vector3.Lerp(start, end, 0.5f) + Vector3.down * sag;
        Vector3 c1  = Vector3.Lerp(start, mid, 0.5f);
        Vector3 c2  = Vector3.Lerp(end,   mid, 0.5f);

        _lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);
            _lr.SetPosition(i, Bezier(start, c1, c2, end, t));
        }
    }

    static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u*u*u*p0 + 3f*u*u*t*p1 + 3f*u*t*t*p2 + t*t*t*p3;
    }
}
