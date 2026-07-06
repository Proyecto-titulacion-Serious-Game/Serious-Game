using UnityEngine;

/// <summary>
/// Loguea métricas de rendimiento a la consola/Player.log cada <see cref="IntervalSeconds"/>:
/// FPS promedio, ms/frame, y —si la plataforma lo soporta (Frame Timing Stats activado en
/// PlayerSettings)— el tiempo de CPU y GPU por frame vía FrameTimingManager.
///
/// Pensado para medir el Explorador en la Quest SIN overlay en pantalla: se lee por
/// `adb logcat -s Unity` o en el Player.log. Se auto-crea en runtime (no va en escena).
/// Costo despreciable: acumula contadores por frame y escribe ~1 línea cada N segundos.
///
/// Leer en logcat:  adb logcat -s Unity | findstr [PERF]
/// </summary>
public class PerformanceLogger : MonoBehaviour
{
    const float IntervalSeconds = 3f;

    int   _frames;
    float _elapsed;
    float _worstFrameMs;   // peor frame del intervalo (spike)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[PerformanceLogger]");
        go.AddComponent<PerformanceLogger>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        // Tiempo real del frame (sin escalar por timeScale; en pausa no infla las métricas).
        float dt = Time.unscaledDeltaTime;
        _frames++;
        _elapsed += dt;
        float frameMs = dt * 1000f;
        if (frameMs > _worstFrameMs) _worstFrameMs = frameMs;

        if (_elapsed < IntervalSeconds || _frames == 0) return;

        float avgFps = _frames / _elapsed;
        float avgMs  = (_elapsed / _frames) * 1000f;

        string gpu = GpuCpuTimings(out string cpu);

        Debug.Log(
            $"[PERF] FPS {avgFps:0.0}  |  frame {avgMs:0.0} ms (peor {_worstFrameMs:0.0} ms)" +
            $"  |  CPU {cpu}  GPU {gpu}");

        _frames = 0;
        _elapsed = 0f;
        _worstFrameMs = 0f;
    }

    /// <summary>Tiempos CPU/GPU del último frame medido por FrameTimingManager (ms),
    /// o "n/d" si la plataforma/PlayerSettings no los reporta.</summary>
    static string GpuCpuTimings(out string cpu)
    {
        cpu = "n/d";
        var timings = new FrameTiming[1];
        FrameTimingManager.CaptureFrameTimings();
        uint got = FrameTimingManager.GetLatestTimings(1, timings);
        if (got == 0) return "n/d";

        double cpuMs = timings[0].cpuFrameTime;   // ms
        double gpuMs = timings[0].gpuFrameTime;   // ms
        cpu = cpuMs > 0.0 ? $"{cpuMs:0.0} ms" : "n/d";
        return gpuMs > 0.0 ? $"{gpuMs:0.0} ms" : "n/d";
    }
}
