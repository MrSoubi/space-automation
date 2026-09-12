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

        if (version == 4)
        {
            Upgrade4To5(state);
            version = 5;
        }

        if (version != 5)
        {
            throw new InvalidDataException("Unsupported save version");
        }
        return state;
    }

    // Version 4 stored mineral nodes as integer units and rover cargo as slot
    // lists of node ids. Version 5 stores litres, cargo sample records and the
    // expedition's analyzed-mineral knowledge. Each old unit becomes one litre
    // and one 1 L sample.
    private static void Upgrade4To5(JsonObject state)
    {
        state["version"] = 5;

        if (state["objects"] is not JsonArray objects)
        {
            return;
        }

        // First pass: mineral nodes. Units become litres, and each node's data
        // key is remembered so old cargo ids can link back to it.
        var dataByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in objects)
        {
            if (item is not JsonObject record ||
                record["type"]?.GetValue<string>() != "mineral" ||
                record["state"] is not JsonObject mineral ||
                mineral["id"] is not JsonValue idNode)
            {
                continue;
            }

            string nodeId = idNode.GetValue<string>();
            if (mineral["data"] is JsonValue dataNode)
            {
                dataByNode[nodeId] = dataNode.GetValue<string>();
            }

            if (mineral["amount"] is JsonNode amount)
            {
                mineral["volume"] = amount.GetValue<int>() * 1.0f;
                mineral.Remove("amount");
            }
        }

        // Second pass: rovers. Slot capacity disappears; each stored node id
        // becomes a 1 L sample record referencing that node.
        foreach (var item in objects)
        {
            if (item is not JsonObject record ||
                record["type"]?.GetValue<string>() != "rover" ||
                record["state"] is not JsonObject rover ||
                rover["id"] is not JsonValue idNode)
            {
                continue;
            }

            rover.Remove("capacity");

            if (rover["stored"] is not JsonArray storedIds)
            {
                continue;
            }

            string roverId = idNode.GetValue<string>();
            var samples = new JsonArray();
            int count = 0;
            foreach (var entry in storedIds)
            {
                if (entry is null)
                {
                    continue;
                }

                count++;
                string nodeId = entry.GetValue<string>();
                samples.Add(new JsonObject
                {
                    ["$type"] = "MineralSample",
                    ["fields"] = new JsonObject
                    {
                        ["id"] = $"sample-{roverId}-{count}",
                        ["source"] = nodeId,
                        ["data"] = dataByNode.GetValueOrDefault(nodeId, ""),
                        ["volume"] = 1.0,
                    },
                });
            }

            rover["stored"] = samples;
            rover["_sample_seq"] = count;
        }
    }
}
