using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Motor de diagnóstico del circuito para el panel del Técnico.
/// Clase pura (sin MonoBehaviour) — se instancia desde TechnicianUI.cs con:
///     private DiagnosticSystem _diagnostic = new DiagnosticSystem();
///
/// El DiagnosticSystemHolder en la jerarquía puede estar vacío —
/// este script NO necesita estar adjunto a ningún GameObject.
/// </summary>
public class DiagnosticSystem
{
    // ─────────────────────────────────────────────
    //  RETO 1 — Ley de Ohm (resistencia vs. objetivo)
    // ─────────────────────────────────────────────

    /// <summary>Diagnóstico del Reto 1: explica el voltaje/corriente del LED en función de la
    /// resistencia colocada frente al valor objetivo (850 Ohm, ver TechnicianManual). Lee los
    /// mismos componentes scene-wide que <c>GameManager.CumpleVictoriaRetos123</c>, así el
    /// diagnóstico nunca contradice la condición de victoria real.</summary>
    public string GetDiagnosisOhmLaw()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- RETO 1: LEY DE OHM --");

        var resistor = FindFirstConnected<Resistor>();
        var led      = FindFirstConnected<LED>();

        if (resistor == null)
        {
            sb.AppendLine("[!] No hay resistencia conectada en el circuito.");
        }
        else
        {
            sb.AppendLine($"Resistencia colocada: {resistor.resistance:F0} Ohm  " +
                          $"{(!resistor.hasFault ? "[OK]" : "[!] INCORRECTA")}");
            if (resistor.hasFault)
            {
                sb.AppendLine($"   Valor correcto: {resistor.correctResistance:F0} Ohm");
                sb.AppendLine(resistor.resistance < resistor.correctResistance
                    ? "   Muy BAJA -> pasa demasiada corriente, el LED se sobrecarga."
                    : "   Muy ALTA -> pasa muy poca corriente, el LED no enciende.");
            }
        }

        if (led == null)
        {
            sb.AppendLine("[!] No hay LED conectado en el circuito.");
        }
        else
        {
            float va = led.nodeA != null ? led.nodeA.voltage : 0f;
            float vb = led.nodeB != null ? led.nodeB.voltage : 0f;
            sb.AppendLine($"LED: {(led.isOn ? "ENCENDIDO" : "APAGADO")} {GetLEDStateIcon(led.state)}");
            sb.AppendLine($"   Voltaje en el LED: {Mathf.Abs(va - vb):F2} V");
            sb.AppendLine($"   Corriente: {led.current * 1000f:F1} mA (objetivo: 10 mA)");
        }

        if (resistor != null && !resistor.hasFault && led != null && led.isOn && led.state == LEDState.Correct)
            sb.AppendLine("\n[OK] Circuito correcto: 850 Ohm entrega ~10 mA al LED.");

