/// <summary>
/// Definiciones globales para los estados de los pines del Arduino.
/// Utilizados por el sistema de red, la interfaz y el motor de validación.
/// </summary>

public enum PinMode
{
    INPUT,
    OUTPUT,
    INPUT_PULLUP
}

public enum PinState
{
    LOW,
    HIGH
}