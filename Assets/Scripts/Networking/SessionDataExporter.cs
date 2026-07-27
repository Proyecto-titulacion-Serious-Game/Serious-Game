using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Recolecta los datos de la sesión de juego y los expone de forma
/// thread-safe para que DashboardServer los sirva al navegador del docente.
/// También escribe un JSON en Application.persistentDataPath al finalizar.
///
/// SETUP: Añadir al mismo GO que DashboardServer (ej. NetworkManager).
/// </summary>
public class SessionDataExporter : MonoBehaviour
{
    public static SessionDataExporter Instance { get; private set; }

    [Tooltip("Etiqueta del grupo/PC (usada en el payload de Supabase). Vacío = nombre del equipo (SystemInfo.deviceName).")]
    public string grupo        = "";

    private readonly object      _lock = new object();
    private SessionExportData    _data = new SessionExportData();
    private SessionHistory       _history = new SessionHistory();
    private SessionLiveData      _live = new SessionLiveData();

    // Cache de JSON serializado en el HILO PRINCIPAL. JsonUtility NO se puede llamar desde el hilo
    // HTTP (lanza excepción) → el servidor sirve estas cadenas ya hechas, no serializa en su hilo.
    private string _liveJson     = "{}";
    private string _resultsJson  = "{}";
    private string _sessionsJson = "{\"sessions\":[]}";

    // Refresco en vivo (hilo principal); el servidor HTTP solo lee el snapshot bajo lock.
    private PerformanceTracker   _tracker;
    private GameManager          _gm;
    private float                _nextLiveRefresh;

    const string HISTORY_FILE = "sessions_history.json";

