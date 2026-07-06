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
    }

    public void AddError(string errorType = "General")
    {
        _currentErrors++;
        if (string.IsNullOrEmpty(errorType)) errorType = "General";
        _errorsByType.TryGetValue(errorType, out int n);
        _errorsByType[errorType] = n + 1;
        Debug.Log($"[PerformanceTracker] Error #{_currentErrors} [{errorType}]");
    }

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

        _records.Add(new LevelRecord
        {
            level      = level,
            timeSeconds = GetTime(),
            errors     = _currentErrors,
            success    = success,
            evaluation = GetEvaluation(),
            errorTypes = GetErrorBreakdown()
        });
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
}

[System.Serializable]
public struct ErrorTagCount
{
    public string tipo;
    public int    count;
}