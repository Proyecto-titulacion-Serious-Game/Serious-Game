using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using HPhysic;

/// <summary>
/// Puente XR ↔ sistema de cables físicos (Cable-physics de Hrober0).
/// Va en el GameObject de un <see cref="Connector"/> (la punta del cable). Añade un
/// <see cref="XRGrabInteractable"/> para poder agarrar la punta con los mandos de la Quest:
///   • Al AGARRAR  → desconecta (si estaba enchufado) para poder moverlo.
///   • Al SOLTAR   → busca el Connector COMPATIBLE más cercano (una hembra de slot) dentro de
///                   <see cref="connectRadius"/> y, si <see cref="Connector.CanConnect"/>, lo enchufa.
///
/// La librería NO trae XR (usa su propio Liftable/IObjectHolder de teclado/mouse); esto lo reemplaza.
/// </summary>
[RequireComponent(typeof(Connector))]
public class XRCableConnector : MonoBehaviour
{
    [Tooltip("Radio (m) para buscar una hembra al soltar la punta.")]
    public float connectRadius = 0.05f;

    private Connector _con;
    private XRGrabInteractable _grab;

    void Awake()
    {
        _con  = GetComponent<Connector>();
        _grab = GetComponent<XRGrabInteractable>();
        if (_grab == null) _grab = gameObject.AddComponent<XRGrabInteractable>();
        // Kinematic como los agarrables que SÍ funcionan (Delivered_*): la mano mueve la punta y
        // el resto del cable la sigue por los springs.
        _grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        _grab.throwOnDetach = false;

        // CLAVE: el NearFarInteractor sólo detecta la CAPA FÍSICA 0 (Default) para agarrar
        // (m_PhysicsLayerMask.m_Bits=1). Los conectores del cable vienen en la capa 6 → no los ve.
        // Los pasamos (y sus colliders hijos) a la capa 0 para que se puedan agarrar.
        SetLayerRecursive(gameObject, 0);
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    void OnEnable()
    {
        _grab.selectEntered.AddListener(OnGrab);
        _grab.selectExited.AddListener(OnRelease);
    }

    void OnDisable()
    {
        _grab.selectEntered.RemoveListener(OnGrab);
        _grab.selectExited.RemoveListener(OnRelease);
    }

    void OnGrab(SelectEnterEventArgs _)
    {
        if (_con.IsConnected) _con.Disconnect();
    }

    void OnRelease(SelectExitEventArgs _)
    {
        // Buscar la hembra compatible más cercana y enchufar.
        Connector best = null;
        float bestDist = connectRadius;
        foreach (var c in FindObjectsByType<Connector>(FindObjectsSortMode.None))
        {
            if (c == null || c == _con) continue;
            if (!_con.CanConnect(c)) continue;   // exige Male↔Female, libres, color (o allowDifrent)
            float d = Vector3.Distance(_con.ConnectionPosition, c.ConnectionPosition);
            if (d < bestDist) { bestDist = d; best = c; }
        }
        if (best != null) best.Connect(_con);
    }
}
