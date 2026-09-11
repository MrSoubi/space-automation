using System.Text.Json.Nodes;
namespace SpaceAutomation.Persistence;

public static class SaveMigrations
{
    public static JsonObject Upgrade(JsonObject input)
    {
        var state = (JsonObject)input.DeepClone();
        var versionNode = state["version"];
        if (versionNode is null)
        {
            throw new InvalidDataException("Save is missing version");
        }

        int version = versionNode.GetValue<int>();

        if (version != 4)
        {
            throw new InvalidDataException("Unsupported save version");
        }
        return state;
    }
}
