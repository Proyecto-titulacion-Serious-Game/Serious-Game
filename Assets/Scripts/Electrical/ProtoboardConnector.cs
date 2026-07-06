using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Engancha las dos patas de un <see cref="ElectricalComponent"/> (LED, resistencia, jumper)
/// al <see cref="ElectricalNode"/> más cercano — sea un <see cref="ProtoboardSlot"/> de la
/// protoboard o un header de pin del Arduino. Sin esto, un componente colocado nunca obtiene
/// nodeA/nodeB y el grafo del Reto 4 queda vacío.
///
/// Es OPT-IN: solo los componentes con este script se re-enganchan; los pre-cableados a mano
/// no se tocan. <see cref="ProtoboardSimulator"/> llama <see cref="Bind"/> en cada simulación
/// (tras BuildNodeMap), así que mover el componente lo re-conecta automáticamente.
///
/// Las patas (leadA/leadB) se auto-crean en los extremos del bounding box si no se asignan.
/// </summary>
[RequireComponent(typeof(ElectricalComponent))]
public class ProtoboardConnector : MonoBehaviour
{
    [Tooltip("Pata A (ánodo/positivo). Si null, se auto-crea en un extremo del componente.")]
    public Transform leadA;
    [Tooltip("Pata B (cátodo/negativo). Si null, se auto-crea en el extremo opuesto.")]
    public Transform leadB;
    [Tooltip("Radio máximo (m) para enganchar una pata a un nodo. ~1.2 cm por defecto.")]
    public float snapRadius = 0.012f;

    [Header("Nodos FIJOS (determinista — opcional)")]
    [Tooltip("Si true, los nodos NO se reenganchan por posición física: se fijan por railId " +
             "(lockRailA/lockRailB). Inmune al escalado de zona / calibración VR. Lo usa el Reto 2.")]
    public bool lockNodes = false;
    [Tooltip("railId del ProtoboardSlot para nodeA (ánodo en un LED). Solo si lockNodes.")]
    public string lockRailA = "";
    [Tooltip("railId del ProtoboardSlot para nodeB (cátodo en un LED). Solo si lockNodes.")]
    public string lockRailB = "";

    // Registro estático: ProtoboardSimulator itera esto sin FindObjectsByType cada tick.
    public static readonly List<ProtoboardConnector> Active = new List<ProtoboardConnector>();

    private ElectricalComponent _comp;
    private ProtoboardSimulator _sim;
    private Vector3 _lastPos;
    private Vector3 _lastLeadA, _lastLeadB;

    /// <summary>
    /// Garantiza que un componente colocable tenga <see cref="ProtoboardConnector"/> para que
    /// el simulador del Reto 4 lo detecte al colocarse (lo registra en <see cref="Active"/>).
    /// Pensado para componentes spawneados en runtime (bandeja de entrega) cuyo prefab no trae
    /// el conector. Equivalente runtime de la herramienta de editor "Añadir Conectores".
    ///
    /// Idempotente. Ignora objetos sin <see cref="ElectricalComponent"/> y la
    /// <see cref="VoltageSource"/> (condición de frontera, no se engancha). Las patas leadA/leadB
    /// se auto-generan del bounding box.
    /// </summary>
    public static ProtoboardConnector EnsureOn(GameObject go)
    {
        if (go == null) return null;

        var comp = go.GetComponent<ElectricalComponent>()
                ?? go.GetComponentInChildren<ElectricalComponent>(true);
        if (comp == null) return null;          // no es un componente eléctrico
        if (comp is VoltageSource) return null; // la fuente no se engancha

        // El conector debe ir en el MISMO GO que el ElectricalComponent (RequireComponent).
        var existing = comp.GetComponent<ProtoboardConnector>();
        return existing != null ? existing : comp.gameObject.AddComponent<ProtoboardConnector>();
    }

    void Awake()
    {
        _comp = GetComponent<ElectricalComponent>();
        EnsureLeads();
    }

