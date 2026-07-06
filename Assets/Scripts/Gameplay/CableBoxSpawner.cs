using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Dispensador industrial de cables jumper para la mesa VR del Explorador.
/// Máquina de estados visual: Idle → Active → Warning → Full.
[RequireComponent(typeof(Collider))]
public class CableBoxSpawner : MonoBehaviour
{
    // ─────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────
    [Header("Cable")]
    public GameObject cablePrefab;
    public Vector3    spawnOffset      = new Vector3(0f, 0.07f, 0f);
    [Range(5, 50)]
    public int        maxActiveCables  = 20;
    [Range(0.1f, 1f)]
    [Tooltip("OBSOLETO desde el cable flexible (opción 3): el largo ya no es fijo, lo pone el jugador " +
             "moviendo cada punta por separado, así que esta escala ya NO se aplica. Se conserva solo para " +
             "no perder el valor serializado en escena. Ver MakeCableFlexible().")]
    public float      cableEscala      = 0.25f;

    [Header("Referencias visuales (asignadas por el tool)")]
    public Renderer  ledRenderer;
    public Renderer  buttonRenderer;
    public Transform buttonCap;
    public Transform gatePlate;
    public TMP_Text  counterText;

    [Header("Animación")]
    public float buttonPressDepth = 0.006f;
    public float buttonAnimTime   = 0.10f;
    public float gateAnimTime     = 0.35f;

    [Header("Dispensado")]
    [Tooltip("Tiempo mínimo entre cables al rozar la caja (evita spawnear muchos de golpe).")]
    public float spawnCooldown = 0.6f;
    [Tooltip("Si está activo, también detecta manos/controladores XR aunque NO tengan el tag " +
             "RightHand/LeftHand (por nombre del collider). Útil si las manos no están tagueadas.")]
    public bool  detectarManosPorNombre = true;

    [Header("Depuración")]
    [Tooltip("Loguea en consola CADA collider que entra al trigger (nombre + tag). " +
             "Actívalo si los cables no salen, para ver si la mano llega y con qué tag.")]
    public bool  logTriggers = false;

    float _lastSpawnTime = -10f;

    // ─────────────────────────────────────────
    //  Paleta de colores (cuerpo, puntaA, puntaB)
    // ─────────────────────────────────────────
    static readonly (Color body, Color probeA, Color probeB)[] Palettes =
    {
        (Hex("E63946"), Hex("FFD60A"), Hex("4361EE")),
        (Hex("2EC4B6"), Hex("FF9F1C"), Hex("CBFF8C")),
        (Hex("8338EC"), Hex("FB5607"), Hex("FFBE0B")),
        (Hex("06D6A0"), Hex("EF233C"), Hex("4895EF")),
        (Hex("F72585"), Hex("4CC9F0"), Hex("7209B7")),
        (Hex("FFB703"), Hex("023047"), Hex("FB8500")),
        (Hex("118AB2"), Hex("EF476F"), Hex("FFD166")),
        (Hex("43AA8B"), Hex("F94144"), Hex("F3722C")),
    };

    enum BoxState { Idle, Active, Warning, Full }

    static readonly Color LedGreen = new Color(0f,    0.78f, 0.39f);
    static readonly Color LedAmber = new Color(0.91f, 0.51f, 0f);
    static readonly Color LedRed   = new Color(0.80f, 0.13f, 0.13f);

    BoxState _state;
    int      _activeCables;
    int      _paletteIdx;
    bool     _gateOpen = true;

    MaterialPropertyBlock _mpbLed;
    MaterialPropertyBlock _mpbButton;

    Coroutine _buttonCo;
    Coroutine _gateCo;
    Vector3   _buttonRestPos;
    Vector3   _gateOpenPos;
    Vector3   _gateClosedPos;

