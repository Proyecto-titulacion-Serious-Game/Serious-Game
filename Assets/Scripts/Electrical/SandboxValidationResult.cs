/// <summary>
/// Resultado del validador dinámico del Reto 4 (sandbox LED blink).
/// Emitido vía <see cref="ProtoboardSimulator.OnSandboxValidated"/>.
///
/// Tipo de datos puro (no MonoBehaviour). Vive en su propio archivo para que
/// Unity asocie correctamente el MonoScript de <c>CircuitSimulator.cs</c> con el
/// MonoBehaviour <see cref="ProtoboardSimulator"/> y no con este struct (lo cual
/// disparaba el error "missing the class attribute 'ExtensionOfNativeClass'").
/// </summary>
public struct SandboxValidationResult
{
    /// <summary>True si el circuito cumple TODOS los criterios del objetivo.</summary>
    public bool   success;
    /// <summary>Número de pin digital activado por ArduinoCore.</summary>
    public int    activatedPin;
    /// <summary>True si ArduinoCore tiene blinkEnabled activo.</summary>
    public bool   blinkEnabled;
    /// <summary>True si la búsqueda encontró un camino desde el pin hasta GND.</summary>
    public bool   pathFound;
    /// <summary>True si el camino contiene al menos un LED.</summary>
    public bool   hasLED;
    /// <summary>True si el LED está con la polaridad correcta (no invertido).</summary>
    public bool   ledForwardBiased;
    /// <summary>True si hay una resistencia >= 100 Ω en el camino.</summary>
    public bool   hasProtection;
    /// <summary>True si el LED está presente pero apagado por corriente insuficiente
    /// (resistencia excesiva) — lo opuesto de una sobrecarga, no un camino roto.</summary>
    public bool   ledUnderCurrent;
    /// <summary>Corriente estimada en mA según resistencia total del camino.</summary>
    public float  currentMa;
    /// <summary>Mensaje legible para mostrar en el HUD o consola del IDE.</summary>
    public string message;
}
