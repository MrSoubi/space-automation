using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using MoonSharp.Interpreter;
using SpaceAutomation.Game;
using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;
using SpaceAutomation.Host;
using SpaceAutomation.Persistence;

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

LuaHost LuaWorld(string code, World? world = null)
{
    var host = new LuaHost(world ?? RoverWorld(), _ => { });
    Check(host.RunCode(code) is null, "script must load");
    return host;
}
Test("lua update drives the rover through the real domain", () => {
    var world = RoverWorld();
    var host = LuaWorld("""
        function startup() end
        function update(dt) station.get_fleet()[1]:move(vector(1, 0), 2) end
        """, world);
    Check(host.CallStartup() is null); Check(host.CallUpdate(1) is null);
    world.Advance(); Near(world.Find<Rover>("r")!.Position.X, 2);
});
Test("lua hangs are aborted by the instruction budget", () => {
    var world = RoverWorld();
    var host = LuaWorld("function startup() end function update(dt) while true do end end", world);
    host.InstructionBudget = 5000;
    Check(host.CallUpdate(1) is not null, "runaway update must be aborted");
    world.Advance(); Check(world.Find<Rover>("r") is not null, "world survives the abort");
    host.InstructionBudget = LuaHost.DefaultInstructionBudget;
    var (value, failure) = host.Eval("return 1 + 1");
    Check(failure is null && value!.Number == 2, "script usable after abort");
});
Test("the lua repl shares the live namespace with main.lua", () => {
    var host = LuaWorld("function startup() end function update(dt) end");
    Check(host.Eval("x = 41").Failure is null);
    var (value, failure) = host.Eval("x + 1");
    Check(failure is null && value!.Number == 42);
});
Test("lua command results mirror domain rejections", () => {
    var host = LuaWorld("function startup() end function update(dt) end");
    string? Reason(string code) { var (v, f) = host.Eval(code); Check(f is null); return v!.Table.Get("reason").Type == DataType.Nil ? null : v.Table.Get("reason").String; }
    bool Accepted(string code) { var (v, f) = host.Eval(code); Check(f is null); return v!.Table.Get("accepted").Boolean; }
    Check(!Accepted("station.get_object('r'):move(vector(0, 0), 1)"));
    Check(Reason("station.get_object('r'):move(vector(0, 0), 1)") == "invalid_direction");
    Check(Reason("station.get_object('r'):move('nope', 1)") == "invalid_direction");
    Check(Reason("station.get_object('r'):move(vector(1, 0), -1)") == "invalid_speed");
    Check(Reason("station.get_object('r'):move(vector(1, 0), true)") == "invalid_speed");
    Check(Accepted("station.get_object('r'):move(vector(1, 0), 1)"));
    Check(Reason("station.get_object('r'):move(vector(1, 0), 1)") == "movement_already_requested");
});
Test("lua proxies expose live state and stable identity", () => {
    var world = RoverWorld();
    var host = LuaWorld("function startup() end function update(dt) end", world);
    Check(host.Eval("return station.get_object('r').position.x").Value!.Number == 0);
    Check(host.Eval("return station.get_object('r').battery.charge").Value!.Number == 20);
    Check(host.Eval("return station.get_object('r').energy.storage_id").Value!.String == "r-battery");
    Check(host.Eval("return station.get_object('r').installed_in").Value!.Type == DataType.Nil);
    Check(host.Eval("return station.get_object('missing')").Value!.Type == DataType.Nil);
    Check(host.Eval("return station.get_object('r') == station.get_object('r')").Value!.Boolean);
    world.Advance();
    Check(host.Eval("return station.get_object('r').position.x").Value!.Number == 0, "no move request means no movement");
});
Test("grid queries work through lua", () => {
    var host = LuaWorld("function startup() end function update(dt) end", Scenario.Create());
    Check(host.Eval("return station.get_object('hub'):grid_status().members[1]").Value!.String == "hub");
    Check(host.Eval("return #station.get_fleet()").Value!.Number == 2);
    Check(host.Eval("return #station.get_objects('battery')").Value!.Number == 4);
    Check(host.Eval("return station.get_tick()").Value!.Number == 0);
});
Test("lua load and runtime failures are reported, not thrown", () => {
    var host = LuaWorld("function startup() end function update(dt) error('boom') end");
    var failure = host.CallUpdate(1);
    Check(failure is not null && failure.Traceback.Contains("boom"));
    Check(host.RunCode("function (") is not null, "syntax errors must be reported");
    var missing = new LuaHost(RoverWorld(), _ => { }).RunCode("function update(dt) end");
    Check(missing is not null && missing.Message.Contains("startup"));
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
Test("game session steps ticks, evaluates lua and saves on exit", () => {
    var entry = Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid() + ".lua");
    var savePath = Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid() + ".json");
    File.WriteAllText(entry, """
        function startup() end
        function update(dt) station.get_fleet()[1]:move(vector(1, 0), 2) end
        """);
    var output = new ConcurrentQueue<string>();
    var session = new GameSession(RoverWorld(), entry, savePath, LuaHost.DefaultInstructionBudget, output.Enqueue, startPaused: true);
    session.Start();
    session.EvaluateLine("station.get_object('r'):move(vector(1, 0), 2)");
    session.Step();
    WaitUntil(() => output.Any(line => line.Contains("tick 1")), "first tick output");
    Near(session.World.Find<Rover>("r")!.Position.X, 2);
    session.Quit(); session.Wait(); session.Dispose();
    var loaded = WorldStore.Load(savePath);
    Check(loaded.Find<Rover>("r") is not null, "session must save the world on exit");
});
Test("game session auto-runs at the configured tick rate", () => {
    var entry = Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid() + ".lua");
    var savePath = Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid() + ".json");
    File.WriteAllText(entry, """
        function startup() end
        function update(dt) station.get_fleet()[1]:move(vector(1, 0), 2) end
        """);
    var output = new ConcurrentQueue<string>();
    var session = new GameSession(RoverWorld(), entry, savePath, LuaHost.DefaultInstructionBudget, output.Enqueue, startPaused: false) { TickMilliseconds = 50 };
    session.Start();
    WaitUntil(() => session.World.Tick >= 3, "three auto ticks");
    Check(session.World.Find<Rover>("r")!.Position.X >= 2, "auto ticks must run update and advance");
    session.Quit(); session.Wait(); session.Dispose();
});
Test("game session latches on update errors and recovers via restart", () => {
    var entry = Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid() + ".lua");
    var savePath = Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid() + ".json");
    File.WriteAllText(entry, "function startup() end function update(dt) error('boom') end");
    var output = new ConcurrentQueue<string>();
    var session = new GameSession(RoverWorld(), entry, savePath, LuaHost.DefaultInstructionBudget, output.Enqueue, startPaused: true);
    session.Start();
    session.Step();
    WaitUntil(() => session.Latched, "latch after failed update");
    WaitUntil(() => output.Any(line => line.Contains("boom")), "error traceback");
    session.Resume();
    WaitUntil(() => output.Any(line => line.Contains("restart required")), "resume refused while latched");
    File.WriteAllText(entry, "function startup() end function update(dt) station.get_fleet()[1]:move(vector(1, 0), 1) end");
    session.Restart();
    WaitUntil(() => !session.Latched && output.Any(line => line.Contains("runtime restarted")), "restart with fixed script");
    session.Step();
    WaitUntil(() => session.World.Tick >= 1 && session.World.Find<Rover>("r")!.Position.X >= 1, "tick after restart");
    session.Quit(); session.Wait(); session.Dispose();
});
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

[GameType("test_counter")]
public sealed class TestCounter : GameObject
{
    [Observed("count")] public int Count { get; private set; }
    public int Increment(int amount = 1) => Count += amount;
}