    void Awake()
    {
        _mpbLed    = new MaterialPropertyBlock();
        _mpbButton = new MaterialPropertyBlock();

        // (El collider del box lo configura RepararInteraccionVR() en Start: NO-trigger para que el
        //  rayo/poke del control SÍ pueda seleccionarlo, + un trigger hijo aparte para el roce físico.)

        var xr = GetComponent<XRBaseInteractable>();
        if (xr != null)
        {
            xr.selectEntered.AddListener(_ => SpawnCable());
            xr.hoverEntered.AddListener(_ => OnHoverEnter());
            xr.hoverExited.AddListener(_  => OnHoverExit());
        }

        if (buttonCap != null) _buttonRestPos = buttonCap.localPosition;
        if (gatePlate  != null)
        {
            _gateOpenPos   = gatePlate.localPosition;
            _gateClosedPos = _gateOpenPos + new Vector3(0f, -0.015f, 0f);
        }
    }

    void Start()
    {
        RepararInteraccionVR();

        if (cablePrefab == null)
            Debug.LogWarning("[CableBoxSpawner] cablePrefab NO asignado en el Inspector — " +
                             "al rozar la caja no saldrá ningún cable. Asigna el prefab del jumper.", this);

        UpdateState();
        StartCoroutine(PulseLED());
    }

    /// <summary>
    /// Auto-reparación VR (runtime). El box no se podía seleccionar con el rayo del control porque
    /// (a) su BoxCollider estaba como TRIGGER y los rayos de XRI ignoran triggers, y (b) el
    /// XRSimpleInteractable tenía la lista de colliders VACÍA → auto-recogía el collider de un
    /// 'Cable_Jumper' hijo, robándoselo (ni se podía agarrar el cable ni seleccionar el box).
    /// Solución: cable(s) hijo des-emparentados, collider propio NO-trigger para seleccionar por
    /// rayo/poke, lista del interactable limitada al box, y un trigger aparte para el roce físico.
    /// </summary>
    void RepararInteraccionVR()
    {
        var xr = GetComponent<XRBaseInteractable>();

        // 0) DESREGISTRAR el interactable del box ANTES de tocar nada. Así el InteractionManager
        //    limpia el mapeo de TODOS sus colliders auto-recogidos (incluido el del cable robado);
        //    si modificáramos la lista con él aún registrado, ese mapeo quedaría fantasma.
        if (xr != null && xr.enabled) xr.enabled = false;

        // 1) Des-emparentar cualquier cable pre-colocado DENTRO del box (roba el collider y no se
        //    puede agarrar). Queda independiente; togglear su grab para que reclame su collider.
        foreach (var g in GetComponentsInChildren<XRGrabInteractable>(true))
            if (g.transform != transform)
            {
                g.transform.SetParent(null, true);
                EnsureMateriales(g.gameObject);   // también traía materiales null → se vería de un ojo
                if (g.enabled) { g.enabled = false; g.enabled = true; }
                Debug.Log($"[CableBoxSpawner] Cable '{g.name}' des-emparentado del box (ahora se puede agarrar).");
            }

        // 2) Collider PROPIO del box en NO-trigger → el rayo/poke del control SÍ lo puede seleccionar.
        Collider bodySel = null;
        foreach (var c in GetComponents<Collider>()) { c.isTrigger = false; bodySel = c; }
        if (bodySel == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.size      = new Vector3(0.12f, 0.1f, 0.08f);
            bodySel = box;
        }

        // 3) Lista del interactable = SOLO el collider propio, y re-registrar (enable).
        if (xr != null)
        {
            xr.colliders.Clear();
            xr.colliders.Add(bodySel);
            xr.enabled = true;   // OnEnable re-registra con la lista corregida
            Debug.Log("[CableBoxSpawner] Interacción VR reparada: apunta el rayo al box y pulsa select/grip para dispensar un cable.");
        }

        // 4) Trigger SEPARADO (hijo) para el roce físico (OnTriggerEnter), por si las manos llegan a
        //    tener collider. Sus eventos burbujean al Rigidbody raíz → OnTriggerEnter de este script.
        if (transform.Find("RoceTrigger") == null)
        {
            var t  = new GameObject("RoceTrigger");
            t.transform.SetParent(transform, false);
            var tc = t.AddComponent<BoxCollider>();
            tc.isTrigger = true;
            tc.size      = new Vector3(0.18f, 0.16f, 0.14f);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (logTriggers)
            Debug.Log($"[CableBoxSpawner] Trigger: entró '{other.name}' (tag='{other.tag}') " +
                      $"→ {(EsMano(other) ? "reconocido como MANO ✓" : "NO reconocido como mano ✗")}");

        if (!EsMano(other)) return;

        // Cooldown: rozar la caja con varios colliders (dedos) no debe escupir 10 cables.
        if (Time.time - _lastSpawnTime < spawnCooldown) return;
        _lastSpawnTime = Time.time;

        SpawnCable();
    }

    /// <summary>
    /// ¿El collider que entró es una mano del jugador? Primero por tag (RightHand/LeftHand);
    /// si <see cref="detectarManosPorNombre"/> está activo, también por nombre del collider
    /// o de algún padre (hand / controller / poke / direct / grab), para cubrir setups donde
    /// las manos no quedaron tagueadas.
    /// </summary>
    bool EsMano(Collider other)
    {
        if (other.CompareTag("RightHand") || other.CompareTag("LeftHand")) return true;
        if (!detectarManosPorNombre) return false;

        // Buscar la palabra clave en el collider o subiendo por la jerarquía.
        for (Transform t = other.transform; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("hand") || n.Contains("controller") ||
                n.Contains("poke") || n.Contains("direct") || n.Contains("grab"))
                return true;
        }
        return false;
    }

