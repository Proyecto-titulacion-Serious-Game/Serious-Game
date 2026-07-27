using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Arranca el PANEL DOCENTE (SessionDataExporter + DashboardServer) automáticamente al entrar a Play,
/// SOLO en el Técnico (PC host). Así el dashboard de métricas funciona sin tener que añadir los
/// componentes a la escena a mano (no estaban puestos en ninguna escena → el panel nunca corría).
///
/// El pipeline de datos (GameManager → PerformanceTracker → ObjectiveSystem) ya vive en la escena del
/// Técnico, así que en cuanto esto arranca el servidor, el panel ya muestra tiempos/errores/resultados.
///
/// ▶ DÓNDE VER LAS MÉTRICAS: al dar Play, la consola imprime
///       [DashboardServer] Panel docente en: http://localhost:8080/
///   Abre esa URL en cualquier navegador (Chrome/Edge). Botón "Exportar a CSV" para Looker/Sheets
///   (exportación manual — no hay sink automático a Google Sheets, ver Supabase abajo).
/// </summary>
public static class DashboardBootstrap
{
    // ── Etiqueta de grupo (usada en el payload de Supabase) ────────────────────
    // Vacío a propósito: si el Técnico no escribe un nombre en RoomCodeEntryUI (panel "Nombre del
    // grupo"), EnviarUnRetoASupabase cae a SystemInfo.deviceName — único por estación.
    // Antes esto era "1" fijo: cualquier estación que se saltara el campo subía como grupo "1",
    // indistinguible de las demás (bug encontrado en auditoría 2026-07-15).
    const string GRUPO         = "";

    // ── Envío a Supabase (telemetría en la nube, tabla telemetria_estudiantes) ──
    // BUG REAL encontrado 2026-07-24: AnalyticsManager vivía SOLO en la escena del Explorador
    // (pre-colocado a mano), pero el ÚNICO código que lo invoca (SessionDataExporter) se
    // bootstrapea aquí SOLO en el Técnico — en cualquier sesión real de 2 máquinas, la máquina
    // con SessionDataExporter (Técnico) NUNCA tenía AnalyticsManager, y la máquina con
    // AnalyticsManager (Explorador) NUNCA tenía SessionDataExporter: AnalyticsManager.Instance
    // daba null siempre y el envío a Supabase era un no-op silencioso, sin importar cuántos
    // retos se completaran. Se instancia aquí, junto con el resto del panel, en la MISMA
    // máquina/GameObject que sí dispara el envío.
    const string SUPABASE_URL      = "https://duigabepvjpzmqllryiw.supabase.co/rest/v1/telemetria_estudiantes";
    const string SUPABASE_ANON_KEY = "sb_publishable_HHNr73IdlS1Ew5IVA6yDRg_lR35ZmT_";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // El Explorador corre en la Quest (Android) → ahí NO se levanta el panel.
        if (Application.platform == RuntimePlatform.Android) return;

        // Solo en el rol Técnico: su escena se llama "Tecnico". (Evita levantarlo en el Explorador PCVR.)
        bool esTecnico = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name.Contains("Tecnico")) { esTecnico = true; break; }
        if (!esTecnico) return;

        // Evitar duplicados si ya estuviera puesto en la escena.
        if (Object.FindAnyObjectByType<DashboardServer>(FindObjectsInactive.Include) != null) return;

        var go = new GameObject("TITA_Dashboard");
        Object.DontDestroyOnLoad(go);

        var exporter = go.AddComponent<SessionDataExporter>();
        exporter.grupo = GRUPO;

        var server   = go.AddComponent<DashboardServer>();
        server.dataExporter  = exporter;
        server.localhostOnly = true;   // solo este PC (sin permisos extra). Cambiar a false para toda la LAN.

        // Solo si no hay ya uno (p.ej. si alguna vez se vuelve a colocar a mano en una escena).
        if (Object.FindAnyObjectByType<AnalyticsManager>(FindObjectsInactive.Include) == null)
        {
            var analytics = go.AddComponent<AnalyticsManager>();
            analytics.supabaseUrlTelemetria = SUPABASE_URL;
            analytics.supabaseAnonKey       = SUPABASE_ANON_KEY;
        }

        Debug.Log("[DashboardBootstrap] Panel docente iniciado. Mira la URL que imprime [DashboardServer] " +
                  "abajo y ábrela en el navegador.");
    }
}
