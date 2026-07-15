using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;

/// <summary>
/// Resaltado de sintaxis + numeración de líneas en runtime para un <see cref="TMP_InputField"/>,
/// sin tocar el prefab (se construye todo por código, así no hay que editar YAML ni cerrar Unity).
///
/// TÉCNICA (estándar para editores de código sobre TMP):
///   - El InputField conserva el texto PLANO (la lógica del parser no cambia).
///   - El texto real del InputField se vuelve invisible (alpha 0) pero el cursor sigue activo,
///     porque el caret se posiciona sobre la geometría del texto, no sobre su color.
///   - Un TextMeshProUGUI "overlay", hijo del componente de texto del InputField, muestra el
///     mismo string con tags &lt;color&gt; por token. Como los tags de color NO cambian el avance
///     de los glifos en TMP, el overlay queda alineado pixel-perfect con el cursor.
///   - Una regleta de números va en una columna a la izquierda (hija del viewport) y sincroniza
///     su scroll vertical con el del texto.
///
/// Robusto y desactivable: si algo falla en el setup concreto, se auto-desactiva en lugar de
/// romper el IDE, y se puede apagar desde ArduinoIDEUI (enableSyntaxHighlight / enableLineNumbers).
/// </summary>
[DisallowMultipleComponent]
public class ArduinoSyntaxHighlighter : MonoBehaviour
{
    // ─── Paleta (estilo VS Code dark) ───────────────────────────────────────
    const string CDefault  = "#D4D4D4";
    const string CComment   = "#6A9955";
    const string CString    = "#CE9178";
    const string CNumber    = "#B5CEA8";
    const string CKeyword   = "#569CD6";   // void, if, for, int, return…
    const string CBuiltin   = "#DCDCAA";   // pinMode, digitalWrite, delay…
    const string CConstant  = "#4FC1FF";   // HIGH, LOW, OUTPUT, LED_BUILTIN…
    const string CGutter    = "#5A6470";

    static readonly HashSet<string> Keywords = new HashSet<string>
    {
        "void","int","float","bool","boolean","char","byte","long","short","double",
        "unsigned","const","static","volatile","if","else","for","while","do","switch",
        "case","break","continue","return","true","false","String","word","sizeof"
    };
    static readonly HashSet<string> Builtins = new HashSet<string>
    {
        "pinMode","digitalWrite","digitalRead","analogWrite","analogRead","delay",
        "delayMicroseconds","millis","micros","map","constrain","tone","noTone","Serial",
        "setup","loop","attachInterrupt","shiftOut","pulseIn"
    };
    static readonly HashSet<string> Constants = new HashSet<string>
    {
        "HIGH","LOW","OUTPUT","INPUT","INPUT_PULLUP","LED_BUILTIN",
        "A0","A1","A2","A3","A4","A5","PI","TRUE","FALSE"
    };

    // Comentario | string | char | número | identificador
    static readonly Regex TokenRx = new Regex(
        @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +
        @"|(?<str>""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')" +
        @"|(?<num>\b\d+\.?\d*[fFlLuU]?\b)" +
        @"|(?<id>[A-Za-z_]\w*)",
        RegexOptions.Compiled);

    // ─── Estado ─────────────────────────────────────────────────────────────
    private TMP_InputField  _field;
    private TMP_Text        _source;     // textComponent del InputField
    private TextMeshProUGUI _overlay;
    private TextMeshProUGUI _gutter;
    private bool            _highlight;
    private bool            _lineNumbers;
    private bool            _ok;

    private const float GutterWidth = 42f;
    private const float GutterPad    = 8f;

    private readonly StringBuilder _sb = new StringBuilder(1024);

    // ─────────────────────────────────────────────
    //  API
    // ─────────────────────────────────────────────
    public void Initialize(TMP_InputField field, bool lineNumbers, bool highlight)
    {
        _field       = field;
        _lineNumbers = lineNumbers;
        _highlight   = highlight;

        if (_field == null) { enabled = false; return; }
        _source = _field.textComponent;
        if (_source == null || _source.font == null)
        {
            Debug.LogWarning("[SyntaxHighlighter] textComponent/font no disponible — desactivado.");
            enabled = false;
            return;
        }

        // Hacer sitio para la regleta empujando el margen izquierdo del texto.
        Vector4 m = _source.margin;
        float leftInset = _lineNumbers ? GutterWidth + GutterPad : 0f;
        _source.margin = new Vector4(m.x + leftInset, m.y, m.z, m.w);

        if (_highlight) BuildOverlay();
        if (_lineNumbers) BuildGutter(m);

        _field.onValueChanged.AddListener(OnTextChanged);
        Refresh(_field.text);
        _ok = true;
    }

    void OnDisable()
    {
        if (_field != null) _field.onValueChanged.RemoveListener(OnTextChanged);
    }

    // FIX: sin esto, tras el primer cierre/apertura del Hub (arduinoHUD.SetActive(false) al salir
    // con Esc desactiva este componente → OnDisable quita el listener) el overlay queda CONGELADO
    // para siempre — el TMP_InputField real sigue recibiendo texto (el cursor avanza), pero como el
    // texto real es invisible (alpha 0) y el overlay ya no se refresca, se ve como si "no se pudiera
    // editar": lo escrito nunca aparece, y queda mostrando el último contenido antes del disable.
    void OnEnable()
    {
        if (!_ok || _field == null) return;
        _field.onValueChanged.AddListener(OnTextChanged);
        Refresh(_field.text);   // el texto pudo cambiar mientras el overlay estaba desconectado
    }

