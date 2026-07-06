using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Añade al Monitor del Técnico la mecánica de click → abre el HUD del Reto 4
/// (ArduinoIDEUI + TechnicianTelemetryUI).  Cierra con click o Escape.
/// Hover → brillo verde-neon en el material.
///
/// arduinoHUD debe apuntar a un Canvas WorldSpace (TechnicianMonitorHUD) que
/// contenga ArduinoIDEUI y TechnicianTelemetryUI. Si el campo está mal asignado
/// (apunta al root del PC u otro GO sin Canvas), Start() busca automáticamente
/// el Canvas correcto subiendo por la jerarquía desde ArduinoIDEUI.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ArduinoMonitorInteract : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Paneles de Reto 4 (asignar en Inspector o Editor Tool)")]
    [Tooltip("Canvas WorldSpace del HUD Arduino (debe contener ArduinoIDEUI + TechnicianTelemetryUI).")]
    public GameObject arduinoHUD;

    [Tooltip("Panel de diagnóstico del circuito (ExplorerCircuitPanel). Opcional.")]
    public GameObject circuitPanel;

    [Header("Cámara (auto-detectada si se deja vacía)")]
    public Camera pcCamera;

    // ── Estado ────────────────────────────────────────────────────────────
    private bool _isOpen;

    /// <summary>True si algún panel de Arduino está abierto. Lo consulta PauseMenu para que
    /// Escape cierre el panel en vez de abrir la pausa cuando hay un HUD abierto.</summary>
    public static bool AnyOpen { get; private set; }

    // ── Hover glow ────────────────────────────────────────────────────────
    private Renderer              _renderer;
    private MaterialPropertyBlock _mpb;
    private Color                 _baseColor;
    private static readonly int   BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int   EmissionID  = Shader.PropertyToID("_EmissionColor");
    private static readonly Color HoverColor  = new Color(0f, 1f, 0.7f, 1f);

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    void Start()
    {
        if (pcCamera == null) pcCamera = Camera.main;

        // Validar que arduinoHUD sea un Canvas (o contenga uno).
        // Si está mal asignado (apunta al root del PC u otro GO sin Canvas),
        // buscar el Canvas correcto subiendo desde ArduinoIDEUI.
        if (!IsValidHUD(arduinoHUD))
            arduinoHUD = FindHUDCanvas();

        if (arduinoHUD == null)
            Debug.LogWarning("[ArduinoMonitor] arduinoHUD no encontrado. " +
                "Ejecuta Tools → TITA → Reto 4 → Setup Monitor Arduino y asigna el TechnicianMonitorHUD Canvas.");

        // Auto-buscar circuitPanel
        if (circuitPanel == null)
        {
            var ecp = FindAnyObjectByType<ExplorerCircuitPanel>(FindObjectsInactive.Include);
            if (ecp != null) circuitPanel = ecp.gameObject;
        }

        // Empezar ocultos
        if (arduinoHUD  != null) arduinoHUD.SetActive(false);
        if (circuitPanel != null) circuitPanel.SetActive(false);

        // Hover material
        _renderer = GetComponent<Renderer>();
        _mpb      = new MaterialPropertyBlock();
        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _baseColor = _mpb.HasProperty(BaseColorID)
                ? _mpb.GetColor(BaseColorID)
                : Color.white;
        }
    }

    void Update()
    {
        if (!_isOpen) return;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
#endif
    }

    // ─────────────────────────────────────────────
    //  Pointer events
    // ─────────────────────────────────────────────

    // El clic en el monitor SOLO ABRE el panel (idempotente). Antes hacía Toggle, y los
    // clics sobre los botones del HUD que "atravesaban" al monitor lo cerraban sin querer.
    // Para CERRAR el panel se usa Escape (ver Update + coordinación con PauseMenu).
    void IPointerClickHandler.OnPointerClick(PointerEventData _) { if (!_isOpen) Open(); }
    void IPointerEnterHandler.OnPointerEnter(PointerEventData _) => SetHover(true);
    void IPointerExitHandler.OnPointerExit(PointerEventData _)   => SetHover(false);

    // ─────────────────────────────────────────────
    //  Lógica open / close
    // ─────────────────────────────────────────────

    public void Toggle()
    {
        if (_isOpen) Close();
        else         Open();
    }

    void Open()
    {
        _isOpen = true;
        AnyOpen = true;
        SetHover(false);

        if (arduinoHUD != null)
        {
            arduinoHUD.SetActive(true);

            var cam = pcCamera != null ? pcCamera : Camera.main;

            // Garantizar worldCamera y GraphicRaycaster en todos los Canvas del HUD
            foreach (var canvas in arduinoHUD.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;
                if (canvas.worldCamera == null && cam != null)
                    canvas.worldCamera = cam;
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            // Garantizar InputSystemUIInputModule en el EventSystem
            var es = EventSystem.current;
            if (es != null && es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                var old = es.GetComponent<StandaloneInputModule>();
                if (old != null) Destroy(old);
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // Refrescar TechnicianTelemetryUI
            var tele = arduinoHUD.GetComponentInChildren<TechnicianTelemetryUI>(true);
            if (tele != null) tele.enabled = true;
        }

        if (circuitPanel != null)
        {
            circuitPanel.SetActive(true);
            var ecp = circuitPanel.GetComponentInChildren<ExplorerCircuitPanel>(true);
            ecp?.ForceRefresh();
        }

        Debug.Log("[ArduinoMonitor] Panel Reto 4 abierto.");
    }

    public void Close()
    {
        _isOpen = false;
        AnyOpen = false;
        if (arduinoHUD  != null) arduinoHUD.SetActive(false);
        if (circuitPanel != null) circuitPanel.SetActive(false);
        Debug.Log("[ArduinoMonitor] Panel Reto 4 cerrado.");
    }

    // ─────────────────────────────────────────────
    //  Hover
    // ─────────────────────────────────────────────

    void SetHover(bool on)
    {
        if (_renderer == null || _isOpen) return;
        _renderer.GetPropertyBlock(_mpb);
        if (on)
        {
            _mpb.SetColor(BaseColorID, HoverColor * 1.2f);
            _mpb.SetColor(EmissionID,  HoverColor * 0.6f);
        }
        else
        {
            _mpb.SetColor(BaseColorID, _baseColor);
            _mpb.SetColor(EmissionID,  Color.black);
        }
        _renderer.SetPropertyBlock(_mpb);
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Devuelve true si go tiene un Canvas en sí mismo o un Canvas component en él.
    /// Se considera inválido si go es null, o si no tiene Canvas y tampoco
    /// contiene ArduinoIDEUI (podría ser el root del PC u otro objeto incorrecto).
    /// </summary>
    static bool IsValidHUD(GameObject go)
    {
        if (go == null) return false;
        if (go.GetComponent<UnityEngine.Canvas>() != null) return true;
        // Tiene ArduinoIDEUI directo → es un panel sin Canvas (inválido como HUD)
        if (go.GetComponent<ArduinoIDEUI>() != null) return false;
        // Tiene ArduinoIDEUI en hijos dentro de un Canvas → válido
        var ide = go.GetComponentInChildren<ArduinoIDEUI>(true);
        if (ide == null) return false;
        var t = ide.transform;
        while (t != null && t != go.transform)
        {
            if (t.GetComponent<UnityEngine.Canvas>() != null) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>
    /// Sube por la jerarquía desde ArduinoIDEUI hasta encontrar un Canvas.
    /// Devuelve el GO del Canvas, o null si no hay ninguno.
    /// </summary>
    static GameObject FindHUDCanvas()
    {
        var ide = FindAnyObjectByType<ArduinoIDEUI>(FindObjectsInactive.Include);
        if (ide == null) return null;

        var t = ide.transform;
        while (t != null)
        {
            if (t.GetComponent<UnityEngine.Canvas>() != null)
                return t.gameObject;
            t = t.parent;
        }
        return null;
    }
}
