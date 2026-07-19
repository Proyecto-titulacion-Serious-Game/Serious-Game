using UnityEngine;
using System.Collections.Generic; // Necesario para usar List<>
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRGrabInteractable (soltar de la bandeja)

/// <summary>
/// Vive en la escena Explorador.unity.
/// Escucha GameSession.OnComponenteRecibido y spawna el componente físico
/// en la bandeja del Explorador para que pueda instalarlo.
/// Modificado para ACUMULAR componentes con físicas reales en la mesa.
/// </summary>
public class ExplorerComponentReceiver : MonoBehaviour
{
    [Header("Punto de spawn general (fallback si no hay slot específico)")]
    public Transform puntoDeEntrega;
    
    [Tooltip("Margen de dispersión para que los componentes no se fusionen al aparecer.")]
    public float radioDispersion = 0.08f;

    [Header("Bandeja híbrida")]
    [Tooltip("Si está activo, los componentes se 'pegan' a la bandeja (puntoDeEntrega) y viajan con " +
             "ella; al agarrarlos con la mano se sueltan para instalarlos. REQUIERE que puntoDeEntrega " +
             "tenga escala UNIFORME (el root del ComponentReceiver, NO el Tray_Visual achatado).")]
    public bool modoBandejaHibrida = true;

    [Header("Slots por tipo — arrastra los empties de la escena (opcional)")]
    [Tooltip("Si se asigna, el Resistor aparece aquí en lugar del puntoDeEntrega general.")]
    public Transform slotResistor;
    [Tooltip("Si se asigna, el LED aparece aquí.")]
    public Transform slotLED;
    [Tooltip("Si se asigna, el Capacitor aparece aquí.")]
    public Transform slotCapacitor;
    [Tooltip("Si se asigna, el ArduinoPin aparece aquí.")]
    public Transform slotArduinoPin;

    [Header("Prefabs base (fallback cuando no hay variante específica)")]
    public GameObject resistorPrefab;
    public GameObject ledPrefab;
    public GameObject capacitorPrefab;
    public GameObject arduinoPinPrefab;

    [Header("Variantes LED (opcionales — asigna los Delivered_LED_X)")]
    public GameObject ledGreenPrefab;
    public GameObject ledRedPrefab;
    public GameObject ledYellowPrefab;

    [Header("Variantes Capacitor (opcionales — asigna los Delivered_Capacitor_X)")]
    public GameObject capacitorBluePrefab;
    public GameObject capacitorBlackPrefab;
    public GameObject capacitorOrangePrefab;

    [Header("Variante Resistor (opcional — asigna Delivered_Resistor_Vertical)")]
    public GameObject resistorVerticalPrefab;

    [Header("Sistema de delivery local (para validar instalación)")]
    public ComponentDeliverySystem delivery;

    // LISTA para acumular componentes en lugar de una sola variable
    private List<GameObject> _componentesRecibidos = new List<GameObject>();
    // Último componente recibido POR TIPO → para REEMPLAZAR en vez de apilar (Retos 1-3 = 1 pieza/tipo).
    private readonly Dictionary<ComponentType, GameObject> _ultimoPorTipo = new Dictionary<ComponentType, GameObject>();
    // Variante con la que se envió ese último componente (color del LED, color del capacitor,
    // orientación del resistor) — necesaria para distinguir un REENVÍO genuino (misma variante,
    // p.ej. doble clic o reintento de red) de un CAMBIO deliberado (el Técnico eligió otro color).
    private readonly Dictionary<ComponentType, ComponentVariant> _ultimaVarientePorTipo = new Dictionary<ComponentType, ComponentVariant>();

    // RECEPTOR PRIMARIO: en la escena pueden coexistir DOS ExplorerComponentReceiver (el standalone
    // 'ComponentReceiver_Caja' + el anidado dentro de Explorer_Player). Ambos se suscriben a
    // OnComponenteRecibido → cada envío spawneaba 2 piezas superpuestas (y el reemplazo por tipo
    // destruía copias cruzadas: enviabas amarillo y "quedaba" el verde del otro receptor). Solo el
    // primero que se habilita procesa los eventos; el resto los ignora.
    private static ExplorerComponentReceiver _primario;

