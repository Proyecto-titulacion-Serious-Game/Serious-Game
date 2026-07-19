using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Imán de enchufe + "tirón" de desenchufe para UNA punta (Probe_A / Probe_B) de un jumper
/// flexible del Reto 4.
///
/// ENCHUFAR: al SOLTAR la punta dentro de <see cref="plugRadius"/> de un punto de conexión
/// (slot de protoboard o header de pin del Arduino), se clava EXACTO sobre el nodo. Con esto:
///   1. Feedback visual de cable conectado (la punta entra en el hueco, no flota cerca).
///   2. Enganche eléctrico garantizado: <see cref="ProtoboardConnector"/> engancha cada pata al
///      nodo más cercano dentro de un snapRadius pequeño (~1.2 cm) usando la POSICIÓN de la punta;
///      al clavarla la distancia pasa a 0 (a mano, en VR, casi nunca aciertas 1.2 cm).
///
/// DESENCHUFAR CON TIRÓN: al AGARRAR una punta ya enchufada, queda anclada en el hueco hasta que
/// tiras de la mano más de <see cref="unplugThreshold"/>; recién ahí se libera y sigue la mano
/// (como sacar un jumper real con un poco de fricción). Un toque sin tirón la deja enchufada.
///
/// La añade <c>CableBoxSpawner.MakeProbeGrabbable</c> a cada punta. Reusa los huecos cacheados en
/// <see cref="ProtoboardSimulator.ConnectionPoints"/> (no recalcula nada).
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class CableProbePlug : MonoBehaviour
{
    [Tooltip("Radio (m) para que la punta se enchufe sola al soltarla cerca de un hueco. " +
             "3 cm = generoso para VR sin saltar a huecos no deseados.")]
    public float plugRadius = 0.03f;

    [Tooltip("Distancia (m) que hay que tirar de la mano para desenchufar una punta agarrada. " +
             "2 cm = un tironcito firme, evita que se salga al solo rozarla.")]
    public float unplugThreshold = 0.02f;

    XRGrabInteractable  _grab;
    ProtoboardSimulator _sim;

    bool      _plugged;     // ¿la punta está enchufada en un hueco?
    Vector3   _plugPos;     // posición del hueco donde está enchufada
    bool      _tugging;     // agarrada-pero-aún-enchufada: anclada hasta superar el tirón
    Transform _interactor;  // mano/controlador que la sostiene
    Vector3   _grabStart;   // posición de la mano al iniciar el agarre

    void Awake() => _grab = GetComponent<XRGrabInteractable>();

    void OnEnable()
    {
        if (_grab == null) return;
        _grab.selectEntered.AddListener(OnGrab);
        _grab.selectExited.AddListener(OnRelease);
    }

    void OnDisable()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnGrab);
        _grab.selectExited.RemoveListener(OnRelease);
    }

    void OnGrab(SelectEnterEventArgs a)
    {
        if (!_plugged) return;                  // punta libre → sigue la mano normalmente
        _tugging    = true;                     // enchufada → anclada hasta el tirón
        _interactor = a.interactorObject?.transform;
        _grabStart  = _interactor != null ? _interactor.position : transform.position;
    }

    void OnRelease(SelectExitEventArgs _)
    {
        _tugging = false;
        TrySnap();
    }

    // Tras el movimiento de XRI: si está en fase de tirón, re-anclar al hueco hasta superar el umbral.
    void LateUpdate()
    {
        if (!_tugging) return;
        if (_interactor == null) { _tugging = false; return; }

        float pulled = Vector3.Distance(_interactor.position, _grabStart);
        if (pulled < unplugThreshold)
        {
            transform.position = _plugPos;      // mantener enchufada (resistir el agarre)
        }
        else
        {
            _tugging = false;                   // ¡desenchufada! ya sigue la mano
            _plugged = false;
            if (_sim != null) _sim.MarkDirty(); // re-simula: la punta sale del nodo
        }
    }

    /// <summary>Al soltar, enchufa la punta al hueco más cercano dentro de plugRadius (si lo hay).</summary>
    void TrySnap()
    {
        // Hay 2 ProtoboardSimulator en la escena (Reto 2 y Reto 4). FindAnyObjectByType devuelve
        // el primero que encuentre según orden interno de Unity, NO el más cercano — un cable del
        // Reto 4 podía terminar enganchado al simulador del Reto 2 (o viceversa) y entonces el imán
        // nunca encontraba huecos dentro de plugRadius (los tableros están lejos entre sí), pareciendo
        // "el cable no conecta". Se resuelve por PROXIMIDAD real a la posición de esta punta.
        if (_sim == null) _sim = NearestSimulator();
        if (_sim == null) return;

        var pts = _sim.ConnectionPoints;
        if (pts == null || pts.Count == 0)
        {
            _sim.MarkDirty();   // aún sin cache → fuerza una simulación para poblar ConnectionPoints
            return;
        }

        Vector3 p = transform.position;
        float bestSqr = plugRadius * plugRadius;
        int best = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            float d = (pts[i].position - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }

        if (best >= 0)
        {
            transform.position = pts[best].position;   // ENCHUFAR sobre el nodo
            _plugged = true;
            _plugPos = pts[best].position;
            _sim.MarkDirty();                           // re-simula con la punta ya en el hueco
        }
        else
        {
            _plugged = false;   // soltada lejos de todo hueco → queda libre
        }
    }

    /// <summary>ProtoboardSimulator más cercano a esta punta (no el primero que encuentre Unity).</summary>
    ProtoboardSimulator NearestSimulator()
    {
        var all = FindObjectsByType<ProtoboardSimulator>(FindObjectsSortMode.None);
        ProtoboardSimulator best = null;
        float bestSqr = float.MaxValue;
        foreach (var s in all)
        {
            if (s == null) continue;
            float d = (s.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = s; }
        }
        return best;
    }
}
