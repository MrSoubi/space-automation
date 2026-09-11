using System.Collections.ObjectModel;
using SpaceAutomation.Game.Energy;
namespace SpaceAutomation.Game;

public sealed class World
{
    private readonly Dictionary<string, GameObject> _objects = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, GameObject> Objects { get; }
    public long Tick { get; private set; }
    public EnergySystem Energy { get; }

    public World(IEnumerable<GameObject>? objects = null, long tick = 0)
    {
        if (tick < 0){
            throw new ArgumentOutOfRangeException(nameof(tick));
        }
        
        Tick = tick;
        Objects = new ReadOnlyDictionary<string, GameObject>(_objects);
        Energy = new EnergySystem(this);

        foreach (var obj in objects ?? []){
            Add(obj);
        }
    }

    public void Add(GameObject obj)
    {
        if (_objects.ContainsKey(obj.Id)){
            throw new InvalidOperationException($"Duplicate object id: {obj.Id}");
        }

        if (obj is EnergyEquipment equipment){
            equipment.InitializePort();
        }

        obj.ValidateState();
        obj.Attach(this);
        _objects.Add(obj.Id, obj);
    }

    public T? Find<T>(string? id) where T : GameObject => id is not null && _objects.TryGetValue(id, out var obj) ? obj as T : null;
    
    public void Validate() {
        foreach (var obj in _objects.Values){
            obj.ValidateState();
            Energy.Validate();
        }
    }
    
    public void Advance()
    {
        var ordered = _objects.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

        foreach (var obj in ordered){
            obj.PrepareTick();
        }

        Energy.Resolve();

        foreach (var obj in ordered){
            obj.Update();
        }

        Tick++;
    }
}
