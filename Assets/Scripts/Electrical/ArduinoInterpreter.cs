using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Intérprete tree-walking de un subconjunto de Arduino C++ para el sandbox del Reto 4.
///
/// Soporta: variables (int/long/float/bool/…), arreglos 1D, asignaciones y compuestas,
/// ++/--, if/else, for, while, do/while, funciones de usuario (incl. setup()/loop()),
/// expresiones con precedencia, y los builtins de Arduino más usados
/// (pinMode, digitalWrite, digitalRead, analogWrite, analogRead, delay, delayMicroseconds,
///  millis, micros, map, constrain, min, max, abs, random, Serial.print/println).
///
/// EJECUCIÓN COOPERATIVA: <see cref="RunSetup"/> y <see cref="RunLoop"/> devuelven una
/// secuencia de <see cref="Signal"/>. El host (ArduinoCore) la consume desde un coroutine:
///   - <see cref="Signal.Kind"/> == Wait  → esperar N segundos de simulación (delay()).
///   - <see cref="Signal.Kind"/> == Tick  → cede un frame para no congelar Unity (watchdog
///     de presupuesto de instrucciones; un while(true) sin delay corre lento pero no bloquea).
///
/// Nunca lanza al host: los errores van a <see cref="OnError"/> y la ejecución se detiene
/// de forma limpia. La placa real se abstrae con <see cref="IBoard"/>.
/// </summary>
public class ArduinoInterpreter
{
    // ─────────────────────────────────────────────
    //  Interfaz con el hardware simulado
    // ─────────────────────────────────────────────
    public interface IBoard
    {
        void PinMode(int pin, int mode);       // mode: OUTPUT=1, INPUT=0, INPUT_PULLUP=2
        void DigitalWrite(int pin, bool high);
        bool DigitalRead(int pin);
        void AnalogWrite(int pin, int duty);   // 0..255 (PWM)
        int  AnalogRead(int pin);
        long MillisNow();                       // ms desde el arranque del programa
    }

    public Action<string> OnSerial;   // Serial.print / println
    public Action<string> OnError;    // errores de compilación / runtime

    // ─────────────────────────────────────────────
    //  Señales de ejecución cooperativa
    // ─────────────────────────────────────────────
    public enum SignalKind { Tick, Wait }
    public struct Signal
    {
        public SignalKind Kind;
        public float      Seconds;
        public static readonly Signal Tick = new Signal { Kind = SignalKind.Tick };
        public static Signal Wait(float s)  => new Signal { Kind = SignalKind.Wait, Seconds = s };
    }

    // Presupuesto de "pasos" por frame antes de ceder (evita congelar el editor/juego).
    private const int STEP_BUDGET     = 40000;
    private const int MAX_CALL_DEPTH  = 64;

    // ─────────────────────────────────────────────
    //  Estado
    // ─────────────────────────────────────────────
    private readonly IBoard _board;
    private readonly Dictionary<string, FuncDef> _functions = new();
    private readonly List<Stmt> _globals = new();
    private Env  _global;
    private bool _compiled;

    private int  _steps;
    private int  _callDepth;
    private Flow _flow;
    private object _ret;

    public bool Compiled => _compiled;
    public bool HasFunction(string name) => _functions.ContainsKey(name);

    public ArduinoInterpreter(IBoard board) { _board = board; }

    /// <summary>Compila sin ejecutar (para el botón "Compilar"). No necesita placa.</summary>
    public static bool TryCompile(string code, out string error)
    {
        string captured = null;
        var probe = new ArduinoInterpreter(null) { OnError = s => captured = s };
        bool ok = probe.Compile(code);
        error = ok ? null : (captured ?? "Error de compilación.");
        return ok;
    }

    // ═════════════════════════════════════════════
    //  API pública
    // ═════════════════════════════════════════════

    /// <summary>Tokeniza y parsea el sketch. Devuelve false (y reporta por OnError) si falla.</summary>
    public bool Compile(string code)
    {
        _compiled = false;
        _functions.Clear();
        _globals.Clear();
        try
        {
            var toks = Preprocessor.ExpandDefines(code, Lexer.Tokenize(code));
            new Parser(toks, _functions, _globals).ParseProgram();
            _compiled = true;
            return true;
        }
        catch (ParseException pe)
        {
            OnError?.Invoke("Error de sintaxis: " + pe.Message);
            return false;
        }
        catch (Exception e)
        {
            OnError?.Invoke("Error al compilar: " + e.Message);
            return false;
        }
    }

    /// <summary>Inicializa el entorno global y ejecuta las declaraciones globales.</summary>
    public void Start()
    {
        _global = new Env(null);
        _flow = Flow.None; _ret = null; _callDepth = 0; _steps = STEP_BUDGET;
        try
        {
            int guard = 0;
            foreach (var s in _globals)
                foreach (var _ in ExecStmt(s, _global))
                    if (++guard > 2_000_000) throw new RuntimeException("Posible bucle infinito en variables globales.");
        }
        catch (RuntimeException re) { OnError?.Invoke("Error: " + re.Message); }
        _flow = Flow.None;
    }

    /// <summary>Ejecuta setup() una vez (si existe).</summary>
    public IEnumerable<Signal> RunSetup() => RunFunction("setup");

    /// <summary>Ejecuta loop() una vez (si existe).</summary>
    public IEnumerable<Signal> RunLoop() => RunFunction("loop");

