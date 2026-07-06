using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

/// <summary>
/// Al completar el juego (Reto 4), muestra en el PC del Técnico un botón "Resultados"
/// que abre el dashboard web local en el navegador para ver las estadísticas de la partida.
///
/// Se auto-crea en runtime SOLO en PC (no en VR), sin necesidad de configuración en escena
/// (igual que los demás overlays del proyecto). El servidor del dashboard debe estar activo
/// (Tools → TITA → Red → Setup Dashboard Docente).
/// </summary>
public class TechnicianResultsWebButton : MonoBehaviour
{
    static TechnicianResultsWebButton _instance;

    [Tooltip("Si queda vacío, abre http://localhost:<puerto>/ del DashboardServer.")]
    public string dashboardUrlOverride = "";

    GameObject _canvasGO;
    Button     _btn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (XRSettings.isDeviceActive) return;            // VR (Explorador) → no aplica
        if (_instance != null) return;
        var go = new GameObject("[TechnicianResultsWebButton]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<TechnicianResultsWebButton>();
    }

    void OnEnable()  => GameManager.OnGameCompleted += HandleGameCompleted;
    void OnDisable() => GameManager.OnGameCompleted -= HandleGameCompleted;

    void HandleGameCompleted()
    {
        EnsureEventSystem();
        if (_btn == null) BuildButton();
        if (_canvasGO != null) _canvasGO.SetActive(true);
    }

    // ─────────────────────────────────────────────
    void BuildButton()
    {
        _canvasGO = new GameObject("Canvas_ResultadosWeb",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _canvasGO.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;                       // por encima del HUD
        var scaler = _canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        DontDestroyOnLoad(_canvasGO);

        var btnGO = new GameObject("BTN_ResultadosWeb",
            typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_canvasGO.transform, false);
        var rt = (RectTransform)btnGO.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 48f);       // centrado abajo
        rt.sizeDelta        = new Vector2(380f, 84f);

        btnGO.GetComponent<Image>().color = new Color(0.12f, 0.43f, 0.92f, 1f);
        _btn = btnGO.GetComponent<Button>();
        _btn.onClick.AddListener(AbrirDashboard);

        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(btnGO.transform, false);
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.text      = "Resultados (Web)";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 34;
        tmp.color     = Color.white;
    }

    public void AbrirDashboard()
    {
        string url = dashboardUrlOverride;
        if (string.IsNullOrEmpty(url))
        {
            int port = 8080;
            var server = FindAnyObjectByType<DashboardServer>(FindObjectsInactive.Include);
            if (server != null) port = server.port;
            url = $"http://localhost:{port}/";
        }
        Debug.Log($"[TechnicianResultsWebButton] Abriendo dashboard: {url}");
        Application.OpenURL(url);
    }

    /// <summary>Garantiza un EventSystem (con módulo del New Input System) para que el botón sea clickeable.</summary>
    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }
}
