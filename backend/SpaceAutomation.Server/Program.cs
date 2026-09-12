using SpaceAutomation.Game;
using SpaceAutomation.Persistence;
using SpaceAutomation.Server;

try
{
    // The default save lives in the player's home directory so that every way
    // of starting the game (just run, VS Code, a raw binary) shares one world
    // instead of creating one save per working directory.
    var save = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".space-automation", "world-host.json");
    var port = 8377;
    var paused = false;

    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--paused")
        {
            paused = true;
        }

        else if ((args[i] == "--save" || args[i] == "--port") && i + 1 < args.Length)
        {
            string option = args[i];
            i++;
            string value = args[i];

            if (option == "--save")
            {
                save = value;
            }

            else if (!int.TryParse(value, out port) || port < 1 || port > 65535)
            {
                Console.Error.WriteLine("Invalid port");
                return 2;
            }
        }
        else
        {
            Console.Error.WriteLine("Usage: SpaceAutomation.Server [--save path] [--port n] [--paused]");
            return 2;
        }
    }

    // A save the current game cannot read (an older world model, a broken
    // file) must never brick the server: keep it aside, say so, and start a
    // fresh expedition. The player loses nothing — the old save stays on disk.
    World world;
    try
    {
        world = WorldStore.Load(save);
    }
    catch (InvalidDataException error)
    {
        string reason = error.InnerException?.Message ?? error.Message;
        string backup = save + ".broken-" + DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");

        if (File.Exists(save))
        {
            File.Move(save, backup);
        }

        world = Scenario.Create();
        Console.WriteLine($"the save at {save} cannot be used by this version: {reason}");
        Console.WriteLine($"kept it as {backup} and started a fresh expedition");
    }

    using var window = new ParticleWindow();
    using var session = new GameSession(world, save, Console.Error.WriteLine, paused);
    var app = ServerApi.Build(session, port);

    try
    {
        // Keep SDL on the main thread; HTTP and simulation run independently.
        app.StartAsync().GetAwaiter().GetResult();
        session.Start();
        window.Run(session, app.Lifetime.ApplicationStopping);
    }
    finally
    {
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
