using System.Collections;
using UnityEngine;

/// <summary>
/// RETO 4 — Endereza el componente al soltarlo sobre la protoboard.
///
/// Problema que resuelve: el componente sigue la mano del Explorador con la rotación que tenga
/// el controlador (XRGrabInteractable · Track Rotation). Al soltarlo, quedaba con esa inclinación
/// "torcida" de la mano, así que sus patas (leadA/leadB) no caían rectas en los huecos y el
/// ProtoboardConnector no las enganchaba (ver LeadB "libre" en el log).
///
/// Este script, SOLO durante el Reto 4 (modo protoboard activo), al soltar la pieza la alinea a
/// la cuadrícula de la protoboard: snap a la orientación ortogonal más cercana RELATIVA a la
/// protoboard. Elimina la inclinación de la mano pero respeta hacia dónde la orientó el jugador
/// (no la fuerza a una única pose). Luego la deja quieta (kinematic) para que la gravedad no la
/// tumbe y arruine el enderezado — que es además el estado de reposo documentado del prefab.
///
/// Se auto-engancha desde ExplorerComponentReceiver.ConfigurarComponente (no requiere cablear nada).
/// </summary>
[DisallowMultipleComponent]
public class Reto4StraightenOnPlace : MonoBehaviour
{
    GrabbableComponent _grab;
    Rigidbody          _rb;

    void Awake()
    {
        _grab = GetComponent<GrabbableComponent>();
        _rb   = GetComponent<Rigidbody>();
    }

    void OnEnable()  { if (_grab != null) _grab.Released += OnReleased; }
    void OnDisable() { if (_grab != null) _grab.Released -= OnReleased; }

    void OnReleased(GrabbableComponent _)
    {
        // Solo en Reto 4: Reto4BreadboardMode setea ResistorScaleReto4 mientras el modo protoboard
        // está activo (y lo pone null al salir). Es nuestro flag de "estamos en el Reto 4".
        if (!Reto4BreadboardMode.ResistorScaleReto4.HasValue) return;

        // GrabbableComponent reactiva la gravedad en ESTE mismo frame, justo después de este evento.
        // Diferimos un frame para enderezar y congelar DESPUÉS de eso (si no, lo pisa la gravedad).
        StartCoroutine(StraightenNextFrame());
    }

