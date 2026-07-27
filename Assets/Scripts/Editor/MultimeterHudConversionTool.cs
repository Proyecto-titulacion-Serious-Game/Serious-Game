#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pedido explícito del usuario (2026-07-25): quitar el modelo 3D del multímetro — el "panel de
/// pared" de la sesión anterior seguía siendo Multimeter_VR_Art (malla, sondas, cables), solo
/// atornillado en vez de agarrable — y reemplazarlo por un HUD/Canvas: texto con voltaje/
/// corriente/resistencia + un botón para cambiar de modo.
///
/// Por qué esto NO rompe la medición: NodeInteractable (asigna un ElectricalNode a la punta roja/
/// negra vía Multimeter.SetRedNode/SetBlackNode) ya funciona apuntando el control XR y apretando
/// el gatillo — un XRSimpleInteractable propio de cada nodo/slot, independiente de la geometría
/// del multímetro. Confirmado: 38 NodeInteractable en Explorador.unity, cubriendo Retos 1-3
/// (nodos fijos) Y Reto 4 (23 nodos de la protoboard). Quitar las puntas/cuerpo NO rompe nada.
///
/// INTENTO 1 (revertido): armé el panel en ExplorerHUD.prefab → Panel_Multimetro, que ya existía
/// a medio construir. Investigando encontré que ExplorerHUD está INACTIVO en Explorador.unity —
/// es un prototipo abandonado (su MultimeterUI nunca tuvo 'multimeter' asignado y nunca se
/// terminó). El HUD REALMENTE activo y usado por el juego es 'VictoryHUD_Canvas' (ScreenSpace
/// Overlay, confirmado por la cadena de m_IsActive hasta la raíz), donde vive PlayerFeedbackUI
/// (instrucciones + "¡FELICIDADES!"). Por eso el panel del multímetro se construye ahí.
///
/// Por qué el botón NO es un UI Button en ese Canvas: al ser ScreenSpaceOverlay (no WorldSpace),
/// TrackedDeviceGraphicRaycaster no puede recibir el rayo del control XR (ese raycaster necesita
/// un plano en el mundo — por eso XRBootManager solo arregla canvases WorldSpace). En vez de
/// inventar un mecanismo nuevo sin probar, se reutiliza Mode_Button/MultimeterModeButton.cs — el
/// botón físico (XRSimpleInteractable) que YA funcionaba en el modelo 3D.
///
/// INTENTO 2 (revertido): reparentar Mode_Button de cada panel a su Reto_Zone vía
/// Transform.SetParent(). El log del propio tool reportaba éxito, pero una verificación con la
/// escena RECARGADA (VerificarMultimetroHud.cs) mostró que el hijo seguía adentro del panel
/// original — reparentar un hijo de un PrefabInstance a otra jerarquía no persiste bien al
/// guardar (limitación conocida de prefabs anidados en Unity, no un bug de este script).
///
/// SOLUCIÓN FINAL: no se mueve nada. Cada Multimeter_Panel_RetoX se REACTIVA (su raíz no tiene
/// mesh propio, solo era collider+scripts) pero se vacía: sus otros 8 hijos (cuerpo, pantalla,
/// sondas, cables) se desactivan, y el Multimeter/collider/scripts viejos de la raíz se
/// DESTRUYEN (no solo se deshabilitan — un componente deshabilitado en un GameObject activo
/// igual aparece en FindAnyObjectByType, así que un Multimeter viejo "apagado" seguiría
/// compitiendo con el nuevo del HUD). Solo Mode_Button queda visible/activo, en la posición que
/// ya tenía (MultimeterPanelSmartPlacement).
///
/// Qué hace:
///   1. En Explorador.unity → VictoryHUD_Canvas: crea Panel_Multimetro (clona TMP_Instruccion
///      como plantilla de fuente/estilo) con TMP_Modo/TMP_Voltaje/TMP_Corriente/TMP_Estado/
///      TMP_ProbeRoja/TMP_ProbeNegra + fondo, UN Multimeter (reemplaza los 4 que vivían en los
///      paneles de pared) y MultimeterUI wireado.
///   2. Cada Multimeter_Panel_RetoX: reactivado pero vaciado (ver arriba) — solo Mode_Button
///      sigue visible, apuntado al Multimeter nuevo. Multimeter_VR_Art (el original, no ligado a
///      ningún reto) se confirma desactivado — no se toca ni se borra.
///
/// Menú: Tools → TITA → Multímetro → Convertir a HUD (quitar modelo 3D)
/// </summary>
public static class MultimeterHudConversionTool
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Multímetro/Convertir a HUD (quitar modelo 3D)")]
    public static void Convert()
    {
        // Fase A: crear/asegurar el panel + Multimeter nuevo, y guardar. Corrido en su propio
        // ciclo de guardado (en vez de encadenar todo en una sola pasada) porque una corrida
        // anterior mostró que asignar 'button.multimeter' en la MISMA pasada donde también se
        // hacía DestroyImmediate de componentes viejos en los 4 paneles no persistía al guardar
        // (el log de esa corrida decía éxito, pero recargar la escena mostraba multimeter=NULL) —
        // mismo tipo de trampa que el bug de "fake null" ya documentado en este proyecto para
        // PrefabUtility.UnloadPrefabContents. Separar en dos guardados evita el problema.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var multimeter = BuildHudPanel();
        if (multimeter == null)
        {
            Debug.LogError("[MultimeterHudConversionTool] ✗ Fase A falló — revisar errores arriba.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        SaveActiveScene();
        Debug.Log("[MultimeterHudConversionTool] Fase A guardada (Panel_Multimetro + Multimeter). Recargando para la Fase B...");

        // Fase B: recargar la escena desde disco (referencia fresca al Multimeter, no la que
        // sobrevivió a los DestroyImmediate de la Fase A) y recién ahí vaciar los 4 paneles 3D.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var freshMultimeter = Object.FindAnyObjectByType<Multimeter>();
        if (freshMultimeter == null)
        {
            Debug.LogError("[MultimeterHudConversionTool] ✗ Fase B: no se encontró el Multimeter recién guardado.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        int panelsEmptied = RewireModeButtons(freshMultimeter);
        int deactivated = DeactivateOldVrArt();

        bool success = panelsEmptied == 4 && deactivated >= 0;
        if (success) SaveActiveScene();

        Debug.Log(success
            ? $"[MultimeterHudConversionTool] ✓ HUD del multímetro listo en VictoryHUD_Canvas/Panel_Multimetro. {panelsEmptied}/4 paneles 3D vaciados (solo Mode_Button activo en cada uno)."
            : "[MultimeterHudConversionTool] ✗ Fase B incompleta — revisar errores arriba.");

        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }

    static void SaveActiveScene()
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);
    }

    // ─────────────────────────────────────────────
    //  1. VictoryHUD_Canvas → Panel_Multimetro
    // ─────────────────────────────────────────────
    static Multimeter BuildHudPanel()
    {
        var canvasGO = FindInSceneByName("VictoryHUD_Canvas");
        if (canvasGO == null)
        {
            Debug.LogError("[MultimeterHudConversionTool] No se encontró 'VictoryHUD_Canvas' en la escena.");
            return null;
        }

        var tmpTemplate = FindDeep(canvasGO.transform, "TMP_Instruccion");
        if (tmpTemplate == null)
        {
            Debug.LogError("[MultimeterHudConversionTool] No se encontró 'TMP_Instruccion' (plantilla de fuente/estilo).");
            return null;
        }

        var panel = FindDeep(canvasGO.transform, "Panel_Multimetro");
        if (panel == null)
        {
            var go = new GameObject("Panel_Multimetro", typeof(RectTransform));
            go.transform.SetParent(canvasGO.transform, false);
            panel = go.transform;

            var rt = (RectTransform)panel;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 60);
            rt.sizeDelta = new Vector2(700, 380);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.07f, 0.16f, 0.75f);
        }

        var tmpModo = FindOrCloneTmp(panel, tmpTemplate, "TMP_Modo",
            pos: new Vector2(0, 140), size: new Vector2(600, 40), fontSize: 22,
            color: new Color(0.9f, 0.75f, 0.25f), alignment: TextAlignmentOptions.Center, text: "DC VOLTAGE");

        var tmpVoltaje = FindOrCloneTmp(panel, tmpTemplate, "TMP_Voltaje",
            pos: new Vector2(0, 65), size: new Vector2(650, 90), fontSize: 56,
            color: new Color(0.4f, 1f, 0.5f), alignment: TextAlignmentOptions.Center, text: "—.— V");

        var tmpCorriente = FindOrCloneTmp(panel, tmpTemplate, "TMP_Corriente",
            pos: new Vector2(0, -15), size: new Vector2(500, 50), fontSize: 34,
            color: new Color(0.55f, 0.85f, 1f), alignment: TextAlignmentOptions.Center, text: "—.— mA");

        var tmpEstado = FindOrCloneTmp(panel, tmpTemplate, "TMP_Estado",
            pos: new Vector2(0, -62), size: new Vector2(600, 36), fontSize: 20,
            color: new Color(0.85f, 0.85f, 0.85f), alignment: TextAlignmentOptions.Center, text: "SIN CONTACTO");

        var tmpProbeRoja = FindOrCloneTmp(panel, tmpTemplate, "TMP_ProbeRoja",
            pos: new Vector2(-170, -115), size: new Vector2(320, 36), fontSize: 16,
            color: new Color(1f, 0.5f, 0.5f), alignment: TextAlignmentOptions.Left, text: "(+) —");

        var tmpProbeNegra = FindOrCloneTmp(panel, tmpTemplate, "TMP_ProbeNegra",
            pos: new Vector2(170, -115), size: new Vector2(320, 36), fontSize: 16,
            color: new Color(0.6f, 0.7f, 1f), alignment: TextAlignmentOptions.Right, text: "(-) —");

        var multimeter = panel.GetComponent<Multimeter>();
        if (multimeter == null) multimeter = panel.gameObject.AddComponent<Multimeter>();

        var ui = panel.GetComponent<MultimeterUI>();
        if (ui == null) ui = panel.gameObject.AddComponent<MultimeterUI>();

        ui.multimeter    = multimeter;
        ui.txtModo       = tmpModo.GetComponent<TextMeshProUGUI>();
        ui.txtVoltaje    = tmpVoltaje.GetComponent<TextMeshProUGUI>();
        ui.txtCorriente  = tmpCorriente.GetComponent<TextMeshProUGUI>();
        ui.txtEstado     = tmpEstado.GetComponent<TextMeshProUGUI>();
        ui.txtProbeRoja  = tmpProbeRoja.GetComponent<TextMeshProUGUI>();
        ui.txtProbeNegra = tmpProbeNegra.GetComponent<TextMeshProUGUI>();
        ui.fondoVoltaje  = panel.GetComponent<Image>();

        Debug.Log("[MultimeterHudConversionTool] VictoryHUD_Canvas/Panel_Multimetro: Multimeter + MultimeterUI + 6 textos listos.");
        return multimeter;
    }

    static Transform FindOrCloneTmp(Transform panel, Transform template, string name,
        Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment, string text)
    {
        var existing = FindDeep(panel, name);
        if (existing != null) return existing;

        var clone = (GameObject)Object.Instantiate(template.gameObject, panel);
        clone.name = name;

        var rt = clone.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var tmp = clone.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.fontStyle = FontStyles.Normal;
        tmp.text = text;

        return clone.transform;
    }

    // ─────────────────────────────────────────────
    //  2. Cada Multimeter_Panel_RetoX: vaciar todo menos Mode_Button
    // ─────────────────────────────────────────────
    static int RewireModeButtons(Multimeter multimeter)
    {
        int done = 0;
        foreach (var label in new[] { "Reto1", "Reto2", "Reto3", "Reto4" })
        {
            var panelGO = FindInSceneByName($"Multimeter_Panel_{label}");
            if (panelGO == null)
            {
                Debug.LogWarning($"[MultimeterHudConversionTool] No se encontró 'Multimeter_Panel_{label}' — se omite.");
                continue;
            }

            var modeButton = panelGO.transform.Find("Mode_Button");
            if (modeButton == null)
            {
                Debug.LogWarning($"[MultimeterHudConversionTool] '{panelGO.name}' no tiene 'Mode_Button' como hijo directo — se omite.");
                continue;
            }

            panelGO.SetActive(true);

            // Quitar TODO lo demás de la raíz (Multimeter/collider/scripts viejos) — un
            // componente solo DESHABILITADO en un GameObject activo sigue apareciendo en
            // FindAnyObjectByType, así que hay que destruirlo, no alcanza con .enabled=false.
            foreach (var comp in panelGO.GetComponents<Component>())
            {
                if (comp is Transform) continue;
                Object.DestroyImmediate(comp, true);
            }

            int hiddenSiblings = 0;
            foreach (Transform child in panelGO.transform)
            {
                if (child == modeButton) continue;
                child.gameObject.SetActive(false);
                hiddenSiblings++;
            }

            modeButton.gameObject.SetActive(true);
            var button = modeButton.GetComponent<MultimeterModeButton>();
            if (button != null)
            {
                button.multimeter = multimeter;
                // Mode_Button es un componente HEREDADO de un PrefabInstance — una asignación de
                // campo común no basta para que Unity la registre como override al guardar
                // (verificado: el log del tool decía éxito, pero recargar la escena mostraba
                // multimeter=NULL otra vez). Hay que pedírselo explícitamente.
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            }

            Debug.Log($"[MultimeterHudConversionTool] '{label}': panel 3D vaciado ({hiddenSiblings} hijos ocultos, " +
                      $"componentes viejos de la raíz destruidos) — solo 'Mode_Button' sigue visible/activo.");
            done++;
        }

        return done;
    }

    // ─────────────────────────────────────────────
    //  3. Confirmar Multimeter_VR_Art (el original, sin reto) desactivado
    // ─────────────────────────────────────────────
    static int DeactivateOldVrArt()
    {
        var go = FindInSceneByName("Multimeter_VR_Art");
        if (go == null)
        {
            Debug.LogWarning("[MultimeterHudConversionTool] No se encontró 'Multimeter_VR_Art' en la escena.");
            return 0;
        }
        if (!go.activeSelf) return 0;
        go.SetActive(false);
        Debug.Log("[MultimeterHudConversionTool] 'Multimeter_VR_Art' desactivado (no borrado).");
        return 1;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    static GameObject FindInSceneByName(string name)
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
#endif
