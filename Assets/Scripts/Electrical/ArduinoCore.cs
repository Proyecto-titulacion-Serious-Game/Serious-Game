using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PinNodeMapping
{
    public int pin;
    public ElectricalNode node;
}

public struct PinStateData
{
    public ElectricalNode node;
    public float duty01;
}

/// <summary>
/// Proxy inteligente para ProgramRunning que permite compatibilidad dual:
/// se comporta como bool (para operadores '!'. etc.) y como delegado (Func<bool>).
/// </summary>
public readonly struct ProgramRunningProxy
{
    private readonly bool _value;
    public ProgramRunningProxy(bool value) => _value = value;

    public static implicit operator bool(ProgramRunningProxy proxy) => proxy._value;
    public static implicit operator Func<bool>(ProgramRunningProxy proxy) => () => proxy._value;
    public static bool operator !(ProgramRunningProxy proxy) => !proxy._value;
}

/// <summary>
/// Núcleo lógico del Arduino virtual (Reto 4). Ejecuta el sketch REAL del Técnico con
/// <see cref="ArduinoInterpreter"/> (setup()/loop() cooperativos, delay() real) y expone el
/// resultado en el modelo físico de pines (<see cref="pinNodeMap"/> + <see cref="activePin"/>)
/// que consume <see cref="ProtoboardSimulator"/> para la simulación MULTI-PIN y la validación
/// del sandbox.
/// </summary>
public class ArduinoCore : MonoBehaviour, ArduinoInterpreter.IBoard
{
    [Header("Hardware Físico")]
    public ArduinoPin activePin;
    public float outputVoltageTTL = 5f;
    public ElectricalNode nodoGND;

    [Header("Mapeo de Pines")]
    public List<PinNodeMapping> pinNodeMap = new List<PinNodeMapping>();

    // Firmas legacy requeridas por los Test del Editor
    public ElectricalNode nodoP13;
    public ElectricalNode nodoA0;

    [Header("Telemetría (Solo Lectura)")]
    public int activePinNumber = 4;
    public PinMode activePinMode = PinMode.INPUT;
    public PinState activePinState = PinState.LOW;
    public bool blinkEnabled = false;
    public int AdcValue = 0;

    public float OutputVoltage => activePin != null ? activePin.pinVoltage : 0f;

    public static event Action<string> OnProgramSerial;
    /// <summary>Errores de compilación/ejecución del programa libre (los escucha ArduinoIDEUI).</summary>
    public static event Action<string> OnProgramError;

    // ── Programa (intérprete real Arduino — sketch libre del Técnico) ──────────────
    private ArduinoInterpreter _interp;
    private Coroutine          _programCo;

    // Estado por pin: duty01 actual (0=LOW, 1=HIGH/255, intermedio=PWM) y último instante escrito.
    // Reemplaza al "activePin único" para soportar sketches con varios pines OUTPUT a la vez
    // (semáforos, secuencias) — ActivePinStates()/PinsRecentlyDriven() reportan TODOS los pines.
    private readonly Dictionary<int, float> _pinDuty        = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _lastDrivenTime = new Dictionary<int, float>();
    private readonly Dictionary<int, bool>  _lastLevel      = new Dictionary<int, bool>();

    // Pines que cuentan como "activos recientemente" para la validación del sandbox, aunque un
    // blink esté momentáneamente en fase OFF (mismo criterio que el motor anterior).
    const float RECENT_PIN_WINDOW = 6f;

    void Awake()
    {
        if (activePin != null && activePin.nodeA != null)
            RegisterPinNode(activePin.correctPinNumber, activePin.nodeA);
    }

    void OnDisable()
    {
        if (_programCo != null) StopCoroutine(_programCo);
        _programCo = null;
    }

    // ─────────────────────────────────────────────
    // SKETCH LIBRE — compila y ejecuta el código real del Técnico
    // ─────────────────────────────────────────────

    /// <summary>
    /// Compila y arranca el sketch recibido (chunked por red, por ArduinoNetworkBridge, o local).
    /// En Play Mode corre setup()+loop() en una corrutina real (delay() de verdad); en Editor
    /// (tests headless) el llamador drena manualmente RunSetup()/RunLoop() vía <see cref="_interp"/>.
    /// </summary>
    public void LoadSketchProgram(string program)
    {
        if (_programCo != null) { StopCoroutine(_programCo); _programCo = null; }
        _pinDuty.Clear(); _lastDrivenTime.Clear(); _lastLevel.Clear();
        activePinMode = PinMode.INPUT;
        blinkEnabled  = false;

        _interp = new ArduinoInterpreter(this);
        _interp.OnSerial += s => OnProgramSerial?.Invoke(s);
        _interp.OnError  += s => OnProgramError?.Invoke(s);

        OnProgramSerial?.Invoke("Programa en C++ recibido. Compilando...");

        if (!_interp.Compile(program))
            return;   // OnError ya avisó el motivo exacto (sintaxis/compilación)

        _interp.Start();   // variables globales

        if (Application.isPlaying)
            _programCo = StartCoroutine(RunProgramLoop());
    }

