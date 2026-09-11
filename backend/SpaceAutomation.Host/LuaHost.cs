using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using SpaceAutomation.Game;

namespace SpaceAutomation.Host;

public sealed record ScriptFailure(string Message, string Traceback);

// Runs the player's Lua in the game process. Every script invocation runs under
// an instruction budget: a runaway loop is aborted deterministically instead of
// hanging the game, which is what makes a single-process design safe.
public sealed class LuaHost
{
    public const long DefaultInstructionBudget = 2_000_000;
    private readonly BudgetDebugger _debugger = new();
    private Script Script { get; }
    private LuaApi Api { get; }
    public long InstructionBudget { get; set; } = DefaultInstructionBudget;

    public LuaHost(World world, Action<string> print)
    {
        Script = new Script(CoreModules.Preset_Complete);
        Script.Options.DebugPrint = print;
        Script.AttachDebugger(_debugger);
        Api = new LuaApi(Script, world);
        Api.Install();
    }

    public ScriptFailure? RunFile(string path) => RunCode(File.ReadAllText(path));
    public ScriptFailure? RunCode(string code)
    {
        _debugger.Arm(InstructionBudget * 10);
        try { Script.DoString(code); }
        catch (SyntaxErrorException e) { return new ScriptFailure(e.Message, e.DecoratedMessage); }
        catch (ScriptRuntimeException e) { return new ScriptFailure(e.Message, e.DecoratedMessage); }
        catch (ScriptBudgetExceededException e) { return new ScriptFailure(e.Message, e.Message); }
        if (Script.Globals.Get("startup").Type != DataType.Function)
            return new ScriptFailure("main.lua must define startup()", "main.lua must define startup()");
        if (Script.Globals.Get("update").Type != DataType.Function)
            return new ScriptFailure("main.lua must define update(dt)", "main.lua must define update(dt)");
        return null;
    }

    public ScriptFailure? CallStartup() => CallGlobal("startup", InstructionBudget * 10);
    public ScriptFailure? CallUpdate(double dt) => CallGlobal("update", InstructionBudget);
    private ScriptFailure? CallGlobal(string name, long budget)
    {
        _debugger.Arm(budget);
        try { Script.Call(Script.Globals.Get(name)); return null; }
        catch (ScriptRuntimeException e) { return new ScriptFailure(e.Message, e.DecoratedMessage); }
        catch (ScriptBudgetExceededException e) { return new ScriptFailure(e.Message, e.Message); }
    }

    // Bare expressions are echoed; statements run silently. Both share Globals
    // with main.lua, so the interpreter sees and changes the live namespace.
    public (DynValue? Value, ScriptFailure? Failure) Eval(string line)
    {
        SyntaxErrorException? syntax = null;
        foreach (var code in new[] { "return " + line, line })
            try
            {
                _debugger.Arm(InstructionBudget);
                return (Script.DoString(code), null);
            }
            catch (SyntaxErrorException e) { if (code == line) syntax = e; }
            catch (ScriptRuntimeException e) { return (null, new ScriptFailure(e.Message, e.DecoratedMessage)); }
            catch (ScriptBudgetExceededException e) { return (null, new ScriptFailure(e.Message, e.Message)); }
        return (null, new ScriptFailure(syntax!.Message, syntax.DecoratedMessage));
    }

    // Hand-written REPL echo formatting so values read naturally:
    // 3, "ok", true, {1.5, 0}, {accepted=false, reason="invalid_speed"}
    public static string Format(DynValue value) => Format(value, 0);
    private static string Format(DynValue value, int depth)
    {
        switch (value.Type)
        {
            case DataType.Nil: case DataType.Void: return "nil";
            case DataType.Boolean: return value.Boolean ? "true" : "false";
            case DataType.Number: return value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case DataType.String: return "\"" + value.String.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
            case DataType.Table:
                if (depth >= 2) return "{...}";
                var array = new List<(double Key, string Text)>();
                var map = new List<(string Key, string Text)>();
                foreach (var pair in value.Table.Pairs)
                {
                    if (pair.Key.Type == DataType.Number && pair.Key.Number >= 1 && pair.Key.Number % 1 == 0)
                        array.Add((pair.Key.Number, Format(pair.Value, depth + 1)));
                    else if (pair.Key.Type == DataType.String && System.Text.RegularExpressions.Regex.IsMatch(pair.Key.String, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
                        map.Add((pair.Key.String, Format(pair.Value, depth + 1)));
                    else map.Add((Format(pair.Key, depth + 1), Format(pair.Value, depth + 1)));
                }
                var parts = array.OrderBy(x => x.Key).Select(x => x.Text)
                    .Concat(map.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Text}"));
                return "{" + string.Join(", ", parts) + "}";
            default: return value.Type.ToString().ToLowerInvariant();
        }
    }

    // MoonSharp calls IsPauseRequested once per bytecode instruction while a
    // debugger is attached, even in Run mode; throwing from it is the only
    // supported way to preempt a script (e.g. "while true do end").
    private sealed class BudgetDebugger : IDebugger
    {
        private long _remaining = long.MaxValue;
        public void Arm(long budget) => _remaining = budget;
        public bool IsPauseRequested()
        {
            if (--_remaining < 0) throw new ScriptBudgetExceededException();
            return false;
        }
        public DebuggerAction GetAction(int ip, SourceRef sourceref) => new() { Action = DebuggerAction.ActionType.Run };
        public bool SignalRuntimeException(ScriptRuntimeException ex) => false;
        public void SignalExecutionEnded() { }
        public void Update(WatchType watchType, IEnumerable<WatchItem> items, int stackFrameIndex) { }
        public List<DynamicExpression> GetWatchItems() => [];
        public void SetSourceCode(SourceCode sourceCode) { }
        public void SetByteCode(string[] byteCode) { }
        public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }
        public DebuggerCaps GetDebuggerCaps() => 0;
        public void SetDebugService(DebugService debugService) { }
    }
}

public sealed class ScriptBudgetExceededException(string message = "script exceeded its instruction budget and was aborted")
    : Exception(message);
