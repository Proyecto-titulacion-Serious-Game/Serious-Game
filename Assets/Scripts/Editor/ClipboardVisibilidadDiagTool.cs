using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

/// <summary>
/// Diagnóstico de por qué el clipboard del Técnico "no se muestra" en juego real. Abre
/// Tecnico.unity (la escena real, no una copia sintética) y revisa TODO lo que podría impedir
/// que se vea o se actualice:
///   1. Jerarquía completa desde Clipboard_Canvas hasta la raíz — ¿algún ancestro está inactivo?
///   2. ¿Hay más de un TecnicoDiagnosticoUI en la escena (duplicados que pisan al bueno)?
///   3. ¿texto/textoAccion apuntan a TMP_Text reales, activos, con Canvas ancestro activo?
///   4. ¿El GameObject dueño del componente está activo Y el componente enabled?
///   5. Croquis de ConnectionManager.rolAutomatico en esta escena (debe ser Tecnico).
///
/// Menú: Tools → TITA → Reto 2 → Diagnosticar visibilidad del clipboard
/// </summary>
public static class ClipboardVisibilidadDiagTool
{
    const string ScenePath = "Assets/Scenes/Tecnico/Tecnico.unity";

    [MenuItem("Tools/TITA/Reto 2/Diagnosticar visibilidad del clipboard")]
    public static void Diagnosticar()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var todos = Object.FindObjectsByType<TecnicoDiagnosticoUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[ClipboardDiag] TecnicoDiagnosticoUI encontrados en la escena: {todos.Length}");
        if (todos.Length == 0)
        {
            Debug.LogError("[ClipboardDiag] ✗ NO HAY NINGÚN TecnicoDiagnosticoUI en Tecnico.unity — " +
                            "el prefab Technician_Workstation en esta escena está desactualizado o no tiene el componente.");
        }
        else if (todos.Length > 1)
        {
            Debug.LogWarning($"[ClipboardDiag] ⚠ HAY {todos.Length} instancias — pueden pisarse entre sí (duplicado).");
        }

        foreach (var ui in todos)
        {
            Debug.Log($"[ClipboardDiag] ── Instancia en '{ui.gameObject.name}' ──");
            Debug.Log($"    componente.enabled = {ui.enabled}");
            Debug.Log($"    gameObject.activeSelf = {ui.gameObject.activeSelf}  activeInHierarchy = {ui.gameObject.activeInHierarchy}");

            // Jerarquía completa hasta la raíz.
            var t = ui.transform;
            string ruta = t.name + $" (active={t.gameObject.activeSelf})";
            var p = t.parent;
            bool algunAncestroInactivo = false;
            while (p != null)
            {
                bool activo = p.gameObject.activeSelf;
                if (!activo) algunAncestroInactivo = true;
                ruta = p.name + $" (active={activo}) / " + ruta;
                p = p.parent;
            }
            Debug.Log($"    Ruta completa: {ruta}");
            if (algunAncestroInactivo)
                Debug.LogError("    ✗ Al menos un ANCESTRO está INACTIVO → el clipboard NO se ve aunque el componente esté bien.");
            else
                Debug.Log("    ✓ Toda la cadena de ancestros está activa.");

            // texto / textoAccion
            ReportarTMP("texto", ui.texto);
            ReportarTMP("textoAccion", ui.textoAccion);

            // Canvas ancestro (World Space necesita esto para renderizar de verdad)
            var canvas = ui.GetComponentInParent<Canvas>(true);
            if (canvas == null)
                Debug.LogError("    ✗ NO hay ningún Canvas ancestro — un TMP_Text sin Canvas no se renderiza.");
            else
                Debug.Log($"    Canvas ancestro: '{canvas.name}' enabled={canvas.enabled} renderMode={canvas.renderMode} " +
                          $"gameObject.activeInHierarchy={canvas.gameObject.activeInHierarchy}");
        }

        // ── ConnectionManager de esta escena ──
        var cms = Object.FindObjectsByType<ConnectionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[ClipboardDiag] ConnectionManager encontrados: {cms.Length}");
        foreach (var cm in cms)
            Debug.Log($"    '{cm.gameObject.name}' activeSelf={cm.gameObject.activeSelf} rolAutomatico={cm.rolAutomatico} " +
                      $"esperarEntradaDeCodigo={cm.esperarEntradaDeCodigo}");

        // ── GameManager de esta escena (para MostrarUltimoDelRetoActual) ──
        var gms = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[ClipboardDiag] GameManager encontrados: {gms.Length}");
        foreach (var gm in gms)
            Debug.Log($"    '{gm.gameObject.name}' activeSelf={gm.gameObject.activeSelf}");

        Debug.Log("[ClipboardDiag] ===== FIN DIAGNÓSTICO =====");

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void ReportarTMP(string nombreCampo, TMP_Text t)
    {
        if (t == null)
        {
            Debug.LogError($"    ✗ {nombreCampo} = NULL (campo sin asignar en el Inspector).");
            return;
        }
        Debug.Log($"    {nombreCampo} = '{t.gameObject.name}' activeInHierarchy={t.gameObject.activeInHierarchy} " +
                   $"enabled={t.enabled} fontSize={t.fontSize} color={t.color} text=\"{Truncar(t.text)}\"");
    }

    static string Truncar(string s) => string.IsNullOrEmpty(s) ? "(vacío)" : s.Replace("\n", " \\n ").Substring(0, Mathf.Min(80, s.Length));
}
