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
///   Abre esa URL en cualquier navegador (Chrome/Edge). Botones "Exportar a CSV" para Looker/Sheets.
/// </summary>
public static class DashboardBootstrap
{
    // ── Subida a Google Sheets (opcional) ──────────────────────────────────────
    // Pega aquí la URL /exec de tu Web App de Apps Script y pon ENABLE_SHEETS en true.
    // El TOKEN debe coincidir con el del script de Apps Script. GRUPO vacío = nombre del PC.
    const bool   ENABLE_SHEETS = true;
    const string SHEETS_URL    = "https://script.google.com/macros/s/AKfycbz09uUB8JyrnBlQjF9AVglLiqfpHvzUSVMrmjtTonaK4eNhUt86uoQWmldeFHnX3jeoLg/exec";
    const string SHEETS_TOKEN  = "TITA-2026-vQ8kZ3mN7rX5tD1c";   // ← ROTADO 2026-07-10 (el anterior quedó expuesto en git). Pon ESTE MISMO valor en 'const TOKEN' del Apps Script y RE-DESPLIEGA nueva versión
    // Vacío a propósito: si el Técnico no escribe un nombre en RoomCodeEntryUI (panel "Nombre del
    // grupo"), SessionDataExporter.SubirASheets cae a SystemInfo.deviceName — único por estación.
    // Antes esto era "1" fijo: cualquier estación que se saltara el campo subía como grupo "1",
    // indistinguible de las demás (bug encontrado en auditoría 2026-07-15).
    const string GRUPO         = "";

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
        // Sink opcional a la nube (Google Sheets vía Apps Script).
        exporter.subirASheets = ENABLE_SHEETS;
        exporter.webhookUrl   = SHEETS_URL;
        exporter.sheetsToken  = SHEETS_TOKEN;
        exporter.grupo        = GRUPO;

        var server   = go.AddComponent<DashboardServer>();
        server.dataExporter  = exporter;
        server.localhostOnly = true;   // solo este PC (sin permisos extra). Cambiar a false para toda la LAN.

        Debug.Log("[DashboardBootstrap] Panel docente iniciado. Mira la URL que imprime [DashboardServer] " +
                  "abajo y ábrela en el navegador.");
    }
}
