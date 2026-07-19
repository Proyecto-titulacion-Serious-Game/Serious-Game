using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using TMPro;

/// <summary>
/// Tutorial de bienvenida para el Explorador VR.
/// Muestra diapositivas de controles al inicio de la sesión.
/// Presionar Trigger (o Espacio en editor) avanza; en la última diapositiva inicia el juego.
///
/// SETUP: Añadir este componente a cualquier GameObject de la escena Explorador.
/// No necesita prefab — construye el canvas proceduralmente.
/// Opcionalmente, GameManager puede suscribirse a OnOnboardingComplete para arrancar el primer reto.
/// </summary>
public class ExplorerOnboarding : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Contenido de las diapositivas
    // ─────────────────────────────────────────────
    readonly struct Slide
    {
        public readonly string Title;
        public readonly string Body;
        public Slide(string t, string b) { Title = t; Body = b; }
    }

    static readonly Slide[] Slides =
    {
        new Slide(
            "Bienvenido al Laboratorio Virtual",
            "Eres el <b>Explorador</b>.\nTrabaja en equipo con el <b>Técnico</b>\npara diagnosticar y reparar los\ncircuitos eléctricos de la nave."),
        new Slide(
            "Movimiento",
            "<b>Palanca izquierda</b>  →  Desplazarse\n<b>Palanca derecha</b>   →  Girar\n\nMantente dentro de la zona de trabajo\niluminada en el suelo."),
        new Slide(
            "Agarrar componentes",
            "Acerca la mano al componente y\n<b>mantén pulsado el Grip</b> (lateral) para tomarlo.\n\n<b>Suelta el Grip</b> dentro del slot correcto\npara instalarlo — el slot brillará <b>amarillo</b>\ncuando estés en posición."),
        new Slide(
            "Multímetro",
            "<b>Trigger derecho</b>  →  Punta roja\n<b>Trigger izquierdo</b> →  Punta negra\n\nApunta la mano hacia un nodo del circuito\ny presiona el trigger para medir."),
        new Slide(
            "¡Todo listo!",
            "Al llegar a la <b>zona de trabajo</b> aparecerán\nel <b>tablero de pasos</b> y la <b>telemetría</b>\ncon lo que debes hacer en cada momento.\n\n<b>Comunícate con el Técnico</b> — él\ntiene el diagrama y las herramientas.\n\n¡Buena suerte!")
    };

    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Posición")]
    [Tooltip("Distancia frente a la cámara al aparece el panel.")]
    public float distanceFromCamera = 1.3f;
    [Range(0f, 1.8f)]
    [Tooltip("Altura del CENTRO del panel sobre el piso real (VRRig.pisoReferencia si existe en la " +
             "escena, si no el plano y=0 del mundo — mismo fallback que VRRig), en metros. " +
             "Ajustado iterativamente probando en VR: 0.23 (pies) -> 0.35 -> 1.1 (valor actual).")]
    public float alturaSobrePiso = 1.1f;

    [Header("Colores")]
    public Color backgroundColor = new Color(0.04f, 0.04f, 0.15f, 0.95f);
    public Color titleColor      = new Color(0.55f, 0.88f, 1.00f);
    public Color bodyColor       = new Color(0.92f, 0.92f, 0.92f);
    public Color accentColor     = new Color(0.30f, 0.82f, 0.45f);
    public Color hintColor       = new Color(0.75f, 0.75f, 0.75f);

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────

    /// <summary>Se dispara cuando el jugador completa todas las diapositivas.</summary>
    public static event System.Action OnOnboardingComplete;

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private int        _slideIndex;
    private bool       _triggerWasDown;
    private bool       _done;

    private GameObject _canvasGO;
    private TMP_Text   _titleTMP;
    private TMP_Text   _bodyTMP;
    private TMP_Text   _pageTMP;
    private TMP_Text   _hintTMP;
    private TMP_Text   _progressBarTMP;

    private readonly List<InputDevice> _right = new();
    private readonly List<InputDevice> _left  = new();

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────
    void Start()
    {
        BuildCanvas();
        PositionCanvas();
        ShowSlide(0);
        CacheControllers();
    }

    void Update()
    {
        if (_done) return;
        if (AdvancePressed()) Advance();
    }

    // ─────────────────────────────────────────────
    //  Input: trigger en cualquier mano o barra espaciadora
    // ─────────────────────────────────────────────
    bool AdvancePressed()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            return true;

        bool down = IsTriggerDown(_right) || IsTriggerDown(_left);
        if (down && !_triggerWasDown)
        {
            _triggerWasDown = true;
            return true;
        }
        if (!down) _triggerWasDown = false;
        return false;
    }

    bool IsTriggerDown(List<InputDevice> devices)
    {
        foreach (var d in devices)
            if (d.TryGetFeatureValue(CommonUsages.triggerButton, out bool v) && v)
                return true;
        return false;
    }

    void CacheControllers()
    {
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, _right);
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left  | InputDeviceCharacteristics.Controller, _left);
    }

    // ─────────────────────────────────────────────
    //  Lógica de diapositivas
    // ─────────────────────────────────────────────
    void Advance()
    {
        _slideIndex++;
        if (_slideIndex >= Slides.Length)
        {
            Dismiss();
            return;
        }
        ShowSlide(_slideIndex);
    }

    void ShowSlide(int index)
    {
        var s = Slides[index];
        if (_titleTMP != null) _titleTMP.text = s.Title;
        if (_bodyTMP  != null) _bodyTMP.text  = s.Body;
        if (_pageTMP  != null) _pageTMP.text  = $"{index + 1} / {Slides.Length}";

        bool isLast = index == Slides.Length - 1;
        if (_hintTMP != null)
            _hintTMP.text = isLast
                ? ">>  Presiona <b>Trigger</b> para comenzar"
                : "Presiona <b>Trigger</b> para continuar  ->";

        // Progress bar as dots
        if (_progressBarTMP != null)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Slides.Length; i++)
                sb.Append(i == index ? "*  " : "o  ");
            _progressBarTMP.text = sb.ToString().TrimEnd();
        }
    }

    void Dismiss()
    {
        _done = true;
        OnOnboardingComplete?.Invoke();
        Destroy(_canvasGO);
        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────
    //  Posicionamiento: una sola vez al inicio
    // ─────────────────────────────────────────────
    void PositionCanvas()
    {
        if (_canvasGO == null) return;

        Transform cam = Camera.main != null ? Camera.main.transform : transform;
        Vector3 fwd   = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;
        if (fwd == Vector3.zero) fwd = Vector3.forward;

        Vector3 pos = cam.position + fwd * distanceFromCamera;
        pos.y = FindFloorY() + alturaSobrePiso;

        _canvasGO.transform.position = pos;
        _canvasGO.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    /// <summary>Y del piso real del Explorador (mismo piso que usa VRRig para calibrar la altura
    /// del avatar) — NO Camera.main.position.y, que es la altura de los OJOS, no la del piso.</summary>
    float FindFloorY()
    {
        var vrRig = FindAnyObjectByType<VRRig>();
        if (vrRig != null && vrRig.pisoReferencia != null)
            return vrRig.pisoReferencia.position.y;
        return 0f; // mismo fallback que VRRig.pisoReferencia vacío: plano y=0 del mundo
    }

    // ─────────────────────────────────────────────
    //  Construcción procedural del canvas
    //  Canvas: 560 x 360 px · escala 0.001 → 0.56m x 0.36m en mundo
    // ─────────────────────────────────────────────
    void BuildCanvas()
    {
        _canvasGO = new GameObject("OnboardingCanvas");
        _canvasGO.transform.localScale = Vector3.one * 0.001f;

        var canvas        = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt        = canvas.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(560, 360);

        // ── Fondo ────────────────────────────────
        var bgImg   = MakeRect("BG", rt, new Vector2(560, 360), Vector2.zero).gameObject.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = backgroundColor;

        // ── Barra superior de color (acento) ─────
        var topBar = MakeRect("Accent", rt, new Vector2(560, 5), new Vector2(0, 177f)).gameObject.AddComponent<UnityEngine.UI.Image>();
        topBar.color = accentColor;

        // ── Indicador de página  (top-right) ─────
        _pageTMP           = AddTMP("Page", rt, new Vector2(100, 26), new Vector2(210, 152), 14);
        _pageTMP.color     = accentColor;
        _pageTMP.alignment = TextAlignmentOptions.Right;

        // ── Título ───────────────────────────────
        _titleTMP           = AddTMP("Title", rt, new Vector2(500, 40), new Vector2(0, 125), 22);
        _titleTMP.color     = titleColor;
        _titleTMP.fontStyle = FontStyles.Bold;
        _titleTMP.alignment = TextAlignmentOptions.Left;

        // ── Separador ────────────────────────────
        var sep   = MakeRect("Sep", rt, new Vector2(500, 1), new Vector2(0, 104)).gameObject.AddComponent<UnityEngine.UI.Image>();
        sep.color = new Color(1, 1, 1, 0.12f);

        // ── Cuerpo ───────────────────────────────
        _bodyTMP              = AddTMP("Body", rt, new Vector2(500, 180), new Vector2(0, 3), 17);
        _bodyTMP.color        = bodyColor;
        _bodyTMP.alignment    = TextAlignmentOptions.Left;
        _bodyTMP.lineSpacing  = 4f;

        // ── Puntos de progreso ───────────────────
        _progressBarTMP           = AddTMP("Dots", rt, new Vector2(500, 22), new Vector2(0, -145), 18);
        _progressBarTMP.color     = accentColor;
        _progressBarTMP.alignment = TextAlignmentOptions.Center;

        // ── Pista / hint ─────────────────────────
        _hintTMP           = AddTMP("Hint", rt, new Vector2(500, 24), new Vector2(0, -163), 14);
        _hintTMP.color     = hintColor;
        _hintTMP.alignment = TextAlignmentOptions.Center;
        _hintTMP.fontStyle = FontStyles.Italic;
    }

    RectTransform MakeRect(string name, RectTransform parent, Vector2 size, Vector2 pos)
    {
        var go     = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt     = go.AddComponent<RectTransform>();
        rt.sizeDelta       = size;
        rt.anchoredPosition = pos;
        rt.localScale      = Vector3.one;
        return rt;
    }

    TMP_Text AddTMP(string name, RectTransform parent, Vector2 size, Vector2 pos, float fontSize)
    {
        var rt   = MakeRect(name, parent, size, pos);
        var tmp  = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.fontSize          = fontSize;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode      = TextOverflowModes.Overflow;
        tmp.richText          = true;
        return tmp;
    }
}
