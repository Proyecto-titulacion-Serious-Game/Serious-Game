using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Click en el libro físico sobre la mesa para abrir el manual a pantalla completa.
/// El manual se muestra como un Canvas Screen Space-Overlay que se activa/desactiva.
/// 
/// Asignar este script al Manual_Book (el cubo 3D).
/// Asignar el Canvas del manual (el overlay de pantalla completa) al campo manualOverlay.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ManualBookOpener : MonoBehaviour
{
    [Header("Canvas del manual a pantalla completa")]
    public GameObject manualOverlay;   // Canvas Screen Space-Overlay

    [Header("Estado de apertura")]
    public bool isOpen = false;

    /// <summary>True si el manual está abierto. Lo consultan PauseMenu (para que Escape no
    /// abra la pausa encima) y ArduinoMonitorInteract (para que ese mismo Escape no cierre
    /// también el Hub del Arduino) — así el manual, al ser overlay de pantalla completa, es
    /// lo único que un Escape cierra; el Hub conserva su prioridad detrás.</summary>
    public static bool AnyOpen { get; private set; }

    [Header("Efecto hover")]
    public Renderer bookRenderer;
    public Color colorNormal = new Color(0.2f, 0.3f, 0.6f);
    public Color colorHover  = new Color(0.4f, 0.6f, 0.9f);

    private MaterialPropertyBlock _mpb;
    private static readonly int _colorID = Shader.PropertyToID("_Color");

    void Awake()
    {
        if (bookRenderer == null)
            bookRenderer = GetComponent<Renderer>();

        _mpb = new MaterialPropertyBlock();

        // Auto-buscar el overlay si no fue asignado en el Inspector
        if (manualOverlay == null)
        {
            // Buscar un Canvas Screen Space Overlay en la escena que contenga
            // TechnicianManualDisplay (así no se confunde con otros overlays)
            var display = FindAnyObjectByType<TechnicianManualDisplay>();
            if (display != null)
                manualOverlay = display.gameObject;
        }

        if (manualOverlay != null)
            manualOverlay.SetActive(false);

        SetColor(colorNormal);

        // Auto-conectar botón Cerrar dentro del overlay
        if (manualOverlay != null)
        {
            foreach (var btn in manualOverlay.GetComponentsInChildren<Button>(true))
            {
                string n = btn.name.ToLowerInvariant();
                if (n.Contains("cerrar") || n.Contains("close"))
                {
                    btn.onClick.RemoveListener(CloseManual);
                    btn.onClick.AddListener(CloseManual);
                }
            }
        }
    }

    void OnDisable()
    {
        // Si el GO se desactiva/destruye con el manual abierto (cambio de escena, etc.), no dejar
        // el flag atascado en true — PauseMenu/ArduinoMonitorInteract quedarían bloqueados creyendo
        // que el manual sigue abierto para siempre.
        if (isOpen) { isOpen = false; AnyOpen = false; }
    }

    void Update()
    {
        if (!isOpen) return;

        // Escape cierra el manual — compatible con New Input System y legacy Input
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            CloseManual();
#else
        if (Input.GetKeyDown(KeyCode.Escape))
            CloseManual();
#endif
    }

    /// <summary>Hover del mouse — brillo azul claro.</summary>
    void OnMouseEnter()
    {
        if (!isOpen) SetColor(colorHover);
    }

    /// <summary>Salir del hover — color normal.</summary>
    void OnMouseExit()
    {
        if (!isOpen) SetColor(colorNormal);
    }

    /// <summary>Click — abre o cierra el manual.</summary>
    void OnMouseDown()
    {
        ToggleManual();
    }

    /// <summary>Alterna entre abierto y cerrado.</summary>
    public void ToggleManual()
    {
        isOpen = !isOpen;
        AnyOpen = isOpen;
        if (manualOverlay != null)
            manualOverlay.SetActive(isOpen);

        Debug.Log($"[ManualBook] {(isOpen ? "Abierto" : "Cerrado")}");
    }

    /// <summary>Cierra el manual (llamado desde el botón Cerrar del overlay).</summary>
    public void CloseManual()
    {
        isOpen = false;
        AnyOpen = false;
        if (manualOverlay != null)
            manualOverlay.SetActive(false);

        SetColor(colorNormal);
    }

    void SetColor(Color c)
    {
        if (bookRenderer == null) return;
        bookRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_colorID, c);
        bookRenderer.SetPropertyBlock(_mpb);
    }
}