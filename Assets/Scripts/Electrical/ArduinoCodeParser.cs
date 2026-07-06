using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Parsea texto libre estilo Arduino C++ y extrae los parámetros de sketch
/// necesarios para <see cref="ArduinoCore.LoadSketch"/>.
///
/// Tolerante a errores de sintaxis estudiantiles:
///   - Mayúsculas mezcladas: PINMODE, Pinmode, DigitalWrite, DIGITAL_WRITE...
///   - Notación de pin: D7, Pin 7, pin_7, 7
///   - LED_BUILTIN como alias de pin 13
///   - true/false/1/0 como alias de HIGH/LOW
///   - Semicolons faltantes
///   - delay() en setup() en vez de loop()
///   - Código con solo setup() (sin loop())
///
/// Uso:
///   var data = ArduinoCodeParser.Parse(codeEditor.text);
///   if (data.isValid) core.LoadSketch(data.pinNumber, ...);
/// </summary>
public static class ArduinoCodeParser
{
    // ─────────────────────────────────────────────
    //  Resultado del parseo (API pública — campo-compatible con versión anterior)
    // ─────────────────────────────────────────────

    public struct SketchData
    {
        /// <summary>True si se encontró al menos un pin OUTPUT válido.</summary>
        public bool    isValid;
        /// <summary>Número de pin digital detectado (2–13).</summary>
        public int     pinNumber;
        /// <summary>Modo del pin (OUTPUT / INPUT).</summary>
        public PinMode mode;
        /// <summary>Estado deseado cuando no hay blink.</summary>
        public PinState state;
        /// <summary>True si el código contiene un patrón HIGH/delay/LOW/delay (blink).</summary>
        public bool    blink;
        /// <summary>Tiempo encendido del blink en ms (primer delay del patrón).</summary>
        public int     blinkMs;
        /// <summary>Tiempo apagado del blink en ms (segundo delay). 0 = igual a blinkMs.</summary>
        public int     blinkOffMs;
        /// <summary>Mensaje principal para la consola del IDE (OK / WARN / ERROR).</summary>
        public string  log;
        /// <summary>Lista de advertencias pedagógicas adicionales.</summary>
        public List<string> warnings;
        /// <summary>TODOS los pines OUTPUT con acción detectados (multi-pin: semáforos, etc.).
        /// Incluye el pin principal. Vacía si no hay ninguno válido.</summary>
        public List<PinConfig> pins;
    }

    /// <summary>Configuración de UN pin de salida del sketch (para multi-pin).</summary>
    public struct PinConfig
    {
        public int      pin;
        public bool     isHigh;      // estado fijo cuando NO hay blink
        public bool     blink;
        public int      blinkOnMs;
        public int      blinkOffMs;
    }

    // ─────────────────────────────────────────────
    //  Constantes
    // ─────────────────────────────────────────────

    private const int PIN_MIN = 2;
    private const int PIN_MAX = 13;

    // ─────────────────────────────────────────────
    //  Patrones compilados
    // ─────────────────────────────────────────────

