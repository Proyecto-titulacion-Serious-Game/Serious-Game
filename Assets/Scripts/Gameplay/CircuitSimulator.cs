using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Motor central de simulación analítica para los retos del juego.
/// Calcula de forma dinámica voltajes, corrientes y estados de falla.
/// VERSIÓN INTEGRAL: Incluye puentes de compatibilidad para el Técnico y sistemas heredados.
/// </summary>
public class CircuitSimulator : MonoBehaviour
{
    [Header("Matriz de Trabajo")]
    public List<ComponentSlot> todosLosSlots = new List<ComponentSlot>();

    private bool        _isDirty = true;
    private GameManager _cachedGM;

    // ─────────────────────────────────────────────
    //  Puente al motor Electrical (CircuitManager)
    // ─────────────────────────────────────────────
    /// <summary>
    /// Devuelve el <see cref="CircuitManager"/> (motor Electrical) que convive en el mismo
    /// GameObject que este simulador de slots. Los HUD del Técnico (TechnicianWorkstation) y del
    /// Explorador (ExplorerCircuitPanel) leen la telemetría y la lista real de componentes desde
    /// ese motor, que es el que pinta el LED y detecta los componentes FIJOS en los Retos 1-3
    /// (el motor de slots no los ve, ver memoria 'retos123_componentes_fijos').
    ///
    /// Reemplaza al antiguo <c>implicit operator CircuitManager(CircuitSimulator)</c>: aquella
    /// conversión implícita podía dispararse en CUALQUIER asignación sin verse en el código.
    /// Ahora la resolución es explícita y rastreable. Conserva el fallback de crear un
    /// CircuitManager si el objeto no lo tiene, para no alterar el comportamiento previo.
    /// </summary>
    public CircuitManager GetCompanionCircuitManager()
    {
        CircuitManager cm = GetComponent<CircuitManager>();
        if (cm == null) cm = gameObject.AddComponent<CircuitManager>();
        return cm;
    }

    // ─────────────────────────────────────────────
    //  PUENTES DE VARIABLES ELÉCTRICAS (Fix CS1061)
    // ─────────────────────────────────────────────
    [Header("Variables Globales de Red (Telemetría para Técnico)")]
    public float sourceVoltage = 9f;    // Voltaje de la fuente (V) — Usado por ObjectiveSystem
    public float totalCurrent = 0.02f;  // Amperaje total (I) — Usado por ComponentSendingTray y ObjectiveSystem
    public float totalPower = 0.18f;    // Potencia de la malla (W) — Usado por ComponentSendingTray
    public bool isShortCircuited = false; // Estado de cortocircuito — Usado por CircuitAudioManager y Técnico

    private List<ElectricalComponent> _legacyComponents = new List<ElectricalComponent>();
    
    /// <summary> Propiedad puente para simular la lista antigua de componentes. </summary>
    public List<ElectricalComponent> components
    {
        get
        {
            _legacyComponents.Clear();
            foreach (var slot in todosLosSlots)
            {
                if (slot != null && slot.InstalledObject != null)
                {
                    if (slot.InstalledObject.TryGetComponent<ElectricalComponent>(out var comp))
                    {
                        _legacyComponents.Add(comp);
                    }
                }
            }
            return _legacyComponents;
        }
    }

    public void AutoDetectComponents()
    {
        // En el nuevo sistema Sandbox, el escaneo es automático por la matriz de slots
        MarkDirty();
    }

    // ─────────────────────────────────────────────
    //  Unity Lifecycle y Advertencias (Fix CS0618)
    // ─────────────────────────────────────────────
    void Awake()
    {
        // FIX: Se removió FindObjectsSortMode que estaba obsoleto en las nuevas versiones de Unity
        if (todosLosSlots.Count == 0)
        {
            todosLosSlots.AddRange(FindObjectsByType<ComponentSlot>(FindObjectsInactive.Include));
        }
    }

    /// <summary>
    /// Marca el circuito para indicar que requiere un recálculo matemático.
    /// </summary>
    public void MarkDirty() => _isDirty = true;

    /// <summary>
    /// Ejecuta el análisis de parámetros eléctricos basándose en las leyes de la electrónica.
    /// </summary>
    public void ForceSimulate()
    {
        if (!_isDirty) return;
        _isDirty = false;

        Debug.Log("[CircuitSimulator] Actualizando análisis nodal y cálculo de mallas...");

        // Paso 1: Inicializar telemetría base de la simulación
        isShortCircuited = false;
        totalCurrent = 0f;
        totalPower = 0f;

        // Resetear estados eléctricos básicos
        foreach (var slot in todosLosSlots)
        {
            if (slot == null || slot.InstalledObject == null) continue;

            if (slot.InstalledObject.TryGetComponent<LED>(out var led))
            {
                led.isOn = false;
                if (led.nodeA != null && led.nodeB != null)
                {
                    led.nodeA.voltage = 0f;
                    led.nodeB.voltage = 0f;
                    led.Calculate();
                }
            }
        }

        // Paso 2: Resolver la matriz según el tipo de reto activo
        if (_cachedGM == null) _cachedGM = FindAnyObjectByType<GameManager>();
        GameManager gm = _cachedGM;
        if (gm == null) return;

        switch (gm.currentLevel)
        {
            case LevelType.OhmLaw:
                SimularReto1_Ohm(gm);
                break;
            case LevelType.Parallel:
                SimularReto2_Parallel();
                break;
            case LevelType.Mixed:
                SimularReto3_Mixed();
                break;
            // Reto 4 usa el sandbox libre (ProtoboardSimulator.ForzarValidacion), no este motor.
        }
    }

