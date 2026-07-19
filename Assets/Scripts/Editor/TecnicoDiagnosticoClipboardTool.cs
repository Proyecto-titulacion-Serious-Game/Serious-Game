using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Consolida el diagnóstico del Técnico en UN solo lugar: el Clipboard_Canvas que ya trae en
/// Technician_Workstation.prefab (encabezados fijos "DIAGNÓSTICO" / "SIGUIENTE ACCIÓN" +
/// TMP_Diagnostico / TMP_AccionSiguiente). Antes existía además un "Diagnostico_Canvas" aparte,
/// creado directo en la escena y superpuesto físicamente sobre el mismo clipboard — dos paneles
/// de diagnóstico dibujándose uno encima del otro. Este tool:
///   1. Cablea TecnicoDiagnosticoUI (el componente que recibe el diagnóstico por red) sobre
///      Clipboard_Canvas, apuntando a sus TMP_Diagnostico/TMP_AccionSiguiente.
///   2. Borra el "Diagnostico_Canvas" suelto de la escena si todavía existe.
///
/// Menú: Tools → TITA → Reto 2 → Consolidar diagnóstico en Clipboard_Canvas
/// Callable en batchmode: -executeMethod TecnicoDiagnosticoClipboardTool.ConsolidarDiagnostico
/// </summary>
public static class TecnicoDiagnosticoClipboardTool
{
    const string PrefabPath = "Assets/Prefabs/Technician_Workstation.prefab";

    [MenuItem("Tools/TITA/Reto 2/Consolidar diagnóstico en Clipboard_Canvas")]
    public static void ConsolidarDiagnostico()
    {
        if (Application.isBatchMode)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Tecnico/Tecnico.unity");

        WirePrefab();
        RemoveOrphanCanvasFromScene();
    }

    static void WirePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform clipboardCanvas = FindDeep(root.transform, "Clipboard_Canvas");
            if (clipboardCanvas == null)
            {
                Aviso("Diagnóstico Técnico", $"No encontré 'Clipboard_Canvas' dentro de {PrefabPath}.");
                return;
            }

            Transform tmpDiag   = FindDeep(clipboardCanvas, "TMP_Diagnostico");
            Transform tmpAccion = FindDeep(clipboardCanvas, "TMP_AccionSiguiente");
            if (tmpDiag == null || tmpAccion == null)
            {
                Aviso("Diagnóstico Técnico", "Clipboard_Canvas no tiene TMP_Diagnostico/TMP_AccionSiguiente — no se tocó nada.");
                return;
            }

            var ui = clipboardCanvas.GetComponent<TecnicoDiagnosticoUI>();
            if (ui == null) ui = clipboardCanvas.gameObject.AddComponent<TecnicoDiagnosticoUI>();
            var tmpDiagText = tmpDiag.GetComponent<TMP_Text>();
            ui.texto       = tmpDiagText;
            ui.textoAccion = tmpAccion.GetComponent<TMP_Text>();

            if (ui.btnPaginaAnterior == null && ui.btnPaginaSiguiente == null)
                CrearPaginacion(clipboardCanvas, tmpDiag, tmpDiagText, ui);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Diagnóstico Técnico] TecnicoDiagnosticoUI cableado sobre Clipboard_Canvas " +
                      "(texto=TMP_Diagnostico, textoAccion=TMP_AccionSiguiente) con paginación.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Crea "&lt; Pág" / "Pág &gt;" + indicador "Pág X/Y", pegados a la esquina inferior de
    /// TMP_Diagnostico (se superponen a la última línea SOLO cuando de verdad hay 2+ páginas —
    /// TecnicoDiagnosticoUI los oculta por completo el resto del tiempo).</summary>
    static void CrearPaginacion(Transform clipboardCanvas, Transform tmpDiag, TMP_Text estilo, TecnicoDiagnosticoUI ui)
    {
        var rect = tmpDiag.GetComponent<RectTransform>();
        Vector2 anchorMin = rect.anchorMin, anchorMax = rect.anchorMax;
        Vector2 centro = rect.anchoredPosition;
        float bottomY = centro.y - rect.sizeDelta.y / 2f;
        float botonesY = bottomY + 8f;   // pegado a la esquina inferior de la caja, no debajo de ella

        var btnAnterior = CrearBotonPagina(clipboardCanvas, estilo, "Btn_DiagAnterior", "<",
            anchorMin, anchorMax, new Vector2(centro.x - 70f, botonesY));
        var btnSiguiente = CrearBotonPagina(clipboardCanvas, estilo, "Btn_DiagSiguiente", ">",
            anchorMin, anchorMax, new Vector2(centro.x + 70f, botonesY));
        var txtNumero = CrearTextoNumeroPagina(clipboardCanvas, estilo,
            anchorMin, anchorMax, new Vector2(centro.x, botonesY));

        ui.btnPaginaAnterior  = btnAnterior;
        ui.btnPaginaSiguiente = btnSiguiente;
        ui.txtNumeroPagina    = txtNumero;
    }

    static Button CrearBotonPagina(Transform parent, TMP_Text referenciaEstilo, string nombre,
        string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(16, 12);
        rt.anchoredPosition = anchoredPos;

        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.55f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRt = lblGO.GetComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = Vector2.zero; lblRt.offsetMax = Vector2.zero;
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 8;
        tmp.color = Color.white;
        if (referenciaEstilo != null && referenciaEstilo.font != null) tmp.font = referenciaEstilo.font;

        return btn;
    }

    static TMP_Text CrearTextoNumeroPagina(Transform parent, TMP_Text referenciaEstilo,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos)
    {
        var go = new GameObject("TMP_DiagPagina", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(50, 12);
        rt.anchoredPosition = anchoredPos;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 6;
        tmp.color = referenciaEstilo != null ? referenciaEstilo.color : Color.white;
        if (referenciaEstilo != null && referenciaEstilo.font != null) tmp.font = referenciaEstilo.font;

        return tmp;
    }

    static void RemoveOrphanCanvasFromScene()
    {
        GameObject found = null;
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var t = FindDeep(go.transform, "Diagnostico_Canvas");
            if (t != null) { found = t.gameObject; break; }
        }

        if (found == null)
        {
            Debug.Log("[Diagnóstico Técnico] No había 'Diagnostico_Canvas' suelto en la escena — nada que borrar.");
            return;
        }

        Object.DestroyImmediate(found);
        Debug.Log("[Diagnóstico Técnico] 'Diagnostico_Canvas' (el panel duplicado) eliminado de la escena.");

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        if (Application.isBatchMode)
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void Aviso(string titulo, string mensaje)
    {
        if (Application.isBatchMode) { Debug.Log($"[{titulo}] {mensaje}"); return; }
        EditorUtility.DisplayDialog(titulo, mensaje, "OK");
    }
}
