using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registra el desempeño del jugador por reto:
/// tiempo empleado, errores cometidos, evaluación final.
/// Se suscribe a eventos de GameManager para registrar automáticamente.
/// </summary>
public class PerformanceTracker : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Umbrales de evaluación")]
    [Tooltip("Tiempo máximo (segundos) para calificación Excelente por reto.")]
    public float[] excellentTimeLimits = { 240f, 300f, 360f, 450f };  // 4,5,6,7.5 min
    public int     maxErrorsForGood    = 3;

    // ─────────────────────────────────────────────
    //  Registro de sesión
    // ─────────────────────────────────────────────
    [Header("Sesión actual (solo lectura)")]
    [SerializeField] private int   _currentErrors = 0;
    [SerializeField] private float _startTime;
    [SerializeField] private int   _currentLevelIndex = 0;

    private List<LevelRecord> _records = new List<LevelRecord>();

    // Desglose de errores por tipo (categoría) del reto en curso. Se reinicia por reto.
    private readonly Dictionary<string, int> _errorsByType = new Dictionary<string, int>();

    // Mensajes DESCRIPTIVOS de cada error del reto en curso ("no llega a GND", "resistencia muy
    // baja → cortocircuito"...). Alimentan la columna "Qué pasó" del dashboard docente.
    private readonly List<string> _errorDetails = new List<string>();
    const int MAX_DETALLES = 30;   // tope: el panel no necesita más y evita crecer sin límite

    // Evita el registro DUPLICADO por reto: GameManager.OnLevelCompleted puede dispararse dos veces
    // por la misma compleción (p.ej. con la escena NoonA cargada aditivamente en el Host, o un
    // re-disparo del temporizador). Solo se permite UN registro por cada carga de nivel; un replay
    // legítimo vuelve a habilitarlo desde HandleLevelLoaded (ResetTracker).
    private bool _recordedThisLevel = false;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Start()
    {
        GameManager.OnLevelLoaded    += HandleLevelLoaded;
        GameManager.OnLevelCompleted += HandleLevelCompleted;
        ResetTracker();
    }

    void OnDestroy()
    {
        GameManager.OnLevelLoaded    -= HandleLevelLoaded;
        GameManager.OnLevelCompleted -= HandleLevelCompleted;
    }

    // ─────────────────────────────────────────────
    //  API Pública
    // ─────────────────────────────────────────────

    public void ResetTracker()
    {
        _startTime         = Time.time;
        _currentErrors     = 0;
        _recordedThisLevel = false;
        _errorsByType.Clear();
        _errorDetails.Clear();
    }

    public void AddError(string errorType = "General", string detalle = "")
    {
        _currentErrors++;
        if (string.IsNullOrEmpty(errorType)) errorType = "General";
        _errorsByType.TryGetValue(errorType, out int n);
        _errorsByType[errorType] = n + 1;
        if (_errorDetails.Count < MAX_DETALLES)
            _errorDetails.Add(string.IsNullOrEmpty(detalle) ? errorType : detalle);
        Debug.Log($"[PerformanceTracker] Error #{_currentErrors} [{errorType}] {detalle}");
    }

    /// <summary>Mensajes descriptivos de los errores del reto en curso (para el dashboard).</summary>
    public string[] GetErrorDetails() => _errorDetails.ToArray();

    /// <summary>Desglose de errores por tipo (categoría) del reto en curso.</summary>
    public ErrorTagCount[] GetErrorBreakdown()
    {
        var arr = new ErrorTagCount[_errorsByType.Count];
        int i = 0;
        foreach (var kv in _errorsByType)
            arr[i++] = new ErrorTagCount { tipo = kv.Key, count = kv.Value };
        return arr;
    }

    public float GetTime()   => Time.time - _startTime;
    public int   GetErrors() => _currentErrors;

    public string GetEvaluation()
    {
        float time  = GetTime();
        float limit = _currentLevelIndex < excellentTimeLimits.Length
                      ? excellentTimeLimits[_currentLevelIndex]
                      : 300f;

        if (_currentErrors == 0 && time <= limit)
            return $"[EXCELENTE] Sin errores en {time:F0}s";

        if (_currentErrors <= maxErrorsForGood)
            return $"[BUENO] {_currentErrors} errores, {time:F0}s";

        return $"[MEJORAR] {_currentErrors} errores";
    }

    /// <summary>Devuelve factor de bono (0-1) basado en velocidad de resolución.</summary>
    public float GetTimeBonus()
    {
        float time  = GetTime();
        float limit = _currentLevelIndex < excellentTimeLimits.Length
                      ? excellentTimeLimits[_currentLevelIndex]
                      : 300f;
        return Mathf.Clamp01(1f - time / limit);
    }

    public List<LevelRecord> GetAllRecords() => _records;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────

    void HandleLevelLoaded(LevelType level)
    {
        _currentLevelIndex = (int)level;
        ResetTracker();
    }

    void HandleLevelCompleted(LevelType level, bool success)
    {
        if (_recordedThisLevel) return;   // ya registrado este nivel → ignorar disparos duplicados
        _recordedThisLevel = true;

        float limite = 600f;
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && gm.currentTimeLimit > 0f) limite = gm.currentTimeLimit;

        float t = GetTime();
        _records.Add(new LevelRecord
        {
            level      = level,
            timeSeconds = t,
            errors     = _currentErrors,
            success    = success,
            evaluation = GetEvaluation(),
            errorTypes = GetErrorBreakdown(),
            detalles   = GetErrorDetails(),
            nota       = CalcularNota10(t, _currentErrors, success, limite)
        });
    }

    /// <summary>
    /// NOTA 0–10 del reto (compartida por ambos roles — el desempeño es de la pareja):
    ///   · 5 puntos por TIEMPO: nota completa hasta el 30 % del límite del reto;
    ///     desde ahí decae linealmente hasta 0 al agotar el límite.
    ///   · 5 puntos por ERRORES: cada intento erróneo resta 1 punto (0 errores = 5).
    ///   · Reto NO completado: la nota se topa en 4.0 (esfuerzo parcial, nunca aprobatoria).
    /// Redondeada a 1 decimal. Ej. (límite 600 s): 150 s y 1 error → 5.0 + 4.0 = 9.0.
    /// </summary>
    public static float CalcularNota10(float tiempoSeg, int errores, bool exito, float limiteSeg)
    {
        if (limiteSeg <= 0f) limiteSeg = 600f;
        float tOptimo = limiteSeg * 0.3f;
        float fTiempo = tiempoSeg <= tOptimo
            ? 1f
            : Mathf.Clamp01(1f - (tiempoSeg - tOptimo) / (limiteSeg - tOptimo));
        float nota = 5f * fTiempo + Mathf.Max(0f, 5f - errores);
        if (!exito) nota = Mathf.Min(nota, 4f);
        return Mathf.Round(nota * 10f) / 10f;
    }
}

[System.Serializable]
public struct LevelRecord
{
    public LevelType       level;
    public float           timeSeconds;
    public int             errors;
    public bool            success;
    public string          evaluation;
    public ErrorTagCount[] errorTypes;
    /// <summary>Mensajes descriptivos de cada error ("no llega a GND", "R muy baja → corto"...).</summary>
    public string[]        detalles;
    /// <summary>Nota 0–10 del reto (5 pts tiempo + 5 pts errores). Ver PerformanceTracker.CalcularNota10.</summary>
    public float           nota;
}

[System.Serializable]
public struct ErrorTagCount
{
    public string tipo;
    public int    count;
}