using UnityEngine;

/// <summary>
/// EXPLORADOR: publica un RESUMEN simple del estado del circuito del reto actual al clipboard del
/// Técnico (vía <see cref="GameSession.ReportarDiagnosticoReto"/>), para los retos que NO tienen un
/// reporter propio: 1 (OhmLaw) y 3 (Mixed). El Reto 2 (Parallel) lo maneja
/// <see cref="Reto2CircuitGuard"/> y el Reto 4 (Arduino) <see cref="Reto4DiagnosticoReporter"/>,
/// ambos con más detalle específico de su circuito.
///
/// Cuenta los LED encendidos en la escena (los que tienen nodos), igual que la victoria de retos 1-3.
/// Se auto-instancia; corre cada 2 s y solo reenvía si el resumen cambió.
/// </summary>
public class RetoDiagnosticoReporter : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<RetoDiagnosticoReporter>() != null) return;

        // EXPLORADOR only (ver resumen de la clase) — el Técnico tiene su PROPIA copia local,
        // nunca reparada, de los mismos Resistor/LED/Capacitor de la escena. Sin este chequeo,
        // ambos clientes llamaban RPC_ReportarDiagnosticoReto (RpcSources.All) y el del Técnico
        // (siempre desactualizado) podía pisar el diagnóstico real del Explorador.
        bool esTecnico = ConnectionManager.Instance != null &&
                          ConnectionManager.Instance.rolAutomatico == ConnectionManager.AutoConnectRole.Tecnico;
        if (esTecnico) return;

        var go = new GameObject("RetoDiagnosticoReporter");
        DontDestroyOnLoad(go);
        go.AddComponent<RetoDiagnosticoReporter>();
    }

    int    _reto = -1;
    string _ultimo;
    float  _next;
    readonly DiagnosticSystem _diagnostic = new DiagnosticSystem();

    void OnEnable()
    {
        GameManager.OnLevelLoaded += OnLevel;
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null) _reto = (int)gm.currentLevel + 1;
    }

    void OnDisable() => GameManager.OnLevelLoaded -= OnLevel;

    void OnLevel(LevelType lt)
    {
        _reto  = (int)lt + 1;
        _ultimo = null;
        _next   = 0f;   // forzar envío YA en el próximo Update(), no esperar el ciclo de 2s
                         // pendiente del reto anterior — si no, el clipboard queda mostrando
                         // el diagnóstico del reto viejo hasta que ese resto de ciclo expire.
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;

        // Reto 2 → Reto2CircuitGuard · Reto 4 → Reto4DiagnosticoReporter. Aquí solo 1 y 3.
        if (_reto != 1 && _reto != 3) return;

        // Diagnóstico RICO (mismo texto que antes solo se veía en el panel local del Técnico,
        // ahora sí viaja por la red): nombra cada componente y su estado, no solo un conteo de LEDs.
        // Se le suma la "próxima acción" (antes vivía en un segundo panel separado).
        string r = _reto == 1
            ? $"{_diagnostic.GetDiagnosisOhmLaw()}\n\n> {_diagnostic.GetNextActionOhmLaw()}"
            : $"{_diagnostic.GetDiagnosisMixed()}\n\n> {_diagnostic.GetNextActionMixed()}";
        if (string.IsNullOrEmpty(r)) return;

        // No deduplicar mientras NO haya GameSession en red todavía: este reporter arranca casi
        // instantáneo (RuntimeInitializeOnLoadMethod), pero el handshake de Fusion + el spawn de
        // GameSession pueden tardar varios segundos — si el primer intento cae en esa ventana,
        // ReportarDiagnosticoReto lo manda por el fallback LOCAL (sin red, el Técnico nunca lo
        // recibe), pero _ultimo quedaba igual "marcado como enviado" y no se reintentaba nunca
        // más si el diagnóstico no volvía a cambiar → el clipboard del Técnico se quedaba vacío
        // toda la sesión. Con la sesión ya lista, sí deduplicamos normal (evita reenviar lo mismo).
        bool haySesion = GameSession.Instance != null && GameSession.Instance.Object != null && GameSession.Instance.Object.IsValid;
        if (haySesion && r == _ultimo) return;
        _ultimo = r;
        GameSession.ReportarDiagnosticoReto(_reto, r);
    }
}
