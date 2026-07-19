using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Renderer), typeof(Collider))]
public class DeskComponent : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Configuración")]
    public ComponentType componentType = ComponentType.Resistor;
    public float         componentValue  = 100f;
    public string        componentDescription = "100 Ω";

    [Tooltip("Variante visual/física concreta que recibirá el Explorador (color del LED, color del " +
             "capacitor, orientación del resistor). 'Auto' la infiere del nombre/prefab de esta pieza.")]
    public ComponentVariant componentVariant = ComponentVariant.Auto;

    [Header("Referencias")]
    public ComponentSendingTray tray;
    public GameObject deliveredPrefab;

    [Header("Visuales")]
    public Color colorNormal   = Color.white;
    public Color colorHover    = new Color(1f, 0.85f, 0.2f);
    public Color colorSelected = Color.yellow;
    public float selectedScaleMultiplier = 1.1f;
    public float emissionIntensity = 1.5f;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private bool _isSelected;
    private Vector3 _originalScale;
    private static readonly int _colorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int _baseColorID = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _originalScale = transform.localScale;

        // Algunos componentes no se podían seleccionar (ni hover): en la INSTANCIA de la
        // escena varios BoxCollider quedaron con m_Size/m_Center mal (overrides), sin cubrir
        // el modelo → el raycast no los golpeaba. Reajustamos el collider al mesh en runtime
        // para garantizar que siempre cubra la pieza visible y sea clickeable.
        FitColliderToMesh();

        var xr = GetComponent<XRSimpleInteractable>();
        if (xr) xr.selectEntered.AddListener(_ => SelectThisComponent());
    }

    /// <summary>Ajusta (o crea) el BoxCollider para que envuelva TODO lo visible de la pieza,
    /// con un pequeño margen para facilitar el clic. Robustece la selección sin depender de cómo
    /// quedó serializado el collider en la escena ni de dónde esté el mesh.
    ///
    /// Clave del fix: se calcula la caja a partir de los <see cref="Renderer.bounds"/> (mundo) de
    /// TODOS los renderers hijos, convertidos al espacio local del root. Así funciona aunque el
    /// modelo esté en un hijo rotado/escalado/desplazado (p. ej. la resistencia VERTICAL), caso en
    /// el que usar el bounds local del mesh hijo dejaba el collider descolocado → no era clickeable.</summary>
    void FitColliderToMesh()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) bc = gameObject.AddComponent<BoxCollider>();

        var renderers = GetComponentsInChildren<Renderer>(true);

        // Caja envolvente en el espacio LOCAL del root (que es lo que el BoxCollider espera).
        // Pasada 1: solo renderers habilitados. Pasada 2 (fallback): cualquier renderer, aunque
        // esté deshabilitado — varias piezas (capacitor naranja, resistencia vertical) tenían el
        // collider serializado a 1 mm y no eran clickeables; necesitamos SIEMPRE un volumen real.
        bool   has = false;
        Bounds local = default;
        for (int pass = 0; pass < 2 && !has; pass++)
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (pass == 0 && !r.enabled) continue;   // pasada 1: solo visibles
                Bounds wb = r.bounds;               // AABB en mundo
                if (wb.size == Vector3.zero) continue;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    Vector3 lp = transform.InverseTransformPoint(corner);   // mundo → local del root
                    if (!has) { local = new Bounds(lp, Vector3.zero); has = true; }
                    else       local.Encapsulate(lp);
                }
            }
        }

        if (has)
        {
            bc.center = local.center;
            bc.size   = local.size * 1.15f;   // 15% de margen para que el clic sea más tolerante
        }
        else if (bc.size.x < 0.01f || bc.size.y < 0.01f || bc.size.z < 0.01f)
        {
            // Sin bounds utilizables y el collider serializado es diminuto → caja mínima clickeable.
            bc.size = Vector3.one * 0.04f;
        }

        bc.enabled   = true;
        bc.isTrigger = false;
    }

    public void OnPointerClick(PointerEventData eventData) => SelectThisComponent();
    public void OnPointerEnter(PointerEventData eventData) { if (!_isSelected) SetColor(colorNormal * 1.2f); }
    public void OnPointerExit(PointerEventData eventData)  { if (!_isSelected) SetColor(colorNormal); }

    public void SelectThisComponent()
    {
        // El mediador (tray) se encarga de deseleccionar a los demás
        if (tray != null) tray.SetSelectedComponent(this);
    }

    /// <summary>
    /// Devuelve la variante concreta a enviar al Explorador. Si está en 'Auto', la infiere de las
    /// palabras clave del nombre del prefab entregable (o de este GameObject): "yellow/amarillo",
    /// "red/rojo", "green/verde", "vertical/horizontal", "black/negro", "orange/naranja", "blue/azul".
    /// Así no hay que cablear la variante a mano en cada pieza ya colocada en la escena.
    /// </summary>
    public ComponentVariant ResolveVariant()
    {
        // 'Auto' = inferir explícitamente del nombre. 'Default' TAMBIÉN se infiere: las piezas de la
        // escena/prefab se serializaron ANTES de que existiera este campo, así que al cargar (sobre
        // todo en builds) el valor ausente cae a Default(0) aunque la pieza sea un LED rojo o un
        // capacitor naranja. Si saliéramos aquí con Default, el Explorador recibiría siempre la
        // variante por defecto (verde/azul/horizontal). Inferir del nombre del prefab/GameObject lo
        // resuelve; si no hay palabra clave, la inferencia devuelve Default igualmente (sin pérdida).
        if (componentVariant != ComponentVariant.Auto && componentVariant != ComponentVariant.Default)
            return componentVariant;

        // Combinamos el nombre del prefab entregable Y el de este GameObject: basta con que CUALQUIERA
        // contenga la palabra clave (p.ej. prefab "LEDYellow" o GameObject "Comp_LED_Yellow").
        // OJO: la palabra "Delivered" contiene "red" ("delive_RED_"), así que hay que quitarla de la
        // clave ANTES de buscar colores — sin esto, "Delivered_LED_Green" matcheaba key.Contains("red")
        // y el LED verde viajaba SIEMPRE como variante roja (bug real: "solo funciona el LED rojo").
        string key = ((deliveredPrefab != null ? deliveredPrefab.name : "") + " " + name)
                     .ToLowerInvariant().Replace("delivered", "");

        switch (componentType)
        {
            case ComponentType.LED:
                if (key.Contains("yellow") || key.Contains("amarill")) return ComponentVariant.LedYellow;
                if (key.Contains("red")    || key.Contains("roj"))     return ComponentVariant.LedRed;
                if (key.Contains("green")  || key.Contains("verde"))   return ComponentVariant.LedGreen;
                return ComponentVariant.Default;

            case ComponentType.Resistor:
                if (key.Contains("vertical"))   return ComponentVariant.ResistorVertical;
                if (key.Contains("horizontal")) return ComponentVariant.ResistorHorizontal;
                return ComponentVariant.Default;

            case ComponentType.Capacitor:
                if (key.Contains("black")  || key.Contains("negro"))  return ComponentVariant.CapacitorBlack;
                if (key.Contains("orange") || key.Contains("naranj")) return ComponentVariant.CapacitorOrange;
                if (key.Contains("blue")   || key.Contains("azul"))   return ComponentVariant.CapacitorBlue;
                return ComponentVariant.Default;

            default:
                return ComponentVariant.Default;
        }
    }

    // Método llamado por la Bandeja
    public void SetSelectionState(bool isSelected)
    {
        _isSelected = isSelected;
        // Aquí debe estar la lógica que antes tenías en Deselect o en el método de selección
        SetColor(isSelected ? colorSelected : colorNormal);
        transform.localScale = isSelected ? _originalScale * selectedScaleMultiplier : _originalScale;
    }

    void SetColor(Color c)
    {
        if (_renderer == null) return;
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_baseColorID, c);
        _mpb.SetColor(_colorID, _isSelected ? c * emissionIntensity : Color.black);
        _renderer.SetPropertyBlock(_mpb);
    }
}