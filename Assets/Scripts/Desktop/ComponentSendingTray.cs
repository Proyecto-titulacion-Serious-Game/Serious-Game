using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Bandeja de envío sobre la mesa del Técnico.
/// Actúa como Mediador para la selección de DeskComponents.
/// </summary>
public class ComponentSendingTray : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Referencias")]
    public TechnicianActions       technicianActions;
    public ComponentDeliverySystem delivery;
    public GameManager             gameManager;

    [Header("UI de la bandeja (World Space Canvas)")]
    public TMP_Text       txtComponenteEnBandeja;
    public TMP_Text       txtDescripcion;
    public TMP_InputField inputValor;
    public TMP_Text       txtInputLabel;
    public Toggle         togglePolaridad;
    public TMP_Text       txtToggleLabel;
    public Button         btnEnviar;
    public TMP_Text       txtFeedback;   // mensaje de estado tras enviar (OK / Error)
    public Transform      traySlot;      // punto donde aparece el componente en la bandeja

    // ─────────────────────────────────────────────
    //  Estado Interno
    // ─────────────────────────────────────────────
    private DeskComponent _currentSelectedDeskComponent;
    private Coroutine     _feedbackCoroutine;

    // Sonido de "enviar". Se carga de Resources/Audio/sfx_send; si no está, usa un click
    // genérico que ya viene en el proyecto. 2D, reproducido con un AudioSource propio.
    private AudioSource _sfx;
    private AudioClip   _sendClip;

    void Awake()
    {
        if (btnEnviar != null)
            btnEnviar.onClick.AddListener(EnviarComponente);

        SetupSendSfx();

        // La etiqueta del toggle no se refrescaba al cambiarlo: se quedaba en "CORRECTA" aunque el
        // Técnico mandara el LED invertido. La polaridad es condición de victoria del Reto 4, así que
        // el Técnico debe VER lo que envía. (El valor ya se leía bien en EnviarComponente.)
        if (togglePolaridad != null)
            togglePolaridad.onValueChanged.AddListener(on =>
            {
                if (txtToggleLabel != null)
                    txtToggleLabel.text = on ? "Polaridad: CORRECTA" : "Polaridad: INVERTIDA";
            });

        if (inputValor == null)
            Debug.LogWarning("[Bandeja] 'inputValor' no asignado en Inspector — " +
                             "el campo de ohms no se ocultará para LEDs/Capacitores.", this);

        ActualizarUI();
    }

    // ─────────────────────────────────────────────
    //  Lógica de Selección (Patrón Mediador)
    // ─────────────────────────────────────────────
    
    public void SetSelectedComponent(DeskComponent newComponent)
    {
        // 1. Apagar el anterior usando el método correcto (SetSelectionState)
        if (_currentSelectedDeskComponent != null)
        {
            _currentSelectedDeskComponent.SetSelectionState(false);
        }

        // 2. Encender el nuevo
        _currentSelectedDeskComponent = newComponent;
        
        if (_currentSelectedDeskComponent != null)
        {
            _currentSelectedDeskComponent.SetSelectionState(true);
        }

        // 3. Actualizar UI
        ActualizarUI();
    }

    // ─────────────────────────────────────────────
    //  Actualización de Interfaz
    // ─────────────────────────────────────────────

    void ActualizarUI()
    {
        if (_currentSelectedDeskComponent != null)
        {
            SetTexto(txtComponenteEnBandeja, _currentSelectedDeskComponent.name.Replace("Comp_", ""));
            SetTexto(txtDescripcion, _currentSelectedDeskComponent.componentDescription);

            // BUG FIX: inputValor solo para tipos que requieren valor numérico
            bool necesitaValor = _currentSelectedDeskComponent.componentType == ComponentType.Resistor
                              || _currentSelectedDeskComponent.componentType == ComponentType.ArduinoPin;
            if (inputValor  != null)
            {
                // Igual que en la rama de limpieza: cerrar la edición y soltar la selección ANTES
                // de desactivar el campo. Si se desactiva con el foco puesto (p.ej. el Técnico
                // tecleaba ohms y clickeó un LED), el EventSystem queda atascado en un objeto
                // inactivo y el editor de código del IDE deja de recibir teclado.
                if (!necesitaValor)
                {
                    if (inputValor.isFocused) inputValor.DeactivateInputField();
                    if (EventSystem.current != null &&
                        EventSystem.current.currentSelectedGameObject == inputValor.gameObject)
                        EventSystem.current.SetSelectedGameObject(null);
                }
                inputValor.gameObject.SetActive(necesitaValor);
                inputValor.text = "";
            }
            if (txtInputLabel != null)  txtInputLabel.gameObject.SetActive(necesitaValor);

            // Toggle de polaridad solo para LED y Capacitor
            bool necesitaToggle = _currentSelectedDeskComponent.componentType == ComponentType.LED
                               || _currentSelectedDeskComponent.componentType == ComponentType.Capacitor;
            if (togglePolaridad != null) { togglePolaridad.gameObject.SetActive(necesitaToggle); togglePolaridad.isOn = true; }
            if (txtToggleLabel  != null) { txtToggleLabel.gameObject.SetActive(necesitaToggle);  txtToggleLabel.text = "Polaridad: CORRECTA"; }

            if (btnEnviar    != null)  btnEnviar.gameObject.SetActive(true);
            if (txtFeedback  != null)  txtFeedback.text = "";   // limpiar feedback anterior
        }
        else
        {
            SetTexto(txtComponenteEnBandeja, "Bandeja vacía");
            SetTexto(txtDescripcion, "Haz click en un componente de la mesa");

            // Cerrar la edición del campo de ohms ANTES de desactivarlo. Desactivar el GO de un
            // TMP_InputField que está en edición deja el sistema de input de TMP "colgado" y el
            // siguiente campo (el editor de código del IDE) no recibe teclado.
            if (inputValor != null)
            {
                if (inputValor.isFocused) inputValor.DeactivateInputField();
                inputValor.gameObject.SetActive(false);
            }
            if (txtInputLabel    != null) txtInputLabel.gameObject.SetActive(false);
            if (togglePolaridad  != null) togglePolaridad.gameObject.SetActive(false);
            if (txtToggleLabel   != null) txtToggleLabel.gameObject.SetActive(false);
            if (btnEnviar        != null) btnEnviar.gameObject.SetActive(false);
        }

        ActualizarPreview();
    }

    void SetTexto(TMP_Text t, string s) { if (t != null) t.text = s; }

    // ─────────────────────────────────────────────
    //  Vista previa 3D sobre la bandeja (traySlot)
    // ─────────────────────────────────────────────

    /// <summary>Copia SOLO-VISUAL del componente seleccionado, mostrada sobre 'traySlot'
    /// (hijo de Tray_Visual). Se reemplaza al cambiar la selección y se destruye al enviar
    /// o deseleccionar. Se construye copiando únicamente los meshes/materiales del prefab
    /// entregable — nunca se instancia el prefab entero, porque trae XRGrabInteractable
    /// (se registraría en el Interaction Manager), Rigidbody (se caería de la bandeja),
    /// BoxCollider (taparía los clicks del canvas) y el ElectricalComponent (entraría a la
    /// simulación del circuito).</summary>
    private GameObject _trayPreview;

    void ActualizarPreview()
    {
        if (_trayPreview != null) { Destroy(_trayPreview); _trayPreview = null; }
        if (_currentSelectedDeskComponent == null || traySlot == null) return;

        // El prefab entregable ES la variante concreta (LED amarillo, resistor vertical…),
        // así el Técnico ve exactamente lo que va a recibir el Explorador. Si la pieza no
        // tiene prefab asignado, se copia el visual de la propia pieza de la mesa.
        GameObject fuente = _currentSelectedDeskComponent.deliveredPrefab != null
                          ? _currentSelectedDeskComponent.deliveredPrefab
                          : _currentSelectedDeskComponent.gameObject;

        _trayPreview = CrearCascaronVisual(fuente, traySlot);
        AjustarAlSlot(_trayPreview);
    }

    /// <summary>Construye un GameObject nuevo bajo 'parent' que reproduce solo los
    /// MeshFilter/MeshRenderer de 'fuente' (sirve tanto para un prefab-asset como para un
    /// objeto de escena), preservando la pose local de cada pieza respecto al root.</summary>
    static GameObject CrearCascaronVisual(GameObject fuente, Transform parent)
    {
        var root = new GameObject("TrayPreview_Visual");
        root.transform.SetParent(parent, false);

        Matrix4x4 aLocalDeFuente = fuente.transform.worldToLocalMatrix;
        foreach (var mf in fuente.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null) continue;

            var parte = new GameObject(mf.name);
            parte.transform.SetParent(root.transform, false);

            Matrix4x4 rel = aLocalDeFuente * mf.transform.localToWorldMatrix;
            parte.transform.localPosition = rel.GetPosition();
            parte.transform.localRotation = rel.rotation;
            parte.transform.localScale    = rel.lossyScale;

            parte.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            parte.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
        }
        return root;
    }

    /// <summary>Escala el cascarón a un tamaño de vitrina uniforme y centra su volumen
    /// visible sobre el punto del slot (los pivotes de los prefabs no coinciden con su
    /// centro visual, así que sin esto la pieza queda hundida o descentrada).</summary>
    void AjustarAlSlot(GameObject preview)
    {
        const float tamanoVitrina = 0.08f;   // dimensión mayor de la pieza en la bandeja, en metros

        var rends = preview.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);

        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (maxDim > 1e-5f)
            preview.transform.localScale *= tamanoVitrina / maxDim;

        // recomputar tras escalar y posar el conjunto centrado sobre el slot
        b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        preview.transform.position += traySlot.position - b.center;
    }

    // ─────────────────────────────────────────────
    //  Envío al Explorador
    // ─────────────────────────────────────────────

    void EnviarComponente()
    {
        if (_currentSelectedDeskComponent == null) return;

        ComponentType tipo       = _currentSelectedDeskComponent.componentType;
        float         valorFinal = 0f;

        if (tipo == ComponentType.Resistor || tipo == ComponentType.ArduinoPin)
        {
            if (inputValor != null && !string.IsNullOrEmpty(inputValor.text))
                float.TryParse(inputValor.text, NumberStyles.Float,
                               CultureInfo.InvariantCulture, out valorFinal);
        }
        else if (tipo == ComponentType.LED || tipo == ComponentType.Capacitor)
        {
            valorFinal = (togglePolaridad != null && togglePolaridad.isOn) ? 1f : -1f;
        }

        // Variante visual/física concreta (color del LED, color del capacitor, orientación del
        // resistor) que eligió el Técnico. Sin esto, el Explorador siempre recibía la variante por
        // defecto (LED verde / resistor horizontal) aunque se enviara otra.
        int variante = (int)_currentSelectedDeskComponent.ResolveVariant();

        if (GameSession.Instance != null)
        {
            // Path de red: el RPC llega al Explorador y su ExplorerComponentReceiver
            // spawna el componente y llama delivery.PrepareForInstall allá.
            GameSession.Instance.RPC_EnviarComponente((int)tipo, valorFinal, variante);
            Debug.Log($"[Bandeja] {tipo} ({valorFinal}) variante={(ComponentVariant)variante} enviado por red.");
        }
        else
        {
            // Path offline/sin Fusion: dispara evento local.
            // ExplorerComponentReceiver lo escucha y spawna + llama PrepareForInstall.
            RaiseOnComponentSentLocal(tipo, valorFinal, variante);
            // Fallback si no hay ExplorerComponentReceiver en la escena.
            delivery?.PrepareForInstall(tipo, valorFinal);
            Debug.LogWarning("[Bandeja] GameSession.Instance es null — entrega local. " +
                             "Verificar que ConnectionManager.modoOffline = false y que " +
                             "no haya un CM duplicado con rolAutomatico = Ninguno en la escena.");
        }

        // Sonido de envío
        if (_sfx != null && _sendClip != null) _sfx.PlayOneShot(_sendClip, GameSettings.SfxVolume);

        // Liberar el foco del EventSystem ANTES de limpiar la bandeja: SetSelectedComponent(null)
        // desactiva 'inputValor' (el campo de ohms), y desactivar un InputField que tiene el foco
        // deja el EventSystem "atascado" en un objeto inactivo → después NINGÚN otro campo recibe
        // teclado (p.ej. el editor de código del IDE no deja escribir tras enviar un componente).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        // Limpiar la bandeja usando el mediador
        SetSelectedComponent(null);

        // Mostrar feedback (se limpia tras 3 s)
        if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
        SetTexto(txtFeedback, "[OK] Componente enviado");
        _feedbackCoroutine = StartCoroutine(LimpiarFeedback(3f));
    }

    IEnumerator LimpiarFeedback(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetTexto(txtFeedback, "");
        _feedbackCoroutine = null;
    }
    // ─────────────────────────────────────────────
    //  Eventos de comunicación (Fix para CS0117)
    // ─────────────────────────────────────────────
    public static event System.Action<ComponentType, float, int> OnComponentSentLocal;

    public static void RaiseOnComponentSentLocal(ComponentType tipo, float valor,
                                                 int variante = (int)ComponentVariant.Default)
    {
        OnComponentSentLocal?.Invoke(tipo, valor, variante);
    }

    // ─────────────────────────────────────────────
    //  Sonido de envío
    // ─────────────────────────────────────────────
    void SetupSendSfx()
    {
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake  = false;
        _sfx.spatialBlend = 0f;   // 2D
        _sfx.volume       = 0.8f;

        // Resources/Audio/sfx_send (ponelo ahí para tu propio sonido). Si no está,
        // se usa un click/pop que ya viene con los samples del proyecto.
        _sendClip = Resources.Load<AudioClip>("Audio/sfx_send");
        if (_sendClip == null)
            _sendClip = Resources.Load<AudioClip>("Audio/sfx_component_installed");  // fallback al SFX de circuito si existe
    }
}