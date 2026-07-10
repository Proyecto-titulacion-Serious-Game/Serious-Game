using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Sistema de entrega asimétrica de componentes — VERSIÓN CORRECTA.
///
/// DISEÑO CLAVE (estilo Keep Talking and Nobody Explodes):
///   - El Técnico SIEMPRE envía lo que seleccionó, sin validación previa
///   - El componente físico SIEMPRE aparece en la bandeja del Explorador
///   - El Explorador SIEMPRE puede instalarlo
///   - La validación ocurre al aplicar al circuito:
///     * Valor correcto → circuito se repara, LED cambia a verde
///     * Valor incorrecto → circuito sigue dañado o empeora
///   - Los jugadores deben colaborar para diagnosticar qué salió mal
///
/// Esto preserva el gameplay asimétrico real: el Técnico puede equivocarse,
/// el Explorador ve las consecuencias, y entre los dos deben resolverlo.
/// </summary>
public class ComponentDeliverySystem : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Referencias")]
    public GameManager gameManager;

    [Tooltip("Transform de la Bandeja_Recepcion del Explorador donde aparecen los componentes.")]
    public Transform puntoDeEntrega;

    [Header("Prefabs de componentes (arrastrar desde Project)")]
    public GameObject resistorPrefab;
    public GameObject ledPrefab;
    public GameObject capacitorPrefab;
    public GameObject arduinoPinPrefab;

    [Header("Estado (solo lectura)")]
    [SerializeField] private ComponentType _pendingType = ComponentType.None;
    [SerializeField] private float         _pendingValue = 0f;
    [SerializeField] private bool          _waitingForInstall = false;
    [SerializeField] private GameObject    _spawnedComponent;

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────
    public static event Action<ComponentType, float> OnComponentSent;       // tipo, valor enviado
    public static event Action<bool>                 OnComponentInstalled;  // slot correcto/incorrecto
    public static event Action<bool>                 OnRepairValidated;     // circuito realmente reparado
    public static event Action<string>               OnDeliveryError;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        if (gameManager == null)
            gameManager = FindAnyObjectByType<GameManager>();

        if (puntoDeEntrega == null)
        {
            var tray = GameObject.Find("Bandeja_Recepcion");
            if (tray != null) puntoDeEntrega = tray.transform;
            else
            {
                var toolbox = FindAnyObjectByType<ToolboxController>();
                if (toolbox != null) puntoDeEntrega = toolbox.GetComponentSlot();
            }
            // Si sigue null, NO creamos un fallback ciego (que caía fuera del mapa):
            // se resuelve a un punto seguro en el primer envío (ResolverPuntoEntregaSeguro).
        }
    }

    void OnEnable()  { GameManager.OnLevelLoaded += OnLevelLoaded; }
    void OnDisable() { GameManager.OnLevelLoaded -= OnLevelLoaded; }

    void OnLevelLoaded(LevelType _) => CancelPendingDelivery();

    // ─────────────────────────────────────────────
    //  API pública del Técnico
    // ─────────────────────────────────────────────

    /// <summary>
    /// El Técnico envía una resistencia con CUALQUIER valor.
    /// NO se valida aquí — el Explorador recibirá el componente igualmente.
    /// La validación real sucede cuando se instala en un slot.
    /// </summary>
    public void SendResistor(float resistanceValue, GameObject prefabOverride = null)
    {
        if (_waitingForInstall)
        {
            Debug.Log("[Delivery] Ya hay un componente en tránsito.");
            return;
        }

        _pendingType  = ComponentType.Resistor;
        _pendingValue = resistanceValue;
        SpawnInDeliveryTray(prefabOverride != null ? prefabOverride : resistorPrefab, resistanceValue);
    }

    public void SendLED(bool correctPolarity = true, GameObject prefabOverride = null)
    {
        if (_waitingForInstall) return;
        _pendingType  = ComponentType.LED;
        _pendingValue = correctPolarity ? 1f : -1f;
        SpawnInDeliveryTray(prefabOverride != null ? prefabOverride : ledPrefab, _pendingValue);
    }

    public void SendCapacitor(bool correctPolarity = true, GameObject prefabOverride = null)
    {
        if (_waitingForInstall) return;
        _pendingType  = ComponentType.Capacitor;
        _pendingValue = correctPolarity ? 1f : -1f;
        SpawnInDeliveryTray(prefabOverride != null ? prefabOverride : capacitorPrefab, _pendingValue);
    }

    public void SendArduinoPin(int pinNumber, GameObject prefabOverride = null)
    {
        if (_waitingForInstall) return;
        _pendingType  = ComponentType.ArduinoPin;
        _pendingValue = pinNumber;
        SpawnInDeliveryTray(prefabOverride != null ? prefabOverride : arduinoPinPrefab, pinNumber);
    }

    // ─────────────────────────────────────────────
    //  Callback desde ComponentSlot
    // ─────────────────────────────────────────────

    /// <summary>
    /// Llamado por ComponentSlot cuando el Explorador instala un componente.
    ///
    /// Hay DOS niveles de validación:
    ///   1. Tipo de slot: ¿el componente va en el slot correcto?
    ///   2. Valor: ¿el valor/polaridad es el que pide el reto?
    /// </summary>
    public void OnExplorerInstalled(ComponentSlot slot)
    {
        if (!_waitingForInstall)
        {
            // Sin entrega del Técnico (modo solo/offline): validar con el valor REAL del componente
            // que el Explorador acaba de colocar. Permite completar el reto sin un segundo jugador.
            if (!DeriveDeliveryFromInstalled(slot))
            {
                Debug.Log("[Delivery] No hay componente en tránsito para instalar.");
                return;
            }
        }

        // Validación 1: ¿Slot correcto? Un slot 'Any' (sandbox) acepta cualquier tipo —igual que
        // ComponentSlot.MatchesSlotType—; antes esto rechazaba "Resistor en Any" y bloqueaba la reparación.
        bool slotCorrect = slot.acceptedType == ComponentSlotType.Any
                        || slot.acceptedType == GetSlotTypeFor(_pendingType);

        if (!slotCorrect)
        {
            OnComponentInstalled?.Invoke(false);
            OnDeliveryError?.Invoke($"Componente {_pendingType} no encaja en slot {slot.acceptedType}");
            gameManager?.RegisterWrongAttempt($"Slot incorrecto: {_pendingType} en {slot.acceptedType}");
            Debug.Log($"[Delivery] ✗ Slot incorrecto");

            Destroy(_spawnedComponent);
            ResetDeliveryState();
            return;
        }

        // Slot correcto — el componente ENTRA físicamente.
        OnComponentInstalled?.Invoke(true);

        // Validación 2: ¿Valor correcto para reparar el circuito?
        bool valueCorrect = ValidateValueForRepair();

        if (valueCorrect)
        {
            // CORREGIDO: En lugar de apagar el componente a la fuerza localmente (ec.enabled = false),
            // lo cual causaba desincronizaciones en red, ahora reportamos la instalación 
            // a través del objeto sincrónico GameSession de Fusion para que impacte en toda la red.
            if (GameSession.Instance != null)
            {
                GameSession.Instance.ReportarInstalacion(true);
            }
            else if (slot.InstalledObject != null && slot.InstalledObject.TryGetComponent<ElectricalComponent>(out var ec))
            {
                // Fallback local por si se corre la escena de forma aislada en modo Offline
                ec.enabled = false;
            }

            ApplyRepairToCircuit();
            OnRepairValidated?.Invoke(true);
            gameManager?.RegisterRepairAction();
            Debug.Log($"[Delivery] ✓ {_pendingType} ({_pendingValue}) instalado y circuito reparado.");
        }
        else
        {
            // Componente instalado pero con VALOR incorrecto.
            // El circuito NO se repara — el Explorador ve el resultado (LED sigue rojo).
            if (GameSession.Instance != null)
            {
                GameSession.Instance.ReportarInstalacion(false); // Sincroniza el fallo con el Servidor/Docente
            }

            OnRepairValidated?.Invoke(false);
            OnDeliveryError?.Invoke($"Valor incorrecto instalado: {_pendingType} = {_pendingValue}");
            gameManager?.RegisterWrongAttempt($"Valor incorrecto: {_pendingType} = {_pendingValue}");
            Debug.Log($"[Delivery] ⚠ {_pendingType} instalado con valor incorrecto. Circuito no reparado.");

            // Aplicar el valor erróneo al circuito para que se vea el efecto
            ApplyIncorrectValueToCircuit();
        }

        if (_spawnedComponent != null)
            Destroy(_spawnedComponent);

        ResetDeliveryState();
    }
    // ─────────────────────────────────────────────
    //  Spawn local
    // ─────────────────────────────────────────────

    void SpawnInDeliveryTray(GameObject prefab, float value)
    {
        if (prefab == null)
        {
            Debug.LogError("[Delivery] Prefab no asignado.");
            return;
        }
        if (puntoDeEntrega == null)
            puntoDeEntrega = ResolverPuntoEntregaSeguro(transform);
        if (puntoDeEntrega == null)
        {
            Debug.LogError("[Delivery] PuntoDeEntrega no asignado.");
            return;
        }

        _spawnedComponent = Instantiate(
            prefab,
            puntoDeEntrega.position,
            puntoDeEntrega.rotation,
            puntoDeEntrega
        );

        ConfigureSpawnedComponent(_spawnedComponent, value);

        _waitingForInstall = true;
        OnComponentSent?.Invoke(_pendingType, value);

        Debug.Log($"[Delivery] {_pendingType} ({value}) enviado al Explorador.");
    }

    /// <summary>
    /// Aplica el valor/polaridad al componente físico spawneado.
    /// Así el componente visible en la bandeja REFLEJA lo que envió el Técnico.
    /// </summary>
    void ConfigureSpawnedComponent(GameObject comp, float value)
    {
        // Reto 4: garantizar que el componente entregado tenga ProtoboardConnector,
        // si no el simulador (CircuitSimulator) nunca lo detecta al colocarse.
        ProtoboardConnector.EnsureOn(comp);

        if (comp.TryGetComponent<Resistor>(out var r))
        {
            r.resistance = value;
            r.hasFault   = false;
        }

        if (comp.TryGetComponent<LED>(out var led))
            led.polarityInverted = value < 0;

        if (comp.TryGetComponent<Capacitor>(out var cap))
            cap.polarityInverted = value < 0;

        if (comp.TryGetComponent<ArduinoPin>(out var pin))
            pin.pinNumber = (int)value;
    }

    // ─────────────────────────────────────────────
    //  Validación del valor (post-instalación)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Encuentra el resistor del reto a reparar. Prefiere el resistor FIJO de la escena con la
    /// falla (hasFault → tiene el correctResistance del reto), luego el de la lista de slots.
    /// Necesario porque el resistor del Reto 1 es una pieza fija NO instalada en un slot, así que
    /// 'circuit.components' (basado en slots) está vacío y la reparación nunca lo alcanzaba.
    /// </summary>
   Resistor BuscarResistorDelReto()
    {
        // 1. Si el reto usa slots, el resistor con falla puede estar instalado allí.
        //    (No abortamos si circuit es null: en Retos 1-3 el circuito lo maneja CircuitManager
        //    y gameManager.circuit suele ser null, así que el resistor real está en la escena.)
        if (gameManager != null && gameManager.circuit != null)
        {
            foreach (var comp in gameManager.circuit.components)
                if (comp is Resistor rs && rs.hasFault)
                    return rs;
        }

        // 2. Pieza FIJA de la escena (Reto 1): el resistor con falla NO está en ningún slot, así que
        //    'circuit.components' (basado en slots) está vacío. Buscamos scene-wide el resistor
        //    cableado (nodeA/nodeB) con la falla, ignorando el de la bandeja de entrega (sin nodos).
        foreach (var r in FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
            if (r != null && r.nodeA != null && r.nodeB != null && r.hasFault)
                return r;

        return null;
    }

    /// <summary>
    /// LED del reto a reparar (pieza fija de la escena). Para polaridad (Reto 3) busca el invertido;
    /// si no, busca la RAMA ROTA del paralelo (Reto 2): LED abierto o con resistencia anómala.
    /// Ignora LEDs sin nodos (el de la bandeja, que no está cableado).
    /// </summary>
    LED BuscarLEDDelReto(bool paraPolaridad)
    {
        LED fallback = null;
        foreach (var led in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
        {
            if (led == null || led.nodeA == null || led.nodeB == null) continue;  // solo cableados
            if (paraPolaridad) { if (led.polarityInverted) return led; }
            else               { if (led.isOpenCircuit || led.resistance > 1000f) return led; }
            if (fallback == null) fallback = led;
        }
        return fallback;
    }

    /// <summary>
    /// Fuerza que AMBOS motores recalculen tras un cambio de componente: el CircuitSimulator
    /// (Gameplay, vía GameManager) y el CircuitManager (Electrical, que es el que pinta el LED en
    /// los Retos 1–3 con SimulateSeries/Parallel y dispara OnCircuitChanged).
    /// </summary>
    void ResimularCircuitos()
    {
        gameManager?.circuit?.MarkDirty();

        foreach (var cm in FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
        {
            if (cm == null) continue;
            cm.MarkDirty();
            cm.ForceSimulate();   // recalcula ya y emite OnCircuitChanged (multímetro, LED, cables)
        }
    }

    bool ValidateValueForRepair()
    {
        // NO bloquear con gameManager.circuit == null: en Retos 1-3 el circuito lo maneja
        // CircuitManager (Electrical), no el CircuitSimulator de slots, así que 'circuit' es null
        // y antes esto rechazaba SIEMPRE el 850 correcto. Resistor/LED/Cap se validan scene-wide.
        switch (_pendingType)
        {
            case ComponentType.Resistor:
            {
                var r = BuscarResistorDelReto();
                // Sin resistor con falla = el reto YA está reparado. Volver a colocar una pieza
                // no debe castigarse como "valor incorrecto" (inflaba errores del dashboard y
                // confundía: "Intento incorrecto #2: Resistor = 850" con el valor CORRECTO).
                if (r == null) return true;
                return r.IsValueCorrect(_pendingValue);
            }

            case ComponentType.LED:
                return _pendingValue >= 0; // polaridad correcta

            case ComponentType.Capacitor:
                return _pendingValue >= 0; // polaridad correcta

            case ComponentType.ArduinoPin:
                if (gameManager?.circuit == null) return false; // este caso SÍ necesita la lista
                foreach (var c in gameManager.circuit.components)
                    if (c is ArduinoPin pin)
                        return (int)_pendingValue == pin.correctPinNumber;
                return false;
        }

        return false;
    }

    // ─────────────────────────────────────────────
    //  Aplicar cambios al Circuit
    // ─────────────────────────────────────────────

    void ApplyRepairToCircuit()
    {
        // El resistor del reto puede ser una pieza FIJA (no en slot) → buscarlo en la escena y
        // aplicarle el valor correcto. (Antes solo se iteraba circuit.components, que está vacío
        // para el Reto 1, así que el resistor real seguía en 10Ω.)
        if (_pendingType == ComponentType.Resistor)
        {
            var r = BuscarResistorDelReto();
            if (r != null)
            {
                r.resistance = _pendingValue;
                r.hasFault   = false;
                // Restaurar una potencia nominal SANA: la sobrecarga (isOverloaded = dissipatedPower
                // > powerRatingWatts) se limpia. En estos circuitos de baja tensión la disipación es
                // < 1 W, así que 1 W de margen elimina cualquier falla de "potencia insuficiente".
                r.powerRatingWatts = Mathf.Max(r.powerRatingWatts, 1f);
            }
            ResimularCircuitos();
            return;
        }

        // El LED del reto también puede ser pieza FIJA (no en slot). Reto 2: rama rota (LED abierto/
        // resistencia anómala) → restaurar resistencia normal. Reto 3: LED invertido → corregir polaridad.
        if (_pendingType == ComponentType.LED)
        {
            // Si una LED había explotado y salido volando, esta entrega la reemplaza:
            // se devuelve a su sitio y vuelve a funcionar (polaridad correcta).
            LEDBlowEffect.RestoreAllBlown(inverted: false);

            bool esPolaridad = Mathf.Abs(_pendingValue) == 1f;
            var led = BuscarLEDDelReto(esPolaridad);
            if (led != null)
            {
                if (esPolaridad) led.polarityInverted = false;
                else if (_pendingValue > 0f) { led.resistance = _pendingValue; led.isOpenCircuit = false; }
            }
            ResimularCircuitos();
            return;
        }

        if (gameManager?.circuit == null) return;

        foreach (var comp in gameManager.circuit.components)
        {
            switch (_pendingType)
            {
                case ComponentType.Resistor:
                    if (comp is Resistor res && res.hasFault)
                    {
                        res.resistance = _pendingValue;
                        res.hasFault   = false;
                    }
                    break;
                case ComponentType.LED:
                    if (comp is LED led)
                    {
                        if (led.polarityInverted) led.polarityInverted = false;
                        // Valor != ±1f → es una resistencia de reemplazo (Reto 2)
                        if (Mathf.Abs(_pendingValue) != 1f && _pendingValue > 0f)
                            led.resistance = _pendingValue;
                    }
                    break;
                case ComponentType.Capacitor:
                    if (comp is Capacitor cap && cap.polarityInverted)
                        cap.polarityInverted = false;
                    break;
                case ComponentType.ArduinoPin:
                    if (comp is ArduinoPin pin)
                    {
                        pin.pinNumber = (int)_pendingValue;
                        pin.hasFault  = false;
                    }
                    break;
            }
        }

        gameManager.circuit.MarkDirty();
    }

    /// <summary>
    /// Aplica el valor INCORRECTO al circuito para que el Explorador
    /// vea el efecto visual — LED sigue rojo, humo, sobrecarga, etc.
    /// El circuito NO se marca como reparado.
    /// </summary>
    void ApplyIncorrectValueToCircuit()
    {
        // Una LED entregada reemplaza físicamente a la quemada aunque venga invertida:
        // se restaura en su sitio, pero con la polaridad equivocada → seguirá sin prender.
        if (_pendingType == ComponentType.LED)
            LEDBlowEffect.RestoreAllBlown(inverted: _pendingValue < 0);

        // Resistor fijo del reto: el valor enviado es INCORRECTO → NO lo aplicamos. Restauramos la
        // resistencia AVERIADA para que el panel/telemetría y el LED muestren honestamente que el
        // circuito sigue roto. (Antes se aplicaba el valor propuesto, que podía "verse correcto" en
        // el panel aunque hasFault siguiera en true → el reto parecía resuelto pero nunca completaba,
        // porque GetResistance() solo lee 'resistance' y la victoria solo lee 'hasFault'.)
        if (_pendingType == ComponentType.Resistor)
        {
            var r = BuscarResistorDelReto();
            if (r != null) r.resistance = r.faultyResistance;   // sigue averiado y SE VE averiado
            ResimularCircuitos();
            return;
        }

        if (gameManager?.circuit == null) return;

        foreach (var comp in gameManager.circuit.components)
        {
            switch (_pendingType)
            {
                case ComponentType.Resistor:
                    if (comp is Resistor res)
                    {
                        // Valor erróneo → NO lo aplicamos: restauramos la resistencia averiada para que
                        // el circuito se vea roto (panel honesto). hasFault permanece en true, y ahora
                        // 'resistance' concuerda con él (nunca un valor "bonito" con la falla activa).
                        res.resistance = res.faultyResistance;
                    }
                    break;
                case ComponentType.LED:
                    if (comp is LED led)
                        led.polarityInverted = _pendingValue < 0;
                    break;
                case ComponentType.Capacitor:
                    if (comp is Capacitor cap)
                        cap.polarityInverted = _pendingValue < 0;
                    break;
                case ComponentType.ArduinoPin:
                    if (comp is ArduinoPin pin)
                        pin.pinNumber = (int)_pendingValue;
                    break;
            }
        }

        gameManager.circuit.MarkDirty();
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Resuelve un punto de entrega "seguro" (dentro del mapa y alcanzable) cuando no hay
    /// Bandeja_Recepcion ni toolbox asignados. Prioridad: Bandeja_Recepcion → protoboard
    /// (zona de trabajo del Explorador) → frente a la cámara del jugador → encima del sistema.
    /// Crea y devuelve un Transform-ancla en esa posición. Lo usan tanto este sistema como
    /// <see cref="ExplorerComponentReceiver"/> para no spawnear nunca fuera del mapa.
    /// </summary>
    public static Transform ResolverPuntoEntregaSeguro(Transform self)
    {
        var tray = GameObject.Find("Bandeja_Recepcion");
        if (tray != null) return tray.transform;

        Vector3 pos;
        var proto = FindAnyObjectByType<ProtoboardSimulator>();
        if (proto != null)
        {
            // 20 cm sobre la protoboard: cae con gravedad y queda al alcance del Explorador.
            pos = proto.transform.position + Vector3.up * 0.20f;
        }
        else if (Camera.main != null)
        {
            var cam = Camera.main.transform;
            pos = cam.position + cam.forward * 0.5f - Vector3.up * 0.2f; // frente y un poco abajo
        }
        else
        {
            pos = (self != null ? self.position : Vector3.zero) + Vector3.up * 0.5f;
        }

        var go = new GameObject("PuntoDeEntrega_Auto");
        go.transform.position = pos;
        Debug.LogWarning("[Delivery] Sin Bandeja_Recepcion: punto de entrega resuelto automáticamente " +
                         $"en {pos}. Para fijarlo, crea un GameObject 'Bandeja_Recepcion' donde quieras " +
                         "que aparezcan los componentes.", go);
        return go.transform;
    }

    ComponentSlotType GetSlotTypeFor(ComponentType type) => type switch
    {
        ComponentType.Resistor   => ComponentSlotType.Resistor,
        ComponentType.LED        => ComponentSlotType.LED,
        ComponentType.Capacitor  => ComponentSlotType.Capacitor,
        ComponentType.ArduinoPin => ComponentSlotType.ArduinoPin,
        _                         => ComponentSlotType.Resistor
    };

    void ResetDeliveryState()
    {
        _spawnedComponent  = null;
        _waitingForInstall = false;
        _pendingType       = ComponentType.None;
        _pendingValue      = 0f;
    }

    void CancelPendingDelivery()
    {
        if (_spawnedComponent != null)
            Destroy(_spawnedComponent);
        ResetDeliveryState();
    }

    // ─────────────────────────────────────────────
    //  API pública adicional
    // ─────────────────────────────────────────────

    public bool HasPendingDelivery() => _waitingForInstall;

    public void CancelDelivery()
    {
        if (!_waitingForInstall) return;
        CancelPendingDelivery();
        Debug.Log("[Delivery] Envío cancelado por el Técnico.");
    }

    /// <summary>
    /// Llamado por ExplorerComponentReceiver cuando llega un componente desde la red.
    /// Prepara el estado local sin spawnear (el objeto ya fue instanciado por el Receiver).
    /// </summary>
    public void PrepareForInstall(ComponentType tipo, float valor)
    {
        _pendingType       = tipo;
        _pendingValue      = valor;
        _waitingForInstall = true;
        _spawnedComponent  = null;   // gestionado por el Receiver
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// PRUEBA SOLO (sin VR / sin Técnico). Ejecuta la ruta REAL de validación + reparación como si
    /// el Técnico hubiera entregado <paramref name="valor"/> de tipo <paramref name="tipo"/> y el
    /// Explorador lo hubiera colocado: corre <see cref="ValidateValueForRepair"/> →
    /// <c>BuscarResistorDelReto/BuscarLEDDelReto</c> → <see cref="ApplyRepairToCircuit"/>. Es lo que
    /// F8 (<see cref="SoloTechnicianDebug"/>) NO hace, porque F8 llama <c>Repair()</c> directo y se
    /// salta esta validación. Devuelve true si el circuito quedó reparado. La invoca F9.
    /// </summary>
    public bool DebugSimularEntregaEInstalacion(ComponentType tipo, float valor)
    {
        _pendingType  = tipo;
        _pendingValue = valor;

        bool valido = ValidateValueForRepair();
        if (valido)
        {
            ApplyRepairToCircuit();
            gameManager?.RegisterRepairAction();
            Debug.Log($"[Delivery][F9] Entrega simulada OK: {tipo}={valor} → circuito reparado por la ruta real.");
        }
        else
        {
            ApplyIncorrectValueToCircuit();
            Debug.LogWarning($"[Delivery][F9] Entrega simulada RECHAZADA: {tipo}={valor} no validó " +
                             "(BuscarResistorDelReto/ValidateValueForRepair devolvió false). Si esto " +
                             "ocurre con el VALOR CORRECTO del reto, el fix está roto.");
        }

        ResetDeliveryState();
        return valido;
    }
#endif

    /// <summary>
    /// Modo solo/offline: deriva tipo+valor del componente físico ya instalado en el slot, para
    /// poder validar/reparar sin que el Técnico haya enviado nada por red. El token físico lo
    /// gestiona el ComponentSlot (no se destruye aquí, por eso _spawnedComponent queda en null).
    /// </summary>
    bool DeriveDeliveryFromInstalled(ComponentSlot slot)
    {
        var obj = slot != null ? slot.InstalledObject : null;
        if (obj == null) return false;

        if (obj.TryGetComponent<Resistor>(out var r))
        {
            _pendingType  = ComponentType.Resistor;
            _pendingValue = r.resistance;
        }
        else if (obj.TryGetComponent<LED>(out var led))
        {
            _pendingType  = ComponentType.LED;
            _pendingValue = led.polarityInverted ? -1f : 1f;
        }
        else if (obj.TryGetComponent<Capacitor>(out var cap))
        {
            _pendingType  = ComponentType.Capacitor;
            _pendingValue = cap.polarityInverted ? -1f : 1f;
        }
        else if (obj.TryGetComponent<ArduinoPin>(out var pin))
        {
            _pendingType  = ComponentType.ArduinoPin;
            _pendingValue = pin.pinNumber;
        }
        else return false;

        _waitingForInstall = true;
        _spawnedComponent  = null;
        Debug.Log($"[Delivery] (offline) Validando componente colocado: {_pendingType} = {_pendingValue}");
        return true;
    }
}



public enum ComponentType
{
    None,
    Resistor,
    LED,
    Capacitor,
    ArduinoPin
}

/// <summary>
/// Variante VISUAL/física concreta de un componente entregable (color del LED, color del
/// capacitor, orientación del resistor). Viaja por la red junto al ComponentType+valor para que el
/// Explorador instancie EXACTAMENTE la pieza que eligió el Técnico (antes siempre caía a la variante
/// por defecto: enviabas un LED amarillo y llegaba verde, o un resistor vertical y llegaba horizontal).
///
/// 'Default' (0) = prefab base del tipo. 'Auto' = SOLO lado Técnico: se infiere del nombre/prefab del
/// DeskComponent y se resuelve a una variante concreta ANTES de enviar (nunca viaja 'Auto' por red).
/// </summary>
public enum ComponentVariant
{
    Default = 0,
    LedGreen,
    LedRed,
    LedYellow,
    CapacitorBlue,
    CapacitorBlack,
    CapacitorOrange,
    ResistorHorizontal,
    ResistorVertical,
    Auto = 100   // marcador de inferencia; el Técnico lo resuelve antes de enviar
}