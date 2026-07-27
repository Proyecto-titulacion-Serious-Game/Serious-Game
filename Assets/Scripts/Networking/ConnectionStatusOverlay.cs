using UnityEngine;
using UnityEngine.XR;
using TMPro;

/// <summary>
/// Muestra en pantalla los mensajes que <see cref="ConnectionManager.OnConnectionFailed"/> ya
/// emitía ("Esperando a que el Técnico cree la sala (12/40)…", "Reintentando…") — que hasta ahora
/// no tenían NINGÚN suscriptor en todo el proyecto. Efecto real: durante los hasta ~4 minutos que
/// el Explorador puede pasar en la sala de espera de <c>GameNotFound</c> (arrancó antes de que el
/// Técnico creara la sala), el jugador no veía nada en pantalla — indistinguible de un colgado real,
/// la causa más probable de la "inestabilidad de red" reportada tras una sesión real de 2 máquinas.
///
/// PC (Técnico): overlay simple con OnGUI, igual que <see cref="ExplorerLinkOverlay"/>/
/// <see cref="RoomCodeEntryUI"/>. VR (Explorador): un Canvas Screen Space - Camera anclado a
/// Camera.main — OnGUI no es fiable dentro del visor — mismo patrón ya usado en
/// <see cref="PlayerFeedbackUI.EnsureCelebrationCanvas"/>.
/// </summary>
public class ConnectionStatusOverlay : MonoBehaviour
{
    static ConnectionStatusOverlay _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[ConnectionStatusOverlay]");
        _instance = go.AddComponent<ConnectionStatusOverlay>();
        DontDestroyOnLoad(go);
    }

    string _mensaje = "";
    bool   _visible;

    // ─── VR (Screen Space - Camera, anclado a Camera.main) ───────────────
    GameObject _vrCanvasGO;
    GameObject _vrPanel;
    TMP_Text   _vrTexto;

    // ─── PC (OnGUI) ────────────────────────────────────────────────────
    GUIStyle  _box, _text;
    Texture2D _bg;

    void OnEnable()
    {
        ConnectionManager.OnConnectionFailed += OnFailed;
        ConnectionManager.OnConnected        += OnConnected;
    }

    void OnDisable()
    {
        ConnectionManager.OnConnectionFailed -= OnFailed;
        ConnectionManager.OnConnected        -= OnConnected;
    }

    void OnFailed(string mensaje)
    {
        _mensaje = mensaje;
        _visible = true;
        if (XRSettings.isDeviceActive) MostrarEnVR();
    }

    void OnConnected()
    {
        _visible = false;
        if (_vrPanel != null) _vrPanel.SetActive(false);
    }

    // ─── PC ────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!_visible || XRSettings.isDeviceActive) return;
        EnsurePCStyles();

        const float w = 640f, h = 56f;
        var rect = new Rect((Screen.width - w) * 0.5f, 10f, w, h);
        GUI.Box(rect, GUIContent.none, _box);
        GUI.Label(new Rect(rect.x + 18, rect.y, w - 36, h), _mensaje, _text);
    }

    void EnsurePCStyles()
    {
        if (_bg == null)
        {
            _bg = new Texture2D(1, 1);
            _bg.SetPixel(0, 0, new Color(0.12f, 0.09f, 0f, 0.92f));
            _bg.Apply();
        }
        if (_box == null)
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg } };
        if (_text == null)
            _text = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 15,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 0.85f, 0.4f) }
            };
    }

    // ─── VR ────────────────────────────────────────────────────────────
    void MostrarEnVR()
    {
        EnsureVRCanvas();
        if (_vrPanel == null) return;   // Camera.main aún no listo — el próximo reintento (~4s) lo reintenta
        _vrTexto.text = _mensaje;
        _vrPanel.SetActive(true);
    }

    void EnsureVRCanvas()
    {
        if (_vrCanvasGO != null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        _vrCanvasGO = new GameObject("ConnectionStatusCanvas");
        var canvas = _vrCanvasGO.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera   = cam;
        canvas.planeDistance = 1.2f;

        var canvasScaler = _vrCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasScaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920, 1080);
        canvasScaler.matchWidthOrHeight  = 0.5f;

        var canvasRT = _vrCanvasGO.GetComponent<RectTransform>();

        _vrPanel = new GameObject("Panel_Estado");
        _vrPanel.transform.SetParent(canvasRT, false);
        var panelRT = _vrPanel.AddComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.9f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(900, 90);
        panelRT.anchoredPosition = Vector2.zero;

        var bg = _vrPanel.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.08f, 0.06f, 0f, 0.85f);

        var textGO = new GameObject("Texto");
        textGO.transform.SetParent(panelRT, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.sizeDelta = new Vector2(860, 80);
        textRT.anchoredPosition = Vector2.zero;
        textRT.localScale = Vector3.one;

        _vrTexto = textGO.AddComponent<TextMeshProUGUI>();
        _vrTexto.fontSize  = 30;
        _vrTexto.alignment = TextAlignmentOptions.Center;
        _vrTexto.color     = new Color(1f, 0.85f, 0.4f);
        _vrTexto.textWrappingMode = TextWrappingModes.Normal;

        _vrPanel.SetActive(false);
    }
}
