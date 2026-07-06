using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRGrabInteractable

/// <summary>
/// Convierte el GO "box" del Explorador en un RECIPIENTE FIJO y HUECO donde caen los
/// componentes enviados por el Técnico, para que el Explorador meta la mano y los agarre
/// (física suelta dentro de la caja).
///
/// Hace, en Awake/Start (runtime y builds):
///  1) Fija la caja: Rigidbody kinematic + sin gravedad, y DESACTIVA su XRGrabInteractable
///     (la caja deja de ser agarrable; es un recipiente estable).
///  2) Construye paredes huecas: a partir del BoxCollider sólido original crea piso + 4
///     paredes (tapa abierta) para que los componentes descansen DENTRO sin ser expulsados.
///  3) Crea un punto de entrega interior (cerca del piso).
///  4) UNIFICA bandejas: reapunta el puntoDeEntrega de TODOS los ExplorerComponentReceiver
///     y ComponentDeliverySystem a ese punto interior, pone modoBandejaHibrida=false
///     (física suelta) y limpia los slots por tipo. Opcionalmente oculta el visual de las
///     bandejas redundantes (que no son esta caja).
///
/// Idempotente: si ya configuró la caja, no la duplica.
/// </summary>
[DisallowMultipleComponent]
public class DeliveryBoxRecipient : MonoBehaviour
{
    [Header("Geometría del recipiente (espacio LOCAL de la caja)")]
    [Tooltip("Grosor de piso y paredes en unidades locales de la caja.")]
    public float grosorPared = 0.08f;

    [Tooltip("Altura del punto de spawn por encima del piso interior (local).")]
    public float alturaSpawnSobrePiso = 0.15f;

    [Header("Unificación de bandejas")]
    [Tooltip("Reapunta TODOS los receivers/delivery a esta caja y pone física suelta.")]
    public bool unificarBandejas = true;

    [Tooltip("Oculta el MeshRenderer de las bandejas/tray que NO son esta caja " +
             "(deja una sola bandeja visible). No las destruye: solo apaga su visual.")]
    public bool ocultarVisualBandejasRedundantes = true;

    [Tooltip("Mueve el indicador holográfico 'MATERIALES RECIBIDOS' (DeliveryTrayIndicator) " +
             "a la boca de la caja, para que la caja herede ese feedback de la bandeja vieja.")]
    public bool migrarIndicadorHolografico = true;

    Transform _interior;            // punto de entrega interior
    bool _configurada;

    void Awake() => Configurar();

    void Start()
    {
        // Reintento: algunos receivers/delivery se auto-cablean en su propio Awake;
        // re-aplicar en Start garantiza que ganamos la última palabra.
        if (unificarBandejas) ReapuntarBandejas();
    }

    [ContextMenu("Configurar ahora")]
    void Configurar()
    {
        if (_configurada) return;

        FijarCaja();
        ConstruirParedesHuecas();
        CrearPuntoInterior();
        if (unificarBandejas) ReapuntarBandejas();
        if (ocultarVisualBandejasRedundantes) OcultarBandejasRedundantes();
        if (migrarIndicadorHolografico) MigrarIndicador();

        _configurada = true;
        Debug.Log("[DeliveryBox] Caja configurada como recipiente fijo hueco. Punto interior listo.", this);
    }

