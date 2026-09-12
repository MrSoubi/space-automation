namespace SpaceAutomation.Game.Resources;

using System.Text.Json;

// The mineral asset database, like Godot's ResourceLoader: one static load of
// Data/minerals.json, then lookups by stable id. Mineral instances reference
// an id, never a copy of the data — editing the file retunes every mineral.
public static class MineralCatalog
{
    private static readonly Dictionary<string, MineralData> _all = Load();

    public static IReadOnlyCollection<MineralData> All => _all.Values;

    // Returns the data for this id, or throws when there is none.
    public static MineralData Get(string id)
    {
        if (!_all.TryGetValue(id, out MineralData? data))
        {
            throw new KeyNotFoundException($"Unknown mineral data: {id}");
        }

        return data;
    }

    public static bool Contains(string id) => _all.ContainsKey(id);

    private static Dictionary<string, MineralData> Load()
    {
        // Relative to the build output, not the current directory: the server
        // is launched from wherever the player happens to be.
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "minerals.json");
        try
        {
            // The file uses lowercase keys; the records use C# casing, so the
            // lookup must ignore case.
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<MineralFile>(File.ReadAllText(path), options);
            if (file?.Minerals is null || file.Minerals.Count == 0)
            {
                throw new InvalidDataException("minerals.json must define at least one mineral");
            }

            var all = new Dictionary<string, MineralData>(StringComparer.Ordinal);
            foreach (MineralData mineral in file.Minerals)
            {
                Validate(mineral);

                if (!all.TryAdd(mineral.Id, mineral))
                {
                    throw new InvalidDataException($"Duplicate mineral id: {mineral.Id}");
                }
            }

            return all;
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            throw new InvalidDataException($"Cannot load mineral data: {path}", e);
        }
    }

    private static void Validate(MineralData mineral)
    {
        Rules.Require(!string.IsNullOrWhiteSpace(mineral.Id), "Mineral needs an id");
        Rules.Require(!string.IsNullOrWhiteSpace(mineral.Name), $"Mineral {mineral.Id} needs a name");
        Rules.Require(!string.IsNullOrWhiteSpace(mineral.Class), $"Mineral {mineral.Id} needs a class");
        Rules.Require(mineral.Density > 0, $"Mineral {mineral.Id} needs a positive density");
        Rules.Require(mineral.Hardness > 0, $"Mineral {mineral.Id} needs a positive hardness");

        if (mineral.Composition is null || mineral.Composition.Count == 0)
        {
            throw new InvalidDataException($"Mineral {mineral.Id} needs a composition");
        }

        foreach (var (element, share) in mineral.Composition)
        {
            Rules.Require(share > 0, $"Mineral {mineral.Id}: element {element} needs a positive share");
        }

        // Composition is weight percent, so it must sum to (almost) exactly 100.
        float total = mineral.Composition.Values.Sum();
        Rules.Require(Math.Abs(total - 100f) <= 0.5f, $"Mineral {mineral.Id}: composition sums to {total}, expected 100");
    }

    private sealed record MineralFile(List<MineralData> Minerals);
}
