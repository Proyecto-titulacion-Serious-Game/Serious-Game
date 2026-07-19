using System;
using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// HUD de instrucciones del Explorador — muestra ACCIONES, nunca teoría.
///
/// Principio: el Explorador no necesita saber el por qué,
/// solo el qué hacer ahora mismo.
/// Ejemplos correctos: "Mide el nodo A con la punta roja"
/// Ejemplos INCORRECTOS: "Calcula V=I×R para encontrar la resistencia"
///
/// También muestra la notificación cuando el Técnico envía un componente.
///
/// JERARQUÍA:
///   ExplorerHUD [Canvas World Space, hijo de Main Camera]
///     └─ Panel_Instruccion  ← este script va aquí
///         ├─ TMP_Instruccion      (instrucción principal)
///         ├─ TMP_SubInstruccion   (detalle adicional)
///         ├─ TMP_Paso            ("Paso 2 de 4")
///         ├─ Panel_Notificacion  (aparece cuando llega componente)
///         │   ├─ TMP_Notificacion
///         │   └─ Img_Icono
///         └─ Img_Progreso        (barra de progreso del reto)
/// </summary>
public class PlayerFeedbackUI : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias de sistemas")]
    public InstructionSystem       instructionSystem;
    public GameManager             gameManager;
    public Multimeter              multimeter;
    public ComponentDeliverySystem delivery;

    [Header("Textos principales (TMP)")]
    public TMP_Text txtInstruccion;       // Instrucción principal grande
    public TMP_Text txtSubInstruccion;    // Detalle adicional (más pequeño)
    public TMP_Text txtPaso;             // "Paso 2 de 4"

    [Header("Barra de progreso")]
    public Image   barraProgreso;         // Fill Amount = progreso
    public TMP_Text txtProgresoPorcentaje;

    [Header("Panel de notificación (llega componente)")]
    public GameObject panelNotificacion;  // Se activa/desactiva
    public TMP_Text   txtNotificacion;    // "¡El Técnico te envió una Resistencia!"
    public Image      imgIconoComponente; // Ícono del componente recibido
    public Sprite     spriteResistor;
    public Sprite     spriteLED;
    public Sprite     spriteCapacitor;

    [Header("Colores del panel según estado")]
    public Color colorNormal    = new Color(0.05f, 0.05f, 0.15f, 0.88f);
    public Color colorAccion    = new Color(0.05f, 0.15f, 0.05f, 0.88f);
    public Color colorAlerta    = new Color(0.2f,  0.08f, 0.0f,  0.88f);
    public Color colorCompletado= new Color(0.0f,  0.2f,  0.05f, 0.88f);
    public Image fondoPanel;

    [Header("Panel de mensaje (contenedor de txtInstruccion/txtSubInstruccion)")]
    [Tooltip("Se activa solo al completar/perder un reto y se oculta solo 5s después.")]
    public GameObject panelMensaje;

    [Header("Celebración de victoria (Canvas Screen Space propio, enganchado a la cámara)")]
    [Tooltip("Segundos que el panel central '¡FELICIDADES!' queda visible antes de ocultarse solo.")]
    public float duracionFelicitacionCentro = 3f;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private float _updateTimer;
    private const float INTERVAL = 0.15f;
    private int   _totalPasos = 4;
    private bool  _mostrandoNotificacion = false;

    // Celebración: canvas Screen Space - Camera independiente del ExplorerHUD WorldSpace,
    // para que el aviso quede fijo en la vista del Explorador (no billboardeado/lerp como el resto del HUD).
    // STATIC a propósito: Explorador.unity tiene MÁS DE UNA instancia de PlayerFeedbackUI activa a la
    // vez (ExplorerHUD propio + un "PlayerFeedbackSystem" agregado aparte bajo GameManager_System —
    // ver memoria 'explorador_instructionsystem_duplicado'). Si esto fuera de instancia, cada una
    // crearía su PROPIO canvas y el jugador vería el aviso duplicado/superpuesto.
    private static GameObject _celebCanvasGO;
    private static GameObject _panelCentro;
    private static TMP_Text   _txtCentroTitulo;
    private static TMP_Text   _txtCentroSub;
    private static GameObject _panelEsquina;
    private static TMP_Text   _txtEsquina;
    private static Coroutine  _celebracionCentroCo;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void OnEnable()
    {
        GameManager.OnLevelLoaded               += OnLevelLoaded;
        GameManager.OnLevelCompleted            += OnLevelCompleted;
        GameManager.OnGameCompleted             += OnGameCompleted;
        ComponentDeliverySystem.OnComponentSent += OnComponentSent;
    }

    void OnDisable()
    {
        GameManager.OnLevelLoaded               -= OnLevelLoaded;
        GameManager.OnLevelCompleted            -= OnLevelCompleted;
        GameManager.OnGameCompleted             -= OnGameCompleted;
        ComponentDeliverySystem.OnComponentSent -= OnComponentSent;
    }

    void Start()
    {
        if (panelNotificacion != null) panelNotificacion.SetActive(false);
        AutoWireReferencias();
    }

    /// <summary>
    /// Estas referencias nunca se cablearon en el ExplorerHUD.prefab (fileID: 0 en el YAML), así
    /// que el HUD de instrucciones y el panel de mensaje quedaban mudos aunque el código disparara
    /// los eventos correctamente. Fallback defensivo: si el Inspector no las asignó, buscarlas en
    /// escena, igual que ya hace GameManager con protoSim o RoomCodeEntryUI con el runner.
    /// </summary>
    void AutoWireReferencias()
    {
        if (gameManager        == null) gameManager        = FindAnyObjectByType<GameManager>();
        if (instructionSystem  == null) instructionSystem  = FindAnyObjectByType<InstructionSystem>();
        if (multimeter         == null) multimeter         = FindAnyObjectByType<Multimeter>();
        if (delivery           == null) delivery           = FindAnyObjectByType<ComponentDeliverySystem>();
        if (panelMensaje       == null && txtInstruccion != null && txtInstruccion.transform.parent != null)
            panelMensaje = txtInstruccion.transform.parent.gameObject;
    }

    void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < INTERVAL) return;
        _updateTimer = 0f;
        RefreshHUD();
    }

    // ─────────────────────────────────────────────
    //  Actualización principal
    // ─────────────────────────────────────────────

    void RefreshHUD()
    {
        if (gameManager == null || instructionSystem == null) return;
        if (_mostrandoNotificacion) return;  // No interrumpir notificaciones

        switch (gameManager.currentLevel)
        {
            case LevelType.OhmLaw:
                _totalPasos = 4;
                MostrarInstruccionOhmLaw();
                break;
            case LevelType.Parallel:
                _totalPasos = 3;
                MostrarInstruccionParallel();
                break;
            case LevelType.Mixed:
                _totalPasos = 4;
                MostrarInstruccionMixed();
                break;
            case LevelType.Arduino:
                _totalPasos = 5;
                MostrarInstruccionArduino();
                break;
        }

        ActualizarProgreso();
    }

    // ─────────────────────────────────────────────
    //  Instrucciones por reto — solo acciones físicas
    // ─────────────────────────────────────────────

    void MostrarInstruccionOhmLaw()
    {
        switch (instructionSystem.currentStep)
        {
            case 0:
                Mostrar(
                    "Apunta con la mano DERECHA\nal primer nodo del circuito",
                    "Presiona el trigger para colocar\nla punta roja del multímetro",
                    Color.clear, colorNormal);
                break;
            case 1:
                Mostrar(
                    "Apunta con la mano IZQUIERDA\nal segundo nodo",
                    "Presiona el trigger izquierdo\npara colocar la punta negra",
                    Color.clear, colorNormal);
                break;
            case 2:
                Mostrar(
                    "Dile al Técnico el voltaje\nque muestra tu multímetro",
                    "Espera instrucciones del Técnico.\nÉl calculará el valor correcto.",
                    Color.clear, colorAccion);
                break;
            case 3:
                Mostrar(
                    "Agarra el componente que\nrecibirás del Técnico",
                    "Grip derecho para tomarlo.\nLlevalo al slot del panel.",
                    Color.clear, colorAccion);
                break;
            default:
                if (gameManager.levelCompleted)
                    Mostrar("¡Reto 1 completado!", "El circuito está funcionando correctamente.", Color.clear, colorCompletado);
                break;
        }
    }

    void MostrarInstruccionParallel()
    {
        switch (instructionSystem.currentStep)
        {
            case 0:
                Mostrar(
                    "Observa qué sensores (LEDs)\nestán apagados en el panel",
                    "Mide con el multímetro el\nvoltaje de cada sensor apagado",
                    Color.clear, colorAlerta);
                break;
            case 1:
                Mostrar(
                    "Reporta al Técnico cuáles\nsensores no tienen voltaje",
                    "El Técnico identificará\nla rama rota del circuito",
                    Color.clear, colorNormal);
                break;
            case 2:
                Mostrar(
                    "Reconecta el cable\nsoltado en el panel",
                    "Grip para tomar el cable.\nArrastralo al punto de conexión.",
                    Color.clear, colorAccion);
                break;
            default:
                if (gameManager.levelCompleted)
                    Mostrar("¡Reto 2 completado!", "Todos los sensores operativos.", Color.clear, colorCompletado);
                break;
        }
    }

    void MostrarInstruccionMixed()
    {
        switch (instructionSystem.currentStep)
        {
            case 0:
                Mostrar(
                    "Hay humo en el panel.\nLocaliza el capacitor",
                    "Dile al Técnico qué\ncomponente tiene humo",
                    Color.clear, colorAlerta);
                break;
            case 1:
                // BUG real corregido: este paso decía "gira el capacitor con Botón B", pero esa
                // rotación en el sitio (PlayerInteraction.CorrectCapacitorPolarity) es código MUERTO
                // — nada la llama. La corrección real del Reto 3 es por ENTREGA (igual que el
                // resistor/LED): el Técnico manda un capacitor con polaridad correcta y el Explorador
                // lo instala en el slot (ComponentDeliverySystem.ApplyRepairToCircuit), tal como ya lo
                // dice el paso 3 para el resto de piezas. Ver memoria "Reto3 capacitor: bug de
                // completabilidad arreglado" — ese fix ya movió la corrección a la entrega; esta
                // instrucción en pantalla nunca se actualizó y dejaba al jugador rotando algo que no
                // hacía nada.
                Mostrar(
                    "Espera el capacitor\ncorregido del Técnico",
                    "Grip para tomarlo.\nColócalo en el slot correcto.",
                    Color.clear, colorAccion);
                break;
            case 2:
                Mostrar(
                    "Localiza el LED apagado.\nUsa la lupa para ver\nel sentido de la flecha",
                    "Dile al Técnico la\norientación que ves",
                    Color.clear, colorNormal);
                break;
            case 3:
                Mostrar(
                    "Instala el componente\nque envíe el Técnico",
                    "Grip para tomarlo.\nColócalo en el slot correcto.",
                    Color.clear, colorAccion);
                break;
            default:
                if (gameManager.levelCompleted)
                    Mostrar("¡Reto 3 completado!", "Módulo de control restaurado.", Color.clear, colorCompletado);
                break;
        }
    }

    void MostrarInstruccionArduino()
    {
        switch (instructionSystem.currentStep)
        {
            case 0:
                Mostrar(
                    "Espera el sketch\ndel Técnico",
                    "El Técnico elegirá el pin.\nEscucha por radio cuál eligió.",
                    Color.clear, colorNormal);
                break;
            case 1:
                Mostrar(
                    "Conecta el LED al\npin indicado por el Técnico",
                    "Toma un LED de la bandeja.\nGrip + inserta ánodo en el pin.",
                    Color.clear, colorAccion);
                break;
            default:
                if (gameManager.levelCompleted)
                    Mostrar("¡Reto 4 completado!", "LED parpadea de forma segura.", Color.clear, colorCompletado);
                else
                    Mostrar(
                        "Conecta resistencia >= 100 Ohm\ny cierra el circuito a GND",
                        "LED → Resistencia → GND en la protoboard",
                        Color.clear, colorAccion);
                break;
        }
    }

    // ─────────────────────────────────────────────
    //  Notificación de entrega
    // ─────────────────────────────────────────────

    void OnComponentSent(ComponentType tipo, float valor)
    {
        string nombre = tipo switch
        {
            ComponentType.Resistor  => $"Resistencia {valor:F0}Ω",
            ComponentType.LED       => "LED",
            ComponentType.Capacitor => "Capacitor",
            _                       => "Componente"
        };

        Sprite icono = tipo switch
        {
            ComponentType.Resistor  => spriteResistor,
            ComponentType.LED       => spriteLED,
            ComponentType.Capacitor => spriteCapacitor,
            _                       => null
        };

        StartCoroutine(MostrarNotificacion($"¡El Técnico te envió:\n{nombre}!\nAgárralo con el Grip derecho.", icono));
    }

    IEnumerator MostrarNotificacion(string mensaje, Sprite icono)
    {
        _mostrandoNotificacion = true;

        if (panelNotificacion != null) panelNotificacion.SetActive(true);
        if (txtNotificacion   != null) txtNotificacion.text = mensaje;
        if (imgIconoComponente!= null && icono != null) imgIconoComponente.sprite = icono;

        yield return new WaitForSeconds(4f);

        if (panelNotificacion != null) panelNotificacion.SetActive(false);
        _mostrandoNotificacion = false;
    }

    // ─────────────────────────────────────────────
    //  Callbacks de eventos
    // ─────────────────────────────────────────────

    void OnLevelLoaded(LevelType level)
    {
        if (panelNotificacion != null) panelNotificacion.SetActive(false);
        _mostrandoNotificacion = false;
        // Nuevo reto en marcha: la insignia "reto completado" del reto anterior ya no aplica.
        if (_panelEsquina != null) _panelEsquina.SetActive(false);
    }

    void OnLevelCompleted(LevelType level, bool success)
    {
        if (success)
        {
            // Reto 4 es el reto LIBRE y final: cuando su circuito creado por ellos funciona, mensaje especial.
            string sub = level == LevelType.Arduino
                ? "¡Su circuito funciona! El LED parpadea de forma segura.\nDiseñaron y validaron su propio diseño."
                : $"Completaste el Reto {(int)level + 1}.\n¡Listo para el nuevo reto!";
            MostrarCelebracion("¡FELICIDADES!", sub, (int)level + 1);
            return;
        }

        MostrarConAutoOcultar($"Reto {(int)level + 1} — intenta mejor",
                "Revisa el procedimiento.", colorCompletado);
    }

    // ─────────────────────────────────────────────
    //  Celebración de victoria — Canvas Screen Space propio
    // ─────────────────────────────────────────────

    /// <summary>
    /// Muestra el panel central "¡FELICIDADES!" (se oculta solo tras <see cref="duracionFelicitacionCentro"/>)
    /// y activa la insignia de la esquina inferior derecha, que persiste hasta que cargue el siguiente reto.
    /// </summary>
    void MostrarCelebracion(string titulo, string sub, int numeroReto)
    {
        EnsureCelebrationCanvas();
        if (_panelCentro == null) return;   // sin Camera.main aún — no debería pasar en runtime real

        if (_txtCentroTitulo != null) _txtCentroTitulo.text = titulo;
        if (_txtCentroSub    != null) _txtCentroSub.text    = sub;
        _panelCentro.SetActive(true);
        if (_celebracionCentroCo != null) StopCoroutine(_celebracionCentroCo);
        _celebracionCentroCo = StartCoroutine(OcultarPanelCentroTrasDelay(duracionFelicitacionCentro));

        if (_txtEsquina != null) _txtEsquina.text = $"✓ Reto {numeroReto} completado";
        if (_panelEsquina != null) _panelEsquina.SetActive(true);
    }

    IEnumerator OcultarPanelCentroTrasDelay(float segundos)
    {
        yield return new WaitForSeconds(segundos);
        if (_panelCentro != null) _panelCentro.SetActive(false);
        _celebracionCentroCo = null;
    }

    /// <summary>
    /// Construye, la primera vez que se necesita, un Canvas Screen Space - Camera (enganchado a
    /// Camera.main del Explorador) con dos paneles: uno central temporal y uno de esquina persistente.
    /// Independiente del ExplorerHUD (World Space + billboard/lerp) para que la celebración quede
    /// fija en la vista, tal como se pidió, sin interferir con las instrucciones permanentes del HUD.
    /// </summary>
    void EnsureCelebrationCanvas()
    {
        if (_celebCanvasGO != null) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[PlayerFeedbackUI] Camera.main no encontrada — no se puede crear el canvas de celebración.");
            return;
        }

        _celebCanvasGO = new GameObject("ExplorerCelebrationCanvas");
        var canvas = _celebCanvasGO.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera   = cam;
        canvas.planeDistance = 1f;

        var scaler = _celebCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        _celebCanvasGO.AddComponent<GraphicRaycaster>();

        var canvasRT = _celebCanvasGO.GetComponent<RectTransform>();

        _panelCentro  = BuildPanelCentro(canvasRT);
        _panelEsquina = BuildPanelEsquina(canvasRT);
        _panelCentro.SetActive(false);
        _panelEsquina.SetActive(false);
    }

    GameObject BuildPanelCentro(RectTransform parent)
    {
        var panel = new GameObject("Panel_Centro");
        panel.transform.SetParent(parent, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(820, 260);
        rt.anchoredPosition = Vector2.zero;

        var bg = panel.AddComponent<Image>();
        bg.color = colorCompletado;

        _txtCentroTitulo = AddCelebText("Titulo", rt, new Vector2(760, 90), new Vector2(0, 55), 56);
        _txtCentroTitulo.fontStyle = FontStyles.Bold;
        _txtCentroTitulo.color     = new Color(0.55f, 1f, 0.6f);

        _txtCentroSub = AddCelebText("Sub", rt, new Vector2(760, 100), new Vector2(0, -50), 26);
        _txtCentroSub.color = new Color(0.92f, 0.95f, 0.92f);

        return panel;
    }

    GameObject BuildPanelEsquina(RectTransform parent)
    {
        var panel = new GameObject("Panel_Esquina");
        panel.transform.SetParent(parent, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(400, 70);
        rt.anchoredPosition = new Vector2(-28, 28);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(colorCompletado.r, colorCompletado.g, colorCompletado.b, 0.92f);

        _txtEsquina = AddCelebText("Txt", rt, new Vector2(360, 50), Vector2.zero, 26);
        _txtEsquina.fontStyle = FontStyles.Bold;
        _txtEsquina.color     = new Color(0.6f, 1f, 0.65f);

        return panel;
    }

    TMP_Text AddCelebText(string name, RectTransform parent, Vector2 size, Vector2 pos, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize   = fontSize;
        tmp.alignment  = TextAlignmentOptions.Center;
        tmp.richText   = true;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    /// <summary>
    /// Se completaron los 4 retos (fin de la misión). Felicitación final destacada.
    /// </summary>
    void OnGameCompleted()
    {
        MostrarConAutoOcultar("¡MISIÓN CUMPLIDA!",
                "Completaron los 4 retos en equipo. ¡Excelente trabajo, técnico y explorador!",
                colorCompletado);
    }

    // ─────────────────────────────────────────────
    //  Panel de mensaje: aparece solo al completar/perder un reto, se oculta solo a los 5s
    // ─────────────────────────────────────────────
    Coroutine _ocultarMensajeCo;

    void MostrarConAutoOcultar(string instruccion, string sub, Color fondo)
    {
        Mostrar(instruccion, sub, Color.clear, fondo);
        if (panelMensaje != null) panelMensaje.SetActive(true);
        if (_ocultarMensajeCo != null) StopCoroutine(_ocultarMensajeCo);
        _ocultarMensajeCo = StartCoroutine(OcultarMensajeTrasDelay(5f));
    }

    IEnumerator OcultarMensajeTrasDelay(float segundos)
    {
        yield return new WaitForSeconds(segundos);
        if (panelMensaje != null) panelMensaje.SetActive(false);
        _ocultarMensajeCo = null;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    void Mostrar(string instruccion, string sub, Color _, Color fondo)
    {
        if (txtInstruccion    != null) txtInstruccion.text    = instruccion;
        if (txtSubInstruccion != null) txtSubInstruccion.text = sub;
        if (txtPaso           != null) txtPaso.text           =
            $"Paso {instructionSystem.currentStep + 1} de {_totalPasos}";
        if (fondoPanel        != null && fondo != Color.clear) fondoPanel.color = fondo;
    }

    void ActualizarProgreso()
    {
        if (barraProgreso == null || instructionSystem == null) return;
        float t = _totalPasos > 0 ? (float)instructionSystem.currentStep / _totalPasos : 0f;
        barraProgreso.fillAmount = Mathf.Clamp01(t);
        if (txtProgresoPorcentaje != null)
            txtProgresoPorcentaje.text = $"{Mathf.RoundToInt(t * 100)}%";
    }
}