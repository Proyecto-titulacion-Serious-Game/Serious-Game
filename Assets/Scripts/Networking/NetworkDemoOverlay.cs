using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using TMPro;
using Fusion;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Overlay de EVIDENCIA DE RED para demostrar el multijugador asimétrico (Reto 4).
///
/// Prueba irrefutable de que un RPC cruzó la red: este overlay se suscribe a los
/// eventos estáticos que SOLO se disparan cuando un RPC de Fusion llega a un cliente
/// (ArduinoNetworkBridge.OnSketchReceived, GameSession.OnComponenteRecibido, etc.).
/// No origina nada; solo escucha. Por eso, ver el contador subir EN EL QUEST cuando
/// el Técnico pulsa "Enviar" en el PC demuestra que el dato viajó por Photon.
///
/// - Se auto-crea al cargar la escena (no hay que añadirlo a mano).
/// - En PC (sin XR) se dibuja con OnGUI en la esquina superior izquierda.
/// - En VR (XR activo) crea un panel world-space anclado al borde inferior del FOV,
///   visible tanto en el headset como en el casting del Quest.
/// - F9 alterna visibilidad (PC).
///
/// 100% aditivo: no modifica ningún script de gameplay.
/// </summary>
public class NetworkDemoOverlay : MonoBehaviour
{
    static NetworkDemoOverlay _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[NetworkDemoOverlay]");
        _instance = go.AddComponent<NetworkDemoOverlay>();
        DontDestroyOnLoad(go);
    }

    // ─── Estado ──────────────────────────────────────────────────────────
    struct LogLine { public string text; public float time; }
    readonly List<LogLine> _log = new();
    const int MaxLines = 6;

    int   _rpcCount;          // total de RPCs recibidos en ESTE dispositivo
    bool  _visible = true;
    bool  _useWorldCanvas;
    float _runnerSearchCd;
    bool  _bothHandled;       // ya se ocultó al conectarse ambos (2 jugadores)

    NetworkRunner _runner;
    Camera        _cam;

    // World-space (VR)
    Canvas    _canvas;
    TMP_Text  _text;

    // OnGUI (PC)
    GUIStyle _styleBox, _styleHdr, _styleLine;
    Texture2D _bgTex;

    // ─── Suscripción a eventos de RED ────────────────────────────────────
    void OnEnable()
    {
        ArduinoNetworkBridge.OnSketchReceived += HandleSketch;
        GameSession.OnComponenteRecibido      += HandleComponente;
        GameSession.OnResultadoValidacion     += HandleResultado;
        GameSession.OnRetoChanged             += HandleReto;
    }

    void OnDisable()
    {
        ArduinoNetworkBridge.OnSketchReceived -= HandleSketch;
        GameSession.OnComponenteRecibido      -= HandleComponente;
        GameSession.OnResultadoValidacion     -= HandleResultado;
        GameSession.OnRetoChanged             -= HandleReto;
    }

    void HandleSketch(int pin, PinMode mode, PinState state, bool blink, int delayOnMs, int delayOffMs)        => Push($"SKETCH   pin D{pin}  {state}{(blink ? "  BLINK " + delayOnMs + "ms" : "")}");

    void HandleComponente(ComponentType tipo, float valor, int variante)
        => Push($"COMPONENTE   {tipo} = {valor:0.##}  ({(ComponentVariant)variante})");

    void HandleResultado(bool ok, int cod)
        => Push($"VALIDACION   {(ok ? "PASS" : "FAIL")}  cod={cod}");

    void HandleReto(int reto)
        => Push($"CAMBIO RETO  ->  {reto}");

    void Push(string s)
    {
        _rpcCount++;
        _log.Add(new LogLine { text = $"[{_rpcCount:00}] {System.DateTime.Now:HH:mm:ss.fff}  {s}", time = Time.unscaledTime });
        if (_log.Count > MaxLines) _log.RemoveAt(0);
        Debug.Log($"[NetDemo] RPC #{_rpcCount}: {s}");
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────
    void Start()
    {
        _useWorldCanvas = XRSettings.isDeviceActive;
        if (_useWorldCanvas) BuildWorldCanvas();
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            _visible = !_visible;
#endif
        if (_runner == null || !_runner.IsRunning)
        {
            _runnerSearchCd -= Time.unscaledDeltaTime;
            if (_runnerSearchCd <= 0f)
            {
                _runner = FindAnyObjectByType<NetworkRunner>();
                _runnerSearchCd = 1f;
            }
        }

        // Apenas estén conectados AMBOS (Técnico + Explorador = 2 jugadores en la sala),
        // ocultar el overlay: la evidencia de red sigue saliendo por Debug.Log (logs del
        // juego). F9 lo vuelve a mostrar manualmente. Se re-arma si baja a <2 (reconexión).
        int players = (_runner != null && _runner.IsRunning &&
                       _runner.SessionInfo != null && _runner.SessionInfo.IsValid)
                      ? _runner.SessionInfo.PlayerCount : 0;
        if (players >= 2 && !_bothHandled)
        {
            _bothHandled = true;
            _visible = false;
            Debug.Log("[NetDemo] Ambos conectados (2 jugadores) → overlay oculto; la evidencia de red continúa en los logs. F9 para mostrarlo.");
        }
        else if (players < 2)
        {
            _bothHandled = false;
        }

        if (_useWorldCanvas && _text != null)
        {
            _canvas.gameObject.SetActive(_visible);
            if (_visible) _text.text = BuildStatusText() + "\n" + BuildLogText();
        }
    }

    void LateUpdate()
    {
        if (!_useWorldCanvas || _canvas == null || !_visible) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        // Anclar al borde inferior del campo de visión, mirando a la cámara.
        Transform t = _canvas.transform;
        t.position = _cam.transform.position
                   + _cam.transform.forward * 1.4f
                   - _cam.transform.up      * 0.55f;
        t.rotation = Quaternion.LookRotation(t.position - _cam.transform.position, _cam.transform.up);
    }

    // ─── Texto de estado (compartido PC / VR) ────────────────────────────
    string BuildStatusText()
    {
        string rol, sala = "—", jug = "—", ping = "—";

        if (_runner != null && _runner.IsRunning)
        {
            rol = _runner.IsServer ? "HOST  (Tecnico / PC)" : "CLIENT  (Explorador / VR)";
            if (_runner.SessionInfo != null && _runner.SessionInfo.IsValid)
            {
                sala = _runner.SessionInfo.Name;
                jug  = _runner.SessionInfo.PlayerCount.ToString();
            }
            try
            {
                double rtt = _runner.GetPlayerRtt(_runner.LocalPlayer);
                ping = $"{rtt * 1000.0:0} ms";
            }
            catch { ping = "n/d"; }
        }
        else
        {
            rol = "OFFLINE  (sin NetworkRunner)";
        }

        return
            $"== EVIDENCIA DE RED (Photon Fusion) ==\n" +
            $"ROL: {rol}\n" +
            $"SALA: {sala}    JUGADORES: {jug}    PING: {ping}\n" +
            $"RPCs RECIBIDOS EN ESTE EQUIPO: {_rpcCount}";
    }

    string BuildLogText()
    {
        if (_log.Count == 0) return "(esperando RPCs de la red...)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _log.Count; i++) sb.AppendLine(_log[i].text);
        return sb.ToString();
    }

    // ─── PC: OnGUI ───────────────────────────────────────────────────────
    void OnGUI()
    {
        if (_useWorldCanvas || !_visible) return;
        EnsureGUIStyles();

        const float w = 560f, pad = 10f;
        float h = 92f + MaxLines * 20f;
        var rect = new Rect(pad, pad, w, h);
        GUI.Box(rect, GUIContent.none, _styleBox);

        var inner = new Rect(rect.x + pad, rect.y + pad, w - pad * 2, h - pad * 2);
        GUILayout.BeginArea(inner);
        GUILayout.Label(BuildStatusText(), _styleHdr);
        GUILayout.Space(4);
        for (int i = 0; i < _log.Count; i++)
            GUILayout.Label(_log[i].text, _styleLine);
        if (_log.Count == 0)
            GUILayout.Label("(esperando RPCs de la red...)", _styleLine);
        GUILayout.EndArea();
    }

    void EnsureGUIStyles()
    {
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0.02f, 0.05f, 0.04f, 0.88f));
            _bgTex.Apply();
        }
        if (_styleBox == null)
            _styleBox = new GUIStyle(GUI.skin.box) { normal = { background = _bgTex } };
        if (_styleHdr == null)
            _styleHdr = new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = new Color(0f, 1f, 0.7f) } };
        if (_styleLine == null)
            _styleLine = new GUIStyle(GUI.skin.label)
            { fontSize = 13, richText = true, normal = { textColor = Color.white } };
    }

    // ─── VR: Canvas world-space ──────────────────────────────────────────
    void BuildWorldCanvas()
    {
        var go = new GameObject("DemoOverlayCanvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<UnityEngine.UI.CanvasScaler>();

        var rt = (RectTransform)_canvas.transform;
        rt.sizeDelta = new Vector2(900, 460);
        go.transform.localScale = Vector3.one * 0.0011f; // ~1.0m x 0.5m

        // Fondo
        var bgGo = new GameObject("BG");
        bgGo.transform.SetParent(go.transform, false);
        var bg = bgGo.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.02f, 0.05f, 0.04f, 0.88f);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

        // Texto
        var txtGo = new GameObject("Text");
        txtGo.transform.SetParent(bgGo.transform, false);
        _text = txtGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 26;
        _text.color = Color.white;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        var tRt = _text.rectTransform;
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(20, 16); tRt.offsetMax = new Vector2(-20, -16);
    }
}