    IEnumerable<Signal> RunFunction(string name)
    {
        if (!_compiled || !_functions.TryGetValue(name, out var fn)) yield break;
        _flow = Flow.None; _ret = null; _steps = STEP_BUDGET; _callDepth = 0;
        var env = new Env(_global);
        var seq = ExecBlock(fn.Body, env).GetEnumerator();
        while (true)
        {
            bool has;
            try { has = seq.MoveNext(); }
            catch (RuntimeException re) { OnError?.Invoke("Error de ejecución: " + re.Message); yield break; }
            catch (Exception e)         { OnError?.Invoke("Error inesperado: " + e.Message);     yield break; }
            if (!has) yield break;
            yield return seq.Current;
        }
    }

    // ═════════════════════════════════════════════
    //  Ejecución de sentencias (iteradores → soportan delay)
    // ═════════════════════════════════════════════

    IEnumerable<Signal> ExecBlock(Block b, Env env)
    {
        foreach (var s in b.Stmts)
        {
            foreach (var sig in ExecStmt(s, env)) yield return sig;
            if (_flow != Flow.None) yield break;
        }
    }

    IEnumerable<Signal> ExecStmt(Stmt s, Env env)
    {
        if (--_steps <= 0) { _steps = STEP_BUDGET; yield return Signal.Tick; }

        switch (s)
        {
            case VarDecl vd:
                env.Define(vd.Name, vd.Init != null ? Eval(vd.Init, env) : (vd.IsArray ? (object)new List<object>() : 0.0));
                yield break;

            case ExprStmt es:
            {
                // delay / delayMicroseconds como sentencia → suspende la ejecución.
                if (es.Expr is CallExpr c && IsDelayCall(c.Name))
                {
                    double ms = ToNum(Eval(c.Args.Count > 0 ? c.Args[0] : null, env));
                    if (c.Name == "delayMicroseconds") ms /= 1000.0;
                    if (ms > 0) yield return Signal.Wait((float)(ms / 1000.0));
                    yield break;
                }
                // Llamada a función de usuario como sentencia → se ejecuta como iterador
                // (así un helper con delay() adentro sí suspende).
                if (es.Expr is CallExpr uc && _functions.ContainsKey(uc.Name))
                {
                    foreach (var sig in CallUserIter(uc, env)) yield return sig;
                    yield break;
                }
                Eval(es.Expr, env);
                yield break;
            }

            case Block bl:
                foreach (var sig in ExecBlock(bl, new Env(env))) yield return sig;
                yield break;

            case IfStmt iff:
                if (ToBool(Eval(iff.Cond, env)))
                    foreach (var sig in ExecStmt(iff.Then, env)) yield return sig;
                else if (iff.Else != null)
                    foreach (var sig in ExecStmt(iff.Else, env)) yield return sig;
                yield break;

            case WhileStmt w:
                while (ToBool(Eval(w.Cond, env)))
                {
                    foreach (var sig in ExecStmt(w.Body, env)) yield return sig;
                    if (_flow == Flow.Break)    { _flow = Flow.None; break; }
                    if (_flow == Flow.Return)   yield break;
                    if (_flow == Flow.Continue) _flow = Flow.None;
                    if (--_steps <= 0) { _steps = STEP_BUDGET; yield return Signal.Tick; }
                }
                yield break;

            case DoWhileStmt dw:
                do
                {
                    foreach (var sig in ExecStmt(dw.Body, env)) yield return sig;
                    if (_flow == Flow.Break)    { _flow = Flow.None; break; }
                    if (_flow == Flow.Return)   yield break;
                    if (_flow == Flow.Continue) _flow = Flow.None;
                    if (--_steps <= 0) { _steps = STEP_BUDGET; yield return Signal.Tick; }
                } while (ToBool(Eval(dw.Cond, env)));
                yield break;

            case ForStmt f:
            {
                var fe = new Env(env);
                if (f.Init != null) foreach (var _ in ExecStmt(f.Init, fe)) { }
                while (f.Cond == null || ToBool(Eval(f.Cond, fe)))
                {
                    foreach (var sig in ExecStmt(f.Body, fe)) yield return sig;
                    if (_flow == Flow.Break)    { _flow = Flow.None; break; }
                    if (_flow == Flow.Return)   yield break;
                    if (_flow == Flow.Continue) _flow = Flow.None;
                    if (f.Post != null) Eval(f.Post, fe);
                    if (--_steps <= 0) { _steps = STEP_BUDGET; yield return Signal.Tick; }
                }
                yield break;
            }

            case ReturnStmt r:
                _ret  = r.Value != null ? Eval(r.Value, env) : null;
                _flow = Flow.Return;
                yield break;

            case BreakStmt:    _flow = Flow.Break;    yield break;
            case ContinueStmt: _flow = Flow.Continue; yield break;
            case EmptyStmt:    yield break;
        }
    }

    /// <summary>Ejecuta una llamada a función de usuario como iterador (soporta delay adentro).</summary>
    IEnumerable<Signal> CallUserIter(CallExpr call, Env caller)
    {
        if (!_functions.TryGetValue(call.Name, out var fn)) { Eval(call, caller); yield break; }
        if (_callDepth >= MAX_CALL_DEPTH) throw new RuntimeException($"Recursión demasiado profunda en '{call.Name}()'.");

        var local = new Env(_global);
        for (int i = 0; i < fn.Params.Count; i++)
            local.Define(fn.Params[i], i < call.Args.Count ? Eval(call.Args[i], caller) : 0.0);

        _callDepth++;
        var savedFlow = _flow; _flow = Flow.None;
        foreach (var sig in ExecBlock(fn.Body, local)) yield return sig;
        _flow = savedFlow;      // return/break dentro de la función no se propaga al caller
        _callDepth--;
    }

    // ═════════════════════════════════════════════
    //  Evaluación de expresiones (síncrona)
    // ═════════════════════════════════════════════

