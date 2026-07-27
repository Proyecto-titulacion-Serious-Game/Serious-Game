#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Mitad TÉCNICO de la prueba de red real pedida por el usuario (2026-07-25): abre Tecnico.unity,
/// entra por el HUD real de <see cref="RoomCodeEntryUI"/> escribiendo el código de clase real
/// "SEC-2VFN" (simula tipeo — fija el campo privado _grupo y llama al mismo método privado
/// CrearSala() que dispara el botón "Comenzar"/Enter, no un atajo aparte), espera que el Explorador
/// (proceso separado, ver <see cref="ExploradorNetworkTest"/>) se conecte por Fusion de verdad, le
/// envía 3 valores de resistor incorrectos + 1 correcto por <see cref="GameSession.EnviarComponente"/>
/// (la RPC real, no una llamada local), y verifica que Reto 1 se complete y la telemetría llegue a
/// Supabase con el sesion_id de esa clase real.
///
/// Corre en paralelo con ExploradorNetworkTest en OTRO proceso de Unity — deben lanzarse juntos.
/// </summary>
[InitializeOnLoad]
public static class TecnicoNetworkTest
{
    const string ScenePath = "Assets/Scenes/Tecnico/Tecnico.unity";
    const string CODIGO_TEST = "SEC-2VFN";
    const float FACTOR_INCORRECTO = 1.50f;

    const string K_ACTIVE = "TNT_Active";
    const string K_PHASE  = "TNT_Phase";
    const string K_TSTART = "TNT_PhaseStart";
    const string K_INTENTO = "TNT_Intento";

    enum Phase
    {
        Idle = 0, Entering = 1, WaitStable = 2, IngresarCodigo = 3, WaitExplorador = 4,
        EnviarIncorrecto = 5, EsperarProceso = 6, EnviarCorrecto = 7, WaitCompletado = 8,
        WaitSupabase = 9, Finish = 10
    }

    static TecnicoNetworkTest()
    {
        if (SessionState.GetBool(K_ACTIVE, false))
            EditorApplication.update += Tick;
    }