    // Paso 1 — limpieza
    private static readonly Regex _rxLineComment  = new(@"//[^\n]*",                   RegexOptions.Compiled);
    private static readonly Regex _rxBlockComment = new(@"/\*[\s\S]*?\*/",             RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex _rxDirective    = new(@"#[^\n]*",                    RegexOptions.Compiled);
    private static readonly Regex _rxStringLit    = new(@"""(?:[^""\\]|\\.)*""",       RegexOptions.Compiled);

    // Paso 2 — normalización de identificadores (aplicados en orden)
    private static readonly (Regex rx, string rep)[] _normalizers = {
        // Variantes de digitalWrite
        (new Regex(@"\b(?:DIGITAL_WRITE|digital_write|DigitalWrite|DIGITALWRITE)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "digitalWrite"),

        // Variantes de pinMode
        (new Regex(@"\b(?:PIN_MODE|pin_mode|PinMode|PINMODE)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "pinMode"),

        // LED_BUILTIN → 13
        (new Regex(@"\bLED_BUILTIN\b", RegexOptions.Compiled), "13"),

        // Prefijos de pin: D7 → 7, Pin 7 → 7, pin_7 → 7  (solo delante de dígitos)
        (new Regex(@"\b[Pp]in[_\s]*(?=\d)", RegexOptions.Compiled), ""),
        (new Regex(@"\b[Dd](?=\d\b)",        RegexOptions.Compiled), ""),

        // Alias HIGH/LOW
        (new Regex(@"\b(?:HIGH|true|TRUE|True|1)\b(?=\s*[,)])", RegexOptions.Compiled), "HIGH"),
        (new Regex(@"\b(?:LOW|false|FALSE|False|0)\b(?=\s*[,)])", RegexOptions.Compiled), "LOW"),

        // Alias OUTPUT/INPUT
        (new Regex(@"\bINPUT_PULLUP\b",      RegexOptions.Compiled | RegexOptions.IgnoreCase), "INPUT_PULLUP"),
        (new Regex(@"\b(?:OUTPUT|output|Output|OUT)\b", RegexOptions.Compiled), "OUTPUT"),
        (new Regex(@"\b(?:INPUT|input|Input|IN)\b",     RegexOptions.Compiled), "INPUT"),

        // Normalizar espacios alrededor de paréntesis y comas
        (new Regex(@"\s*\(\s*", RegexOptions.Compiled), "("),
        (new Regex(@"\s*\)\s*", RegexOptions.Compiled), ")"),
        (new Regex(@"\s*,\s*",  RegexOptions.Compiled), ","),
    };

    // Paso 3 — extracción
    private static readonly Regex _rxPinMode    = new(@"pinMode\((\d+),(OUTPUT|INPUT_PULLUP|INPUT)\)", RegexOptions.Compiled);
    private static readonly Regex _rxDigWrite   = new(@"digitalWrite\((\d+),(HIGH|LOW)\)",            RegexOptions.Compiled);
    private static readonly Regex _rxDelay      = new(@"\bdelay\((\d+)\)",                            RegexOptions.Compiled);

    // Patrón blink: HIGH delay(x) LOW delay(y) en el mismo bloque de texto
    private static readonly Regex _rxBlinkBlock = new(
        @"digitalWrite\((\d+),HIGH\).*?delay\((\d+)\).*?digitalWrite\(\1,LOW\).*?delay\((\d+)\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────

    /// <summary>
    /// Parsea <paramref name="code"/> y devuelve un <see cref="SketchData"/>.
    /// Nunca lanza excepciones — los errores quedan en <c>log</c> y <c>warnings</c>.
    /// </summary>
    public static SketchData Parse(string code)
    {
        var result = new SketchData
        {
            blinkMs    = 500,
            blinkOffMs = 0,
            mode       = PinMode.OUTPUT,
            state      = PinState.HIGH,
            warnings   = new List<string>()
        };

        if (string.IsNullOrWhiteSpace(code))
        {
            result.log = "ERROR: El editor está vacío. Escribe tu sketch de Arduino.";
            return result;
        }

        // ── Paso 1: Limpiar ruido ────────────────────────────────────────────
        string clean = StripNoise(code);

        // ── Paso 2: Normalizar tokens ────────────────────────────────────────
        string norm = Normalize(clean);

        // ── Paso 3: Extraer por pin en todo el código ────────────────────────
        var pinMap = new Dictionary<int, PinEntry>();

        foreach (Match m in _rxPinMode.Matches(norm))
        {
            if (!int.TryParse(m.Groups[1].Value, out int p)) continue;
            GetOrCreate(pinMap, p).mode = m.Groups[2].Value switch
            {
                "INPUT_PULLUP" => PinMode.INPUT_PULLUP,
                "INPUT"        => PinMode.INPUT,
                _              => PinMode.OUTPUT
            };
        }

        foreach (Match m in _rxDigWrite.Matches(norm))
        {
            if (!int.TryParse(m.Groups[1].Value, out int p)) continue;
            var e = GetOrCreate(pinMap, p);
            if (m.Groups[2].Value == "HIGH") e.hasHigh = true;
            else                              e.hasLow  = true;
        }

        if (pinMap.Count == 0)
        {
            // Último recurso: buscar cualquier número aislado que parezca un pin
            var anyPin = Regex.Match(norm, @"\b([2-9]|1[0-3])\b");
            if (anyPin.Success && int.TryParse(anyPin.Groups[1].Value, out int fallbackPin))
            {
                result.warnings.Add($"WARN: No se detectó pinMode/digitalWrite. " +
                                    $"¿Querías usar el pin {fallbackPin}? " +
                                    $"Escribe 'pinMode({fallbackPin}, OUTPUT)'.");
            }
            result.log = "ERROR: No se encontraron llamadas a pinMode o digitalWrite. " +
                         "Usa el template de ejemplo como punto de partida.";
            return result;
        }

        // ── Paso 4: Detectar blink por pin (dentro del cuerpo de loop) ───────
        string loopBody = ExtractFunctionBody(norm, "loop");
        string fullBody = string.IsNullOrEmpty(loopBody) ? norm : loopBody;

        foreach (Match m in _rxBlinkBlock.Matches(fullBody))
        {
            if (!int.TryParse(m.Groups[1].Value, out int p)) continue;
            var e = GetOrCreate(pinMap, p);
            e.blink      = true;
            e.blinkOnMs  = int.TryParse(m.Groups[2].Value, out int on) ? Mathf.Clamp(on, 50, 5000) : 500;
            e.blinkOffMs = int.TryParse(m.Groups[3].Value, out int off) ? Mathf.Clamp(off, 50, 5000) : e.blinkOnMs;
        }

        // Si no se detectó blink por el regex de bloque, verificar patrón básico por pin
        var delayMatch = _rxDelay.Match(fullBody);
        foreach (var kv in pinMap)
        {
            var e = kv.Value;
            if (!e.blink && e.hasHigh && e.hasLow && delayMatch.Success)
            {
                e.blink     = true;
                e.blinkOnMs = int.TryParse(delayMatch.Groups[1].Value, out int ms)
                    ? Mathf.Clamp(ms, 50, 5000) : 500;
                e.blinkOffMs = e.blinkOnMs;
            }
        }

        // ── Paso 5: Elegir el pin principal (primero OUTPUT con acción) ───────
        PinEntry chosen = null;
        int chosenPin   = -1;
        result.pins = new List<PinConfig>();

        foreach (var kv in pinMap)
        {
            int     pin = kv.Key;
            PinEntry e  = kv.Value;

            if (pin < PIN_MIN || pin > PIN_MAX)
            {
                result.warnings.Add($"Pin {pin} fuera del rango D{PIN_MIN}–D{PIN_MAX} (evita RX/TX). Ignorado.");
                continue;
            }

            if (e.mode == PinMode.OUTPUT && (e.hasHigh || e.hasLow || e.blink))
            {
                // MULTI-PIN: registrar TODOS los pines OUTPUT con acción (semáforo, secuencias…)
                result.pins.Add(new PinConfig
                {
                    pin        = pin,
                    isHigh     = e.hasHigh,
                    blink      = e.blink,
                    blinkOnMs  = e.blink ? e.blinkOnMs  : 500,
                    blinkOffMs = e.blink ? e.blinkOffMs : 500,
                });
                if (chosen == null) { chosen = e; chosenPin = pin; }
            }
            else if (e.mode == PinMode.INPUT || e.mode == PinMode.INPUT_PULLUP)
            {
                result.warnings.Add($"Pin {pin} está en modo INPUT. " +
                                    $"Para encender un LED necesitas OUTPUT: 'pinMode({pin}, OUTPUT)'.");
            }
        }

        // Si no hay OUTPUT con acción, tomar cualquier pin con OUTPUT declarado
        if (chosen == null)
        {
            foreach (var kv in pinMap)
            {
                if (kv.Key < PIN_MIN || kv.Key > PIN_MAX) continue;
                if (kv.Value.mode == PinMode.OUTPUT)
                {
                    chosen    = kv.Value;
                    chosenPin = kv.Key;
                    result.warnings.Add($"Pin D{chosenPin}: OUTPUT declarado pero sin digitalWrite. " +
                                        $"Agrega 'digitalWrite({chosenPin}, HIGH)' en loop().");
                    break;
                }
            }
        }

        // Si aún no hay pin, tomar el primero en el mapa (sin importar modo)
        if (chosen == null)
        {
            foreach (var kv in pinMap)
            {
                if (kv.Key < PIN_MIN || kv.Key > PIN_MAX) continue;
                chosen    = kv.Value;
                chosenPin = kv.Key;
                result.warnings.Add($"Pin {chosenPin}: modo {chosen.mode}. Se usará como pin principal.");
                break;
            }
        }

        if (chosen == null || chosenPin < 0)
        {
            result.log = "ERROR: No se encontró ningún pin válido en el rango D2–D13.";
            return result;
        }

        // ── Paso 6: Rellenar SketchData ──────────────────────────────────────
        result.pinNumber  = chosenPin;
        result.mode       = chosen.mode;
        result.state      = chosen.hasHigh ? PinState.HIGH : PinState.LOW;
        result.blink      = chosen.blink;
        result.blinkMs    = chosen.blink ? chosen.blinkOnMs  : 500;
        result.blinkOffMs = chosen.blink ? chosen.blinkOffMs : 500;
        result.isValid    = result.mode == PinMode.OUTPUT;

        if (!result.isValid)
        {
            result.log = $"ERROR: Pin D{chosenPin} en modo {result.mode}. " +
                         "Para el Reto 4 necesitas OUTPUT.";
            return result;
        }

        // ── Paso 7: Construir mensaje de log para el IDE ──────────────────────
        if (result.blink)
        {
            result.log = $"OK  Pin D{chosenPin} | OUTPUT | BLINK " +
                         $"ON={result.blinkMs}ms / OFF={result.blinkOffMs}ms. " +
                         "Compilado sin errores.";
        }
        else if (result.state == PinState.HIGH)
        {
            result.log = $"OK  Pin D{chosenPin} | OUTPUT | HIGH (LED fijo). " +
                         "Para BLINK agrega: digitalWrite + delay + digitalWrite + delay en loop().";
            result.warnings.Add("El objetivo del Reto 4 es hacer PARPADEAR el LED. Agrega el patrón BLINK.");
        }
        else
        {
            result.log = $"WARN: Pin D{chosenPin} · OUTPUT · LOW — el LED estará apagado. " +
                         "¿Quisiste HIGH?";
            result.warnings.Add($"Recuerda: 'digitalWrite({chosenPin}, HIGH)' enciende el LED.");
        }

        return result;
    }

    // ─────────────────────────────────────────────
    //  Limpieza de ruido
    // ─────────────────────────────────────────────

    static string StripNoise(string raw)
    {
        string s = _rxBlockComment.Replace(raw,  " ");
        s         = _rxLineComment.Replace(s,    " ");
        s         = _rxDirective.Replace(s,      " ");
        s         = _rxStringLit.Replace(s,      "\"\"");
        return s;
    }

    // ─────────────────────────────────────────────
    //  Normalización de tokens
    // ─────────────────────────────────────────────

    static string Normalize(string code)
    {
        foreach (var (rx, rep) in _normalizers) code = rx.Replace(code, rep);
        return code;
    }

    // ─────────────────────────────────────────────
    //  Extracción del cuerpo de función
    // ─────────────────────────────────────────────

    /// <summary>Extrae el contenido entre llaves de la función named.</summary>
    static string ExtractFunctionBody(string code, string funcName)
    {
        int nameIdx = code.IndexOf(funcName + "(", StringComparison.OrdinalIgnoreCase);
        if (nameIdx < 0) return string.Empty;

        int open = code.IndexOf('{', nameIdx);
        if (open < 0) return string.Empty;

        int depth = 1, i = open + 1;
        while (i < code.Length && depth > 0)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}') depth--;
            i++;
        }
        return depth == 0 ? code.Substring(open + 1, i - open - 2) : string.Empty;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    static PinEntry GetOrCreate(Dictionary<int, PinEntry> map, int pin)
    {
        if (!map.TryGetValue(pin, out var e)) { e = new PinEntry(); map[pin] = e; }
        return e;
    }

    // Estado acumulado por pin durante el parseo
    class PinEntry
    {
        public PinMode mode     = PinMode.OUTPUT;
        public bool    hasHigh;
        public bool    hasLow;
        public bool    blink;
        public int     blinkOnMs  = 500;
        public int     blinkOffMs = 500;
    }

    // ─────────────────────────────────────────────
    //  Template de inicio para el IDE
    // ─────────────────────────────────────────────

    /// <summary>
    /// Código que se carga en el IDE en modo libre por primera vez.
    /// El alumno reemplaza __ con el pin elegido.
    /// </summary>
    public const string StarterTemplate =
        "// Elige un pin digital libre (D2 - D13)\n" +
        "// y reemplaza __ con ese numero.\n" +
        "\n" +
        "void setup() {\n" +
        "  pinMode(__, OUTPUT);\n" +
        "}\n" +
        "\n" +
        "void loop() {\n" +
        "  digitalWrite(__, HIGH);\n" +
        "  delay(500);\n" +
        "  digitalWrite(__, LOW);\n" +
        "  delay(500);\n" +
        "}\n";
}
