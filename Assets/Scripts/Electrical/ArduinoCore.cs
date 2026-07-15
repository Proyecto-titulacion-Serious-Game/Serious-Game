using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Emulador del procesador ATmega328P de una placa Arduino.
/// Simula el ciclo loop() de Arduino de forma continua mediante una corrutina.
///
/// Funciones soportadas:
///   - digitalWrite(pin, HIGH/LOW) → salida TTL / 0 V
///   - digitalRead(pin)            → lee estado de un pin digital
///   - analogRead(A0)              → ADC 10 bits: mapea 0–5 V a 0–1023
///   - delay(ms)                   → pausa el ciclo simulado
/// </summary>
public class ArduinoCore : MonoBehaviour, ArduinoInterpreter.IBoard
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Nodos eléctricos (pines físicos)")]
    [Tooltip("ElectricalNode del pin D13 (salida digital principal).")]
    public ElectricalNode nodoP13;
    [Tooltip("ElectricalNode de tierra (GND).")]
    public ElectricalNode nodoGND;
    [Tooltip("ElectricalNode del pin A0 (entrada analógica del sensor).")]
    public ElectricalNode nodoA0;

    [Header("Configuración del sketch cargado")]
    [Tooltip("Número de pin activo (configurado vía ArduinoIDEUI).")]
    public int      activePinNumber = 13;
    [Tooltip("Modo del pin activo.")]
    public PinMode  activePinMode   = PinMode.OUTPUT;
    [Tooltip("Estado del pin activo en modo OUTPUT.")]
    public PinState activePinState  = PinState.LOW;
    [Tooltip("Activar blink (alterna HIGH/LOW según los intervalos).")]
    public bool     blinkEnabled    = false;
    
    // Tiempos de encendido y apagado separados para onda asimétrica
    [SerializeField] private int _blinkIntervalOnMs  = 500;
    [SerializeField] private int _blinkIntervalOffMs = 500;

    [Header("Mapa de pines digitales (sandbox — Reto 4)")]
    [Tooltip("Mapea número de pin → ElectricalNode en el modelo 3D del Arduino.")]
    public List<PinNodeMapping> pinNodeMap = new List<PinNodeMapping>();

    [Header("Hardware")]
    [Tooltip("Voltaje TTL de salida cuando el pin está en HIGH (normalmente 5 V).")]
    public float outputVoltageTTL = 5f;

    [Header("Telemetría (solo lectura)")]
    [SerializeField] private int   _adcValue;
    [SerializeField] private float _outputVoltage;

    // ─────────────────────────────────────────────
    //  Propiedades públicas
    // ─────────────────────────────────────────────
    public int   AdcValue       => _adcValue;
    public float OutputVoltage  => _outputVoltage;
    
    // Lectura pública de los tiempos para el CircuitSimulator
    public int   blinkIntervalOnMs  => _blinkIntervalOnMs;
    public int   blinkIntervalOffMs => _blinkIntervalOffMs;

    // ─────────────────────────────────────────────
    //  Aliases de compatibilidad (API antigua)
    // ─────────────────────────────────────────────
    public ElectricalNode pin13Node { get => nodoP13; set => nodoP13 = value; }
    public ElectricalNode gndNode   { get => nodoGND; set => nodoGND = value; }
    public ElectricalNode a0Node    { get => nodoA0;  set => nodoA0  = value; }

    // ─────────────────────────────────────────────
    //  Evento
    // ─────────────────────────────────────────────
    public static event Action<int> OnAdcSampleReady;

    /// <summary>Salida de Serial.print del programa (la consola del IDE se suscribe).</summary>
    public static event Action<string> OnProgramSerial;
    /// <summary>Errores de compilación/ejecución del programa libre.</summary>
    public static event Action<string> OnProgramError;

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private bool                _blinkState = true; // Inicia en HIGH (pin legacy)
    private ProtoboardSimulator _protoSim;   // Motor del Reto 4 (sandbox)

    // MULTI-PIN: estado independiente de cada pin OUTPUT del sketch (semáforos, secuencias…).
    private readonly List<ActivePinState> _pins = new List<ActivePinState>();

    // ── Modo PROGRAMA (intérprete Arduino libre — Reto 4) ────────────────
    private ArduinoInterpreter _interp;
    private bool       _programMode;
    private Coroutine  _programCo;
    private float      _programStartTime;
    // Salida actual de cada pin escrito por el programa: duty 0..1 (PWM) + nivel lógico.
    private readonly Dictionary<int, PinOut> _programPins = new Dictionary<int, PinOut>();
    private struct PinOut { public bool high; public float duty01; }

    // Detección de "parpadeo" para la telemetría del Técnico (sin parsear texto):
    // si un pin OUTPUT alterna su nivel, se considera blink en ese pin.
    private readonly Dictionary<int, bool> _lastLevel = new Dictionary<int, bool>();
    private int   _blinkPin = -1;
    private float _blinkLastToggle = -999f;

    // Tracking POR PIN (independiente del blink de arriba) para el validador libre del Reto 4:
    // cualquier pin que haya estado activo (HIGH o PWM>0) recientemente cuenta como "en juego",
    // sin exigir que parpadee ni limitarse a un único pin global.
    const float RECENT_PIN_WINDOW = 6f;   // cubre un ciclo típico de delay()-based (p.ej. semáforo)
    private readonly Dictionary<int, float> _lastDrivenTime = new Dictionary<int, float>();

    /// <summary>Pines que estuvieron activos (HIGH/PWM&gt;0) en los últimos <paramref name="windowSeconds"/>.
    /// Lo usa <see cref="ProtoboardSimulator"/> para validar el Reto 4 sin depender de un único pin.</summary>
    public IEnumerable<int> PinsRecentlyDriven(float windowSeconds = RECENT_PIN_WINDOW)
    {
        float now = Time.time;
        foreach (var kv in _lastDrivenTime)
            if (now - kv.Value < windowSeconds) yield return kv.Key;
    }

    /// <summary>Estado runtime de un pin de salida (su propio blink).</summary>
    private class ActivePinState
    {
        public int   pin;
        public bool  isHigh;     // estado fijo cuando NO hay blink
        public bool  blink;
        public int   onMs, offMs;
        public bool  phaseOn = true;
        public float nextToggle;
        public bool  CurrentlyHigh => blink ? phaseOn : isHigh;
    }

    /// <summary>Pines de salida y su voltaje relativo (duty01: 0=LOW, 1=5V, intermedio=PWM).
    /// Lo usa la simulación para tratar cada pin encendido como una fuente independiente.</summary>
    public IEnumerable<(ElectricalNode node, bool high, float duty01)> ActivePinStates()
    {
        if (_programMode)
        {
            foreach (var kv in _programPins)
            {
                var n = PinToNode(kv.Key);
                if (n != null) yield return (n, kv.Value.duty01 > 0f, kv.Value.duty01);
            }
            yield break;
        }
        foreach (var p in _pins)
        {
            var n = PinToNode(p.pin);
            if (n != null) yield return (n, p.CurrentlyHigh, p.CurrentlyHigh ? 1f : 0f);
        }
    }

    /// <summary>True si hay al menos un pin de salida configurado (multi-pin o programa).</summary>
    public bool HasActivePins => _programMode ? _programPins.Count > 0 : _pins.Count > 0;

    // ─────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────
    void OnEnable()
    {
        if (nodoGND != null) nodoGND.voltage = 0f;
        AutoDiscoverPinNodes();
        StartCoroutine(ArduinoLoop());
    }

    void OnDisable() { _programMode = false; _programCo = null; StopAllCoroutines(); }

    // ─────────────────────────────────────────────
    //  Ciclo simulado (equivale al loop() de Arduino)
    // ─────────────────────────────────────────────
    IEnumerator ArduinoLoop()
    {
        var tick = new WaitForSeconds(0.05f);   // 20 Hz: resuelve cualquier ritmo de blink por pin
        while (true)
        {
            _adcValue = AnalogRead();
            OnAdcSampleReady?.Invoke(_adcValue);

            if (_programMode)
            {
                // En modo programa, el intérprete escribe los pines directamente.
                // Aquí solo refrescamos la "bandera de parpadeo" para la validación de victoria.
                UpdateBlinkFlag();
            }
            else
            {
                // MULTI-PIN (modo Bloques): cada pin de salida avanza su propio blink y escribe su voltaje.
                bool changed = false;
                float now = Time.time;
                foreach (var p in _pins)
                {
                    if (p.blink && now >= p.nextToggle)
                    {
                        p.phaseOn   = !p.phaseOn;
                        p.nextToggle = now + (p.phaseOn ? p.onMs : p.offMs) / 1000f;
                    }
                    if (WritePinVoltage(p.pin, p.CurrentlyHigh)) changed = true;
                }

                // Compatibilidad: mantener el _blinkState del pin legacy en sincronía.
                if (_pins.Count > 0) _blinkState = _pins[0].CurrentlyHigh;

                if (changed) MarkProtoboardDirty();
            }

            yield return tick;
        }
    }

    /// <summary>Escribe el voltaje de un pin a su nodo; devuelve true si cambió.</summary>
    bool WritePinVoltage(int pin, bool high)
    {
        var node = PinToNode(pin);
        if (node == null) return false;
        float v = high ? outputVoltageTTL : 0f;
        if (high) _lastDrivenTime[pin] = Time.time;
        if (Mathf.Approximately(node.voltage, v)) return false;
        node.voltage = v;
        if (pin == activePinNumber) _outputVoltage = v;   // telemetría del pin principal
        return true;
    }

    // ─────────────────────────────────────────────
    //  API de simulación de pines
    // ─────────────────────────────────────────────

    public void DigitalWrite(int pin, bool high)
    {
        float newVoltage = high ? outputVoltageTTL : 0f;
        if (Mathf.Approximately(_outputVoltage, newVoltage)) return;

        _outputVoltage = newVoltage;
        ElectricalNode target = PinToNode(pin);
        if (target != null) target.voltage = _outputVoltage;

        MarkProtoboardDirty();
    }

    public bool DigitalRead(int pin)
    {
        ElectricalNode node = PinToNode(pin);
        return node != null && node.voltage >= 2.5f;
    }

    public int AnalogRead()
    {
        if (nodoA0 == null) return 0;
        float voltage = Mathf.Clamp(nodoA0.voltage, 0f, 5f);
        return Mathf.RoundToInt(voltage / 5f * 1023f);
    }

    // ─────────────────────────────────────────────
    //  API para ArduinoIDEUI / esquemas modernos
    // ─────────────────────────────────────────────

    public void LoadSketch(int pinNumber, PinMode mode, PinState state, bool blink, int blinkOnMs, int blinkOffMs)
    {
        // Un solo pin (compat). Si es OUTPUT, lo convertimos en la única entrada multi-pin.
        var pins = new List<ArduinoCodeParser.PinConfig>();
        if (mode == PinMode.OUTPUT)
            pins.Add(new ArduinoCodeParser.PinConfig
            {
                pin = pinNumber, isHigh = state == PinState.HIGH,
                blink = blink, blinkOnMs = blinkOnMs, blinkOffMs = blinkOffMs
            });

        LoadSketchMulti(pins, pinNumber, mode, state, blink, blinkOnMs, blinkOffMs);
    }

    /// <summary>
    /// Carga un sketch MULTI-PIN: cada pin de salida puede tener su propio HIGH/LOW/BLINK
    /// independiente (semáforos, secuencias, varios LEDs selectivos). El primer pin se usa
    /// además como "pin principal" para la telemetría y la validación.
    /// </summary>
    public void LoadSketchMulti(List<ArduinoCodeParser.PinConfig> configs,
                                int mainPin, PinMode mainMode, PinState mainState,
                                bool mainBlink, int mainOnMs, int mainOffMs)
    {
        StopProgram();   // el modo Bloques reemplaza a cualquier programa libre en curso
        _pins.Clear();
        float now = Time.time;
        if (configs != null)
        {
            foreach (var c in configs)
                _pins.Add(new ActivePinState
                {
                    pin = c.pin, isHigh = c.isHigh, blink = c.blink,
                    onMs = Mathf.Max(50, c.blinkOnMs), offMs = Mathf.Max(50, c.blinkOffMs),
                    phaseOn = true, nextToggle = now + Mathf.Max(50, c.blinkOnMs) / 1000f
                });
        }

        // Pin principal (telemetría + validación legacy).
        activePinNumber     = mainPin;
        activePinMode       = mainMode;
        activePinState      = mainState;
        blinkEnabled        = mainBlink;
        _blinkIntervalOnMs  = mainOnMs;
        _blinkIntervalOffMs = mainOffMs;
        _blinkState         = true;

        Debug.Log($"[ArduinoCore] Sketch MULTI-PIN cargado: {_pins.Count} pin(es) de salida. " +
                  $"Principal=D{mainPin} ({mainMode}, blink={mainBlink}).");
        MarkProtoboardDirty();
    }

    // ─────────────────────────────────────────────
    //  API de compatibilidad (ArduinoNetworkBridge)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Recibe el sketch del Técnico por red utilizando los 6 parámetros asimétricos.
    /// </summary>
    public void RecibirCodigoDePC(int pin, bool isOutput, bool isHigh, int delayOnMs, int delayOffMs, bool isBlink)
    {
        LoadSketch(
            pinNumber:  pin,
            mode:       isOutput ? PinMode.OUTPUT : PinMode.INPUT,
            state:      isHigh   ? PinState.HIGH  : PinState.LOW,
            blink:      isBlink,
            blinkOnMs:  delayOnMs,
            blinkOffMs: delayOffMs
        );

        MarkProtoboardDirty();
    }

    public int GetAnalogReadA0() => _adcValue;

    // ═════════════════════════════════════════════
    //  Modo PROGRAMA — intérprete Arduino libre (Reto 4)
    // ═════════════════════════════════════════════

    /// <summary>True si hay un programa libre compilado y corriendo.</summary>
    public bool ProgramRunning => _programMode;

    /// <summary>
    /// Compila y arranca un sketch LIBRE (texto completo) con el intérprete.
    /// Reemplaza cualquier modo Bloques o programa anterior. Si no compila, no cambia nada
    /// (los errores van por <see cref="OnProgramError"/>).
    /// </summary>
    public void LoadSketchProgram(string code)
    {
        StopProgram();
        if (string.IsNullOrWhiteSpace(code)) return;

        _pins.Clear();
        _programPins.Clear();
        _lastLevel.Clear();
        _blinkPin = -1;
        _blinkLastToggle = -999f;

        _interp = new ArduinoInterpreter(this)
        {
            OnSerial = s => OnProgramSerial?.Invoke(s),
            OnError  = s => { Debug.LogWarning("[Arduino] " + s, this); OnProgramError?.Invoke(s); }
        };

        if (!_interp.Compile(code)) { _programMode = false; return; }

        _programMode      = true;
        _programStartTime = Time.time;
        _interp.Start();

        if (isActiveAndEnabled) _programCo = StartCoroutine(ProgramCoroutine());
        MarkProtoboardDirty();
        Debug.Log("[ArduinoCore] Programa libre compilado y en ejecución.", this);
    }

    /// <summary>Solo compila (para el botón "Compilar"): reporta errores sin ejecutar.</summary>
    public bool CompileOnly(string code, out string error)
    {
        error = null;
        string captured = null;
        var probe = new ArduinoInterpreter(this) { OnError = s => captured = s };
        bool ok = probe.Compile(code);
        if (!ok) error = captured;
        return ok;
    }

    /// <summary>Detiene el programa libre en curso (si lo hay).</summary>
    public void StopProgram()
    {
        _programMode = false;
        if (_programCo != null) { StopCoroutine(_programCo); _programCo = null; }
    }

    IEnumerator ProgramCoroutine()
    {
        // setup() una vez
        foreach (var sig in _interp.RunSetup())
            yield return sig.Kind == ArduinoInterpreter.SignalKind.Wait
                ? (object)new WaitForSeconds(sig.Seconds) : null;

        // loop() para siempre
        while (_programMode)
        {
            foreach (var sig in _interp.RunLoop())
                yield return sig.Kind == ArduinoInterpreter.SignalKind.Wait
                    ? (object)new WaitForSeconds(sig.Seconds) : null;
            yield return null;   // cede al menos un frame por iteración de loop()
        }
    }

    // ── ArduinoInterpreter.IBoard ────────────────────────────────────────
    void ArduinoInterpreter.IBoard.PinMode(int pin, int mode)
    {
        if (mode == 1) // OUTPUT
        {
            if (!_programPins.ContainsKey(pin))
                _programPins[pin] = new PinOut { high = false, duty01 = 0f };
            activePinMode = PinMode.OUTPUT;
        }
    }

    void ArduinoInterpreter.IBoard.DigitalWrite(int pin, bool high)
        => SetProgramPin(pin, high ? 1f : 0f);

    void ArduinoInterpreter.IBoard.AnalogWrite(int pin, int duty)
        => SetProgramPin(pin, Mathf.Clamp01(duty / 255f));

    bool ArduinoInterpreter.IBoard.DigitalRead(int pin)
    {
        var node = PinToNode(pin);
        return node != null && node.voltage >= 2.5f;
    }

    int  ArduinoInterpreter.IBoard.AnalogRead(int pin) => _adcValue;

    long ArduinoInterpreter.IBoard.MillisNow()
        => (long)((Time.time - _programStartTime) * 1000f);

    /// <summary>Fija la salida de un pin (duty 0..1) y detecta parpadeo para la validación.</summary>
    void SetProgramPin(int pin, float duty01)
    {
        bool level = duty01 > 0f;
        _programPins[pin] = new PinOut { high = level, duty01 = duty01 };

        var node = PinToNode(pin);
        if (node != null) node.voltage = duty01 * outputVoltageTTL;
        if (pin == activePinNumber) _outputVoltage = duty01 * outputVoltageTTL;

        // Detección de parpadeo (para telemetría del Técnico): un pin que alterna su nivel = blink.
        if (_lastLevel.TryGetValue(pin, out bool prev) && prev != level)
        {
            _blinkPin = pin;
            _blinkLastToggle = Time.time;
        }
        _lastLevel[pin] = level;

        if (level) _lastDrivenTime[pin] = Time.time;

        MarkProtoboardDirty();
    }

    /// <summary>Refresca blinkEnabled/activePin para que EvaluateSandbox reconozca el objetivo.</summary>
    void UpdateBlinkFlag()
    {
        bool blinking = (Time.time - _blinkLastToggle) < 3f && _blinkPin >= 2;
        blinkEnabled = blinking;
        if (blinking)
        {
            activePinNumber = _blinkPin;
            activePinMode   = PinMode.OUTPUT;
        }
    }

    /// <summary>
    /// Marca sucio el motor del Reto 4 para que recalcule el MNA.
    /// </summary>
    void MarkProtoboardDirty()
    {
        if (_protoSim == null)
            _protoSim = FindAnyObjectByType<ProtoboardSimulator>();
        _protoSim?.MarkDirty();
    }

    // ─────────────────────────────────────────────
    //  Utilidades
    // ─────────────────────────────────────────────

    /// <summary>
    /// Resuelve un número de pin a su ElectricalNode en el modelo 3D.
    /// </summary>
    public ElectricalNode PinToNode(int pin)
    {
        foreach (var m in pinNodeMap)
            if (m.pin == pin) return m.node;

        // Compatibilidad legacy: pin 13 siempre disponible via nodoP13
        if (pin == 13) return nodoP13;

        // Fallback: Si no hay mapa, cualquier pin cae a nodoP13
        if (pinNodeMap.Count == 0) return nodoP13;

        return null;
    }

    /// <summary>
    /// Escanea la jerarquía buscando componentes 'ArduinoPin' leyendo su variable 'isDigital' y 'pinNumber'.
    /// Esto permite que los prefabs modulares actúen como pines reales.
    /// </summary>
    void AutoDiscoverPinNodes()
    {
        bool faltanNodos = pinNodeMap.Count == 0;
        foreach (var m in pinNodeMap) if (m.node == null) { faltanNodos = true; break; }
        if (!faltanNodos && nodoGND != null) return;

        // 1. Buscar todos los scripts ArduinoPin adjuntos al modelo 3D
        var pinesFisicos = GetComponentsInChildren<ArduinoPin>(true);

        foreach (var pinFisico in pinesFisicos)
        {
            // Leer directamente las variables del script ArduinoPin.cs
            bool esDigital = pinFisico.isDigital;
            int numPin = pinFisico.pinNumber;
            
            // Buscar el nodo eléctrico en ese mismo objeto prefab
            var nodoElectrico = pinFisico.GetComponent<ElectricalNode>();
            
            if (nodoElectrico == null) continue;

            // Llenar el mapa de pines digitales (2 al 13)
            if (esDigital && numPin >= 2 && numPin <= 13)
            {
                if (PinToNode(numPin) == null) 
                {
                    RegisterPinNode(numPin, nodoElectrico);
                }
                
                // Actualizar alias si es el pin 13
                if (numPin == 13 && nodoP13 == null) nodoP13 = nodoElectrico;
            }
            // Mapear entradas analógicas asumiendo que isDigital es false y pinNumber es 0
            else if (!esDigital && numPin == 0 && nodoA0 == null)
            {
                nodoA0 = nodoElectrico;
            }
            // Mapear los GND evaluando si el nombre del GameObject contiene "GND"
            else if (pinFisico.gameObject.name.ToUpper().Contains("GND") && nodoGND == null)
            {
                nodoGND = nodoElectrico;
            }
        }

        // Fallback de compatibilidad si no encuentra los ArduinoPin o faltan referencias
        if (nodoGND == null) nodoGND = FindNearestNamedNode("GND");
        if (nodoP13 == null) { var n13 = PinToNode(13); if (n13 != null) nodoP13 = n13; }

        int validos = 0;
        foreach (var m in pinNodeMap) if (m.node != null) validos++;
        Debug.Log($"[ArduinoCore] Auto-enlace adaptado a prefabs ArduinoPin: {validos}/{pinNodeMap.Count} pines, " +
                  $"GND={(nodoGND != null)}, P13={(nodoP13 != null)}.", this);
    }

    /// <summary>
    /// Busca el nodo más cercano que contenga el texto buscado en su nombre.
    /// </summary>
    ElectricalNode FindNearestNamedNode(string contains)
    {
        ElectricalNode Buscar(ElectricalNode[] nodos)
        {
            ElectricalNode best = null; float bestD = float.MaxValue;
            foreach (var n in nodos)
            {
                if (n.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (n.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }
        return Buscar(GetComponentsInChildren<ElectricalNode>(true))
            ?? Buscar(FindObjectsByType<ElectricalNode>(FindObjectsInactive.Include));
    }

    /// <summary>
    /// Registra en runtime la asociación pin → nodo.
    /// </summary>
    public void RegisterPinNode(int pin, ElectricalNode node)
    {
        for (int i = 0; i < pinNodeMap.Count; i++)
        {
            if (pinNodeMap[i].pin == pin)
            {
                var entry = pinNodeMap[i];
                entry.node = node;
                pinNodeMap[i] = entry;
                return;
            }
        }
        pinNodeMap.Add(new PinNodeMapping { pin = pin, node = node });
    }
}

// ─────────────────────────────────────────────
//  Enums y structs auxiliares
// ─────────────────────────────────────────────
public enum PinMode  { OUTPUT, INPUT, INPUT_PULLUP }
public enum PinState { LOW, HIGH }

/// <summary>
/// Asociación entre número de pin digital Arduino y su nodo eléctrico en el modelo 3D.
/// Se serializa en el Inspector de <see cref="ArduinoCore.pinNodeMap"/>.
/// </summary>
[Serializable]
public struct PinNodeMapping
{
    [Tooltip("Número de pin digital Arduino (2–13).")]
    public int            pin;
    [Tooltip("ElectricalNode del header físico de ese pin en el modelo 3D.")]
    public ElectricalNode node;
}