using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using SpaceAutomation.Game;
using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;
using SpaceAutomation.Persistence;
using SpaceAutomation.Server;

int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception e) { Console.Error.WriteLine($"FAIL {name}: {e}"); failed++; }
}
void Check(bool ok, string message = "Assertion failed") { if (!ok) throw new Exception(message); }
void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < 1e-9, $"{actual} != {expected}");
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected an exception"); }
World RoverWorld(double charge = 20) => new(Scenario.RoverAssembly("r", charge: charge));

Test("movement normalized, capped and one tick only", () => {
    var world = RoverWorld(); var rover = world.Find<Rover>("r")!;
    Check(rover.Move(new(3, 4), 100).Accepted); world.Advance(); Near(rover.Position.X, 1.8); Near(rover.Position.Y, 2.4);
    var previous = rover.Position; world.Advance(); Check(rover.Position == previous);
});
Test("invalid orders do not reserve movement; zero speed does", () => {
    var world = RoverWorld(); var rover = world.Find<Rover>("r")!;
    Check(rover.Move(Vector2.Zero, 1).Reason == "invalid_direction");
    Check(rover.Move(new(1, 0), double.NaN).Reason == "invalid_speed");
    Check(rover.Move(new(1, 0), -1).Reason == "invalid_speed");
    Check(rover.Move(new(1, 0), 0).Accepted);
    Check(rover.Move(new(1, 0), 1).Reason == "movement_already_requested");
    world.Advance(); Check(rover.Position == Vector2.Zero);
});
Test("extreme finite direction normalizes without overflow", () => {
    var world = RoverWorld(); var rover = world.Find<Rover>("r")!;
    Check(rover.Move(new(1e308, 1e308), 2).Accepted); world.Advance(); Near(rover.Position.X, Math.Sqrt(2));
});
Test("hub star, cycle, split and storage ownership", () => {
    var hub = new Hub { Id = "hub" }; var a = new Battery { Id = "a" }; var b = new Battery { Id = "b" };
    var world = new World([hub, a, b]); hub.Connect("a"); hub.Connect("b"); a.Connect("b");
    Near(world.Energy.GridFor("hub").Capacity, 40); hub.Disconnect("a"); Near(world.Energy.GridFor("hub").Capacity, 40);
    b.Disconnect("hub"); Near(world.Energy.GridFor("hub").Capacity, 0); Near(a.Charge + b.Charge, 40);
});
Test("generation, deficit and storage cap conserve energy", () => {
    var b = new Battery { Id = "b", Storage = new() { Capacity = 5, Charge = 4 } };
    var s = new SolarPanel { Id = "s", Source = new() { ProductionPerTick = 3 } }; var m = new Motor { Id = "m" };
    var w = new World([b, s, m]); s.Connect("b"); s.Connect("m"); m.Consumer.Demand = 6; w.Advance(); Near(b.Charge, 1); Near(m.Consumer.Received, 6);
    m.Consumer.Demand = 0; w.Advance(); w.Advance(); Near(b.Charge, 5);
    s.SetEnabled(false); m.Consumer.Demand = 7; w.Advance(); Near(m.Consumer.Received, 0); Near(b.Charge, 5);
});
Test("consumer priority is stable and full or zero", () => {
    var b = new Battery { Id = "b", Storage = new() { Charge = 3 } }; var a = new Motor { Id = "a" }; var z = new Motor { Id = "z" };
    var w = new World([z, b, a]); a.Connect("b"); z.Connect("b"); a.Consumer.Demand = z.Consumer.Demand = 2;
    w.Advance(); Near(a.Consumer.Received, 2); Near(z.Consumer.Received, 0); Near(b.Charge, 1);
});
Test("empty rover recovers through real portable panel", () => {
    var world = RoverWorld(0); var rover = world.Find<Rover>("r")!;
    rover.Move(new(1, 0), 1); world.Advance(); Check(rover.LastMoveResult == "not_enough_energy");
    for (int i = 0; i < 10; i++) world.Advance(); rover.Move(new(1, 0), 1); world.Advance(); Near(rover.Position.X, 1);
});
Test("replace real parts without losing charge or ownership", () => {
    var world = RoverWorld(); var rover = world.Find<Rover>("r")!; var old = rover.Battery!;
    var spare = new Battery { Id = "spare", Storage = new() { Capacity = 40, Charge = 31 } }; world.Add(spare);
    Check(rover.ReplaceComponent("battery", "spare").Accepted); Check(old.InstalledIn is null); Near(old.Charge, 20); Check(ReferenceEquals(rover.Battery, spare));
    Check(spare.Connect("r").Reason == "connect_through_host");
    rover.Move(new(1, 0), 2); Check(rover.ReplaceComponent("battery", old.Id).Reason == "movement_pending"); world.Advance();
    Check(rover.ReplaceComponent("battery", old.Id).Reason == "component_out_of_reach");
    Check(rover.ReplaceComponent("motor", null).Accepted); Check(rover.Move(new(1, 0), 1).Reason == "missing_motor");
});
Test("connected rover cannot move or replace parts", () => {
    var world = RoverWorld(); world.Add(new Hub { Id = "hub" }); var rover = world.Find<Rover>("r")!;
    rover.Connect("hub"); Check(rover.Move(new(1, 0), 1).Reason == "connected_to_grid"); Check(rover.ReplaceComponent("motor", null).Reason == "connected_to_grid");
    Near(world.Energy.GridFor("hub").Capacity, 20);
});
Test("saved components and pending command slots round-trip", () => {
    var world = RoverWorld(); world.Find<Rover>("r")!.Move(new(3, 4), 2);
    var loaded = WorldStore.Deserialize(WorldStore.Serialize(world)); var rover = loaded.Find<Rover>("r")!;
    Check(rover.Move(new(1, 0), 1).Reason == "movement_already_requested"); loaded.Advance(); Near(rover.Position.X, 1.2); Near(rover.Battery!.Charge, 18.2);
});
Test("version three rover saves migrate without installed_in", () => {
    var input = JsonNode.Parse("""
        {"version":3,"tick":0,"objects":[{"type":"rover","state":{
        "id":"r","position":{"$type":"Vector2","x":0,"y":0},"speed_limit":3,"last_move_result":null,"_movement":null,
        "energy":{"$type":"EnergyPort","fields":{"connections":[],"source_id":null,"consumer_id":null,"storage_id":null}},
        "installed_in":null}}]}
        """)!.AsObject();
    var world = WorldStore.Deserialize(input); Check(world.Find<Rover>("r") is not null);
    Check(input["version"]!.GetValue<int>() == 3);
    input["objects"]![0]!["state"]!["installed_in"] = "other"; Throws(() => WorldStore.Deserialize(input));
});
Test("invalid saves do not silently overwrite fields or ownership", () => {
    var data = WorldStore.Serialize(RoverWorld()); data["objects"]![0]!["state"]!["unexpected"] = 1; Throws(() => WorldStore.Deserialize(data));
    data = WorldStore.Serialize(RoverWorld()); data["objects"]![1]!["state"]!["installed_in"] = "missing"; Throws(() => WorldStore.Deserialize(data));
});
Test("a new attributed object round-trips through the save system", () => {
    Model.Objects.Add("test_counter", typeof(TestCounter)); // fixtures live outside the Game assembly; real types self-register
    try {
        var world = new World([new TestCounter { Id = "counter" }]);
        world.Find<TestCounter>("counter")!.Increment(7);
        var restored = WorldStore.Deserialize(WorldStore.Serialize(world));
        Check(restored.Find<TestCounter>("counter")!.Count == 7, "new [GameType] objects persist without persistence edits");
    } finally { Model.Objects.Remove("test_counter"); }
});

