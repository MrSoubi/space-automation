using System.Collections.Concurrent;
using System.Diagnostics;
using SpaceAutomation.Game;
using SpaceAutomation.Persistence;

namespace SpaceAutomation.Server;

public abstract record SessionCommand
{
    public sealed record Pause : SessionCommand;

    public sealed record Resume : SessionCommand;

    public sealed record Save : SessionCommand;

    public sealed record Quit : SessionCommand;

    // A piece of work submitted from an HTTP handler, to run on the game
    // thread, with its result handed back through the completion source.
    public sealed record Call(Func<object?> Work, TaskCompletionSource<object?> Completion) : SessionCommand;
}

// Owns the world and the clock on one dedicated thread. HTTP handlers submit
// commands and calls, then read the published snapshot; nothing outside this
// class ever touches the world. The loop never waits for a client: ticks fire
// on their own schedule and a slow client simply misses ticks — isolation
// replaces the old in-process instruction budget.
public sealed class GameSession : IDisposable
{
    private readonly BlockingCollection<SessionCommand> _commands = new BlockingCollection<SessionCommand>();

    private readonly string _savePath;
    private readonly Action<string> _print;
    private readonly Thread _thread;
    private volatile bool _quit;
    private volatile bool _paused;
    private volatile ServerState _state;
    private bool _disposed;

    public World World { get; }

    public ServerState State => _state;

    public bool Paused => _paused;

    public int TickMilliseconds { get; set; } = 1000;

    public GameSession(World world, string savePath, Action<string> print, bool startPaused)
    {
        World = world;
        _savePath = savePath;
        _print = print;
        _paused = startPaused;
        _state = StateProjection.Snapshot(world, running: !startPaused);
        _thread = new Thread(GameLoop) { Name = "game" };
    }

    public void Start() => _thread.Start();

    public void Wait() => _thread.Join();

    public void Pause() => Submit(new SessionCommand.Pause());

    public void Resume() => Submit(new SessionCommand.Resume());

    public void PauseOrResume()
    {
        if (_paused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    // Runs exactly one tick and reports when it has completed. Only offered
    // while paused; a running session already ticks on its own schedule.
    public Task<CommandResult> Step()
    {
        if (!_paused)
        {
            return Task.FromResult(CommandResult.Reject("not_paused"));
        }

        return Call(world =>
        {
            TickRound();
            return CommandResult.Ok();
        });
    }

    public void Save() => Submit(new SessionCommand.Save());

    public void Quit() => Submit(new SessionCommand.Quit());

    // Runs work on the game thread and returns its result. This is the only
    // way anything outside the session reaches the world.
    public async Task<T> Call<T>(Func<World, T> work)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new SessionCommand.Call(() => (object?)work(World), completion);

        try
        {
            _commands.Add(command);
        }

        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(GameSession), "the session is shutting down");
        }

        object? result = await completion.Task.ConfigureAwait(false);
        // The queued function returns T. T may itself be nullable, so a null
        // result is valid; this assertion only suppresses the compiler warning.
        return (T)result!;
    }

    private void Submit(SessionCommand command)
    {
        try
        {
            _commands.Add(command);
        }

        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            /* shutting down */
        }
    }

    private void GameLoop()
    {
        var clock = Stopwatch.StartNew();
        var nextTickAt = 0.0;

        while (!_quit)
        {
            if (_paused)
            {
                if (TryTake(Timeout.Infinite, out var command) && command is not null)
                {
                    Execute(command);
                }
            }
            else
            {
                while (TryTake(0, out var command) && command is not null)
                {
                    Execute(command);
                }

                if (_quit || _paused)
                {
                    continue;
                }

                var elapsed = clock.Elapsed.TotalMilliseconds;
                if (elapsed >= nextTickAt + TickMilliseconds)
                {
                    nextTickAt = elapsed; // no catch-up bursts
                }
                if (elapsed >= nextTickAt)
                {
                    TickRound();
                    nextTickAt += TickMilliseconds;
                }
                else if (TryTake((int)(nextTickAt - elapsed), out var command) && command is not null)
                {
                    Execute(command);
                }
            }
        }

        // The loop is done: fail any calls that were still queued so no HTTP
        // request is left hanging on a session that will never run them.
        while (_commands.TryTake(out var pending))
        {
            if (pending is SessionCommand.Call call && call.Completion is not null)
            {
                call.Completion.TrySetException(new ObjectDisposedException(nameof(GameSession)));
            }
        }
    }

    private bool TryTake(int timeout, out SessionCommand? command)
    {
        try
        {
            return _commands.TryTake(out command, timeout);
        }

        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            command = null;
            return false;
        }
    }

    private void Execute(SessionCommand command)
    {
        switch (command)
        {
            case SessionCommand.Call call:
            {
                var work = call.Work;
                var completion = call.Completion;
                try
                {
                    object? result = work();
                    completion.TrySetResult(result);
                }
                catch (Exception e)
                {
                    completion.TrySetException(e);
                }
                break;
            }

            case SessionCommand.Pause:
                _paused = true;
                break;

            case SessionCommand.Resume:
                _paused = false;
                break;

            case SessionCommand.Save:
                SaveWorld("manual");
                break;

            case SessionCommand.Quit:
                _quit = true;
                break;
        }

        PublishState();
    }

    private void TickRound()
    {
        World.Advance();
        SaveWorld("tick");
        PublishState();
    }

    private void SaveWorld(string reason)
    {
        try
        {
            WorldStore.Save(World, _savePath);
        }
        catch (Exception e)
        {
            _print($"autosave failed ({reason}): {e.Message}");
        }
    }

    // The snapshot is the only world state HTTP ever serves. Rebuilt after
    // every tick and every command, published as one immutable value.
    private void PublishState()
    {
        _state = StateProjection.Snapshot(World, running: !_paused);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _quit = true;
        _commands.CompleteAdding();

        if (_thread.IsAlive)
        {
            _thread.Join();
        }

        SaveWorld("exit"); // final save: paused sessions are not autosaved per tick
        _commands.Dispose();
    }
}
