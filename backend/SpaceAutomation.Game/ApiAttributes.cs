namespace SpaceAutomation.Game;

// The save system reads these declarations; gameplay classes never deal with JSON.
[AttributeUsage(AttributeTargets.Class)]
public sealed class GameTypeAttribute : Attribute
{
    public string Name { get; }

    public GameTypeAttribute(string name)
    {
        Name = name;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ValueTypeAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ObservedAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public bool Persist { get; init; } = true;

    public ObservedAttribute(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SavedAttribute : Attribute
{
    public string Name { get; }

    public SavedAttribute(string name)
    {
        Name = name;
    }
}
