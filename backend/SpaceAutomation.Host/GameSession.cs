using System.Collections.Concurrent;
using System.Diagnostics;
using MoonSharp.Interpreter;
using SpaceAutomation.Game;
using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;
using SpaceAutomation.Persistence;

namespace SpaceAutomation.Host;

public abstract record SessionCommand
{
    public sealed record Evaluate(string Line) : SessionCommand;
    public sealed record Pause : SessionCommand;
    public sealed record Resume : SessionCommand;
    public sealed record Step : SessionCommand;
    public sealed record Restart : SessionCommand;
    public sealed record Quit : SessionCommand;
}

public sealed record ObjectRow(string Id, string Type, string Position, string Detail);

// Owns the world, the Lua runtime, and the clock on one dedicated thread.
// Terminals only submit commands and read published state (Paused, Latched,
// Objects rows); they never touch the world directly.
public sealed class GameSession : IDisposable
{
    private readonly BlockingCollection<SessionCommand> _commands = new();
    private readonly string _entry;
    private readonly string _savePath;
    private readonly Action<string> _print;
    private readonly Thread _thread;
    private volatile bool _quit;
    private volatile bool _paused;
    private volatile bool _latched;
    private volatile IReadOnlyList<ObjectRow> _objects = [];
    private bool _disposed;

    public World World { get; }
    public LuaHost Host { get; private set; }
    public long InstructionBudget { get; set; }
    public int TickMilliseconds { get; set; } = 1000;

    public bool Paused => _paused;
    public bool Latched => _latched;
    public IReadOnlyList<ObjectRow> Objects => _objects;

    public GameSession(World world, string entry, string savePath, long instructionBudget, Action<string> print, bool startPaused)
    {
        World = world;
        _entry = entry;
        _savePath = savePath;
        InstructionBudget = instructionBudget;
        _print = print;
        Host = new LuaHost(world, print);
        var failure = Host.RunFile(entry);
        if (failure is null) failure = Host.CallStartup();
        if (failure is not null)
        {
            _print($"error in {entry}:\n{failure.Traceback}\npaused — fix the script and :restart");
            _latched = true;
        }
        _paused = startPaused || _latched;
        _thread = new Thread(GameLoop) { Name = "game" };
    }

    public void Start() => _thread.Start();
    public void Wait() => _thread.Join();
    public void Pause() => Submit(new SessionCommand.Pause());
    public void Resume() => Submit(new SessionCommand.Resume());
    public void PauseOrResume() { if (_paused) Resume(); else Pause(); }
    public void Step() => Submit(new SessionCommand.Step());
    public void Restart() => Submit(new SessionCommand.Restart());
    public void Quit() => Submit(new SessionCommand.Quit());
    public void EvaluateLine(string line) => Submit(new SessionCommand.Evaluate(line));
    private void Submit(SessionCommand command)
    {
        try { _commands.Add(command); } catch (ObjectDisposedException) { /* shutting down */ }
    }

    private void GameLoop()
    {
        var clock = Stopwatch.StartNew();
        var nextTickAt = 0.0;
        while (!_quit)
        {
            if (_paused || _latched)
            {
                if (TryTake(Timeout.Infinite, out var command)) Execute(command);
            }
            else
            {
                while (TryTake(0, out var command)) Execute(command);
                if (_quit || _paused || _latched) continue;
                var elapsed = clock.Elapsed.TotalMilliseconds;
                if (elapsed >= nextTickAt + TickMilliseconds) nextTickAt = elapsed; // no catch-up bursts
                if (elapsed >= nextTickAt) { TickRound(); nextTickAt += TickMilliseconds; }
                else if (TryTake((int)(nextTickAt - elapsed), out var command)) Execute(command);
            }
        }
    }

    private bool TryTake(int timeout, out SessionCommand command)
    {
        try { return _commands.TryTake(out command!, timeout); }
        catch (OperationCanceledException) { command = null!; return false; }
        catch (ObjectDisposedException) { command = null!; return false; }
    }

