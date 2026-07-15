using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Menú de pausa del Técnico (PC plano). Esc abre el menú y pausa; Esc de nuevo (o el
/// botón "Reanudar") reanuda. Se auto-crea en runtime — NO va en ninguna escena ni prefab.
///
/// Solo el Técnico: el Explorador (Quest APK o PCVR) corre con XR activo y se descarta.
///
/// Pausa = Time.timeScale 0 + los controladores del Técnico (TechnicianMover,
/// ThirdPersonCamera, WorkstationSeat) consultan IsPaused y se congelan.
/// NOTA multijugador: con timeScale=0 se congela también la simulación del host; ideal
/// para pruebas en solo. Si se quiere pausa "solo local" durante una sesión con el
/// Explorador conectado, quitar la línea de timeScale.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    private GameObject _panel;          // panel principal de pausa
    private GameObject _settingsPanel;  // sub-panel de ajustes

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return;                                   // Explorador Quest: no aplica
#else
        if (UnityEngine.XR.XRSettings.isDeviceActive) return;  // Explorador PCVR
        if (!IsTecnicoLoaded()) return;           // solo la escena del Técnico
        if (FindAnyObjectByType<PauseMenu>() != null) return;

        var go = new GameObject("[PauseMenu]");
        go.AddComponent<PauseMenu>();
        DontDestroyOnLoad(go);
#endif
    }

    static bool IsTecnicoLoaded()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == "Tecnico") return true;
        return false;
    }

    void Start() => BuildUI();

    void OnDestroy()
    {
        // Por si el objeto se destruye estando en pausa, no dejar el juego congelado.
        if (IsPaused) { IsPaused = false; Time.timeScale = 1f; }
    }

    void Update()
    {
        // unscaledDeltaTime no hace falta: Update corre aunque timeScale sea 0.
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        // Si hay un panel de Arduino o el manual abiertos, este Escape lo cierra a ÉL (lo
        // manejan ArduinoMonitorInteract / ManualBookOpener) y NO abrimos la pausa encima:
        // evita que un Escape para cerrar el manual/Hub también congele el juego con la
        // pausa. Solo cuando no hay ningún panel abierto, Escape togglea la pausa.
        if (!IsPaused && (ArduinoMonitorInteract.AnyOpen || ManualBookOpener.AnyOpen)) return;

        // Estando en Ajustes, Escape vuelve al menú principal de pausa (no sale del todo).
        if (IsPaused && _settingsPanel != null && _settingsPanel.activeSelf) { CloseSettings(); return; }

        Toggle();
    }

    void Toggle() { if (IsPaused) Resume(); else Pause(); }

    void Pause()
    {
        IsPaused       = true;
        Time.timeScale = 0f;
        if (_settingsPanel != null) _settingsPanel.SetActive(false);  // siempre abrir en el menú principal
        if (_panel != null)         _panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    public void Resume()
    {
        IsPaused       = false;
        Time.timeScale = 1f;
        if (_panel != null)         _panel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);   // por si quedó en Ajustes

        // Si se estaba caminando, devolver el cursor bloqueado para el mouse-look.
        if (ThirdPersonCamera.IsActive)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ─────────────────────────────────────────────
    //  UI generada en runtime
    // ─────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGO = new GameObject("PauseCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;                       // por encima de todo
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Fondo oscurecido (atrapa los clics → no pasan al juego)
        _panel = new GameObject("Dim");
        _panel.transform.SetParent(canvasGO.transform, false);
        var dim = _panel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        Stretch(dim.rectTransform);

        // Título
        var title = NewText(_panel.transform, "PAUSA", 80, FontStyles.Bold);
        Anchor(title.rectTransform, 0.5f, 0.70f, new Vector2(700, 130));

        // Botón Reanudar
        MakeButton(_panel.transform, "Reanudar (Esc)", 0.54f, new Color(0.10f, 0.55f, 0.36f, 1f), Resume);
        // Botón Ajustes
        MakeButton(_panel.transform, "Ajustes", 0.42f, new Color(0.16f, 0.38f, 0.60f, 1f), OpenSettings);
        // Botón Salir del juego
        MakeButton(_panel.transform, "Salir del juego", 0.30f, new Color(0.55f, 0.16f, 0.16f, 1f), QuitGame);

        // Hint
        var hint = NewText(_panel.transform, "WASD: moverse   ·   Mouse: mirar   ·   F: liberar cursor   ·   E: sentarse",
                           24, FontStyles.Normal);
        hint.color = new Color(1f, 1f, 1f, 0.6f);
        Anchor(hint.rectTransform, 0.5f, 0.16f, new Vector2(1200, 60));

        BuildSettingsPanel(canvasGO.transform);

        _panel.SetActive(false);
    }

    // ─────────────────────────────────────────────
    //  Panel de Ajustes
    // ─────────────────────────────────────────────
    void BuildSettingsPanel(Transform canvas)
    {
        _settingsPanel = new GameObject("SettingsPanel");
        _settingsPanel.transform.SetParent(canvas, false);
        var dim = _settingsPanel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.85f);
        Stretch(dim.rectTransform);

        var title = NewText(_settingsPanel.transform, "AJUSTES", 60, FontStyles.Bold);
        Anchor(title.rectTransform, 0.5f, 0.82f, new Vector2(700, 100));

        // Sensibilidad del mouse (multiplicador 0.2–3.0)
        MakeSlider(_settingsPanel.transform, "Sensibilidad del mouse", 0.64f,
            GameSettings.SensMin, GameSettings.SensMax, GameSettings.MouseSensitivity,
            v => GameSettings.MouseSensitivity = v, v => $"{v:0.0}x");

        // Volumen de sonidos (SFX) 0–100%
        MakeSlider(_settingsPanel.transform, "Volumen de sonidos", 0.48f,
            0f, 1f, GameSettings.SfxVolume,
            v => GameSettings.SfxVolume = v, v => $"{v * 100f:0}%");

        // Volumen de música / ambientación 0–100%
        MakeSlider(_settingsPanel.transform, "Volumen de música", 0.32f,
            0f, 1f, GameSettings.MusicVolume,
            v => GameSettings.MusicVolume = v, v => $"{v * 100f:0}%");

        // Botón Volver
        MakeButton(_settingsPanel.transform, "Volver", 0.15f, new Color(0.16f, 0.38f, 0.60f, 1f), CloseSettings);

        _settingsPanel.SetActive(false);
    }

    void OpenSettings()  { if (_panel) _panel.SetActive(false); if (_settingsPanel) _settingsPanel.SetActive(true); }
    void CloseSettings() { if (_settingsPanel) _settingsPanel.SetActive(false); if (_panel) _panel.SetActive(true); }

    // ─────────────────────────────────────────────
    //  Constructores de UI
    // ─────────────────────────────────────────────
    Button MakeButton(Transform parent, string label, float anchorY, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go  = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        Anchor(img.rectTransform, 0.5f, anchorY, new Vector2(380, 80));
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        var lbl = NewText(go.transform, label, 32, FontStyles.Normal);
        Stretch(lbl.rectTransform);
        return btn;
    }

    /// <summary>Fila con etiqueta + valor + slider funcional, cableado a GameSettings.</summary>
    void MakeSlider(Transform parent, string label, float anchorY,
                    float min, float max, float value,
                    System.Action<float> onChanged, System.Func<float, string> fmt)
    {
        // Etiqueta (izquierda) y valor (derecha) en la misma franja.
        var lab = NewText(parent, label, 30, FontStyles.Normal);
        lab.alignment = TextAlignmentOptions.Left;
        Anchor(lab.rectTransform, 0.5f, anchorY + 0.055f, new Vector2(760, 44));

        var valTxt = NewText(parent, fmt(value), 30, FontStyles.Bold);
        valTxt.alignment = TextAlignmentOptions.Right;
        Anchor(valTxt.rectTransform, 0.5f, anchorY + 0.055f, new Vector2(760, 44));

        // Slider
        var sgo = new GameObject("Slider", typeof(RectTransform));
        sgo.transform.SetParent(parent, false);
        Anchor((RectTransform)sgo.transform, 0.5f, anchorY, new Vector2(760, 26));
        var slider = sgo.AddComponent<Slider>();

        var bg = NewImage(sgo.transform, new Color(1f, 1f, 1f, 0.20f));
        Stretch(bg.rectTransform);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sgo.transform, false);
        var faRt = (RectTransform)fillArea.transform;
        faRt.anchorMin = new Vector2(0f, 0.25f); faRt.anchorMax = new Vector2(1f, 0.75f);
        faRt.offsetMin = new Vector2(6f, 0f);    faRt.offsetMax = new Vector2(-6f, 0f);
        var fill = NewImage(fillArea.transform, new Color(0.25f, 0.8f, 0.55f, 1f));
        fill.rectTransform.anchorMin = new Vector2(0f, 0f); fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = new Vector2(12f, 0f);

        var hsa = new GameObject("Handle Slide Area", typeof(RectTransform));
        hsa.transform.SetParent(sgo.transform, false);
        var hsaRt = (RectTransform)hsa.transform;
        hsaRt.anchorMin = Vector2.zero; hsaRt.anchorMax = Vector2.one;
        hsaRt.offsetMin = new Vector2(12f, 0f); hsaRt.offsetMax = new Vector2(-12f, 0f);
        var handle = NewImage(hsa.transform, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(26f, 26f);
        handle.rectTransform.anchorMin = new Vector2(0f, 0.5f); handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);

        slider.fillRect      = fill.rectTransform;
        slider.handleRect    = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction     = Slider.Direction.LeftToRight;
        slider.minValue = min; slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v => { onChanged(v); valTxt.text = fmt(v); });
    }

    /// <summary>Cierra el juego (o detiene el Play en el Editor). Antes de cerrar, "Salir" CUENTA COMO
    /// PARTIDA FINALIZADA: registra el resultado parcial y espera a que suba a Google Sheets.</summary>
    public void QuitGame()
    {
        Time.timeScale = 1f;
        IsPaused = false;
        Debug.Log("[PauseMenu] Salir del juego solicitado.");
        StartCoroutine(FinalizarYSalir());
    }

    IEnumerator FinalizarYSalir()
    {
        // 1. Finalizar la sesión con el progreso actual (dispara OnSessionEnded → guarda + sube).
        var obj = FindAnyObjectByType<ObjectiveSystem>();
        if (obj != null) obj.FinalizarSesion();

        // 2. Esperar (máx 5 s) a que termine la subida a Sheets antes de cerrar.
        var exp = SessionDataExporter.Instance;
        float t = 0f;
        while (exp != null && exp.SubidaEnCurso && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }

        Debug.Log("[PauseMenu] Sesión registrada. Cerrando el juego.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static Image NewImage(Transform parent, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static TextMeshProUGUI NewText(Transform parent, string text, float size, FontStyles style)
    {
        var go  = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text      = text;
        t.fontSize  = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color     = Color.white;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, float ax, float ay, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
    }
}
