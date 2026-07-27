using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Completa el subpanel "Cómo funciona el multímetro" dentro del glosario del manual
/// (Technician_Workstation.prefab → Panel_Glosario). ManualGlossaryToggle.cs ya tiene el campo
/// <c>subPanelInfoMultimetro</c> y la lógica para mostrarlo/ocultarlo (AbrirInfoMultimetro /
/// ToggleGlosario / CerrarGlosario), pero el GameObject en sí nunca se creó ni se asignó en el
/// Inspector — el botón "Cómo funciona el multímetro" existía pero no hacía nada visible.
///
/// Este tool crea "SubPanel_InfoMultimetro" (fondo + imagen Multimetro.jpg + texto) como hijo de
/// Panel_Glosario, ANTES de los botones en el orden de hijos (para que Button_CerrarGlosario y
/// Button_InfoMultimetro sigan clickeables por encima), y lo deja inactivo por defecto.
///
/// Menú: Tools → TITA → Reto 2 → Completar subpanel INFO del multímetro
/// Callable en batchmode: -executeMethod ManualInfoMultimetroSubPanelTool.CompletarSubPanel
/// </summary>
public static class ManualInfoMultimetroSubPanelTool
{
    const string PrefabPath = "Assets/Prefabs/Technician_Workstation.prefab";
    const string ImagenMultimetroGuid = "d15a93af5db74f198466f4907c858800"; // Assets/Imagenes/reto1/Multimetro.jpg

    [MenuItem("Tools/TITA/Reto 2/Completar subpanel INFO del multímetro")]
    public static void CompletarSubPanel()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform panelGlosario = FindDeep(root.transform, "Panel_Glosario");
            if (panelGlosario == null)
            {
                Aviso("No encontré 'Panel_Glosario' en el prefab.");
                return;
            }

            // ManualGlossaryToggle vive en OTRO GameObject (referencia a Panel_Glosario por campo),
            // no encima de Panel_Glosario mismo — buscar en todo el prefab, no con GetComponent().
            var toggle = root.GetComponentInChildren<ManualGlossaryToggle>(true);
            if (toggle == null)
            {
                Aviso("No encontré ManualGlossaryToggle en ningún GameObject del prefab — no se tocó nada.");
                return;
            }

            Transform yaExiste = FindDeep(panelGlosario, "SubPanel_InfoMultimetro");
            if (yaExiste != null)
            {
                toggle.subPanelInfoMultimetro = yaExiste.gameObject;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Aviso("'SubPanel_InfoMultimetro' ya existía — solo se re-cableó el campo por si estaba suelto.");
                return;
            }

