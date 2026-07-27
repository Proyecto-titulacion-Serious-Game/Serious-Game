using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Actualiza el HUD de pantalla del Técnico suscribiéndose a eventos de GameManager.
/// Se añade al root del prefab TechnicianHUD.
/// Referencias externas a asignar en el Inspector: gameManager.
/// </summary>
public class TechnicianHUDController : MonoBehaviour
{
    [Header("Referencias")]
    public GameManager gameManager;

    [Header("Info — panel superior")]
    public TMP_Text txtReto;
    public TMP_Text txtTimer;
    public TMP_Text txtErrores;

    [Header("Panel de transición de zona")]
    public GameObject panelTransicion;
    public TMP_Text   txtTransicionTitulo;
    public TMP_Text   txtTransicionSub;

    [Header("Overlay de validación")]
    public GameObject panelValidacion;
    public Image      imgValidacionBg;
    public TMP_Text   txtValidacionEstado;
    public Image      progressFill;
    public GameObject panelChecklist;
    public TMP_Text   txtCheck1;
    public TMP_Text   txtCheck2;
    public TMP_Text   txtCheck3;
    public Button     btnCerrarValidacion;

    // ─────────────────────────────────────────────
    void OnEnable()
    {
        GameManager.OnLevelLoaded         += OnLevelLoaded;
        GameManager.OnTimerTick           += OnTimerTick;
        GameManager.OnTimerExpired        += OnTimerExpired;
        GameManager.OnZoneTransitionStart += OnZoneTransitionStart;
        GameManager.OnZoneActivated       += OnZoneActivated;
        ObjectiveSystem.OnSessionEnded    += OnSessionEnded;
    }

    void OnDisable()
    {
        GameManager.OnLevelLoaded         -= OnLevelLoaded;
        GameManager.OnTimerTick           -= OnTimerTick;
        GameManager.OnTimerExpired        -= OnTimerExpired;
        GameManager.OnZoneTransitionStart -= OnZoneTransitionStart;
        GameManager.OnZoneActivated       -= OnZoneActivated;
        ObjectiveSystem.OnSessionEnded    -= OnSessionEnded;
    }

    void Start()
    {
        if (panelTransicion != null) panelTransicion.SetActive(false);
    }

    void Update()
    {
        if (gameManager == null || txtErrores == null) return;
        txtErrores.text = $"Errores: {gameManager.GetWrongAttempts()}";
    }

    // ─────────────────────────────────────────────
    void OnLevelLoaded(LevelType level)
    {
        if (txtReto != null)
            txtReto.text = level switch
            {
                LevelType.OhmLaw   => "RETO 1 — Ley de Ohm",
                LevelType.Parallel => "RETO 2 — Paralelo",
                LevelType.Mixed    => "RETO 3 — Mixto",
                LevelType.Arduino  => "RETO 4 — Arduino",
                _                  => "RETO —"
            };

        if (panelTransicion != null) panelTransicion.SetActive(false);
    }

    void OnTimerTick(float remaining)
    {
        if (txtTimer == null) return;
        int min = Mathf.FloorToInt(remaining / 60f);
        int sec = Mathf.FloorToInt(remaining % 60f);
        txtTimer.text  = $"{min}:{sec:00}";
        txtTimer.color = remaining < 60f ? new Color(1f, 0.3f, 0.3f) : Color.white;
    }

    /// <summary>
    /// El tiempo del reto es de REFERENCIA: desde 2026-07-26 <c>GameManager</c> ya NO llama
    /// <c>CompleteLevel(false)</c> al agotarse, así que el reto sigue jugable y solo baja la nota.
    /// El HUD lo dice en ámbar ("sigan"), no en rojo de fracaso: antes esto quedaba en "0:00" rojo
    /// justo cuando el reto se cerraba solo, y el equipo lo leía como "perdimos".
    /// </summary>
    void OnTimerExpired(LevelType _)
    {
        if (txtTimer != null)
        {
            txtTimer.text  = "0:00 EXTRA";
            txtTimer.color = new Color(1f, 0.7f, 0.2f);   // ámbar: aviso, no fracaso
        }
        if (txtTransicionSub != null && panelTransicion != null && panelTransicion.activeSelf)
            txtTransicionSub.text = "Tiempo de referencia agotado — pueden seguir; solo baja la nota.";
    }

    void OnZoneTransitionStart(LevelType level, bool success)
    {
        if (panelTransicion == null) return;
        panelTransicion.SetActive(true);

        int  num     = (int)level + 1;
        bool esReto4 = level == LevelType.Arduino;   // reto libre / final

        if (txtTransicionTitulo != null)
            txtTransicionTitulo.text = !success
                ? $"RETO {num} — Tiempo agotado"
                : esReto4 ? "¡FELICIDADES!"
                          : $"RETO {num} COMPLETADO";

        if (txtTransicionSub != null)
            txtTransicionSub.text = !success
                ? "Revisa el procedimiento."
                : esReto4 ? "¡Su circuito funciona! Diseñaron y validaron su propio diseño."
                          : "Cargando siguiente zona...";
    }

    void OnZoneActivated(int index)
    {
        // El panel de transición se oculta cuando el nuevo nivel ya cargó (OnLevelLoaded)
    }

    /// <summary>
    /// Fin de la sesión (los 4 retos, o corte por tiempo/salida manual): pantalla final en el
    /// Técnico con el resultado REAL, no un "misión cumplida" fijo. Antes se colgaba de
    /// GameManager.OnGameCompleted (sin datos) y SIEMPRE mostraba el mismo texto de victoria,
    /// incluso cuando un reto terminó por timeout (bug real reportado: el Reto 4 "desaparecía"
    /// por timeout y ni el Técnico ni el Explorador veían un cierre claro de qué pasó).
    /// </summary>
    void OnSessionEnded(SessionResult result)
    {
        if (panelTransicion != null) panelTransicion.SetActive(true);
        bool exito = result.evaluation.StartsWith("[EXCELENTE]") || result.evaluation.StartsWith("[BUENO]");
        if (txtTransicionTitulo != null)
            txtTransicionTitulo.text = exito ? "¡MISIÓN CUMPLIDA!" : "SESIÓN TERMINADA";
        if (txtTransicionSub != null)
            txtTransicionSub.text = $"{result.evaluation} — {Mathf.RoundToInt(result.scorePercent * 100f)}% del puntaje total.";
    }
}