    private void SimularReto1_Ohm(GameManager gm)
    {
        sourceVoltage = 9f;
        ComponentSlot slotR   = todosLosSlots.Find(s => s.targetElectricalValue == 850f);

        // Buscar el LED del reto. Primero en los slots; si no, como pieza FIJA del circuito
        // (el LED del Reto 1 normalmente está soldado al circuito, no se instala en un slot).
        LED           led     = null;
        foreach (var slot in todosLosSlots)
        {
            if (slot?.InstalledObject == null) continue;
            if (slot.InstalledObject.TryGetComponent<LED>(out var l)) { led = l; break; }
        }
        if (led == null) led = BuscarLEDActivoEnEscena();

        // Buscar el RESISTOR del reto. Primero en el slot 850; si no, como pieza FIJA del circuito
        // (en esta escena el resistor del Reto 1 NO está en un slot → hay que buscarlo en escena,
        // si no nunca se leía su valor real y se quedaba en 10Ω).
        Resistor res = null;
        if (slotR?.InstalledObject != null) slotR.InstalledObject.TryGetComponent<Resistor>(out res);
        if (res == null) res = BuscarResistorActivoEnEscena();

        // Sin resistor con nodos → apagar el LED y salir.
        if (res == null || res.nodeA == null || res.nodeB == null)
        {
            if (led != null && led.nodeA != null && led.nodeB != null)
            {
                led.nodeA.voltage = 0f; led.nodeB.voltage = 0f; led.Calculate();
            }
            return;
        }

        // ── Circuito serie correcto: V_src → Resistor → LED → GND ──────────
        float rRes   = Mathf.Max(res.resistance, 0.001f);
        float rLed   = (led != null) ? led.GetResistance() : 0f;
        float rTotal = rRes + rLed;

        if (rTotal < 0.1f) { isShortCircuited = true; return; }

        float I = sourceVoltage / rTotal;

        // Nodo de unión R-LED: voltaje después del resistor
        float vJuncion = sourceVoltage - I * rRes;

        // Voltajes y cálculo del Resistor
        res.nodeA.voltage = sourceVoltage;
        res.nodeB.voltage = vJuncion;
        res.Calculate();

        // Telemetría
        totalCurrent     = I;
        totalPower       = res.dissipatedPower;
        isShortCircuited = res.isOverloaded;

        // Voltajes y cálculo del LED (FIX principal: antes nunca se actualizaba)
        if (led != null && led.nodeA != null && led.nodeB != null)
        {
            led.nodeA.voltage = vJuncion;
            led.nodeB.voltage = 0f;
            led.Calculate();
        }
    }

    /// <summary>
    /// LED activo del reto que NO está instalado en un slot (pieza fija/soldada del circuito).
    /// Solo considera LEDs activos (el de la zona del reto actual) y con nodos asignados.
    /// </summary>
    private LED BuscarLEDActivoEnEscena()
    {
        foreach (var l in FindObjectsByType<LED>(FindObjectsInactive.Exclude))        {
            if (l == null) continue;
            if (l.nodeA == null || l.nodeB == null) continue;
            return l;
        }
        return null;
    }

    /// <summary>
    /// Resistor activo del reto cableado al circuito (con nodos). Ignora el resistor de la bandeja
    /// (que no tiene nodos asignados). Permite leer el valor real aunque no esté en un slot.
    /// </summary>
    private Resistor BuscarResistorActivoEnEscena()
    {
        foreach (var r in FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
        {
            if (r == null) continue;
            if (r.nodeA == null || r.nodeB == null) continue;
            return r;
        }
        return null;
    }

    private void SimularReto2_Parallel()
    {
        sourceVoltage = 5f;
        foreach (var slot in todosLosSlots)
        {
            if (slot != null && slot.InstalledObject != null)
            {
                if (slot.InstalledObject.TryGetComponent<LED>(out var led))
                {
                    if (led.nodeA != null && led.nodeB != null)
                    {
                        led.nodeA.voltage = led.polarityInverted ? 0f : sourceVoltage;
                        led.nodeB.voltage = led.polarityInverted ? sourceVoltage : 0f;
                        led.Calculate();

                        totalCurrent += led.current;
                    }
                }
            }
        }
        totalPower = sourceVoltage * totalCurrent;
    }

    private void SimularReto3_Mixed()
    {
        sourceVoltage = 6f;
        foreach (var slot in todosLosSlots)
        {
            if (slot != null && slot.InstalledObject != null)
            {
                if (slot.InstalledObject.TryGetComponent<Resistor>(out var res))
                {
                    if (res.nodeA != null && res.nodeB != null)
                    {
                        res.nodeA.voltage = sourceVoltage;
                        res.nodeB.voltage = 2f;
                        res.Calculate();
                        totalCurrent += res.current;
                    }
                }
            }
        }
        totalPower = sourceVoltage * totalCurrent;
    }

    public bool AreAllLEDsOn()
    {
        int ledsEncendidos = 0;
        int ledsTotales = 0;

        foreach (var slot in todosLosSlots)
        {
            if (slot != null && slot.InstalledObject != null && slot.InstalledObject.TryGetComponent<LED>(out var led))
            {
                ledsTotales++;
                if (led.isOn && led.state == LEDState.Correct) ledsEncendidos++;
            }
        }

        // Si no hay LEDs en slots (Retos 1-2: los LEDs son piezas FIJAS del circuito), contar los
        // LEDs CABLEADOS activos de la escena. Se ignoran los sin nodos (el de la bandeja de envío).
        if (ledsTotales == 0)
        {
            foreach (var led in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            {
                if (led == null || led.nodeA == null || led.nodeB == null) continue;
                ledsTotales++;
                if (led.isOn && led.state == LEDState.Correct) ledsEncendidos++;
            }
        }

        return ledsTotales > 0 && ledsEncendidos == ledsTotales;
    }
}