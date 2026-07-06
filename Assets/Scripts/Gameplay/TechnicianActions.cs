using UnityEngine;

/// <summary>
/// Acciones del Técnico sobre el circuito.
/// CORRECCIÓN CRÍTICA: Al enviar o reemplazar, destruye el componente de la mesa 
/// para evitar acumulaciones visuales en la pantalla del Técnico.
/// </summary>
public class TechnicianActions : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Referencias")]
    public CircuitManager    circuit;
    public Multimeter        multimeter;
    public InstructionSystem instructionSystem;
    public GameManager       gameManager;
    public PerformanceTracker performance;

    [Header("Valores correctos por reto")]
    public float correctResistance   = 100f;   // Reto 1
    public float normalLedResistance = 50f;    // Reto 2

    [Header("Modo demo (sin InstructionSystem)")]
    [Tooltip("True = repara sin requerir pasos previos del InstructionSystem")]
    public bool demoMode = true;

    [Header("Selección actual (solo lectura)")]
    public ElectricalComponent selectedComponent;
    private SelectableComponent _selectedVisual;

    // ─────────────────────────────────────────────
    //  Selección de componente
    // ─────────────────────────────────────────────

    /// <summary>
    /// Selecciona un componente del circuito para diagnosticar o reparar.
    /// Llamado desde SelectableComponent.OnSelect() o DeskComponent.
    /// </summary>
    public void SelectComponent(ElectricalComponent component, SelectableComponent visual)
    {
        // Quitar highlight del anterior
        if (_selectedVisual != null)
            _selectedVisual.ResetHighlight();

        selectedComponent = component;
        _selectedVisual   = visual;

        if (_selectedVisual != null)
            _selectedVisual.Highlight();

        // Penalización solo en modo normal (no demo)
        if (!demoMode && gameManager != null &&
            gameManager.currentLevel == LevelType.OhmLaw &&
            !(component is Resistor))
        {
            RegisterError("Seleccion de componente incorrecto en Reto 1");
            Debug.Log("[TechnicianActions] Seleccionaste el componente incorrecto.");
        }
        else
        {
            Debug.Log($"[TechnicianActions] Seleccionado: {component.name}");
        }
    }

    // ─────────────────────────────────────────────
    //  Reparaciones
    // ─────────────────────────────────────────────

    /// <summary>
    /// Reemplaza la resistencia seleccionada con el valor correcto.
    /// CORREGIDO: Destruye el GameObject visual de la mesa del Técnico tras enviarlo.
    /// </summary>
    public void ReplaceSelectedResistor()
    {
        if (!demoMode && instructionSystem != null && !instructionSystem.CanRepairResistor())
        {
            Debug.Log("[TechnicianActions] Primero mide y selecciona la resistencia.");
            RegisterError("Intento de reparar antes de tiempo");
            return;
        }

        if (selectedComponent == null)
        {
            Debug.Log("[TechnicianActions] No hay componente seleccionado.");
            return;
        }

        if (selectedComponent is Resistor r)
        {
            r.resistance = correctResistance;
            r.hasFault   = false;

            // CRÍTICO: sin esto el LED no cambia de color
            circuit?.MarkDirty();
            gameManager?.RegisterRepairAction();

            Debug.Log($"[TechnicianActions] Resistencia reemplazada: {correctResistance} Ohm. LED debe cambiar a verde.");

            // ─────────────────────────────────────────────────────────────────
            // ¡EL FIX CRÍTICO AQUÍ!
            // Destruimos el objeto visual que está sobre la mesa del Técnico
            // ─────────────────────────────────────────────────────────────────
            if (_selectedVisual != null)
            {
                Debug.Log($"[TechnicianActions] Limpiando mesa: Destruyendo {_selectedVisual.gameObject.name}");
                Destroy(_selectedVisual.gameObject);
                _selectedVisual = null; // Limpiar referencia visual
            }

            selectedComponent = null; // Limpiar referencia lógica para el siguiente componente
        }
        else
        {
            Debug.Log("[TechnicianActions] El componente seleccionado no es una resistencia.");
            if (!demoMode) RegisterError("Intento reparar componente incorrecto");
        }
    }

    /// <summary>
    /// Reconecta el cable suelto del ArduinoPin en la protoboard (Reto 4).
    /// Asignar este método a un botón "Reconectar Cable" en el Inspector.
    /// </summary>
    public void FixLooseCable()
    {
        if (circuit == null) return;

        foreach (var comp in circuit.components)
        {
            if (comp is ArduinoPin pin && pin.hasLooseCable)
            {
                pin.FixLooseCable();
                circuit.MarkDirty();
                gameManager?.RegisterRepairAction();
                GameSession.Instance?.ReportarCableReparado();
                Debug.Log("[TechnicianActions] Cable suelto reconectado.");
                return;
            }
        }

        Debug.Log("[TechnicianActions] No se encontró cable suelto en el circuito activo.");
        if (!demoMode) RegisterError("Acción reconectar cable sin cable suelto presente");
    }

    /// <summary>
    /// Repara la rama rota del circuito paralelo (Reto 2).
    /// </summary>
    public void FixParallelCircuit()
    {
        if (circuit == null) return;

        if (!demoMode && instructionSystem != null && !instructionSystem.CanRepairParallel())
        {
            Debug.Log("[TechnicianActions] Primero mide la rama rota.");
            RegisterError("Intento de reparar paralelo antes de medir");
            return;
        }

        // Los LEDs del Reto 2 son piezas FIJAS (no están en slots), así que iterar circuit.components
        // no encontraba la rama rota. Buscar en la ESCENA el LED de la rama abierta (resistencia
        // anómala / abierto) y restaurarlo. Se mantiene el camino por slots por compatibilidad.
        foreach (var l in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
        {
            if (l == null || l.nodeA == null || l.nodeB == null) continue;   // solo LEDs cableados
            if (l.isOpenCircuit || l.resistance > 1000f)
            { l.resistance = normalLedResistance; l.isOpenCircuit = false; }
        }
        foreach (var comp in circuit.components)
            if (comp is LED led && !led.isOn)
                led.resistance = normalLedResistance;

        circuit?.MarkDirty();
        foreach (var cm in FindObjectsByType<CircuitManager>(FindObjectsInactive.Exclude))
        { cm.MarkDirty(); cm.ForceSimulate(); }   // CircuitManager es quien pinta el LED en Reto 2
        gameManager?.RegisterRepairAction();

        // Sincronizar con el Explorador: enviar el LED de reemplazo con normalLedResistance
        // como valor. ComponentDeliverySystem lo aplica al circuito del Explorador al instalarlo.
        GameSession.Instance?.EnviarComponente(ComponentType.LED, normalLedResistance);

        Debug.Log("[TechnicianActions] Circuito paralelo reparado.");

        // Si en el Reto 2 también tienes un clon en la mesa que quieras borrar al dar clic:
        if (_selectedVisual != null)
        {
            Destroy(_selectedVisual.gameObject);
            _selectedVisual = null;
        }
        selectedComponent = null;
    }

    /// <summary>
    /// Aplica directamente un valor de resistencia al circuito.
    /// Usado por ComponentSendingTray en modo demo.
    /// </summary>
    public bool ApplyResistorValue(float value)
    {
        if (circuit == null) return false;

        foreach (var comp in circuit.components)
        {
            if (comp is Resistor r)
            {
                if (r.IsValueCorrect(value))
                {
                    r.resistance = value;
                    r.hasFault   = false;
                    circuit?.MarkDirty();
                    gameManager?.RegisterRepairAction();
                    Debug.Log($"[TechnicianActions] Resistencia {value} Ohm aplicada. LED cambiando.");
                    return true;
                }
                else
                {
                    RegisterError($"R incorrecta: {value} Ohm");
                    Debug.Log($"[TechnicianActions] Valor incorrecto: {value} Ohm. Correcto: {r.correctResistance} Ohm");
                    return false;
                }
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>Nombre del componente seleccionado actualmente.</summary>
    public string GetSelectedComponentName()
        => selectedComponent == null ? "None" : selectedComponent.name;

    /// <summary>True si el componente seleccionado es una Resistencia.</summary>
    public bool HasSelectedResistor() => selectedComponent is Resistor;

    void RegisterError(string reason)
    {
        // Normaliza el motivo a una categoría corta para el desglose "tipo de errores".
        string cat =
            (reason.Contains("R incorrecta") || reason.Contains("Ohm")) ? "Valor de resistencia" :
            reason.Contains("incorrect")                                ? "Selección incorrecta" :
            "Procedimiento";
        performance?.AddError(cat);
        Debug.Log($"[TechnicianActions] Error [{cat}]: {reason}");
    }
}