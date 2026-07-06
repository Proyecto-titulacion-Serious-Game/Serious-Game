using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// SandboxValidationResult se movió a su propio archivo (SandboxValidationResult.cs)
// para que Unity asocie el MonoScript de este archivo con el MonoBehaviour
// ProtoboardSimulator y no con el struct (evita el error ExtensionOfNativeClass).

/// <summary>
/// Motor matemático de la protoboard sandbox (Reto 4).
/// Monitorea la matriz de ProtoboardSlots, construye el grafo eléctrico por railId
/// y calcula V, I y P mediante análisis nodal simplificado.
///
/// Diferencia con CircuitSimulator (Gameplay/): la topología se deduce dinámicamente
/// de qué componentes/cables están colocados en qué slots, sin listas hardcodeadas.
///
/// SETUP: añadir este script al GameObject padre de la protoboard y rellenar
/// todosLosSlots usando Tools > TITA > Generador de Slots.
/// </summary>
public class ProtoboardSimulator : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Protoboard")]
    [Tooltip("Todos los ProtoboardSlots de la cuadrícula. Rellenar con el generador de Editor.")]
    public List<ProtoboardSlot> todosLosSlots = new List<ProtoboardSlot>();

    [Header("Telemetría (solo lectura)")]
    [SerializeField] private float _sourceVoltage;
    [SerializeField] private float _totalCurrentmA;   // miliamperios
    [SerializeField] private float _totalPowerW;      // Watts
    [SerializeField] private bool  _isShortCircuited;
    [SerializeField] private bool  _isOpenCircuit;

    [Header("Simulación")]
    [SerializeField] private float _interval = 0.05f; // 20 Hz

    // ─────────────────────────────────────────────
    //  Propiedades públicas (lectura de telemetría)
    // ─────────────────────────────────────────────
    public float sourceVoltage    => _sourceVoltage;
    /// <summary>Corriente total en miliamperios (mA).</summary>
    public float totalCurrentmA   => _totalCurrentmA;
    /// <summary>Potencia total disipada en Watts (W).</summary>
    public float totalPowerW      => _totalPowerW;
    public bool  isShortCircuited => _isShortCircuited;
    public bool  isOpenCircuit    => _isOpenCircuit;

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────
    /// <summary>Dispara cada vez que el simulador recalcula el circuito.</summary>
    public static event Action OnCircuitChanged;

    /// <summary>
    /// Dispara cuando el resultado de la validación sandbox cambia.
    /// Suscribirse en <see cref="InstructionSystem"/> y <see cref="GameManager"/>
    /// para la condición de victoria desacoplada del Reto 4.
    /// </summary>
    public static event Action<SandboxValidationResult> OnSandboxValidated;

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private bool _dirty = true;
    private ArduinoCore _arduino;
    private SandboxValidationResult _lastSandboxResult;

    // ─────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────
    void OnEnable()
    {
        StartCoroutine(SimLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────
    /// <summary>Solicita una nueva simulación en el próximo tick.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>
    /// Ejecuta simulación + validación AHORA (síncrono), sin esperar al próximo tick del SimLoop.
    /// Lo usa el botón "Comprobar circuito" del Reto 4 para que un solo toque refleje el estado
    /// actual del circuito (dispara <see cref="OnSandboxValidated"/> al instante).
    /// </summary>
    public void ForzarValidacion()
    {
        _dirty = false;
        RunSimulation();
        OnCircuitChanged?.Invoke();
        ValidateSandboxObjective();
    }

    // ─────────────────────────────────────────────
    //  Bucle de simulación
    // ─────────────────────────────────────────────
    IEnumerator SimLoop()
    {
        var wait = new WaitForSeconds(_interval);
        while (true)
        {
            if (_dirty)
            {
                _dirty = false;
                RunSimulation();
                OnCircuitChanged?.Invoke();
                ValidateSandboxObjective();
            }
            yield return wait;
        }
    }

    // ─────────────────────────────────────────────
    //  Núcleo: construcción del mapa de nodos
    // ─────────────────────────────────────────────

    /// <summary>
    /// Agrupa los slots por railId. El primer slot de cada grupo actúa como nodo
    /// representativo (se le añade o recicla su ElectricalNode component).
    /// </summary>
    void BuildNodeMap()
    {
        var representatives = new Dictionary<string, ElectricalNode>();

        foreach (var slot in todosLosSlots)
        {
            if (slot == null || string.IsNullOrEmpty(slot.railId)) continue;   // entradas null/destruidas en la lista

            if (!representatives.TryGetValue(slot.railId, out ElectricalNode node))
            {
                node = slot.GetComponent<ElectricalNode>();
                if (node == null) node = slot.gameObject.AddComponent<ElectricalNode>();
                node.voltage = 0f;
                node.current = 0f;
                representatives[slot.railId] = node;
            }

            slot.assignedNode = node;
        }
    }

    /// <summary>
    /// Nodo eléctrico representativo de un railId (VCC, GND, COL_0, …). Lo usan los conectores
    /// en modo DETERMINISTA (ProtoboardConnector.lockNodes) para cablearse por nombre de riel en
    /// vez de por posición física. Asegura el mapa de nodos si aún no está construido.
    /// </summary>
    public ElectricalNode NodeForRail(string railId)
    {
        if (string.IsNullOrEmpty(railId)) return null;

        foreach (var s in todosLosSlots)
            if (s != null && s.railId == railId && s.assignedNode != null) return s.assignedNode;

        // Aún no hay mapa (p.ej. lo llamamos antes del primer RunSimulation) → construirlo y reintentar.
        BuildNodeMap();
        foreach (var s in todosLosSlots)
            if (s != null && s.railId == railId && s.assignedNode != null) return s.assignedNode;

        return null;
    }

    // ─────────────────────────────────────────────
    //  Enganche físico → eléctrico (cables/patas → nodos)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Re-engancha cada componente con <see cref="ProtoboardConnector"/> al nodo más cercano
    /// (slot de protoboard o header de pin del Arduino). Esto es lo que conecta físicamente
    /// el circuito que arma el Explorador con el grafo eléctrico.
    /// </summary>
    // Puntos de conexión del último BindConnectors (slots + pines Arduino + GND). Los slots/pines
    // no se mueven, así que el "imán" de las puntas de cable (CableProbePlug) puede leerlos aunque
    // sean de un tick atrás para enchufarse al hueco más cercano al soltar.
    private List<ConnectionPoint> _cachedPoints = new List<ConnectionPoint>();
    /// <summary>Huecos enchufables (solo lectura): slots de protoboard + headers de pin + GND.</summary>
    public IReadOnlyList<ConnectionPoint> ConnectionPoints => _cachedPoints;

    void BindConnectors()
    {
        // Recolectar SIEMPRE (aunque no haya conectores aún) para mantener ConnectionPoints fresco
        // para el imán de enchufe; gatherear slots+pines es barato y solo corre cuando _dirty (20 Hz).
        var points = GatherConnectionPoints();
        _cachedPoints = points;

        var connectors = ProtoboardConnector.Active;
        for (int i = 0; i < connectors.Count; i++)
            if (connectors[i] != null) connectors[i].Bind(points);
    }

    /// <summary>Reúne todos los puntos de conexión: slots de protoboard + headers de pin + GND.</summary>
    List<ConnectionPoint> GatherConnectionPoints()
    {
        var pts = new List<ConnectionPoint>();

        foreach (var slot in todosLosSlots)
            if (slot != null && slot.assignedNode != null)
                pts.Add(new ConnectionPoint(slot.transform.position, slot.assignedNode));

        if (_arduino == null)
            _arduino = GetComponentInChildren<ArduinoCore>(true) ?? FindAnyObjectByType<ArduinoCore>();

        if (_arduino != null)
        {
            foreach (var m in _arduino.pinNodeMap)
                if (m.node != null)
                    pts.Add(new ConnectionPoint(m.node.transform.position, m.node));
            if (_arduino.nodoGND != null)
                pts.Add(new ConnectionPoint(_arduino.nodoGND.transform.position, _arduino.nodoGND));
        }
        return pts;
    }

    /// <summary>
    /// Todos los componentes del sandbox: hijos del simulador + cualquiera con
    /// <see cref="ProtoboardConnector"/> (aunque esté spawneado fuera de la jerarquía).
    /// </summary>
    List<ElectricalComponent> AllSandboxComponents()
    {
        return GetComponentsInChildren<ElectricalComponent>(true)
            .Concat(ProtoboardConnector.Active.Select(pc => pc != null ? pc.GetComponent<ElectricalComponent>() : null))
            .Where(c => c != null)
            .Distinct()
            .ToList();
    }

    // ─────────────────────────────────────────────
    //  Núcleo: simulación eléctrica
    // ─────────────────────────────────────────────
    void RunSimulation()
    {
        BuildNodeMap();
        BindConnectors();

        var allComps = AllSandboxComponents()
            .Where(c => c.nodeA != null && c.nodeB != null)
            .ToList();

        var passiveComps = allComps.Where(c => !(c is VoltageSource)).ToList();
        var source = allComps.OfType<VoltageSource>().FirstOrDefault();

        // ── CASO A: hay una batería (Retos 1-3 con VoltageSource) → fuente única ──
        if (source != null)
        {
            SimulateSingleSource(passiveComps, source.nodeA, source.voltage, source.nodeB);
            return;
        }

        // ── CASO B: sandbox Arduino (Reto 4) → MULTI-PIN ─────────────────────────
        if (_arduino == null) _arduino = GetComponentInChildren<ArduinoCore>(true)
                                      ?? FindAnyObjectByType<ArduinoCore>();
        if (_arduino == null || _arduino.nodoGND == null || passiveComps.Count == 0)
        {
            ClearTelemetry(openCircuit: true);
            return;
        }

        // Cada pin de salida encendido es una fuente independiente hacia GND. Su voltaje es
        // duty01 × 5V: un digitalWrite(HIGH) da 5V; un analogWrite(PWM) da el promedio temporal.
        var highPins = _arduino.ActivePinStates()
            .Where(s => s.duty01 > 0f && s.node != null)
            .Select(s => (node: s.node, v: s.duty01 * _arduino.outputVoltageTTL))
            .ToList();

        SimulateArduinoMultiPin(passiveComps, highPins, _arduino.nodoGND);
    }

    /// <summary>Simulación clásica de una sola fuente (batería de los Retos 1-3).</summary>
    void SimulateSingleSource(List<ElectricalComponent> passiveComps,
                              ElectricalNode srcNodeA, float srcV, ElectricalNode srcNodeB)
    {
        if (srcV <= 0.001f || passiveComps.Count == 0 || srcNodeA == null || srcNodeB == null)
        {
            ClearTelemetry(openCircuit: true);
            return;
        }

        _sourceVoltage = srcV;

        bool solved = CircuitGraphAnalyzer.SolveMNA(passiveComps, srcNodeA, srcV, srcNodeB);
        if (!solved) { ApplyShort(passiveComps, srcV); return; }

        float totalI = CurrentOutOf(srcNodeA, passiveComps);
        _isShortCircuited = totalI > 1.0f;
        _isOpenCircuit    = totalI < 0.0001f;
        _totalCurrentmA   = totalI * 1000f;
        _totalPowerW      = srcV * totalI;

        foreach (var comp in passiveComps)
        {
            if (comp is LED led) led.ApplyResolvedCurrent();
            else                 comp.Calculate();
        }
    }

    /// <summary>
    /// Simulación MULTI-PIN del Reto 4: resuelve el MNA una vez por cada pin encendido
    /// (cada uno como fuente independiente 5V→GND) y, para cada LED, conserva la mayor
    /// corriente recibida. Así varios LEDs en pines distintos encienden de forma SELECTIVA
    /// según lo que programó el Técnico (semáforos, secuencias, etc.).
    /// </summary>
    void SimulateArduinoMultiPin(List<ElectricalComponent> passiveComps,
                                 List<(ElectricalNode node, float v)> highPins, ElectricalNode gnd)
    {
        if (highPins.Count == 0)
        {
            // Ningún pin encendido ahora (todos LOW / blink en fase OFF) → LEDs apagados.
            foreach (var c in passiveComps) { c.current = 0f; c.voltageDrop = 0f; }
            foreach (var led in passiveComps.OfType<LED>()) { led.current = 0f; led.ApplyResolvedCurrent(); }
            ClearTelemetry(openCircuit: true);
            return;
        }

        _sourceVoltage = 5f;   // telemetría: 5V fijos para el Técnico

        var ledMax  = new Dictionary<LED, float>();
        float totalI = 0f;
        bool  anyShort = false;

        foreach (var (pinNode, srcV) in highPins)
        {
            if (srcV <= 0.001f) continue;
            bool solved = CircuitGraphAnalyzer.SolveMNA(passiveComps, pinNode, srcV, gnd);
            if (!solved) { anyShort = true; continue; }

            totalI += CurrentOutOf(pinNode, passiveComps);

            // Quedarse con la mayor corriente que cada LED recibe en alguna pasada.
            foreach (var led in passiveComps.OfType<LED>())
            {
                float ic = Mathf.Abs(led.current);
                if (!ledMax.TryGetValue(led, out float prev) || ic > prev) ledMax[led] = ic;
            }
        }

        if (anyShort)
        {
            ApplyShort(passiveComps, 5f);
            return;
        }

        _isShortCircuited = totalI > 1.0f;
        _isOpenCircuit    = totalI < 0.0001f;
        _totalCurrentmA   = totalI * 1000f;
        _totalPowerW      = _sourceVoltage * totalI;

        // Aplicar a cada LED su corriente máxima (el pin que lo alimenta); el resto, su última.
        foreach (var comp in passiveComps)
        {
            if (comp is LED led)
            {
                led.current = ledMax.TryGetValue(led, out float ic) ? ic : 0f;
                led.ApplyResolvedCurrent();
            }
            else comp.Calculate();
        }
    }

    /// <summary>Suma la corriente que SALE de un nodo hacia sus componentes vecinos.</summary>
    static float CurrentOutOf(ElectricalNode node, List<ElectricalComponent> comps)
    {
        float i = 0f;
        foreach (var c in comps)
        {
            if      (c.nodeA == node) i += Mathf.Max(0f,  c.current);
            else if (c.nodeB == node) i += Mathf.Max(0f, -c.current);
        }
        return i;
    }

    void ApplyShort(List<ElectricalComponent> passiveComps, float srcV)
    {
        _isShortCircuited = true;
        _isOpenCircuit    = false;
        float faultI      = srcV / 0.1f;
        _totalCurrentmA   = faultI * 1000f;
        _totalPowerW      = srcV * faultI;
        foreach (var c in passiveComps) { c.current = faultI; c.voltageDrop = 0f; }
    }

    void ClearTelemetry(bool openCircuit)
    {
        _sourceVoltage    = 0f;
        _totalCurrentmA   = 0f;
        _totalPowerW      = 0f;
        _isShortCircuited = false;
        _isOpenCircuit    = openCircuit;
    }

    // ═════════════════════════════════════════════
    //  SANDBOX VALIDATION — Reto 4 dinámico
    // ═════════════════════════════════════════════

    /// <summary>
    /// Punto de entrada del validador sandbox.
    /// Se llama automáticamente tras cada simulación. Emite <see cref="OnSandboxValidated"/>
    /// solo cuando el resultado cambia, para no saturar suscriptores.
    /// </summary>
    void ValidateSandboxObjective()
    {
        if (_arduino == null) _arduino = FindAnyObjectByType<ArduinoCore>();
        if (_arduino == null) return;

        var result = EvaluateSandbox(_arduino);

        // Solo disparar si el resultado cambió (éxito o mensaje distinto)
        if (result.success == _lastSandboxResult.success &&
            result.message == _lastSandboxResult.message) return;

        _lastSandboxResult = result;
        Debug.Log(result.success
            ? "[Reto4] Validación: ✓ CIRCUITO COMPLETO (Pin → R≥100Ω → LED → GND, blink seguro)."
            : $"[Reto4] Validación: ✗ {result.message}", this);
        OnSandboxValidated?.Invoke(result);
    }

    /// <summary>
    /// Evalúa si el circuito cumple el objetivo sandbox:
    /// "Haz parpadear un LED de forma segura desde cualquier pin digital."
    ///
    /// Algoritmo (grafo dirigido + backtracking):
    ///   1. Verificar que el sketch tiene BLINK + OUTPUT activos en ArduinoCore.
    ///   2. Resolver el nodo del pin activado vía <see cref="ArduinoCore.PinToNode"/>.
    ///   3. FindPath desde ese nodo hasta nodoGND respetando la dirección del diodo:
    ///        el LED solo es recorrible ánodo → cátodo, así que un LED invertido
    ///        bloquea el camino por topología (no por un flag). Si no hay camino
    ///        dirigido pero sí uno ignorando el diodo → falla = "LED invertido".
    ///   4. Sobre el camino encontrado: exigir LED + resistencia >= 100 Ω (protección).
    ///   5. Verificar que la corriente estimada está en rango seguro (≤ maxSafeCurrent).
    /// </summary>
    SandboxValidationResult EvaluateSandbox(ArduinoCore arduino)
    {
        var r = new SandboxValidationResult { activatedPin = arduino.activePinNumber };

        // ── Condición 1: sketch con BLINK y OUTPUT ───────────────────────
        if (arduino.activePinMode != PinMode.OUTPUT)
            return Fail(r, "El pin del Arduino no está en modo OUTPUT. " +
                            "Agrega 'pinMode(X, OUTPUT)' en setup().");

        r.blinkEnabled = arduino.blinkEnabled;
        if (!arduino.blinkEnabled)
            return Fail(r, $"Pin D{arduino.activePinNumber} en OUTPUT pero sin BLINK. " +
                            "El objetivo es hacer parpadear el LED.");

        // ── Condición 2: nodo del pin activo existe ───────────────────────
        ElectricalNode startNode = arduino.PinToNode(arduino.activePinNumber);
        ElectricalNode gndNode   = arduino.nodoGND;

        if (startNode == null)
            return Fail(r, $"Pin D{arduino.activePinNumber} no tiene nodo eléctrico asignado " +
                            "en el modelo 3D del Arduino. Usa el Inspector de ArduinoCore " +
                            "para añadir el mapeo en 'Pin Node Map'.");

        if (gndNode == null)
            return Fail(r, "Nodo GND del Arduino no asignado en el Inspector de ArduinoCore.");

        // ── Condición 3: recorrido por el GRAFO DIRIGIDO de la protoboard ─
        // El LED se modela como arista dirigida (ánodo → cátodo): el diodo NO
        // conduce al revés, así que un LED mal orientado bloquea físicamente el
        // camino — la polaridad se valida por topología, no por un flag.
        var allComps = AllSandboxComponents()
            .Where(c => c.nodeA != null && c.nodeB != null && !(c is VoltageSource))
            .ToList();

        if (allComps.Count == 0)
            return Fail(r, "No hay componentes colocados en la protoboard.");

        var adj = BuildAdjacency(allComps);
        if (!adj.ContainsKey(startNode))
            return Fail(r, $"Ningún componente conectado al pin D{arduino.activePinNumber} " +
                            "en la protoboard. Conecta un cable desde ese pin.");

        // Búsqueda 1: camino respetando la dirección del diodo (la verdad eléctrica)
        var pathFound = new List<ElectricalComponent>();
        bool reached = FindPath(startNode, gndNode, adj, respectDiode: true,
                                new HashSet<ElectricalNode>(),
                                new List<ElectricalComponent>(),
                                pathFound);

        if (!reached)
        {
            // Búsqueda 2 (diagnóstico): ¿existe el camino si ignoramos la dirección
            // del diodo? Si sí y hay un LED, la falla es exactamente la polaridad.
            var anyPath = new List<ElectricalComponent>();
            bool physicallyClosed = FindPath(startNode, gndNode, adj, respectDiode: false,
                                             new HashSet<ElectricalNode>(),
                                             new List<ElectricalComponent>(),
                                             anyPath);

            if (physicallyClosed && anyPath.OfType<LED>().Any())
                return Fail(r, "El LED está con la polaridad invertida. " +
                                "Gíralo 180° — el ánodo (patita larga) debe apuntar al pin.");

            return Fail(r, $"El circuito desde D{arduino.activePinNumber} no llega a GND. " +
                            "Asegúrate de cerrar el circuito: Pin → Resistencia → LED → GND.");
        }

        r.pathFound = true;

        // ── Condición 4: LED en el camino (ya garantizado forward por el grafo) ─
        var leds = pathFound.OfType<LED>().ToList();
        if (leds.Count == 0)
            return Fail(r, "No se detectó ningún LED en el camino Pin → GND. " +
                            "Coloca un LED entre el pin y GND.");

        var forwardLED = leds[0]; // el grafo dirigido garantiza que se cruzó ánodo→cátodo

        r.hasLED           = true;
        r.ledForwardBiased = true;

        // ── Condición 5: resistencia de protección ────────────────────────
        var protectionR = pathFound.OfType<Resistor>()
                                   .FirstOrDefault(res => res.GetResistance() >= 100f);
        if (protectionR == null)
            return Fail(r, "Falta una resistencia de al menos 100 Ω entre el pin y GND. " +
                            "Sin ella el LED puede quemarse por exceso de corriente.");

        r.hasProtection = true;

        // ── Condición 6: corriente estimada en rango seguro ───────────────
        // I = (Vfuente − ΣVf_diodos) / Rtotal — consistente con el modelo de diodo
        // del MNA (CircuitGraphAnalyzer): la caída directa del LED no impulsa corriente.
        float totalR  = pathFound.Sum(c => c.GetResistance());
        if (totalR < 0.1f) totalR = 0.1f;
        float vDiodes = pathFound.OfType<LED>().Sum(l => l.forwardVoltage);
        float currentA = Mathf.Max(0f, arduino.outputVoltageTTL - vDiodes) / totalR;
        r.currentMa    = currentA * 1000f;

        if (r.currentMa > forwardLED.maxSafeCurrent * 1000f)
            return Fail(r, $"Corriente estimada {r.currentMa:F1} mA supera el límite seguro " +
                            $"del LED ({forwardLED.maxSafeCurrent * 1000f:F0} mA). " +
                            "Aumenta la resistencia (330 Ω recomendado).");

        // ── ¡Todo OK! ─────────────────────────────────────────────────────
        r.success = true;
        r.message = $"¡Circuito validado! Pin D{arduino.activePinNumber} · " +
                    $"BLINK {arduino.blinkIntervalOnMs}ms · " +
                    $"I ≈ {r.currentMa:F1} mA · LED encendido de forma segura.";
        return r;
    }

    // ── Utilidades de grafo ──────────────────────────────────────────────

    /// <summary>
    /// Construye la lista de adyacencia del grafo eléctrico con dirección de diodo.
    /// Cables y resistencias generan dos aristas <c>forward</c> (recorribles en
    /// ambos sentidos). Un <see cref="LED"/> genera dos aristas, pero solo la que
    /// va de ánodo → cátodo se marca <c>forward = true</c>; la inversa queda
    /// <c>forward = false</c> y <see cref="FindPath"/> la descarta cuando
    /// <c>respectDiode</c> está activo. El ánodo es <c>nodeA</c> salvo que el LED
    /// tenga <see cref="LED.polarityInverted"/> = true.
    /// </summary>
    static Dictionary<ElectricalNode, List<(ElectricalNode node, ElectricalComponent comp, bool forward)>>
        BuildAdjacency(List<ElectricalComponent> comps)
    {
        var adj = new Dictionary<ElectricalNode, List<(ElectricalNode, ElectricalComponent, bool)>>();

        void AddEdge(ElectricalNode from, ElectricalNode to, ElectricalComponent c, bool forward)
        {
            if (!adj.TryGetValue(from, out var list))
            {
                list = new List<(ElectricalNode, ElectricalComponent, bool)>();
                adj[from] = list;
            }
            list.Add((to, c, forward));
        }

        foreach (var c in comps)
        {
            if (c is LED led)
            {
                // ánodo = nodeA salvo polaridad invertida; el diodo solo conduce ánodo→cátodo
                bool anodeIsA = !led.polarityInverted;
                AddEdge(c.nodeA, c.nodeB, c,  anodeIsA);  // A→B forward solo si A es ánodo
                AddEdge(c.nodeB, c.nodeA, c, !anodeIsA);  // B→A forward solo si B es ánodo
            }
            else
            {
                AddEdge(c.nodeA, c.nodeB, c, true);
                AddEdge(c.nodeB, c.nodeA, c, true);
            }
        }
        return adj;
    }

    /// <summary>
    /// DFS con backtracking sobre el grafo dirigido. Encuentra la primera ruta
    /// desde <paramref name="current"/> hasta <paramref name="target"/> y la escribe
    /// en <paramref name="outPath"/>.
    ///
    /// Cuando <paramref name="respectDiode"/> es true, las aristas de LED marcadas
    /// <c>forward = false</c> se omiten — modela que un diodo no deja pasar corriente
    /// en sentido inverso. Con <paramref name="respectDiode"/> = false el grafo se
    /// recorre como no dirigido (usado para diagnosticar "LED invertido").
    /// </summary>
    static bool FindPath(
        ElectricalNode current,
        ElectricalNode target,
        Dictionary<ElectricalNode, List<(ElectricalNode node, ElectricalComponent comp, bool forward)>> adj,
        bool respectDiode,
        HashSet<ElectricalNode> visited,
        List<ElectricalComponent> pathSoFar,
        List<ElectricalComponent> outPath)
    {
        if (current == target)
        {
            outPath.AddRange(pathSoFar);
            return true;
        }

        visited.Add(current);

        if (adj.TryGetValue(current, out var neighbors))
        {
            foreach (var (next, comp, forward) in neighbors)
            {
                if (visited.Contains(next)) continue;
                if (respectDiode && comp is LED && !forward) continue; // diodo bloquea sentido inverso

                pathSoFar.Add(comp);
                if (FindPath(next, target, adj, respectDiode, visited, pathSoFar, outPath))
                    return true;
                pathSoFar.RemoveAt(pathSoFar.Count - 1);
            }
        }

        visited.Remove(current); // backtrack para explorar caminos alternativos
        return false;
    }

    static SandboxValidationResult Fail(SandboxValidationResult r, string msg)
    {
        r.success = false;
        r.message = msg;
        return r;
    }
}
