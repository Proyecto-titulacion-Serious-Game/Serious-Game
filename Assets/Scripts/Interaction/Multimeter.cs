using UnityEngine;
using TMPro;

/// <summary>
/// Multímetro de banco (panel fijo en la pared) para el Explorador.
///
/// FLUJO:
///   1. El cuerpo está fijo en la pared de cada Reto_Zone — no se agarra.
///   2. El jugador estira las puntas (Rod_Visual, con su propio XRGrabInteractable)
///      hasta los nodos del circuito. Apunta el controlador DERECHO al nodo a medir
///      → gatillo → punta roja asignada. Igual con el IZQUIERDO → punta negra.
///   3. El display muestra voltaje y corriente en tiempo real.
///   4. El Técnico lee los mismos valores en TechnicianUIController
///      mediante las propiedades measuredVoltage / measuredCurrent.
///
/// Un panel por reto (Multimeter_Panel_Art), hijo de cada RetoX_Zone — se activa/
/// desactiva junto con la zona vía GameManager.LoadLevel(). Los consumidores
/// (NodeInteractable, MultimeterModeButton, GameManager, etc.) resuelven "el
/// multímetro activo" con FindAnyObjectByType&lt;Multimeter&gt;() cuando su referencia
/// serializada es null o quedó apuntando a una instancia inactiva — así que solo
/// existe UN Multimeter activo a la vez sin código adicional.
///
/// NO necesita MultimeterProbe.cs ni CircuitNode.cs.
/// Trabaja directamente con ElectricalNode asignado por NodeInteractable.
/// </summary>
public class Multimeter : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Display")]
    public TMP_Text txtVoltage;
    public TMP_Text txtCurrent;
    public TMP_Text txtStatus;
    public TMP_Text txtMode;

    [Header("Indicadores visuales de punta asignada")]
    public Renderer indicatorRed;    // se ilumina verde cuando la punta roja está asignada
    public Renderer indicatorBlack;  // igual para punta negra

    [Header("Modo de medición")]
    public MultimeterMode mode = MultimeterMode.DCVoltage;

    // ─────────────────────────────────────────────
    //  Estado (solo lectura desde inspector)
    // ─────────────────────────────────────────────

    [Header("Lectura actual (solo lectura)")]
    [SerializeField] private float _measuredVoltage;
    [SerializeField] private float _measuredCurrent;
    [SerializeField] private bool  _isReading;

    // Propiedades públicas que lee TechnicianUIController
    public float measuredVoltage => _measuredVoltage;
    public float measuredCurrent => _measuredCurrent;
    public bool  isReading       => _isReading;

    // ─────────────────────────────────────────────
    //  Seguimiento de modo Resistencia (requisito Reto 4)
    // ─────────────────────────────────────────────
    // No se limpia en ResetProbes()/SetMode() (eso pasaría cada vez que se suelta una punta o
    // se cambia de modo) — solo GameManager.LoadLevel() la reinicia al entrar a un reto nuevo.
    [SerializeField] private bool _usedResistanceMode;

    /// <summary>True si en algún momento de este reto se tomó una lectura real (ambas puntas
    /// asignadas) con el multímetro en modo Resistencia. Lo consulta GameManager.EvaluarReto4().</summary>
    public bool wasUsedInResistanceMode => _usedResistanceMode;

    /// <summary>Reinicia el seguimiento de modo Resistencia — llamado por GameManager al cargar reto.</summary>
    public void ResetResistanceModeTracking() => _usedResistanceMode = false;

    // ─────────────────────────────────────────────
    //  Nodos asignados por NodeInteractable
    // ─────────────────────────────────────────────
    private ElectricalNode _nodeRed;
    private ElectricalNode _nodeBlack;

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────
    /// <summary>Se dispara la primera vez que ambas puntas están conectadas y se obtiene una lectura.</summary>
    public static event System.Action OnReadingTaken;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    void Awake()
    {
        _indicatorMpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        TakeReading();
        UpdateDisplay();
    }

    /// <summary>Cicla Voltaje → Corriente → Resistencia (usado por Mode_Button).</summary>
    public void CiclarModo()
    {
        SetMode((MultimeterMode)(((int)mode + 1) % 3));
        Debug.Log($"[Multimeter] Modo → {mode}");
    }

    // ─────────────────────────────────────────────
    //  API pública — llamada por NodeInteractable
    // ─────────────────────────────────────────────

    /// <summary>Asigna el nodo a la punta roja (mano derecha).</summary>
    public void SetRedNode(ElectricalNode node)
    {
        // Loguear solo al CAMBIAR de nodo — esto se llama cada frame de contacto y
        // llegó a meter >50.000 líneas de "Punta roja" en una sola sesión.
        if (node != _nodeRed)
        {
            Debug.Log($"[Multimeter] Punta roja → {node?.gameObject.name} ({node?.voltage:F2}V)");
            _nodeRed = node;
            RecomputeBridgeComponent();
        }
        SetIndicator(indicatorRed, node != null);
    }

    /// <summary>Asigna el nodo a la punta negra (mano izquierda).</summary>
    public void SetBlackNode(ElectricalNode node)
    {
        if (node != _nodeBlack)
        {
            Debug.Log($"[Multimeter] Punta negra → {node?.gameObject.name} ({node?.voltage:F2}V)");
            _nodeBlack = node;
            RecomputeBridgeComponent();
        }
        SetIndicator(indicatorBlack, node != null);
    }

    // ─────────────────────────────────────────────
    //  Componente puente (para lectura de corriente) — cacheado
    // ─────────────────────────────────────────────
    // TakeReading() corre cada Update() mientras ambas puntas están asignadas. Antes escaneaba
    // TODA la escena con FindObjectsByType cada frame para hallar el componente entre las 2
    // puntas — costoso en Quest. Ahora se recalcula solo cuando cambia una punta (ver SetRedNode/
    // SetBlackNode) y TakeReading() reutiliza el resultado cacheado.
    private ElectricalComponent _bridgeComponent;

    // Bug real de playtest (2026-07-26): "el voltaje se ve bien pero CORRIENTE y RESISTENCIA
    // marcan 0 aunque las 2 puntas estén sobre nodos reales".
    //
    // Causa 1 — LA FUENTE NO TIENE CORRIENTE RESUELTA. CircuitManager.ForceSimulate() excluye al
    //   VoltageSource del MNA (passiveComps) y nunca llama a su Calculate(), así que
    //   VoltageSource.current queda en 0 PARA SIEMPRE. Los puntos de medición "oficiales" del
    //   Reto 1 son justamente los bornes de la batería (NP_R1_VCC lleva probeType=Red y
    //   Node_R1_GND probeType=Black en la escena) → el puente hallado era el Battery_9V → I=0 mA
    //   y, en modo Resistencia, 0 Ω; el voltaje (que no depende del puente) seguía bien: 9,00 V.
    // Causa 2 — PAR SIN COMPONENTE EXACTO. Si las puntas abarcan más de un componente (p.ej.
    //   VCC↔Mid = switch + resistencia) ningún componente tiene ESE par de nodos → puente null →
    //   I=0 igual.
    //
    // Solución: el puente pasivo sigue siendo la lectura preferida (es la corriente que atraviesa
    // ESE componente), pero cuando no lo hay se cae al circuito que contiene ambos nodos:
    //   · puntas en los bornes de la fuente → la fuente lleva SIEMPRE la corriente total;
    //   · circuito SERIE sin puente exacto  → la corriente es la misma en todo el lazo.
    // Fuera de esos casos (paralelo/mixto sin puente) no se puede saber → 0 A, como antes.
    private VoltageSource  _bridgeSource;
    private CircuitManager _bridgeCircuit;
    private float          _nextBridgeRescan;   // throttle del rescan de escena (ver TakeReading)

    void RecomputeBridgeComponent()
    {
        _bridgeComponent = null;
        _bridgeSource    = null;
        _bridgeCircuit   = null;
        if (_nodeRed == null || _nodeBlack == null) return;

        var allComps = FindObjectsByType<ElectricalComponent>(FindObjectsInactive.Exclude);
        foreach (var comp in allComps)
        {
            if (comp == null) continue;
            bool bridges = (comp.nodeA == _nodeRed   && comp.nodeB == _nodeBlack) ||
                           (comp.nodeA == _nodeBlack && comp.nodeB == _nodeRed);
            if (!bridges) continue;

            // Preferir SIEMPRE un componente pasivo: la fuente comparte bornes con el resto del
            // lazo y su 'current' nunca se resuelve (ver nota arriba).
            if (comp is VoltageSource vs) { if (_bridgeSource == null) _bridgeSource = vs; continue; }

            _bridgeComponent = comp;
            break;
        }

        if (_bridgeComponent == null)
            _bridgeCircuit = FindCircuitContaining(_nodeRed, _nodeBlack);
    }

    /// <summary>CircuitManager activo cuya lista de componentes toca AMBOS nodos probados.</summary>
    static CircuitManager FindCircuitContaining(ElectricalNode a, ElectricalNode b)
    {
        foreach (var cm in FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
        {
            if (cm == null || cm.components == null) continue;
            bool hasA = false, hasB = false;
            foreach (var c in cm.components)
            {
                if (c == null) continue;
                if (c.nodeA == a || c.nodeB == a) hasA = true;
                if (c.nodeA == b || c.nodeB == b) hasB = true;
            }
            if (hasA && hasB) return cm;
        }
        return null;
    }

    /// <summary>
    /// Corriente que el amperímetro debe mostrar entre las 2 puntas. Ver la nota de
    /// <see cref="RecomputeBridgeComponent"/> para el orden de resolución.
    /// </summary>
    float ResolveProbedCurrent()
    {
        if (_bridgeComponent != null) return Mathf.Abs(_bridgeComponent.current);
        if (_bridgeCircuit   == null) return 0f;

        // Bornes de la fuente: la fuente lleva la corriente total del circuito, sea cual sea la
        // topología. Serie sin puente exacto: la corriente es la misma en todo el lazo.
        if (_bridgeSource != null || _bridgeCircuit.topology == CircuitTopology.Series)
            return Mathf.Abs(_bridgeCircuit.totalCurrent);

        return 0f;
    }

    // ─────────────────────────────────────────────
    //  API para MultimeterProbeContact (protoboard sandbox — Reto 4)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Asigna la punta roja al nodo de un <see cref="ProtoboardSlot"/>.
    /// Llamado por <see cref="MultimeterProbeContact"/> cuando la punta toca un slot.
    /// Voltaje leído por <see cref="TakeReading"/> vía <c>_nodeRed.voltage</c>,
    /// que MNA actualiza cada 20 Hz en <see cref="ProtoboardSimulator"/>.
    /// </summary>
    public void SetRedProbeSlot(ProtoboardSlot slot) =>
        SetRedNode(slot != null ? slot.assignedNode : null);

    /// <summary>
    /// Asigna la punta negra al nodo de un <see cref="ProtoboardSlot"/>.
    /// Si ambas puntas están en el mismo railId, <c>assignedNode</c> será
    /// el mismo objeto → voltaje diferencial = 0 V (comportamiento correcto).
    /// </summary>
    public void SetBlackProbeSlot(ProtoboardSlot slot) =>
        SetBlackNode(slot != null ? slot.assignedNode : null);

    /// <summary>
    /// Diagnóstico completo — clic derecho en el script en el Inspector → "Diagnosticar Lectura".
    /// Funciona en Play Mode.
    /// </summary>
    [ContextMenu("Diagnosticar Lectura")]
    public void DiagnosticarLectura()
    {
        Debug.Log("──────────── [Multimeter] DIAGNÓSTICO ────────────");
        Debug.Log($"  Punta roja  (_nodeRed):   {(_nodeRed   != null ? $"'{_nodeRed.name}' → {_nodeRed.voltage:F3} V"   : "NULL ← no asignada")}");
        Debug.Log($"  Punta negra (_nodeBlack): {(_nodeBlack != null ? $"'{_nodeBlack.name}' → {_nodeBlack.voltage:F3} V" : "NULL ← no asignada")}");
        Debug.Log($"  Leyendo: {_isReading}  |  Voltaje medido: {_measuredVoltage:F3} V  |  Corriente: {_measuredCurrent * 1000f:F2} mA");

        var nodeInteractables = FindObjectsByType<NodeInteractable>(FindObjectsInactive.Include);
        Debug.Log($"  NodeInteractables en escena: {nodeInteractables.Length}");
        foreach (var ni in nodeInteractables)
        {
            string targetInfo = ni.nodeTarget != null
                ? $"'{ni.nodeTarget.name}' → {ni.nodeTarget.voltage:F3} V"
                : "nodeTarget = NULL ← ASIGNAR EN INSPECTOR";
            string multInfo = ni.multimeter != null ? $"'{ni.multimeter.name}'" : "NULL";
            Debug.Log($"    NodeInteractable '{ni.name}': nodeTarget={targetInfo}, multimeter={multInfo}");
        }

        var gm = FindAnyObjectByType<GameManager>();
        CircuitManager gmCircuit = null;
        if (gm != null)
        {
            gmCircuit = gm.circuit != null ? gm.circuit.GetCompanionCircuitManager() : null;
            string circuitInfo = gmCircuit != null
                ? $"'{gmCircuit.name}' path={GetPath(gmCircuit.transform)}"
                : "NULL ← CRÍTICO";
            Debug.Log($"  GameManager '{gm.name}': circuit → {circuitInfo}");
            Debug.Log($"  GameManager zonas: reto1={NullOrName(gm.reto1Zone)} | reto2={NullOrName(gm.reto2Zone)} | reto3={NullOrName(gm.reto3Zone)} | reto4={NullOrName(gm.reto4Zone)}");
        }
        else
        {
            Debug.LogWarning("  GameManager NO encontrado en la escena.");
        }

        var allCMs = FindObjectsByType<CircuitManager>(FindObjectsInactive.Include);
        Debug.Log($"  CircuitManagers en escena: {allCMs.Length}");
        foreach (var cm in allCMs)
        {
            bool isActive = cm.gameObject.activeInHierarchy;
            bool isGmCircuit = cm == gmCircuit;
            Debug.Log($"  ── CircuitManager '{cm.name}' {(isGmCircuit ? "← GameManager.circuit" : "")} " +
                      $"path={GetPath(cm.transform)} activo={isActive} " +
                      $"components={cm.components.Count} sourceVoltage={cm.sourceVoltage:F2} V " +
                      $"totalCurrent={cm.totalCurrent * 1000f:F2} mA shortCircuit={cm.isShortCircuited}");
            foreach (var comp in cm.components)
            {
                string nodeA = comp.nodeA != null ? $"'{comp.nodeA.name}'={comp.nodeA.voltage:F2}V" : "NULL ← no asignado";
                string nodeB = comp.nodeB != null ? $"'{comp.nodeB.name}'={comp.nodeB.voltage:F2}V" : "NULL ← no asignado";
                if (comp is VoltageSource vs)
                    Debug.Log($"    VoltageSource '{comp.name}': voltage.field={vs.voltage:F2}V | nodeA={nodeA} | nodeB={nodeB}");
                else
                    Debug.Log($"    {comp.GetType().Name} '{comp.name}': nodeA={nodeA} | nodeB={nodeB} | R={comp.GetResistance():F1}Ω");
            }
        }
        if (allCMs.Length == 0)
            Debug.LogWarning("  CircuitManager NO encontrado en la escena.");
        Debug.Log("──────────────────────────────────────────────────");
    }

    /// <summary>Alias de probeA → _nodeRed (compatibilidad con código existente).</summary>
    public ElectricalNode probeA => _nodeRed;

    /// <summary>Alias de probeB → _nodeBlack (compatibilidad con código existente).</summary>
    public ElectricalNode probeB => _nodeBlack;

    /// <summary>Alias de SetProbeA → SetRedNode (usado por PlayerInteraction).</summary>
    public void SetProbeA(ElectricalNode node) => SetRedNode(node);

    /// <summary>Alias de SetProbeB → SetBlackNode (usado por PlayerInteraction).</summary>
    public void SetProbeB(ElectricalNode node) => SetBlackNode(node);

    /// <summary>Reinicia ambas puntas (llamado por GameManager al cargar nivel).</summary>
    public void ResetProbes()
    {
        _nodeRed   = null;
        _nodeBlack = null;
        _bridgeComponent = null;
        _bridgeSource    = null;
        _bridgeCircuit   = null;
        _measuredVoltage = 0f;
        _measuredCurrent = 0f;
        _isReading = false;
        SetIndicator(indicatorRed,   false);
        SetIndicator(indicatorBlack, false);
        UpdateDisplay();
    }

    /// <summary>
    /// Cambia el modo de medición SIN soltar las puntas — igual que girar la perilla de un
    /// multímetro real: los cables siguen donde estaban.
    ///
    /// Antes esto llamaba a ResetProbes(), así que pulsar el botón de modo dejaba la pantalla en
    /// "SIN CONTACTO" hasta que el Explorador volvía a apuntar y gatillar los DOS nodos. Como
    /// MultimeterProbe solo reasigna en el flanco del gatillo o en un OnTriggerEnter NUEVO, una
    /// punta ya apoyada no se reasignaba sola: cambiar a CORRIENTE parecía "no funcionar".
    /// GameManager.LoadLevel() sigue llamando a ResetProbes() al entrar a cada reto.
    /// </summary>
    public void SetMode(MultimeterMode newMode)
    {
        mode = newMode;
        RecomputeBridgeComponent();
        TakeReading();
        UpdateDisplay();
    }

    // ─────────────────────────────────────────────
    //  Lectura eléctrica
    // ─────────────────────────────────────────────

    void TakeReading()
    {
        // Panel fijo: los jacks están soldados al cuerpo (sin sockets que desconectar) — solo
        // falta tocar un nodo con las puntas.
        if (_nodeRed == null || _nodeBlack == null)
        {
            _isReading       = false;
            _measuredVoltage = 0f;
            _measuredCurrent = 0f;
            return;
        }

        bool wasReading = _isReading;
        _isReading = true;
        if (!wasReading) OnReadingTaken?.Invoke();
        if (mode == MultimeterMode.Resistance) _usedResistanceMode = true;

        // 1. Voltaje (Diferencia de potencial real)
        float vDiff = _nodeRed.voltage - _nodeBlack.voltage;

        // 2. Corriente — componente puente, cacheado (ver RecomputeBridgeComponent). Si el
        // objeto cacheado fue destruido (p.ej. componente reemplazado por red) el operador ==
        // de Unity lo detecta como null; se recalcula en vez de volver a escanear la escena
        // cada frame de forma incondicional. El rescan va limitado a 2 Hz: hay pares de nodos
        // que legítimamente NO tienen puente pasivo (bornes de la fuente, tramos de varios
        // componentes) y sin el límite se escaneaba la escena entera en CADA Update().
        if (_bridgeComponent == null && Time.unscaledTime >= _nextBridgeRescan)
        {
            _nextBridgeRescan = Time.unscaledTime + 0.5f;
            RecomputeBridgeComponent();
        }
        float i = ResolveProbedCurrent();

        switch (mode)
        {
            case MultimeterMode.DCVoltage:
                _measuredVoltage = vDiff;
                _measuredCurrent = i;
                break;
            case MultimeterMode.DCCurrent:
                _measuredCurrent = i;
                _measuredVoltage = vDiff;
                break;
            case MultimeterMode.Resistance:
                _measuredCurrent = i;
                _measuredVoltage = Mathf.Abs(i) > 0.0001f ? vDiff / i : 0f;
                break;
        }
    }

    // ─────────────────────────────────────────────
    //  Display
    // ─────────────────────────────────────────────

    void UpdateDisplay()
    {
        if (!_isReading)
        {
            bool redAssigned   = _nodeRed   != null;
            bool blackAssigned = _nodeBlack != null;

            Set(txtVoltage, "—.— V");
            Set(txtCurrent, "—.— mA");
            Set(txtStatus,  redAssigned && !blackAssigned ? "FALTA PUNTA NEGRA"
                          : !redAssigned && blackAssigned ? "FALTA PUNTA ROJA"
                          : "SIN CONTACTO");
            Set(txtMode, ModeLabel());
            return;
        }

        switch (mode)
        {
            case MultimeterMode.DCVoltage:
            case MultimeterMode.DCCurrent:
                Set(txtVoltage, FormatVoltage(_measuredVoltage));
                Set(txtCurrent, FormatCurrent(_measuredCurrent));
                break;

            case MultimeterMode.Resistance:
                // _measuredVoltage YA es el cociente V/I (calculado en TakeReading()), no un
                // voltaje crudo — formatearlo directo. Dividirlo otra vez acá por _measuredCurrent
                // (bug real encontrado y corregido el 2026-07-24: divide dos veces, R/I en vez de
                // R) mostraba una resistencia incorrecta en la pantalla.
                Set(txtVoltage, FormatResistance(_measuredVoltage));
                Set(txtCurrent, FormatCurrent(_measuredCurrent));
                break;
        }

        Set(txtStatus, "MIDIENDO");
        Set(txtMode,   ModeLabel());
    }

    // ─────────────────────────────────────────────
    //  Visual — indicadores
    // ─────────────────────────────────────────────

    static readonly Color _colorAssigned = new Color(0.2f, 0.85f, 0.3f); // verde
    static readonly Color _colorIdle     = new Color(0.4f, 0.4f,  0.4f); // gris
    static readonly int   _baseColorID   = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _indicatorMpb;

    void SetIndicator(Renderer r, bool assigned)
    {
        if (r == null) return;
        // Lazy-init defensivo: si algo llama ResetProbes()/SetMode() antes de que Awake() corra
        // (p.ej. GameManager.LoadLevel invocado muy temprano tras cargar la escena), _indicatorMpb
        // todavía era null y Renderer.GetPropertyBlock(null) tiraba ArgumentNullException.
        if (_indicatorMpb == null) _indicatorMpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_indicatorMpb);
        _indicatorMpb.SetColor(_baseColorID, assigned ? _colorAssigned : _colorIdle);
        r.SetPropertyBlock(_indicatorMpb);
    }

    // ─────────────────────────────────────────────
    //  Formateo
    // ─────────────────────────────────────────────

    /// <summary>Público para que otras UI (ej. MultimeterUI, HUD del casco) formateen igual que la pantalla del dispositivo.</summary>
    public static string FormatVoltage(float v)
    {
        return Mathf.Abs(v) >= 1f
             ? $"{v:F2} V"
             : $"{v * 1000f:F1} mV";
    }

    public static string FormatCurrent(float i)
    {
        float mA = i * 1000f;
        return Mathf.Abs(mA) >= 1f
             ? $"{mA:F1} mA"
             : $"{i * 1_000_000f:F0} µA";
    }

    public static string FormatResistance(float r)
    {
        return r >= 1000f
             ? $"{r / 1000f:F2} kΩ"
             : $"{r:F0} Ω";
    }

    /// <summary>Público para que MultimeterUI (panel HUD) muestre el mismo nombre de modo sin duplicar el switch.</summary>
    public string ModeLabel() => mode switch
    {
        MultimeterMode.DCVoltage  => "DC VOLTAGE",
        MultimeterMode.DCCurrent  => "DC CURRENT",
        MultimeterMode.Resistance => "RESISTANCE",
        _                         => "DC VOLTAGE"
    };

    static void Set(TMP_Text t, string s) { if (t) t.text = s; }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    static string NullOrName(GameObject go) => go != null ? $"'{go.name}'" : "NULL ← asignar en Inspector";
}

// ──────────────────────────────────────────────────────────────────────────────

public enum MultimeterMode { DCVoltage, DCCurrent, Resistance }