    private GameManager _gm;   // para saber el reto actual (acumular en Reto 4)

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    void Awake()
    {
        // Auto-asignar ComponentDeliverySystem y copiar sus prefabs si faltan
        if (delivery == null)
            delivery = FindAnyObjectByType<ComponentDeliverySystem>();

        if (delivery != null)
        {
            if (resistorPrefab   == null) resistorPrefab   = delivery.resistorPrefab;
            if (ledPrefab        == null) ledPrefab        = delivery.ledPrefab;
            if (capacitorPrefab  == null) capacitorPrefab  = delivery.capacitorPrefab;
            if (arduinoPinPrefab == null) arduinoPinPrefab = delivery.arduinoPinPrefab;
        }
        else
        {
            Debug.LogWarning("[ExplorerComponentReceiver] ComponentDeliverySystem no encontrado. " +
                             "Los componentes recibidos no podrán instalarse en el circuito.", this);
        }

        // Auto-asignar punto de entrega desde el Toolbox si no está asignado
        if (puntoDeEntrega == null)
        {
            var toolbox = FindAnyObjectByType<ToolboxController>();
            if (toolbox != null) puntoDeEntrega = toolbox.GetComponentSlot();
        }
    }

    void OnEnable()
    {
        if (_primario == null) _primario = this;

        GameSession.OnComponenteRecibido          += HandleComponenteRecibido;
        GameSession.OnRetoChanged                 += HandleRetoChanged;
        GameSession.OnCableFixed                  += HandleCableFixed;
        ComponentSendingTray.OnComponentSentLocal += HandleComponenteRecibidoLocal;
    }

    void OnDisable()
    {
        if (_primario == this) _primario = null;

        GameSession.OnComponenteRecibido          -= HandleComponenteRecibido;
        GameSession.OnRetoChanged                 -= HandleRetoChanged;
        GameSession.OnCableFixed                  -= HandleCableFixed;
        ComponentSendingTray.OnComponentSentLocal -= HandleComponenteRecibidoLocal;
    }

    // ─────────────────────────────────────────────
    //  Handlers
    // ─────────────────────────────────────────────

    // GameSession (multijugador) — la variante (color/orientación) viaja en el RPC.
    void HandleComponenteRecibido(ComponentType tipo, float valor, int variante)
        => SpawnComponente(tipo, valor, null, (ComponentVariant)variante);

    // OnComponentSentLocal (editor/offline) — misma firma que el evento Action<ComponentType, float, int>
    void HandleComponenteRecibidoLocal(ComponentType tipo, float valor, int variante)
        => SpawnComponente(tipo, valor, null, (ComponentVariant)variante);

