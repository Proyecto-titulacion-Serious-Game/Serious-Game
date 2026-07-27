#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Mitad EXPLORADOR de la prueba de red real pedida por el usuario (2026-07-25): abre
/// Explorador.unity SIN forzar modoOffline (a diferencia de Reto1TelemetriaConCodigoTest), se
/// conecta de verdad por Fusion (rolAutomatico=Explorador se auto-conecta en
/// ConnectionManager.Start()) a la sala que crea <see cref="TecnicoNetworkTest"/> en el otro
/// proceso, y por cada componente recibido por RPC real (ComponentDeliverySystem.HasPendingDelivery
/// se vuelve true vía GameSession.OnComponenteRecibido → ExplorerComponentReceiver → PrepareForInstall)
/// simula la instalación física llamando OnExplorerInstalled — el mismo punto de entrada que usaría
/// la colisión real del componente contra el ComponentSlot en VR.
///
/// Corre en paralelo con TecnicoNetworkTest en OTRO proceso de Unity — deben lanzarse juntos.
/// </summary>
[InitializeOnLoad]
public static class ExploradorNetworkTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const int MAX_ENTREGAS = 4;   // 3 incorrectas + 1 correcta

    const string K_ACTIVE = "ENT_Active";
    const string K_PHASE  = "ENT_Phase";
    const string K_TSTART = "ENT_PhaseStart";
    const string K_PROCESADAS = "ENT_Procesadas";

    enum Phase { Idle = 0, Entering = 1, WaitStable = 2, WaitConexion = 3, ProcesarEntregas = 4, CerrarSwitch = 5, WaitFinal = 6, Finish = 7 }

    static GameManager _gm;
    static ComponentDeliverySystem _delivery;

    static ExploradorNetworkTest()
    {
        if (SessionState.GetBool(K_ACTIVE, false))
            EditorApplication.update += Tick;
    }

    [MenuItem("Tools/TITA/Pruebas/Red real — mitad EXPLORADOR (correr junto con TecnicoNetworkTest)")]
    public static void Run()
    {
        if (SessionState.GetBool(K_ACTIVE, false)) { Debug.LogWarning("[ExploradorNet] Ya hay una corrida en curso."); return; }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveOpenScenes();

        SessionState.SetBool(K_ACTIVE, true);
        SessionState.SetInt(K_PHASE, (int)Phase.Entering);
        SessionState.SetInt(K_PROCESADAS, 0);
        SessionState.SetFloat(K_TSTART, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update += Tick;

        Debug.Log("[ExploradorNet] Entrando a Play Mode (rol Explorador/Client, red REAL — sin forzar offline)...");
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
                Debug.Log("[ExploradorNet] Play Mode activo. Esperando estabilización...");
                SetPhase(Phase.WaitStable);
                break;

            case Phase.WaitStable:
                if (Elapsed < 2.5) return;
                _gm = Object.FindAnyObjectByType<GameManager>();
                if (_gm == null) { if (Elapsed < 10) return; Fail("No hay GameManager tras 10s."); return; }
                _delivery = Object.FindFirstObjectByType<ComponentDeliverySystem>(FindObjectsInactive.Include);
                if (_delivery == null) { Fail("No hay ComponentDeliverySystem en escena."); return; }
                SetPhase(Phase.WaitConexion);
                break;

            case Phase.WaitConexion:
            {
                var runner = Object.FindAnyObjectByType<Fusion.NetworkRunner>();
                bool conectado = runner != null && runner.IsRunning;
                if (conectado)
                {
                    Debug.Log("[ExploradorNet] ✅ Runner de Fusion corriendo — conexión establecida.");
                    SetPhase(Phase.ProcesarEntregas);
                    return;
                }
                if (Elapsed > 60) { Fail("No se estableció conexión de red en 60s."); return; }
                break;
            }

            case Phase.ProcesarEntregas:
            {
                int procesadas = SessionState.GetInt(K_PROCESADAS, 0);
                if (procesadas >= MAX_ENTREGAS) { SetPhase(Phase.CerrarSwitch); return; }

                if (_delivery.HasPendingDelivery())
                {
                    var slot = BuscarSlotDeResistor();
                    if (slot == null) { Fail("No hay ComponentSlot de tipo Resistor."); return; }

                    int erroresPrev = ContarErrores();
                    _delivery.OnExplorerInstalled(slot);
                    int erroresPost = ContarErrores();

                    procesadas++;
                    SessionState.SetInt(K_PROCESADAS, procesadas);
                    Debug.Log($"[ExploradorNet] [Explorador] Entrega #{procesadas}/{MAX_ENTREGAS} recibida por RPC real e " +
                              $"'instalada' (OnExplorerInstalled) → errores {erroresPrev} → {erroresPost}.");
                }

                if (Elapsed > 90) { Fail($"Solo llegaron {procesadas}/{MAX_ENTREGAS} entregas en 90s — revisar RPC del lado Técnico."); return; }
                break;
            }

            case Phase.CerrarSwitch:
            {
                foreach (var sw in Object.FindObjectsByType<CircuitSwitch>(FindObjectsInactive.Exclude))
                {
                    if (sw == null || sw.isOn) continue;
                    sw.Toggle();
                    Debug.Log($"[ExploradorNet] [Explorador] Cierra el switch '{sw.name}'.");
                }
                ForzarReevaluacion();
                SetPhase(Phase.WaitFinal);
                break;
            }

            case Phase.WaitFinal:
                if (Elapsed < 5.0) return;
                Debug.Log($"[ExploradorNet] Nivel completado (lado Explorador) = {(_gm != null ? _gm.levelCompleted.ToString() : "N/A")}");
                SetPhase(Phase.Finish);
                break;

            case Phase.Finish:
                Report();
                break;
        }
    }

    static ComponentSlot BuscarSlotDeResistor()
    {
        foreach (var s in Object.FindObjectsByType<ComponentSlot>(FindObjectsInactive.Exclude))
            if (s != null && s.acceptedType == ComponentSlotType.Resistor)
                return s;
        return null;
    }

    static int ContarErrores()
    {
        var tracker = Object.FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
        return tracker != null ? tracker.GetErrors() : -1;
    }

    static void ForzarReevaluacion()
    {
        if (_gm == null) return;
        if (_gm.circuit != null) _gm.circuit.MarkDirty();
        foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
        {
            if (cm == null) continue;
            cm.MarkDirty();
            cm.ForceSimulate();
        }
    }

    static void Fail(string reason)
    {
        Debug.LogError($"[ExploradorNet] ✗ ABORTADO: {reason}");
        SetPhase(Phase.Finish);
    }

    static void Report()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(K_ACTIVE, false);
        SessionState.SetInt(K_PHASE, (int)Phase.Idle);

        Debug.Log("═══════════════ [ExploradorNet] REPORTE FINAL ═══════════════");

        EditorApplication.isPlaying = false;
        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
#endif
