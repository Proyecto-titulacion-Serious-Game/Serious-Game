using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Corre una partida REAL en Play Mode (única forma de que las corrutinas — transición entre
/// retos y el POST HTTP a Supabase — se ejecuten de verdad): completa los 4 retos usando el mismo
/// atajo de producción F4 (<see cref="GameManager.DebugCompleteCurrentLevel"/>, el que usa
/// DebugLevelSkipper), fuerza modoOffline para no depender de un segundo jugador/red, espera a que
/// la sesión termine y el envío a Supabase (AnalyticsManager) se complete, y reporta el resultado
/// leyendo los logs reales de AnalyticsManager.
///
/// IMPORTANTE: entrar a Play Mode dispara un DOMAIN RELOAD (recarga los assemblies) — cualquier
/// campo static se pierde en ese instante. Todo el estado de la máquina (fase, nivel actual)
/// vive en <see cref="SessionState"/> (persiste a través del reload); la suscripción a
/// EditorApplication.update se re-engancha en el constructor estático [InitializeOnLoad], que
/// Unity vuelve a ejecutar automáticamente después de cada reload.
///
/// Menú: Tools → TITA → Pruebas → Playthrough completo + enviar a Supabase (Play Mode real)
/// </summary>
[InitializeOnLoad]
public static class FullPlaythroughSupabaseSend
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const float LEVEL_WAIT_BUFFER = 1.5f;
    const float SUPABASE_WAIT     = 8f;

    const string K_ACTIVE = "FP_Active";
    const string K_PHASE  = "FP_Phase";
    const string K_LEVEL  = "FP_Level";
    const string K_TSTART = "FP_PhaseStart";

    enum Phase { Idle = 0, Entering = 1, WaitStable = 2, PlayLevel = 3, WaitTransition = 4, WaitSupabase = 5, Finish = 6 }

    static GameManager _gm;   // se re-busca en cada Tick si es null (no sobrevive al reload)

    static FullPlaythroughSupabaseSend()
    {
        // Se re-ejecuta tras CADA domain reload (incl. el de entrar a Play Mode) — reenganchar
        // el tick solo si había una corrida activa (SessionState sobrevive al reload).
        if (SessionState.GetBool(K_ACTIVE, false))
            EditorApplication.update += Tick;
    }

    [MenuItem("Tools/TITA/Pruebas/Playthrough completo + enviar a Supabase (Play Mode real)")]
    public static void Run()
    {
        if (SessionState.GetBool(K_ACTIVE, false)) { Debug.LogWarning("[FullPlaythrough] Ya hay una corrida en curso."); return; }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveOpenScenes();

        SoloTechnicianDebug.forzarOfflineParaPruebaSolo = true;

        SessionState.SetBool(K_ACTIVE, true);
        SessionState.SetInt(K_PHASE, (int)Phase.Entering);
        SessionState.SetInt(K_LEVEL, 0);
        SessionState.SetFloat(K_TSTART, (float)EditorApplication.timeSinceStartup);

        EditorApplication.update += Tick;

        Debug.Log("[FullPlaythrough] Entrando a Play Mode...");
        EditorApplication.isPlaying = true;
    }

    static double Elapsed => EditorApplication.timeSinceStartup - SessionState.GetFloat(K_TSTART, 0f);

    static void SetPhase(Phase p)
    {
        SessionState.SetInt(K_PHASE, (int)p);
        SessionState.SetFloat(K_TSTART, (float)EditorApplication.timeSinceStartup);
    }

    static void Tick()
    {
        var phase = (Phase)SessionState.GetInt(K_PHASE, (int)Phase.Idle);
        if (phase == Phase.Idle) return;

        switch (phase)
        {
            case Phase.Entering:
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                Debug.Log("[FullPlaythrough] Play Mode activo. Esperando estabilización...");
                SetPhase(Phase.WaitStable);
                break;

            case Phase.WaitStable:
                if (Elapsed < 2.5) return;
                _gm = Object.FindAnyObjectByType<GameManager>();
                if (_gm == null)
                {
                    if (Elapsed < 8) return;   // dar más margen antes de rendirse
                    Fail("No se encontró GameManager en Play Mode tras 8s — ¿la escena no bootstrapeó?");
                    return;
                }
                Debug.Log($"[FullPlaythrough] GameManager listo. Nivel actual: {_gm.currentLevel}");
                EnsureSessionDataExporter();
                SetPhase(Phase.PlayLevel);
                break;

            case Phase.PlayLevel:
            {
                if (_gm == null) _gm = Object.FindAnyObjectByType<GameManager>();
                if (_gm == null) { Fail("GameManager se volvió null a mitad de la corrida."); return; }

                int level = SessionState.GetInt(K_LEVEL, 0);
                Debug.Log($"[FullPlaythrough] Completando Reto {level + 1} (F4 real: DebugCompleteCurrentLevel)...");
                _gm.DebugCompleteCurrentLevel();
                SetPhase(Phase.WaitTransition);
                break;
            }

            case Phase.WaitTransition:
            {
                if (_gm == null) _gm = Object.FindAnyObjectByType<GameManager>();
                float delay = GetZoneTransitionDelay(_gm) + LEVEL_WAIT_BUFFER;
                if (Elapsed < delay) return;

                int level = SessionState.GetInt(K_LEVEL, 0) + 1;
                SessionState.SetInt(K_LEVEL, level);
                if (level < 4) { SetPhase(Phase.PlayLevel); }
                else
                {
                    Debug.Log("[FullPlaythrough] Los 4 retos completados. Esperando envío (local + Supabase)...");
                    SetPhase(Phase.WaitSupabase);
                }
                break;
            }

            case Phase.WaitSupabase:
                if (Elapsed < SUPABASE_WAIT) return;
                SetPhase(Phase.Finish);
                break;

            case Phase.Finish:
                Report();
                break;
        }
    }

    /// <summary>
    /// DashboardBootstrap (RuntimeInitializeOnLoadMethod) solo crea SessionDataExporter cuando
    /// hay una escena cargada llamada "Tecnico" — esta prueba corre offline sobre Explorador.unity
    /// nada más, así que ese gate nunca se cumple y HandleLevelCompletedForSupabase jamás se
    /// suscribe (bug real detectado en la corrida del 25-jul-2026: "SessionDataExporter en
    /// escena: NO" en el reporte final, pese a que AnalyticsManager sí existía). Cargar además
    /// Tecnico.unity para pasar ese gate crearía un SEGUNDO GameManager (cada escena tiene el
    /// suyo, sin deduplicación) y contaminaría la prueba. En vez de eso, se replica aquí
    /// exactamente lo que hace DashboardBootstrap: crear el GameObject con SessionDataExporter,
    /// que se engancha al AnalyticsManager.Instance YA existente en Explorador.unity (colocado a
    /// mano) vía su propio Awake()/OnEnable().
    /// </summary>
    static void EnsureSessionDataExporter()
    {
        if (Object.FindAnyObjectByType<SessionDataExporter>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[FullPlaythrough] SessionDataExporter ya presente en la escena, no se duplica.");
            return;
        }

        var analytics = Object.FindAnyObjectByType<AnalyticsManager>(FindObjectsInactive.Include);
        if (analytics == null)
        {
            Debug.LogWarning("[FullPlaythrough] No hay AnalyticsManager en la escena — " +
                              "SessionDataExporter no podrá enviar nada aunque se cree.");
        }

        var go = new GameObject("Test_SessionDataExporter");
        var exporter = go.AddComponent<SessionDataExporter>();
        exporter.grupo = "[CLASE DE PRUEBA] FullPlaythroughSupabaseSend";
        Debug.Log("[FullPlaythrough] SessionDataExporter creado a mano para esta corrida " +
                  "(replica lo que DashboardBootstrap hace en la escena Tecnico).");
    }

    static float GetZoneTransitionDelay(GameManager gm)
    {
        if (gm == null) return 3f;
        var f = typeof(GameManager).GetField("zoneTransitionDelay", BindingFlags.Public | BindingFlags.Instance);
        return f != null ? (float)f.GetValue(gm) : 3f;
    }

    static void Fail(string reason)
    {
        Debug.LogError($"[FullPlaythrough] ✗ ABORTADO: {reason}");
        SetPhase(Phase.Finish);
    }

    static void Report()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(K_ACTIVE, false);
        SessionState.SetInt(K_PHASE, (int)Phase.Idle);

        var analytics = Object.FindAnyObjectByType<AnalyticsManager>();
        var exporter   = Object.FindAnyObjectByType<SessionDataExporter>();

        Debug.Log("═══════════════ [FullPlaythrough] REPORTE FINAL ═══════════════");
        Debug.Log($"AnalyticsManager en escena: {(analytics != null ? "SÍ" : "NO ← sin esto, EnviarMetricas nunca se llama")}");
        Debug.Log($"SessionDataExporter en escena: {(exporter != null ? "SÍ" : "NO")}");
        Debug.Log("[FullPlaythrough] Revisa arriba en este mismo log las líneas [AnalyticsManager]/[SessionDataExporter]/[ObjectiveSystem] para el resultado exacto del envío.");

        EditorApplication.isPlaying = false;

        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
