using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Cicla el valor de una Resistor al activarla con el controlador VR.
/// Requiere XRSimpleInteractable en el mismo GameObject.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class InteractableResistor : MonoBehaviour
{
    public Resistor resistor;
    public float[] values = { 10f, 50f, 100f, 200f };

    private int                  _index       = 0;
    private XRSimpleInteractable _interactable;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (resistor == null)
            resistor = GetComponent<Resistor>() ?? GetComponentInParent<Resistor>();
    }

    void OnEnable()  => _interactable.selectEntered.AddListener(OnActivated);
    void OnDisable() => _interactable.selectEntered.RemoveListener(OnActivated);

void OnActivated(SelectEnterEventArgs _)
    {
        if (resistor == null)
        {
            Debug.LogWarning("[InteractableResistor] Campo 'resistor' no asignado.", this);
            return;
        }

        _index = (_index + 1) % values.Length;
        resistor.resistance = values[_index];
        
        // ✅ NUEVO: Avisamos al motor global que la resistencia cambió
        CircuitGraphAnalyzer.OnCircuitChanged?.Invoke();

        Debug.Log($"[InteractableResistor] Nueva resistencia: {resistor.resistance} Ω");
    }
}
