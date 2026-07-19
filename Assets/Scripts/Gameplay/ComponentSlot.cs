using UnityEngine;

public enum ComponentSlotType { Resistor, LED, Capacitor, ArduinoPin, Any }

/// <summary>
/// Slot físico donde el Explorador instala un componente recibido del Técnico.
/// VERSIÓN SANDBOX: Permite libertad de instalación, modo refrigeradora y liberación de piezas.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ComponentSlot : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Tipo aceptado")]
    public ComponentSlotType acceptedType = ComponentSlotType.Resistor;

    [Header("Valores de Validación (Modo Sandbox)")]
    public float targetElectricalValue = 0f;

    [Header("Referencias")]
    public ComponentDeliverySystem delivery;
    public GameManager             gameManager;   

    [Header("Feedback visual")]
    public Renderer slotRenderer;
    public Color colorNormal  = new Color(0.3f, 0.3f, 0.3f);
    public Color colorHover   = new Color(0.9f, 0.9f, 0.2f);
    public Color colorCorrect = new Color(0.2f, 0.9f, 0.3f);
    public Color colorWrong   = new Color(0.9f, 0.2f, 0.2f);

    [Header("Anclaje al instalar")]
    public Transform installAnchor;

    [Header("Componente dañado a reemplazar (opcional)")]
    public GameObject damagedComponent;

    [Header("Estado (solo lectura)")]
    [SerializeField] private bool       _hasComponent = false;
    [SerializeField] private GameObject _installed;



    public GameObject InstalledObject => _installed;

    // ─────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────
    private MaterialPropertyBlock _mpb;
    private static readonly int _colorID       = Shader.PropertyToID("_BaseColor"); 
    private static readonly int _colorLegacyID = Shader.PropertyToID("_Color");     

    void Start()
    {
        // Movemos el SetColor al Start, cuando ya todo Unity está cargado
        SetColor(colorNormal);
    }

    void Awake()
    {
        // Forzamos la creación del PropertyBlock lo más temprano posible
        _mpb = new MaterialPropertyBlock();

        if (slotRenderer == null) slotRenderer = GetComponent<Renderer>();
        if (delivery     == null) delivery     = FindAnyObjectByType<ComponentDeliverySystem>();
        if (gameManager  == null) gameManager  = FindAnyObjectByType<GameManager>();

        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger) col.isTrigger = true;
    }


    // ─────────────────────────────────────────────
    //  Trigger (Magnetismo Físico Infalible)
    // ─────────────────────────────────────────────
    // Cache de 1 entrada: OnTriggerStay corre cada physics tick por cada collider superpuesto
    // (hasta 32 slots en la protoboard del Reto 4), y antes llamaba GetComponentInParent en CADA
    // tick sin importar que el collider fuera el mismo del tick anterior. Mientras la misma pieza
    // siga rozando el mismo slot (el caso normal), esto evita repetir la búsqueda en la jerarquía.
    private Collider           _cachedTriggerCollider;
    private GrabbableComponent _cachedTriggerGC;

    GrabbableComponent ResolveGrabbable(Collider other)
    {
        if (other == _cachedTriggerCollider) return _cachedTriggerGC;
        _cachedTriggerCollider = other;
        _cachedTriggerGC       = other.GetComponentInParent<GrabbableComponent>();
        return _cachedTriggerGC;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_hasComponent) return;

        var gc = ResolveGrabbable(other);
        if (gc == null) return;

        if (gc.transform.parent != null && gc.transform.parent.name.Contains("Anchor")) return;
        if (delivery != null && !delivery.HasPendingDelivery()) return;

        ComponentType incoming = DetectComponentType(gc.gameObject);
        if (MatchesSlotType(incoming, acceptedType))
        {
            SetColor(colorHover); // Feedback visual inmediato al entrar
        }
    }

    void OnTriggerStay(Collider other)
    {
        // Si ya tiene una pieza, no hace nada
        if (_hasComponent) return;

        var gc = ResolveGrabbable(other);
        if (gc == null) return;

        // Evitar robar componentes pegados en otros slots
        if (gc.transform.parent != null && gc.transform.parent.name.Contains("Anchor")) return;

        ComponentType incoming = DetectComponentType(gc.gameObject);
        if (!MatchesSlotType(incoming, acceptedType)) return;

        // ─── EL SECRETO DEL IMÁN ───
        // Mientras la pieza siga dentro del cubito verde, preguntamos sin parar: "¿Ya la soltó?"
        // Si la soltó, la pegamos a la fuerza en ese mismo milisegundo.
        if (!gc.IsGrabbed)
        {
            InstallComponent(gc.gameObject);
            SetColor(colorCorrect);
            delivery?.OnExplorerInstalled(this);
            Debug.Log($"[Slot {name}] ¡Imán activado! {gc.name} succionado con éxito.");
        }
    }

    void OnTriggerExit(Collider other)
    {
        var gc = ResolveGrabbable(other);
        if (other == _cachedTriggerCollider) { _cachedTriggerCollider = null; _cachedTriggerGC = null; }
        if (gc == null) return;

        // CASO 1: El jugador agarra y arranca un componente que ya estaba magnetizado.
        // Solo liberar si DE VERDAD lo está agarrando. Si la pieza "salió" del trigger sin estar
        // agarrada (deriva por física / anchor fuera del collider), NO liberar → evita el bucle
        // instalar→liberar→caer→reinstalar que disparaba "valor incorrecto" miles de veces.
        if (_hasComponent && _installed != null && gc.gameObject == _installed)
        {
            if (gc.IsGrabbed) LiberarSlotPorRetiro();
            return;
        }

        // CASO 2: El jugador solo estaba pasando la pieza por encima y se alejó
        if (!_hasComponent)
        {
            SetColor(colorNormal);
        }
    }

    // Nota: El método OnComponentReleasedInSlot se borra por completo, 
    // ya que OnTriggerStay hace el trabajo de forma más segura.
    void OnComponentReleasedInSlot(GrabbableComponent gc)
    {
        if (gc != null) gc.Released -= OnComponentReleasedInSlot;
        //_hoveringGC = null;
        
        if (_hasComponent) return;

        // Doble verificación de que la mano realmente lo soltó
        if (gc != null && gc.IsGrabbed) return;

        InstallComponent(gc.gameObject);
        SetColor(colorCorrect);
        delivery?.OnExplorerInstalled(this);
    }

    // ─────────────────────────────────────────────
    //  Instalación y Liberación
    // ─────────────────────────────────────────────
    void InstallComponent(GameObject comp)
    {
        _hasComponent = true;
        _installed    = comp;

        Transform anchor = installAnchor != null ? installAnchor : transform;
        // Posición al slot, pero CONSERVAR la rotación con la que el Explorador colocó la pieza:
        // así la orientación física del componente se respeta en todos los retos (igual que en el
        // bareboard del Reto 4, donde no hay imán y la pieza queda tal cual la dejás).
        comp.transform.position = anchor.position;
        comp.transform.SetParent(anchor, worldPositionStays: true);

        if (comp.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = true;
            rb.useGravity  = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Ocultar la pieza dañada SOLO si no contiene un LED. En el Reto 1 el LED es una pieza
        // fija del circuito; si quedó dentro del 'damagedComponent', desactivarlo lo hacía
        // DESAPARECER (y al quedar inactivo el simulador no lo encontraba → nunca se ponía verde).
        if (damagedComponent != null && damagedComponent.GetComponentInChildren<LED>(true) == null)
            damagedComponent.SetActive(false);

        // Avisar al simulador que hay una nueva pieza en el tablero
        if (gameManager != null && gameManager.circuit != null)
        {
            gameManager.circuit.MarkDirty();
        }

        // Sonido de "colocado en slot" (CircuitAudioManager). Suena SOLO al instalar en un slot,
        // no al agarrar.
        GrabbableComponent.RaisePlacedInSlot();
    }

    void LiberarSlotPorRetiro()
    {
        if (_installed != null)
        {
            if (_installed.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false;
                rb.useGravity  = true;
            }
            _installed.transform.SetParent(null);
        }

        _installed    = null;
        _hasComponent = false;
        SetColor(colorNormal);

        if (damagedComponent != null)
            damagedComponent.SetActive(true);

        // Avisar al simulador que quedó un hueco vacío
        if (gameManager != null && gameManager.circuit != null)
        {
            gameManager.circuit.MarkDirty();
        }
    }

    public void ReleaseComponent()
    {
        LiberarSlotPorRetiro();
    }

    // ─────────────────────────────────────────────
    //  Detección y Validaciones
    // ─────────────────────────────────────────────
    ComponentType DetectComponentType(GameObject obj)
    {
        if (obj.GetComponent<Resistor>()   != null) return ComponentType.Resistor;
        if (obj.GetComponent<LED>()        != null) return ComponentType.LED;
        if (obj.GetComponent<Capacitor>()  != null) return ComponentType.Capacitor;
        if (obj.GetComponent<ArduinoPin>() != null) return ComponentType.ArduinoPin;

        if (obj.TryGetComponent<DeskComponent>(out var desk))
            return desk.componentType;

        return ComponentType.None;
    }

    bool MatchesSlotType(ComponentType c, ComponentSlotType s) => (c, s) switch
    {
        (_, ComponentSlotType.Any)                               => true, // Modo Sandbox activado
        (ComponentType.Resistor,   ComponentSlotType.Resistor)   => true,
        (ComponentType.LED,        ComponentSlotType.LED)        => true,
        (ComponentType.Capacitor,  ComponentSlotType.Capacitor)  => true,
        (ComponentType.ArduinoPin, ComponentSlotType.ArduinoPin) => true,
        _                                                        => false
    };

// ─────────────────────────────────────────────
    //  Helpers de Color y Renderizado (BLINDADOS)
    // ─────────────────────────────────────────────
    void SetColor(Color c)
    {
        if (slotRenderer == null) return;
        
        // ── FIX: Asegurar que _mpb siempre exista antes de usarlo ──
        if (_mpb == null) 
        {
            _mpb = new MaterialPropertyBlock();
        }

        slotRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_colorID, c);
        _mpb.SetColor(_colorLegacyID, c);
        slotRenderer.SetPropertyBlock(_mpb);
    }

    void ResetColor()
    {
        if (_hasComponent) return; 
        SetColor(colorNormal);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = acceptedType switch
        {
            ComponentSlotType.Any        => new Color(0f, 1f, 1f, 0.3f), // Cyan transparente para matriz
            ComponentSlotType.Resistor   => Color.yellow,
            ComponentSlotType.LED        => Color.green,
            ComponentSlotType.Capacitor  => Color.blue,
            ComponentSlotType.ArduinoPin => Color.magenta,
            _                            => Color.white
        };
        var col = GetComponent<Collider>();
        if (col is BoxCollider box)
            Gizmos.DrawWireCube(transform.position + box.center, box.size);
    }
}