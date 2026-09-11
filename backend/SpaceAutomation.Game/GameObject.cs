namespace SpaceAutomation.Game;

public abstract class GameObject
{
    private World? _world;

    [Observed("id", "Stable object identifier.")]
    public string Id { get; init; } = "";

    [Observed("position", "Position in meters.")]
    public Vector2 Position { get; set; }

    public World World => _world ?? throw new InvalidOperationException("Add this object to a World first");

    internal void Attach(World world)
    {
        if (_world is not null && !ReferenceEquals(_world, world)){
            throw new InvalidOperationException("Object already belongs to another world");
        }
        
        _world = world;
    }

    public virtual void PrepareTick() { }

    public virtual void Update() { }

    public virtual void ValidateState()
    {
        Rules.Require(!string.IsNullOrWhiteSpace(Id), "Object needs an id");
        Rules.Require(Position.IsFinite, "Position must be finite");
    }
}
