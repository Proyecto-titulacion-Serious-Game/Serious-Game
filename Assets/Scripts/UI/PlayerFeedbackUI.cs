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

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private float _updateTimer;
    private const float INTERVAL = 0.15f;
    private int   _totalPasos = 4;
    private bool  _mostrandoNotificacion = false;

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
                Mostrar(
                    "Gira el capacitor 180°\npara corregir la polaridad",
                    "Botón B (mano derecha)\npara rotar el componente",
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
    }

    void OnLevelCompleted(LevelType level, bool success)
    {
        // Reto 4 es el reto LIBRE y final: cuando su circuito creado por ellos funciona, mensaje especial.
        if (success && level == LevelType.Arduino)
        {
            Mostrar("¡FELICIDADES!",
                    "¡Su circuito funciona! El LED parpadea de forma segura.\nDiseñaron y validaron su propio diseño.",
                    Color.clear, colorCompletado);
            return;
        }

        if (success)
        {
            Mostrar("¡FELICIDADES!",
                    $"Completaste el Reto {(int)level + 1}.\n¡Listo para el nuevo reto!",
                    Color.clear, colorCompletado);
        }
        else
        {
            Mostrar($"Reto {(int)level + 1} — intenta mejor",
                    "Revisa el procedimiento.", Color.clear, colorCompletado);
        }
    }

    /// <summary>
    /// Se completaron los 4 retos (fin de la misión). Felicitación final destacada.
    /// </summary>
    void OnGameCompleted()
    {
        Mostrar("¡MISIÓN CUMPLIDA!",
                "Completaron los 4 retos en equipo. ¡Excelente trabajo, técnico y explorador!",
                Color.clear, colorCompletado);
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