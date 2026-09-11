using MoonSharp.Interpreter;
using SpaceAutomation.Game;
using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;
using SpaceAutomation.Persistence;

namespace SpaceAutomation.Host;

// Hand-written Lua API over the live simulation. One process, direct calls:
// no sockets, no schema export, no generated proxies. Proxies are stable Lua
// tables (cached per object id) with a metatable so all data is read live.
public sealed class LuaApi(Script script, World world)
{
    private readonly Dictionary<string, Table> _proxies = new(StringComparer.Ordinal);
    public Table Station { get; } = new(script);

    public void Install()
    {
        script.Globals.Set("station", DynValue.NewTable(Station));
        script.Globals.Set("vector", Method("vector", args =>
        {
            var x = ReadNumber(args[0]); var y = ReadNumber(args[1]);
            if (x is null || y is null) throw new ScriptRuntimeException("vector(x, y) requires two numbers");
            return Vector(new Vector2(x.Value, y.Value));
        }));
        Station.Set("get_tick", Method("get_tick", _ => DynValue.NewNumber(world.Tick)));
        Station.Set("get_fleet", Method("get_fleet", _ => DynValue.NewTable(Array(
            world.Objects.Values.OfType<Rover>().OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => DynValue.NewTable(Proxy(x.Id)))))));
        Station.Set("get_object", Method("get_object", args =>
        {
            var id = ArgString(args, 0);
            return id is not null && world.Find<GameObject>(id) is not null ? DynValue.NewTable(Proxy(id)) : DynValue.Nil;
        }));
        Station.Set("get_objects", Method("get_objects", args =>
        {
            var filter = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : null;
            return DynValue.NewTable(Array(world.Objects.Values.OrderBy(x => x.Id, StringComparer.Ordinal)
                .Where(x => filter is null || Model.ObjectKey(x) == filter).Select(x => DynValue.NewTable(Proxy(x.Id)))));
        }));
    }

    public Table Proxy(string id)
    {
        if (_proxies.TryGetValue(id, out var cached)) return cached;
        var proxy = new Table(script);
        var meta = new Table(script);
        meta.Set("__index", Method("__index", args => Index(id, args[1])));
        proxy.MetaTable = meta;
        _proxies[id] = proxy;
        return proxy;
    }

    // One dispatch point per proxy: shared members first, then type-specific ones.
    private DynValue Index(string id, DynValue key)
    {
        if (key.Type != DataType.String || world.Find<GameObject>(id) is not { } obj) return DynValue.Nil;
        switch (key.String)
        {
            case "id": return DynValue.NewString(obj.Id);
            case "type": return DynValue.NewString(Model.ObjectKey(obj));
            case "position": return Vector(obj.Position);
        }
        if (obj is IEnergyNode node && key.String == "energy") return Port(node.Energy);
        if (obj is EnergyEquipment equipment) switch (key.String)
        {
            case "installed_in": return StringOrNil(equipment.InstalledIn);
            case "connect": return Method("connect", args =>
            {
                var other = ArgString(args, 1);
                return Result(other is null ? CommandResult.Reject("invalid_energy_endpoint") : equipment.Connect(other));
            });
            case "disconnect": return Method("disconnect", args =>
            {
                var other = ArgString(args, 1);
                return Result(other is null ? CommandResult.Reject("invalid_energy_endpoint") : equipment.Disconnect(other));
            });
            case "grid_status": return Method("grid_status", _ => GridStatus(equipment));
        }
        if (obj is Rover rover) switch (key.String)
        {
            case "speed_limit": return DynValue.NewNumber(rover.SpeedLimit);
            case "max_speed": return DynValue.NewNumber(rover.MaxSpeed);
            case "last_move_result": return StringOrNil(rover.LastMoveResult);
            case "battery": return ProxyOrNil(rover.Battery);
            case "motor": return ProxyOrNil(rover.Motor);
            case "solar_panel": return ProxyOrNil(rover.SolarPanel);
            case "move": return Method("move", args =>
            {
                var direction = ReadVector(args[1]);
                var speed = ReadNumber(args[2]);
                if (direction is null) return Result(CommandResult.Reject("invalid_direction"));
                if (speed is null) return Result(CommandResult.Reject("invalid_speed"));
                return Result(rover.Move(direction.Value, speed.Value));
            });
            case "replace_component": return Method("replace_component", args =>
            {
                var slot = ArgString(args, 1);
                if (slot is null) return Result(CommandResult.Reject("invalid_slot"));
                var component = args[2];
                var componentId = component.Type is DataType.Nil or DataType.Void ? null : ArgString(args, 2);
                if (componentId is null && component.Type is not (DataType.Nil or DataType.Void)) return Result(CommandResult.Reject("invalid_component"));
                return Result(rover.ReplaceComponent(slot, componentId));
            });
        }
        if (obj is Battery battery) switch (key.String)
        {
            case "charge": return DynValue.NewNumber(battery.Charge);
            case "capacity": return DynValue.NewNumber(battery.Capacity);
        }
        if (obj is Motor motor) switch (key.String)
        {
            case "max_speed": return DynValue.NewNumber(motor.MaxSpeed);
            case "energy_per_move": return DynValue.NewNumber(motor.EnergyPerMove);
        }
        if (obj is SolarPanel panel) switch (key.String)
        {
            case "enabled": return DynValue.NewBoolean(panel.Enabled);
            case "energy_per_tick": return DynValue.NewNumber(panel.EnergyPerTick);
            case "energy_generated": return DynValue.NewNumber(panel.EnergyGenerated);
            case "set_enabled": return Method("set_enabled", args =>
            {
                if (args[1].Type != DataType.Boolean) return Result(CommandResult.Reject("invalid_enabled"));
                return Result(panel.SetEnabled(args[1].Boolean));
            });
        }
        return DynValue.Nil;
    }

    private DynValue Method(string name, Func<CallbackArguments, DynValue> body) =>
        DynValue.NewCallback(new CallbackFunction((context, args) => body(args), name));

    private DynValue Result(CommandResult result)
    {
        var table = new Table(script);
        table.Set("accepted", DynValue.NewBoolean(result.Accepted));
        if (!result.Accepted) table.Set("reason", StringOrNil(result.Reason));
        return DynValue.NewTable(table);
    }

    private DynValue GridStatus(EnergyEquipment equipment)
    {
        var table = new Table(script);
        foreach (var (name, value) in equipment.GridStatus())
            table.Set(name, value switch
            {
                string[] members => DynValue.NewTable(Array(members.Select(DynValue.NewString))),
                double number => DynValue.NewNumber(number),
                _ => DynValue.Nil,
            });
        return DynValue.NewTable(table);
    }

    private DynValue Port(EnergyPort port)
    {
        var table = new Table(script);
        table.Set("connections", DynValue.NewTable(Array(port.Connections.Select(DynValue.NewString))));
        table.Set("source_id", StringOrNil(port.SourceId));
        table.Set("consumer_id", StringOrNil(port.ConsumerId));
        table.Set("storage_id", StringOrNil(port.StorageId));
        return DynValue.NewTable(table);
    }

    private DynValue Vector(Vector2 v)
    {
        var table = new Table(script);
        table.Set("x", DynValue.NewNumber(v.X));
        table.Set("y", DynValue.NewNumber(v.Y));
        return DynValue.NewTable(table);
    }

    private Table Array(IEnumerable<DynValue> items)
    {
        var table = new Table(script);
        var index = 1;
        foreach (var item in items) table.Set(DynValue.NewNumber(index++), item);
        return table;
    }

    private DynValue ProxyOrNil(GameObject? obj) => obj is null ? DynValue.Nil : DynValue.NewTable(Proxy(obj.Id));
    private DynValue StringOrNil(string? text) => text is null ? DynValue.Nil : DynValue.NewString(text);

    private static string? ArgString(CallbackArguments args, int index) =>
        args.Count > index && args[index].Type == DataType.String ? args[index].String : null;

    private static double? ReadNumber(DynValue value) =>
        value.Type == DataType.Number ? value.Number : null;

    private static Vector2? ReadVector(DynValue value)
    {
        if (value.Type != DataType.Table) return null;
        var x = value.Table.Get("x"); var y = value.Table.Get("y");
        if (x.Type != DataType.Number || y.Type != DataType.Number) return null;
        return new Vector2(x.Number, y.Number);
    }
}
