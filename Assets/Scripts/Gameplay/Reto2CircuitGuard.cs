using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Guardián de runtime del circuito del Reto 2 (protoboard libre). NO EDITA LA ESCENA ni mueve /
/// reescala / rota ninguna pieza — es 100% no destructivo. Se auto-instancia con <see cref="Bootstrap"/>.
///
/// Responsabilidades (solo mientras el reto actual es <see cref="LevelType.Parallel"/>):
///   1. Apaga el <see cref="CircuitManager"/> VIEJO de Reto2_Zone (motor de piezas fijas) para que
///      no pelee con el <see cref="ProtoboardSimulator"/> (MNA), que es el que ahora conduce.
///   2. Cuando el Técnico envía un LED de reemplazo, al SOLTARLO lo cablea a la MISMA rama donde
///      estaba el LED dañado (lee los rieles reales de ESE LED — respeta tu diseño, sin columnas
///      hardcodeadas), con polaridad correcta, y desactiva el dañado.
///
/// El resto de las piezas del circuito (las que ya colocaste/calibraste) conservan su
/// ProtoboardConnector físico: se cablean por su posición real. Este guard NO toca sus transforms.
/// El LED enciende sólo cuando el circuito está realmente cerrado (cables de batería a los rieles +
/// el jumper que cierra la rama + polaridad correcta) — lo decide el MNA, no este script.
/// </summary>
[DisallowMultipleComponent]
public class Reto2CircuitGuard : MonoBehaviour
{
    const string Reto2ZoneName = "Reto2_Zone";
    const string BoardName     = "Protoboard_Reto2";

    public static Reto2CircuitGuard Instance { get; private set; }