    void OnHoverEnter() => SetButtonEmission(LedGreen * 3.5f);
    void OnHoverExit()  => UpdateVisuals();

    public void SpawnCable()
    {
        if (cablePrefab == null) { Debug.LogWarning("[CableBoxSpawner] cablePrefab no asignado."); return; }
        if (_state == BoxState.Full) return;

        if (_buttonCo != null) StopCoroutine(_buttonCo);
        _buttonCo = StartCoroutine(AnimateButtonPress());

        Vector3 pos   = transform.position + transform.TransformDirection(spawnOffset);
        GameObject go = Instantiate(cablePrefab, pos, Random.rotation);

        EnsureMateriales(go);   // sin material → URP usa el shader de ERROR (magenta y se ve en UN solo ojo en VR)
        ApplyCableColors(go, _paletteIdx);
        _paletteIdx = (_paletteIdx + 1) % Palettes.Length;

        MakeCableElectrical(go);
        MakeCableFlexible(go);   // jumper de puntas independientes (opción 3): cada punta se agarra
                                 // y se clava por separado; el cuerpo se dibuja flexible (VRCableRenderer).
                                 // Reemplaza al cable rígido de largo fijo (un solo salto). Así un mismo
                                 // cable cubre saltos cortos (slot↔slot) y largos (pin Arduino↔slot).

        _activeCables++;
        var tracker = go.AddComponent<CableTracker>();
        tracker.spawner = this;

        UpdateState();
    }

    internal void OnCableDestroyed()
    {
        _activeCables = Mathf.Max(0, _activeCables - 1);
        UpdateState();
        if (_state != BoxState.Full && !_gateOpen) StartGateAnim(true);
    }

    void UpdateState()
    {
        float ratio = maxActiveCables > 0 ? (float)_activeCables / maxActiveCables : 0f;
        BoxState next = ratio >= 1f    ? BoxState.Full
                      : ratio >= 0.75f ? BoxState.Warning
                      : ratio > 0f    ? BoxState.Active
                      :                 BoxState.Idle;

        bool shouldOpen = next != BoxState.Full;
        if (next != _state || shouldOpen != _gateOpen)
        {
            _state = next;
            if (shouldOpen != _gateOpen) StartGateAnim(shouldOpen);
        }

        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (counterText != null)
            counterText.text = $"{_activeCables}/{maxActiveCables}";

        Color led = _state switch
        {
            BoxState.Warning => LedAmber,
            BoxState.Full    => LedRed,
            _                => LedGreen,
        };
        SetLedEmission(led * 1.5f);

        Color btn = _state == BoxState.Full ? LedRed * 1.5f : LedGreen * 1.2f;
        SetButtonEmission(btn);
    }

