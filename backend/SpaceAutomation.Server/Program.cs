using SpaceAutomation.Game;
using SpaceAutomation.Persistence;
using SpaceAutomation.Server;

// Entry point: the simulation runs as an HTTP service on localhost. The game
// gives players nothing but this API — automation and tools are external
// programs in any language, polling state and submitting commands. The server
// never waits for a client; ticks fire on their own schedule.

try
{
    var save = ".space-automation/world-host.json";
    var port = 8377;
    var paused = false;

    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--paused") paused = true;

        else if (args[i] is "--save" or "--port" && i + 1 < args.Length)
        {
            var value = args[++i];

            if (args[i - 1] == "--save") save = value;

            else if (!int.TryParse(value, out port) || port is < 1 or > 65535) { Console.Error.WriteLine("Invalid port"); return 2; }
        }
        else { Console.Error.WriteLine("Usage: SpaceAutomation.Server [--save path] [--port n] [--paused]"); return 2; }
    }

    var world = WorldStore.Load(save);
    var session = new GameSession(world, save, Console.WriteLine, paused);
    var app = ServerApi.Build(session, port);

    session.Start();
    StartOpsConsole(session);

    Console.WriteLine($"space automation server — tick {world.Tick}, {world.Objects.Count} objects — http://127.0.0.1:{port}");
    Console.WriteLine("controls: :pause :resume :step :save :quit — or POST /session/... from any client");

    app.Run(); // Ctrl+C: graceful shutdown, then the session saves on dispose
    session.Dispose();
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }

// A tiny operator console for the terminal the server was started in. Without
// a client connected you can still pause, step, save and quit.
static void StartOpsConsole(GameSession session)
{
    if (Console.IsInputRedirected) return; // service mode: no operator at a terminal

    new Thread(() =>
    {
        while (true)
        {
            var line = Console.ReadLine();
            if (line is null) break;

            switch (line.Trim())
            {
                case ":pause": session.Pause(); break;
                case ":resume": session.Resume(); break;
                case ":step": session.Step(); break;
                case ":save": session.Save(); break;
                case ":quit": session.Quit(); break;
                case "": break;
                default: Console.WriteLine("unknown control; available: :pause :resume :step :save :quit"); break;
            }
        }
    }) { IsBackground = true, Name = "ops" }.Start();
}