    ProtoboardSimulator _sim;
    bool _activo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("Reto2CircuitGuard");
        Instance = go.AddComponent<Reto2CircuitGuard>();
    }

    void OnEnable()  => GameManager.OnLevelLoaded += OnLevel;
    void OnDisable() => GameManager.OnLevelLoaded -= OnLevel;

    void Start()
    {
        // Por si arrancamos ya dentro del Reto 2 (F4 / prueba directa).
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && gm.currentLevel == LevelType.Parallel) Activar();
    }

    void OnLevel(LevelType lvl)
    {
        if (lvl == LevelType.Parallel) Activar();
        else _activo = false;
    }

    void Activar()
    {
        _activo = true;
        LocalizarSim();
        ApagarCircuitManagerViejo();
        _sim?.MarkDirty();

        // Forzar el LED dañado INVERTIDO tras un instante (después de los reseteos de polaridad de
        // otros sistemas al cargar el nivel) → el "dañado" es robusto y exige reemplazo.
        CancelInvoke(nameof(ForzarLedDanado));
        Invoke(nameof(ForzarLedDanado), 0.4f);

        // Enviar resumen al clipboard del Técnico cada 2s (solo si cambió) → ambos saben qué falta.
        CancelInvoke(nameof(EnviarResumenTecnico));
        InvokeRepeating(nameof(EnviarResumenTecnico), 1.2f, 2f);
    }

    string _ultimoResumen;

    /// <summary>Construye un RESUMEN corto del estado del circuito (Reto 2) y lo publica al Técnico por red
    /// (GameSession). Es un resumen 2D (LEDs + qué falta), no el circuito — respeta la asimetría.</summary>
    void EnviarResumenTecnico()
    {
        if (_sim == null) return;

        int on = 0, total = 0;
        bool danadoPresente = false;
        foreach (var led in _sim.GetComponentsInChildren<LED>(true))
        {
            if (led == null || !led.gameObject.activeInHierarchy) continue;
            if (led.nodeA == null || led.nodeB == null) continue;
            total++;
            if (led.isOn) on++;
            if (EsNombreDanado(led.name) && led.polarityInverted) danadoPresente = true;
        }
        // Resumen SIMPLE para el Técnico: estado + una acción.
        string r;
        if (total > 0 && on == total)
            r = $"LEDs: {on}/{total} ✅\nCircuito completo.";
        else if (danadoPresente)
            r = $"LEDs: {on}/{total}\nFalta: reemplazar el LED dañado.";
        else if (on == 0)
            r = $"LEDs: {on}/{total}\nFalta: completar el cableado.";
        else
            r = $"LEDs: {on}/{total}\nCasi — revisa la rama apagada.";

        if (r == _ultimoResumen) return;   // no reenviar si no cambió
        _ultimoResumen = r;
        GameSession.ReportarDiagnosticoReto(2, r);
    }

    /// <summary>Fuerza el LED dañado (por nombre) a polaridad invertida → no enciende hasta reemplazarlo.
    /// Robusto: corre tras los reseteos de polaridad de otros sistemas al cargar el nivel.</summary>
    void ForzarLedDanado()
    {
        if (_sim == null) LocalizarSim();
        if (_sim == null) return;
        AsegurarNodosFijados();   // primero garantiza que las piezas locked tengan nodos
        foreach (var led in _sim.GetComponentsInChildren<LED>(true))
            if (led != null && led.gameObject.activeSelf && EsNombreDanado(led.name) && !led.polarityInverted)
            {
                led.polarityInverted = true;
                _sim.MarkDirty();
                Debug.Log($"[Reto2CircuitGuard] LED dañado forzado invertido: {led.name} (exige reemplazo).");
            }
    }

    /// <summary>Asigna los nodos de las piezas LOCKED (por railId) usando el simulador CORRECTO del
    /// Reto 2. Necesario porque el Bind del conector puede resolver el sim equivocado (hay 2 en escena)
    /// y dejar los nodos en null. Bind solo asigna cuando el nodo NO es null, así que esto no se pisa.</summary>
    void AsegurarNodosFijados()
    {
        if (_sim == null) return;
        if (_sim.todosLosSlots != null) _sim.todosLosSlots.RemoveAll(s => s == null);   // quitar entradas destruidas
        foreach (var comp in _sim.GetComponentsInChildren<ElectricalComponent>(true))
        {
            if (comp == null || comp is VoltageSource) continue;
            var conn = comp.GetComponent<ProtoboardConnector>();
            if (conn == null || !conn.lockNodes) continue;
            if (!string.IsNullOrEmpty(conn.lockRailA)) { var na = _sim.NodeForRail(conn.lockRailA); if (na != null) comp.nodeA = na; }
            if (!string.IsNullOrEmpty(conn.lockRailB)) { var nb = _sim.NodeForRail(conn.lockRailB); if (nb != null) comp.nodeB = nb; }
        }
        _sim.ForzarValidacion();   // SÍNCRONO: resuelve el MNA ya (para que isOn/voltajes queden frescos)
    }


    // ─────────────────────────────────────────────
    //  Reemplazo del LED dañado (lo llama ExplorerComponentReceiver al entregar un LED)
    // ─────────────────────────────────────────────

    /// <summary>El Técnico envió un LED. Solo relevante en Reto 2: al soltarlo, se cablea a la rama dañada.</summary>
    public static void NotifyLedDelivered(GameObject ledGO)
    {
        Debug.Log($"[Reto2CircuitGuard] NotifyLedDelivered LED='{(ledGO != null ? ledGO.name : "null")}' " +
                  $"instance={Instance != null} activo={(Instance != null && Instance._activo)}");
        if (Instance == null || !Instance._activo || ledGO == null) return;
        Instance.EngancharReemplazo(ledGO);
    }

    // Tiempo (sin sostener el LED) tras el cual la red de seguridad reemplaza solo, por si el evento
    // de soltar (selectExited) no dispara en VR. Ver EngancharReemplazo (opción A + B).
    const float FallbackReemplazoSegundos = 12f;

    class ReemplazoPendiente { public GameObject ledGO; public bool cableado; }

    void EngancharReemplazo(GameObject ledGO)
    {
        var e = new ReemplazoPendiente { ledGO = ledGO };
        var grab = ledGO.GetComponentInChildren<XRGrabInteractable>(true);
        if (grab != null)
        {
            // (A) Al SOLTAR el LED se cablea de inmediato.
            grab.selectExited.AddListener(_ => IntentarCablear(e));
            // (B) Red de seguridad: si el drop XR no dispara en VR, se cablea igual tras un rato
            //     (solo si NO lo está sosteniendo, para no quitárselo de la mano).
            StartCoroutine(RedDeSeguridadReemplazo(e, grab));
        }
        else
        {
            IntentarCablear(e);   // sin agarre (offline / test) → cablear de una
        }
    }

    void IntentarCablear(ReemplazoPendiente e)
    {
        if (e == null || e.cableado) return;
        e.cableado = true;
        CablearEnRamaDanada(e.ledGO);
    }

    System.Collections.IEnumerator RedDeSeguridadReemplazo(ReemplazoPendiente e, XRGrabInteractable grab)
    {
        float t = 0f;
        var wait = new WaitForSeconds(0.5f);
        while (t < FallbackReemplazoSegundos)
        {
            if (e == null || e.cableado) yield break;   // ya se cableó (soltó el LED)
            yield return wait;
            bool sostenido = grab != null && grab.isSelected;   // dale chance de colocarlo él mismo
            t = sostenido ? 0f : t + 0.5f;
        }
        if (e != null && !e.cableado)
        {
            Debug.Log("[Reto2CircuitGuard] Red de seguridad: reemplazo automático del LED dañado (el 'soltar' no disparó).");
            IntentarCablear(e);
        }
    }

    void CablearEnRamaDanada(GameObject ledGO)
    {
        if (ledGO == null) return;
        if (_sim == null) LocalizarSim();
        if (_sim == null) { Debug.LogWarning("[Reto2CircuitGuard] No hay ProtoboardSimulator del Reto 2."); return; }

        var led = ledGO.GetComponent<LED>() ?? ledGO.GetComponentInChildren<LED>(true);
        if (led == null) { Debug.LogWarning("[Reto2CircuitGuard] El componente entregado no es un LED."); return; }

        // Localizar el LED DAÑADO y leer SUS rieles reales (respeta tu diseño; no hardcodeamos columnas).
        var danado = BuscarLedDanado(led);
        string railA = null, railB = null;
        if (danado != null)
        {
            RielesDe(danado.GetComponent<ProtoboardConnector>(), out railA, out railB);

            // Polaridad CORRECTA: cátodo (railB) hacia GND, ánodo (railA) hacia el otro riel.
            // (El LED conduce ánodo→cátodo; si lo dejamos al revés, no prende aunque haya energía.)
            if (EsGnd(railA) && !EsGnd(railB)) { (railA, railB) = (railB, railA); }

            // El nuevo hereda la posición/rotación/escala del dañado (queda "donde estaba"). Solo
            // movemos el LED NUEVO (no es parte del diseño calibrado); el dañado no se toca, se oculta.
            var t = danado.transform;
            ledGO.transform.SetPositionAndRotation(t.position, t.rotation);
            ledGO.transform.localScale = t.lossyScale;
            danado.gameObject.SetActive(false);
        }

        foreach (var rb in ledGO.GetComponentsInChildren<Rigidbody>(true))
            { rb.isKinematic = true; rb.useGravity = false; }

        // FIJAR el LED nuevo: ya no se puede volver a agarrar (queda "soldado" en la rama).
        foreach (var grab in ledGO.GetComponentsInChildren<XRGrabInteractable>(true))
            if (grab != null) grab.enabled = false;
        var gc = ledGO.GetComponentInChildren<GrabbableComponent>(true);
        if (gc != null) gc.DisableGrab();
        foreach (var col in ledGO.GetComponentsInChildren<Collider>(true))
            if (col != null && !col.isTrigger) col.enabled = false;   // que no lo "empuje" la mano

        led.polarityInverted = false;   // el LED nuevo va con polaridad correcta
        var conn = ProtoboardConnector.EnsureOn(led.gameObject) ?? led.GetComponent<ProtoboardConnector>();
        if (conn == null) conn = led.gameObject.AddComponent<ProtoboardConnector>();

        if (!string.IsNullOrEmpty(railA) && !string.IsNullOrEmpty(railB) && railA != railB)
        {
            // Cablear al mismo par de rieles que ocupaba el LED dañado (determinista, ánodo=railA).
            conn.LockToRails(railA, railB, _sim);
            // Explícito (robusto): asignar los nodos YA con el sim correcto, sin depender del Bind.
            led.nodeA = _sim.NodeForRail(railA);
            led.nodeB = _sim.NodeForRail(railB);
            Debug.Log($"[Reto2CircuitGuard] LED de reemplazo cableado a la rama dañada ({railA}→{railB}, " +
                      "polaridad correcta). Encenderá cuando el circuito esté cerrado (cables).");
        }
        else
        {
            // No pudimos leer los rieles del dañado → dejar el enganche FÍSICO por posición (el jugador
            // lo colocó donde el dañado). No hardcodeamos columnas para no romper tu diseño.
            _sim.MarkDirty();
            Debug.LogWarning("[Reto2CircuitGuard] No leí los rieles del LED dañado; el reemplazo queda " +
                             "con enganche físico por posición (revisa que sus patas caigan en los slots).");
            return;
        }

        _sim.ForzarValidacion();   // resuelve el MNA ya → el LED nuevo enciende si la rama está cerrada
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    void LocalizarSim()
    {
        foreach (var s in FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include))
        {
            if (s == null) continue;
            if (s.name == BoardName || TieneAncestro(s.transform, Reto2ZoneName)) { _sim = s; return; }
        }
    }

    /// <summary>El LED "dañado" bajo el board. FIABLE: primero por NOMBRE (…damaged/dañado/faulty),
    /// luego por polaridad invertida. Si no hay uno claro, devuelve null y NO se toca ningún LED
    /// (evita desactivar por error el LED bueno, que puede estar apagado mientras no hay energía).</summary>
    GameObject BuscarLedDanado(LED excluir)
    {
        var raiz = _sim != null ? _sim.transform : null;
        if (raiz == null) return null;
        var leds = raiz.GetComponentsInChildren<LED>(true);

        // 1) Por nombre (lo más fiable — el LED dañado se llama Circuit_LED2_damaged).
        foreach (var l in leds)
            if (l != null && l != excluir && l.gameObject.activeSelf && EsNombreDanado(l.name)) return l.gameObject;
        // 2) Por polaridad invertida (la falla clásica), si está marcada.
        foreach (var l in leds)
            if (l != null && l != excluir && l.gameObject.activeSelf && l.polarityInverted) return l.gameObject;
        // 3) Sin candidato claro → NO tocar ningún LED.
        Debug.LogWarning("[Reto2CircuitGuard] No identifiqué el LED dañado por nombre/polaridad; " +
                         "no desactivo ninguno (para no quitar el LED bueno). Renombra el dañado con 'damaged'.");
        return null;
    }

    static bool EsNombreDanado(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        n = n.ToLowerInvariant();
        return n.Contains("damaged") || n.Contains("dañad") || n.Contains("danad") || n.Contains("faulty");
    }

    static bool EsGnd(string railId) =>
        !string.IsNullOrEmpty(railId) && railId.ToUpperInvariant().Contains("GND");

    /// <summary>Rieles (ánodo/cátodo) de un conector: usa sus lockRail si están fijados; si no, los
    /// deduce del slot más cercano a cada pata (snapshot de la posición REAL — no mueve nada).</summary>
    void RielesDe(ProtoboardConnector conn, out string railA, out string railB)
    {
        railA = railB = null;
        if (conn == null) return;

        if (conn.lockNodes && !string.IsNullOrEmpty(conn.lockRailA) && !string.IsNullOrEmpty(conn.lockRailB))
        {
            railA = conn.lockRailA; railB = conn.lockRailB; return;
        }

        var slots = _sim != null ? _sim.GetComponentsInChildren<ProtoboardSlot>(true) : null;
        if (slots == null) return;
        if (conn.leadA != null) railA = RielMasCercano(conn.leadA.position, slots);
        if (conn.leadB != null) railB = RielMasCercano(conn.leadB.position, slots);
    }

    static string RielMasCercano(Vector3 p, ProtoboardSlot[] slots)
    {
        string mejor = null; float mejorSqr = float.MaxValue;
        foreach (var s in slots)
        {
            if (s == null) continue;
            float d = (s.transform.position - p).sqrMagnitude;
            if (d < mejorSqr) { mejorSqr = d; mejor = s.railId; }
        }
        return mejor;
    }

    void ApagarCircuitManagerViejo()
    {
        foreach (var cm in FindObjectsByType<CircuitManager>(FindObjectsInactive.Include))
        {
            if (cm == null || !cm.enabled) continue;
            // El CircuitManager viejo cuelga de Reto2_Zone pero NO del board del protoboard.
            if (!TieneAncestro(cm.transform, Reto2ZoneName)) continue;
            if (_sim != null && EsDescendiente(cm.transform, _sim.transform)) continue;
            cm.enabled = false;
            Debug.Log($"[Reto2CircuitGuard] CircuitManager viejo apagado en '{cm.name}' (manda el ProtoboardSimulator).");
        }
    }

    static bool TieneAncestro(Transform t, string nombre)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == nombre) return true;
        return false;
    }

    static bool EsDescendiente(Transform t, Transform ancestro)
    {
        for (var p = t; p != null; p = p.parent)
            if (p == ancestro) return true;
        return false;
    }
}
