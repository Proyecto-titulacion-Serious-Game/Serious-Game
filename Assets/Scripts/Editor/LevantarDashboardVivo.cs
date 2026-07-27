#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Prueba dirigida por el usuario (2026-07-25): abre la escena del Técnico en Play Mode y la deja
/// corriendo (sin auto-salir) para poder verificar desde AFUERA (curl/navegador) que
/// http://localhost:8080/ realmente muestra los datos reales que ya se confirmaron en Supabase —
/// no solo que la API de Supabase los recibió.
///
/// A diferencia de <see cref="Reto1TelemetriaConCodigoTest"/> y <see cref="FullPlaythroughSupabaseSend"/>,
/// este NO llama EditorApplication.Exit() — se queda vivo hasta que algo externo mate el proceso
/// Unity (o se presione Detener manualmente). Pensado para correr en background y curl-earlo aparte.
///
/// Menú: Tools → TITA → Pruebas → Levantar dashboard del Técnico y dejarlo vivo (Play Mode)
/// </summary>
public static class LevantarDashboardVivo
{
    const string ScenePath = "Assets/Scenes/Tecnico/Tecnico.unity";

    [MenuItem("Tools/TITA/Pruebas/Levantar dashboard del Técnico y dejarlo vivo (Play Mode)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("[LevantarDashboardVivo] Escena Tecnico abierta. Entrando a Play Mode (quedará corriendo)...");
        EditorApplication.isPlaying = true;
    }
}
#endif
