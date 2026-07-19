using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;

/// <summary>
/// Multímetro digital VR para el Explorador.
///
/// FLUJO:
///   1. Explorador agarra el multímetro (XRGrabInteractable).
///   2. Apunta el controlador DERECHO al nodo a medir → gatillo → punta roja asignada.
///   3. Apunta el controlador IZQUIERDO al nodo de referencia → gatillo → punta negra.
///   4. El display muestra voltaje y corriente en tiempo real.
///   5. El Técnico lee los mismos valores en TechnicianUIController
///      mediante las propiedades measuredVoltage / measuredCurrent.
///
/// JERARQUÍA EN UNITY:
///   Multimeter_VR                   ← este script + XRGrabInteractable + Rigidbody
///   ├─ Body                         ← Cube (0.06 × 0.12 × 0.02), color gris oscuro
///   ├─ Indicator_Red                ← Sphere (0.008), color rojo — se ilumina al asignar
///   ├─ Indicator_Black              ← Sphere (0.008), color negro — se ilumina al asignar
///   └─ Screen_Canvas                ← Canvas WorldSpace, Scale 0.001
///       ├─ TMP_Voltage              ← "9.00 V"
///       ├─ TMP_Current              ← "15.3 mA"
///       ├─ TMP_Status               ← "MIDIENDO" / "SIN CONTACTO"
///       └─ TMP_Mode                 ← "DC VOLTAGE"
///
/// NO necesita MultimeterProbe.cs ni CircuitNode.cs.
/// Trabaja directamente con ElectricalNode asignado por NodeInteractable.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
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

    [Header("Feedback háptico al asignar nodo")]
    [Range(0f, 1f)] public float hapticIntensity = 0.3f;
    public float hapticDuration = 0.08f;

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
    //  XR
    // ─────────────────────────────────────────────
    private XRGrabInteractable _grab;
    private UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor _currentInteractor;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);
        _indicatorMpb = new MaterialPropertyBlock();

        // Mismo bug ya documentado y arreglado antes en CableBoxSpawner.RepararInteraccionVR(): un
        // XRGrabInteractable con la lista 'colliders' VACÍA auto-recolecta TODOS los colliders de sus
        // hijos (Physics.GetComponentsInChildren), no solo el propio — con las 2 puntas del
        // multímetro (Probe_Red_Tip/Probe_Black_Tip) colgando como hijos vía Cable_Red/Cable_Black,
        // el grab del CUERPO del multímetro podía "robarse" el collider de una punta, dejándola sin
        // poder agarrarse por separado (el reporte: "solo la punta negra se puede agarrar"). Acotar
        // la lista al collider propio del cuerpo evita la ambigüedad, sin tocar las puntas (que ya
        // tienen su propio XRGrabInteractable independiente vía MultimeterProbe).
        var bodyCollider = GetComponent<Collider>();
        if (bodyCollider != null)
        {
            _grab.colliders.Clear();
            _grab.colliders.Add(bodyCollider);
        }
    }

    void OnDestroy()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnGrabbed);
        _grab.selectExited.RemoveListener(OnReleased);
    }

    void Update()
    {
        TakeReading();
        UpdateDisplay();
        AtenderCambioDeModoPorStick();
    }

    // ─────────────────────────────────────────────
    //  Cambio de modo con el CLICK del joystick (mano que sostiene)
    // ─────────────────────────────────────────────
    //  El botón físico Mode_Button perdía el "duelo de distancias" de XRI contra el grab del
    //  cuerpo (el pivote del multímetro queda a ~1 cm del botón): al querer presionarlo, la mano
    //  agarraba el multímetro (reporte real). Como el Reto 4 EXIGE pasar a modo OHMS, hace falta
    //  un camino que no dependa de puntería: mientras se SOSTIENE el multímetro, apretar el
    //  JOYSTICK (click del stick) de esa misma mano cicla Voltaje → Corriente → Resistencia.
    //  (El stick de esa mano no compite con nada: mover/girar usa el stick de la otra o queda
    //  consumido por la rotación de objeto en mano, que ignora el click.)
    //  En PCVR/editor también funciona la tecla M como atajo de prueba.

    private bool _stickClickPrev;

    void AtenderCambioDeModoPorStick()
    {
        bool kb = UnityEngine.InputSystem.Keyboard.current != null &&
                  UnityEngine.InputSystem.Keyboard.current.mKey.wasPressedThisFrame;

        bool click = false;
        if (_currentInteractor != null)   // solo mientras alguien lo sostiene
        {
            XRNode nodo = XRNode.RightHand;
            if (_currentInteractor is XRBaseInputInteractor input &&
                input.handedness == InteractorHandedness.Left)
                nodo = XRNode.LeftHand;

            var dev = InputDevices.GetDeviceAtXRNode(nodo);
            dev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out click);
        }

        if ((click && !_stickClickPrev) || kb) CiclarModo();
        _stickClickPrev = click;
    }

    /// <summary>Cicla Voltaje → Corriente → Resistencia (usado por el stick-click y el botón físico).</summary>
    public void CiclarModo()
    {
        SetMode((MultimeterMode)(((int)mode + 1) % 3));
        SendHaptic();
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
            SendHaptic();   // vibrar solo al tocar un nodo NUEVO, no 60 veces/s de contacto
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
            SendHaptic();
            _nodeBlack = node;
            RecomputeBridgeComponent();
        }
        SetIndicator(indicatorBlack, node != null);
    }

    // ─────────────────────────────────────────────
    //  Componente puente (para lectura de corriente) — cacheado
    // ─────────────────────────────────────────────
    // TakeReading() corre cada Update() mientras ambas puntas están asignadas (constante
    // mientras el Explorador sostiene el multímetro en contacto). Antes escaneaba TODA la
    // escena con FindObjectsByType cada frame para hallar el componente entre las 2 puntas
    // — costoso en Quest. Ahora se recalcula solo cuando cambia una punta (ver SetRedNode/
    // SetBlackNode) y TakeReading() reutiliza el resultado cacheado.
    private ElectricalComponent _bridgeComponent;

    void RecomputeBridgeComponent()
    {
        _bridgeComponent = null;
        if (_nodeRed == null || _nodeBlack == null) return;

        var allComps = FindObjectsByType<ElectricalComponent>(FindObjectsInactive.Exclude);
        foreach (var comp in allComps)
        {
            if ((comp.nodeA == _nodeRed && comp.nodeB == _nodeBlack) ||
                (comp.nodeA == _nodeBlack && comp.nodeB == _nodeRed))
            {
                _bridgeComponent = comp;
                break;
            }
        }
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

    /// <summary>Reinicia ambas puntas (llamado por GameManager al cargar nivel).</summary>

    /// <summary>Alias de probeA → _nodeRed (compatibilidad con código existente).</summary>
    public ElectricalNode probeA => _nodeRed;

    /// <summary>Alias de probeB → _nodeBlack (compatibilidad con código existente).</summary>
    public ElectricalNode probeB => _nodeBlack;

    /// <summary>Alias de SetProbeA → SetRedNode (usado por PlayerInteraction).</summary>
    public void SetProbeA(ElectricalNode node) => SetRedNode(node);

    /// <summary>Alias de SetProbeB → SetBlackNode (usado por PlayerInteraction).</summary>
    public void SetProbeB(ElectricalNode node) => SetBlackNode(node);

    public void ResetProbes()
    {
        _nodeRed   = null;
        _nodeBlack = null;
        _bridgeComponent = null;
        _measuredVoltage = 0f;
        _measuredCurrent = 0f;
        _isReading = false;
        SetIndicator(indicatorRed,   false);
        SetIndicator(indicatorBlack, false);
        UpdateDisplay();
    }

    /// <summary>Cambia el modo de medición.</summary>
    public void SetMode(MultimeterMode newMode)
    {
        mode = newMode;
        ResetProbes();
    }

    // ─────────────────────────────────────────────
    //  Lectura eléctrica
    // ─────────────────────────────────────────────

    void TakeReading()
    {
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
        // de Unity lo detecta como null; se recalcula una vez en vez de volver a escanear
        // la escena cada frame de forma incondicional.
        if (_bridgeComponent == null) RecomputeBridgeComponent();
        float i = _bridgeComponent != null ? Mathf.Abs(_bridgeComponent.current) : 0f;

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
                float ohms = Mathf.Abs(_measuredCurrent) > 0.0001f
                           ? _measuredVoltage / _measuredCurrent
                           : 0f;
                Set(txtVoltage, FormatResistance(ohms));
                Set(txtCurrent, FormatCurrent(_measuredCurrent));
                break;
        }

        Set(txtStatus, "MIDIENDO");
        Set(txtMode,   ModeLabel());
    }

    // ─────────────────────────────────────────────
    //  XR — grab
    // ─────────────────────────────────────────────

    void OnGrabbed(SelectEnterEventArgs args)  => _currentInteractor = args.interactorObject;
    void OnReleased(SelectExitEventArgs args)  => _currentInteractor = null;

    void SendHaptic()
    {
        if (_currentInteractor == null) return;
        (_currentInteractor as UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor)
            ?.SendHapticImpulse(hapticIntensity, hapticDuration);
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

    static string FormatVoltage(float v)
    {
        return Mathf.Abs(v) >= 1f
             ? $"{v:F2} V"
             : $"{v * 1000f:F1} mV";
    }

    static string FormatCurrent(float i)
    {
        float mA = i * 1000f;
        return Mathf.Abs(mA) >= 1f
             ? $"{mA:F1} mA"
             : $"{i * 1_000_000f:F0} µA";
    }

    static string FormatResistance(float r)
    {
        return r >= 1000f
             ? $"{r / 1000f:F2} kΩ"
             : $"{r:F0} Ω";
    }

    string ModeLabel() => mode switch
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
