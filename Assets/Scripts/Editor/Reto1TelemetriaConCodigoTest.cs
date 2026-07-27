#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Prueba end-to-end pedida por el usuario (2026-07-25): reproduce, por la ruta REAL de código (no
/// atajos de F8), la secuencia — Explorador entra a la zona de Reto 1 y mide con el multímetro,
/// el Técnico envía la resistencia equivocada 3 veces, luego la correcta y completa el reto — usando
/// el código de clase real <c>SEC-2VFN</c> (ya creado por el usuario en la tabla `sesiones_config` de
/// Supabase) para confirmar que la telemetría de Reto 1 llega enlazada a esa sesión real, no a
/// "Modo Práctica Libre".
///
/// Por qué Play Mode real (igual que <see cref="FullPlaythroughSupabaseSend"/>): las corrutinas de
/// `AnalyticsManager` (ValidarCodigoSesion, EnviarMetricas) y la corrutina diferida de
/// `SessionDataExporter` (fix del bug de orden de suscripción, mismo día) solo corren de verdad en
/// Play Mode. Estado persistido en <see cref="SessionState"/> para sobrevivir el domain reload de
/// entrar a Play Mode.
///
/// Usa <see cref="ComponentDeliverySystem.DebugSimularEntregaEInstalacion"/> (la MISMA ruta de
/// validación real que usa F9/F10/F11 — no `Repair()` directo como F8) para que un valor incorrecto
/// se rechace de verdad y uno correcto repare de verdad, exactamente como si el Técnico lo hubiera
/// enviado por red y el Explorador lo hubiera instalado.
///
/// Menú: Tools → TITA → Pruebas → Reto 1 con código SEC-2VFN (Play Mode real)
/// </summary>
[InitializeOnLoad]
public static class Reto1TelemetriaConCodigoTest
{
    const string ScenePath   = "Assets/Scenes/Explorador.unity";
    const string CODIGO_TEST = "SEC-2VFN";

    // Factor claramente fuera de tolerancia (±12%, ver SoloTechnicianDebug) para que el rechazo
    // sea inequívoco y no dependa del valor exacto del reto.
    const float FACTOR_INCORRECTO = 1.50f;

    const string K_ACTIVE  = "R1T_Active";
    const string K_PHASE   = "R1T_Phase";
    const string K_TSTART  = "R1T_PhaseStart";
    const string K_INTENTO = "R1T_IntentoIncorrecto";
    const string K_ERRORES_ANTES = "R1T_ErroresAntes";

    enum Phase
    {
        Idle = 0, Entering = 1, WaitStable = 2, ValidarCodigo = 3, WaitCodigo = 4,
        AsegurarReto1 = 5, EnviarIncorrecto = 6, EsperarEntreIntentos = 7,
        EnviarCorrecto = 8, WaitTransicion = 9, WaitSupabase = 10, Finish = 11
    }

    static GameManager _gm;
    static ComponentDeliverySystem _delivery;

    static Reto1TelemetriaConCodigoTest()
    {
        if (SessionState.GetBool(K_ACTIVE, false))
            EditorApplication.update += Tick;
    }

