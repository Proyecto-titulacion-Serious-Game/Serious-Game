using UnityEngine;
using HPhysic;

/// <summary>
/// ETAPA 3 — puente ELÉCTRICO del sistema de cables físicos.
/// Va en el root de un CableFisico. Vigila sus dos conectores (Start/End = machos). Cuando AMBOS
/// están enchufados a hembras con <see cref="ContactNode"/>, crea un <see cref="Jumper"/> (~0Ω) entre
/// los dos nodos → el <see cref="ProtoboardSimulator"/> (MNA) cierra el circuito y transmite corriente.
/// Al desenchufar cualquiera de las puntas, elimina el Jumper.
///
/// El Jumper se crea como HIJO del ProtoboardSimulator para que <c>AllSandboxComponents()</c> lo incluya.
/// </summary>
public class CableElectricalBridge : MonoBehaviour
{
    [Tooltip("Los dos conectores macho del cable (Start/End). Si están vacíos se auto-detectan.")]
    public Connector start;
    public Connector end;

    [Tooltip("Cada cuánto revisa el estado de conexión (s).")]
    public float pollInterval = 0.25f;

    private Jumper _jumper;
    private ProtoboardSimulator _sim;
    private float _t;

    void Awake()
    {
        if (start == null || end == null)
        {
            var cons = GetComponentsInChildren<Connector>(true);
            if (cons.Length >= 1) start = cons[0];
            if (cons.Length >= 2) end   = cons[1];
        }
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < pollInterval) return;
        _t = 0f;
        Evaluate();
    }

    void Evaluate()
    {
        ElectricalNode a = NodeOf(start);
        ElectricalNode b = NodeOf(end);

        if (a != null && b != null && a != b) EnsureJumper(a, b);
        else RemoveJumper();
    }

    /// <summary>Nodo de la hembra donde está enchufada esta punta (o null).</summary>
    static ElectricalNode NodeOf(Connector c)
    {
        if (c == null || c.ConnectedTo == null) return null;
        var cn = c.ConnectedTo.GetComponent<ContactNode>()
              ?? c.ConnectedTo.GetComponentInParent<ContactNode>();
        return cn != null ? cn.Resolve() : null;
    }

    void EnsureJumper(ElectricalNode a, ElectricalNode b)
    {
        // Resolver el ProtoboardSimulator DESDE los nodos conectados (su slot/borne cuelga del board
        // que tiene el sim). Hay 2 sims en la escena (Reto 2 y Reto 4); FindAnyObjectByType global
        // podía colgar el Jumper del sim equivocado → la corriente no llegaba. Reutilizable en Reto 4.
        var sim = (a != null ? a.GetComponentInParent<ProtoboardSimulator>() : null)
               ?? (b != null ? b.GetComponentInParent<ProtoboardSimulator>() : null)
               ?? _sim ?? FindAnyObjectByType<ProtoboardSimulator>();
        _sim = sim;

        if (_jumper == null)
        {
            var go = new GameObject($"CableJumper_{name}");
            if (_sim != null) go.transform.SetParent(_sim.transform, false);
            _jumper = go.AddComponent<Jumper>();
            _jumper.resistance = 0.01f;
        }

        if (_jumper.nodeA != a || _jumper.nodeB != b)
        {
            _jumper.nodeA = a;
            _jumper.nodeB = b;
            if (_sim != null) _sim.MarkDirty();
        }
    }

    void RemoveJumper()
    {
        if (_jumper == null) return;
        Destroy(_jumper.gameObject);
        _jumper = null;
        if (_sim != null) _sim.MarkDirty();
    }

    void OnDisable() => RemoveJumper();
}
