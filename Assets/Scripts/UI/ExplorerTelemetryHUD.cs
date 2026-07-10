using TMPro;
using UnityEngine;
using Fusion;

/// <summary>
/// HUD holográfico del Explorador (boceto VR Reto 4): paneles flotantes neón sobre la mesa
/// con TELEMETRÍA REAL — voltaje/corriente/potencia/ADC del ProtoboardSimulator + ArduinoCore,
/// estado del circuito, y tráfico de red (ping/jugadores). World-space para que se vea en el
/// headset y en el casting del Quest.
///
/// Auto-construye su UI estilo AXISTUDIO si no se le asigna (patrón DeliveryTrayIndicator).
/// Tres paneles: TELEMETRY (izq), SYSTEM STATUS (centro), NET LINK (der), como el boceto.
/// </summary>
public class ExplorerTelemetryHUD : MonoBehaviour
{
    [Header("Construcción world-space")]
    [SerializeField] private Vector2 canvasSizePx   = new(1680, 520);
    [SerializeField] private float   worldScale      = 0.0011f;   // ~1.85m x 0.57m
    [SerializeField] private float   refreshInterval = 0.2f;

    [SerializeField] private TMP_Text _telemetry, _system, _net;
    private ProtoboardSimulator _sim;
    private ArduinoCore         _core;
    private NetworkRunner       _runner;
    private float _t;

    void Start()
    {
        if (_telemetry == null) BuildUI();
        RefreshRefs();
        UpdateAll();
    }

    void OnEnable()  => ProtoboardSimulator.OnCircuitChanged += UpdateAll;
    void OnDisable() => ProtoboardSimulator.OnCircuitChanged -= UpdateAll;

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < refreshInterval) return;
        _t = 0f;
        UpdateAll();
    }

    // ─── Datos REALES ────────────────────────────────────────────────────
    void RefreshRefs()
    {
        // Un sim desactivado no es fake-null: con 2 ProtoboardSimulator en escena (Reto 2 y
        // Reto 4, en zonas que se prenden/apagan por reto) hay que re-resolver al cambiar de reto.
        if (_sim == null || !_sim.isActiveAndEnabled)
            _sim = FindAnyObjectByType<ProtoboardSimulator>();
        if (_core == null || !_core.isActiveAndEnabled)
            _core = FindAnyObjectByType<ArduinoCore>();
        if (_runner == null || !_runner.IsRunning) _runner = FindAnyObjectByType<NetworkRunner>();
    }

    void UpdateAll()
    {
        RefreshRefs();
        if (_telemetry == null) return;

        string c   = AxiStudioTheme.Hex(AxiStudioTheme.Cyan);
        string sys = AxiStudioTheme.Hex(AxiStudioTheme.TxtSystem);
        string net = AxiStudioTheme.Hex(AxiStudioTheme.TxtNet);
        string wrn = AxiStudioTheme.Hex(AxiStudioTheme.TxtWarn);

        // ── TELEMETRY (V / I / P / ADC reales) ──
        float v   = _sim  != null ? _sim.sourceVoltage  : 0f;
        float iMa = _sim  != null ? _sim.totalCurrentmA : 0f;
        float p   = _sim  != null ? _sim.totalPowerW    : 0f;
        int   adc = _core != null ? _core.AdcValue      : 0;
        float coreTemp = 24f + p * 6.5f;   // estimación derivada de la potencia disipada
        _telemetry.text =
            $"<color={net}>VOLTAGE</color>   {v,6:0.00} V\n" +
            $"<color={net}>CURRENT</color>   {iMa,6:0.0} mA\n" +
            $"<color={net}>POWER</color>     {p,6:0.00} W\n" +
            $"<color={net}>ADC A0</color>    {adc,6} /1023\n" +
            $"<color={net}>CORE TEMP</color> {coreTemp,5:0.0} C  <size=70%><color=#5E7A7F>(est)</color></size>";

        // ── SYSTEM STATUS (estado del circuito + pin) ──
        string estado;
        if (_sim == null)               estado = $"<color={wrn}>NO SIM</color>";
        else if (_sim.isShortCircuited) estado = $"<color={wrn}>SHORT CIRCUIT</color>";
        else if (_sim.isOpenCircuit)    estado = $"<color={wrn}>OPEN CIRCUIT</color>";
        else                            estado = $"<color={sys}>NOMINAL</color>";

        int  pin   = _core != null ? _core.activePinNumber : 0;
        bool blink = _core != null && _core.blinkEnabled;
        _system.text =
            $"<color={c}>STATUS</color>  {estado}\n" +
            $"<color={c}>PIN</color>     D{pin}\n" +
            $"<color={c}>MODE</color>    {(blink ? "BLINK" : "STEADY")}\n" +
            $"<color={c}>OUT</color>     {(_core != null ? _core.OutputVoltage : 0f):0.0} V";

        // ── NET LINK (estado de red real) ──
        if (_runner != null && _runner.IsRunning)
        {
            string rol = _runner.IsServer ? "HOST" : "CLIENT";
            string sala = _runner.SessionInfo != null && _runner.SessionInfo.IsValid
                        ? _runner.SessionInfo.Name : "-";
            int jug = _runner.SessionInfo != null && _runner.SessionInfo.IsValid
                    ? _runner.SessionInfo.PlayerCount : 0;
            string ping;
            try { ping = $"{_runner.GetPlayerRtt(_runner.LocalPlayer) * 1000.0:0} ms"; }
            catch { ping = "n/d"; }
            _net.text =
                $"<color={c}>LINK</color>    <color={sys}>ONLINE</color>\n" +
                $"<color={c}>ROLE</color>    {rol}\n" +
                $"<color={c}>ROOM</color>    {sala}\n" +
                $"<color={c}>PEERS</color>   {jug}\n" +
                $"<color={c}>PING</color>    {ping}";
        }
        else
        {
            _net.text =
                $"<color={c}>LINK</color>    <color={wrn}>OFFLINE</color>\n" +
                $"<color={c}>ROLE</color>    -\n" +
                $"<color={c}>ROOM</color>    -\n" +
                $"<color={c}>PEERS</color>   0\n" +
                $"<color={c}>PING</color>    -";
        }
    }

    // ─── Auto-construcción world-space ───────────────────────────────────
    public void BuildUI()
    {
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            var cgo = AxiStudioTheme.NewUI("ExplorerHUDCanvas", transform);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            cgo.AddComponent<UnityEngine.UI.CanvasScaler>();
            var rt = (RectTransform)canvas.transform;
            rt.sizeDelta = canvasSizePx;
            cgo.transform.localScale = Vector3.one * worldScale;
            cgo.transform.localPosition = Vector3.zero;
        }

        _telemetry = AxiStudioTheme.BuildPanel(canvas.transform, "TELEMETRY",
            new Vector2(0.005f, 0.04f), new Vector2(0.33f, 0.97f), 22);
        _system = AxiStudioTheme.BuildPanel(canvas.transform, "SYSTEM STATUS",
            new Vector2(0.34f, 0.04f), new Vector2(0.66f, 0.97f), 22);
        _net = AxiStudioTheme.BuildPanel(canvas.transform, "NET LINK",
            new Vector2(0.67f, 0.04f), new Vector2(0.995f, 0.97f), 22);
    }
}