    IEnumerator RunProgramLoop()
    {
        foreach (var sig in _interp.RunSetup()) yield return ToYield(sig);

        while (true)
        {
            bool anyStep = false;
            foreach (var sig in _interp.RunLoop())
            {
                anyStep = true;
                yield return ToYield(sig);
            }
            // loop() que nunca cede (sin delay ni Signal.Tick) → ceder un frame para no colgar Unity.
            if (!anyStep) yield return null;
        }
    }

    static object ToYield(ArduinoInterpreter.Signal sig)
        => sig.Kind == ArduinoInterpreter.SignalKind.Wait ? (object)new WaitForSeconds(sig.Seconds) : null;

    /// <summary>True si el sketch compiló correctamente (independiente de si setup()/loop() ya corrieron).</summary>
    public ProgramRunningProxy ProgramRunning => new ProgramRunningProxy(_interp != null && _interp.Compiled);

    // ─────────────────────────────────────────────
    // ArduinoInterpreter.IBoard — el sketch controla el hardware a través de esta interfaz
    // ─────────────────────────────────────────────

    void ArduinoInterpreter.IBoard.PinMode(int pin, int mode)
    {
        activePinNumber = pin;
        activePinMode   = mode == 1 ? PinMode.OUTPUT : (mode == 2 ? PinMode.INPUT_PULLUP : PinMode.INPUT);

        if (activePin != null)
        {
            activePin.pinNumber = pin;
            activePin.hasFault  = (pin != activePin.correctPinNumber);
        }
    }

    void ArduinoInterpreter.IBoard.DigitalWrite(int pin, bool high) => WritePin(pin, high ? 1f : 0f);
    void ArduinoInterpreter.IBoard.AnalogWrite(int pin, int duty)   => WritePin(pin, Mathf.Clamp01(duty / 255f));
    bool ArduinoInterpreter.IBoard.DigitalRead(int pin) => _pinDuty.TryGetValue(pin, out float d) && d > 0f;
    int  ArduinoInterpreter.IBoard.AnalogRead(int pin)  => AdcValue;
    long ArduinoInterpreter.IBoard.MillisNow()          => (long)(Time.time * 1000f);

    void WritePin(int pin, float duty01)
    {
        _pinDuty[pin]        = duty01;
        _lastDrivenTime[pin] = Time.time;
        activePinNumber      = pin;
        activePinState       = duty01 > 0f ? PinState.HIGH : PinState.LOW;

        bool level = duty01 > 0f;
        if (_lastLevel.TryGetValue(pin, out bool prevLevel) && prevLevel != level) blinkEnabled = true;
        _lastLevel[pin] = level;

        // Físico: si este pin es el mismo que el ArduinoPin inspeccionable de la escena, refleja
        // el voltaje ahí también (multímetro / lectura visual del header).
        if (activePin != null && activePin.correctPinNumber == pin)
            activePin.SetPwmOutput(Mathf.RoundToInt(duty01 * 255f));
    }

    /// <summary>Pines de salida y su duty01 ACTUAL (0=LOW, 1=HIGH/255, intermedio=PWM), para que
    /// ProtoboardSimulator resuelva el MNA tratando cada pin encendido como fuente independiente.</summary>
    public List<PinStateData> ActivePinStates()
    {
        var list = new List<PinStateData>();
        foreach (var kv in _pinDuty)
        {
            if (kv.Value <= 0f) continue;
            var node = PinToNode(kv.Key);
            if (node != null) list.Add(new PinStateData { node = node, duty01 = kv.Value });
        }
        return list;
    }

    /// <summary>Pines que estuvieron activos (HIGH o PWM&gt;0) en los últimos
    /// <see cref="RECENT_PIN_WINDOW"/> segundos — usado por la validación del sandbox para no
    /// exigir que un blink esté en fase ON en el instante exacto de comprobar.</summary>
    public List<int> PinsRecentlyDriven()
    {
        float now = Time.time;
        var list = new List<int>();
        foreach (var kv in _lastDrivenTime)
            if (now - kv.Value < RECENT_PIN_WINDOW) list.Add(kv.Key);
        return list;
    }