    void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
        _lastPos = transform.position;
        if (leadA != null) _lastLeadA = leadA.position;
        if (leadB != null) _lastLeadB = leadB.position;
        MarkSimDirty();   // re-simula al aparecer/colocarse
    }

    void OnDisable() { Active.Remove(this); }

    void Update()
    {
        // Re-engancha al mover el componente O cualquiera de sus patas. En el jumper flexible las
        // puntas se mueven solas (cada una se agarra por separado) y la raíz NO se mueve, así que
        // vigilar solo transform.position dejaría de re-conectar al recolocar una punta.
        bool moved = Moved(ref _lastPos, transform.position);
        if (leadA != null) moved |= Moved(ref _lastLeadA, leadA.position);
        if (leadB != null) moved |= Moved(ref _lastLeadB, leadB.position);
        if (moved) MarkSimDirty();
    }

    static bool Moved(ref Vector3 last, Vector3 now)
    {
        if ((now - last).sqrMagnitude > 1e-6f) { last = now; return true; }   // > 1 mm
        return false;
    }

    void MarkSimDirty()
    {
        if (_sim == null) _sim = FindAnyObjectByType<ProtoboardSimulator>();
        if (_sim != null) _sim.MarkDirty();
    }

    // ─────────────────────────────────────────────
    //  Enganche (llamado por ProtoboardSimulator)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Asigna nodeA/nodeB del componente al nodo más cercano a cada pata dentro de snapRadius.
    /// Una pata sin nodo cercano queda en null → ese extremo flota (circuito abierto), que es
    /// el comportamiento físico correcto.
    /// </summary>
    public static bool DebugBind = true;   // diagnóstico: loguea (al cambiar) la distancia real al punto más cercano

    /// <summary>Span MÍNIMO (m) entre las patas auto-creadas. Si el bounding box del componente
    /// es más corto, las patas se separan a esta distancia para que cada una alcance un hueco
    /// distinto del protoboard. 0 = usar solo el bounding box. Lo setea Reto4BreadboardMode.</summary>
    public static float MinLeadSpan = 0f;

    ElectricalNode _lastA, _lastB;

    public void Bind(List<ConnectionPoint> points)
    {
        if (_comp == null) return;

        // Modo DETERMINISTA: los nodos se fijan por railId, no por posición física. Inmune al
        // escalado de zona (ZoneProximityScaler) y a la calibración VR — la causa por la que el
        // snapping físico enganchaba ambas patas al mismo nodo (LED en corto → nunca encendía).
        if (lockNodes)
        {
            if (_sim == null) _sim = GetComponentInParent<ProtoboardSimulator>() ?? FindAnyObjectByType<ProtoboardSimulator>();
            if (_sim != null)
            {
                if (!string.IsNullOrEmpty(lockRailA)) { var na = _sim.NodeForRail(lockRailA); if (na != null) _comp.nodeA = na; }
                if (!string.IsNullOrEmpty(lockRailB)) { var nb = _sim.NodeForRail(lockRailB); if (nb != null) _comp.nodeB = nb; }
            }
            return;
        }

        if (leadA == null || leadB == null) return;
        _comp.nodeA = Nearest(leadA.position, points, out float dA, out var nearA);
        _comp.nodeB = Nearest(leadB.position, points, out float dB, out var nearB);

        if (DebugBind && (_comp.nodeA != _lastA || _comp.nodeB != _lastB))
        {
            _lastA = _comp.nodeA; _lastB = _comp.nodeB;
            Debug.Log($"[Connector '{name}'] puntos={points.Count} | " +
                      $"LeadA {LeadDesc(_comp.nodeA, nearA, dA)} | " +
                      $"LeadB {LeadDesc(_comp.nodeB, nearB, dB)} | " +
                      $"snapRadius={snapRadius*100f:0.#}cm");
        }
    }

    static string LeadDesc(ElectricalNode conectado, ElectricalNode cercano, float dist)
        => conectado != null
            ? $"→ {conectado.name} ({dist*100f:0.#}cm)"
            : $"libre (más cerca: {(cercano != null ? cercano.name : "?")} {dist*100f:0.#}cm)";

    /// <summary>
    /// Asigna patas externas (p.ej. Probe_A / Probe_B de un cable jumper) y descarta las
    /// auto-creadas por <see cref="EnsureLeads"/> si las hubiera. Llamar tras AddComponent.
    /// </summary>
    public void SetLeads(Transform a, Transform b)
    {
        if (a != null) ReplaceLead(ref leadA, a);
        if (b != null) ReplaceLead(ref leadB, b);
    }

    /// <summary>Fija el componente a dos rieles por railId (modo determinista). El siguiente Bind
    /// asigna nodeA=NodeForRail(railA) y nodeB=NodeForRail(railB), ignorando la posición física.
    /// En un LED, railA es el ÁNODO y railB el CÁTODO.
    ///
    /// <paramref name="sim"/>: simulador destino. Pásalo SIEMPRE cuando el componente NO cuelga del
    /// board (p.ej. un LED de reemplazo en la bandeja) — así no dependemos de FindAnyObjectByType,
    /// que es ambiguo con 2 protoboards en escena (Reto 2 y Reto 4).</summary>
    public void LockToRails(string railA, string railB, ProtoboardSimulator sim = null)
    {
        lockNodes = true;
        lockRailA = railA;
        lockRailB = railB;
        if (sim != null) _sim = sim;
        else if (_sim == null) _sim = GetComponentInParent<ProtoboardSimulator>() ?? FindAnyObjectByType<ProtoboardSimulator>();
        _sim?.MarkDirty();
    }

    void ReplaceLead(ref Transform slot, Transform newLead)
    {
        if (slot != null && slot != newLead && slot.parent == transform &&
            (slot.name == "LeadA" || slot.name == "LeadB"))
            Destroy(slot.gameObject);   // limpia la auto-creada
        slot = newLead;
    }

    ElectricalNode Nearest(Vector3 p, List<ConnectionPoint> points, out float nearestDist, out ElectricalNode nearestNode)
    {
        ElectricalNode best = null;
        ElectricalNode near = null;
        float bestSqr = snapRadius * snapRadius;
        float minSqr  = float.MaxValue;          // distancia al más cercano (aunque esté fuera de radio)
        foreach (var cp in points)
        {
            if (cp.node == null) continue;
            float d = (cp.position - p).sqrMagnitude;
            if (d < minSqr) { minSqr = d; near = cp.node; }
            if (d <= bestSqr) { bestSqr = d; best = cp.node; }
        }
        nearestDist = minSqr == float.MaxValue ? -1f : Mathf.Sqrt(minSqr);
        nearestNode = near;
        return best;
    }

    // ─────────────────────────────────────────────
    //  Auto-creación de patas
    // ─────────────────────────────────────────────

    void EnsureLeads()
    {
        if (leadA != null && leadB != null) return;

        var rend = GetComponentInChildren<Renderer>();
        Vector3 center = rend != null ? rend.bounds.center  : transform.position;
        Vector3 ext    = rend != null ? rend.bounds.extents : Vector3.one * 0.01f;

        // Eje mundial más largo del bounding box = orientación de las patas
        Vector3 dir = Vector3.right; float m = ext.x;
        if (ext.y > m) { dir = Vector3.up;      m = ext.y; }
        if (ext.z > m) { dir = Vector3.forward; m = ext.z; }

        // Garantizar un span mínimo para que cada pata alcance un hueco distinto (modo protoboard).
        if (MinLeadSpan > 0f) m = Mathf.Max(m, MinLeadSpan * 0.5f);

        if (leadA == null) leadA = MakeLead("LeadA", center + dir * m);
        if (leadB == null) leadB = MakeLead("LeadB", center - dir * m);
    }

    Transform MakeLead(string n, Vector3 worldPos)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, true);  // worldPositionStays → sigue al componente
        go.transform.position = worldPos;
        return go.transform;
    }

    void OnDrawGizmosSelected()
    {
        if (leadA != null) { Gizmos.color = Color.red;   Gizmos.DrawWireSphere(leadA.position, snapRadius); }
        if (leadB != null) { Gizmos.color = Color.black; Gizmos.DrawWireSphere(leadB.position, snapRadius); }
    }
}

/// <summary>Punto de conexión (posición + nodo eléctrico) que el conector puede enganchar.</summary>
public struct ConnectionPoint
{
    public Vector3        position;
    public ElectricalNode node;
    public ConnectionPoint(Vector3 position, ElectricalNode node) { this.position = position; this.node = node; }
}