    private void Execute(SessionCommand command)
    {
        switch (command)
        {
            case SessionCommand.Evaluate(var line): Evaluate(line); break;
            case SessionCommand.Pause: _paused = true; _print($"paused at tick {World.Tick}"); break;
            case SessionCommand.Resume:
                if (_latched) { _print("script failed; :restart required before resuming"); break; }
                _paused = false; break;
            case SessionCommand.Step:
                if (_latched) { _print("script failed; :restart required before resuming"); break; }
                if (_paused) TickRound(); else _print("already running; pause first"); break;
            case SessionCommand.Restart: RestartRuntime(); break;
            case SessionCommand.Quit: _quit = true; break;
        }
        PublishObjects();
    }

    private void TickRound()
    {
        if (_latched) return;
        var failure = Host.CallUpdate(1);
        if (failure is not null)
        {
            _print($"error in update():\n{failure.Traceback}\npaused — fix the script and :restart");
            _latched = true; _paused = true;
            SaveWorld("error"); // preserve already-accepted commands and slots
            PublishObjects();
            return;
        }
        World.Advance();
        SaveWorld("tick");
        _print($"tick {World.Tick}");
        PublishObjects();
    }

    private void RestartRuntime()
    {
        Host = new LuaHost(World, _print) { InstructionBudget = InstructionBudget };
        var failure = Host.RunFile(_entry);
        if (failure is null) failure = Host.CallStartup();
        _latched = failure is not null;
        _paused = true;
        _print(failure is not null ? $"error in {_entry}:\n{failure.Traceback}" : $"runtime restarted at tick {World.Tick} (paused)");
    }

    private void Evaluate(string line)
    {
        if (line.Length == 0) return;
        if (line[0] == ':')
        {
            var control = line switch
            {
                ":pause" => (SessionCommand?)new SessionCommand.Pause(),
                ":resume" => new SessionCommand.Resume(),
                ":step" => new SessionCommand.Step(),
                ":restart" => new SessionCommand.Restart(),
                ":quit" => new SessionCommand.Quit(),
                _ => null,
            };
            if (control is null) { _print("unknown control; available: :pause :resume :step :restart :quit"); return; }
            Execute(control);
            return;
        }
        var (value, failure) = Host.Eval(line);
        if (failure is not null) { _print(failure.Traceback); return; }
        if (value is { Type: not (DataType.Nil or DataType.Void) }) _print(LuaHost.Format(value));
    }

    private void SaveWorld(string reason)
    {
        try { WorldStore.Save(World, _savePath); }
        catch (Exception e) { _print($"autosave failed ({reason}): {e.Message}"); }
    }

    private void PublishObjects()
    {
        _objects = World.Objects.Values.OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(obj => new ObjectRow(obj.Id, Model.ObjectKey(obj),
                $"{obj.Position.X:0.##}, {obj.Position.Y:0.##}", Describe(obj))).ToArray();
    }

    private static string Describe(GameObject obj) => obj switch
    {
        Rover rover => string.Join(" · ", new[]
        {
            rover.Battery is { } battery ? $"battery {battery.Charge:0.##}/{battery.Capacity:0.##} J" : "no battery",
            rover.Motor is { } ? $"max {rover.MaxSpeed:0.##} m/t" : "no motor",
        }),
        Battery battery => $"{battery.Charge:0.##}/{battery.Capacity:0.##} J",
        Motor motor => $"max {motor.MaxSpeed:0.##} m/t · {motor.EnergyPerMove:0.##} J/move",
        SolarPanel panel => $"{panel.EnergyPerTick:0.##} J/tick{(panel.Enabled ? "" : " (off)")}",
        _ => "",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _quit = true;
        _commands.CompleteAdding();
        if (_thread.IsAlive) _thread.Join();
        if (World is not null) SaveWorld("exit"); // final save: paused sessions are not autosaved per tick
        _commands.Dispose();
    }
}
