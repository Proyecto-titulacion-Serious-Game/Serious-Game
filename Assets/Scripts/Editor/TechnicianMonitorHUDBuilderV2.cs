#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reconstruye Assets/Prefabs/TechnicianMonitorHUD.prefab — versión 2.
///
/// ⚠ DEPRECATED / NO EJECUTAR sin editar antes: este builder añade <see cref="ArduinoCodeEditor"/>
/// (IDE legacy de un mini-juego "encuentra el error" de un solo sketch fijo) al MISMO GameObject
/// donde vive <see cref="ArduinoIDEUI"/> (el IDE real y activo del Reto 4: código libre +
/// intérprete completo). El v1 builder ya deja ArduinoIDEUI en ese root; este v2 solo AÑADE
/// ArduinoCodeEditor encima, sin quitar el otro — quedan 2 scripts compartiendo el MISMO
/// TMP_InputField/consola (mismos fileID), pisándose el uno al otro (listeners de
/// onValueChanged duplicados, Start() de ArduinoCodeEditor sobrescribe el texto, eventos de
/// Ardity reescriben la consola). Esto causaba que el editor de código dejara de aceptar
/// texto tras interactuar con otros paneles (manual/diagnóstico/envío de componente) — se
/// quitó ArduinoCodeEditor del prefab en runtime el 2026-07-11. Si necesitas re-generar el
/// HUD, usa el builder v1 (sin ArduinoCodeEditor) o borra la línea `AddComponent&lt;ArduinoCodeEditor&gt;()`
/// de este archivo primero.
///
/// Diferencias respecto al builder original (v1):
///   - Panel izquierdo: TMP_InputField editable con código real + ArduinoCodeEditor
///                      (reemplaza los dropdowns de ArduinoIDEUI)
///   - Panel derecho:   Telemetría igual que v1 + nueva sección "Estado Arduino"
///   - Header/Footer/Validation Overlay: sin cambios visuales
///
/// Menú: Tools → TITA → Reto 4 → Reconstruir HUD Monitor Técnico (v2 — Code Editor)
/// </summary>
public static class TechnicianMonitorHUDBuilderV2
{
    // ─────────────────────────────────────────────────────────────
    //  Config
    // ─────────────────────────────────────────────────────────────
    const string PREFAB_PATH  = "Assets/Prefabs/TechnicianMonitorHUD.prefab";
    const float  CW           = 1920f;
    const float  CH           = 1080f;
    const float  CANVAS_SCALE = 1f / 1600f;

    // ─────────────────────────────────────────────────────────────
    //  Paleta dark-industrial (idéntica a v1)
    // ─────────────────────────────────────────────────────────────
    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

    static readonly Color BgBase      = C("#080C12");
    static readonly Color BgPanel     = C("#0D1117");
    static readonly Color BgTerminal  = C("#050709");
    static readonly Color BgCard      = C("#111820");
    static readonly Color BgHeader    = C("#0A0F18");
    static readonly Color BgFooter    = C("#06090E");
    static readonly Color Accent      = C("#00FFB2");
    static readonly Color AccentBlue  = C("#00B8FF");
    static readonly Color AccentOrange= C("#FF9800");
    static readonly Color Border      = C("#1E2D3D");
    static readonly Color TextMuted   = C("#546E7A");
    static readonly Color TextCode    = C("#A6ACCD");
    static readonly Color TextPrimary = C("#E8EAF6");
    static readonly Color StateOk     = C("#00FF7F");
    static readonly Color StateOkBg   = C("#002B1A");
    static readonly Color StateErr    = C("#FF2D55");
    static readonly Color StateErrBg  = C("#2B000D");
    static readonly Color BtnGreen    = C("#00E676");
    static readonly Color BtnGreenBg  = C("#002B1A");
    static readonly Color BtnBlue     = C("#00B8FF");
    static readonly Color BtnBlueBg   = C("#002030");

