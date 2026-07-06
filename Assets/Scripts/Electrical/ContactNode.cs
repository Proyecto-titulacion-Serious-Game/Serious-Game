using UnityEngine;

/// <summary>
/// Marca el <see cref="ElectricalNode"/> que representa una hembra de cable (CableContactFemale).
/// - Hembras de SLOT: dejan <see cref="explicitNode"/> en null → el nodo se resuelve del
///   <see cref="ProtoboardSlot"/> padre (su <c>assignedNode</c>, que fija BuildNodeMap por railId).
/// - Bornes de BATERÍA: se les pone un <see cref="explicitNode"/> propio (nodo del borne + o −).
///
/// Lo usa <see cref="CableElectricalBridge"/> para saber qué nodos unir cuando un cable se enchufa.
/// </summary>
public class ContactNode : MonoBehaviour
{
    [Tooltip("Nodo explícito (bornes de batería). Si es null, se resuelve por el ProtoboardSlot padre.")]
    public ElectricalNode explicitNode;

    /// <summary>Devuelve el nodo eléctrico de esta hembra (o null si aún no hay).</summary>
    public ElectricalNode Resolve()
    {
        if (explicitNode != null) return explicitNode;
        var slot = GetComponentInParent<ProtoboardSlot>();
        return slot != null ? slot.assignedNode : null;
    }
}