    IEnumerator PulseLED()
    {
        float t = 0f;
        while (true)
        {
            if (_state == BoxState.Idle)
            {
                t += Time.deltaTime * 0.5f;
                float i = Mathf.Lerp(0.5f, 1.8f, (Mathf.Sin(t * Mathf.PI * 2f) + 1f) * 0.5f);
                SetLedEmission(LedGreen * i);
            }
            else if (_state == BoxState.Warning)
            {
                SetLedEmission(LedAmber * 2.2f);
                yield return new WaitForSeconds(0.28f);
                SetLedEmission(LedAmber * 0.4f);
                yield return new WaitForSeconds(0.28f);
                continue;
            }
            yield return null;
        }
    }

    IEnumerator AnimateButtonPress()
    {
        if (buttonCap == null) yield break;
        var pressed = _buttonRestPos + new Vector3(0f, 0f, buttonPressDepth);
        yield return MoveTo(buttonCap, _buttonRestPos, pressed, buttonAnimTime * 0.35f);
        yield return MoveTo(buttonCap, pressed, _buttonRestPos, buttonAnimTime * 0.65f);
    }

    void StartGateAnim(bool open)
    {
        _gateOpen = open;
        if (_gateCo != null) StopCoroutine(_gateCo);
        if (gatePlate != null)
            _gateCo = StartCoroutine(MoveTo(gatePlate, gatePlate.localPosition,
                open ? _gateOpenPos : _gateClosedPos, gateAnimTime));
    }