    object Eval(Expr e, Env env)
    {
        switch (e)
        {
            case null:        return 0.0;
            case NumLit n:    return n.Value;
            case StrLit s:    return s.Value;
            case BoolLit b:   return b.Value ? 1.0 : 0.0;

            case Ident id:
                if (env.TryGet(id.Name, out var v)) return v;
                if (Constants.TryGetValue(id.Name, out var k)) return k;
                throw new RuntimeException($"Variable no definida: '{id.Name}'.");

            case ArrayLit al:
            {
                var list = new List<object>(al.Items.Count);
                foreach (var it in al.Items) list.Add(Eval(it, env));
                return list;
            }

            case IndexExpr ix:
            {
                var arr = Eval(ix.Target, env) as List<object>;
                if (arr == null) throw new RuntimeException("Se indexó algo que no es un arreglo.");
                int i = (int)ToNum(Eval(ix.Index, env));
                if (i < 0 || i >= arr.Count) throw new RuntimeException($"Índice fuera de rango ({i}).");
                return arr[i];
            }

            case UnaryExpr u:   return EvalUnary(u, env);
            case BinaryExpr bi: return EvalBinary(bi, env);
            case AssignExpr a:  return EvalAssign(a, env);
            case CallExpr c:    return EvalCall(c, env);
        }
        throw new RuntimeException("Expresión no soportada.");
    }

    object EvalUnary(UnaryExpr u, Env env)
    {
        switch (u.Op)
        {
            case "-": return -ToNum(Eval(u.Operand, env));
            case "+": return  ToNum(Eval(u.Operand, env));
            case "!": return ToBool(Eval(u.Operand, env)) ? 0.0 : 1.0;
            case "~": return (double)(~(long)ToNum(Eval(u.Operand, env)));
            case "++pre":
            case "--pre":
            {
                double nv = ToNum(ReadLValue(u.Operand, env)) + (u.Op == "++pre" ? 1 : -1);
                WriteLValue(u.Operand, nv, env);
                return nv;
            }
            case "++post":
            case "--post":
            {
                double ov = ToNum(ReadLValue(u.Operand, env));
                WriteLValue(u.Operand, ov + (u.Op == "++post" ? 1 : -1), env);
                return ov;
            }
        }
        throw new RuntimeException("Operador unario desconocido: " + u.Op);
    }

    object EvalBinary(BinaryExpr b, Env env)
    {
        // Cortocircuito lógico
        if (b.Op == "&&") return (ToBool(Eval(b.Left, env)) && ToBool(Eval(b.Right, env))) ? 1.0 : 0.0;
        if (b.Op == "||") return (ToBool(Eval(b.Left, env)) || ToBool(Eval(b.Right, env))) ? 1.0 : 0.0;

        object l = Eval(b.Left, env);
        object r = Eval(b.Right, env);

        if (b.Op == "+" && (l is string || r is string)) return ToStr(l) + ToStr(r);

        double a = ToNum(l), c = ToNum(r);
        switch (b.Op)
        {
            case "+": return a + c;
            case "-": return a - c;
            case "*": return a * c;
            case "/": return c == 0 ? 0.0 : a / c;
            case "%": return c == 0 ? 0.0 : (double)((long)a % (long)c);
            case "==": return a == c ? 1.0 : 0.0;
            case "!=": return a != c ? 1.0 : 0.0;
            case "<":  return a <  c ? 1.0 : 0.0;
            case "<=": return a <= c ? 1.0 : 0.0;
            case ">":  return a >  c ? 1.0 : 0.0;
            case ">=": return a >= c ? 1.0 : 0.0;
            case "&":  return (double)((long)a &  (long)c);
            case "|":  return (double)((long)a |  (long)c);
            case "^":  return (double)((long)a ^  (long)c);
            case "<<": return (double)((long)a << (int)c);
            case ">>": return (double)((long)a >> (int)c);
        }
        throw new RuntimeException("Operador desconocido: " + b.Op);
    }

    object EvalAssign(AssignExpr a, Env env)
    {
        double cur = a.Op == "=" ? 0 : ToNum(ReadLValue(a.Target, env));
        object rhs = Eval(a.Value, env);
        object result;
        switch (a.Op)
        {
            case "=":  result = rhs; break;
            case "+=":
                if (ReadLValue(a.Target, env) is string || rhs is string)
                    result = ToStr(ReadLValue(a.Target, env)) + ToStr(rhs);
                else result = cur + ToNum(rhs);
                break;
            case "-=": result = cur - ToNum(rhs); break;
            case "*=": result = cur * ToNum(rhs); break;
            case "/=": result = ToNum(rhs) == 0 ? 0.0 : cur / ToNum(rhs); break;
            case "%=": result = ToNum(rhs) == 0 ? 0.0 : (double)((long)cur % (long)ToNum(rhs)); break;
            default: throw new RuntimeException("Asignación desconocida: " + a.Op);
        }
        WriteLValue(a.Target, result, env);
        return result;
    }

    object ReadLValue(Expr target, Env env)
    {
        if (target is Ident id)
        {
            if (env.TryGet(id.Name, out var v)) return v;
            throw new RuntimeException($"Variable no definida: '{id.Name}'.");
        }
        if (target is IndexExpr ix) return Eval(ix, env);
        throw new RuntimeException("Destino de asignación inválido.");
    }

    void WriteLValue(Expr target, object value, Env env)
    {
        if (target is Ident id) { env.Set(id.Name, value); return; }
        if (target is IndexExpr ix)
        {
            var arr = Eval(ix.Target, env) as List<object>;
            if (arr == null) throw new RuntimeException("Asignación a algo que no es arreglo.");
            int i = (int)ToNum(Eval(ix.Index, env));
            if (i < 0 || i >= arr.Count) throw new RuntimeException($"Índice fuera de rango ({i}).");
            arr[i] = value;
            return;
        }
        throw new RuntimeException("Destino de asignación inválido.");
    }