    public int GetAnalogReadA0() => AdcValue;

    public ElectricalNode PinToNode(int pin)
    {
        foreach (var mapping in pinNodeMap)
            if (mapping.pin == pin) return mapping.node;
        return null;
    }

    public void UpdateADC(float sensorVoltage)
    {
        AdcValue = Mathf.Clamp(Mathf.RoundToInt((sensorVoltage / 5f) * 1023f), 0, 1023);
    }

    public void RegisterPinNode(int pin, ElectricalNode node)
    {
        for (int i = 0; i < pinNodeMap.Count; i++)
        {
            if (pinNodeMap[i].pin == pin)
            {
                var map = pinNodeMap[i];
                map.node = node;
                pinNodeMap[i] = map;
                return;
            }
        }
        pinNodeMap.Add(new PinNodeMapping { pin = pin, node = node });
    }

    // ─────────────────────────────────────────────
    // MODO SIMPLE (red) — pin/estado pre-derivados desde el selector del IDE (sin código libre)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Aplica un sketch ya PARSEADO por texto (<see cref="ArduinoCodeParser"/>, canal
    /// <c>ArduinoNetworkBridge.DeliverSketchText</c>) — sin correr el intérprete. Soporta
    /// MULTI-PIN real: aplica el estado de TODOS los pines de <paramref name="pinConfigs"/>
    /// (semáforos/secuencias), no solo el principal. El blink de este canal es un estado fijo
    /// ON (sin animación en el tiempo) — para blink animado real usa <see cref="LoadSketchProgram"/>.
    /// </summary>
    public void LoadSketchMulti(object pinConfigs, int pin, PinMode mode, PinState state,
                                 bool blink, int onMs, int offMs)
    {
        if (_programCo != null) { StopCoroutine(_programCo); _programCo = null; }
        _interp = null;
        _pinDuty.Clear(); _lastDrivenTime.Clear(); _lastLevel.Clear();

        activePinNumber = pin;
        activePinMode   = mode;
        activePinState  = state;
        blinkEnabled    = blink;

        if (activePin != null)
        {
            activePin.pinNumber = pin;
            activePin.hasFault  = (pin != activePin.correctPinNumber);
        }

        if (mode != PinMode.OUTPUT)
        {
            OnProgramSerial?.Invoke($"Modo INPUT configurado en D{pin}.");
            return;
        }

        if (pinConfigs is List<ArduinoCodeParser.PinConfig> configs && configs.Count > 0)
        {
            foreach (var cfg in configs)
                WritePin(cfg.pin, (cfg.isHigh || cfg.blink) ? 1f : 0f);
        }
        else
        {
            WritePin(pin, (state == PinState.HIGH || blink) ? 1f : 0f);
        }

        OnProgramSerial?.Invoke(blink
            ? $"Iniciando rutina BLINK en D{pin} ({onMs}ms)."
            : $"Pin D{pin} fijado estáticamente en {state}.");
    }

    public void RecibirCodigoDePC(int pin, bool isOutput, bool isHigh, int delayOnMs, int delayOffMs, bool isBlink)
    {
        // Desconecta cualquier sketch libre en curso: este modo pisa el estado del pin directamente.
        if (_programCo != null) { StopCoroutine(_programCo); _programCo = null; }
        _interp = null;

        activePinNumber = pin;
        activePinMode   = isOutput ? PinMode.OUTPUT : PinMode.INPUT;
        activePinState  = isHigh ? PinState.HIGH : PinState.LOW;
        blinkEnabled    = isBlink;

        if (activePin != null)
        {
            activePin.pinNumber = pin;
            activePin.hasFault  = (pin != activePin.correctPinNumber);
        }

        if (!isOutput)
        {
            WritePin(pin, 0f);
            OnProgramSerial?.Invoke($"Modo INPUT configurado en D{pin}.");
            return;
        }

        WritePin(pin, isHigh || isBlink ? 1f : 0f);
        OnProgramSerial?.Invoke(isBlink
            ? $"Iniciando rutina BLINK en D{pin} ({delayOnMs}ms)."
            : $"Pin D{pin} fijado estáticamente en {activePinState}.");
    }

    // ─────────────────────────────────────────────
    // MÉTODOS LEGACY (compatibilidad con llamadas directas fuera del intérprete)
    // ─────────────────────────────────────────────

    public void DigitalWrite(int pin, bool isHigh) => WritePin(pin, isHigh ? 1f : 0f);
    public void DigitalWrite(int pin, PinState state) => DigitalWrite(pin, state == PinState.HIGH);
}