    // ─────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadHistory();
    }

    void OnEnable()
    {
        ObjectiveSystem.OnSessionEnded += HandleSessionEnded;
        GameManager.OnLevelLoaded      += HandleLevelLoaded;
        GameManager.OnLevelCompleted   += HandleLevelCompletedForSupabase;
    }

    void OnDisable()
    {
        ObjectiveSystem.OnSessionEnded -= HandleSessionEnded;
        GameManager.OnLevelLoaded      -= HandleLevelLoaded;
        GameManager.OnLevelCompleted   -= HandleLevelCompletedForSupabase;
    }

    // ─────────────────────────────────────────────
    //  API pública (thread-safe)
    // ─────────────────────────────────────────────

    public SessionExportData GetSnapshot()
    {
        lock (_lock) { return _data; }
    }

    // JSON ya serializado en el hilo principal (lo sirve el DashboardServer desde su hilo HTTP).
    public string GetLiveJson()     { lock (_lock) { return _liveJson;     } }
    public string GetResultsJson()  { lock (_lock) { return _resultsJson;  } }
    public string GetSessionsJson() { lock (_lock) { return _sessionsJson; } }

    /// <summary>Historial (lista) de todas las sesiones finalizadas — para el dashboard.</summary>
    public SessionHistory GetHistorySnapshot()
    {
        lock (_lock) { return _history; }
    }

    /// <summary>Estado EN VIVO de la sesión en curso — para el panel docente (ambos roles).</summary>
    public SessionLiveData GetLiveSnapshot()
    {
        lock (_lock) { return _live; }
    }

    // ─────────────────────────────────────────────
    //  Refresco en vivo (hilo principal de Unity)
    // ─────────────────────────────────────────────
    void Update()
    {
        // Flush de borrados pedidos desde el hilo HTTP del dashboard (JsonUtility solo aquí).
        if (_pendingSaveHistory)
        {
            _pendingSaveHistory = false;
            SaveHistory();
            if (_pendingClearResults) { _pendingClearResults = false; SaveToDisk(); }
        }

        if (Time.unscaledTime < _nextLiveRefresh) return;
        _nextLiveRefresh = Time.unscaledTime + 0.5f;
        RefreshLive();
    }

    void RefreshLive()
    {
        if (_tracker == null) _tracker = FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
        if (_gm == null)      _gm      = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);

        var live = new SessionLiveData { active = _gm != null };

        if (_gm != null)
            live.currentReto = LevelName(_gm.currentLevel);

        if (_tracker != null)
        {
            live.currentTimeSeconds = _tracker.GetTime();
            live.currentErrors      = _tracker.GetErrors();
            live.currentErrorTypes  = _tracker.GetErrorBreakdown();
            live.currentErrorDetails = _tracker.GetErrorDetails();

            var recs = _tracker.GetAllRecords();
            var dto  = new LevelRecordDto[recs.Count];
            for (int i = 0; i < recs.Count; i++) dto[i] = new LevelRecordDto(recs[i]);
            live.completedRecords = dto;
            live.retosCompletados = recs.Count;
        }

        var gs = GameSession.Instance;
        live.exploradorConectado = gs != null && gs.ExploradorListo;
        live.tecnicoConectado    = gs != null;   // el Host instancia GameSession

        lock (_lock)
        {
            live.state = _data.state;
            _live = live;
            // Serializar AQUÍ (hilo principal) y cachear para el servidor HTTP.
            _liveJson     = JsonUtility.ToJson(_live);
            _resultsJson  = JsonUtility.ToJson(_data);
            _sessionsJson = JsonUtility.ToJson(_history);
        }
    }

    public void SetAccessCode(string code)
    {
        lock (_lock) { _data.accessCode = code; }
    }

    // ─────────────────────────────────────────────
    //  Borrado de datos (docente, vía dashboard)
    // ─────────────────────────────────────────────
    // El hilo HTTP del DashboardServer solo puede MARCAR la petición: JsonUtility (que usa
    // SaveHistory/SaveToDisk) revienta fuera del hilo principal. Update() hace el flush.
    volatile bool _pendingSaveHistory;
    volatile bool _pendingClearResults;

    /// <summary>Borra UNA sesión del historial por su timestamp exacto (el id visible en la tabla).
    /// Thread-safe (llamable desde el hilo HTTP). Devuelve true si algo se borró.</summary>
    public bool BorrarSesion(string timestamp)
    {
        if (string.IsNullOrEmpty(timestamp)) return false;
        bool borrado;
        lock (_lock)
        {
            var lista = new List<SessionSummaryDto>(_history.sessions);
            borrado = lista.RemoveAll(s => s != null && s.timestamp == timestamp) > 0;
            if (borrado) _history.sessions = lista.ToArray();
        }
        if (borrado)
        {
            _pendingSaveHistory = true;
            Debug.Log($"[SessionDataExporter] Sesión '{timestamp}' borrada del historial (pedido del dashboard).");
        }
        return borrado;
    }

    /// <summary>Borra TODO el historial y el resultado de la última sesión. Thread-safe.</summary>
    public void BorrarTodoElHistorial()
    {
        lock (_lock)
        {
            _history.sessions = Array.Empty<SessionSummaryDto>();
            _data.hasResult   = false;
            _data.records     = Array.Empty<LevelRecordDto>();
            _data.state       = "En espera";
        }
        _pendingSaveHistory  = true;
        _pendingClearResults = true;
        Debug.Log("[SessionDataExporter] Historial COMPLETO borrado (pedido del dashboard).");
    }

    // ─────────────────────────────────────────────
    //  Handlers
    // ─────────────────────────────────────────────

    void HandleLevelLoaded(LevelType level)
    {
        lock (_lock)
        {
            _data.currentReto = LevelName(level);
            _data.state       = "En progreso";
        }
    }

    void HandleSessionEnded(SessionResult result)
    {
        var tracker = FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
        var records = tracker != null ? tracker.GetAllRecords() : new List<LevelRecord>();

        // 1. Guardado local tradicional (mantiene compatibilidad con tu sistema actual)
        var serialized = new LevelRecordDto[records.Count];
        for (int i = 0; i < records.Count; i++)
            serialized[i] = new LevelRecordDto(records[i]);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        lock (_lock)
        {
            _data.hasResult    = true;
            _data.state        = "Sesión finalizada";
            _data.summary      = new SessionResultDto(result);
            _data.records      = serialized;
            _data.timestamp    = stamp;

            var lista = new List<SessionSummaryDto>(_history.sessions)
            {
                new SessionSummaryDto(result, stamp, _data.accessCode, serialized)
            };
            _history.sessions = lista.ToArray();
        }

        SaveToDisk();
        SaveHistory();

        // El envío a Supabase YA se hizo reto por reto en HandleLevelCompletedForSupabase, apenas
        // cada uno terminó — no se re-envía aquí (evita filas duplicadas). Ver esa función para el
        // motivo: antes esto solo pasaba una vez, al FINAL de los 4 retos, así que si la sesión se
        // cortaba antes (p.ej. el Reto 4 nunca cerraba) se perdían TAMBIÉN los retos 1-3 que sí se
        // habían completado bien — un reto perdido no debería tirar a los demás.
    }

    /// <summary>
    /// Envía a Supabase el registro de UN reto apenas termina (éxito o fallo), en vez de esperar a
    /// que los 4 hayan terminado. Antes el envío completo vivía en HandleSessionEnded (una sola vez
    /// al final): si el Reto 4 nunca llegaba a completarse (p.ej. el riel GND del protoboard no
    /// cerraba el circuito), los retos 1-3 —ya jugados y evaluados bien— tampoco llegaban a la base
    /// de datos, porque todo el envío dependía de un evento que solo dispara al cerrar la sesión
    /// COMPLETA. Ahora cada reto se sube en cuanto su propio registro existe.
    /// </summary>
    void HandleLevelCompletedForSupabase(LevelType level, bool success)
    {
        // BUG REAL 2026-07-25 (reportado: "la telemetría no llega a la base de datos al completar
        // cada reto por separado" — reproducible en TODOS los retos, no intermitente): este handler
        // y PerformanceTracker.HandleLevelCompleted están suscritos AMBOS a GameManager.OnLevelCompleted,
        // y C# invoca un evento multicast en el orden de suscripción. SessionDataExporter se crea
        // dinámicamente en DashboardBootstrap vía RuntimeInitializeOnLoadMethod(AfterSceneLoad) — su
        // OnEnable (donde se suscribe) corre DURANTE esa fase, que la documentación de Unity ubica
        // justo después de Awake+OnEnable de los objetos de escena pero ANTES de que corra el Start()
        // de ningún MonoBehaviour de escena. PerformanceTracker se suscribe en su propio Start() (no
        // OnEnable) — así que, en cualquier partida real, SessionDataExporter queda suscrito ANTES
        // que PerformanceTracker. Resultado: cada vez que GameManager dispara OnLevelCompleted, ESTE
        // método corría primero y el registro que busca en PerformanceTracker.GetAllRecords() todavía
        // no existía (lo agrega PerformanceTracker.HandleLevelCompleted, que corre después, en el
        // MISMO despacho síncrono del evento) → "no encontré el registro... se omite este envío" en
        // TODOS los retos, siempre, sin importar la red/Supabase.
        //
        // Fix: no depender del orden de suscripción de un evento compartido. Diferir la búsqueda del
        // registro un frame (o unos pocos, por margen) con una coroutine — para entonces el despacho
        // síncrono original del evento (con TODOS sus suscriptores, incluido PerformanceTracker) ya
        // terminó hace rato, así que el registro siempre está ahí.
        StartCoroutine(BuscarYEnviarRetoDiferido(level));
    }

    IEnumerator BuscarYEnviarRetoDiferido(LevelType level)
    {
        const int MAX_FRAMES_ESPERA = 5; // margen de sobra: 1 frame ya alcanza en el caso normal

        for (int intento = 0; intento < MAX_FRAMES_ESPERA; intento++)
        {
            yield return null; // esperar al siguiente frame

            var tracker = FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
            if (tracker == null) continue;

            var records = tracker.GetAllRecords();
            LevelRecord? rec = null;
            for (int i = records.Count - 1; i >= 0; i--)
                if (records[i].level == level) { rec = records[i]; break; }

            if (rec != null)
            {
                EnviarUnRetoASupabase(rec.Value);
                yield break;
            }
        }

        Debug.LogWarning($"[SessionDataExporter] HandleLevelCompletedForSupabase: no encontré el " +
                          $"registro de {LevelName(level)} en PerformanceTracker tras {MAX_FRAMES_ESPERA} " +
                          "frames de espera — se omite este envío (¿PerformanceTracker no está en la escena?).");
    }

    /// <summary>Construye el payload de UN reto y lo envía a Supabase (si AnalyticsManager está disponible).</summary>
    void EnviarUnRetoASupabase(LevelRecord rec)
    {
        if (AnalyticsManager.Instance == null)
        {
            Debug.LogWarning($"[SessionDataExporter] No se pudo enviar {LevelName(rec.level)} a Supabase: " +
                              "AnalyticsManager.Instance es null (¿no se instanció en DashboardBootstrap?).");
            return;
        }

        // Extraer contadores de errores del breakdown del tracker
        int cortocircuitos = 0;
        int sobrecorriente = 0;
        int polaridadInvertida = 0;

        if (rec.errorTypes != null)
        {
            foreach (var tag in rec.errorTypes)
            {
                string tipoLower = (tag.tipo ?? "").ToLower();
                if (tipoLower.Contains("corto")) cortocircuitos += tag.count;
                else if (tipoLower.Contains("potencia") || tipoLower.Contains("sobrecarga") || tipoLower.Contains("watt")) sobrecorriente += tag.count;
                else if (tipoLower.Contains("polaridad") || tipoLower.Contains("invertida")) polaridadInvertida += tag.count;
            }
        }

        // Mapear el nivel de Unity al ID entero del reto (1 al 4)
        string nombreReto = LevelName(rec.level);
        int retoIdInt = nombreReto.Contains("1") ? 1 :
                        nombreReto.Contains("2") ? 2 :
                        nombreReto.Contains("3") ? 3 : 4;

        // Crear el Payload exacto que pide la tabla 'telemetria_estudiantes'.
        // sesion_id / nombre_clase: vienen de AnalyticsManager.idSesionActual/nombreClaseActual,
        // resueltos por ValidarCodigoSesion contra sesiones_config (ver RoomCodeEntryUI.CrearSala).
        // Si el docente no escribió un código que coincida con ninguna clase creada, nombreClaseActual
        // ya trae su propio default ("Modo Práctica Libre") — antes este campo iba quemado como
        // "[CLASE DE PRUEBA]" sin importar si se había validado una sesión real o no.
        var payloadSupabase = new AnalyticsManager.TelemetriaPayload
        {
            sesion_id = AnalyticsManager.Instance.idSesionActual,
            nombre_clase = AnalyticsManager.Instance.nombreClaseActual,
            grupo_estudiantes = string.IsNullOrEmpty(grupo) ? SystemInfo.deviceName : grupo,
            reto_id = retoIdInt,

            tiempo_resolucion_seg = Mathf.RoundToInt(rec.timeSeconds),
            completado = rec.success,
            nota_autograder = rec.nota,

            cant_cortocircuitos = cortocircuitos,
            cant_sobrecorriente = sobrecorriente,
            cant_polaridad_invertida = polaridadInvertida,

            fallos_compilacion_ide = 0,
            desconexiones_logica_fisica = 0,
            rechazos_componentes = rec.errors
        };

        AnalyticsManager.Instance.EnviarMetricas(payloadSupabase);
        Debug.Log($"[SessionDataExporter] {nombreReto} enviado a Supabase " +
                  $"(nota={rec.nota}, tiempo={rec.timeSeconds:F0}s, exito={rec.success}).");
    }

    // ─────────────────────────────────────────────

    void SaveToDisk()
    {
        try
        {
            SessionExportData snapshot;
            lock (_lock) { snapshot = _data; }

            string json = JsonUtility.ToJson(snapshot, prettyPrint: true);
            string path = Path.Combine(Application.persistentDataPath, "session_results.json");
            File.WriteAllText(path, json);
            Debug.Log($"[SessionDataExporter] Guardado en: {path}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionDataExporter] Error al guardar: {e.Message}");
        }
    }

    void LoadHistory()
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, HISTORY_FILE);
            if (!File.Exists(path)) return;
            var loaded = JsonUtility.FromJson<SessionHistory>(File.ReadAllText(path));
            if (loaded != null && loaded.sessions != null) _history = loaded;
            Debug.Log($"[SessionDataExporter] Historial cargado: {_history.sessions.Length} sesiones.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionDataExporter] No se pudo cargar el historial: {e.Message}");
        }
    }

    void SaveHistory()
    {
        try
        {
            SessionHistory snapshot;
            lock (_lock) { snapshot = _history; }

            string json = JsonUtility.ToJson(snapshot, prettyPrint: true);
            string path = Path.Combine(Application.persistentDataPath, HISTORY_FILE);
            File.WriteAllText(path, json);
            Debug.Log($"[SessionDataExporter] Historial guardado ({snapshot.sessions.Length} sesiones) en: {path}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionDataExporter] Error al guardar el historial: {e.Message}");
        }
    }

    public static string LevelName(LevelType level) => level switch
    {
        LevelType.OhmLaw   => "Reto 1 — Ley de Ohm",
        LevelType.Parallel => "Reto 2 — Paralelo",
        LevelType.Mixed    => "Reto 3 — Mixto",
        LevelType.Arduino  => "Reto 4 — Arduino",
        _                  => level.ToString()
    };
}

