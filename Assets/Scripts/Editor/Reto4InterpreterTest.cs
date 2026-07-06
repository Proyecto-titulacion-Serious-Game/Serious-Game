using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verificación HEADLESS del intérprete Arduino del Reto 4 libre.
/// No necesita VR ni Play Mode: corre sketches reales contra una placa simulada (mock) y
/// comprueba secuencias multi-pin, analogWrite (PWM), suspensión por delay, el watchdog
/// de bucles sin delay y la salida de Serial.
///
/// Ejecutar:
///   Editor:     Tools → TITA → Reto 4 → Test de intérprete (headless)
///   Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4InterpreterTest.Run -logFile -
/// </summary>
public static class Reto4InterpreterTest
{
    // Placa simulada: registra escrituras de pin, duty y Serial.
    class MockBoard : ArduinoInterpreter.IBoard
    {
        public readonly Dictionary<int, int>  duty    = new();
        public readonly Dictionary<int, bool> level   = new();
        public readonly HashSet<int>          written = new();
        public readonly List<string>          serial  = new();
        long _ms;

        public void PinMode(int pin, int mode) { }
        public void DigitalWrite(int pin, bool high) { written.Add(pin); level[pin] = high; duty[pin] = high ? 255 : 0; }
        public void AnalogWrite(int pin, int d)      { written.Add(pin); duty[pin] = d; level[pin] = d > 0; }
        public bool DigitalRead(int pin) => level.TryGetValue(pin, out var v) && v;
        public int  AnalogRead(int pin)  => 512;
        public long MillisNow()          => _ms;
        public void Advance(long ms)     => _ms += ms;
    }

