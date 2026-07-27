using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gestiona los objetivos específicos de cada reto y el puntaje final.
/// Se suscribe a eventos de GameManager para actualizar el estado automáticamente.
/// </summary>
public class ObjectiveSystem : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias")]
    public GameManager      gameManager;
    public PerformanceTracker performance;

    // ─────────────────────────────────────────────
    //  Estado
    // ─────────────────────────────────────────────
    [Header("Objetivos activos (solo lectura)")]
    [SerializeField] private List<Objective> _objectives = new List<Objective>();
    [SerializeField] private int   _totalScore    = 0;
    [SerializeField] private int   _maxScore      = 0;

    // ─────────────────────────────────────────────
    //  Eventos
    // ─────────────────────────────────────────────
    public static event Action<Objective>     OnObjectiveCompleted;
    public static event Action<int, int>      OnScoreUpdated;        // score, maxScore
    public static event Action<SessionResult> OnSessionEnded;

    // ─────────────────────────────────────────────
    //  Flags de interacción
    // ─────────────────────────────────────────────
    // Evitan que objetivos que requieren acción del jugador se completen solos
    private bool _multimeterUsed = false;
    private bool _ended          = false;   // evita disparar OnSessionEnded dos veces

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Start()
    {
        GameManager.OnLevelLoaded              += BuildObjectivesForLevel;
        GameManager.OnLevelCompleted           += HandleLevelCompleted;
        GameManager.OnGameCompleted            += HandleGameCompleted;
        CircuitManager.OnCircuitChanged        += AutoCompleteByCircuitState;
        Multimeter.OnReadingTaken              += OnMultimeterUsed;
        ProtoboardSimulator.OnSandboxValidated += HandleSandboxValidated;
    }

    void OnDestroy()
    {
        GameManager.OnLevelLoaded              -= BuildObjectivesForLevel;
        GameManager.OnLevelCompleted           -= HandleLevelCompleted;
        GameManager.OnGameCompleted            -= HandleGameCompleted;
        CircuitManager.OnCircuitChanged        -= AutoCompleteByCircuitState;
        Multimeter.OnReadingTaken              -= OnMultimeterUsed;
        ProtoboardSimulator.OnSandboxValidated -= HandleSandboxValidated;
    }

    void HandleSandboxValidated(SandboxValidationResult r)
    {
        if (r.blinkEnabled)              CompleteObjectiveOnce(1); // sketch subido con blink
        if (r.pathFound)                 CompleteObjectiveOnce(2); // LED conectado
        if (r.hasProtection)             CompleteObjectiveOnce(3); // resistencia presente
        if (r.success)                   CompleteObjectiveOnce(4); // circuito validado
    }

    void OnMultimeterUsed() => _multimeterUsed = true;

    // ─────────────────────────────────────────────
    //  Construcción de objetivos por reto
    // ─────────────────────────────────────────────

    void BuildObjectivesForLevel(LevelType level)
    {
        _objectives.Clear();
        _totalScore      = 0;
        _maxScore        = 0;
        _multimeterUsed  = false;

        switch (level)
        {
            case LevelType.OhmLaw:
                AddObjective("Conectar el multímetro a dos nodos",            100);
                AddObjective("Leer e interpretar el voltaje medido",           150);
                AddObjective("Seleccionar la resistencia defectuosa",          150);
                AddObjective("Reemplazar la resistencia con el valor correcto", 200);
                break;

            case LevelType.Parallel:
                AddObjective("Identificar el sensor (LED) que no enciende",       150);
                AddObjective("Medir el LED: 0V / sin corriente = polaridad invertida", 200);
                AddObjective("Colocar el LED con la polaridad correcta",          250);
                break;

            case LevelType.Mixed:
                AddObjective("Identificar el LED con polaridad invertida",         200);
                AddObjective("Identificar el capacitor con polaridad invertida",   200);
                AddObjective("Leer código de colores y corregir la resistencia",   250);
                AddObjective("Corregir las 3 fallas en orden de prioridad",        150);
                break;

            case LevelType.Arduino:
                AddObjective("Escribir el sketch: elegir pin, OUTPUT, BLINK",       200);
                AddObjective("Subir el codigo al Arduino desde el IDE",             150);
                AddObjective("Explorador conecta LED en la protoboard",             200);
                AddObjective("Circuito con resistencia de proteccion >= 100 Ohm",   200);
                AddObjective("Validar circuito: LED parpadea de forma segura",      250);
                break;
        }

        OnScoreUpdated?.Invoke(_totalScore, _maxScore);
    }

    void AddObjective(string description, int points)
    {
        _objectives.Add(new Objective
        {
            description = description,
            maxPoints   = points,
            isCompleted = false
        });
        _maxScore += points;
    }

    // ─────────────────────────────────────────────
    //  API Pública — llamar desde otros sistemas
    // ─────────────────────────────────────────────

    /// <summary>Marca el objetivo en el índice como completado y suma puntos.</summary>
    public void CompleteObjective(int index, float timeBonus = 1f)
    {
        if (index < 0 || index >= _objectives.Count) return;

        var obj = _objectives[index];
        if (obj.isCompleted) return;

        obj.isCompleted   = true;
        obj.pointsEarned  = Mathf.RoundToInt(obj.maxPoints * Mathf.Clamp01(timeBonus));
        _totalScore      += obj.pointsEarned;
        _objectives[index] = obj;

        OnObjectiveCompleted?.Invoke(obj);
        OnScoreUpdated?.Invoke(_totalScore, _maxScore);

        Debug.Log($"[ObjectiveSystem] ✓ {obj.description} — +{obj.pointsEarned}pts");
    }

    public List<Objective>  GetObjectives()  => _objectives;
    public int              GetTotalScore()  => _totalScore;
    public int              GetMaxScore()    => _maxScore;
    public float            GetScorePercent() =>
        _maxScore > 0 ? (float)_totalScore / _maxScore : 0f;

    // ─────────────────────────────────────────────
    //  Manejo de fin de sesión
    // ─────────────────────────────────────────────

    void HandleLevelCompleted(LevelType level, bool success)
    {
        // Si el nivel se completó con éxito, dar bono de velocidad
        if (success && performance != null)
        {
            float elapsed = performance.GetTime();
            float limit   = gameManager?.currentTimeLimit ?? 600f;
            float bonus   = Mathf.Clamp01(1f - elapsed / limit);

            // Completar el último objetivo del nivel automáticamente con bono
            int lastIdx = _objectives.Count - 1;
            CompleteObjective(lastIdx, bonus);
        }
    }

    // ─────────────────────────────────────────────
    //  Auto-completar objetivos por estado del circuito
    // ─────────────────────────────────────────────

    /// <summary>
    /// Detecta hitos observables en el circuito y marca el objetivo correspondiente.
    /// Solo completa cada objetivo una vez (CompleteObjective es idempotente).
    /// </summary>
    void AutoCompleteByCircuitState()
    {
        if (gameManager?.circuit == null) return;

        var circuit = gameManager.circuit;

        switch (gameManager.currentLevel)
        {
            case LevelType.OhmLaw:
                // Obj 0: multímetro conectado a dos nodos — solo si el jugador usó el multímetro
                if (_multimeterUsed)
                    CompleteObjectiveOnce(0);
                // Obj 1: voltaje leído — solo tras usar el multímetro y obtener lectura > 0
                if (_multimeterUsed && circuit.totalCurrent > 0f)
                    CompleteObjectiveOnce(1);
                // Obj 2: resistencia seleccionada → delegado a TechnicianActions
                // Obj 3: reparación aplicada → se completa en HandleLevelCompleted
                break;

            case LevelType.Parallel:
                // Obj 0: sensor apagado identificado — el jugador midió con el multímetro
                if (_multimeterUsed)
                    CompleteObjectiveOnce(0);
                // Obj 1: polaridad invertida diagnosticada — tras medir (LED a 0V / sin corriente)
                if (_multimeterUsed)
                    CompleteObjectiveOnce(1);
                // Obj 2: LED colocado con polaridad correcta → HandleLevelCompleted
                break;

            case LevelType.Mixed:
                // Obj 0: capacitor invertido detectado
                foreach (var c in circuit.components)
                    if (c is Capacitor cap && cap.polarityInverted)
                    { CompleteObjectiveOnce(0); break; }

                // Obj 1: LED invertido detectado
                foreach (var c in circuit.components)
                    if (c is LED led && led.polarityInverted)
                    { CompleteObjectiveOnce(1); break; }

                // Obj 2: resistencia incorrecta identificada
                foreach (var c in circuit.components)
                    if (c is Resistor r && r.hasFault)
                    { CompleteObjectiveOnce(2); break; }

                // Obj 3: todas las fallas corregidas → HandleLevelCompleted
                break;

            case LevelType.Arduino:
                // Obj 0: multímetro en escena
                if (circuit.sourceVoltage > 0f)
                    CompleteObjectiveOnce(0);

                // Obj 1: pin corregido
                bool pinOk = true;
                foreach (var c in circuit.components)
                    if (c is ArduinoPin p && p.hasFault) { pinOk = false; break; }
                if (pinOk && gameManager.HasPerformedRepair())
                    CompleteObjectiveOnce(1);

                // Sandbox Reto 4: objetivos 1-4 se completan via OnSandboxValidated (ver HandleSandboxValidated)
                break;
        }
    }

    void CompleteObjectiveOnce(int index)
    {
        if (index < 0 || index >= _objectives.Count) return;
        if (_objectives[index].isCompleted) return;
        CompleteObjective(index);
    }

    /// <summary>Finaliza la sesión a demanda (ej. al pulsar "Salir del juego"): registra el resultado
    /// PARCIAL con los retos completados hasta ahora. Cuenta como partida finalizada.</summary>
    public void FinalizarSesion() => HandleGameCompleted();

    /// <summary>
    /// Red de seguridad: si el proceso se cierra SIN pasar por "Salir del juego" (Alt+F4, la X de
    /// la ventana, cerrar la Quest a la fuerza, o simplemente matar el proceso) — el escenario más
    /// probable en un aula real — la sesión se perdía por completo: OnSessionEnded solo se disparaba
    /// al completar los 4 retos o al pulsar Salir explícitamente. Ahora, al cerrar, se finaliza
    /// igual con lo completado hasta ese momento (mismo camino que FinalizarSesion). El respaldo
    /// local (JSON/CSV) se guarda de forma síncrona; la subida a Sheets es best-effort — Unity da
    /// unos pocos frames a OnApplicationQuit, no siempre alcanzan para que termine el POST HTTP.
    /// </summary>
    void OnApplicationQuit()
    {
        if (!_ended) HandleGameCompleted();
    }

    void HandleGameCompleted()
    {
        if (_ended) return;     // no terminar la sesión dos veces (fin normal + salir manual)
        _ended = true;

        // Agregar totales desde todos los registros de reto (no solo el último nivel activo)
        int   retosCompletados = 0;
        int   totalErrors      = 0;
        float totalTime        = 0f;

        if (performance != null)
        {
            var records = performance.GetAllRecords();
            retosCompletados = records.Count;
            foreach (var r in records)
            {
                totalErrors += r.errors;
                totalTime   += r.timeSeconds;
            }
        }

        // Sin retos completados (p. ej. salir al inicio o probar solo el Técnico) NO es "Excelente".
        string sessionEval =
            retosCompletados == 0 ? "[ABANDONADA] Sin retos completados" :
            totalErrors == 0      ? $"[EXCELENTE] {retosCompletados} reto(s) sin errores" :
            totalErrors <= 3      ? $"[BUENO] {totalErrors} errores en {retosCompletados} reto(s)" :
                                    $"[MEJORAR] {totalErrors} errores — repasar procedimientos";

        var result = new SessionResult
        {
            totalScore       = _totalScore,
            maxScore         = _maxScore,
            scorePercent     = GetScorePercent(),
            totalErrors      = totalErrors,
            totalTimeSeconds = totalTime,
            evaluation       = sessionEval
        };

        OnSessionEnded?.Invoke(result);
        Debug.Log($"[ObjectiveSystem] Sesión terminada: {_totalScore}/{_maxScore} pts " +
                  $"({result.scorePercent:P0}) — {result.evaluation}");
    }
}

// ─────────────────────────────────────────────
//  Estructuras de datos
// ─────────────────────────────────────────────

[Serializable]
public struct Objective
{
    public string description;
    public int    maxPoints;
    public int    pointsEarned;
    public bool   isCompleted;
}

[Serializable]
public struct SessionResult
{
    public int    totalScore;
    public int    maxScore;
    public float  scorePercent;
    public int    totalErrors;
    public float  totalTimeSeconds;
    public string evaluation;
}