    static IEnumerator MoveTo(Transform t, Vector3 from, Vector3 to, float dur)
    {
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            if (t == null) yield break; // CORRECCIÓN: Previene MissingReferenceException
            float n = Mathf.Clamp01(e / dur);
            float s = n < 0.5f ? 4f * n * n * n : 1f - Mathf.Pow(-2f * n + 2f, 3f) / 2f;
            t.localPosition = Vector3.Lerp(from, to, s);
            yield return null;
        }
        if (t != null) t.localPosition = to;
    }

    static IEnumerator ScaleIn(Transform t, Vector3 target, float dur)
    {
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            if (t == null) yield break; // CORRECCIÓN: Salva el sistema si el cable muere antes de terminar de crecer
            float n = Mathf.Clamp01(e / dur);
            t.localScale = target * (1f - Mathf.Pow(1f - n, 3f));
            yield return null;
        }
        if (t != null) t.localScale = target;
    }

    void SetLedEmission(Color c)
    {
        if (ledRenderer == null) return;
        if (_mpbLed == null) _mpbLed = new MaterialPropertyBlock();   // lazy-init: evita NRE si se llama en teardown
        _mpbLed.SetColor("_BaseColor",     c * 0.6f);
        _mpbLed.SetColor("_EmissionColor", c);
        ledRenderer.SetPropertyBlock(_mpbLed);
    }

    void SetButtonEmission(Color c)
    {
        if (buttonRenderer == null) return;
        _mpbButton.SetColor("_BaseColor",     c * 0.5f);
        _mpbButton.SetColor("_EmissionColor", c);
        buttonRenderer.SetPropertyBlock(_mpbButton);
    }

    /// <summary>
    /// Convierte el cable spawneado en un jumper eléctrico real: <see cref="Jumper"/>
    /// (≈0 Ω) + <see cref="ProtoboardConnector"/> con las patas enganchadas a las puntas
    /// físicas Probe_A / Probe_B. Así, al meter cada punta en un slot/pin, el cable conecta
    /// esos nodos en el grafo del Reto 4. Si el prefab ya los trae, no se duplican.
    /// </summary>
    static void MakeCableElectrical(GameObject go)
    {
        if (go.GetComponent<Jumper>() != null) return;   // ya configurado en el prefab

        go.AddComponent<Jumper>();                        // satisface RequireComponent del conector
        var connector = go.AddComponent<ProtoboardConnector>();

        Transform a = null, b = null;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (a == null && t.name.Contains("Probe_A")) a = t;
            if (b == null && t.name.Contains("Probe_B")) b = t;
        }
        connector.SetLeads(a, b);   // si null, EnsureLeads ya creó patas en los extremos
    }

    /// <summary>
    /// Garantiza que el cable dispensado sea agarrable en VR. El CableBox solo lo hacía
    /// eléctrico (Jumper + ProtoboardConnector) pero nunca le añadía el agarre, así que si
    /// el prefab no traía XRGrabInteractable, el cable salía pero no se podía tomar.
    ///
    /// Añade las 3 piezas que XRI exige (XRGrabInteractable + Rigidbody + Collider) solo si
    /// faltan. Se llama a escala completa del prefab (antes del scale-in), para que el
    /// XRGrabInteractable recolecte bien los colliders hijos al registrarse.
    /// </summary>
    /// <summary>
    /// Convierte el cable dispensado en un JUMPER FLEXIBLE de puntas independientes (opción 3).
    /// Cada punta (Probe_A/Probe_B) se agarra y se clava por separado; el cuerpo se dibuja como
    /// línea flexible (<see cref="VRCableRenderer"/> + LineRenderer en world-space, ya en el prefab).
    ///
    /// Por qué: el modelo rígido anterior fijaba la separación de las puntas a un único largo
    /// (cableEscala), así que un mismo cable solo servía para UN salto. Los logs de Bind mostraron
    /// que el Reto 4 necesita saltos cortos (slot↔slot ~4 cm) Y largos (pin Arduino↔slot ~7 cm) a la
    /// vez — imposible con largo fijo. Con puntas independientes el largo lo pone el jugador.
    /// </summary>
    static void MakeCableFlexible(GameObject go)
    {
        Transform probeA = null, probeB = null, body = null;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (probeA == null && t.name.Contains("Probe_A"))   probeA = t;
            if (probeB == null && t.name.Contains("Probe_B"))   probeB = t;
            if (body   == null && t.name.Contains("Cable_Body")) body  = t;
        }

        // 1) El cilindro rígido (Cable_Body) sobra: el VRCableRenderer dibuja el cuerpo flexible
        //    entre las dos puntas. Lo ocultamos para que no quede un palo recto flotando.
        if (body != null)
        {
            var br = body.GetComponent<Renderer>();
            if (br != null) br.enabled = false;
        }

        // 2) La raíz deja de ser agarrable como bloque rígido: ya no se agarra "el cable", solo las
        //    puntas. Quitamos su XRGrab + collider + Rigidbody (no los requiere nada: VRCableRenderer,
        //    LineRenderer, Jumper y ProtoboardConnector viven sin ellos).
        var rootGrab = go.GetComponent<XRGrabInteractable>();
        if (rootGrab != null) Destroy(rootGrab);
        foreach (var c in go.GetComponents<Collider>()) Destroy(c);
        var rootRb = go.GetComponent<Rigidbody>();
        if (rootRb != null) Destroy(rootRb);

        // 3) Cada punta, agarrable de forma independiente.
        MakeProbeGrabbable(probeA);
        MakeProbeGrabbable(probeB);

        // 4) Separación inicial (~9 cm) para que se dispense con forma de cable colgante. Es solo
        //    cosmético: las puntas son independientes y el jugador las estira a la distancia que necesite.
        if (probeB != null) probeB.localPosition = new Vector3(0f, 0f, 0.09f);

        // 5) Arco parabólico garantizado por código (el prefab serializa campos obsoletos del
        //    VRCableRenderer anterior). El cuerpo se arquea HACIA ARRIBA con altura proporcional a la
        //    distancia entre puntas → se levanta del tablero: puntas fáciles de agarrar y se ve qué dos
        //    huecos une cada cable (no quedan cables planos pisándose).
        var cr = go.GetComponent<VRCableRenderer>();
        if (cr == null) cr = go.AddComponent<VRCableRenderer>();
        if (cr.origin == null) cr.origin = probeA;
        if (cr.target == null) cr.target = probeB;
        cr.segments    = 20;     // curva suave
        cr.arcUpward   = true;   // arquea hacia arriba (agarrable + legible)
        cr.arcPerMeter = 0.4f;   // 40% de la distancia = altura del arco (parábola natural)
        cr.minArc      = 0.02f;  // 2 cm de arco aunque las puntas estén juntas
        cr.maxArc      = 0.12f;  // tope 12 cm para conexiones largas
    }

    /// <summary>
    /// Hace una punta del jumper agarrable por separado: collider de agarre + Rigidbody kinemático
    /// (la punta queda donde se la deja, no cae) + XRGrabInteractable que vuelve a colgar de la raíz
    /// al soltar (conserva la world-pos). El enganche eléctrico usa la POSICIÓN del transform de la
    /// punta (no el collider), así que el collider puede ser generoso sin afectar el snap a nodos.
    /// </summary>
    static void MakeProbeGrabbable(Transform probe)
    {
        if (probe == null) return;
        var pgo = probe.gameObject;

        // Se conserva la malla y la escala del PREFAB (forma de conector original) — ya NO se
        // sustituye por un cubo. El pivote sigue siendo el punto de conexión (lo que usan el plug
        // y el bind), así que la lógica eléctrica no cambia.

        // Collider de agarre: usar el que trae el prefab; solo asegurar que exista y no sea trigger.
        var col = pgo.GetComponent<Collider>();
        if (col == null) col = pgo.AddComponent<SphereCollider>();
        col.isTrigger = false;

        var rb = pgo.GetComponent<Rigidbody>();
        if (rb == null) rb = pgo.AddComponent<Rigidbody>();
        rb.useGravity   = false;
        rb.isKinematic  = true;
        rb.interpolation = RigidbodyInterpolation.None;

        var grab = pgo.GetComponent<XRGrabInteractable>();
        if (grab == null) grab = pgo.AddComponent<XRGrabInteractable>();
        grab.movementType         = XRGrabInteractable.MovementType.Kinematic;
        grab.trackPosition        = true;
        grab.trackRotation        = true;
        grab.trackScale           = false;   // conserva la escala de conector de la punta (no la fuerza al attach)
        grab.throwOnDetach        = false;
        grab.retainTransformParent = true;   // al soltar vuelve a colgar de la raíz, manteniendo la world-pos

        // Imán de enchufe: al soltar la punta cerca de un slot/pin, se clava sobre el nodo → se ve
        // conectada Y queda dentro del snapRadius eléctrico (lo que faltaba para "que conecten").
        if (pgo.GetComponent<CableProbePlug>() == null) pgo.AddComponent<CableProbePlug>();
    }

    static Material _cableMeshMat;
    static Material _cableLineMat;

    /// <summary>
    /// Asegura que cada renderer del cable tenga un material URP válido. El prefab los trae en null
    /// (m_Materials: [{fileID: 0}]) y un MaterialPropertyBlock NO asigna material → URP cae al shader
    /// de ERROR, que no es estéreo: en VR el cable se ve magenta y SOLO en un ojo (parece "seguirte").
    /// </summary>
    static void EnsureMateriales(GameObject go)
    {
        if (_cableMeshMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _cableMeshMat = new Material(sh);
        }
        if (_cableLineMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            _cableLineMat = new Material(sh);
        }

        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            if (r.sharedMaterial == null) r.sharedMaterial = _cableMeshMat;

        foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true))
            if (lr.sharedMaterial == null) lr.sharedMaterial = _cableLineMat;
    }

    static void ApplyCableColors(GameObject go, int idx)
    {
        var p = Palettes[idx % Palettes.Length];

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mpb = new MaterialPropertyBlock();   // bloque fresco por renderer — evita herencia de propiedades
            if (r.name.Contains("Cable_Body"))
            {
                mpb.SetColor("_BaseColor",     p.body);
                mpb.SetColor("_EmissionColor", p.body * 0.1f);
                r.SetPropertyBlock(mpb);
            }
            else if (r.name.Contains("Probe_A"))
            {
                mpb.SetColor("_BaseColor", p.probeA);
                r.SetPropertyBlock(mpb);
            }
            else if (r.name.Contains("Probe_B"))
            {
                mpb.SetColor("_BaseColor", p.probeB);
                r.SetPropertyBlock(mpb);
            }
        }
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
}

public class CableTracker : MonoBehaviour
{
    [HideInInspector] public CableBoxSpawner spawner;
    void OnDestroy() => spawner?.OnCableDestroyed();
}