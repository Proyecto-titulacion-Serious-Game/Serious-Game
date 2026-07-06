using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// Coloca el diagnóstico del circuito en el clipboard/HUD del TÉCNICO.
/// - Si tienes un TMP_Text seleccionado (en el clipboard o el HUD) → le engancha TecnicoDiagnosticoUI.
/// - Si no, crea un Canvas WorldSpace + texto sobre el clipboard del Técnico (ajusta pos/escala a mano).
///
/// Correr en la escena del Técnico (Tecnico.unity).
/// Menú: Tools → TITA → Reto 2 → Diagnóstico en clipboard del Técnico
/// </summary>
public static class TecnicoDiagnosticoClipboardTool
{
    [MenuItem("Tools/TITA/Reto 2/Diagnóstico en clipboard del Técnico")]
    public static void Crear()
    {
        TMP_Text txt = null;
        GameObject host = null;

        // 1) ¿Hay un TMP_Text seleccionado? → usarlo (lo más fiable: tú eliges dónde va).
        if (Selection.activeGameObject != null)
            txt = Selection.activeGameObject.GetComponentInChildren<TMP_Text>(true);

        if (txt != null)
        {
            host = txt.gameObject;
        }
        else
        {
            // 2) Añadir el texto DENTRO del Canvas del clipboard del Técnico (ya está bien posicionado
            //    en el tablero) → hereda posición/escala correctas. NO crear un canvas flotante aparte.
            Canvas canvas = null;
            var clip = Object.FindAnyObjectByType<ClipboardZoom>(FindObjectsInactive.Include);
            if (clip != null) canvas = clip.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
                    if (c != null && c.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(c) && c.name.Contains("Clipboard"))
                        { canvas = c; break; }

            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Diagnóstico Técnico",
                    "No encontré el Canvas del clipboard del Técnico.\n" +
                    "Selecciona un TMP_Text (donde quieras el diagnóstico) y vuelve a correr el tool.", "OK");
                return;
            }

            var txtGO = new GameObject("TMP_Diagnostico", typeof(TextMeshProUGUI));
            txtGO.transform.SetParent(canvas.transform, false);
            var tmp = txtGO.GetComponent<TextMeshProUGUI>();
            tmp.text      = "Esperando estado del circuito…";
            tmp.fontSize  = 22;
            tmp.color     = Color.black;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            // Copiar la fuente de un texto hermano del clipboard para que se vea igual y renderice.
            var hermano = canvas.GetComponentInChildren<TMP_Text>(true);
            if (hermano != null) { tmp.font = hermano.font; tmp.fontSharedMaterial = hermano.fontSharedMaterial; tmp.color = hermano.color; tmp.fontSize = hermano.fontSize; }
            // Colocar en la mitad inferior del clipboard (ajústalo a mano si pisa otro texto).
            var trt = tmp.rectTransform;
            trt.anchorMin = new Vector2(0.06f, 0.05f);
            trt.anchorMax = new Vector2(0.94f, 0.42f);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            txt  = tmp;
            host = txtGO;
            Undo.RegisterCreatedObjectUndo(txtGO, "Crear Diagnóstico Técnico");
        }

        var ui = host.GetComponent<TecnicoDiagnosticoUI>();
        if (ui == null) ui = host.AddComponent<TecnicoDiagnosticoUI>();
        ui.texto = txt;
        EditorUtility.SetDirty(ui);
        if (txt != null) EditorUtility.SetDirty(txt);
        Selection.activeGameObject = host;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(host.scene);

        EditorUtility.DisplayDialog("Diagnóstico Técnico",
            "Listo — TecnicoDiagnosticoUI enganchado.\n\n" +
            (host.name == "TMP_Diagnostico"
                ? "Se creó 'TMP_Diagnostico' DENTRO del Canvas del clipboard (posición correcta heredada). " +
                  "Si pisa otro texto, mueve su RectTransform en el Inspector.\n\n" +
                  "⚠ Borra el 'Diagnostico_Canvas' flotante anterior si aún existe."
                : "Usé el TMP_Text seleccionado.") +
            "\n\nEl Explorador envía el resumen por red; el Técnico lo verá aquí.", "OK");
    }
}