    void OnTextChanged(string s) => Refresh(s);

    void LateUpdate()
    {
        if (!_ok || _gutter == null || _source == null) return;
        // La regleta sigue el scroll VERTICAL del texto, pero ignora el horizontal.
        var g = _gutter.rectTransform;
        float y = _source.rectTransform.anchoredPosition.y;
        if (!Mathf.Approximately(g.anchoredPosition.y, y))
            g.anchoredPosition = new Vector2(g.anchoredPosition.x, y);
    }

    // ─────────────────────────────────────────────
    //  Construcción de overlay y regleta
    // ─────────────────────────────────────────────
    void BuildOverlay()
    {
        var go = new GameObject("SyntaxOverlay", typeof(RectTransform));
        go.transform.SetParent(_source.transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _overlay = go.AddComponent<TextMeshProUGUI>();
        CopyTextSettings(_source, _overlay);
        _overlay.margin       = _source.margin;
        _overlay.richText     = true;
        _overlay.raycastTarget = false;
        _overlay.color        = HexToColor(CDefault);

        // El InputField trata el texto como literal (sin rich text): así su geometría —que
        // posiciona el caret— coincide con el overlay, que escapa < y > con <noparse>.
        _field.richText = false;

        // Ocultar el texto real del InputField (el caret sigue visible y funcional).
        var c = _source.color; c.a = 0f; _source.color = c;
        _field.customCaretColor = true;
        if (_field.caretColor.a < 0.1f) _field.caretColor = Color.white;
    }

    void BuildGutter(Vector4 originalMargin)
    {
        // Hija del viewport (padre del textComponent) para fijar la X aunque haya scroll H.
        Transform viewport = _source.transform.parent != null
            ? _source.transform.parent : _source.transform;

        var go = new GameObject("LineNumbers", typeof(RectTransform));
        go.transform.SetParent(viewport, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(GutterWidth, 0f);
        rt.anchoredPosition = new Vector2(originalMargin.x, 0f);

        _gutter = go.AddComponent<TextMeshProUGUI>();
        CopyTextSettings(_source, _gutter);
        _gutter.alignment     = TextAlignmentOptions.TopRight;
        _gutter.margin        = new Vector4(0f, originalMargin.y, GutterPad, 0f);
        _gutter.richText      = true;
        _gutter.raycastTarget = false;
        _gutter.color         = HexToColor(CGutter);
    }

    static void CopyTextSettings(TMP_Text src, TMP_Text dst)
    {
        dst.font               = src.font;
        dst.fontSharedMaterial = src.fontSharedMaterial;
        dst.fontSize           = src.fontSize;
        dst.fontStyle          = src.fontStyle;
        dst.enableAutoSizing   = false;
        dst.characterSpacing   = src.characterSpacing;
        dst.wordSpacing        = src.wordSpacing;
        dst.lineSpacing        = src.lineSpacing;
        dst.paragraphSpacing   = src.paragraphSpacing;
        dst.alignment          = src.alignment;
        dst.overflowMode       = src.overflowMode;
        dst.extraPadding       = src.extraPadding;
    }

    // ─────────────────────────────────────────────
    //  Refresco
    // ─────────────────────────────────────────────
    void Refresh(string code)
    {
        if (_highlight && _overlay != null)
            _overlay.text = Highlight(code ?? string.Empty);

        if (_lineNumbers && _gutter != null)
            _gutter.text = BuildLineNumbers(code ?? string.Empty);
    }

    string BuildLineNumbers(string code)
    {
        int lines = 1;
        for (int i = 0; i < code.Length; i++) if (code[i] == '\n') lines++;

        _sb.Clear();
        for (int i = 1; i <= lines; i++)
        {
            _sb.Append(i);
            if (i < lines) _sb.Append('\n');
        }
        return _sb.ToString();
    }

    // ─────────────────────────────────────────────
    //  Tokenizador → rich text
    // ─────────────────────────────────────────────
    string Highlight(string code)
    {
        _sb.Clear();
        int pos = 0;

        foreach (Match m in TokenRx.Matches(code))
        {
            if (m.Index > pos) AppendEscaped(code, pos, m.Index - pos);   // texto entre tokens

            string color = null;
            string val   = m.Value;

            if (m.Groups["comment"].Success)      color = CComment;
            else if (m.Groups["str"].Success)     color = CString;
            else if (m.Groups["num"].Success)     color = CNumber;
            else if (m.Groups["id"].Success)
            {
                if      (Builtins.Contains(val))  color = CBuiltin;
                else if (Keywords.Contains(val))  color = CKeyword;
                else if (Constants.Contains(val)) color = CConstant;
            }

            if (color != null)
            {
                _sb.Append("<color=").Append(color).Append('>');
                AppendEscaped(val, 0, val.Length);
                _sb.Append("</color>");
            }
            else
            {
                AppendEscaped(val, 0, val.Length);
            }

            pos = m.Index + m.Length;
        }

        if (pos < code.Length) AppendEscaped(code, pos, code.Length - pos);
        return _sb.ToString();
    }

    /// <summary>Copia [start, start+len) escapando &lt; y &gt; para que TMP no los lea como tags.</summary>
    void AppendEscaped(string s, int start, int len)
    {
        int end = start + len;
        for (int i = start; i < end; i++)
        {
            char c = s[i];
            if      (c == '<') _sb.Append("<noparse><</noparse>");
            else if (c == '>') _sb.Append("<noparse>></noparse>");
            else               _sb.Append(c);
        }
    }

    // ─────────────────────────────────────────────
    static Color HexToColor(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }
}