    [MenuItem("Tools/TITA/Pruebas/Red real — mitad TÉCNICO (correr junto con ExploradorNetworkTest)")]
    public static void Run()
    {
        if (SessionState.GetBool(K_ACTIVE, false)) { Debug.LogWarning("[TecnicoNet] Ya hay una corrida en curso."); return; }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveOpenScenes();

        SessionState.SetBool(K_ACTIVE, true);
        SessionState.SetInt(K_PHASE, (int)Phase.Entering);
        SessionState.SetInt(K_INTENTO, 0);
        SessionState.SetFloat(K_TSTART, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update += Tick;

        Debug.Log("[TecnicoNet] Entrando a Play Mode (rol Técnico/Host)...");
        EditorApplication.isPlaying = true;
    }

    static double Elapsed => EditorApplication.timeSinceStartup - SessionState.GetFloat(K_TSTART, 0f);
    static void SetPhase(Phase p) { SessionState.SetInt(K_PHASE, (int)p); SessionState.SetFloat(K_TSTART, (float)EditorApplication.timeSinceStartup); }

    static void Tick()
    {
        var phase = (Phase)SessionState.GetInt(K_PHASE, (int)Phase.Idle);
        if (phase == Phase.Idle) return;

        switch (phase)
        {
            case Phase.Entering:
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                Debug.Log("[TecnicoNet] Play Mode activo. Esperando estabilización...");
                SetPhase(Phase.WaitStable);
                break;

            case Phase.WaitStable:
                if (Elapsed < 3.0) return;
                SetPhase(Phase.IngresarCodigo);
                break;

            case Phase.IngresarCodigo:
            {
                var ui = Object.FindAnyObjectByType<RoomCodeEntryUI>(FindObjectsInactive.Include);
                if (ui == null) { if (Elapsed < 10) return; Fail("No apareció RoomCodeEntryUI tras 10s."); return; }

                var cm = ConnectionManager.Instance;
                if (cm == null) { if (Elapsed < 10) return; Fail("No hay ConnectionManager."); return; }

                Debug.Log($"[TecnicoNet] [HUD RoomCodeEntryUI] Escribiendo código de clase '{CODIGO_TEST}' y presionando 'Comenzar'...");
                var fGrupo = typeof(RoomCodeEntryUI).GetField("_grupo", BindingFlags.NonPublic | BindingFlags.Instance);
                fGrupo.SetValue(ui, CODIGO_TEST);
                var mCrear = typeof(RoomCodeEntryUI).GetMethod("CrearSala", BindingFlags.NonPublic | BindingFlags.Instance);
                mCrear.Invoke(ui, new object[] { cm });

                SetPhase(Phase.WaitExplorador);
                break;
            }

            case Phase.WaitExplorador:
            {
                var runner = Object.FindAnyObjectByType<Fusion.NetworkRunner>();
                int players = runner != null ? runner.ActivePlayers.Count() : 0;
                if (players >= 2)
                {
                    Debug.Log($"[TecnicoNet] ✅ Explorador conectado (ActivePlayers={players}). Esperando que llegue a Reto 1...");
                    SetPhase(Phase.EnviarIncorrecto);
                    return;
                }
                if (Elapsed > 60) { Fail("El Explorador no se conectó en 60s (revisar sala/código en ambos procesos)."); return; }
                break;
            }

            case Phase.EnviarIncorrecto:
            {
                if (Elapsed < 5.0) return;   // margen para que el Explorador termine de estabilizar en Reto 1

                int intento = SessionState.GetInt(K_INTENTO, 0) + 1;
                SessionState.SetInt(K_INTENTO, intento);

                var gs = GameSession.Instance;
                if (gs == null) { Fail("No hay GameSession.Instance en el Técnico."); return; }

                float valorIncorrecto = 850f * FACTOR_INCORRECTO;   // 850Ω es el correctResistance real de Reto 1
                Debug.Log($"[TecnicoNet] [Técnico] Envía por RPC (EnviarComponente) intento {intento}/3 — Resistor {valorIncorrecto:0.#}Ω (incorrecta).");
                gs.EnviarComponente(ComponentType.Resistor, valorIncorrecto);

                SetPhase(Phase.EsperarProceso);
                break;
            }

            case Phase.EsperarProceso:
                if (Elapsed < 4.0) return;   // margen para RPC ida+vuelta + instalación simulada del lado Explorador
                if (SessionState.GetInt(K_INTENTO, 0) < 3) SetPhase(Phase.EnviarIncorrecto);
                else SetPhase(Phase.EnviarCorrecto);
                break;

            case Phase.EnviarCorrecto:
            {
                var gs = GameSession.Instance;
                if (gs == null) { Fail("GameSession.Instance se volvió null."); return; }
                Debug.Log("[TecnicoNet] [Técnico] Envía por RPC la resistencia CORRECTA 850Ω.");
                gs.EnviarComponente(ComponentType.Resistor, 850f);
                SetPhase(Phase.WaitCompletado);
                break;
            }

            case Phase.WaitCompletado:
            {
                if (Elapsed < 6.0) return;
                var gm = Object.FindAnyObjectByType<GameManager>();
                Debug.Log($"[TecnicoNet] Nivel completado (lado Técnico) = {(gm != null ? gm.levelCompleted.ToString() : "N/A (sin GameManager)")}");
                SetPhase(Phase.WaitSupabase);
                break;
            }

            case Phase.WaitSupabase:
                if (Elapsed < 8.0) return;
                SetPhase(Phase.Finish);
                break;

            case Phase.Finish:
                Report();
                break;
        }
    }

    static void Fail(string reason)
    {
        Debug.LogError($"[TecnicoNet] ✗ ABORTADO: {reason}");
        SetPhase(Phase.Finish);
    }

    static void Report()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(K_ACTIVE, false);
        SessionState.SetInt(K_PHASE, (int)Phase.Idle);

        Debug.Log("═══════════════ [TecnicoNet] REPORTE FINAL ═══════════════");
        Debug.Log("Revisar arriba [AnalyticsManager]/[SessionDataExporter]/[GameManager]/[GameSession] para el detalle.");

        EditorApplication.isPlaying = false;
        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
#endif
