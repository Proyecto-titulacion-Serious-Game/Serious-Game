using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Panel de resultados final para el Explorador VR.
/// Se construye proceduralmente al recibir ObjectiveSystem.OnSessionEnded.
/// Se posiciona en mundo frente a la cámara; no requiere prefab.
///
/// SETUP: Añadir este componente a cualquier GameObject de la escena Explorador.
/// </summary>
public class ExplorerResultsPanel : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Posición")]
    public float distanceFromCamera = 1.5f;
    [Range(-0.5f, 0.5f)]
    public float verticalOffset = 0f;

    [Header("Colores")]
    public Color backgroundColor = new Color(0.03f, 0.03f, 0.12f, 0.97f);
    public Color headerColor     = new Color(0.55f, 0.88f, 1.00f);
    public Color excelentColor   = new Color(0.20f, 0.92f, 0.35f);
    public Color buenoColor      = new Color(1.00f, 0.82f, 0.10f);
    public Color mejorarColor    = new Color(1.00f, 0.32f, 0.22f);
    public Color labelColor      = new Color(0.70f, 0.70f, 0.70f);
    public Color valueColor      = new Color(0.95f, 0.95f, 0.95f);
    public Color accentColor     = new Color(0.30f, 0.82f, 0.45f);

    // ─────────────────────────────────────────────
    //  Internals
    // ─────────────────────────────────────────────
    private static readonly string[] RetoNames = { "Ley de Ohm", "Paralelo", "Mixto", "Arduino" };
    private GameObject _canvasGO;

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────
    void OnEnable()  => ObjectiveSystem.OnSessionEnded += ShowResults;
    void OnDisable() => ObjectiveSystem.OnSessionEnded -= ShowResults;

    // ─────────────────────────────────────────────
    //  Display
    // ─────────────────────────────────────────────
    void ShowResults(SessionResult result)
    {
        if (_canvasGO != null) Destroy(_canvasGO);

        var tracker = FindAnyObjectByType<PerformanceTracker>(FindObjectsInactive.Include);
        var records = tracker != null ? tracker.GetAllRecords() : new List<LevelRecord>();

        BuildCanvas(result, records);
        PositionCanvas();
    }

    void PositionCanvas()
    {
        if (_canvasGO == null) return;
        Transform cam = Camera.main != null ? Camera.main.transform : transform;
        Vector3 fwd = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;
        if (fwd == Vector3.zero) fwd = Vector3.forward;
        _canvasGO.transform.position = cam.position + fwd * distanceFromCamera + Vector3.up * verticalOffset;
        _canvasGO.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    // ─────────────────────────────────────────────
    //  Canvas procedural
    //  640 × 490 px · escala 0.001 → 0.64 m × 0.49 m
    // ─────────────────────────────────────────────
    void BuildCanvas(SessionResult result, List<LevelRecord> records)
    {
        _canvasGO = new GameObject("ResultsCanvas");
        _canvasGO.transform.localScale = Vector3.one * 0.001f;

        var canvas        = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt       = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(640, 490);

        // ── Fondo ────────────────────────────────
        AddImage("BG", rt, new Vector2(640, 490), Vector2.zero, backgroundColor);

        // ── Barra de acento superior ──────────────
        AddImage("Accent", rt, new Vector2(640, 5), new Vector2(0, 242f), accentColor);

        // ── Título ───────────────────────────────
        var title   = AddText("Title", rt, new Vector2(580, 30), new Vector2(0, 215), 17);
        title.text  = "LABORATORIO VIRTUAL  —  Sesión Finalizada";
        title.color = headerColor;
        title.alignment = TextAlignmentOptions.Center;
        title.fontStyle = FontStyles.Bold;

        // ── Evaluación ───────────────────────────
        Color evalColor = result.evaluation.Contains("EXCELENTE") ? excelentColor :
                          result.evaluation.Contains("BUENO")     ? buenoColor    : mejorarColor;

        var evalText   = AddText("Eval", rt, new Vector2(580, 38), new Vector2(0, 178), 28);
        evalText.text  = ExtractEvalBadge(result.evaluation);
        evalText.color = evalColor;
        evalText.fontStyle = FontStyles.Bold;
        evalText.alignment = TextAlignmentOptions.Center;

        var evalMsg   = AddText("EvalMsg", rt, new Vector2(580, 22), new Vector2(0, 152), 15);
        evalMsg.text  = ExtractEvalMessage(result.evaluation);
        evalMsg.color = labelColor;
        evalMsg.alignment = TextAlignmentOptions.Center;

        // ── Separador ────────────────────────────
        AddImage("Sep1", rt, new Vector2(580, 1), new Vector2(0, 136), new Color(1,1,1,0.15f));

        // ── Resumen numérico ─────────────────────
        int   min    = Mathf.FloorToInt(result.totalTimeSeconds / 60f);
        int   sec    = Mathf.FloorToInt(result.totalTimeSeconds % 60f);
        float pct    = result.maxScore > 0 ? (float)result.totalScore / result.maxScore : 0f;

        float summaryY = 113f;
        AddSummaryRow(rt, "Puntuación:", $"{result.totalScore} / {result.maxScore} pts   ({pct:P0})", summaryY);
        AddSummaryRow(rt, "Tiempo total:", $"{min}:{sec:00}", summaryY - 26f);
        AddSummaryRow(rt, "Errores:", $"{result.totalErrors}", summaryY - 52f);

        // ── Separador ────────────────────────────
        AddImage("Sep2", rt, new Vector2(580, 1), new Vector2(0, 50), new Color(1,1,1,0.15f));

        // ── Desglose por reto ────────────────────
        // Cada fila busca SU registro por LevelType, no por posición en la lista: si algún reto no
        // llegó a registrarse (p.ej. la victoria se evaluó en el otro proceso y aquí solo llegó el
        // cambio de reto por red), indexar por posición corría los registros de lugar — el Reto 4
        // aparecía en la fila del Reto 3 ("Mixto: 15 min y 14 errores") y el 4 quedaba vacío.
        float rowY = 32f;
        for (int i = 0; i < 4; i++)
        {
            string name  = i < RetoNames.Length ? RetoNames[i] : $"Reto {i+1}";
            string label = $"Reto {i + 1}  —  {name}";

            bool found = false;
            LevelRecord r = default;
            foreach (var x in records)
                if ((int)x.level == i) { r = x; found = true; }   // el último si hubiera duplicados

            if (found)
            {
                int rm = Mathf.FloorToInt(r.timeSeconds / 60f);
                int rs = Mathf.FloorToInt(r.timeSeconds % 60f);
                string check = r.success ? "✓" : "✗";
                Color  rowC  = r.success ? excelentColor : mejorarColor;
                AddRetoRow(rt, label, $"{check}  {rm}:{rs:00}  ·  {r.errors} err", rowY - i * 24f, rowC);
            }
            else
            {
                AddRetoRow(rt, label, "—", rowY - i * 24f, labelColor);
            }
        }

        // ── Pie / mensaje final ──────────────────
        var footer   = AddText("Footer", rt, new Vector2(580, 20), new Vector2(0, -228f), 13);
        footer.text  = "Los resultados han sido exportados para el docente.";
        footer.color = new Color(0.5f, 0.5f, 0.5f);
        footer.alignment = TextAlignmentOptions.Center;
        footer.fontStyle = FontStyles.Italic;
    }

    // ─────────────────────────────────────────────
    //  Helpers de layout
    // ─────────────────────────────────────────────

    void AddSummaryRow(RectTransform parent, string label, string value, float y)
    {
        var l = AddText($"SL_{y}", parent, new Vector2(220, 22), new Vector2(-100, y), 15);
        l.text = label; l.color = labelColor; l.alignment = TextAlignmentOptions.Right;

        var v = AddText($"SV_{y}", parent, new Vector2(320, 22), new Vector2(80, y), 15);
        v.text = value; v.color = valueColor; v.alignment = TextAlignmentOptions.Left;
        v.fontStyle = FontStyles.Bold;
    }

    void AddRetoRow(RectTransform parent, string label, string value, float y, Color valueCol)
    {
        var l = AddText($"RL_{y}", parent, new Vector2(320, 20), new Vector2(-80, y), 13);
        l.text = label; l.color = labelColor; l.alignment = TextAlignmentOptions.Left;

        var v = AddText($"RV_{y}", parent, new Vector2(200, 20), new Vector2(170, y), 13);
        v.text = value; v.color = valueCol; v.alignment = TextAlignmentOptions.Right;
        v.fontStyle = FontStyles.Bold;
    }

    UnityEngine.UI.Image AddImage(string name, RectTransform parent, Vector2 size, Vector2 pos, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size; rt.anchoredPosition = pos; rt.localScale = Vector3.one;
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.color = color;
        return img;
    }

    TMP_Text AddText(string name, RectTransform parent, Vector2 size, Vector2 pos, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size; rt.anchoredPosition = pos; rt.localScale = Vector3.one;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;
        return tmp;
    }

    // ─────────────────────────────────────────────
    //  Parseo de la evaluación
    // ─────────────────────────────────────────────

    static string ExtractEvalBadge(string eval)
    {
        if (eval.Contains("EXCELENTE")) return "[ EXCELENTE ]";
        if (eval.Contains("BUENO"))     return "[ BUENO ]";
        return "[ MEJORAR ]";
    }

    static string ExtractEvalMessage(string eval)
    {
        // El mensaje va después del primer ']'
        int idx = eval.IndexOf(']');
        if (idx >= 0 && idx + 1 < eval.Length)
            return eval[(idx + 1)..].Trim();
        return "";
    }
}
