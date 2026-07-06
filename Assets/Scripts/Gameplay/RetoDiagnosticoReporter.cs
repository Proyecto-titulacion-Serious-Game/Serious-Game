using UnityEngine;

/// <summary>
/// EXPLORADOR: publica un RESUMEN simple del estado del circuito del reto actual al clipboard del
/// Técnico (vía <see cref="GameSession.ReportarDiagnosticoReto"/>), para los retos que NO tienen un
/// guard propio: 1 (OhmLaw), 3 (Mixed) y 4 (Arduino). El Reto 2 (Parallel) lo maneja
/// <see cref="Reto2CircuitGuard"/> con más detalle.
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
        var go = new GameObject("RetoDiagnosticoReporter");
        DontDestroyOnLoad(go);
        go.AddComponent<RetoDiagnosticoReporter>();
    }

    int    _reto = -1;
    string _ultimo;
    float  _next;

    void OnEnable()
    {
        GameManager.OnLevelLoaded += OnLevel;
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null) _reto = (int)gm.currentLevel + 1;
    }

    void OnDisable() => GameManager.OnLevelLoaded -= OnLevel;

    void OnLevel(LevelType lt) { _reto = (int)lt + 1; _ultimo = null; }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;

        // Reto 2 → Reto2CircuitGuard · Reto 4 → GameManager (feedback graduado). Aquí solo 1 y 3.
        if (_reto != 1 && _reto != 3) return;

        int on = 0, total = 0;
        foreach (var led in FindObjectsByType<LED>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (led == null || !led.gameObject.activeInHierarchy) continue;
            if (led.nodeA == null || led.nodeB == null) continue;   // solo LEDs conectados al circuito
            total++;
            if (led.isOn) on++;
        }
        if (total == 0) return;   // nada que reportar (ej. escena del Técnico, sin LEDs)

        string r = (on == total)
            ? $"LEDs: {on}/{total} ✅\nCircuito completo."
            : $"LEDs: {on}/{total}\nFalta: completar el circuito.";

        if (r == _ultimo) return;
        _ultimo = r;
        GameSession.ReportarDiagnosticoReto(_reto, r);
    }
}
