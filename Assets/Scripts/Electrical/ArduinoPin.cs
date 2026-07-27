using UnityEngine;

/// <summary>
/// Representa un Pin de salida del Arduino para el Reto 4.
/// Actúa como fuente de voltaje dinámica (soporta PWM).
/// </summary>
public class ArduinoPin : ElectricalComponent
{
    [Header("Configuración del Pin")]
    public int pinNumber;
    public int correctPinNumber = 4; // El pin objetivo del Reto 4
    public bool hasFault = false;
    public bool hasLooseCable = false;

    [Header("Salida Eléctrica")]
    [Tooltip("Voltaje real que está entregando el pin en este instante")]
    public float pinVoltage = 0.0f; 

    public override float GetResistance()
    {
        // Un pin activo tiene resistencia interna casi nula.
        // Si el cable está suelto o quemado, se abre el circuito.
        return (hasLooseCable || isOpenCircuit) ? 1_000_000f : 0.1f;
    }

    public override void Calculate()
    {
        if (nodeA == null || nodeB == null) return;

        // La corriente fluirá en base a la resistencia que tenga el resto del circuito.
        float voltageDiff = nodeA.voltage - nodeB.voltage;
        current = (hasLooseCable || isOpenCircuit) ? 0f : voltageDiff / GetResistance();
        voltageDrop = voltageDiff;
    }

    // ⚡ NUEVO: Mapeo de señal PWM a voltaje físico (0-255 a 0V-5V)
    // Invocar este método desde ArduinoCore o ArduinoNetworkBridge
    public void SetPwmOutput(int pwmValue)
    {
        // 1. Limitar el valor a resolución de 8 bits
        pwmValue = Mathf.Clamp(pwmValue, 0, 255);
        
        // 2. Mapeo lineal
        float effectiveVoltage = 5.0f * (pwmValue / 255.0f);
        
        // 3. Aplicar voltaje al pin
        this.pinVoltage = effectiveVoltage;
        
        // Si el pin está inyectando voltaje en el nodo A del circuito
        if (nodeA != null) 
        {
            nodeA.voltage = this.pinVoltage;
        }

        // Forzar actualización física del circuito
        var circuit = GetComponentInParent<CircuitManager>();
        if (circuit != null) circuit.MarkDirty();
    }

    // Interacciones desde PlayerInteraction.cs
    public void RepairPin(int selectedPinNumber)
    {
        pinNumber = selectedPinNumber;
        hasFault = (pinNumber != correctPinNumber);
        
        var circuit = GetComponentInParent<CircuitManager>();
        if (circuit != null) circuit.MarkDirty();
    }

    public void FixLooseCable()
    {
        hasLooseCable = false;
        
        var circuit = GetComponentInParent<CircuitManager>();
        if (circuit != null) circuit.MarkDirty();
    }
}