    [MenuItem("Tools/TITA/Reto 4/Test de intérprete (headless)")]
    public static void Run()
    {
        int fails = 0;
        Debug.Log("===== RETO 4 — TEST DEL INTÉRPRETE ARDUINO =====");

        // ── [1] Secuencia multi-pin con for + arreglo + delay ─────────────
        {
            var board = new MockBoard();
            var interp = new ArduinoInterpreter(board);
            string err = null; interp.OnError = e => err = e;

            string code = @"
                int pins[] = {2, 3, 4};
                void setup() { for (int i = 0; i < 3; i++) pinMode(pins[i], OUTPUT); }
                void loop() {
                    for (int i = 0; i < 3; i++) {
                        digitalWrite(pins[i], HIGH);
                        delay(100);
                        digitalWrite(pins[i], LOW);
                    }
                }";

            if (!interp.Compile(code)) { fails++; Debug.LogError($"[1] No compiló: {err}"); }
            else
            {
                interp.Start();
                Drain(interp.RunSetup(), board, 1000);
                int waits = CountWaits(interp.RunLoop(), board, 1000);

                Debug.Log($"[1] pines escritos = {{{string.Join(",", board.written)}}}, delays = {waits}");
                if (!(board.written.Contains(2) && board.written.Contains(3) && board.written.Contains(4)))
                { fails++; Debug.LogError("[1] FALLO: no se escribieron los 3 pines de la secuencia."); }
                if (waits != 3) { fails++; Debug.LogError($"[1] FALLO: se esperaban 3 delay(), hubo {waits}."); }
                if (err != null) { fails++; Debug.LogError($"[1] FALLO: error en ejecución: {err}"); }
            }
        }

        // ── [2] analogWrite fija el duty (PWM/brillo) ─────────────────────
        {
            var board = new MockBoard();
            var interp = new ArduinoInterpreter(board);
            interp.Compile("void setup(){ pinMode(9, OUTPUT); } void loop(){ analogWrite(9, 128); }");
            interp.Start();
            Drain(interp.RunSetup(), board, 100);
            Drain(interp.RunLoop(), board, 100);
            int d = board.duty.TryGetValue(9, out var v) ? v : -1;
            Debug.Log($"[2] duty(pin 9) = {d}");
            if (d != 128) { fails++; Debug.LogError($"[2] FALLO: analogWrite no fijó duty=128 (fue {d})."); }
        }

        // ── [3] Watchdog: while(true) sin delay NO cuelga ─────────────────
        {
            var board = new MockBoard();
            var interp = new ArduinoInterpreter(board);
            interp.Compile("void setup(){} void loop(){ int x = 0; while (true) { x = x + 1; } }");
            interp.Start();
            int signals = 0; bool sawTick = false;
            foreach (var s in interp.RunLoop())
            {
                if (s.Kind == ArduinoInterpreter.SignalKind.Tick) sawTick = true;
                if (++signals >= 5) break;   // si cuelga, nunca llegaríamos aquí
            }
            Debug.Log($"[3] señales obtenidas de un loop infinito = {signals} (tick={sawTick})");
            if (!sawTick) { fails++; Debug.LogError("[3] FALLO: el bucle sin delay no cedió (riesgo de congelar Unity)."); }
        }

        // ── [4] if/else + variables + Serial + millis ─────────────────────
        {
            var board = new MockBoard();
            var interp = new ArduinoInterpreter(board);
            string err = null; interp.OnError = e => err = e;
            interp.OnSerial = s => board.serial.Add(s.TrimEnd('\n'));
            interp.Compile(@"
                int contador = 0;
                void setup(){ Serial.begin(9600); }
                void loop(){
                    contador++;
                    if (contador % 2 == 0) { digitalWrite(7, HIGH); }
                    else { digitalWrite(7, LOW); }
                    Serial.println(contador);
                }");
            interp.Start();
            Drain(interp.RunSetup(), board, 100);
            Drain(interp.RunLoop(), board, 100);  // contador = 1 → LOW
            Drain(interp.RunLoop(), board, 100);  // contador = 2 → HIGH
            Debug.Log($"[4] serial = [{string.Join(",", board.serial)}], pin7 = {(board.level.TryGetValue(7, out var l7) && l7)}");
            if (err != null) { fails++; Debug.LogError($"[4] FALLO: {err}"); }
            if (!(board.level.TryGetValue(7, out var hi) && hi))
            { fails++; Debug.LogError("[4] FALLO: el if/else no dejó el pin 7 en HIGH en la 2ª pasada."); }
        }

        // ── [5] Error de sintaxis se reporta, no crashea ──────────────────
        {
            bool ok = ArduinoInterpreter.TryCompile("void loop( { digitalWrite(13 HIGH }", out string err);
            Debug.Log($"[5] compila código roto = {ok}, error = {err}");
            if (ok) { fails++; Debug.LogError("[5] FALLO: un sketch con sintaxis inválida 'compiló'."); }
        }

        // ── Resultado ─────────────────────────────────────────────────────
        if (fails == 0) Debug.Log("===== RESULTADO: ✓ TODOS LOS TESTS PASARON =====");
        else            Debug.LogError($"===== RESULTADO: ✗ {fails} TEST(S) FALLARON =====");

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    // Consume una secuencia de señales (simulando el avance del tiempo en cada delay).
    static void Drain(IEnumerable<ArduinoInterpreter.Signal> seq, MockBoard board, int cap)
    {
        int n = 0;
        foreach (var s in seq)
        {
            if (s.Kind == ArduinoInterpreter.SignalKind.Wait) board.Advance((long)(s.Seconds * 1000f));
            if (++n >= cap) break;
        }
    }

    static int CountWaits(IEnumerable<ArduinoInterpreter.Signal> seq, MockBoard board, int cap)
    {
        int waits = 0, n = 0;
        foreach (var s in seq)
        {
            if (s.Kind == ArduinoInterpreter.SignalKind.Wait) { waits++; board.Advance((long)(s.Seconds * 1000f)); }
            if (++n >= cap) break;
        }
        return waits;
    }
}
