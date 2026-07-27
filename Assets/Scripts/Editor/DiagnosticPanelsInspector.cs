#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico puntual: lista todas las instancias de TecnicoDiagnosticoUI y el DiagnosticPanel de
/// Technicianworkstation en Tecnico.unity, con su ruta de jerarquía y posición, para confirmar si se
/// superponen en pantalla (reporte de "2 textos uno encima del otro").
/// </summary>
public static class DiagnosticPanelsInspector
{
    [MenuItem("Tools/TITA/Diagnóstico/Inspeccionar paneles de diagnóstico (Técnico)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Tecnico/Tecnico.unity", OpenSceneMode.Single);

        foreach (var ui in Object.FindObjectsByType<TecnicoDiagnosticoUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var rt = ui.texto != null ? ui.texto.GetComponent<RectTransform>() : null;
            Debug.Log($"##PANEL## TecnicoDiagnosticoUI en '{Path(ui.transform)}' activo={ui.gameObject.activeInHierarchy} " +
                      $"texto='{(ui.texto != null ? Path(ui.texto.transform) : "NULL")}' " +
                      $"anchoredPos={(rt != null ? rt.anchoredPosition.ToString() : "-")} " +
                      $"worldPos={(rt != null ? rt.position.ToString() : "-")}");
        }

        var tw = Object.FindAnyObjectByType<TechnicianWorkstation>(FindObjectsInactive.Include);
        if (tw != null)
        {
            var f = typeof(TechnicianWorkstation).GetField("txtDiagnostico");
            var txtDiag = f?.GetValue(tw) as TMPro.TMP_Text;
            var rt = txtDiag != null ? txtDiag.GetComponent<RectTransform>() : null;
            Debug.Log($"##PANEL## Technicianworkstation.txtDiagnostico en '{Path(tw.transform)}' activo={tw.gameObject.activeInHierarchy} " +
                      $"texto='{(txtDiag != null ? Path(txtDiag.transform) : "NULL")}' " +
                      $"anchoredPos={(rt != null ? rt.anchoredPosition.ToString() : "-")} " +
                      $"worldPos={(rt != null ? rt.position.ToString() : "-")}");
        }
        else
        {
            Debug.Log("##PANEL## Technicianworkstation NO encontrado en la escena.");
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
#endif