    // ─────────────────────────────────────────────────────────────
    //  Font
    // ─────────────────────────────────────────────────────────────
    static TMP_FontAsset _font;
    static TMP_FontAsset Font
    {
        get
        {
            if (_font != null) return _font;
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (_font == null)
                _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return _font;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Entry point
    // ─────────────────────────────────────────────────────────────
    [MenuItem("Tools/TITA/Reto 4/Reconstruir HUD Monitor Técnico (v2 - Code Editor)")]
    public static void Build()
    {
        BuildSilent();
        EditorUtility.DisplayDialog(
            "HUD Monitor Técnico v2 — Code Editor",
            "Prefab guardado en:\n" + PREFAB_PATH + "\n\n" +
            "Ejecuta ahora:\n" +
            "Tools → TITA → Reto 4 → Resetear HUD Monitor (v2 - diseño completo)\n" +
            "para instanciarlo en la escena.",
            "OK");
    }

    /// <summary>Construye el prefab sin mostrar diálogo. Llamado por Reto4HUDResetTool.</summary>
    public static void BuildSilent()
    {
        _font = null;

        var root   = NewGO("TechnicianMonitorHUD");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 2;
        root.AddComponent<GraphicRaycaster>();

        var rootRT = root.GetComponent<RectTransform>();
        rootRT.sizeDelta  = new Vector2(CW, CH);
        rootRT.localScale = Vector3.one * CANVAS_SCALE;

        root.AddComponent<Image>().color = BgBase;

        // Componentes al root
        var hud   = root.AddComponent<TechnicianHUDController>();
        var telem = root.AddComponent<TechnicianTelemetryUI>();
        var codeEditor = root.AddComponent<ArduinoCodeEditor>();

        // Secciones
        BuildHeader(root, hud);
        BuildFooter(root);

        var main   = NewGO("MainArea", root);
        var mainRT = main.AddComponent<RectTransform>();
        SetAnchors(mainRT, 0f, 0.06f, 1f, 0.96f);
        Zero(mainRT);

        // Divisor vertical 55/45
        var div = NewImg("Divider_V", main, Border);
        var divRT = div.GetComponent<RectTransform>();
        SetAnchors(divRT, 0.55f, 0f, 0.55f, 1f);
        divRT.sizeDelta        = new Vector2(1f, 0f);
        divRT.anchoredPosition = Vector2.zero;

        BuildCodeEditorPanel(main, codeEditor);
        BuildTelemetryPanel(main, telem);
        BuildValidationOverlay(root, hud);

        // Guardar prefab
        string dir = Path.GetDirectoryName(PREFAB_PATH);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log("[TechnicianMonitorHUDBuilderV2] Prefab guardado: " + PREFAB_PATH);
    }

    // ─────────────────────────────────────────────────────────────
    //  HEADER BAR
    // ─────────────────────────────────────────────────────────────
    static void BuildHeader(GameObject root, TechnicianHUDController hud)
    {
        var hdr = NewImg("Header", root, BgHeader);
        SetAnchors(hdr.GetComponent<RectTransform>(), 0f, 0.96f, 1f, 1f);
        Zero(hdr.GetComponent<RectTransform>());
        AddBorderLine(hdr, "BorderBot", false, false, Border);

        var logo = NewTxt("Txt_Logo", hdr, ">>  TITA LAB v2.4", 18f, Accent, bold: true);
        PinLeft(logo.rectTransform, 20f);

        NewTxt("Sep1", hdr, "|", 14f, Border).rectTransform.anchoredPosition = new Vector2(0, 0);
        PinLeft(NewTxt("Sep1", hdr, "|", 14f, Border).rectTransform, 210f);

        var txtReto = NewTxt("Txt_Reto", hdr, "RETO 4 — ARDUINO BLINK", 13f, TextPrimary, bold: true);
        txtReto.fontStyle |= FontStyles.UpperCase;
        txtReto.characterSpacing = 2f;
        PinLeft(txtReto.rectTransform, 232f);
        hud.txtReto = txtReto;

        var txtTimer = NewTxt("Txt_Timer", hdr, "15:00", 20f, AccentBlue, bold: true);
        PinRight(txtTimer.rectTransform, 24f);
        hud.txtTimer = txtTimer;

        var txtErr = NewTxt("Txt_Errores", hdr, "ERR: 0", 12f, TextMuted);
        PinRight(txtErr.rectTransform, 130f);
        hud.txtErrores = txtErr;

        // Indicador de conexión Arduino
        var dot = NewImg("Dot_Arduino", hdr, AccentOrange);
        var dotRT = dot.GetComponent<RectTransform>();
        dotRT.anchorMin = dotRT.anchorMax = dotRT.pivot = new Vector2(1f, 0.5f);
        dotRT.anchoredPosition = new Vector2(-340f, 0f);
        dotRT.sizeDelta = new Vector2(8f, 8f);

        var txtArduino = NewTxt("Txt_Arduino", hdr, "SIMULACION", 11f, AccentOrange);
        PinRight(txtArduino.rectTransform, 356f);
    }

    // ─────────────────────────────────────────────────────────────
    //  FOOTER BAR
    // ─────────────────────────────────────────────────────────────
    static void BuildFooter(GameObject root)
    {
        var foot = NewImg("Footer", root, BgFooter);
        SetAnchors(foot.GetComponent<RectTransform>(), 0f, 0f, 1f, 0.06f);
        Zero(foot.GetComponent<RectTransform>());
        AddBorderLine(foot, "BorderTop", true, false, Border);

        var txt = NewTxt("Txt_Footer", foot,
            "[ARDITY: COM?]   [BAUD: 9600]   [Fusion: HOST]   Sketch: sketch_reto4.ino",
            10f, TextMuted);
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(txt.rectTransform);
        txt.margin = new Vector4(16f, 0f, 16f, 0f);
    }

    // ─────────────────────────────────────────────────────────────
    //  PANEL IZQUIERDO — Editor de Código (55%)
    // ─────────────────────────────────────────────────────────────
    static void BuildCodeEditorPanel(GameObject parent, ArduinoCodeEditor codeEditor)
    {
        var panel = NewImg("Panel_CodeEditor", parent, BgPanel);
        SetAnchors(panel.GetComponent<RectTransform>(), 0f, 0f, 0.55f, 1f);
        Zero(panel.GetComponent<RectTransform>());

        // ── File bar (top 8%) ─────────────────────────────────────
        var fileBar = NewImg("FileBar", panel, BgHeader);
        SetAnchors(fileBar.GetComponent<RectTransform>(), 0f, 0.92f, 1f, 1f);
        Zero(fileBar.GetComponent<RectTransform>());
        AddBorderLine(fileBar, "Bot", false, false, Border);

        var ico = NewTxt("Ico_File", fileBar, "#", 14f, Accent);
        PinLeft(ico.rectTransform, 16f);

        NewTxt("Txt_FileName", fileBar, "sketch_reto4.ino", 13f, TextCode)
            .rectTransform.anchoredPosition = new Vector2(-50f, 0f); // approx center
        PinLeft(NewTxt("Txt_FileName2", fileBar, "sketch_reto4.ino", 13f, TextCode)
            .rectTransform, 40f);

        // Badge estado código
        var badge = NewImg("Badge_Status", fileBar, BtnBlueBg);
        var badgeRT = badge.GetComponent<RectTransform>();
        badgeRT.anchorMin = badgeRT.anchorMax = badgeRT.pivot = new Vector2(1f, 0.5f);
        badgeRT.anchoredPosition = new Vector2(-12f, 0f);
        badgeRT.sizeDelta = new Vector2(100f, 26f);
        AddBorder4(badge, BtnBlue, 1f);
        var badgeTxt = NewTxt("Txt_Badge", badge, "EDITABLE ●", 10f, BtnBlue);
        badgeTxt.alignment = TextAlignmentOptions.Center;
        StretchFull(badgeTxt.rectTransform);

        // ── Área de código (8% - 82%) ─────────────────────────────
        var codeArea = NewImg("CodeArea", panel, BgTerminal);
        SetAnchors(codeArea.GetComponent<RectTransform>(), 0f, 0.18f, 1f, 0.92f);
        Zero(codeArea.GetComponent<RectTransform>());
        AddBorderLine(codeArea, "TopLine", true, false,
            new Color(Accent.r, Accent.g, Accent.b, 0.1f));

        // Gutter números de línea
        var gutter = NewImg("Gutter", codeArea, new Color(0.03f, 0.05f, 0.09f, 1f));
        SetAnchors(gutter.GetComponent<RectTransform>(), 0f, 0f, 0.055f, 1f);
        Zero(gutter.GetComponent<RectTransform>());
        AddBorderLine(gutter, "Right", false, true, Border);
        var lineNums = NewTxt("Txt_LineNums", gutter,
            "001\n002\n003\n004\n005\n006\n007\n008\n009\n010\n011\n012",
            11f, TextMuted);
        lineNums.alignment = TextAlignmentOptions.TopRight;
        StretchFull(lineNums.rectTransform);
        lineNums.margin = new Vector4(2f, 8f, 4f, 8f);

        // InputField multilinea editable
        var inputField = BuildCodeInputField(codeArea);
        codeEditor.inputCode = inputField;

        // ── Consola (10% - 18%) ────────────────────────────────────
        var console = NewImg("Console", panel, new Color(0.03f, 0.04f, 0.07f, 1f));
        SetAnchors(console.GetComponent<RectTransform>(), 0f, 0.10f, 1f, 0.18f);
        Zero(console.GetComponent<RectTransform>());
        AddBorderLine(console, "Top", true, false, Border);

        // Prompt icon
        var prompt = NewTxt("Ico_Prompt", console, ">", 12f, Accent);
        PinLeft(prompt.rectTransform, 12f);

        var txtConsole = NewTxt("Txt_Console", console,
            "<color=#546E7A>Haz clic en Verificar para analizar el código.</color>",
            11f, TextMuted);
        txtConsole.richText  = true;
        txtConsole.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(txtConsole.rectTransform);
        txtConsole.margin = new Vector4(32f, 0f, 8f, 0f);
        codeEditor.txtConsole = txtConsole;

        // ── Barra de botones (bottom 10%) ─────────────────────────
        var btnRow = NewImg("ButtonRow", panel, BgPanel);
        SetAnchors(btnRow.GetComponent<RectTransform>(), 0f, 0f, 1f, 0.10f);
        Zero(btnRow.GetComponent<RectTransform>());
        AddBorderLine(btnRow, "Top", true, false, Border);

        // Status text
        var txtStatus = NewTxt("Txt_Status", btnRow, "Listo.", 11f, TextMuted);
        txtStatus.alignment = TextAlignmentOptions.MidlineLeft;
        SetAnchors(txtStatus.rectTransform, 0f, 0f, 0.4f, 1f);
        Zero(txtStatus.rectTransform);
        txtStatus.margin = new Vector4(16f, 0f, 4f, 0f);
        codeEditor.txtStatus = txtStatus;

        // Botón VERIFICAR
        var btnVerify = MakeActionButton(btnRow, "Btn_Verificar", "VERIFICAR",
            new Vector2(-340f, 0f), new Vector2(240f, 52f), BtnBlueBg, BtnBlue);
        codeEditor.btnVerify = btnVerify;

        // Botón SUBIR
        var btnUpload = MakeActionButton(btnRow, "Btn_Subir", "SUBIR  >>",
            new Vector2(-80f, 0f), new Vector2(240f, 52f), BtnGreenBg, BtnGreen);
        codeEditor.btnUpload = btnUpload;

        // Preview panel bajo el código (zona de resultados del parser)
        var previewGO = NewImg("Panel_Preview", panel, new Color(0.04f, 0.06f, 0.1f, 0.9f));
        SetAnchors(previewGO.GetComponent<RectTransform>(), 0f, 0.82f, 1f, 0.92f);
        Zero(previewGO.GetComponent<RectTransform>());
        AddBorderLine(previewGO, "Top", true, false,
            new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.3f));

        var txtPreview = NewTxt("Txt_Preview", previewGO,
            "<color=#546E7A><i>— sketch no analizado —</i></color>",
            11f, TextMuted);
        txtPreview.richText  = true;
        txtPreview.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(txtPreview.rectTransform);
        txtPreview.margin = new Vector4(16f, 0f, 8f, 0f);
        codeEditor.txtPreview = txtPreview;
    }

    // ─────────────────────────────────────────────────────────────
    //  TMP_InputField editable (editor de código)
    // ─────────────────────────────────────────────────────────────
    static TMP_InputField BuildCodeInputField(GameObject parent)
    {
        // Área de edición (de 5.5% en X para dejar el gutter)
        var go = NewGO("InputField_Code", parent);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, 0.055f, 0f, 1f, 1f);
        Zero(rt);

        var bg = go.AddComponent<Image>();
        bg.color = Color.clear;

        var field = go.AddComponent<TMP_InputField>();
        field.lineType    = TMP_InputField.LineType.MultiLineNewline;
        field.contentType = TMP_InputField.ContentType.Standard;
        field.characterLimit = 0;
        field.scrollSensitivity = 10f;

        // Viewport con máscara
        var vpGO = NewGO("Viewport", go);
        var vpRT = vpGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = new Vector2(10f, 6f);
        vpRT.offsetMax = new Vector2(-6f, -6f);
        vpGO.AddComponent<RectMask2D>();

        // Texto principal
        var txtGO = NewGO("Text", vpGO);
        var txtRT = txtGO.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = txtRT.offsetMax = Vector2.zero;
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.font       = Font;
        txt.fontSize   = 18f;
        txt.color      = TextCode;
        txt.alignment  = TextAlignmentOptions.TopLeft;
        txt.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        txt.richText   = false;
        txt.lineSpacing = 4f;

        // Caret / selección
        var caretGO = NewGO("Caret", vpGO);
        caretGO.AddComponent<RectTransform>();

        field.textViewport  = vpRT;
        field.textComponent = txt;
        field.caretColor    = Accent;
        field.caretWidth    = 2;
        field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.3f);

