using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flecha holográfica pulsante sobre un objeto interactivo del Reto 4 (botón de validación, caja
/// de cables, etc.), para que el Explorador lo encuentre fácil en VR (mismo estilo neón cyan que
/// <see cref="DeliveryTrayIndicator"/>). Genérico y reusable — el texto del label es configurable
/// (<see cref="labelText"/>), así que un mismo componente sirve para señalar cualquier prop.
///
/// Canvas WorldSpace auto-construido en Awake: triángulo apuntando hacia abajo (generado por
/// código, NO depende de ningún glifo de fuente ni sprite externo — así se garantiza que
/// renderiza sin importar qué fuente TMP esté configurada) + label debajo. Animación: el CANVAS
/// entero flota (bobbing) y siempre mira hacia el jugador (así el label queda legible); la FLECHA
/// además gira sobre su propio eje (Z local) por separado — si girara el canvas completo, el
/// texto quedaría ilegible medio giro de cada vuelta.
/// </summary>
public class ValidationButtonIndicator : MonoBehaviour
{
    [Header("Posición sobre el objeto (espacio local de este GameObject)")]
    public float heightAbove  = 0.15f;
    public float bobAmplitude = 0.02f;
    public float bobSpeed     = 2f;

    [Header("Giro de la flecha sobre su propio eje (grados/seg)")]
    public float spinSpeed = 90f;

    [Header("Texto")]
    public string labelText = "VALIDAR";

    [Header("Fuente TMP (opcional — si está vacío usa la fuente por defecto)")]
    public TMP_FontAsset font;

    static Color C(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static readonly Color _cyan = C("#00E5FF");

    Transform _canvasTr;
    Transform _arrowTr;
    Vector3   _basePos;
    Camera    _cam;

    void Awake()
    {
        BuildCanvas();
    }

    void Update()
    {
        if (_canvasTr == null) return;

        float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        _canvasTr.localPosition = _basePos + Vector3.up * bob;

        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 dir = _canvasTr.position - _cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                _canvasTr.rotation = Quaternion.LookRotation(dir);
        }

        // La flecha gira sobre su propio eje (perpendicular al canvas, ya billboardeado hacia el
        // jugador) — efecto de "marcador llamativo" sin afectar la legibilidad del label, que no gira.
        if (_arrowTr != null)
            _arrowTr.localRotation = Quaternion.Euler(0f, 0f, -Time.time * spinSpeed);
    }

    void BuildCanvas()
    {
        var canvasGO = new GameObject("Indicador_Flecha");
        canvasGO.transform.SetParent(transform, false);
        _basePos = new Vector3(0f, heightAbove, 0f);
        canvasGO.transform.localPosition = _basePos;
        canvasGO.transform.localScale    = Vector3.one * 0.0015f;
        _canvasTr = canvasGO.transform;

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160, 130);

        // Triángulo apuntando hacia abajo — generado por código, no depende de glifo/sprite externo.
        // Pivot centrado (0.5,0.5) a propósito: gira sobre su propio eje en Update(), y con pivot
        // centrado el giro es limpio alrededor de su propio centro (con pivot en un borde, "girar"
        // se vería como un bamboleo orbitando ese borde en vez de un giro en el lugar).
        var arrowGO = new GameObject("Flecha");
        arrowGO.transform.SetParent(canvasGO.transform, false);
        _arrowTr = arrowGO.transform;
        var arrowImg = arrowGO.AddComponent<Image>();
        arrowImg.sprite = CreateTriangleSprite();
        arrowImg.color  = _cyan;
        var arrowRT = arrowGO.GetComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(0.5f, 1f);
        arrowRT.anchorMax = new Vector2(0.5f, 1f);
        arrowRT.pivot     = new Vector2(0.5f, 0.5f);
        arrowRT.sizeDelta = new Vector2(70, 70);
        arrowRT.anchoredPosition = new Vector2(0f, -35f);

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(canvasGO.transform, false);
        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text      = labelText;
        label.fontSize  = 26;
        label.alignment = TextAlignmentOptions.Center;
        label.color     = _cyan;
        label.fontStyle = FontStyles.Bold;
        if (font != null) label.font = font;
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 0.35f);
        labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;
    }

    static Sprite CreateTriangleSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            // t=0 arriba (base ancha), t=1 abajo (punta) — triángulo apuntando hacia ABAJO.
            float t = y / (float)(size - 1);
            float halfWidthAtY = (1f - t) * (size * 0.5f);
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - size * 0.5f);
                pixels[y * size + x] = dx <= halfWidthAtY ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 1f));
    }
}