    [MenuItem("Tools/TITA/Pruebas/Reto 1 con código SEC-2VFN (Play Mode real)")]
    public static void Run()
    {
        if (SessionState.GetBool(K_ACTIVE, false)) { Debug.LogWarning("[Reto1Test] Ya hay una corrida en curso."); return; }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveOpenScenes();

        SoloTechnicianDebug.forzarOfflineParaPruebaSolo = true;

        SessionState.SetBool(K_ACTIVE, true);
        SessionState.SetInt(K_PHASE, (int)Phase.Entering);
        SessionState.SetInt(K_INTENTO, 0);
        SessionState.SetFloat(K_TSTART, (float)EditorApplication.timeSinceStartup);

        EditorApplication.update += Tick;

        Debug.Log("[Reto1Test] Entrando a Play Mode...");
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
                Debug.Log("[Reto1Test] Play Mode activo. Esperando estabilización...");
                SetPhase(Phase.WaitStable);
                break;

            case Phase.WaitStable:
                if (Elapsed < 2.5) return;
                _gm = Object.FindAnyObjectByType<GameManager>();
                if (_gm == null)
                {
                    if (Elapsed < 8) return;
                    Fail("No se encontró GameManager en Play Mode tras 8s.");
                    return;
                }
                EnsureSessionDataExporter();
                SetPhase(Phase.ValidarCodigo);
                break;

            case Phase.ValidarCodigo:
            {
                var analytics = AnalyticsManager.Instance;
                if (analytics == null) { Fail("No hay AnalyticsManager en la escena — no se puede validar el código."); return; }
                Debug.Log($"[Reto1Test] Validando código de sesión '{CODIGO_TEST}' contra sesiones_config...");
                analytics.ValidarCodigoSesion(CODIGO_TEST);
                SetPhase(Phase.WaitCodigo);
                break;
            }

            case Phase.WaitCodigo:
            {
                if (Elapsed < 4.0) return;   // margen para el GET real a Supabase
                var analytics = AnalyticsManager.Instance;
                bool enlazado = analytics != null && !string.IsNullOrEmpty(analytics.idSesionActual);
                Debug.Log(enlazado
                    ? $"[Reto1Test] ✅ Código '{CODIGO_TEST}' ENLAZADO → sesion_id={analytics.idSesionActual}, nombreClaseActual='{analytics.nombreClaseActual}'."
                    : $"[Reto1Test] ⚠️ Código '{CODIGO_TEST}' NO se enlazó (idSesionActual sigue vacío) — la telemetría caerá en modo práctica libre. Revisar conexión/consulta a sesiones_config.");
                SetPhase(Phase.AsegurarReto1);
                break;
            }

            case Phase.AsegurarReto1:
            {
                if (_gm == null) _gm = Object.FindAnyObjectByType<GameManager>();
                if (_gm == null) { Fail("GameManager se volvió null."); return; }

                if (_gm.currentLevel != LevelType.OhmLaw)
                {
                    Debug.Log($"[Reto1Test] Nivel actual es {_gm.currentLevel}, forzando salto a Reto 1 (OhmLaw)...");
                    InvocarPrivado(_gm, "LoadLevel", 0);
                }
                else
                {
                    Debug.Log("[Reto1Test] Ya en Reto 1 (OhmLaw).");
                }

                _delivery = Object.FindFirstObjectByType<ComponentDeliverySystem>(FindObjectsInactive.Include);
                if (_delivery == null) { Fail("No hay ComponentDeliverySystem en escena."); return; }

                var tracker = Object.FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
                SessionState.SetInt(K_ERRORES_ANTES, tracker != null ? tracker.GetErrors() : -1);

                Debug.Log("[Reto1Test] [Explorador] Simulando medición con el multímetro en Reto 1 " +
                          "(NodeInteractable ya validado en esta sesión — no repite esa prueba aquí).");
                SetPhase(Phase.EnviarIncorrecto);
                break;
            }

            case Phase.EnviarIncorrecto:
            {
                int intento = SessionState.GetInt(K_INTENTO, 0) + 1;
                SessionState.SetInt(K_INTENTO, intento);

                Resistor faulty = BuscarResistorConFalla();
                if (faulty == null) { Fail("No hay resistor con falla activo en Reto 1."); return; }

                float valorIncorrecto = faulty.correctResistance * FACTOR_INCORRECTO;

                // RUTA REAL de producción (NO el atajo DebugSimularEntregaEInstalacion, que a propósito
                // NO llama RegisterWrongAttempt en su rama de rechazo — solo prueba tolerancia, no
                // telemetría). Esto es exactamente lo que pasa cuando el Técnico envía un valor y el
                // Explorador lo instala de verdad: ComponentDeliverySystem.PrepareForInstall +
                // OnExplorerInstalled, la misma ruta de red real (GameSession.EnviarComponente → tray →
                // instalación en ComponentSlot → OnExplorerInstalled).
                var slot = BuscarSlotDeResistor();
                if (slot == null) { Fail("No hay ComponentSlot de tipo Resistor en Reto 1."); return; }

                _delivery.PrepareForInstall(ComponentType.Resistor, valorIncorrecto);
                int erroresPrev = ContarErrores();
                _delivery.OnExplorerInstalled(slot);
                int erroresPost = ContarErrores();

                Debug.Log($"[Reto1Test] [Técnico→Explorador] Intento {intento}/3 — instala resistencia " +
                          $"INCORRECTA {valorIncorrecto:0.#}Ω (correcta={faulty.correctResistance}Ω) por la " +
                          $"RUTA REAL (OnExplorerInstalled) → errores {erroresPrev} → {erroresPost} " +
                          $"({(erroresPost > erroresPrev ? "✅ contado" : "⚠️ NO se contó")}).");

                SetPhase(Phase.EsperarEntreIntentos);
                break;
            }

            case Phase.EsperarEntreIntentos:
                if (Elapsed < 1.0) return;   // deja un frame/margen real entre intentos, como una partida real
                if (SessionState.GetInt(K_INTENTO, 0) < 3) { SetPhase(Phase.EnviarIncorrecto); }
                else { SetPhase(Phase.EnviarCorrecto); }
                break;

            case Phase.EnviarCorrecto:
            {
                int erroresAntes = SessionState.GetInt(K_ERRORES_ANTES, -1);
                int erroresAhora = ContarErrores();
                Debug.Log($"[Reto1Test] Errores registrados tras los 3 intentos incorrectos (ruta real): " +
                          $"{erroresAntes} → {erroresAhora} (delta esperado ≥ 3 si RegisterWrongAttempt se disparó cada vez).");

                Resistor faulty = BuscarResistorConFalla();
                if (faulty == null) { Fail("No hay resistor con falla activo al momento de enviar la correcta."); return; }

                var slot = BuscarSlotDeResistor();
                if (slot == null) { Fail("No hay ComponentSlot de tipo Resistor en Reto 1."); return; }

                _delivery.PrepareForInstall(ComponentType.Resistor, faulty.correctResistance);
                _delivery.OnExplorerInstalled(slot);
                bool ok = !faulty.hasFault;
                Debug.Log($"[Reto1Test] [Técnico→Explorador] Instala la resistencia CORRECTA " +
                          $"{faulty.correctResistance}Ω por la ruta real → " +
                          $"{(ok ? "✅ reparado" : "❌ INESPERADO: fue rechazada")}.");

                // Reto 1 también tiene un CircuitSwitch (arranca isOn=false, 1.000.000Ω — bloquea toda
                // corriente) que el Explorador debe cerrar como parte del armado físico. Sin esto, el
                // circuito nunca conduce corriente sin importar cuán correcto esté el resistor.
                foreach (var sw in Object.FindObjectsByType<CircuitSwitch>(FindObjectsInactive.Exclude))
                {
                    if (sw == null || sw.isOn) continue;
                    sw.Toggle();
                    Debug.Log($"[Reto1Test] [Explorador] Cierra el switch '{sw.name}' (isOn ahora={sw.isOn}).");
                }

                ForzarReevaluacion(_gm);
                SetPhase(Phase.WaitTransicion);
                break;
            }

            case Phase.WaitTransicion:
                if (Elapsed < 2.5) return;
                Debug.Log($"[Reto1Test] Nivel completado = {_gm.levelCompleted} (esperado: true).");
                SetPhase(Phase.WaitSupabase);
                break;

            case Phase.WaitSupabase:
                if (Elapsed < 8.0) return;   // margen para la corrutina diferida + el POST real a Supabase
                SetPhase(Phase.Finish);
                break;

            case Phase.Finish:
                Report();
                break;
        }
    }

    static Resistor BuscarResistorConFalla()
    {
        foreach (var r in Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
            if (r != null && r.nodeA != null && r.nodeB != null && r.hasFault)
                return r;
        return null;
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

    static void ForzarReevaluacion(GameManager gm)
    {
        if (gm == null) return;
        if (gm.circuit != null) gm.circuit.MarkDirty();
        foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
        {
            if (cm == null) continue;
            cm.MarkDirty();
            cm.ForceSimulate();
        }
    }

    static void InvocarPrivado(object obj, string metodo, params object[] args)
    {
        var m = obj.GetType().GetMethod(metodo, BindingFlags.NonPublic | BindingFlags.Instance);
        m?.Invoke(obj, args);
    }

    static void EnsureSessionDataExporter()
    {
        if (Object.FindAnyObjectByType<SessionDataExporter>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[Reto1Test] SessionDataExporter ya presente en la escena.");
            return;
        }
        var go = new GameObject("Test_SessionDataExporter_Reto1");
        var exporter = go.AddComponent<SessionDataExporter>();
        exporter.grupo = "Prueba Reto1 SEC-2VFN";
        Debug.Log("[Reto1Test] SessionDataExporter creado a mano (replica DashboardBootstrap).");
    }

    static void Fail(string reason)
    {
        Debug.LogError($"[Reto1Test] ✗ ABORTADO: {reason}");
        SetPhase(Phase.Finish);
    }

    static void Report()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(K_ACTIVE, false);
        SessionState.SetInt(K_PHASE, (int)Phase.Idle);

        var analytics = Object.FindAnyObjectByType<AnalyticsManager>();
        Debug.Log("═══════════════ [Reto1Test] REPORTE FINAL ═══════════════");
        Debug.Log($"sesion_id enlazado: {(analytics != null ? analytics.idSesionActual : "N/A")}");
        Debug.Log($"nombre_clase: {(analytics != null ? analytics.nombreClaseActual : "N/A")}");
        Debug.Log("Revisar arriba en este log las líneas [AnalyticsManager]/[SessionDataExporter] para el resultado exacto del POST a Supabase.");

        EditorApplication.isPlaying = false;

        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
#endif
