using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Fusion;

/// <summary>
/// Panel "SERIAL MONITOR – Node N" del boceto Técnico (Reto 4), con DATOS REALES:
/// escucha los eventos de red verdaderos (sketch subido por RPC, bridge conectado,
/// componentes, validación, cambio de reto) y produce un log tipo terminal con
/// colores por etiqueta [SYSTEM]/[NET]/[LCD].
///
/// Auto-construye su propia UI estilo AXISTUDIO si no se le asigna un TMP en el Inspector
/// (patrón del proyecto, como DeliveryTrayIndicator). Si su GameObject no cuelga de un
/// Canvas, crea uno screen-space y se ancla abajo-izquierda como en el boceto.
/// </summary>
public class SerialMonitorFeed : MonoBehaviour
{
    [Header("UI (auto-construida si se deja vacía)")]
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Vector2 anchorMin = new(0.012f, 0.02f);
    [SerializeField] private Vector2 anchorMax = new(0.40f, 0.36f);

    [Header("Config")]
    [SerializeField] private int nodeId   = 7;     // "Node 7" del boceto (se sincroniza con el pin real)
    [SerializeField] private int maxLines = 14;

    private readonly List<string> _lines = new();
    private NetworkRunner _runner;

    // ─── Lifecycle ───────────────────────────────────────────────────────
    void Start()
    {
        EnsureUI();
        Sys($"Serial Monitor online — Node {nodeId}");
        Net("Waiting for Virtual Arduino link...");
    }

    void OnEnable()
    {
        ArduinoNetworkBridge.OnBridgeReady     += HandleBridgeReady;
        ArduinoNetworkBridge.OnBridgeDestroyed += HandleBridgeLost;
        ArduinoNetworkBridge.OnSketchReceived  += HandleSketch;
        GameSession.OnComponenteRecibido       += HandleComponente;
        GameSession.OnResultadoValidacion      += HandleValidacion;
        GameSession.OnRetoChanged              += HandleReto;
    }

    void OnDisable()
    {
        ArduinoNetworkBridge.OnBridgeReady     -= HandleBridgeReady;
        ArduinoNetworkBridge.OnBridgeDestroyed -= HandleBridgeLost;
        ArduinoNetworkBridge.OnSketchReceived  -= HandleSketch;
        GameSession.OnComponenteRecibido       -= HandleComponente;
        GameSession.OnResultadoValidacion      -= HandleValidacion;
        GameSession.OnRetoChanged              -= HandleReto;
    }

    // ─── Handlers de eventos REALES de red ───────────────────────────────
    void HandleBridgeReady(ArduinoNetworkBridge _)
    {
        Sys($"Booting Virtual Arduino Node {nodeId}");
        Net($"Link established. Role: {RoleString()}");
    }

    void HandleBridgeLost(ArduinoNetworkBridge _) => Warn("Link lost. Node offline.");

    void HandleSketch(int pin, PinMode mode, PinState state, bool blink, int blinkOnMs, int blinkOffMs){
        nodeId = pin;
        Sys($"Uploading sketch to Node {pin}...");
        Lcd($"PinMode {pin} {mode}");
        string action = blink ? $"BLINK {state} @ {blinkOnMs}ms" : $"digitalWrite -> {state}";
        Net($"D{pin}: {action}");
        Sys($"Upload OK. {RoleString()}");
    }

    void HandleComponente(ComponentType tipo, float valor, int variante)
        => Net($"Component packet RX: {tipo} = {valor:0.##} ({(ComponentVariant)variante})");

    void HandleValidacion(bool ok, int cod)
    {
        if (ok) Sys("Validation PASS. Objective met.");
        else    Warn($"Validation FAIL. code={cod}");
    }

    void HandleReto(int reto) => Sys($"Loading challenge profile {reto}...");

    // ─── Escritura con estilo por etiqueta ───────────────────────────────
    void Sys(string m)  => Push("SYSTEM", AxiStudioTheme.TxtSystem, m);
    void Net(string m)  => Push("NET",    AxiStudioTheme.TxtNet,    m);
    void Lcd(string m)  => Push("LCD",    AxiStudioTheme.TxtLcd,    m);
    void Warn(string m) => Push("ERR",    AxiStudioTheme.TxtWarn,   m);

    void Push(string tag, Color tagColor, string msg)
    {
        string line = $"<color={AxiStudioTheme.Hex(tagColor)}>[{tag}]</color> " +
                      $"<color=#8FA9AE>{System.DateTime.Now:HH:mm:ss}</color> {msg}";
        _lines.Add(line);
        if (_lines.Count > maxLines) _lines.RemoveAt(0);
        if (bodyText != null) bodyText.text = string.Join("\n", _lines);
    }

    string RoleString()
    {
        if (_runner == null || !_runner.IsRunning) _runner = FindAnyObjectByType<NetworkRunner>();
        if (_runner == null || !_runner.IsRunning) return "OFFLINE";
        return _runner.IsServer ? "HOST" : "CLIENT";
    }

    // ─── Auto-construcción de UI ─────────────────────────────────────────
    void EnsureUI()
    {
        if (bodyText != null) return;

        Transform host = GetComponentInParent<Canvas>()?.transform;
        if (host == null)
        {
            var cgo = AxiStudioTheme.NewUI("AxiCanvas_SerialMonitor", null);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            cgo.AddComponent<UnityEngine.UI.CanvasScaler>();
            transform.SetParent(cgo.transform, false);
            host = cgo.transform;
        }

        var holder = AxiStudioTheme.NewUI("SerialMonitorPanel", host);
        var hrt = (RectTransform)holder.transform;
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
        bodyText = AxiStudioTheme.BuildPanel(holder.transform,
            $"SERIAL MONITOR  -  Node {nodeId}", anchorMin, anchorMax);
    }
}