    // ─────────────────────────────────────────────
    //  Llamadas a builtins / funciones de usuario (contexto de valor)
    // ─────────────────────────────────────────────
    object EvalCall(CallExpr c, Env env)
    {
        switch (c.Name)
        {
            case "pinMode":          _board.PinMode(Arg(c, env, 0), Arg(c, env, 1)); return 0.0;
            case "digitalWrite":     _board.DigitalWrite(Arg(c, env, 0), ToBool(Eval(c.Args[1], env))); return 0.0;
            case "analogWrite":      _board.AnalogWrite(Arg(c, env, 0), Arg(c, env, 1)); return 0.0;
            case "digitalRead":      return _board.DigitalRead(Arg(c, env, 0)) ? 1.0 : 0.0;
            case "analogRead":       return (double)_board.AnalogRead(Arg(c, env, 0));
            case "delay":
            case "delayMicroseconds": return 0.0;   // como expresión no suspende (se maneja a nivel sentencia)
            case "millis":           return (double)_board.MillisNow();
            case "micros":           return (double)_board.MillisNow() * 1000.0;
            case "map":              return ArduMap(Arg(c,env,0), Arg(c,env,1), Arg(c,env,2), Arg(c,env,3), Arg(c,env,4));
            case "constrain":        return Math.Max(Argd(c,env,1), Math.Min(Argd(c,env,0), Argd(c,env,2)));
            case "min":              return Math.Min(Argd(c,env,0), Argd(c,env,1));
            case "max":              return Math.Max(Argd(c,env,0), Argd(c,env,1));
            case "abs":              return Math.Abs(Argd(c,env,0));
            case "sq":               return Argd(c,env,0) * Argd(c,env,0);
            case "random":
                if (c.Args.Count >= 2) { int lo = Arg(c,env,0), hi = Arg(c,env,1); return (double)_rng.Next(lo, Math.Max(lo, hi)); }
                if (c.Args.Count == 1) { int hi = Arg(c,env,0); return (double)_rng.Next(0, Math.Max(0, hi)); }
                return (double)_rng.Next();
            case "randomSeed":       return 0.0;
            case "Serial.begin":     return 0.0;
            case "Serial.print":     OnSerial?.Invoke(ToStr(c.Args.Count > 0 ? Eval(c.Args[0], env) : "")); return 0.0;
            case "Serial.println":   OnSerial?.Invoke(ToStr(c.Args.Count > 0 ? Eval(c.Args[0], env) : "") + "\n"); return 0.0;
        }

        // Función de usuario en contexto de valor: se ejecuta de forma síncrona
        // (los delay() internos no suspenden, pero el valor de retorno es correcto).
        if (_functions.TryGetValue(c.Name, out var fn))
        {
            if (_callDepth >= MAX_CALL_DEPTH) throw new RuntimeException($"Recursión demasiado profunda en '{c.Name}()'.");
            var local = new Env(_global);
            for (int i = 0; i < fn.Params.Count; i++)
                local.Define(fn.Params[i], i < c.Args.Count ? Eval(c.Args[i], env) : 0.0);

            _callDepth++;
            var savedFlow = _flow; var savedRet = _ret; _flow = Flow.None; _ret = null;
            int guard = 0;   // contexto de valor: no hay frame al cual ceder → corta bucles infinitos
            foreach (var _ in ExecBlock(fn.Body, local))
                if (++guard > 2_000_000) throw new RuntimeException($"Posible bucle infinito en '{c.Name}()'.");
            object ret = _flow == Flow.Return ? _ret : 0.0;
            _flow = savedFlow; _ret = savedRet;
            _callDepth--;
            return ret ?? 0.0;
        }

        throw new RuntimeException($"Función desconocida: '{c.Name}()'.");
    }

    int    Arg(CallExpr c, Env env, int i)  => (int)ToNum(i < c.Args.Count ? Eval(c.Args[i], env) : 0.0);
    double Argd(CallExpr c, Env env, int i) => ToNum(i < c.Args.Count ? Eval(c.Args[i], env) : 0.0);

    static double ArduMap(long x, long inMin, long inMax, long outMin, long outMax)
    {
        if (inMax == inMin) return outMin;
        return (double)((x - inMin) * (outMax - outMin) / (inMax - inMin) + outMin);
    }

    private readonly System.Random _rng = new System.Random();

    // ─────────────────────────────────────────────
    //  Conversiones
    // ─────────────────────────────────────────────
    static bool IsDelayCall(string name) => name == "delay" || name == "delayMicroseconds";

    static double ToNum(object o)
    {
        switch (o)
        {
            case null: return 0.0;
            case double d: return d;
            case bool b: return b ? 1.0 : 0.0;
            case string s: return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0.0;
            case List<object> _: return 0.0;
        }
        return 0.0;
    }
    static bool ToBool(object o) => ToNum(o) != 0.0;
    static string ToStr(object o)
    {
        if (o is double d) return d == Math.Floor(d) && !double.IsInfinity(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture);
        return o?.ToString() ?? "";
    }

