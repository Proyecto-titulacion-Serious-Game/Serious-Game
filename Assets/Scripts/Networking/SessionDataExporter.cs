using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

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

    [Header("Subida a Google Sheets (opcional, vía Apps Script)")]
    [Tooltip("Si está activo, al terminar la sesión hace POST al webhook y agrega filas a la Sheet.")]
    public bool   subirASheets = false;
    [Tooltip("URL /exec del Web App de Apps Script.")]
    public string webhookUrl   = "";
    [Tooltip("Token compartido (debe coincidir con TOKEN en el Apps Script).")]
    public string sheetsToken  = "TITA-2026-cambia-esto";
    [Tooltip("Etiqueta del grupo/PC. Vacío = nombre del equipo (SystemInfo.deviceName).")]
    public string grupo        = "";

    /// <summary>True mientras hay una subida a Google Sheets en curso. Lo usa PauseMenu para esperar
    /// a que termine antes de cerrar el juego al pulsar "Salir".</summary>
    public bool SubidaEnCurso { get; private set; }

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
    }

    void OnDisable()
    {
        ObjectiveSystem.OnSessionEnded -= HandleSessionEnded;
        GameManager.OnLevelLoaded      -= HandleLevelLoaded;
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
        // Tecla de PRUEBA: Ctrl+F8 sube una fila de test a Google Sheets (verificar la conexión sin
        // jugar). Requiere Ctrl porque F8 sola ya la usan TecnicoValidarPrecaucion (re-verificar el
        // reto online) y SoloTechnicianDebug (offline) — sin el modificador, cada F8 del Técnico
        // metía además una fila [PRUEBA] en la hoja del docente.
        var kb = Keyboard.current;
        if (kb != null && kb.f8Key.wasPressedThisFrame &&
            (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
            StartCoroutine(SubirPrueba());

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

            // Añadir esta sesión al HISTORIAL (lista de todas las sesiones), con sus registros por reto.
            var lista = new List<SessionSummaryDto>(_history.sessions)
            {
                new SessionSummaryDto(result, stamp, _data.accessCode, serialized)
            };
            _history.sessions = lista.ToArray();
        }

        SaveToDisk();
        SaveHistory();

        // Sink opcional a la nube: además del respaldo local, sube la sesión a Google Sheets.
        if (subirASheets && !string.IsNullOrEmpty(webhookUrl))
        {
            SubidaEnCurso = true;
            StartCoroutine(SubirASheets(result, serialized, stamp));
        }
    }

    // ─────────────────────────────────────────────
    //  Subida a Google Sheets (webhook de Apps Script)
    // ─────────────────────────────────────────────
    [Serializable] class SheetsSesion { public string fecha, grupo, codigo, evaluacion;
                                        public int score, scoreMax, porcentaje, tiempo, errores; }
    [Serializable] class SheetsReto   { public string reto, evaluacion, tipos;
                                        public int tiempo, errores, exito; }
    [Serializable] class SheetsPayload{ public string token; public SheetsSesion session; public SheetsReto[] records; }

    IEnumerator SubirASheets(SessionResult r, LevelRecordDto[] recs, string stamp)
    {
        string codigo;
        lock (_lock) { codigo = _data.accessCode; }
        string grp = string.IsNullOrEmpty(grupo) ? SystemInfo.deviceName : grupo;

        var payload = new SheetsPayload
        {
            token   = sheetsToken,
            session = new SheetsSesion
            {
                fecha = stamp, grupo = grp, codigo = codigo, evaluacion = r.evaluation,
                score = r.totalScore, scoreMax = r.maxScore,
                porcentaje = Mathf.RoundToInt(r.scorePercent * 100f),
                tiempo = Mathf.RoundToInt(r.totalTimeSeconds), errores = r.totalErrors
            },
            records = Array.ConvertAll(recs ?? Array.Empty<LevelRecordDto>(), x => new SheetsReto
            {
                reto = x.levelName, tiempo = Mathf.RoundToInt(x.timeSeconds),
                errores = x.errors, exito = x.success ? 1 : 0, evaluacion = x.evaluation,
                tipos = TiposInline(x.errorTypes)
            })
        };

        yield return PostPayload(payload);
    }

    /// <summary>PRUEBA: sube una fila de test a la hoja al instante (tecla F8 en Play), para verificar
    /// la conexión a Google Sheets SIN tener que completar una sesión real.</summary>
    public IEnumerator SubirPrueba()
    {
        var payload = new SheetsPayload
        {
            token   = sheetsToken,
            session = new SheetsSesion
            {
                fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                grupo = string.IsNullOrEmpty(grupo) ? SystemInfo.deviceName : grupo,
                codigo = "TEST", evaluacion = "[PRUEBA]",
                score = 100, scoreMax = 100, porcentaje = 100, tiempo = 0, errores = 0
            },
            records = new[]
            {
                new SheetsReto { reto = "Reto 1 — PRUEBA", tiempo = 42, errores = 1, exito = 1,
                                 evaluacion = "[PRUEBA]", tipos = "Cortocircuito:1" }
            }
        };
        Debug.Log("[SessionDataExporter] F8: enviando fila de PRUEBA a Google Sheets...");
        yield return PostPayload(payload);
    }

    IEnumerator PostPayload(SheetsPayload payload)
    {
        if (string.IsNullOrEmpty(webhookUrl))
        {
            Debug.LogWarning("[SessionDataExporter] webhookUrl vacío: configura SHEETS_URL en DashboardBootstrap.");
            yield break;
        }
        byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using (var req = new UnityWebRequest(webhookUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.redirectLimit = 5;    // Apps Script responde con 302 → hay que seguir el redirect
            req.timeout       = 15;
            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            if (ok) Debug.Log($"[SessionDataExporter] Subido a Google Sheets: {req.downloadHandler.text}");
            else    Debug.LogWarning($"[SessionDataExporter] No se pudo subir a Sheets: {req.error}. " +
                                     "El respaldo local (JSON/CSV) está intacto.");
        }
        SubidaEnCurso = false;
    }

    // "cortocircuito:2;polaridad:1" — desglose de errores en una celda.
    static string TiposInline(ErrorTagCount[] arr)
    {
        if (arr == null || arr.Length == 0) return "";
        var parts = new string[arr.Length];
        for (int i = 0; i < arr.Length; i++) parts[i] = arr[i].tipo + ":" + arr[i].count;
        return string.Join(";", parts);
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

    public LevelRecordDto() { }
    public LevelRecordDto(LevelRecord r)
    {
        levelName   = SessionDataExporter.LevelName(r.level);
        timeSeconds = r.timeSeconds;
        errors      = r.errors;
        success     = r.success;
        evaluation  = r.evaluation;
        errorTypes  = r.errorTypes ?? Array.Empty<ErrorTagCount>();
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
