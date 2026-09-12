namespace SpaceAutomation.Game.Resources;

// One mineral species: pure data, shared by every instance of that mineral —
// the equivalent of a Unity ScriptableObjects or a Godot Resource. The source
// is Data/minerals.json; MineralCatalog loads and validates it once.
public sealed record MineralData(
    string Id,
    string Name,
    string Class,
    float Density,
    string Color,
    float Hardness,
    Dictionary<string, float> Composition)
{
    // How the definition appears in /state. It is only served once the type
    // has been analyzed; nodes and samples hide it until then.
    public Dictionary<string, object?> Observe() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["class"] = Class,
        ["density"] = Density,
        ["color"] = Color,
        ["hardness"] = Hardness,
        ["composition"] = Composition,
    };
}
