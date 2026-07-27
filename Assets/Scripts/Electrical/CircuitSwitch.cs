using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Interruptor de circuito interactivo en VR.
/// Toca con el controlador para encender/apagar el circuito.
///
/// OFF: resistencia ≈ infinita (circuito abierto) → LED apagado, sin corriente.
/// ON:  resistencia ≈ 0 Ω     (circuito cerrado) → corriente fluye normalmente.
///
/// SETUP en escena:
///   1. Añadir CircuitSwitch al GameObject del switch (requiere XRSimpleInteractable).
///   2. Asignar nodeA (entrada VCC) y nodeB (salida hacia R) en Inspector.
///   3. El CircuitManager del padre detecta este componente en serie automáticamente.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class CircuitSwitch : ElectricalComponent
{
    [Header("Estado inicial")]
    public bool isOn = false;   // empieza apagado — jugador debe activarlo

    [Header("Debug (sin VR)")]
    [Tooltip("Tecla para alternar el switch desde el teclado. None = desactivado.")]
    public KeyCode debugKey = KeyCode.S;

    [Header("Colores de feedback visual")]
    public Color colorOff = new Color(0.80f, 0.20f, 0.10f);   // rojo = apagado
    public Color colorOn  = new Color(0.20f, 0.85f, 0.30f);   // verde = encendido

    [Header("Haptics (auto-detectado)")]
    public HapticFeedback haptics;

    private XRSimpleInteractable _interactable;
    private Renderer              _renderer;
    private MaterialPropertyBlock _mpb;
    private static readonly int   _colorID = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        // El renderer puede estar en el root o en el hijo "Visual" (prefab Vol.2)
        _renderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        _mpb      = new MaterialPropertyBlock();

        if (haptics == null) haptics = FindAnyObjectByType<HapticFeedback>();
    }

    void OnEnable()
    {
        _interactable.selectEntered.AddListener(OnActivated);
        UpdateVisual();
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnActivated);
    }

    void Update()
    {
        if (debugKey != KeyCode.None)
        {
            var kb = Keyboard.current;
            bool pressed = kb != null && (debugKey switch
            {
                KeyCode.S      => kb.sKey.wasPressedThisFrame,
                KeyCode.E      => kb.eKey.wasPressedThisFrame,
                KeyCode.T      => kb.tKey.wasPressedThisFrame,
                KeyCode.Space  => kb.spaceKey.wasPressedThisFrame,
                KeyCode.Return => kb.enterKey.wasPressedThisFrame,
                _              => false
            });
            if (pressed) Toggle();
        }
    }

    /// <summary>Alterna el switch desde código o desde el Inspector (clic derecho → Toggle).</summary>
    [ContextMenu("Toggle")]

    public void Toggle()
    {
        isOn = !isOn;
        UpdateVisual();
        
        // ❌ BORRAR O COMENTAR LA LÍNEA ANTIGUA:
        // GetComponentInParent<CircuitManager>()?.MarkDirty();
        
        // ✅ NUEVA LÍNEA: Avisar al motor MNA que algo cambió físicamente
        CircuitGraphAnalyzer.OnCircuitChanged?.Invoke();
        
        Debug.Log($"[CircuitSwitch] '{name}' → {(isOn ? "ON  ✓" : "OFF ✗")} (debug)");
    }

    void OnActivated(SelectEnterEventArgs args)
    {
        haptics?.PlayLight();
        Toggle();
    }

    // ── ElectricalComponent ───────────────────────────────────────────
    // OFF: 1 MΩ (circuito abierto, corriente ~0)
    // ON:  0.01 Ω (prácticamente un cable, caída de tensión insignificante)
    public override float GetResistance() => isOn ? 0.01f : 1_000_000f;

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;
        float v  = nodeA.voltage - nodeB.voltage;
        current     = v / GetResistance();
        voltageDrop = v;
    }

    // ── Feedback visual ───────────────────────────────────────────────
    void UpdateVisual()
    {
        if (_renderer == null || _mpb == null) return;
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_colorID, isOn ? colorOn : colorOff);
        _renderer.SetPropertyBlock(_mpb);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = isOn ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.015f);
    }
}