// HTTP test harness: boots the real server on an ephemeral localhost port and
// talks to it with a plain HttpClient, exactly like a player program would.
(HttpClient Http, GameSession Session, WebApplication App) StartServer(World world, string savePath, bool paused = true, int tickMilliseconds = 1000)
{
    var session = new GameSession(world, savePath, _ => { }, paused) { TickMilliseconds = tickMilliseconds };
    var app = ServerApi.Build(session, port: 0);
    app.StartAsync().GetAwaiter().GetResult();
    var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
    session.Start();
    return (http, session, app);
}
(int Status, JsonElement? Json) Fetch(HttpClient http, string path)
{
    using var response = http.GetAsync(path).GetAwaiter().GetResult();
    var raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    return ((int)response.StatusCode, raw.Length == 0 ? null : JsonDocument.Parse(raw).RootElement.Clone());
}
(int Status, JsonElement? Json) Post(HttpClient http, string path, object? body = null)
{
    using var response = http.PostAsJsonAsync(path, body).GetAwaiter().GetResult();
    var raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    return ((int)response.StatusCode, raw.Length == 0 ? null : JsonDocument.Parse(raw).RootElement.Clone());
}
JsonElement Get(HttpClient http, string path) => Fetch(http, path).Json!.Value;
JsonElement ObjectOf(JsonElement state, string id)
{
    foreach (var obj in state.GetProperty("objects").EnumerateArray())
        if (obj.GetProperty("id").GetString() == id) return obj;
    throw new Exception($"object {id} missing from state");
}
string? ReasonOf(JsonElement result) => result.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
void ShutDown(GameSession session, WebApplication app)
{
    session.Quit(); session.Wait(); session.Dispose();
    app.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

Test("http commands drive the rover through the real domain", () => {
    var savePath = Path.Combine(Path.GetTempPath(), "server-" + Guid.NewGuid() + ".json");
    var (http, session, app) = StartServer(RoverWorld(), savePath);
    try {
        var move = Post(http, "/objects/r/move", new { direction = new { x = 1.0, y = 0.0 }, speed = 2.0 });
        Check(move.Status == 200 && move.Json!.Value.GetProperty("accepted").GetBoolean());
        var step = Post(http, "/session/step");
        Check(step.Status == 200 && step.Json!.Value.GetProperty("accepted").GetBoolean());
        var state = Get(http, "/state");
        Check(state.GetProperty("tick").GetInt64() == 1);
        Near(ObjectOf(state, "r").GetProperty("position").GetProperty("x").GetDouble(), 2);
    } finally { ShutDown(session, app); }
    var loaded = WorldStore.Load(savePath);
    Check(loaded.Find<Rover>("r") is not null, "the session must save the world on exit");
    Near(loaded.Find<Rover>("r")!.Position.X, 2);
});
Test("http command results mirror domain rejections", () => {
    var (http, session, app) = StartServer(RoverWorld(), Path.Combine(Path.GetTempPath(), "server-" + Guid.NewGuid() + ".json"));
    try {
        string? Move(object? body) => ReasonOf(Post(http, "/objects/r/move", body).Json!.Value);
        Check(Move(new { direction = new { x = 0.0, y = 0.0 }, speed = 1.0 }) == "invalid_direction");
        Check(Move(new { direction = new { x = 1.0 }, speed = 1.0 }) == "invalid_direction"); // missing y
        Check(Move(new { speed = 1.0 }) == "invalid_direction"); // missing direction
        Check(Move(new { direction = new { x = 1.0, y = 0.0 }, speed = -1.0 }) == "invalid_speed");
        Check(Move(new { direction = new { x = 1.0, y = 0.0 } }) == "invalid_speed"); // missing speed
        Check(Post(http, "/objects/r/move", new { direction = new { x = 1.0, y = 0.0 }, speed = 1.0 }).Json!.Value.GetProperty("accepted").GetBoolean());
        Check(Move(new { direction = new { x = 1.0, y = 0.0 }, speed = 1.0 }) == "movement_already_requested");
        Check(Post(http, "/objects/r-battery/move", new { direction = new { x = 1.0, y = 0.0 }, speed = 1.0 }).Status == 400, "batteries cannot move");
        Check(Post(http, "/objects/missing/move", new { direction = new { x = 1.0, y = 0.0 }, speed = 1.0 }).Status == 404);
        var malformed = Post(http, "/objects/r/move", new { direction = new { x = 1.0, y = 0.0 }, speed = "fast" });
        Check(malformed.Status == 400, "malformed bodies are binding failures");
        Check(ReasonOf(malformed.Json!.Value) == "invalid_body");
        Check(Post(http, "/objects/r/move", "not json at all").Status == 400, "unparseable bodies are rejected too");
    } finally { ShutDown(session, app); }
});
Test("state projection exposes live observed values", () => {
    var (http, session, app) = StartServer(RoverWorld(), Path.Combine(Path.GetTempPath(), "server-" + Guid.NewGuid() + ".json"));
    try {
        var state = Get(http, "/state");
        Check(state.GetProperty("tick").GetInt64() == 0);
        Check(state.GetProperty("running").GetBoolean() == false, "server starts paused in this test");
        Check(state.GetProperty("objects").GetArrayLength() == 4);
        var rover = ObjectOf(state, "r");
        Check(rover.GetProperty("type").GetString() == "rover");
        Near(rover.GetProperty("position").GetProperty("x").GetDouble(), 0);
        Near(rover.GetProperty("speed_limit").GetDouble(), 3);
        Near(rover.GetProperty("max_speed").GetDouble(), 3);
        Check(rover.GetProperty("energy").GetProperty("storage_id").GetString() == "r-battery");
        var battery = ObjectOf(state, "r-battery");
        Near(battery.GetProperty("charge").GetDouble(), 20);
        Near(battery.GetProperty("capacity").GetDouble(), 20);
        Check(battery.GetProperty("installed_in").GetString() == "r");
        var fetched = Fetch(http, "/objects/r");
        Check(fetched.Status == 200 && fetched.Json!.Value.GetProperty("id").GetString() == "r");
        Check(Fetch(http, "/objects/missing").Status == 404);
        Post(http, "/session/step");
        Near(ObjectOf(Get(http, "/state"), "r").GetProperty("position").GetProperty("x").GetDouble(), 0);
        Check(ObjectOf(Get(http, "/state"), "r").GetProperty("last_move_result").GetString() is null, "no move request means no movement");
    } finally { ShutDown(session, app); }
});
Test("grid queries work through http", () => {
    var world = Scenario.Create();
    var (http, session, app) = StartServer(world, Path.Combine(Path.GetTempPath(), "server-" + Guid.NewGuid() + ".json"));
    try {
        var grid = Get(http, "/objects/hub/grid_status");
        Check(grid.GetProperty("members")[0].GetString() == "hub");
        var state = Get(http, "/state");
        Check(state.GetProperty("objects").GetArrayLength() == world.Objects.Count, "every object appears in the state");
        var types = state.GetProperty("objects").EnumerateArray().Select(obj => obj.GetProperty("type").GetString()!).ToList();
        Check(types.Count(type => type == "rover") == 2, "the fleet is filterable client-side");
        Check(types.Count(type => type == "battery") == 4);
        Check(Fetch(http, "/objects/rover-1-motor/grid_status").Status == 200, "any energy node can query its grid");
    } finally { ShutDown(session, app); }
});
Test("connect, disconnect and set_enabled work through http", () => {
    var world = RoverWorld();
    world.Add(new Hub { Id = "hub" });
    var (http, session, app) = StartServer(world, Path.Combine(Path.GetTempPath(), "server-" + Guid.NewGuid() + ".json"));
    try {
        Check(Post(http, "/objects/r/connect", new { target = "hub" }).Json!.Value.GetProperty("accepted").GetBoolean());
        Check(ReasonOf(Post(http, "/objects/r/move", new { direction = new { x = 1.0, y = 0.0 }, speed = 1.0 }).Json!.Value) == "connected_to_grid");
        Check(Post(http, "/objects/r/disconnect", new { target = "hub" }).Json!.Value.GetProperty("accepted").GetBoolean());
        Check(ReasonOf(Post(http, "/objects/r/connect", new { }).Json!.Value) == "invalid_energy_endpoint");
        Check(Post(http, "/objects/r-solar-panel/set_enabled", new { enabled = false }).Json!.Value.GetProperty("accepted").GetBoolean());
        Check(ObjectOf(Get(http, "/state"), "r-solar-panel").GetProperty("enabled").GetBoolean() == false);
        Check(ReasonOf(Post(http, "/objects/r-solar-panel/set_enabled", new { }).Json!.Value) == "invalid_enabled");
    } finally { ShutDown(session, app); }
});
Test("http commands land between ticks while auto-running", () => {
    var savePath = Path.Combine(Path.GetTempPath(), "server-" + Guid.NewGuid() + ".json");
    var (http, session, app) = StartServer(RoverWorld(), savePath, paused: false, tickMilliseconds: 50);
    try {
        WaitUntil(() => session.State.Running && session.State.Tick >= 1, "auto ticks");
        var move = Post(http, "/objects/r/move", new { direction = new { x = 1.0, y = 0.0 }, speed = 1.0 });
        Check(move.Status == 200 && move.Json!.Value.GetProperty("accepted").GetBoolean(), "commands are admitted while running");
        WaitUntil(() => session.State.Tick >= 2 && ObjectOf(Get(http, "/state"), "r").GetProperty("position").GetProperty("x").GetDouble() >= 1, "move lands at the next tick");
        Check(ReasonOf(Post(http, "/session/step").Json!.Value) == "not_paused", "stepping needs a paused session");
        Post(http, "/session/pause");
        WaitUntil(() => !session.State.Running, "pause takes effect");
        Check(Post(http, "/session/step").Json!.Value.GetProperty("accepted").GetBoolean());
    } finally { ShutDown(session, app); }
    var loaded = WorldStore.Load(savePath);
    Check(loaded.Find<Rover>("r") is not null, "the running session saved on exit");
});

static void WaitUntil(Func<bool> condition, string description = "condition")
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!condition())
    {
        if (DateTime.UtcNow > deadline) throw new Exception($"timeout waiting for {description}");
        Thread.Sleep(10);
    }
}
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

[GameType("test_counter")]
public sealed class TestCounter : GameObject
{
    [Observed("count")] public int Count { get; private set; }
    public int Increment(int amount = 1) => Count += amount;
}
