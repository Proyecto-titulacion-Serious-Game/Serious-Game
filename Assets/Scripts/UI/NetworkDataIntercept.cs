using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Fusion;

/// <summary>
/// Panel "NETWORK DATA INTERCEPT" del boceto Técnico (Reto 4), con DATOS REALES:
/// cada RPC que llega por la red (sketch, componente, validación, cambio de reto)
/// añade UNA fila a la tabla — Packet ID incremental, Node, Type, Timestamp, Size, Status.
/// Es la prueba visible de tráfico de red dentro de la propia UI del juego.
///
/// Auto-construye su UI estilo AXISTUDIO si no se le asigna TMP (igual que SerialMonitorFeed).
/// Posición por defecto: abajo-derecha, como en el boceto.
/// </summary>
public class NetworkDataIntercept : MonoBehaviour
{
    [Header("UI (auto-construida si se deja vacía)")]
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Vector2 anchorMin = new(0.60f, 0.02f);
    [SerializeField] private Vector2 anchorMax = new(0.988f, 0.40f);

    [Header("Config")]
    [SerializeField] private int maxRows = 9;

    private readonly List<string> _rows = new();
    private int _packetId;

    private const string Mono = "<mspace=0.56em>";

    // ─── Lifecycle ───────────────────────────────────────────────────────
    void Start() => EnsureUI();

    void OnEnable()
    {
        ArduinoNetworkBridge.OnSketchReceived += HandleSketch;
        GameSession.OnComponenteRecibido      += HandleComponente;
        GameSession.OnResultadoValidacion     += HandleValidacion;
        GameSession.OnRetoChanged             += HandleReto;
    }

    void OnDisable()
    {
        ArduinoNetworkBridge.OnSketchReceived -= HandleSketch;
        GameSession.OnComponenteRecibido      -= HandleComponente;
        GameSession.OnResultadoValidacion     -= HandleValidacion;
        GameSession.OnRetoChanged             -= HandleReto;
    }

    // ─── Handlers (cada RPC real = una fila) ─────────────────────────────
    void HandleSketch(int pin, PinMode mode, PinState state, bool blink, int blinkOnMs, int blinkOffMs)        => AddRow($"D{pin}", "SKETCH", blink ? 18 : 12);

    void HandleComponente(ComponentType tipo, float valor, int variante)
        => AddRow(tipo.ToString().Substring(0, Mathf.Min(4, tipo.ToString().Length)).ToUpper(), "COMP", 8);

    void HandleValidacion(bool ok, int cod)
        => AddRow(ok ? "PASS" : "FAIL", "VALID", 5, ok ? "OK" : "ERR");

    void HandleReto(int reto) => AddRow($"L{reto}", "RETO", 4);

    void AddRow(string node, string type, int size, string status = "OK")
    {
        _packetId++;
        string time = System.DateTime.Now.ToString("HH:mm:ss");
        Color sc = status == "OK" ? AxiStudioTheme.TxtSystem : AxiStudioTheme.TxtWarn;
        string row =
            $"IP{_packetId:00}  " +
            $"{node,-5} " +
            $"<color={AxiStudioTheme.Hex(AxiStudioTheme.TxtNet)}>{type,-6}</color> " +
            $"{time}  " +
            $"{size,3}B  " +
            $"<color={AxiStudioTheme.Hex(sc)}>{status}</color>";
        _rows.Add(row);
        if (_rows.Count > maxRows) _rows.RemoveAt(0);
        Render();
    }

    void Render()
    {
        if (bodyText == null) return;
        string header =
            $"<color={AxiStudioTheme.Hex(AxiStudioTheme.Cyan)}>" +
            $"PKT   NODE  TYPE   TIME      SIZE STATUS</color>";
        bodyText.text = Mono + header + "\n" +
                        (_rows.Count == 0 ? "<color=#5E7A7F>(sin tráfico — esperando RPC...)</color>"
                                          : string.Join("\n", _rows)) +
                        "</mspace>";
    }

    // ─── Auto-construcción de UI ─────────────────────────────────────────
    void EnsureUI()
    {
        if (bodyText == null)
        {
            Transform host = GetComponentInParent<Canvas>()?.transform;
            if (host == null)
            {
                var cgo = AxiStudioTheme.NewUI("AxiCanvas_NetIntercept", null);
                var canvas = cgo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                cgo.AddComponent<UnityEngine.UI.CanvasScaler>();
                transform.SetParent(cgo.transform, false);
                host = cgo.transform;
            }

            var holder = AxiStudioTheme.NewUI("NetInterceptPanel", host);
            var hrt = (RectTransform)holder.transform;
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            bodyText = AxiStudioTheme.BuildPanel(holder.transform,
                "NETWORK DATA INTERCEPT", anchorMin, anchorMax, 12);
        }
        Render();
    }
}