    // ───────────────────────────── 1) Caja fija ─────────────────────────────
    void FijarCaja()
    {
        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = true;
            rb.useGravity  = false;
        }
        // La caja deja de ser agarrable (recipiente estable). Desactivamos en vez de borrar
        // para que sea reversible desde el Inspector.
        foreach (var grab in GetComponents<XRGrabInteractable>())
            grab.enabled = false;
    }

    // ─────────────────────────── 2) Paredes huecas ──────────────────────────
    void ConstruirParedesHuecas()
    {
        var solido = GetComponent<BoxCollider>();
        if (solido == null)
        {
            Debug.LogWarning("[DeliveryBox] No hay BoxCollider en la caja; no puedo derivar el hueco.", this);
            return;
        }

        Vector3 s    = solido.size;
        Vector3 c    = solido.center;
        Vector3 half = s * 0.5f;
        float   t    = grosorPared;

        // Desactivamos el collider sólido (lo reemplazan las paredes). Reversible.
        solido.enabled = false;

        var paredesRoot = transform.Find("Recipiente_Paredes");
        if (paredesRoot != null) return; // ya construido

        var root = new GameObject("Recipiente_Paredes").transform;
        root.SetParent(transform, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale    = Vector3.one;

        // Piso + 4 paredes (sin tapa = top abierto)
        AddWall(root, "Piso",   new Vector3(c.x,            c.y - half.y + t * 0.5f, c.z),            new Vector3(s.x, t,   s.z));
        AddWall(root, "Pared+X", new Vector3(c.x + half.x - t * 0.5f, c.y,           c.z),            new Vector3(t,   s.y, s.z));
        AddWall(root, "Pared-X", new Vector3(c.x - half.x + t * 0.5f, c.y,           c.z),            new Vector3(t,   s.y, s.z));
        AddWall(root, "Pared+Z", new Vector3(c.x,            c.y,                     c.z + half.z - t * 0.5f), new Vector3(s.x, s.y, t));
        AddWall(root, "Pared-Z", new Vector3(c.x,            c.y,                     c.z - half.z + t * 0.5f), new Vector3(s.x, s.y, t));
    }

    void AddWall(Transform parent, string nombre, Vector3 center, Vector3 size)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(parent, false);
        go.layer = gameObject.layer;
        var bc = go.AddComponent<BoxCollider>();
        bc.center    = center;
        bc.size      = size;
        bc.isTrigger = false; // sólidas: contienen los componentes
    }

    // ─────────────────────────── 3) Punto interior ──────────────────────────
    void CrearPuntoInterior()
    {
        var solido = GetComponent<BoxCollider>();
        Vector3 c    = solido != null ? solido.center : Vector3.zero;
        Vector3 half = solido != null ? solido.size * 0.5f : Vector3.one * 0.5f;

        var existente = transform.Find("PuntoEntrega_Interior");
        _interior = existente != null ? existente : new GameObject("PuntoEntrega_Interior").transform;
        _interior.SetParent(transform, false);
        // Cerca del piso, centrado: los componentes spawnean aquí (+offset) y caen dentro.
        _interior.localPosition = new Vector3(c.x, c.y - half.y + alturaSpawnSobrePiso, c.z);
        _interior.localRotation = Quaternion.identity;
        // Escala mundial uniforme requerida por el modo de spawn — la caja tiene escala uniforme (0.2).
    }

    // ─────────────────────── 4) Unificar bandejas ───────────────────────────
    void ReapuntarBandejas()
    {
        if (_interior == null) CrearPuntoInterior();

        int receivers = 0, deliveries = 0;

        foreach (var rec in FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include))
        {
            rec.puntoDeEntrega     = _interior;
            rec.modoBandejaHibrida = false;        // física suelta dentro de la caja
            rec.slotResistor       = null;
            rec.slotLED            = null;
            rec.slotCapacitor      = null;
            rec.slotArduinoPin     = null;
            receivers++;
        }

        foreach (var del in FindObjectsByType<ComponentDeliverySystem>(FindObjectsInactive.Include))
        {
            del.puntoDeEntrega = _interior;
            deliveries++;
        }

        Debug.Log($"[DeliveryBox] Bandejas unificadas → punto interior. " +
                  $"Receivers reapuntados: {receivers}, DeliverySystems: {deliveries}.", this);
    }

    void OcultarBandejasRedundantes()
    {
        // Oculta el visual de cualquier GO cuyo nombre sugiera 'bandeja/tray' y NO sea
        // esta caja ni sus hijos. No destruye nada (solo apaga MeshRenderers).
        int ocultos = 0;
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            var t = mr.transform;
            if (t == transform || t.IsChildOf(transform)) continue;

            string n = t.name.ToLowerInvariant();
            bool esBandeja = n.Contains("bandeja") || n.Contains("tray");
            if (!esBandeja) continue;

            mr.enabled = false;
            ocultos++;
            Debug.Log($"[DeliveryBox] Visual de bandeja redundante oculto: '{t.name}'.", t);
        }
        if (ocultos == 0)
            Debug.Log("[DeliveryBox] No se encontraron bandejas visuales redundantes para ocultar.", this);
    }

    // ─────────── 5) Migrar indicador holográfico a la boca de la caja ───────────
    void MigrarIndicador()
    {
        var indicadores = FindObjectsByType<DeliveryTrayIndicator>(FindObjectsInactive.Include);
        if (indicadores.Length == 0)
        {
            Debug.Log("[DeliveryBox] No hay DeliveryTrayIndicator en escena para migrar.", this);
            return;
        }

        // Posición = boca de la caja (centro del borde superior del collider, en mundo).
        var col = GetComponent<BoxCollider>();
        Vector3 topLocal = (col != null ? col.center : Vector3.zero)
                         + new Vector3(0f, (col != null ? col.size.y : 1f) * 0.5f + 0.01f, 0f);
        Vector3 worldPos = transform.TransformPoint(topLocal);

        var ind = indicadores[0];
        ind.transform.SetPositionAndRotation(worldPos, transform.rotation);
        ind.gameObject.SetActive(true);

        // Re-encender su visual por si OcultarBandejasRedundantes apagó algún renderer.
        foreach (var mr in ind.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = true;
        foreach (var lr in ind.GetComponentsInChildren<LineRenderer>(true))  lr.enabled = true;

        // Si hubiera más de un indicador, dejar solo uno.
        for (int i = 1; i < indicadores.Length; i++)
            indicadores[i].gameObject.SetActive(false);

        Debug.Log($"[DeliveryBox] Indicador holográfico '{ind.name}' movido a la boca de la caja.", ind);
    }

    /// <summary>Punto de entrega interior (para que otros sistemas lo consulten).</summary>
    public Transform PuntoInterior => _interior;
}