    IEnumerator StraightenNextFrame()
    {
        yield return null;

        // Referencia de la protoboard: el GameObject que tiene el ProtoboardSimulator.
        var sim = FindAnyObjectByType<ProtoboardSimulator>();
        Quaternion boardRot = sim != null ? sim.transform.rotation : Quaternion.identity;

        // Rotación actual EXPRESADA en el marco de la protoboard, redondeada al múltiplo de 90°
        // más cercano en cada eje → queda alineada a la cuadrícula, sin la inclinación de la mano.
        Quaternion local = Quaternion.Inverse(boardRot) * transform.rotation;
        Vector3 e = local.eulerAngles;
        e.x = Mathf.Round(e.x / 90f) * 90f;
        e.y = Mathf.Round(e.y / 90f) * 90f;
        e.z = Mathf.Round(e.z / 90f) * 90f;
        transform.rotation = boardRot * Quaternion.Euler(e);

        // RESISTOR: además de enderezarlo, ACOSTARLO horizontal y posarlo SOBRE la superficie con
        // cada pata encima de un slot de net distinto. Sin esto quedaba clavado dentro del slot /
        // hundido en el protoboard (bug reportado): el snap de rotación conservaba la pose vertical
        // de la mano y la posición del drop, que solía estar dentro del volumen de la placa.
        // La variante VERTICAL se excluye: su pose de diseño es parada (no se acuesta).
        bool esResistor = GetComponent<Resistor>() != null || GetComponentInChildren<Resistor>(true) != null;
        if (esResistor && !name.ToLowerInvariant().Contains("vertical"))
            AcostarYPosarSobreSlots(sim, boardRot);

        // Dejar la pieza quieta y plana sobre la protoboard (estado de reposo del prefab).
        if (_rb != null)
        {
            _rb.isKinematic     = true;
            _rb.useGravity      = false;
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Posa el resistor como PUENTE entre los 2 puntos de conexión de nets DISTINTOS más cercanos
    /// al drop — pueden ser 2 slots de la protoboard, un header de PIN del Arduino y un slot, un
    /// slot y un GND del Arduino, o PIN↔GND directo (los headers de pin y los GND son puntos
    /// enchufables del simulador, igual que los slots). La pieza se ESTIRA a lo largo hasta que
    /// cada pata queda sobre su punto ("alargar hasta que conecte de slot a slot / al PIN / al
    /// GND"), y el cuerpo queda ~2 mm por encima de la línea entre ambos (nunca hundido).
    /// Solo actúa si el drop fue cerca de un punto (menos de 10 cm) — lejos, no se toca.
    /// </summary>
    void AcostarYPosarSobreSlots(ProtoboardSimulator sim, Quaternion boardRot)
    {
        if (sim == null) return;
        var conn = GetComponent<ProtoboardConnector>() ?? GetComponentInChildren<ProtoboardConnector>(true);
        if (conn == null || conn.leadA == null || conn.leadB == null) return;

        var puntos = sim.ConnectionPoints;   // slots + headers de pin + GNDs (cache del simulador)
        if (puntos == null || puntos.Count == 0) return;

        Vector3 up     = boardRot * Vector3.up;
        Vector3 centro = (conn.leadA.position + conn.leadB.position) * 0.5f;

        // Radio de activación del snap ESCALADO al tablero real: el board del Reto 4 está
        // agrandado (nets a ~18 cm entre sí, pines del Arduino a ~70-95 cm del protoboard), así
        // que un radio fijo de 10 cm dejaba la pieza "suelta" en la mayoría de los drops y las
        // patas nunca quedaban a los 1.2 cm que exige el Bind → "no llega a GND" aunque la
        // resistencia estuviera ahí (reporte real con digitalWrite(4, HIGH) sin LED).
        float sepNets = sim.SepararacionMinimaEntreNetsDistintos();
        float radioSnap = Mathf.Max(0.1f, sepNets * 1.25f);

        // 1) Punto ancla: el punto de conexión más cercano al centro de la pieza.
        int iA = -1; float dA = float.MaxValue;
        for (int i = 0; i < puntos.Count; i++)
        {
            if (puntos[i].node == null) continue;
            float d = (puntos[i].position - centro).sqrMagnitude;
            if (d < dA) { dA = d; iA = i; }
        }
        if (iA < 0 || dA > radioSnap * radioSnap) return;   // drop lejos de todo → no tocar

        Vector3 eje = conn.leadA.position - conn.leadB.position;
        if (eje.sqrMagnitude < 1e-8f) return;
        float span = eje.magnitude;

        // 2) Compañero: el punto de OTRO net más cercano al ancla, PREFIRIENDO los que caen en el
        //    rango alcanzable del estiramiento (0.6×–6× del largo de patas). Sin este filtro, al
        //    soltar cerca del header del Arduino el "más cercano" era el PIN CONTIGUO (a ~2.5 mm)
        //    y la pieza se encogía en un micro-puente D4↔D5 que nadie quiere. Si ningún punto cae
        //    en rango, se usa el más cercano a secas (mejor un puente corto que ninguno).
        var pA = puntos[iA];
        float minPref = 0.6f * span, maxPref = 6f * span;
        int iB = -1, iBFallback = -1; float dB = float.MaxValue, dBFallback = float.MaxValue;
        for (int i = 0; i < puntos.Count; i++)
        {
            if (i == iA || puntos[i].node == null || puntos[i].node == pA.node) continue;
            float d = Vector3.Distance(puntos[i].position, pA.position);
            if (d >= minPref && d <= maxPref) { if (d < dB) { dB = d; iB = i; } }
            else if (d < dBFallback)          { dBFallback = d; iBFallback = i; }
        }
        if (iB < 0) iB = iBFallback;
        if (iB < 0) return;
        var pB = puntos[iB];
        float distObj  = Vector3.Distance(pA.position, pB.position);
        if (distObj < 1e-4f) return;

        // 3) Orientar el eje de patas a la línea pA→pB (3D: entre un pin del Arduino y un slot la
        //    línea puede tener pendiente — la pieza queda como un puentecito inclinado, natural).
        Vector3 linea = (pB.position - pA.position).normalized;
        Vector3 ejeN  = eje.normalized;
        if (Vector3.Dot(ejeN, linea) < 0f) linea = -linea;   // conservar el sentido actual (no voltear)
        transform.rotation = Quaternion.FromToRotation(ejeN, linea) * transform.rotation;

        // 4) ESTIRAR el eje largo LOCAL para que las patas cubran exactamente pA↔pB.
        //    Clamp defensivo: nunca encoger a menos del 60% ni estirar más de 6× (si el par está
        //    absurdamente lejos, mejor quedar corto que un resistor gigante).
        float estirar = Mathf.Clamp(distObj / span, 0.6f, 6f);
        Vector3 ejeLocal = transform.InverseTransformDirection(linea);
        int idx = (Mathf.Abs(ejeLocal.x) >= Mathf.Abs(ejeLocal.y) && Mathf.Abs(ejeLocal.x) >= Mathf.Abs(ejeLocal.z)) ? 0
                : (Mathf.Abs(ejeLocal.y) >= Mathf.Abs(ejeLocal.z)) ? 1 : 2;
        Vector3 esc = transform.localScale;
        esc[idx] *= estirar;
        transform.localScale = esc;

        // 5) POSAR: centro de patas en el punto medio pA↔pB, elevado ~2 mm para que el cuerpo
        //    quede SOBRE los huecos y no dentro de la placa. Las patas quedan a milímetros de sus
        //    puntos — muy dentro del snapRadius del conector (1.2 cm), el Bind las engancha.
        Vector3 midPts   = (pA.position + pB.position) * 0.5f + up * 0.002f;
        Vector3 midLeads = (conn.leadA.position + conn.leadB.position) * 0.5f;
        transform.position += midPts - midLeads;

        Debug.Log($"[Reto4Straighten] Resistor puenteado '{pA.node.name}' ↔ '{pB.node.name}' " +
                  $"(largo {distObj * 100f:F1} cm, estirado ×{estirar:F2}).");
    }
}
