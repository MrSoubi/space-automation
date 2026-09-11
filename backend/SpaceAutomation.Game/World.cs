using System.Collections.ObjectModel;
namespace SpaceAutomation.Game;

public sealed class World
{
    private readonly Dictionary<string, GameObject> _objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, GameObject> Objects { get; }
    public long Tick { get; private set; }

    public World(IEnumerable<GameObject>? objects = null, long tick = 0)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        Tick = tick;
        Objects = new ReadOnlyDictionary<string, GameObject>(_objects);

        if (objects is not null)
        {
            foreach (var obj in objects)
            {
                Add(obj);
            }
        }
    }

    public void Add(GameObject obj)
    {
        if (_objects.ContainsKey(obj.Id))
        {
            throw new InvalidOperationException($"Duplicate object id: {obj.Id}");
        }

        obj.ValidateState();
        obj.Attach(this);
        _objects.Add(obj.Id, obj);
    }

    public T? Find<T>(string? id) where T : GameObject
    {
        if (id is null)
        {
            return null;
        }

        if (!_objects.TryGetValue(id, out var obj))
        {
            return null;
        }

        return obj as T;
    }

    public void Validate() {
        foreach (var obj in _objects.Values)
        {
            obj.ValidateState();
        }
    }

    public void Advance()
    {
        var ordered = _objects.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

        foreach (var obj in ordered)
        {
            obj.PrepareTick();
        }

        foreach (var obj in ordered)
        {
            obj.Update();
        }

        Tick++;
    }
}
