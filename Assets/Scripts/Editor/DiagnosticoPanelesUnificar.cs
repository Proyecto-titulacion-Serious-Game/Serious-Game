#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Deja UN SOLO panel de diagnóstico visible para el Técnico en Tecnico.unity — hoy hay dos
/// superpuestos casi en el mismo punto ("Clipboard/Diagnostico_Canvas/Diagnostico_Text" del sistema
/// en red vía GameSession, y "Clipboard/Clipboard_Canvas/TMP_Diagnostico" del panel local de
/// TechnicianWorkstation) más una tercera copia de TecnicoDiagnosticoUI mal enganchada al hint de
/// scroll del manual (le pisaba el texto). Se conserva el canal en red (funciona cruzando 2 procesos
/// distintos, PC+Quest) y se apaga el resto.
/// </summary>
public static class DiagnosticoPanelesUnificar
{
    [MenuItem("Tools/TITA/Diagnóstico/Unificar paneles del Técnico (dejar solo 1)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Tecnico/Tecnico.unity", OpenSceneMode.Single);

        int apagados = 0, removidos = 0;

        // 1) Apagar el panel local duplicado (Clipboard_Canvas) — se queda solo Diagnostico_Canvas.
        foreach (var tw in Object.FindObjectsByType<TechnicianWorkstation>(FindObjectsInactive.Include))
        {
            var clipboardCanvas = tw.transform.Find("Clipboard/Clipboard_Canvas");
            if (clipboardCanvas != null && clipboardCanvas.gameObject.activeSelf)
            {
                clipboardCanvas.gameObject.SetActive(false);
                apagados++;
                Debug.Log($"[DiagnosticoPanelesUnificar] Apagado: {Path(clipboardCanvas)}");
            }
        }

        // 2) Quitar la instancia de TecnicoDiagnosticoUI mal enganchada (pisaba TMP_ScrollHint del manual).
        //    Se identifica por NO apuntar a un texto dentro de "Diagnostico_Canvas".
        foreach (var ui in Object.FindObjectsByType<TecnicoDiagnosticoUI>(FindObjectsInactive.Include))
        {
            if (ui.texto == null) continue;
            if (Path(ui.texto.transform).Contains("Diagnostico_Canvas")) continue;   // la correcta, no tocar

            Debug.Log($"[DiagnosticoPanelesUnificar] Removiendo TecnicoDiagnosticoUI mal enganchada en " +
                      $"{Path(ui.transform)} (apuntaba a '{Path(ui.texto.transform)}').");
            Object.DestroyImmediate(ui);
            removidos++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[DiagnosticoPanelesUnificar] ✓ Listo. Paneles apagados={apagados} Componentes removidos={removidos}");

        if (Application.isBatchMode) EditorApplication.Exit((apagados >= 1 && removidos >= 1) ? 0 : 1);
    }

    static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
#endif
