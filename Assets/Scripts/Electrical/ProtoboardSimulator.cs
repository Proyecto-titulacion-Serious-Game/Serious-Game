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

        // ProtoboardConnector.Active es una lista GLOBAL compartida por los 2 simuladores (Reto 2 y
        // Reto 4) — sin filtrar, este bucle llamaba Bind(points) con las coordenadas de ESTE tablero
        // sobre TODOS los conectores de la escena, incluidos los del OTRO reto. Como sus componentes
        // están físicamente lejos, Nearest() no encontraba nada dentro de snapRadius y les ponía
        // nodeA/nodeB en null — desconectando (de forma intermitente, según cuál simulador corriera
        // último) un componente ya bien enganchado en su propio reto. Se filtra a solo los conectores
        // cuyo simulador más cercano sea ESTE, así cada tablero únicamente re-engancha lo suyo.
        var connectors = ProtoboardConnector.Active;
        for (int i = 0; i < connectors.Count; i++)
        {
            var c = connectors[i];
            if (c == null) continue;
            if (NearestSimulator(c.transform.position) != this) continue;
            c.Bind(points);
        }
    }

    /// <summary>El ProtoboardSimulator de la escena más cercano a una posición dada (no el primero
    /// que encuentre Unity) — usado para que cada tablero solo re-enganche SUS propios componentes.</summary>
    static ProtoboardSimulator NearestSimulator(Vector3 worldPos)
    {
        var all = FindObjectsByType<ProtoboardSimulator>(FindObjectsSortMode.None);
        ProtoboardSimulator best = null;
        float bestSqr = float.MaxValue;
        foreach (var s in all)
        {
            if (s == null) continue;
            float d = (s.transform.position - worldPos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = s; }
        }
        return best;
    }

    /// <summary>
    /// Separación FÍSICA real (m) entre los 2 slots más cercanos que pertenecen a NETS distintos
    /// (railId distinto) — la distancia mínima real que debe alcanzar un componente para puentear
    /// dos huecos utilizables. NO es "hueco vecino dentro de la misma fila": cada railId agrupa
    /// varios slots del MISMO net eléctrico, físicamente separados entre sí (igual que el riel GND
    /// real, documentado en el proyecto), así que esa distancia no sirve como referencia de tamaño.
    /// Devuelve 0 si hay menos de 2 nets distintos.
    /// </summary>
    public float SepararacionMinimaEntreNetsDistintos()
    {
        var slots = todosLosSlots.Where(s => s != null).ToList();
        float min = float.MaxValue;
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
            {
                if (slots[i].railId == slots[j].railId) continue;   // mismo net, no cuenta
                float d = Vector3.Distance(slots[i].transform.position, slots[j].transform.position);
                if (d < min) min = d;
            }
        return min == float.MaxValue ? 0f : min;
    }

    /// <summary>Reúne todos los puntos de conexión: slots de protoboard + headers de pin + GND.</summary>
    List<ConnectionPoint> GatherConnectionPoints()
    {
        var pts = new List<ConnectionPoint>();

        foreach (var slot in todosLosSlots)
            if (slot != null && slot.assignedNode != null)
                pts.Add(new ConnectionPoint(slot.transform.position, slot.assignedNode));

        // Hay 2 ProtoboardSimulator en la escena (Reto 2 y Reto 4). Arduino y Bareboard son HERMANOS
        // bajo Reto4_TiltGroup (no padre-hijo), así que GetComponentInChildren no basta para Reto 4 —
        // pero un FindAnyObjectByType global SÍ es peligroso: antes de restaurar el ArduinoCore de hoy
        // devolvía null siempre (dormido), pero ahora el simulador del Reto 2 (que no tiene Arduino
        // propio) también lo encontraría por búsqueda global y le "contagiaría" los pines/GND del
        // Reto 4 como huecos enchufables. Se acota la búsqueda al padre común (Reto4_TiltGroup para
        // Bareboard, Bareboard-del-Reto2 para el otro) — ahí el Reto 2 nunca encuentra nada.
        if (_arduino == null)
            _arduino = GetComponentInChildren<ArduinoCore>(true)
                    ?? (transform.parent != null ? transform.parent.GetComponentInChildren<ArduinoCore>(true) : null);

        if (_arduino != null)
        {
            foreach (var m in _arduino.pinNodeMap)
                if (m.node != null)
                    pts.Add(new ConnectionPoint(m.node.transform.position, m.node));

            // El modelo físico del Arduino trae VARIOS pines GND (header digital, header de poder,
            // AREF...), todos son el mismo net eléctrico. Antes solo se registraba _arduino.nodoGND
            // como único hueco enchufable → los otros GND del mesh (con su propio collider/mesh
            // visibles) nunca respondían al imán de CableProbePlug. Se registran TODOS los
            // ElectricalNode "Nodo_GND*" del modelo como huecos, todos apuntando al MISMO nodo
            // lógico (_arduino.nodoGND) — cualquiera de ellos cierra el circuito, como en la vida real.
            if (_arduino.nodoGND != null)
            {
                foreach (var n in _arduino.GetComponentsInChildren<ElectricalNode>(true))
                    if (n != null && n.name.StartsWith("Nodo_GND"))
                        pts.Add(new ConnectionPoint(n.transform.position, _arduino.nodoGND));
            }

            // Cabecera analógica (Nodo_A0..A5): a diferencia de GND (un único net compartido), cada
            // pin analógico es su PROPIO ElectricalNode físico en el modelo — solo A0 tiene lectura
            // real en el juego (GetAnalogReadA0), pero los demás (A1-A5) deben poder ENCHUFARSE igual
            // (el imán no distinguía "pin sin lógica" de "pin sin registrar": antes de este fix
            // ninguno de los 6 aparecía como hueco, solo nodoA0 si estaba asignado a mano).
            foreach (var n in _arduino.GetComponentsInChildren<ElectricalNode>(true))
                if (n != null && n.name.StartsWith("Nodo_A") && !n.name.StartsWith("Nodo_ARDUINO"))
                    pts.Add(new ConnectionPoint(n.transform.position, n));
        }
        return pts;
    }

    /// <summary>
    /// Todos los componentes del sandbox: hijos del simulador + cualquiera con
    /// <see cref="ProtoboardConnector"/> (aunque esté spawneado fuera de la jerarquía).
    /// </summary>
    List<ElectricalComponent> AllSandboxComponents()
    {
        // Mismo filtro por proximidad que BindConnectors(): sin esto, el resistor/LED del OTRO reto
        // (con nodeA/nodeB apuntando a nodos que este simulador nunca metió en su BuildNodeMap) se
        // colaba en el solver MNA de este tablero.
        return GetComponentsInChildren<ElectricalComponent>(true)
            .Concat(ProtoboardConnector.Active
                .Where(pc => pc != null && NearestSimulator(pc.transform.position) == this)
                .Select(pc => pc.GetComponent<ElectricalComponent>()))
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
            ? "[Reto4] Validación: ✓ CIRCUITO COMPLETO (todos los pines activos cierran seguro a GND)."
            : $"[Reto4] Validación: ✗ {result.message}", this);
        OnSandboxValidated?.Invoke(result);
    }

    /// <summary>
    /// Evalúa el circuito LIBRE del Reto 4: "que el código del Técnico y el cableado del
    /// Explorador encajen de forma segura", sin exigir un patrón concreto (un LED parpadeando,
    /// un semáforo de varios pines, corriente continua sin LED, etc.).
    ///
    /// Algoritmo (grafo dirigido + backtracking, por cada pin activo):
    ///   1. Tomar todos los pines que estuvieron activos recientemente
    ///      (<see cref="ArduinoCore.PinsRecentlyDriven"/> — no un único pin global).
    ///   2. Por cada uno, FindPath desde su nodo hasta GND respetando la dirección del diodo:
    ///        el LED solo es recorrible ánodo → cátodo, así que un LED invertido bloquea el
    ///        camino por topología. Si no hay camino dirigido pero sí uno ignorando el diodo
    ///        → falla = "LED invertido".
    ///   3. Si el camino tiene LED: válido si su estado YA resuelto por el MNA de este tick
    ///      (<see cref="LED.state"/>) es <see cref="LEDState.Correct"/> — sin recalcular ni
    ///      exigir una resistencia mínima fija, el MNA ya refleja si la protección alcanza.
    ///   4. Si el camino NO tiene LED (corriente continua): válido si ningún resistor del
    ///      camino está sobrecargado (<see cref="Resistor.isOverloaded"/>) y el circuito no
    ///      está en cortocircuito.
    ///   5. Éxito solo si TODOS los pines activos tienen un camino válido.
    /// </summary>
    SandboxValidationResult EvaluateSandbox(ArduinoCore arduino)
    {
        var r = new SandboxValidationResult();

        var candidatePins = arduino.PinsRecentlyDriven().OrderBy(p => p).ToList();
        if (candidatePins.Count == 0)
            return Fail(r, "Ningún pin del Arduino está activo ahora mismo. Verifica que el " +
                            "sketch esté corriendo (setup+loop) y escriba HIGH o PWM en algún " +
                            "pin OUTPUT.");

        r.activatedPin  = candidatePins[0];
        r.blinkEnabled  = true;   // al menos un pin OUTPUT está activo ahora (ya no exige parpadeo)

        ElectricalNode gndNode = arduino.nodoGND;
        if (gndNode == null)
            return Fail(r, "Nodo GND del Arduino no asignado en el Inspector de ArduinoCore.");

        var allComps = AllSandboxComponents()
            .Where(c => c.nodeA != null && c.nodeB != null && !(c is VoltageSource))
            .ToList();

        if (allComps.Count == 0)
            return Fail(r, "No hay componentes colocados en la protoboard.");

        var adj = BuildAdjacency(allComps);
        bool anySuccess = false;
        string firstFailure = null;

        foreach (int pin in candidatePins)
        {
            ElectricalNode startNode = arduino.PinToNode(pin);
            if (startNode == null)
            {
                firstFailure ??= $"Pin D{pin} no tiene nodo eléctrico asignado en el modelo 3D " +
                                  "del Arduino. Usa el Inspector de ArduinoCore para añadir el " +
                                  "mapeo en 'Pin Node Map'.";
                continue;
            }

            if (!adj.ContainsKey(startNode))
            {
                firstFailure ??= $"Ningún componente conectado al pin D{pin} en la protoboard. " +
                                  "Conecta un cable desde ese pin.";
                continue;
            }

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

                firstFailure ??= (physicallyClosed && anyPath.OfType<LED>().Any())
                    ? $"El LED del pin D{pin} está con la polaridad invertida. Gíralo 180° — " +
                      "el ánodo (patita larga) debe apuntar al pin."
                    : $"El circuito desde el pin D{pin} no llega a GND (conexión abierta). Cierra el camino hasta GND.";
                continue;
            }

            r.pathFound = true;
            var leds = pathFound.OfType<LED>().ToList();

            if (leds.Count > 0)
            {
                // Camino CON LED: confiar en el estado ya resuelto por el MNA de este tick
                // (evita recalcular con una fórmula cerrada que podría desincronizarse).
                // r.hasLED se marca ANTES del chequeo de estado: el LED SÍ está en el camino
                // aunque esté sobrecargado/apagado, así Clasificar() no lo confunde con "sin LED".
                r.hasLED = true;
                var badLed = leds.FirstOrDefault(l => l.state != LEDState.Correct);
                if (badLed != null)
                {
                    firstFailure ??= badLed.state == LEDState.Off
                        ? $"El LED del pin D{pin} no enciende (corriente insuficiente)."
                        : $"El LED del pin D{pin} recibe demasiada corriente ({badLed.state}). " +
                          "Aumenta la resistencia (330 Ω recomendado).";
                    // hasProtection refleja si hay una resistencia >=100 Ω real en el camino:
                    // si existe pero no alcanza, es CorrienteAlta; si no existe ninguna, SinResistencia.
                    r.hasProtection    = pathFound.OfType<Resistor>().Any(res => res.resistance >= 100f);
                    r.ledUnderCurrent  = badLed.state == LEDState.Off;
                    r.currentMa        = Mathf.Abs(badLed.current) * 1000f;
                    continue;
                }
                r.ledForwardBiased = true;
                r.hasProtection    = true;
                r.currentMa        = Mathf.Abs(leds[0].current) * 1000f;
            }
            else
            {
                // Camino SIN LED: corriente continua válida si nada está sobrecargado.
                var overloaded = pathFound.OfType<Resistor>().FirstOrDefault(res => res.isOverloaded);
                if (overloaded != null || isShortCircuited)
                {
                    firstFailure ??= $"La rama del pin D{pin} está en sobrecarga o cortocircuito. " +
                                      "Aumenta la resistencia entre el pin y GND.";
                    continue;
                }
                r.hasProtection = true;
                r.currentMa     = Mathf.Abs(pathFound.FirstOrDefault()?.current ?? 0f) * 1000f;
            }

            anySuccess = true;
        }

        if (!anySuccess || firstFailure != null)
            return Fail(r, firstFailure ?? "El circuito no cumple el objetivo todavía.");

        // ── ¡Todo OK! ─────────────────────────────────────────────────────
        r.success = true;
        r.message = candidatePins.Count == 1
            ? $"¡Circuito validado! Pin D{candidatePins[0]} · I ≈ {r.currentMa:F1} mA · conexión segura."
            : $"¡Circuito validado! {candidatePins.Count} pines activos (D{string.Join(", D", candidatePins)}), todos con conexión segura a GND.";
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
