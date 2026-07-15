using System;

/// <summary>
/// Código de diagnóstico del circuito del Reto 4 (sandbox LED blink).
/// Se deriva de <see cref="SandboxValidationResult"/> en el lado del Explorador
/// y viaja como int por RPC hasta el Técnico, que reconstruye el texto.
/// </summary>
public enum Reto4Diagnostico
{
    Ninguno        = 0,
    SinBlink       = 1, // pin no OUTPUT o sketch sin parpadeo  → problema de CÓDIGO (Técnico)
    SinCamino      = 2, // no hay circuito completo pin→GND     → pin no coincide / incompleto
    LEDInvertido   = 3, // LED con la polaridad invertida
    SinLED         = 4, // hay camino al pin pero sin LED
    SinResistencia = 5, // falta resistencia de protección (el LED se quemará)
    CorrienteAlta  = 6, // corriente sobre el límite seguro del LED
    Generico       = 7
}

/// <summary>
/// Sistema de feedback graduado en 3 niveles para el Reto 4 (apoyo de aprendizaje
/// con andamiaje: síntoma → pista de zona → diagnóstico explícito).
///
///   Nivel 1 (1er fallo):     síntoma — remite a la telemetría/multímetro, sin revelar la causa.
///   Nivel 2 (2º-3er fallo):  pista de zona — en forma de pregunta, orienta sin dar la respuesta.
///   Nivel 3 (4º+ fallo):     diagnóstico explícito — nombra el pin y el arreglo exacto (red de seguridad).
///
/// La clasificación y el cálculo de nivel corren en el Explorador; el texto se construye
/// en el Técnico (un único lugar, sin enviar strings por la red).
/// </summary>
public static class Reto4Feedback
{
    /// <summary>Clasifica el resultado de validación en un código de diagnóstico.</summary>
    public static Reto4Diagnostico Clasificar(SandboxValidationResult r)
    {
        if (r.success)        return Reto4Diagnostico.Ninguno;
        if (!r.blinkEnabled)  return Reto4Diagnostico.SinBlink;   // incluye "pin no OUTPUT"
        if (!r.pathFound)
        {
            // El LED invertido y el "no llega a GND" comparten pathFound=false; los
            // distingue el mensaje del simulador (única pista fiable para este caso).
            if (!string.IsNullOrEmpty(r.message) &&
                r.message.IndexOf("invertid", StringComparison.OrdinalIgnoreCase) >= 0)
                return Reto4Diagnostico.LEDInvertido;
            return Reto4Diagnostico.SinCamino;
        }
        if (!r.hasLED)        return Reto4Diagnostico.SinLED;
        if (!r.hasProtection) return Reto4Diagnostico.SinResistencia;
        return Reto4Diagnostico.CorrienteAlta;                    // pasó todo menos la corriente segura
    }

    /// <summary>Nivel de ayuda según intentos fallidos: 1=síntoma, 2=pista de zona, 3=diagnóstico.</summary>
    public static int NivelPorIntentos(int intentos)
        => intentos <= 1 ? 1 : (intentos <= 3 ? 2 : 3);

    /// <summary>
    /// Texto (rich-text ASCII-safe) para la consola del Técnico. Devuelve null si no hay nada.
    /// </summary>
    public static string Construir(int nivel, int pin, Reto4Diagnostico motivo)
    {
        // ── Nivel 1: síntoma — sin revelar la causa, remite a los instrumentos ──
        if (nivel <= 1)
            return "<color=#FFAA00>> Validacion fallida. Revisa la telemetria (V / I / estado) " +
                   "y pide al Explorador medir con el multimetro.</color>";

        // ── Nivel 2: pista de zona (en forma de pregunta) ──
        if (nivel == 2)
        {
            string p = motivo switch
            {
                Reto4Diagnostico.SinBlink       => "el circuito podria estar bien, pero revisa TU codigo: ¿algun pin OUTPUT tiene una salida activa ahora (HIGH fijo, PWM, o parpadeo)?",
                Reto4Diagnostico.SinCamino      => "la corriente no completa el circuito. ¿El pin que programaste es el mismo donde el Explorador conecto el LED?",
                Reto4Diagnostico.LEDInvertido   => "algo bloquea el paso de corriente. ¿El LED podria estar al reves?",
                Reto4Diagnostico.SinLED         => "hay conexion hasta el pin, pero no se detecta el LED. ¿Esta bien insertado en la protoboard?",
                Reto4Diagnostico.SinResistencia => "¡cuidado! parece faltar la resistencia de proteccion antes del LED.",
                Reto4Diagnostico.CorrienteAlta  => "pasa demasiada corriente. ¿La resistencia es suficiente para proteger el LED?",
                _                               => "el circuito aun no valida. Usen el multimetro para ubicar donde se corta.",
            };
            return $"<color=#FFD27F>> PISTA: {p}</color>";
        }

        // ── Nivel 3: diagnóstico explícito (pin + arreglo exacto) ──
        string d = motivo switch
        {
            Reto4Diagnostico.SinBlink       => $"ningun pin OUTPUT esta activo ahora mismo. Revisa que loop() escriba HIGH o PWM en el pin D{pin} (parpadeo, PWM, o HIGH fijo son validos).",
            Reto4Diagnostico.SinCamino      => $"tu codigo activa el pin D{pin}, pero no hay circuito completo desde D{pin} hasta GND. Confirma con el Explorador que el LED + resistencia esten en el pin D{pin}.",
            Reto4Diagnostico.LEDInvertido   => $"el LED del pin D{pin} esta con la polaridad invertida. La pata larga (anodo) va del lado del pin.",
            Reto4Diagnostico.SinLED         => $"el pin D{pin} si llega a GND, pero sin LED en el camino. Pide al Explorador insertar el LED entre el pin y GND.",
            Reto4Diagnostico.SinResistencia => $"falta resistencia de proteccion (>=100 ohm) en el pin D{pin}. Sin ella el LED se quema. Pide una resistencia en serie.",
            Reto4Diagnostico.CorrienteAlta  => $"la corriente en el pin D{pin} supera el limite seguro del LED. Pide una resistencia mayor (330 ohm recomendado).",
            _                               => $"el circuito del pin D{pin} aun no cumple el objetivo. Revisen LED, resistencia y polaridad.",
        };
        return $"<color=#FF8888>> DIAGNOSTICO: {d}</color>";
    }
}