            string imgPath = AssetDatabase.GUIDToAssetPath(ImagenMultimetroGuid);
            Sprite spriteMultimetro = string.IsNullOrEmpty(imgPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Sprite>(imgPath);
            if (spriteMultimetro == null)
                Debug.LogWarning("[InfoMultimetro] No pude cargar el sprite de Multimetro.jpg (guid " +
                                 ImagenMultimetroGuid + ") — el subpanel se crea igual, sin imagen.");

            var subPanel = CrearSubPanel(panelGlosario, spriteMultimetro);

            // Insertarlo ANTES de los botones (Button_CerrarGlosario / Button_InfoMultimetro) en el
            // orden de hermanos, para que esos botones sigan dibujándose ENCIMA (clickeables) aunque
            // el subpanel esté cubriendo el texto del glosario detrás.
            int idxTmpGlosario = -1;
            for (int i = 0; i < panelGlosario.childCount; i++)
                if (panelGlosario.GetChild(i).name == "TMP_Glosario") { idxTmpGlosario = i; break; }
            subPanel.transform.SetSiblingIndex(idxTmpGlosario >= 0 ? idxTmpGlosario + 1 : 0);

            toggle.subPanelInfoMultimetro = subPanel;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[InfoMultimetro] 'SubPanel_InfoMultimetro' creado y cableado en ManualGlossaryToggle.subPanelInfoMultimetro.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static GameObject CrearSubPanel(Transform parent, Sprite spriteMultimetro)
    {
        var panel = new GameObject("SubPanel_InfoMultimetro", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = new Vector2(6, 6);
        panelRt.offsetMax = new Vector2(-6, -6);

        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.12f, 0.22f, 0.98f);   // mismo tono que Panel_Glosario, casi opaco

        // --- Título ---
        var titulo = CrearTexto(panel.transform, "TMP_TituloInfoMultimetro",
            new Vector2(0, 375), new Vector2(260, 30),
            "COMO FUNCIONA EL MULTIMETRO", 14, FontStyles.Bold, new Color(0.4f, 0.8f, 1f));

        // --- Imagen ---
        var imgGO = new GameObject("Image_Multimetro", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imgGO.transform.SetParent(panel.transform, false);
        var imgRt = imgGO.GetComponent<RectTransform>();
        imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
        imgRt.sizeDelta = new Vector2(220, 220);
        imgRt.anchoredPosition = new Vector2(0, 240);
        var img = imgGO.GetComponent<Image>();
        img.sprite = spriteMultimetro;
        img.preserveAspect = true;

        // --- Texto informativo (modos, uso, cuándo usarlo) ---
        string cuerpo =
            "El multimetro es un panel fijo en la\npared (uno por reto) que el Explorador\n" +
            "(VR) usa para medir el circuito con 2\npuntas por cable: ROJA (mano derecha) y\n" +
            "NEGRA (mano izquierda, referencia).\n\n" +
            "TIENE 3 MODOS (boton fisico del panel):\n" +
            "1. VOLTAJE (DC): diferencia de\n   potencial entre las 2 puntas.\n" +
            "2. CORRIENTE (DC): corriente que\n   atraviesa el componente.\n" +
            "3. RESISTENCIA (OHMS): valor del\n   componente entre las puntas.\n\n" +
            "COMO SE USA: agarra el MANGO de cada\n" +
            "punta (el panel esta fijo, no se agarra),\n" +
            "elige el modo con el boton del panel,\n" +
            "acerca cada punta a un nodo y lee la\n" +
            "pantalla.\n\n" +
            "CUANDO USARLO: Reto 1 (confirmar\n" +
            "voltaje), Retos 2 y 3 (diagnosticar\n" +
            "una rama), Reto 4 (ademas medir la\n" +
            "RESISTENCIA en modo OHMS antes de\n" +
            "validar).";
        var cuerpoTxt = CrearTexto(panel.transform, "TMP_InfoMultimetro",
            new Vector2(0, -110), new Vector2(270, 430),
            cuerpo, 10, FontStyles.Normal, new Color(0.92f, 0.95f, 1f));
        cuerpoTxt.alignment = TextAlignmentOptions.TopLeft;
        cuerpoTxt.enableAutoSizing = true;
        cuerpoTxt.fontSizeMin = 6;
        cuerpoTxt.fontSizeMax = 10;

        // Oculto por defecto: solo AbrirInfoMultimetro() (botón "Cómo funciona el multímetro") lo
        // activa; sin esto se vería SIEMPRE que se abre el glosario, tapando el texto normal.
        panel.SetActive(false);
        return panel;
    }

    static TMP_Text CrearTexto(Transform parent, string nombre, Vector2 anchoredPos, Vector2 size,
        string texto, float fontSize, FontStyles estilo, Color color)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = texto;
        tmp.fontSize = fontSize;
        tmp.fontStyle = estilo;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        // Copiar la fuente de un texto hermano del glosario para que se vea igual y renderice
        // (si no se asigna ninguna, TMP usa la fuente default del proyecto y puede verse distinto).
        var hermano = parent.parent != null ? parent.parent.GetComponentInChildren<TMP_Text>(true) : null;
        if (hermano != null && hermano.font != null) tmp.font = hermano.font;

        return tmp;
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

    static void Aviso(string mensaje)
    {
        if (Application.isBatchMode) { Debug.Log("[InfoMultimetro] " + mensaje); return; }
        EditorUtility.DisplayDialog("Info Multímetro", mensaje, "OK");
    }
}
