namespace SpaceAutomation.Game;

// The world is the collection of game objects plus a tick counter. Objects
// enter only through Add (validated and attached); every Advance() runs one
// simulation tick over all of them.
public sealed class World
{
    // Ordinal comparison means ids match by exact characters — never by case
    // or language rules, whatever the computer's culture happens to be.
    private readonly Dictionary<string, GameObject> _objects = new(StringComparer.Ordinal);

    // A read-only view for everyone else: outside code can look objects up,
    // but only Add() can put them in.
    public IReadOnlyDictionary<string, GameObject> Objects => _objects;

    // Completed simulation ticks.
    public long Tick { get; private set; }

    public World(IEnumerable<GameObject> objects, long tick = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        Tick = tick;

        foreach (var obj in objects)
        {
            Add(obj);
        }
    }

    public void Add(GameObject obj)
    {
        if (_objects.ContainsKey(obj.Id))
        {
            throw new InvalidOperationException($"Duplicate object id: {obj.Id}");
        }

        // Validate before attaching: a broken object never enters the world.
        obj.ValidateState();
        obj.Attach(this);
        _objects.Add(obj.Id, obj);
    }

    // Returns the object with this id, or null when there is none.
    public GameObject? Find(string id) => _objects.GetValueOrDefault(id);

    // Re-checks every object. Add validates newcomers; this catches objects
    // whose state went bad later, for example just before a save.
    public void Validate()
    {
        foreach (var obj in _objects.Values)
        {
            obj.ValidateState();
        }
    }

    // One simulation tick, in two phases: first every object declares what
    // it wants this tick (PrepareTick), then every object acts on it (Update).
    // The split lets objects see the whole world's intentions before acting.
    public void Advance()
    {
        // Sort by id so the order is always the same: identical worlds tick
        // identically. The default string sort depends on the system culture,
        // which would quietly break that promise.
        var objects = _objects.Values.OrderBy(obj => obj.Id, StringComparer.Ordinal).ToArray();

        foreach (var obj in objects)
        {
            obj.PrepareTick();
        }

        foreach (var obj in objects)
        {
            obj.Update();
        }

        Tick++;
    }
}