// ─────────────────────────────────────────────────────────
//  DTOs serializables por JsonUtility
// ─────────────────────────────────────────────────────────

[Serializable]
public class SessionExportData
{
    public bool              hasResult   = false;
    public string            currentReto = "Sin iniciar";
    public string            state       = "En espera";
    public string            timestamp   = "";
    public string            accessCode  = "----";
    public SessionResultDto  summary     = new SessionResultDto();
    public LevelRecordDto[]  records     = Array.Empty<LevelRecordDto>();
}

[Serializable]
public class SessionResultDto
{
    public int    totalScore;
    public int    maxScore;
    public float  scorePercent;
    public int    totalErrors;
    public float  totalTimeSeconds;
    public string evaluation = "";

    public SessionResultDto() { }
    public SessionResultDto(SessionResult r)
    {
        totalScore       = r.totalScore;
        maxScore         = r.maxScore;
        scorePercent     = r.scorePercent;
        totalErrors      = r.totalErrors;
        totalTimeSeconds = r.totalTimeSeconds;
        evaluation       = r.evaluation;
    }
}

[Serializable]
public class LevelRecordDto
{
    public string         levelName  = "";
    public float          timeSeconds;
    public int            errors;
    public bool           success;
    public string         evaluation = "";
    public ErrorTagCount[] errorTypes = Array.Empty<ErrorTagCount>();
    /// <summary>Mensajes descriptivos de cada error ("no llega a GND", "R muy baja → corto"...).</summary>
    public string[]       detalles   = Array.Empty<string>();
    /// <summary>Nota 0–10 del reto (5 pts tiempo + 5 pts errores).</summary>
    public float          nota;

