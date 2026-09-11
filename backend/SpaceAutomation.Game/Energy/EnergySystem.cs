namespace SpaceAutomation.Game.Energy;

public sealed class EnergySystem(World world)
{
    // Any object implementing IEnergyNode joins the grid, whatever class it is.
    private Dictionary<string, IEnergyNode> Nodes => world.Objects.Values.OfType<IEnergyNode>().ToDictionary(x => x.Id, StringComparer.Ordinal);
    public CommandResult Connect(string firstId, string otherId)
    {
        var nodes = Nodes;
        if (!nodes.TryGetValue(firstId, out var first) || !nodes.TryGetValue(otherId, out var other)) return CommandResult.Reject("invalid_energy_endpoint");
        if (firstId == otherId) return CommandResult.Reject("self_connection");
        if (first is EnergyEquipment { InstalledIn: not null } || other is EnergyEquipment { InstalledIn: not null }) return CommandResult.Reject("connect_through_host");
        if (first is IMobile { HasPendingMovement: true } || other is IMobile { HasPendingMovement: true }) return CommandResult.Reject("movement_pending");
        if (!first.Energy.Connections.Contains(otherId)) { first.Energy.Connections.Add(otherId); other.Energy.Connections.Add(firstId); }
        return CommandResult.Ok();
    }
    public CommandResult Disconnect(string firstId, string otherId)
    {
        var nodes = Nodes;
        if (!nodes.TryGetValue(firstId, out var first) || !nodes.TryGetValue(otherId, out var other)) return CommandResult.Reject("invalid_energy_endpoint");
        if (!first.Energy.Connections.Remove(otherId)) return CommandResult.Reject("not_connected");
        other.Energy.Connections.Remove(firstId);
        return CommandResult.Ok();
    }
    public void Validate()
    {
        var nodes = Nodes;
        foreach (var obj in nodes.Values)
        {
            ((GameObject)obj).ValidateState(); // every energy node is a game object
            Rules.Require(obj is not EnergyEquipment { InstalledIn: not null } || obj.Energy.Connections.Count == 0, "Installed parts connect through their host");
            foreach (var neighbor in obj.Energy.Connections)
                Rules.Require(neighbor != obj.Id && nodes.TryGetValue(neighbor, out var other) && other.Energy.Connections.Contains(obj.Id), "Energy links must be reciprocal");
            foreach (var (id, capability) in new[] { (obj.Energy.SourceId, typeof(IProducer)), (obj.Energy.ConsumerId, typeof(IConsumer)), (obj.Energy.StorageId, typeof(IStorage)) })
            {
                if (id is null) continue;
                Rules.Require(nodes.TryGetValue(id, out var target) && capability.IsInstanceOfType(target), "Missing energy capability");
                Rules.Require(id == obj.Id || (target is EnergyEquipment { InstalledIn: var owner } && owner == obj.Id), "Component ownership mismatch");
            }
            if (obj is EnergyEquipment { InstalledIn: { } hostId })
            {
                Rules.Require(nodes.TryGetValue(hostId, out var host) && host is not EnergyEquipment { InstalledIn: not null }, "Missing independent host");
                var port = nodes[hostId].Energy;
                Rules.Require(new[] { port.SourceId, port.ConsumerId, port.StorageId }.Contains(obj.Id), "Host does not reference component");
            }
            if (obj is IComponentHost componentHost) componentHost.ValidateComponents();
        }
    }
    private IEnumerable<IEnergyNode[]> Groups()
    {
        var nodes = Nodes;
        var edges = nodes.ToDictionary(x => x.Key, x => x.Value.Energy.Connections.ToHashSet(StringComparer.Ordinal));
        foreach (var obj in nodes.Values)
            if (obj is EnergyEquipment { InstalledIn: { } host }) { edges[obj.Id].Add(host); edges[host].Add(obj.Id); }
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in edges.Keys.Order(StringComparer.Ordinal))
        {
            if (visited.Contains(root)) continue;
            var todo = new Stack<string>(); todo.Push(root);
            var members = new List<IEnergyNode>();
            while (todo.TryPop(out var id))
            {
                if (!visited.Add(id)) continue;
                members.Add(nodes[id]);
                foreach (var next in edges[id]) if (!visited.Contains(next)) todo.Push(next);
            }
            yield return members.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        }
    }
    public EnergyGrid GridFor(string id)
    {
        var group = Groups().First(x => x.Any(obj => obj.Id == id));
        return new(group.Select(x => x.Id).ToArray(), group.OfType<IProducer>().Where(x => x.Enabled).Sum(x => x.Source.ProductionPerTick),
            group.OfType<IConsumer>().Sum(x => x.Consumer.Demand), group.OfType<IStorage>().Sum(x => x.Storage.Charge), group.OfType<IStorage>().Sum(x => x.Storage.Capacity));
    }
    public void Resolve()
    {
        Validate();
        foreach (var group in Groups())
        {
            var generated = 0.0;
            foreach (var obj in group.OfType<IProducer>())
            {
                var amount = obj.Enabled ? obj.Source.ProductionPerTick : 0;
                obj.Source.EnergyGenerated += amount; generated += amount;
            }
            var batteries = group.OfType<IStorage>().ToArray();
            var available = generated + batteries.Sum(x => x.Storage.Charge);
            var used = 0.0;
            foreach (var obj in group.OfType<IConsumer>())
            {
                obj.Consumer.Received = 0;
                if (obj.Consumer.Demand <= available)
                { obj.Consumer.Received = obj.Consumer.Demand; used += obj.Consumer.Demand; available -= obj.Consumer.Demand; }
            }
            var deficit = Math.Max(0, used - generated);
            foreach (var obj in batteries) { var drawn = Math.Min(obj.Storage.Charge, deficit); obj.Storage.Charge -= drawn; deficit -= drawn; }
            var surplus = Math.Max(0, generated - used);
            foreach (var obj in batteries) { var stored = Math.Min(obj.Storage.Capacity - obj.Storage.Charge, surplus); obj.Storage.Charge += stored; surplus -= stored; }
        }
    }
}