        return field;
    }

    // ─────────────────────────────────────────────────────────────
    //  PANEL DERECHO — Telemetría (45%)
    // ─────────────────────────────────────────────────────────────
    static void BuildTelemetryPanel(GameObject parent, TechnicianTelemetryUI telem)
    {
        var panel = NewImg("Panel_Telemetria", parent, BgPanel);
        SetAnchors(panel.GetComponent<RectTransform>(), 0.55f, 0f, 1f, 1f);
        Zero(panel.GetComponent<RectTransform>());

        // Header telemetría
        var hdr = NewImg("Header_Telem", panel, BgHeader);
        SetAnchors(hdr.GetComponent<RectTransform>(), 0f, 0.92f, 1f, 1f);
        Zero(hdr.GetComponent<RectTransform>());
        AddBorderLine(hdr, "Bot", false, false,
            new Color(Accent.r, Accent.g, Accent.b, 0.15f));

        var ico = NewTxt("Ico_Telem", hdr, "~", 16f, Accent);
        PinLeft(ico.rectTransform, 14f);

        var hdrTxt = NewTxt("Txt_HdrTelem", hdr, "TELEMETRIA EN TIEMPO REAL", 12f, Accent, bold: true);
        hdrTxt.fontStyle |= FontStyles.UpperCase;
        hdrTxt.characterSpacing = 1.5f;
        PinLeft(hdrTxt.rectTransform, 38f);

        // Área de cards (21%–92%)
        var cardsArea = NewGO("CardsArea", panel);
        var caRT = cardsArea.AddComponent<RectTransform>();
        SetAnchors(caRT, 0f, 0.21f, 1f, 0.92f);
        Zero(caRT);

        var (cardV,   valV,   unitV)   = MakeTelemCard(cardsArea, "Card_Voltaje",    "VOLTAJE",   "V",      AccentBlue);
        var (cardI,   valI,   unitI)   = MakeTelemCard(cardsArea, "Card_Corriente",  "CORRIENTE", "mA",     AccentBlue);
        var (cardW,   valW,   _)       = MakeTelemCard(cardsArea, "Card_Potencia",   "POTENCIA",  "mW",     Accent);
        var (cardADC, valADC, _)       = MakeTelemCard(cardsArea, "Card_ADC",        "ADC  A0",   "/ 1023", Accent);

        const float g = 0.01f;
        SetAnchors(cardV.GetComponent<RectTransform>(),   0f,       0.51f+g, 0.5f-g, 1f);
        SetAnchors(cardI.GetComponent<RectTransform>(),   0.5f+g,   0.51f+g, 1f,     1f);
        SetAnchors(cardW.GetComponent<RectTransform>(),   0f,       0f,      0.5f-g, 0.49f-g);
        SetAnchors(cardADC.GetComponent<RectTransform>(), 0.5f+g,   0f,      1f,     0.49f-g);
        Zero(cardV.GetComponent<RectTransform>());
        Zero(cardI.GetComponent<RectTransform>());
        Zero(cardW.GetComponent<RectTransform>());
        Zero(cardADC.GetComponent<RectTransform>());

        telem.txtValorVoltaje   = valV;
        telem.txtValorCorriente = valI;
        telem.txtValorPotencia  = valW;
        telem.txtValorAdc       = valADC;
        telem.imgCardVoltaje    = cardV.GetComponent<Image>();
        telem.imgCardCorriente  = cardI.GetComponent<Image>();

        // ── Sección estado Arduino (11%–21%) ──────────────────────
        var arduinoStatus = NewImg("Panel_ArduinoStatus", panel, BgCard);
        SetAnchors(arduinoStatus.GetComponent<RectTransform>(), 0f, 0.11f, 1f, 0.21f);
        Zero(arduinoStatus.GetComponent<RectTransform>());
        AddBorderLine(arduinoStatus, "Top", true, false, Border);
        AddBorderLine(arduinoStatus, "Bot", false, false, Border);

        var icoPin = NewTxt("Ico_Pin", arduinoStatus, "D", 14f, AccentOrange, bold: true);
        PinLeft(icoPin.rectTransform, 16f);

        var lblPin = NewTxt("Lbl_Pin", arduinoStatus, "Pin activo:", 11f, TextMuted);
        PinLeft(lblPin.rectTransform, 38f);

        var valPin = NewTxt("Txt_PinActivo", arduinoStatus, "D13", 16f, AccentOrange, bold: true);
        SetAnchors(valPin.rectTransform, 0f, 0f, 1f, 1f);
        Zero(valPin.rectTransform);
        valPin.alignment = TextAlignmentOptions.Center;

        var lblMode = NewTxt("Lbl_Mode", arduinoStatus, "OUTPUT | BLINK", 11f, TextMuted);
        PinRight(lblMode.rectTransform, 16f);

        // ── Banner alerta (bottom 11%) ─────────────────────────────
        var banner = NewImg("Banner_Estado", panel, StateOkBg);
        SetAnchors(banner.GetComponent<RectTransform>(), 0f, 0f, 1f, 0.11f);
        Zero(banner.GetComponent<RectTransform>());
        AddBorderLine(banner, "Top", true, false,
            new Color(StateOk.r, StateOk.g, StateOk.b, 0.25f));

        var stripe = NewImg("Banner_Stripe", banner, StateOk);
        var strRT = stripe.GetComponent<RectTransform>();
        SetAnchors(strRT, 0f, 0f, 0f, 1f);
        strRT.sizeDelta        = new Vector2(5f, 0f);
        strRT.anchoredPosition = Vector2.zero;
        telem.bannerStripe = stripe.GetComponent<Image>();

        var txtAlert = NewTxt("Txt_Alerta", banner,
            "OK  ESTADO: OPERACION SEGURA", 14f, StateOk, bold: true);
        txtAlert.fontStyle |= FontStyles.UpperCase;
        txtAlert.alignment  = TextAlignmentOptions.Center;
        StretchFull(txtAlert.rectTransform);
        telem.lblAlerta   = txtAlert;
        telem.panelAlerta = banner.GetComponent<Image>();
    }

    // ─────────────────────────────────────────────────────────────
    //  VALIDATION OVERLAY
    // ─────────────────────────────────────────────────────────────
    static void BuildValidationOverlay(GameObject root, TechnicianHUDController hud)
    {
        var overlay = NewImg("Panel_Validacion", root, new Color(0f, 0f, 0f, 0.65f));
        StretchFull(overlay.GetComponent<RectTransform>());
        overlay.GetComponent<Image>().raycastTarget = true;
        overlay.SetActive(false);
        hud.panelValidacion = overlay;

        var modal = NewImg("Modal", overlay, BgPanel);
        SetAnchors(modal.GetComponent<RectTransform>(), 0.20f, 0.31f, 0.80f, 0.69f);
        Zero(modal.GetComponent<RectTransform>());
        AddBorder4(modal, AccentBlue, 2f);
        hud.imgValidacionBg = modal.GetComponent<Image>();

        var topLine = NewImg("TopLine", modal, AccentBlue);
        var tlRT = topLine.GetComponent<RectTransform>();
        SetAnchors(tlRT, 0f, 1f, 1f, 1f);
        tlRT.sizeDelta = new Vector2(0f, 2f);
        tlRT.anchoredPosition = Vector2.zero;

        var txtEstado = NewTxt("Txt_ValidacionEstado", modal,
            ">> EVALUANDO CIRCUITO...", 22f, AccentBlue, bold: true);
        txtEstado.alignment = TextAlignmentOptions.Center;
        SetAnchors(txtEstado.rectTransform, 0f, 0.60f, 1f, 0.92f);
        Zero(txtEstado.rectTransform);
        hud.txtValidacionEstado = txtEstado;

        var progBg = NewImg("ProgressBg", modal, Border);
        SetAnchors(progBg.GetComponent<RectTransform>(), 0.05f, 0.50f, 0.95f, 0.60f);
        Zero(progBg.GetComponent<RectTransform>());
        var progFill = NewImg("ProgressFill", progBg, AccentBlue);
        SetAnchors(progFill.GetComponent<RectTransform>(), 0f, 0f, 0f, 1f);
        Zero(progFill.GetComponent<RectTransform>());
        hud.progressFill = progFill.GetComponent<Image>();

        var chkPanel = NewGO("Panel_Checklist", modal);
        var chkRT = chkPanel.AddComponent<RectTransform>();
        SetAnchors(chkRT, 0.04f, 0.12f, 0.96f, 0.50f);
        Zero(chkRT);
        hud.panelChecklist = chkPanel;

        string[] labels = { ">> Verificando continuidad...", ">> Midiendo corriente...", ">> Validando pin Arduino..." };
        float[] y0 = { 0.66f, 0.33f, 0.00f };
        float[] y1 = { 1.00f, 0.66f, 0.33f };
        var checks = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var item = NewTxt($"Chk_{i + 1}", chkPanel, labels[i], 12f, TextMuted);
            item.alignment = TextAlignmentOptions.MidlineLeft;
            SetAnchors(item.rectTransform, 0f, y0[i], 1f, y1[i]);
            Zero(item.rectTransform);
            item.margin = new Vector4(12f, 0f, 4f, 0f);
            checks[i] = item;
        }
        hud.txtCheck1 = checks[0]; hud.txtCheck2 = checks[1]; hud.txtCheck3 = checks[2];

        var btnCloseGO = NewImg("Btn_Cerrar", modal, StateErrBg);
        AddBorder4(btnCloseGO, StateErr, 1f);
        SetAnchors(btnCloseGO.GetComponent<RectTransform>(), 0.55f, 0.06f, 0.75f, 0.18f);
        Zero(btnCloseGO.GetComponent<RectTransform>());
        var btnTxt = NewTxt("Txt_Cerrar", btnCloseGO, "X  CERRAR", 11f, StateErr, bold: true);
        btnTxt.alignment = TextAlignmentOptions.Center;
        StretchFull(btnTxt.rectTransform);
        var btnClose = btnCloseGO.AddComponent<Button>();
        btnClose.targetGraphic = btnCloseGO.GetComponent<Image>();
        btnCloseGO.SetActive(false);
        hud.btnCerrarValidacion = btnClose;
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers de creación de UI
    // ─────────────────────────────────────────────────────────────

    static Button MakeActionButton(GameObject parent, string name, string label,
        Vector2 anchoredPos, Vector2 sizeDelta, Color bgColor, Color borderColor)
    {
        var go = NewImg(name, parent, bgColor);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        AddBorder4(go, borderColor, 2f);

        // Acento izquierdo
        var accent = NewImg("Accent", go, borderColor);
        var aRT = accent.GetComponent<RectTransform>();
        SetAnchors(aRT, 0f, 0f, 0f, 1f);
        aRT.sizeDelta = new Vector2(4f, 0f);
        aRT.anchoredPosition = Vector2.zero;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        ColorBlock cb = btn.colors;
        cb.normalColor      = bgColor;
        cb.highlightedColor = bgColor * 1.4f;
        cb.pressedColor     = borderColor;
        btn.colors = cb;

        var txt = NewTxt("Label", go, label, 14f, borderColor, bold: true);
        txt.alignment  = TextAlignmentOptions.Center;
        txt.fontStyle |= FontStyles.UpperCase;
        StretchFull(txt.rectTransform);
        return btn;
    }

    static (GameObject card, TMP_Text val, TMP_Text unit) MakeTelemCard(
        GameObject parent, string name, string title, string unitStr, Color accentColor)
    {
        var card = NewImg(name, parent, BgCard);
        AddBorder4(card, Border, 1f);

        // Línea acento superior
        var top = NewImg("AccentTop", card, accentColor);
        var tRT = top.GetComponent<RectTransform>();
        SetAnchors(tRT, 0f, 1f, 1f, 1f);
        tRT.sizeDelta = new Vector2(0f, 3f);
        tRT.anchoredPosition = Vector2.zero;

        // Label
        var lbl = NewTxt("Lbl", card, title, 10f, TextMuted);
        lbl.fontStyle        |= FontStyles.UpperCase;
        lbl.characterSpacing  = 2f;
        lbl.alignment         = TextAlignmentOptions.Top;
        SetAnchors(lbl.rectTransform, 0f, 0.72f, 1f, 1f);
        Zero(lbl.rectTransform);
        lbl.margin = new Vector4(10f, 8f, 4f, 0f);

        // Valor
        var valTxt = NewTxt("Val", card, "0.00", 32f, accentColor, bold: true);
        valTxt.alignment = TextAlignmentOptions.Center;
        SetAnchors(valTxt.rectTransform, 0f, 0.30f, 1f, 0.72f);
        Zero(valTxt.rectTransform);

        // Unidad
        var unitTxt = NewTxt("Unit", card, unitStr, 12f, TextMuted);
        unitTxt.alignment = TextAlignmentOptions.BottomRight;
        SetAnchors(unitTxt.rectTransform, 0f, 0f, 1f, 0.30f);
        Zero(unitTxt.rectTransform);
        unitTxt.margin = new Vector4(4f, 0f, 10f, 8f);

        return (card, valTxt, unitTxt);
    }

    // ─────────────────────────────────────────────────────────────
    //  Factory helpers (igual que v1)
    // ─────────────────────────────────────────────────────────────

    static GameObject NewGO(string name, GameObject parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent.transform, false);
        return go;
    }

    static GameObject NewImg(string name, GameObject parent, Color color)
    {
        var go  = NewGO(name, parent);
        var img = go.AddComponent<Image>();
        img.color         = color;
        img.raycastTarget = false;
        go.AddComponent<RectTransform>();
        return go;
    }

    static TextMeshProUGUI NewTxt(string name, GameObject parent, string text,
        float size, Color color, bool bold = false)
    {
        var go  = NewGO(name, parent);
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.font      = Font;
        txt.text      = text;
        txt.fontSize  = size;
        txt.color     = color;
        if (bold) txt.fontStyle |= FontStyles.Bold;
        txt.raycastTarget = false;
        txt.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        return txt;
    }

    static void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
    }

    static void Zero(RectTransform rt)
    {
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }

    static void PinLeft(RectTransform rt, float x)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot     = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(200f, 0f);
    }

    static void PinRight(RectTransform rt, float xFromRight)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-xFromRight, 0f);
        rt.sizeDelta = new Vector2(160f, 0f);
    }

    static void AddBorderLine(GameObject parent, string name,
        bool isTop, bool isRight, Color color)
    {
        var go  = NewImg(name, parent, color);
        var rt  = go.GetComponent<RectTransform>();
        if (isRight)
        {
            SetAnchors(rt, 1f, 0f, 1f, 1f);
            rt.sizeDelta = new Vector2(1f, 0f);
        }
        else if (isTop)
        {
            SetAnchors(rt, 0f, 1f, 1f, 1f);
            rt.sizeDelta = new Vector2(0f, 1f);
        }
        else
        {
            SetAnchors(rt, 0f, 0f, 1f, 0f);
            rt.sizeDelta = new Vector2(0f, 1f);
        }
        rt.anchoredPosition = Vector2.zero;
    }

    static void AddBorder4(GameObject go, Color color, float thickness)
    {
        string[] names = { "BL", "BT", "BR", "BB" };
        bool[]   horiz = { false, false, false, false };
        // L, T, R, B
        Vector2[] amin = { new Vector2(0,0), new Vector2(0,1), new Vector2(1,0), new Vector2(0,0) };
        Vector2[] amax = { new Vector2(0,1), new Vector2(1,1), new Vector2(1,1), new Vector2(1,0) };
        Vector2[] sd   = { new Vector2(thickness,0), new Vector2(0,thickness),
                           new Vector2(thickness,0), new Vector2(0,thickness) };

        for (int i = 0; i < 4; i++)
        {
            var b  = NewImg(names[i], go, color);
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = amin[i]; rt.anchorMax = amax[i];
            rt.sizeDelta = sd[i];   rt.anchoredPosition = Vector2.zero;
        }
    }
}
#endif
