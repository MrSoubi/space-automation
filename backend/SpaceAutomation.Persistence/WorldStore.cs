using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceAutomation.Game;
namespace SpaceAutomation.Persistence;

public static class WorldStore
{
    public static JsonObject Serialize(World world)
    {
        world.Validate();
        var objects = new JsonArray();
        foreach (var obj in world.Objects.Values)
        {
            var state = new JsonObject();
            foreach (var p in Model.Saved(obj.GetType())) state[Model.Name(p)] = ValueCodec.Encode(p.GetValue(obj));
            objects.Add(new JsonObject { ["type"] = Model.ObjectKey(obj), ["state"] = state });
        }
        return new JsonObject { ["version"] = 4, ["tick"] = world.Tick, ["objects"] = objects };
    }
    public static World Deserialize(JsonObject input)
    {
        var state = SaveMigrations.Upgrade(input);
        var tick = state["tick"]!.GetValue<long>();
        var world = new World(tick: tick);
        foreach (var item in state["objects"]!.AsArray())
        {
            var type = Model.Objects[item!["type"]!.GetValue<string>()];
            var obj = (GameObject)Activator.CreateInstance(type)!;
            ValueCodec.RestoreProperties(obj, item["state"]!.AsObject(), Model.Saved(type));
            world.Add(obj);
        }
        world.Validate(); return world;
    }
    public static World Load(string path)
    {
        if (!File.Exists(path)) return Scenario.Create();
        try { return Deserialize(JsonNode.Parse(File.ReadAllText(path))!.AsObject()); }
        catch (Exception e) { throw new InvalidDataException($"Invalid or unsupported save file: {path}", e); }
    }
    public static void Save(World world, string path)
    {
        var json = Serialize(world).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            { var bytes = System.Text.Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