    void SpawnComponente(ComponentType tipo, float valor, GameObject prefabOverride,
                         ComponentVariant variante = ComponentVariant.Default)
    {
        // Solo el receptor primario spawnea (evita duplicados si hay 2 receivers en escena).
        if (_primario != null && _primario != this) return;

        // Reto 4: el Arduino YA NO se entrega como componente físico. Es un objeto fijo en la
        // escena y se programa por código (Técnico → ArduinoNetworkBridge → ArduinoCore), sus
        // pines se conectan con cables (CableBox + ProtoboardConnector). Ignoramos cualquier
        // ArduinoPin que llegue por el canal de entrega (legacy del paradigma lineal de retos 1-3).
        if (tipo == ComponentType.ArduinoPin)
        {
            Debug.LogWarning("[Receiver] ArduinoPin ignorado: el Arduino no se entrega como " +
                             "componente; se programa por el bridge y se conecta con cables.", this);
            return;
        }

        // ¡ELIMINADO EL DESTROY AQUÍ PARA PERMITIR ACUMULACIÓN!

        // If delivery already spawned a ghost (from the delivery path in ComponentSendingTray),
        // cancel it so we spawn at the correct Explorer location instead.
        if (delivery != null && delivery.HasPendingDelivery())
            delivery.CancelDelivery();

        // Prioridad: prefab enviado desde el Técnico → variante específica → prefab base.
        GameObject prefab = prefabOverride != null ? prefabOverride : SeleccionarPrefab(tipo, valor, variante);

        Transform slot = tipo switch
        {
            ComponentType.Resistor   => slotResistor   != null ? slotResistor   : puntoDeEntrega,
            ComponentType.LED        => slotLED        != null ? slotLED        : puntoDeEntrega,
            ComponentType.Capacitor  => slotCapacitor  != null ? slotCapacitor  : puntoDeEntrega,
            ComponentType.ArduinoPin => slotArduinoPin != null ? slotArduinoPin : puntoDeEntrega,
            _                        => puntoDeEntrega
        };

        // Si no hay slot ni puntoDeEntrega asignados, resolver uno seguro (protoboard/cámara)
        // en vez de no spawnear o caer fuera del mapa.
        if (slot == null)
            slot = puntoDeEntrega = ComponentDeliverySystem.ResolverPuntoEntregaSeguro(transform);

        if (prefab == null || slot == null)
        {
            Debug.LogWarning($"[Receiver] Prefab o punto de entrega no asignado para {tipo}.");
            return;
        }

        // REEMPLAZAR el componente anterior del mismo tipo: en los Retos 1-3 solo hay 1 pieza por
        // tipo, así que reenviar no debe apilar objetos en la mesa. (Tipos distintos coexisten.)
        // RETO 4 (sandbox): el Técnico puede enviar VARIAS unidades del mismo tipo (3 LEDs de
        // distinto color, 2 resistencias de distinto valor/orientación) → se ACUMULAN.
        //
        // BUG (2026-07-16): antes esto se decidía con GameManager.currentLevel, un campo LOCAL
        // (MonoBehaviour, no [Networked]) que cada proceso (Técnico y Explorador son ejecutables
        // separados conectados por Fusion) mantiene por su cuenta, sincronizado solo por la cadena
        // de eventos OnRetoChanged→LoadLevel. Si esa cadena no llegó a tiempo o el GameManager local
        // del Explorador no estaba listo, esReto4 daba false AUNQUE el reto activo fuera el 4, y
        // reenviar un LED de otro color destruía el anterior. GameSession.RetoActual SÍ es
        // [Networked] (replicado por Fusion), así que es la fuente de verdad correcta; el fallback a
        // GameManager solo aplica en modo solo/offline (sin GameSession).
        if (_gm == null) _gm = FindAnyObjectByType<GameManager>();
        // Acepta 3 o 4 por si RetoActual es 0-based (Arduino=3, como lo pasan hoy AvanzarReto/
        // DebugLevelSkipper) o 1-based en algún flujo — mismo criterio defensivo que ExplorerLinkOverlay.
        bool esReto4 = GameSession.Instance != null
            ? (GameSession.Instance.RetoActual == 3 || GameSession.Instance.RetoActual == 4)
            : (_gm != null && _gm.currentLevel == LevelType.Arduino);

        if (!esReto4 && _ultimoPorTipo.TryGetValue(tipo, out var previo) && previo != null)
        {
            bool mismaVariante = _ultimaVarientePorTipo.TryGetValue(tipo, out var varientePrevia) && varientePrevia == variante;

            if (YaFijadoEnSlot(previo) && mismaVariante)
            {
                // El anterior ya quedó "soldado" en su slot (p.ej. Reto2CircuitGuard lo cableó a la
                // rama dañada y deshabilitó su grab) Y es la MISMA variante — esto es un reenvío
                // genuino (doble clic, reintento porque el diagnóstico aún no marca "completo", o una
                // reconexión de red que repite el envío), no debe destruir la pieza que el jugador ya
                // colocó correctamente.
                Debug.Log($"[Receiver] {tipo} anterior ya está fijado en su slot (misma variante); se ignora el reenvío duplicado.");
                return;
            }

            // BUG (reportado): si el Técnico envía un LED de OTRO color mientras el anterior ya está
            // fijado en la rama dañada (p.ej. mandó rojo primero y después amarillo/verde), el guard
            // de arriba ignoraba SIEMPRE el segundo envío con "YaFijadoEnSlot" — el LED nuevo nunca
            // llegaba a spawnear ("el LED desaparece" desde la perspectiva del jugador, que esperaba
            // uno nuevo). Con variante distinta, si estaba fijado hay que DESHACER el cableado viejo
            // (reactivar el LED dañado original que Reto2CircuitGuard había ocultado) antes de destruir
            // la pieza — si no, el reto queda con el riel apuntando a un GameObject destruido.
            if (YaFijadoEnSlot(previo) && !mismaVariante)
                Reto2CircuitGuard.DeshacerReemplazo(previo);

            // BUG REAL (jugado en VR): si 'previo' estaba puesto en un ComponentSlot (Retos 1-3,
            // p.ej. una resistencia incorrecta en el Reto 3), destruirlo aquí sin avisarle al slot
            // dejaba _hasComponent/_installed del slot apuntando a un objeto ya destruido para
            // siempre — su imán (OnTriggerStay) nunca volvía a aceptar nada porque arranca con
            // "if (_hasComponent) return;". La pieza NUEVA que el jugador intentaba encajar ahí
            // nunca se enganchaba y quedaba a merced de la física normal contra el hueco del slot,
            // saliendo disparada. ComponentSlot.ReleaseComponent() ya existía pero nadie lo llamaba.
            foreach (var slotOcupado in FindObjectsByType<ComponentSlot>(FindObjectsInactive.Include))
                if (slotOcupado != null && slotOcupado.InstalledObject == previo) { slotOcupado.ReleaseComponent(); break; }

            _componentesRecibidos.Remove(previo);
            Destroy(previo);
        }

        // Crear un ligero desfase aleatorio para que no colisionen violentamente
        Vector3 offsetAleatorio = new Vector3(
            Random.Range(-radioDispersion, radioDispersion),
            0.05f, // Aparece un poquito arriba de la mesa para caer con gravedad
            Random.Range(-radioDispersion, radioDispersion)
        );

        Vector3 posicionSpawn = slot.position + offsetAleatorio;

        GameObject nuevoComponente = Instantiate(prefab, posicionSpawn, slot.rotation);

        // Agregar a nuestra lista de control + registrar como el actual de su tipo.
        _componentesRecibidos.Add(nuevoComponente);
        _ultimoPorTipo[tipo] = nuevoComponente;
        _ultimaVarientePorTipo[tipo] = variante;

        ConfigurarComponente(nuevoComponente, tipo, valor);

        bool tieneRb = nuevoComponente.TryGetComponent<Rigidbody>(out var rb);

        if (modoBandejaHibrida)
        {
            // ── BANDEJA HÍBRIDA ───────────────────────────────────────────────
            // El componente se "sostiene" en la bandeja: se emparenta al punto de entrega y queda
            // kinematic → VIAJA con la caja cuando el Explorador la mueve. Al agarrarlo con la mano
            // (XRGrabInteractable) se suelta (un-parent) y pasa a física para poder instalarlo.
            AdvertirSiEscalaNoUniforme(slot);
            nuevoComponente.transform.SetParent(slot, worldPositionStays: true);
            if (tieneRb) { rb.isKinematic = true; rb.useGravity = false; }

            var grab = nuevoComponente.GetComponentInChildren<XRGrabInteractable>(true);
            if (grab != null)
            {
                grab.retainTransformParent = false;   // que XRI no lo re-pegue a la bandeja al soltar
                grab.selectEntered.AddListener(_ =>
                {
                    // Solo des-emparentar de la bandeja. NO tocar isKinematic/useGravity aquí:
                    // el XRGrabInteractable es MovementType.Kinematic y gestiona el kinematic durante
                    // el agarre. Forzar no-kinemático + gravedad hacía que la pieza CAYERA y temblara
                    // ("epilepsia") al moverla. La gravedad post-soltar la pone GrabbableComponent.
                    nuevoComponente.transform.SetParent(null, worldPositionStays: true);
                });
            }
        }
        else if (tieneRb)
        {
            // Modo clásico: cae por gravedad y descansa por física sobre la bandeja.
            rb.isKinematic = false;
            rb.useGravity  = true;
        }

        // Collision continua + interpolación: no atravesar la bandeja fina ni temblar.
        if (tieneRb)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
        }