    public LevelRecordDto() { }
    public LevelRecordDto(LevelRecord r)
    {
        levelName   = SessionDataExporter.LevelName(r.level);
        timeSeconds = r.timeSeconds;
        errors      = r.errors;
        success     = r.success;
        evaluation  = r.evaluation;
        errorTypes  = r.errorTypes ?? Array.Empty<ErrorTagCount>();
        detalles    = r.detalles   ?? Array.Empty<string>();
        nota        = r.nota;
    }
}

// ─────────────────────────────────────────────────────────
//  Datos EN VIVO de la sesión en curso (para el panel docente)
// ─────────────────────────────────────────────────────────

[Serializable]
public class SessionLiveData
{
    public bool             active;                 // hay una partida en escena
    public string           state               = "En espera";
    public string           currentReto         = "Sin iniciar";
    public float            currentTimeSeconds;     // tiempo en el reto en curso
    public int              currentErrors;          // errores en el reto en curso
    public ErrorTagCount[]  currentErrorTypes   = Array.Empty<ErrorTagCount>();
    public string[]         currentErrorDetails = Array.Empty<string>();   // "qué pasó" de cada error
    public int              retosCompletados;
    public LevelRecordDto[] completedRecords    = Array.Empty<LevelRecordDto>();
    public bool             exploradorConectado;
    public bool             tecnicoConectado;
}

// ─────────────────────────────────────────────────────────
//  Historial de sesiones (lista para el dashboard)
// ─────────────────────────────────────────────────────────

[Serializable]
public class SessionHistory
{
    public SessionSummaryDto[] sessions = Array.Empty<SessionSummaryDto>();
}

[Serializable]
public class SessionSummaryDto
{
    public string timestamp  = "";
    public string accessCode = "----";
    public string evaluation = "";
    public int    totalScore;
    public int    maxScore;
    public float  scorePercent;
    public int    totalErrors;
    public float  totalTimeSeconds;
    public LevelRecordDto[] records = Array.Empty<LevelRecordDto>();   // registros por reto de esta sesión

    public SessionSummaryDto() { }
    public SessionSummaryDto(SessionResult r, string stamp, string code, LevelRecordDto[] recs = null)
    {
        timestamp        = stamp;
        accessCode       = code;
        evaluation       = r.evaluation;
        totalScore       = r.totalScore;
        maxScore         = r.maxScore;
        scorePercent     = r.scorePercent;
        totalErrors      = r.totalErrors;
        totalTimeSeconds = r.totalTimeSeconds;
        records          = recs ?? Array.Empty<LevelRecordDto>();
    }
}
