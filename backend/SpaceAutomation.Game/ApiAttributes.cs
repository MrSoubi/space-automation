namespace SpaceAutomation.Game;

// The save system reads these declarations; gameplay classes never deal with JSON.
[AttributeUsage(AttributeTargets.Class)]
public sealed class GameTypeAttribute(string name) : Attribute { public string Name { get; } = name; }
[AttributeUsage(AttributeTargets.Class)]
public sealed class ValueTypeAttribute : Attribute;
[AttributeUsage(AttributeTargets.Property)]
public sealed class ObservedAttribute(string name, string description = "") : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public bool Persist { get; init; } = true;
}
[AttributeUsage(AttributeTargets.Property)]
public sealed class SavedAttribute(string name) : Attribute { public string Name { get; } = name; }