        return sb.ToString().TrimEnd();
    }

    public string GetNextActionOhmLaw()
    {
        var resistor = FindFirstConnected<Resistor>();
        if (resistor != null && resistor.hasFault)
            return $"Resistencia incorrecta ({resistor.resistance:F0} Ohm).\n" +
                   $"Escribe {resistor.correctResistance:F0} en el campo y pulsa ENVIAR.";

        var led = FindFirstConnected<LED>();
        if (led == null)
            return "Di al Explorador que conecte el LED al circuito.";
        if (!led.isOn)
            return "El LED no enciende. Verifica que fuente, resistencia y LED formen un circuito cerrado.";
        if (led.state == LEDState.Overload || led.state == LEDState.NearOverload)
            return "Corriente alta. Sube el valor de la resistencia.";

        return "OK El circuito esta correcto. Reto completado!";
    }

    // ─────────────────────────────────────────────
    //  RETO 2 — Paralelo (voltaje por rama, 2 LEDs)
    // ─────────────────────────────────────────────

    /// <summary>Diagnóstico del Reto 2: una rama por LED — dice si cada slot conectado transmite
    /// voltaje y por qué esa rama no enciende (cable suelto, polaridad, sobrecarga). Igual que en
    /// Reto 1, lee LEDs scene-wide (no <c>circuit.components</c>: el CircuitManager viejo queda
    /// apagado en este reto, ver <see cref="Reto2CircuitGuard"/>).</summary>
    public string GetDiagnosisParallel()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- RETO 2: CIRCUITO PARALELO --");

        var leds = new List<LED>();
        foreach (var l in UnityEngine.Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            if (l != null && l.nodeA != null && l.nodeB != null) leds.Add(l);

        if (leds.Count == 0)
        {
            sb.AppendLine("[!] No hay LEDs conectados todavia.");
            return sb.ToString().TrimEnd();
        }

        int ok = 0;
        for (int i = 0; i < leds.Count; i++)
        {
            var l = leds[i];
            float va = l.nodeA != null ? l.nodeA.voltage : 0f;
            float vb = l.nodeB != null ? l.nodeB.voltage : 0f;
            float vRama = Mathf.Abs(va - vb);
            bool ramaOk = l.isOn && l.state == LEDState.Correct;
            if (ramaOk) ok++;

            sb.AppendLine($"Rama {i + 1} ({l.name}): {(ramaOk ? "[OK]" : "[!]")}");
            sb.AppendLine($"   Voltaje transmitido al slot: {vRama:F2} V");
            sb.AppendLine($"   Corriente: {l.current * 1000f:F1} mA");
            if (!ramaOk)
            {
                if (vRama < 0.5f)
                    sb.AppendLine("   Sin voltaje -> cable suelto o rama abierta (revisa esa rama).");
                else if (l.polarityInverted)
                    sb.AppendLine("   LED con polaridad invertida -> girarlo 180.");
                else if (l.state == LEDState.Overload || l.state == LEDState.NearOverload)
                    sb.AppendLine("   Corriente alta -> falta resistencia de proteccion en esa rama.");
                else
                    sb.AppendLine("   Revisar que el LED este bien insertado en su slot.");
            }
        }

        sb.AppendLine($"\nRamas correctas: {ok}/{leds.Count}");

        // Cables fisicos (jumpers que el Explorador conecta a mano) — reporte explicito por cable,
        // no solo inferido del voltaje del LED. Cada CableElectricalBridge tiene 2 puntas (start/end);
        // "conectado" = enchufada en un slot/borne real (Connector.IsConnected).
        var cables = UnityEngine.Object.FindObjectsByType<CableElectricalBridge>(FindObjectsInactive.Exclude);
        if (cables.Length > 0)
        {
            sb.AppendLine("\nCABLES FISICOS:");
            int completos = 0;
            foreach (var cable in cables)
            {
                bool puntaA = cable.start != null && cable.start.IsConnected;
                bool puntaB = cable.end   != null && cable.end.IsConnected;
                if (puntaA && puntaB) completos++;
                string estado = (puntaA && puntaB) ? "[OK] conectado en ambas puntas"
                               : (puntaA || puntaB) ? "[!] solo una punta conectada — falta la otra"
                               : "[!] sin conectar";
                sb.AppendLine($"  {cable.name}: {estado}");
            }
            sb.AppendLine($"  Total: {completos}/{cables.Length} cables cerrando el circuito.");
        }

        return sb.ToString().TrimEnd();
    }

    public string GetNextActionParallel()
    {
        // Prioridad 1: cables sueltos — sin esto no vale la pena revisar LEDs (nada tendra voltaje).
        foreach (var cable in UnityEngine.Object.FindObjectsByType<CableElectricalBridge>(FindObjectsInactive.Exclude))
        {
            bool puntaA = cable.start != null && cable.start.IsConnected;
            bool puntaB = cable.end   != null && cable.end.IsConnected;
            if (!puntaA || !puntaB)
                return $"Di al Explorador que conecte el cable '{cable.name}' " +
                       $"({(!puntaA && !puntaB ? "ambas puntas sueltas" : "le falta una punta")}) en un slot de la protoboard.";
        }

        foreach (var l in UnityEngine.Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
        {
            if (l == null || l.nodeA == null || l.nodeB == null) continue;

            if (l.polarityInverted)
                return $"Di al Explorador que gire el LED '{l.name}' 180 grados (polaridad).";

            float va = l.nodeA.voltage, vb = l.nodeB.voltage;
            if (Mathf.Abs(va - vb) < 0.5f)
                return $"La rama de '{l.name}' no tiene voltaje. Revisa que los cables esten en los rieles correctos.";

            if (l.state == LEDState.Overload || l.state == LEDState.NearOverload)
                return $"Corriente alta en '{l.name}'. Verifica la resistencia de proteccion de esa rama.";
        }
        return "OK Ambas ramas del paralelo estan encendidas de forma segura.";
    }

    // ─────────────────────────────────────────────
    //  RETO 3 — Mixto (polaridad por componente)
    // ─────────────────────────────────────────────

    /// <summary>Diagnóstico del Reto 3: un veredicto de polaridad por CADA componente (resistor,
    /// LED, capacitor), scene-wide, igual que <c>GameManager.CumpleVictoriaRetos123</c> (caso
    /// Mixed) para no contradecir la condición de victoria real.</summary>
    public string GetDiagnosisMixed()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- RETO 3: POLARIDAD Y VALORES --");
        bool any = false;

        foreach (var r in UnityEngine.Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
        {
            if (r == null || r.nodeA == null || r.nodeB == null) continue;
            any = true;
            sb.AppendLine($"Resistencia '{r.name}': {r.resistance:F0} Ohm  " +
                          (r.hasFault ? $"[!] INCORRECTA (correcto: {r.correctResistance:F0} Ohm)" : "[OK]"));
        }
        foreach (var led in UnityEngine.Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
        {
            if (led == null || led.nodeA == null || led.nodeB == null) continue;
            any = true;
            sb.AppendLine($"LED '{led.name}': " +
                          (led.polarityInverted ? "[!] POLARIDAD INVERTIDA"
                           : led.state == LEDState.Overload ? "[!] SOBRECARGA"
                           : !led.isOn ? "[!] apagado"
                           : "[OK] polaridad correcta"));
        }
        foreach (var cap in UnityEngine.Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
        {
            if (cap == null || cap.nodeA == null || cap.nodeB == null) continue;
            any = true;
            sb.AppendLine($"Capacitor '{cap.name}': " +
                          (cap.polarityInverted ? "[!] POLARIDAD INVERTIDA" : "[OK] polaridad correcta"));
        }

        if (!any) return "[!] Sin componentes conectados todavia.";
        return sb.ToString().TrimEnd();
    }

    public string GetNextActionMixed()
    {
        foreach (var cap in UnityEngine.Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
            if (cap != null && cap.nodeA != null && cap.polarityInverted)
                return $"[!!] URGENTE: di al Explorador que gire el capacitor '{cap.name}' 180 grados.";

        foreach (var led in UnityEngine.Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            if (led != null && led.nodeA != null && led.polarityInverted)
                return $"Di al Explorador que gire el LED '{led.name}' 180 grados (polaridad).";

        foreach (var led in UnityEngine.Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            if (led != null && led.nodeA != null && led.state == LEDState.Overload)
                return $"Corriente alta en '{led.name}'. Sube el valor de la resistencia en serie.";

        foreach (var r in UnityEngine.Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
            if (r != null && r.nodeA != null && r.hasFault)
                return $"Resistencia '{r.name}' incorrecta ({r.resistance:F0} Ohm). Correcto: {r.correctResistance:F0} Ohm.";

        return "OK Todos los componentes tienen la polaridad y valores correctos.";
    }

    /// <summary>Primer componente conectado (con ambos nodos asignados) del tipo dado, scene-wide.</summary>
    static T FindFirstConnected<T>() where T : ElectricalComponent
    {
        foreach (var c in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude))
            if (c != null && c.nodeA != null && c.nodeB != null) return c;
        return null;
    }

    // ─────────────────────────────────────────────
    //  RETO 4 — Diagnóstico Arduino
    // ─────────────────────────────────────────────

    /// <summary>Diagnóstico principal para Reto 4 (Arduino + Protoboard).</summary>
    public string GetDiagnosisArduino(ArduinoCore arduino, ProtoboardSimulator protoSim)
    {
        if (arduino == null && protoSim == null)
            return "[!] Arduino no inicializado.\n    Avanza al Reto 4 primero.";

        var sb = new StringBuilder();

        // ── Sketch cargado ──────────────────────────────────────────
        if (arduino != null)
        {
            bool sketchReady = arduino.activePinMode == PinMode.OUTPUT && arduino.PinsRecentlyDriven().Any();
            sb.AppendLine("-- SKETCH CARGADO --");
            sb.AppendLine($"Pin activo  : D{arduino.activePinNumber}  " +
                          $"{(sketchReady ? "[OK]" : "[!] Sketch incompleto (ningun pin OUTPUT activo)")}");
            sb.AppendLine($"Modo        : {arduino.activePinMode}");
            sb.AppendLine($"Estado      : {(arduino.blinkEnabled ? $"BLINK {arduino.activePinNumber}" : arduino.activePinState.ToString())}");
            sb.AppendLine($"Voltaje pin : {arduino.OutputVoltage:F2} V");
        }

        // ── Protoboard ───────────────────────────────────────────────
        if (protoSim != null)
        {
            sb.AppendLine("\n-- PROTOBOARD --");
            sb.AppendLine($"Corriente : {protoSim.totalCurrentmA:F1} mA" +
                          $"{(protoSim.totalCurrentmA > 25f ? "  [!] SOBRECARGA" : "")}");
            sb.AppendLine($"Potencia  : {protoSim.totalPowerW * 1000f:F1} mW");
            if (protoSim.isOpenCircuit)
                sb.AppendLine("[!] CIRCUITO ABIERTO — verifica resistor y cable.");
        }

        // ── Fallas pendientes ────────────────────────────────────────
        var faults = GetArduinoFaults(arduino, protoSim);
        if (faults.Count > 0)
        {
            sb.AppendLine("\n-- FALLAS PENDIENTES --");
            foreach (var f in faults) sb.AppendLine(f);
        }
        else
        {
            sb.AppendLine("\n[OK] Sin fallas. Pide al Explorador\n     que presione el boton fisico.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Guía de siguiente acción para Reto 4 Sandbox.</summary>
    public string GetNextActionArduino(ArduinoCore arduino, ProtoboardSimulator protoSim)
    {
        if (arduino == null)
            return "Abre el IDE del monitor, escribe el sketch y pulsa SUBIR.";

        // 1. Sketch sin configurar
        if (arduino.activePinMode != PinMode.OUTPUT)
            return "SKETCH: agrega 'pinMode(X, OUTPUT)' en setup(). Pulsa COMPILAR y SUBIR.";

        if (!arduino.PinsRecentlyDriven().Any())
            return $"SKETCH: ningun pin OUTPUT esta activo (pin D{arduino.activePinNumber}).\n" +
                   "Agrega HIGH, PWM, o parpadeo en loop(). Pulsa SUBIR.";

        // 2. Circuito abierto
        if (protoSim != null && protoSim.isOpenCircuit)
            return $"Di al Explorador: conecta LED y resistencia desde el pin D{arduino.activePinNumber} hasta GND.";

        // 3. Sobrecarga
        if (protoSim != null && protoSim.totalCurrentmA > 20f)
            return $"[!] Corriente alta ({protoSim.totalCurrentmA:F0} mA).\n" +
                   "Di al Explorador: aumenta la resistencia (>= 330 Ohm recomendado).";

        return $"OK: Pin D{arduino.activePinNumber} activo, circuito detectado.\n" +
               "Pide al Explorador presionar el boton fisico VR.";
    }

    List<string> GetArduinoFaults(ArduinoCore arduino, ProtoboardSimulator protoSim)
    {
        var faults = new List<string>();

        if (arduino != null && !(arduino.activePinMode == PinMode.OUTPUT && arduino.PinsRecentlyDriven().Any()))
            faults.Add($"[1] Sketch: pin D{arduino.activePinNumber} incompleto (ningun pin OUTPUT activo)");

        if (protoSim != null && protoSim.isOpenCircuit)
            faults.Add("[2] Protoboard: circuito abierto (resistor o cable)");
        else if (protoSim != null && protoSim.totalCurrentmA > 25f)
            faults.Add("[2] Protoboard: sobrecarga — resistencia incorrecta");

        var pin = UnityEngine.Object.FindAnyObjectByType<ArduinoPin>();
        if (pin != null && pin.hasLooseCable)
            faults.Add("[3] Cable suelto detectado en protoboard");

        return faults;
    }

    // ─────────────────────────────────────────────
    //  Helpers privados
    // ─────────────────────────────────────────────

    string GetLEDStateIcon(LEDState state) => state switch
    {
        LEDState.Correct      => "[OK]",
        LEDState.NearOverload => "[~]",
        LEDState.Overload     => "[X]",
        _                     => "[.]"
    };
}
