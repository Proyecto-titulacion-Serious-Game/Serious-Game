using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verificación headless de la tubería de métricas (PerformanceTracker → nota 0-10 →
/// SessionDataExporter → JSON/historial en disco), SIN disparar ObjectiveSystem.HandleGameCompleted
/// (evita invocar AnalyticsManager.EnviarMetricas / Supabase y el webhook de Sheets — esta prueba NO
/// debe escribir filas de prueba en una tabla o planilla real).
///
/// Menú: Tools → TITA → Metricas → Test pipeline (headless)
/// </summary>
public static class MetricsPipelineHeadlessTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Metricas/Test pipeline (headless)")]
    public static void Run()
    {
        int fails = 0;
        Debug.Log("===== METRICAS — TEST PIPELINE (headless, sin tocar Supabase/Sheets) =====");

        // ── 1) PerformanceTracker.CalcularNota10 — función pura, sin escena ──
        fails += TestNota(tiempo: 150f, errores: 1, exito: true, limite: 600f, esperado: 9.0f);
        fails += TestNota(tiempo: 0f,   errores: 0, exito: true, limite: 600f, esperado: 10.0f);
        fails += TestNota(tiempo: 600f, errores: 0, exito: true, limite: 600f, esperado: 5.0f);
        fails += TestNota(tiempo: 50f,  errores: 10,exito: true, limite: 600f, esperado: 5.0f); // 5pts tiempo + max(0,5-10)=0
        fails += TestNota(tiempo: 50f,  errores: 0, exito: false,limite: 600f, esperado: 4.0f); // tope 4.0 si no exito

        // ── 2) PerformanceTracker en escena real: acumulación de 4 registros (uno por reto) ──
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var tracker = Object.FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
        if (tracker == null)
        {
            Debug.LogError("[MetricsTest] No hay PerformanceTracker en Explorador.unity.");
            Finish(1); return;
        }

        var tHandleLevelCompleted = typeof(PerformanceTracker).GetMethod("HandleLevelCompleted",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (tHandleLevelCompleted == null)
        {
            Debug.LogError("[MetricsTest] No encontré PerformanceTracker.HandleLevelCompleted (privado) por reflexión.");
            Finish(1); return;
        }

        var niveles = new[] { LevelType.OhmLaw, LevelType.Parallel, LevelType.Mixed, LevelType.Arduino };
        for (int i = 0; i < niveles.Length; i++)
        {
            tracker.ResetTracker();
            tracker.AddError("TestError", $"detalle de prueba reto {i + 1}");
            tHandleLevelCompleted.Invoke(tracker, new object[] { niveles[i], true });
        }

        var records = tracker.GetAllRecords();
        if (records.Count != 4)
        {
            Debug.LogError($"[MetricsTest] Se esperaban 4 registros acumulados, hay {records.Count}.");
            fails++;
        }
        else
        {
            Debug.Log("[MetricsTest] 4 registros acumulados correctamente:");
            foreach (var r in records)
                Debug.Log($"[MetricsTest]   - {SessionDataExporter.LevelName(r.level)}: nota={r.nota} errores={r.errors} " +
                          $"exito={r.success} detalles=[{string.Join(", ", r.detalles)}]");

            foreach (var r in records)
                if (r.errors != 1 || !r.success || r.detalles == null || r.detalles.Length != 1)
                {
                    Debug.LogError($"[MetricsTest] Registro de {SessionDataExporter.LevelName(r.level)} con forma inesperada.");
                    fails++;
                }
        }

        // ── 3) SessionDataExporter: guardado local (JSON + historial) SIN pasar por HandleSessionEnded ──
        // Construye un SessionResult sintético y llama DIRECTO a SaveToDisk/SaveHistory por reflexión,
        // evitando el camino HandleSessionEnded → AnalyticsManager.EnviarMetricas (Supabase) / Sheets.
        var exporter = Object.FindAnyObjectByType<SessionDataExporter>(FindObjectsInactive.Include);
        if (exporter == null)
        {
            Debug.LogWarning("[MetricsTest] No hay SessionDataExporter en la escena — se omite el paso 3 (guardado en disco).");
        }
        else
        {
            var tExporter = typeof(SessionDataExporter);
            var fData    = tExporter.GetField("_data",    BindingFlags.NonPublic | BindingFlags.Instance);
            var fHistory = tExporter.GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            var mSaveToDisk   = tExporter.GetMethod("SaveToDisk",   BindingFlags.NonPublic | BindingFlags.Instance);
            var mSaveHistory  = tExporter.GetMethod("SaveHistory",  BindingFlags.NonPublic | BindingFlags.Instance);

            if (fData == null || fHistory == null || mSaveToDisk == null || mSaveHistory == null)
            {
                Debug.LogError("[MetricsTest] No pude acceder por reflexión a _data/_history/SaveToDisk/SaveHistory.");
                fails++;
            }
            else
            {
                var data = fData.GetValue(exporter) as SessionExportData;
                data.hasResult = true;
                data.state     = "Sesión finalizada (PRUEBA HEADLESS — no enviada a Supabase/Sheets)";
                data.timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                data.summary   = new SessionResultDto
                {
                    totalScore = 700, maxScore = 900, scorePercent = 700f / 900f,
                    totalErrors = 4, totalTimeSeconds = 600f,
                    evaluation = "[PRUEBA HEADLESS]"
                };
                var dto = new LevelRecordDto[records.Count];
                for (int i = 0; i < records.Count; i++) dto[i] = new LevelRecordDto(records[i]);
                data.records = dto;
                fData.SetValue(exporter, data);

                mSaveToDisk.Invoke(exporter, null);
                mSaveHistory.Invoke(exporter, null);

                string jsonPath = Path.Combine(Application.persistentDataPath, "session_results.json");
                string histPath = Path.Combine(Application.persistentDataPath, "sessions_history.json");

                if (!File.Exists(jsonPath)) { Debug.LogError($"[MetricsTest] No se creó {jsonPath}"); fails++; }
                else
                {
                    string txt = File.ReadAllText(jsonPath);
                    bool ok = txt.Contains("\"hasResult\": true") || txt.Contains("hasResult") ;
                    Debug.Log($"[MetricsTest] session_results.json ({txt.Length} chars) en {jsonPath} — contiene 'hasResult': {ok}");
                    if (!txt.Contains("PRUEBA HEADLESS")) { Debug.LogError("[MetricsTest] El JSON no refleja los datos sintéticos escritos."); fails++; }
                }

                if (!File.Exists(histPath)) { Debug.LogError($"[MetricsTest] No se creó {histPath}"); fails++; }
                else
                    Debug.Log($"[MetricsTest] sessions_history.json presente en {histPath} ({new FileInfo(histPath).Length} bytes).");

                Debug.Log("[MetricsTest] NOTA: no se invocó HandleSessionEnded → AnalyticsManager.EnviarMetricas " +
                          "NUNCA se llamó. Ninguna fila de prueba fue (ni será) enviada a Supabase ni a Sheets.");
            }
        }

        Debug.Log(fails == 0
            ? "\n[MetricsTest] ===== RESULTADO: ✓ Pipeline de métricas (nota, acumulación, guardado local) OK ====="
            : $"\n[MetricsTest] ===== RESULTADO: ✗ {fails} fallo(s) — ver arriba =====");

        Finish(fails == 0 ? 0 : 1);
    }

    static int TestNota(float tiempo, int errores, bool exito, float limite, float esperado)
    {
        float nota = PerformanceTracker.CalcularNota10(tiempo, errores, exito, limite);
        bool ok = Mathf.Abs(nota - esperado) < 0.05f;
        Debug.Log($"[MetricsTest] CalcularNota10(t={tiempo}, err={errores}, exito={exito}, limite={limite}) = {nota} " +
                  $"(esperado {esperado}) → {(ok ? "OK" : "FALLO")}");
        return ok ? 0 : 1;
    }

    static void Finish(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
