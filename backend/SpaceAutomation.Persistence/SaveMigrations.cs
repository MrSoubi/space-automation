using System.Text.Json.Nodes;
namespace SpaceAutomation.Persistence;

public static class SaveMigrations
{
    public static JsonObject Upgrade(JsonObject input)
    {
        var state = (JsonObject)input.DeepClone();
        var version = state["version"]!.GetValue<int>();
        if (version == 3)
        {
            // Rovers became hosts rather than installable equipment: drop their
            // always-null installed_in field, which the type no longer declares.
            foreach (var record in state["objects"]!.AsArray())
            {
                if (record!["type"]!.GetValue<string>() != "rover") continue;
                var fields = record["state"]!.AsObject();
                if (fields["installed_in"] is not null) throw new InvalidDataException("Rovers are hosts, not installed components");
                fields.Remove("installed_in");
            }
            state["version"] = 4; version = 4;
        }
        if (version != 4) throw new InvalidDataException("Unsupported save version");
        return state;
    }
}
