namespace SpaceAutomation.Game;
using System.Numerics;

public abstract class GameObject
{
    private World? _world;

    [Observed("id", "Stable object identifier.")]
    public string Id { get; init; } = "";

    [Observed("position", "Position in meters.")]
    public Vector2 Position { get; set; }

    public World World
    {
        get
        {
            if (_world is null)
            {
                throw new InvalidOperationException("Add this object to a World first");
            }

            return _world;
        }
    }

    internal void Attach(World world)
    {
        if (_world is not null && !ReferenceEquals(_world, world))
        {
            throw new InvalidOperationException("Object already belongs to another world");
        }

        _world = world;
    }

    public virtual void PrepareTick() { }

    public virtual void Update() { }

    // Whether this object appears in the player-facing state. Most objects do;
    // an undiscovered mineral, for example, stays hidden until a scan finds it.
    public virtual bool IsPlayerVisible => true;

    public virtual void ValidateState()
    {
        Rules.Require(!string.IsNullOrWhiteSpace(Id), "Object needs an id");
    }
}
