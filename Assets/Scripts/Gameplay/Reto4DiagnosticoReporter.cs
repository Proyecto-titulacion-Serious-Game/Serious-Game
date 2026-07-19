using UnityEngine;

/// <summary>
/// EXPLORADOR: publica cada ~2 s el diagnóstico RICO del Reto 4 (sketch/pin activo, corriente y
/// potencia de la protoboard, circuito abierto/sobrecarga) al clipboard del Técnico, vía
/// <see cref="GameSession.ReportarDiagnosticoReto"/> — mismo canal y mismo motor
/// (<see cref="DiagnosticSystem.GetDiagnosisArduino"/>) que ya usaban Retos 1/2/3
/// (<see cref="RetoDiagnosticoReporter"/>/<see cref="Reto2CircuitGuard"/>).
///
/// Antes, el Reto 4 solo llegaba al Técnico cuando presionaba "Comprobar" (el mensaje graduado
/// de <see cref="Reto4Feedback"/>, vía GameManager.PublicarDiagnosticoReto4) — sin nada
/// continuo mientras el Explorador todavía está cableando. Ambos mensajes comparten el mismo
/// canal de red: el de Comprobar aparece al instante y este lo reemplaza en el siguiente ciclo.
/// </summary>
public class Reto4DiagnosticoReporter : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<Reto4DiagnosticoReporter>() != null) return;

        // EXPLORADOR only — el Arduino/protoboard real vive en su cliente; la copia del Técnico
        // nunca se repara (ver el mismo chequeo en RetoDiagnosticoReporter/Reto2CircuitGuard).
        bool esTecnico = ConnectionManager.Instance != null &&
                          ConnectionManager.Instance.rolAutomatico == ConnectionManager.AutoConnectRole.Tecnico;
        if (esTecnico) return;

        var go = new GameObject("Reto4DiagnosticoReporter");
        DontDestroyOnLoad(go);
        go.AddComponent<Reto4DiagnosticoReporter>();
    }

    bool   _activo;
    string _ultimo;
    float  _next;
    GameManager _gameManager;
    readonly DiagnosticSystem _diagnostic = new DiagnosticSystem();

    void OnEnable()  => GameManager.OnLevelLoaded += OnLevel;
    void OnDisable() => GameManager.OnLevelLoaded -= OnLevel;

    void OnLevel(LevelType lt)
    {
        _activo = lt == LevelType.Arduino;
        _ultimo = null;
        _next   = 0f;   // forzar envío YA en el próximo Update() al entrar al Reto 4, no esperar
                         // el resto del ciclo de 2s pendiente del reto anterior.
        if (_activo && _gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
    }

    void Update()
    {
        if (!_activo) return;
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;

        if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
        if (_gameManager == null) return;

        var arduino  = FindAnyObjectByType<ArduinoCore>();
        var protoSim = _gameManager.protoSim;
        if (arduino == null && protoSim == null) return;

        string r = $"{_diagnostic.GetDiagnosisArduino(arduino, protoSim)}\n\n> " +
                   $"{_diagnostic.GetNextActionArduino(arduino, protoSim)}";
        if (string.IsNullOrEmpty(r)) return;

        // Ver comentario equivalente en RetoDiagnosticoReporter.Update(): no deduplicar mientras
        // GameSession todavía no spawneó en red — evita que el primer envío (fallback local,
        // nunca llega al Técnico) quede marcado como "ya enviado" para siempre.
        bool haySesion = GameSession.Instance != null && GameSession.Instance.Object != null && GameSession.Instance.Object.IsValid;
        if (haySesion && r == _ultimo) return;
        _ultimo = r;
        GameSession.ReportarDiagnosticoReto(4, r);
    }
}
