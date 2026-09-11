namespace SpaceAutomation.Game.Energy;

[ValueType]
public sealed class EnergyPort
{
    [Observed("connections", "IDs of externally connected equipment.")]
    public List<string> Connections { get; set; } = [];
    [Observed("source_id")] public string? SourceId { get; set; }
    [Observed("consumer_id")] public string? ConsumerId { get; set; }
    [Observed("storage_id")] public string? StorageId { get; set; }
    public void Validate()
    {
        Rules.Require(Connections is not null && Connections.All(x => !string.IsNullOrEmpty(x)) && Connections.Distinct().Count() == Connections.Count,
            "Connections must be unique object IDs");
        foreach (var id in new[] { SourceId, ConsumerId, StorageId })
            Rules.Require(id is null || id.Length > 0, "Invalid component reference");
    }
}