    // ─────────────────────────────────────────────
    //  Constantes Arduino
    // ─────────────────────────────────────────────
    static readonly Dictionary<string, object> Constants = new()
    {
        { "HIGH", 1.0 }, { "LOW", 0.0 },
        { "OUTPUT", 1.0 }, { "INPUT", 0.0 }, { "INPUT_PULLUP", 2.0 },
        { "LED_BUILTIN", 13.0 },
        { "true", 1.0 }, { "false", 0.0 }, { "TRUE", 1.0 }, { "FALSE", 0.0 },
        { "PI", Math.PI },
        { "A0", 14.0 }, { "A1", 15.0 }, { "A2", 16.0 }, { "A3", 17.0 }, { "A4", 18.0 }, { "A5", 19.0 },
    };

    enum Flow { None, Break, Continue, Return }

    // ═════════════════════════════════════════════
    //  Entorno de variables (scopes encadenados)
    // ═════════════════════════════════════════════
    class Env
    {
        readonly Env _parent;
        readonly Dictionary<string, object> _vars = new();
        public Env(Env parent) { _parent = parent; }

        public void Define(string name, object value) => _vars[name] = value;

        public bool TryGet(string name, out object value)
        {
            for (var e = this; e != null; e = e._parent)
                if (e._vars.TryGetValue(name, out value)) return true;
            value = null; return false;
        }

        public void Set(string name, object value)
        {
            for (var e = this; e != null; e = e._parent)
                if (e._vars.ContainsKey(name)) { e._vars[name] = value; return; }
            _vars[name] = value;   // implícita en el scope actual si no existía
        }
    }

    // ═════════════════════════════════════════════
    //  AST
    // ═════════════════════════════════════════════
    abstract class Expr { }
    sealed class NumLit   : Expr { public double Value; }
    sealed class StrLit   : Expr { public string Value; }
    sealed class BoolLit  : Expr { public bool Value; }
    sealed class Ident    : Expr { public string Name; }
    sealed class ArrayLit : Expr { public List<Expr> Items = new(); }
    sealed class IndexExpr: Expr { public Expr Target; public Expr Index; }
    sealed class UnaryExpr: Expr { public string Op; public Expr Operand; }
    sealed class BinaryExpr:Expr { public string Op; public Expr Left, Right; }
    sealed class AssignExpr:Expr { public string Op; public Expr Target; public Expr Value; }
    sealed class CallExpr : Expr { public string Name; public List<Expr> Args = new(); }

    abstract class Stmt { }
    sealed class VarDecl    : Stmt { public string Name; public Expr Init; public bool IsArray; }
    sealed class ExprStmt   : Stmt { public Expr Expr; }
    sealed class Block      : Stmt { public List<Stmt> Stmts = new(); }
    sealed class IfStmt     : Stmt { public Expr Cond; public Stmt Then, Else; }
    sealed class WhileStmt  : Stmt { public Expr Cond; public Stmt Body; }
    sealed class DoWhileStmt: Stmt { public Stmt Body; public Expr Cond; }
    sealed class ForStmt    : Stmt { public Stmt Init; public Expr Cond; public Expr Post; public Stmt Body; }
    sealed class ReturnStmt : Stmt { public Expr Value; }
    sealed class BreakStmt   : Stmt { }
    sealed class ContinueStmt: Stmt { }
    sealed class EmptyStmt   : Stmt { }

    sealed class FuncDef { public string Name; public List<string> Params = new(); public Block Body; }

    // ═════════════════════════════════════════════
    //  Preprocesador mínimo (#define de objeto)
    // ═════════════════════════════════════════════
    // El lexer descarta las líneas '#...' (como #include), pero '#define LED 13' es de lo más
    // común en sketches de estudiantes: sin esto, el sketch "compilaba" (Verificar decía OK) y
    // luego fallaba en ejecución con "Variable no definida: 'LED'". Se expande a nivel de
    // TOKENS (no texto), así 'LED' no pisa identificadores que lo contengan (p. ej. 'LED_ROJO').
    static class Preprocessor
    {
        public static List<Tok> ExpandDefines(string src, List<Tok> toks)
        {
            var macros = CollectDefines(src);
            if (macros.Count == 0) return toks;

            // Varias pasadas para #define encadenados; tope fijo por si el alumno
            // escribe macros circulares (#define A B / #define B A).
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                var outToks = new List<Tok>(toks.Count);
                foreach (var t in toks)
                {
                    if (t.Type == TokType.Id && macros.TryGetValue(t.Text, out var rep))
                    { outToks.AddRange(rep); changed = true; }
                    else outToks.Add(t);
                }
                toks = outToks;
                if (!changed) break;
            }
            return toks;
        }

