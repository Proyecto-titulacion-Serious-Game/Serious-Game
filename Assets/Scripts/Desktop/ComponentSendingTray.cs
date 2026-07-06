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
    }

    void SetTexto(TMP_Text t, string s) { if (t != null) t.text = s; }

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
        SetTexto(txtFeedback, "✓ Componente enviado");
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