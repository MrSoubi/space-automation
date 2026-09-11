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
            foreach (var property in Model.Saved(obj.GetType()))
            {
                string name = Model.Name(property);
                object? value = property.GetValue(obj);
                state[name] = ValueCodec.Encode(value);
            }

            objects.Add(new JsonObject { ["type"] = Model.ObjectKey(obj), ["state"] = state });
        }

        return new JsonObject { ["version"] = 4, ["tick"] = world.Tick, ["objects"] = objects };
    }

    public static World Deserialize(JsonObject input)
    {
        var state = SaveMigrations.Upgrade(input);
        var tickNode = state["tick"];
        if (tickNode is null)
        {
            throw new InvalidDataException("Save is missing tick");
        }

        long tick = tickNode.GetValue<long>();
        var objects = state["objects"] as JsonArray;
        if (objects is null)
        {
            throw new InvalidDataException("Save needs an objects array");
        }

        var world = new World([], tick: tick);
        foreach (var item in objects)
        {
            var record = item as JsonObject;
            if (record is null)
            {
                throw new InvalidDataException("Invalid saved object record");
            }

            var typeNode = record["type"];
            if (typeNode is null)
            {
                throw new InvalidDataException("Saved object is missing type");
            }

            string typeName = typeNode.GetValue<string>();
            if (!Model.Objects.TryGetValue(typeName, out var type))
            {
                throw new InvalidDataException($"Unknown saved object type: {typeName}");
            }

            var fields = record["state"] as JsonObject;
            if (fields is null)
            {
                throw new InvalidDataException($"Saved object is missing state: {typeName}");
            }

            var obj = Activator.CreateInstance(type) as GameObject;
            if (obj is null)
            {
                throw new InvalidDataException($"Cannot create game object: {typeName}");
            }

            ValueCodec.RestoreProperties(obj, fields, Model.Saved(type));
            world.Add(obj);
        }

        world.Validate();
        return world;
    }

    public static World Load(string path)
    {
        if (!File.Exists(path))
        {
            return Scenario.Create();
        }

        try
        {
            string json = File.ReadAllText(path);
            var state = JsonNode.Parse(json) as JsonObject;
            if (state is null)
            {
                throw new InvalidDataException("Save must contain a JSON object");
            }

            return Deserialize(state);
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"Invalid or unsupported save file: {path}", e);
        }
    }

    public static void Save(World world, string path)
    {
        var state = Serialize(world);
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = state.ToJsonString(options);

        path = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            throw new InvalidDataException($"Save path needs a parent directory: {path}");
        }

        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