        static Dictionary<string, List<Tok>> CollectDefines(string src)
        {
            var macros = new Dictionary<string, List<Tok>>();
            foreach (var raw in src.Split('\n'))
            {
                string line = raw.TrimStart();
                if (line.Length == 0 || line[0] != '#') continue;
                string body = line.Substring(1).TrimStart();
                if (!body.StartsWith("define", StringComparison.Ordinal)) continue;  // #include etc.: se ignoran como antes
                body = body.Substring(6).TrimStart();

                int e = 0;
                while (e < body.Length && (char.IsLetterOrDigit(body[e]) || body[e] == '_')) e++;
                if (e == 0) throw new ParseException("#define sin nombre de macro.");
                string name = body.Substring(0, e);
                if (e < body.Length && body[e] == '(')
                    throw new ParseException($"#define {name}(...) con parámetros no está soportado; usa una función.");

                string value = body.Substring(e).Trim();
                if (value.Length == 0) continue;   // '#define DEBUG' sin valor: no hay nada que sustituir

                var valToks = Lexer.Tokenize(value);   // también le quita comentarios al final de la línea
                valToks.RemoveAll(t => t.Type == TokType.EOF);   // el EOF del lexer NO debe inyectarse en medio del stream
                if (valToks.Count > 0) macros[name] = valToks;
            }
            return macros;
        }
    }

    // ═════════════════════════════════════════════
    //  Lexer
    // ═════════════════════════════════════════════
    enum TokType { Num, Str, Id, Op, Punct, EOF }
    struct Tok { public TokType Type; public string Text; public int Pos; }

    sealed class ParseException : Exception { public ParseException(string m) : base(m) { } }
    sealed class RuntimeException : Exception { public RuntimeException(string m) : base(m) { } }

    static class Lexer
    {
        static readonly string[] MultiOps =
        {
            "==","!=","<=",">=","&&","||","++","--","+=","-=","*=","/=","%=","<<",">>"
        };
        const string SingleOps = "+-*/%=<>!&|^~?:";

        public static List<Tok> Tokenize(string src)
        {
            var toks = new List<Tok>();
            int i = 0, n = src.Length;
            while (i < n)
            {
                char ch = src[i];

                // Espacios
                if (char.IsWhiteSpace(ch)) { i++; continue; }

                // Comentarios
                if (ch == '/' && i + 1 < n && src[i + 1] == '/')
                { while (i < n && src[i] != '\n') i++; continue; }
                if (ch == '/' && i + 1 < n && src[i + 1] == '*')
                { i += 2; while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/')) i++; i += 2; continue; }

                // Preprocesador (#include, #define …) → ignorar la línea
                if (ch == '#') { while (i < n && src[i] != '\n') i++; continue; }

                // String
                if (ch == '"')
                {
                    var sb = new StringBuilder(); i++;
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '\\' && i + 1 < n) { sb.Append(Unescape(src[i + 1])); i += 2; }
                        else sb.Append(src[i++]);
                    }
                    i++; // cierre
                    toks.Add(new Tok { Type = TokType.Str, Text = sb.ToString(), Pos = i });
                    continue;
                }

                // Char literal → número (código)
                if (ch == '\'')
                {
                    i++;
                    char c2;
                    if (src[i] == '\\' && i + 1 < n) { c2 = Unescape(src[i + 1]); i += 2; }
                    else c2 = src[i++];
                    if (i < n && src[i] == '\'') i++;
                    toks.Add(new Tok { Type = TokType.Num, Text = ((int)c2).ToString(), Pos = i });
                    continue;
                }

                // Número (incl. hex 0x, decimales, exponente). Sufijos L/U/F se ignoran.
                if (char.IsDigit(ch) || (ch == '.' && i + 1 < n && char.IsDigit(src[i + 1])))
                {
                    int start = i;
                    if (ch == '0' && i + 1 < n && (src[i + 1] == 'x' || src[i + 1] == 'X'))
                    {
                        i += 2;
                        while (i < n && Uri.IsHexDigit(src[i])) i++;
                        long hv = Convert.ToInt64(src.Substring(start + 2, i - start - 2), 16);
                        SkipNumSuffix(src, ref i, n);
                        toks.Add(new Tok { Type = TokType.Num, Text = hv.ToString(), Pos = i });
                        continue;
                    }
                    while (i < n && (char.IsDigit(src[i]) || src[i] == '.')) i++;
                    if (i < n && (src[i] == 'e' || src[i] == 'E'))
                    {
                        i++;
                        if (i < n && (src[i] == '+' || src[i] == '-')) i++;
                        while (i < n && char.IsDigit(src[i])) i++;
                    }
                    string numTxt = src.Substring(start, i - start);
                    SkipNumSuffix(src, ref i, n);
                    toks.Add(new Tok { Type = TokType.Num, Text = numTxt, Pos = i });
                    continue;
                }

                // Identificador / palabra clave
                if (char.IsLetter(ch) || ch == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    toks.Add(new Tok { Type = TokType.Id, Text = src.Substring(start, i - start), Pos = i });
                    continue;
                }

                // Operador multi-caracter
                bool matched = false;
                foreach (var op in MultiOps)
                    if (i + op.Length <= n && src.Substring(i, op.Length) == op)
                    { toks.Add(new Tok { Type = TokType.Op, Text = op, Pos = i }); i += op.Length; matched = true; break; }
                if (matched) continue;

                // Operador simple
                if (SingleOps.IndexOf(ch) >= 0)
                { toks.Add(new Tok { Type = TokType.Op, Text = ch.ToString(), Pos = i }); i++; continue; }

                // Puntuación
                if ("(){}[],;.".IndexOf(ch) >= 0)
                { toks.Add(new Tok { Type = TokType.Punct, Text = ch.ToString(), Pos = i }); i++; continue; }

                // Desconocido → ignorar
                i++;
            }
            toks.Add(new Tok { Type = TokType.EOF, Text = "", Pos = i });
            return toks;
        }

        static void SkipNumSuffix(string s, ref int i, int n)
        { while (i < n && "uUlLfF".IndexOf(s[i]) >= 0) i++; }

        static char Unescape(char c) => c switch
        { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', '\\' => '\\', '"' => '"', '\'' => '\'', _ => c };
    }

    // ═════════════════════════════════════════════
    //  Parser (descenso recursivo)
    // ═════════════════════════════════════════════
    sealed class Parser
    {
        readonly List<Tok> _t;
        readonly Dictionary<string, FuncDef> _funcs;
        readonly List<Stmt> _globals;
        int _p;

        static readonly HashSet<string> TypeKw = new()
        {
            "void","int","long","short","char","float","double","bool","boolean","byte","word",
            "unsigned","signed","const","static","volatile","size_t",
            "uint8_t","int8_t","uint16_t","int16_t","uint32_t","int32_t","uint64_t","int64_t","String"
        };

        public Parser(List<Tok> t, Dictionary<string, FuncDef> funcs, List<Stmt> globals)
        { _t = t; _funcs = funcs; _globals = globals; }

        Tok Cur => _t[_p];
        Tok Next => _t[Math.Min(_p + 1, _t.Count - 1)];
        bool IsEOF => Cur.Type == TokType.EOF;
        bool Is(string s) => Cur.Text == s && (Cur.Type == TokType.Op || Cur.Type == TokType.Punct || Cur.Type == TokType.Id);
        void Expect(string s) { if (Cur.Text != s) throw new ParseException($"Se esperaba '{s}' pero se encontró '{Cur.Text}'."); _p++; }
        bool Accept(string s) { if (Cur.Text == s) { _p++; return true; } return false; }

        // ── Programa ──────────────────────────────
        public void ParseProgram()
        {
            while (!IsEOF)
            {
                // Saltar ';' sueltos
                if (Is(";")) { _p++; continue; }

                if (Cur.Type == TokType.Id && TypeKw.Contains(Cur.Text))
                {
                    // Consumir calificadores/tipo
                    int save = _p;
                    while (Cur.Type == TokType.Id && TypeKw.Contains(Cur.Text)) _p++;
                    // Puede haber '*' o '&' de puntero/ref → ignorar
                    while (Is("*") || Is("&")) _p++;

                    if (Cur.Type == TokType.Id && Next.Text == "(")
                    {
                        ParseFunction();
                        continue;
                    }
                    // Declaración global de variable(s)
                    _p = save;
                    foreach (var d in ParseVarDecls()) _globals.Add(d);
                    continue;
                }

                // Cualquier otra cosa a nivel global → sentencia global (tolerante)
                _globals.Add(ParseStatement());
            }
        }

        void ParseFunction()
        {
            string name = Cur.Text; _p++;
            Expect("(");
            var ps = new List<string>();
            if (!Is(")"))
            {
                do
                {
                    while (Cur.Type == TokType.Id && TypeKw.Contains(Cur.Text)) _p++;
                    while (Is("*") || Is("&")) _p++;
                    if (Cur.Type == TokType.Id) { ps.Add(Cur.Text); _p++; }
                    // arreglo en parámetro: int x[]
                    while (Is("[")) { _p++; if (!Is("]")) ParseExpr(); Expect("]"); }
                } while (Accept(","));
            }
            Expect(")");
            var body = ParseBlock();
            _funcs[name] = new FuncDef { Name = name, Params = ps, Body = body };
        }

        // ── Sentencias ────────────────────────────
        Block ParseBlock()
        {
            Expect("{");
            var b = new Block();
            while (!Is("}") && !IsEOF) b.Stmts.Add(ParseStatement());
            Expect("}");
            return b;
        }

        Stmt ParseStatement()
        {
            if (Is("{")) return ParseBlock();
            if (Is(";")) { _p++; return new EmptyStmt(); }

            if (Cur.Type == TokType.Id)
            {
                switch (Cur.Text)
                {
                    case "if":       return ParseIf();
                    case "while":    return ParseWhile();
                    case "for":      return ParseFor();
                    case "do":       return ParseDoWhile();
                    case "return":   _p++; { Expr v = Is(";") ? null : ParseExpr(); Accept(";"); return new ReturnStmt { Value = v }; }
                    case "break":    _p++; Accept(";"); return new BreakStmt();
                    case "continue": _p++; Accept(";"); return new ContinueStmt();
                }
                if (TypeKw.Contains(Cur.Text))
                {
                    var decls = ParseVarDecls();
                    return decls.Count == 1 ? decls[0] : Wrap(decls);
                }
            }

            // Sentencia-expresión
            var e = ParseExpr();
            Accept(";");
            return new ExprStmt { Expr = e };
        }

        static Stmt Wrap(List<Stmt> decls) { var b = new Block(); b.Stmts.AddRange(decls); return b; }

        List<Stmt> ParseVarDecls()
        {
            // Consumir tipo + calificadores
            while (Cur.Type == TokType.Id && TypeKw.Contains(Cur.Text)) _p++;
            while (Is("*") || Is("&")) _p++;

            var result = new List<Stmt>();
            do
            {
                if (Cur.Type != TokType.Id) throw new ParseException($"Se esperaba un nombre de variable, no '{Cur.Text}'.");
                string name = Cur.Text; _p++;

                bool isArray = false;
                if (Is("["))
                {
                    isArray = true;
                    _p++;
                    if (!Is("]")) ParseExpr();   // tamaño (se ignora; el arreglo se dimensiona por el inicializador)
                    Expect("]");
                }

                Expr init = null;
                if (Accept("=")) init = ParseExpr();

                result.Add(new VarDecl { Name = name, Init = init, IsArray = isArray });
            } while (Accept(","));

            Accept(";");
            return result;
        }

        Stmt ParseIf()
        {
            _p++; Expect("(");
            var cond = ParseExpr();
            Expect(")");
            var then = ParseStatement();
            Stmt els = null;
            if (Cur.Text == "else") { _p++; els = ParseStatement(); }
            return new IfStmt { Cond = cond, Then = then, Else = els };
        }

        Stmt ParseWhile()
        {
            _p++; Expect("(");
            var cond = ParseExpr();
            Expect(")");
            return new WhileStmt { Cond = cond, Body = ParseStatement() };
        }

        Stmt ParseDoWhile()
        {
            _p++;
            var body = ParseStatement();
            if (Cur.Text != "while") throw new ParseException("Se esperaba 'while' después de 'do'.");
            _p++; Expect("(");
            var cond = ParseExpr();
            Expect(")"); Accept(";");
            return new DoWhileStmt { Body = body, Cond = cond };
        }

        Stmt ParseFor()
        {
            _p++; Expect("(");
            Stmt init = Is(";") ? new EmptyStmt() : ParseForInit();
            Accept(";");
            Expr cond = Is(";") ? null : ParseExpr();
            Accept(";");
            Expr post = Is(")") ? null : ParseExpr();
            Expect(")");
            return new ForStmt { Init = init, Cond = cond, Post = post, Body = ParseStatement() };
        }

        Stmt ParseForInit()
        {
            if (Cur.Type == TokType.Id && TypeKw.Contains(Cur.Text))
            {
                var decls = ParseVarDeclsNoSemicolon();
                return decls.Count == 1 ? decls[0] : Wrap(decls);
            }
            return new ExprStmt { Expr = ParseExpr() };
        }

        List<Stmt> ParseVarDeclsNoSemicolon()
        {
            while (Cur.Type == TokType.Id && TypeKw.Contains(Cur.Text)) _p++;
            while (Is("*") || Is("&")) _p++;
            var result = new List<Stmt>();
            do
            {
                string name = Cur.Text; _p++;
                bool isArray = false;
                if (Is("[")) { isArray = true; _p++; if (!Is("]")) ParseExpr(); Expect("]"); }
                Expr init = null;
                if (Accept("=")) init = ParseExpr();
                result.Add(new VarDecl { Name = name, Init = init, IsArray = isArray });
            } while (Accept(","));
            return result;
        }

        // ── Expresiones (precedencia) ─────────────
        Expr ParseExpr() => ParseAssign();

        Expr ParseAssign()
        {
            var left = ParseBinary(0);
            if (Cur.Type == TokType.Op &&
                (Cur.Text == "=" || Cur.Text == "+=" || Cur.Text == "-=" ||
                 Cur.Text == "*=" || Cur.Text == "/=" || Cur.Text == "%="))
            {
                string op = Cur.Text; _p++;
                var right = ParseAssign();
                if (!(left is Ident || left is IndexExpr))
                    throw new ParseException("Destino de asignación inválido.");
                return new AssignExpr { Op = op, Target = left, Value = right };
            }
            return left;
        }

        static readonly Dictionary<string, int> BinPrec = new()
        {
            { "||", 1 }, { "&&", 2 }, { "|", 3 }, { "^", 4 }, { "&", 5 },
            { "==", 6 }, { "!=", 6 },
            { "<", 7 }, { "<=", 7 }, { ">", 7 }, { ">=", 7 },
            { "<<", 8 }, { ">>", 8 },
            { "+", 9 }, { "-", 9 },
            { "*", 10 }, { "/", 10 }, { "%", 10 },
        };

        Expr ParseBinary(int minPrec)
        {
            var left = ParseUnary();
            while (Cur.Type == TokType.Op && BinPrec.TryGetValue(Cur.Text, out int prec) && prec >= minPrec)
            {
                string op = Cur.Text; _p++;
                var right = ParseBinary(prec + 1);
                left = new BinaryExpr { Op = op, Left = left, Right = right };
            }
            return left;
        }

        Expr ParseUnary()
        {
            if (Cur.Type == TokType.Op)
            {
                if (Cur.Text == "!" || Cur.Text == "-" || Cur.Text == "+" || Cur.Text == "~")
                { string op = Cur.Text; _p++; return new UnaryExpr { Op = op, Operand = ParseUnary() }; }
                if (Cur.Text == "++" || Cur.Text == "--")
                { string op = Cur.Text; _p++; return new UnaryExpr { Op = op + "pre", Operand = ParseUnary() }; }
            }
            return ParsePostfix();
        }

        Expr ParsePostfix()
        {
            var e = ParsePrimary();
            while (true)
            {
                if (Is("["))
                {
                    _p++;
                    var idx = ParseExpr();
                    Expect("]");
                    e = new IndexExpr { Target = e, Index = idx };
                }
                else if (Cur.Type == TokType.Op && (Cur.Text == "++" || Cur.Text == "--"))
                {
                    string op = Cur.Text; _p++;
                    e = new UnaryExpr { Op = op + "post", Operand = e };
                }
                else break;
            }
            return e;
        }

        Expr ParsePrimary()
        {
            var t = Cur;

            if (t.Type == TokType.Num)
            { _p++; return new NumLit { Value = double.Parse(t.Text, CultureInfo.InvariantCulture) }; }

            if (t.Type == TokType.Str)
            { _p++; return new StrLit { Value = t.Text }; }

            if (Is("("))
            { _p++; var e = ParseExpr(); Expect(")"); return e; }

            if (Is("{"))   // inicializador de arreglo { a, b, c }
            {
                _p++;
                var arr = new ArrayLit();
                if (!Is("}")) do { arr.Items.Add(ParseExpr()); } while (Accept(","));
                Expect("}");
                return arr;
            }

            if (t.Type == TokType.Id)
            {
                if (t.Text == "true")  { _p++; return new BoolLit { Value = true }; }
                if (t.Text == "false") { _p++; return new BoolLit { Value = false }; }

                string name = t.Text; _p++;

                // Nombre con punto: Serial.print, Serial.println, etc.
                while (Is("."))
                {
                    _p++;
                    if (Cur.Type != TokType.Id) throw new ParseException("Se esperaba un miembro tras '.'.");
                    name += "." + Cur.Text; _p++;
                }

                if (Is("("))   // llamada
                {
                    _p++;
                    var call = new CallExpr { Name = name };
                    if (!Is(")")) do { call.Args.Add(ParseExpr()); } while (Accept(","));
                    Expect(")");
                    return call;
                }
                return new Ident { Name = name };
            }

            throw new ParseException($"Token inesperado: '{t.Text}'.");
        }
    }
}
