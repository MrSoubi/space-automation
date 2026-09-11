using System.Collections.Concurrent;
using SpaceAutomation.Game;
using SpaceAutomation.Host;
using SpaceAutomation.Persistence;

// Entry point: picks the terminal frontend. Interactive terminals get the
// Terminal.Gui TUI (buttons, tabs, mouse); --plain or redirected streams fall
// back to the line-based console. The game itself runs identically in both.

try
{
    var save = ".space-automation/world-host.json";
    var entry = "player/main.lua";
    var plain = false;
    var paused = false;
    var budget = LuaHost.DefaultInstructionBudget;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--paused") paused = true;
        else if (args[i] == "--plain") plain = true;
        else if (args[i] is "--save" or "--script" or "--budget" && i + 1 < args.Length)
        {
            var value = args[++i];
            if (args[i - 1] == "--save") save = value;
            else if (args[i - 1] == "--script") entry = value;
            else if (!long.TryParse(value, out budget) || budget < 1) { Console.Error.WriteLine("Invalid budget"); return 2; }
        }
        else { Console.Error.WriteLine("Usage: SpaceAutomation.Host [--save path] [--script path] [--budget n] [--paused] [--plain]"); return 2; }
    }

    var world = WorldStore.Load(save);
    EnsureEntryScript(entry);
    plain = plain || Console.IsInputRedirected || Console.IsOutputRedirected;

    if (plain)
    {
        var session = new GameSession(world, entry, save, budget, Console.WriteLine, paused);
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; session.Quit(); };
        new Thread(() => { while (true) { var line = Console.ReadLine(); if (line is null) break; session.EvaluateLine(line); } })
            { IsBackground = true, Name = "input" }.Start();
        Console.WriteLine($"space automation host — tick {session.World.Tick}, {session.World.Objects.Count} objects");
        session.Start();
        session.Wait();
        session.Dispose();
    }
    else
    {
        var output = new ConcurrentQueue<string>();
        var session = new GameSession(world, entry, save, budget, output.Enqueue, paused);
        session.Start();
        TuiTerminal.Run(session, output);
    }
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }

static void EnsureEntryScript(string path)
{
    if (File.Exists(path)) return;
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, """
        -- Space Automation player script. The game calls startup() once after
        -- loading, then update(dt) once per tick. Use the interpreter prompt to
        -- inspect anything live. Edit freely; :restart reloads this file.

        function startup()
        end

        function update(dt)
            -- Example: keep the first rover moving east at half speed.
            -- local rover = station.get_fleet()[1]
            -- if rover then rover:move(vector(1, 0), rover.max_speed / 2) end
        end
        """);
}
