using TMPro;
using UnityEngine;

/// <summary>
/// Etiqueta 3D de un nodo eléctrico visible para el Explorador en VR.
///
/// PROPÓSITO (comunicación técnica):
///   El Explorador puede leer "Nodo A: 9.0 V" y decirle al Técnico
///   "hay voltaje en el Nodo A pero no en el Nodo B", usando terminología precisa.
///
/// SETUP:
///   Agregar este componente al mismo GameObject que tiene ElectricalNode,
///   o a cualquier hijo de la escena del Explorador.
///   Si no hay TextMeshPro asignado se crea uno automáticamente en Awake.
/// </summary>
[RequireComponent(typeof(ElectricalNode))]
public class CircuitNodeDisplay : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Identificación del nodo")]
    [Tooltip("Nombre que verá el Explorador, p. ej. 'Nodo A', 'Nodo B', 'GND'.")]
    public string nodeName = "Nodo A";

    [Header("TextMeshPro 3D (WorldSpace)")]
    [Tooltip("Dejar vacío para auto-crear al inicio.")]
    public TextMeshPro label;

    [Header("Presentación")]
    [Tooltip("Desplazamiento en Y respecto al nodo para que la etiqueta no tape el componente.")]
    public float offsetY = 0.06f;
    [Tooltip("Tamaño del texto en unidades de TMP (valor típico para VR WorldSpace: 0.01–0.015 m/u).")]
    public float labelScale = 0.012f;
    [Tooltip("Mostrar también la corriente en el nodo (mA).")]
    public bool showCurrent = false;
    [Tooltip("La etiqueta siempre mira hacia la cámara principal.")]
    public bool billboard = true;

    // ─────────────────────────────────────────────
    //  Interno
    // ─────────────────────────────────────────────
    private ElectricalNode _node;
    private Camera         _cam;   // cache de Camera.main (evita el FindByTag por-frame)

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        _node = GetComponent<ElectricalNode>();
        CreateLabelIfNeeded();
    }

    void OnEnable()  { CircuitManager.OnCircuitChanged += Refresh; }
    void OnDisable() { CircuitManager.OnCircuitChanged -= Refresh; }

    void LateUpdate()
    {
        if (!billboard || label == null) return;
        if (_cam == null) _cam = Camera.main;   // re-busca solo si se perdió la cámara
        if (_cam == null) return;
        // El label mira hacia la cámara del jugador
        label.transform.rotation = Quaternion.LookRotation(
            label.transform.position - _cam.transform.position);
    }

    // ─────────────────────────────────────────────
    //  Actualización del texto
    // ─────────────────────────────────────────────
    void Refresh()
    {
        if (_node == null || label == null) return;

        string voltColor = _node.voltage >= 1f ? "#00FF88" : "#FF4444";
        string text = $"<b><color={voltColor}>{nodeName}</color></b>\n" +
                      $"{_node.voltage:F1} V";

        if (showCurrent)
            text += $"\n{_node.current * 1000f:F1} mA";

        label.text = text;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    void CreateLabelIfNeeded()
    {
        if (label != null) return;

        var go = new GameObject($"NodeLabel_{nodeName}");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.up * offsetY;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one * labelScale;

        label = go.AddComponent<TextMeshPro>();
        label.alignment          = TextAlignmentOptions.Center;
        label.fontSize           = 3f;
        label.color              = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.outlineWidth       = 0.2f;
        label.outlineColor       = Color.black;
    }
}