        delivery?.PrepareForInstall(tipo, valor);

        // Reto 2 (protoboard determinista): si es un LED, el guard lo cablea a la rama dañada al
        // soltarlo (ánodo=COL_1 → cátodo=COL_2, polaridad correcta). No-op fuera del Reto 2.
        if (tipo == ComponentType.LED)
            Reto2CircuitGuard.NotifyLedDelivered(nuevoComponente);

        Debug.Log($"[Receiver] Componente recibido y acumulado: {tipo} ({(prefabOverride != null ? prefabOverride.name : "base")}) = {valor}");
    }

    void HandleRetoChanged(int reto)
    {
        // Limpiamos toda la mesa destruyendo todos los componentes acumulados
        foreach (var comp in _componentesRecibidos)
        {
            if (comp != null)
            {
                Destroy(comp);
            }
        }
        
        // Vaciamos la lista para el nuevo reto
        _componentesRecibidos.Clear();
        _ultimoPorTipo.Clear();
        _ultimaVarientePorTipo.Clear();
        Debug.Log("[Receiver] Mesa limpiada para el nuevo reto.");
    }

    /// <summary>
    /// Elige el prefab a instanciar según la VARIANTE concreta que envió el Técnico (color del LED,
    /// color del capacitor, orientación del resistor). Si esa variante no tiene prefab asignado en el
    /// Inspector, cae a un default coherente (LED verde, capacitor azul, resistor horizontal) y por
    /// último al prefab base del tipo. Así enviar un LED amarillo llega amarillo, y un resistor
    /// vertical llega vertical, en vez de caer siempre a la variante por defecto.
    /// </summary>
    GameObject SeleccionarPrefab(ComponentType tipo, float valor, ComponentVariant variante)
    {
        switch (tipo)
        {
            case ComponentType.Resistor:
                if (variante == ComponentVariant.ResistorVertical && resistorVerticalPrefab != null)
                    return resistorVerticalPrefab;
                return resistorPrefab;

            case ComponentType.LED:
                switch (variante)
                {
                    case ComponentVariant.LedRed:    if (ledRedPrefab    != null) return ledRedPrefab;    break;
                    case ComponentVariant.LedYellow: if (ledYellowPrefab != null) return ledYellowPrefab; break;
                    case ComponentVariant.LedGreen:  if (ledGreenPrefab  != null) return ledGreenPrefab;  break;
                }
                // Default LED: verde si existe, si no el base.
                return ledGreenPrefab != null ? ledGreenPrefab : ledPrefab;

            case ComponentType.Capacitor:
                switch (variante)
                {
                    case ComponentVariant.CapacitorBlack:  if (capacitorBlackPrefab  != null) return capacitorBlackPrefab;  break;
                    case ComponentVariant.CapacitorOrange: if (capacitorOrangePrefab != null) return capacitorOrangePrefab; break;
                    case ComponentVariant.CapacitorBlue:   if (capacitorBluePrefab   != null) return capacitorBluePrefab;   break;
                }
                return capacitorBluePrefab != null ? capacitorBluePrefab : capacitorPrefab;

            case ComponentType.ArduinoPin:
                return arduinoPinPrefab;

            default:
                return null;
        }
    }

    /// <summary>Un componente entregado queda "fijado" (soldado) en su slot cuando algo lo dejó sin
    /// grab (p.ej. Reto2CircuitGuard.CablearEnRamaDanada deshabilita su XRGrabInteractable tras
    /// cablearlo). Sirve para no destruirlo/duplicarlo si el Técnico reenvía el mismo tipo.</summary>
    static bool YaFijadoEnSlot(GameObject go)
    {
        var grab = go.GetComponentInChildren<XRGrabInteractable>(true);
        return grab != null && !grab.enabled;
    }

    /// <summary>Avisa si el punto de entrega tiene escala no uniforme (deformaría a los hijos).</summary>
    static void AdvertirSiEscalaNoUniforme(Transform t)
    {
        Vector3 s = t.lossyScale;
        if (Mathf.Abs(s.x - s.y) > 0.01f || Mathf.Abs(s.x - s.z) > 0.01f)
            Debug.LogWarning($"[Receiver] El punto de entrega '{t.name}' tiene escala NO uniforme {s} → " +
                             "los componentes emparentados se deformarán. Usa el ROOT del ComponentReceiver " +
                             "(escala 1,1,1), no el Tray_Visual achatado.", t);
    }

    // ─────────────────────────────────────────────
    //  Configuración del prefab instanciado
    // ─────────────────────────────────────────────

    // Reto 4: el Técnico reparó el cable — propagar al circuito del Explorador
    void HandleCableFixed()
    {
        var circuit = FindAnyObjectByType<CircuitManager>();
        if (circuit == null) return;

        foreach (var comp in circuit.components)
        {
            if (comp is ArduinoPin pin && pin.hasLooseCable)
            {
                pin.FixLooseCable();
                circuit.MarkDirty();
                Debug.Log("[Receiver] Cable suelto reparado remotamente (Reto 4).");
                return;
            }
        }
    }

    void ConfigurarComponente(GameObject obj, ComponentType tipo, float valor)
    {
        // Reto 4 (modo protoboard): fijar la escala del resistor para que sus patas alcancen
        // huecos distintos. Se aplica ANTES de EnsureOn para que el bounding box ya estirado
        // defina la posición de las patas (leadA/leadB).
        //
        // La escala se calcula de la GEOMETRÍA REAL de la protoboard (distancia física mínima
        // entre 2 slots de nets distintos — cada railId es un net de varios huecos separados, no
        // una fila contigua, así que esa distancia mínima entre-nets es la referencia correcta),
        // en vez de un valor fijo a mano. Si por algún motivo no se puede medir (protoSim no
        // resuelto todavía), cae al valor calibrado de Reto4BreadboardMode como respaldo.
        if (tipo == ComponentType.Resistor)
            AplicarEscalaResistorReto4(obj);

        // Reto 4: garantizar ProtoboardConnector en el componente recibido por red,
        // si no el CircuitSimulator nunca lo engancha a los nodos de la protoboard.
        ProtoboardConnector.EnsureOn(obj);

        // Reto 4: enderezar la pieza a la cuadrícula al soltarla (en vez de heredar la
        // inclinación de la mano), para que sus patas entren rectas en los huecos.
        // Requiere GrabbableComponent (lo traen los prefabs entregables).
        if (obj.GetComponent<GrabbableComponent>() != null && obj.GetComponent<Reto4StraightenOnPlace>() == null)
            obj.AddComponent<Reto4StraightenOnPlace>();

        switch (tipo)
        {
            case ComponentType.Resistor:
                if (obj.TryGetComponent<Resistor>(out var r))
                {
                    r.resistance = valor;
                    r.hasFault   = false;
                }
                break;
            case ComponentType.LED:
                if (obj.TryGetComponent<LED>(out var led))
                    led.polarityInverted = valor < 0;
                break;
            case ComponentType.Capacitor:
                if (obj.TryGetComponent<Capacitor>(out var cap))
                    cap.polarityInverted = valor < 0;
                break;
            case ComponentType.ArduinoPin:
                if (obj.TryGetComponent<ArduinoPin>(out var pin))
                    pin.pinNumber = (int)valor;
                break;
        }
    }

    /// <summary>
    /// Escala el resistor entregado en el Reto 4 para que sus patas alcancen físicamente 2 slots de
    /// nets distintos del bareboard REAL — mide la separación mínima real entre nets (ver
    /// ProtoboardSimulator.SepararacionMinimaEntreNetsDistintos) y escala el eje más largo del mesh
    /// (a escala base) para que la cubra exactamente. Los otros 2 ejes quedan a una fracción fija
    /// del factor de largo, para un grosor visualmente razonable (no un cilindro gigante).
    /// </summary>
    void AplicarEscalaResistorReto4(GameObject obj)
    {
        if (_gm == null) _gm = FindAnyObjectByType<GameManager>();
        float targetSpan = _gm != null && _gm.protoSim != null
            ? _gm.protoSim.SepararacionMinimaEntreNetsDistintos()
            : 0f;

        var rend = obj.GetComponentInChildren<Renderer>();
        if (targetSpan > 0f && rend != null)
        {
            Vector3 escalaActual = obj.transform.localScale;
            Vector3 tamanoBase = new Vector3(
                rend.bounds.size.x / Mathf.Max(escalaActual.x, 0.0001f),
                rend.bounds.size.y / Mathf.Max(escalaActual.y, 0.0001f),
                rend.bounds.size.z / Mathf.Max(escalaActual.z, 0.0001f));

            // Mismo criterio que ProtoboardConnector.EnsureLeads() para elegir el eje de las patas:
            // el más largo del bounding box.
            int ejeLargo = (tamanoBase.x >= tamanoBase.y && tamanoBase.x >= tamanoBase.z) ? 0
                          : (tamanoBase.y >= tamanoBase.z) ? 1 : 2;

            float factorLargo = targetSpan / Mathf.Max(tamanoBase[ejeLargo], 0.0001f);
            const float proporcionAncho = 0.65f; // grosor visual (0.4 se veía demasiado delgado en el protoboard)

            // "Un poco más largo" que la separación entre nets (a pedido): el cuerpo sobresale de los
            // 2 huecos —como un resistor real con las patas dobladas hacia abajo— y se lee claramente
            // "de slot a slot". Las patas quedan ~12% más allá de cada hueco, muy dentro del
            // snapRadius del conector (1.2 cm), así que el enganche eléctrico no cambia.
            // La variante VERTICAL se EXCLUYE: su pose de diseño es parada, conserva la escala exacta.
            const float estiramientoHorizontal = 1.25f;
            bool esVertical = obj.name.ToLowerInvariant().Contains("vertical");

            Vector3 nuevaEscala = Vector3.one * (factorLargo * proporcionAncho);
            nuevaEscala[ejeLargo] = factorLargo * (esVertical ? 1f : estiramientoHorizontal);

            obj.transform.localScale = nuevaEscala;
            Debug.Log($"[Receiver] Resistor Reto4: escala calculada de la separación real entre slots " +
                      $"({targetSpan * 100f:F1} cm, eje {(ejeLargo == 0 ? "X" : ejeLargo == 1 ? "Y" : "Z")}) = {nuevaEscala}");
        }
        else if (Reto4BreadboardMode.ResistorScaleReto4.HasValue && Reto4BreadboardMode.ResistorScaleReto4.Value != Vector3.zero)
        {
            // Respaldo: no se pudo medir la protoboard real todavía (protoSim sin resolver).
            obj.transform.localScale = Reto4BreadboardMode.ResistorScaleReto4.Value;
            Debug.LogWarning("[Receiver] Resistor Reto4: no until protoSim para medir — usando escala calibrada de respaldo.");
        }
    }
}