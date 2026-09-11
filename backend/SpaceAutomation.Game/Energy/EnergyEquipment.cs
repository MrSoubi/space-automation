namespace SpaceAutomation.Game.Energy;

public abstract class EnergyEquipment : GameObject, IEnergyNode
{
    [Observed("energy", "Port connections and installed component IDs.")]
    public EnergyPort Energy { get; set; } = new();
    [Observed("installed_in", "Host ID, or None for independent equipment.")]
    public string? InstalledIn { get; set; }
    public CommandResult Connect(string other_id) => World.Energy.Connect(Id, other_id);
    public CommandResult Disconnect(string other_id) => World.Energy.Disconnect(Id, other_id);
    public Dictionary<string, object?> GridStatus() => World.Energy.GridFor(Id).ToDictionary();
    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(Energy is not null, "Energy port required");
        Energy!.Validate();
        Rules.Require(InstalledIn is null || InstalledIn.Length > 0, "Invalid installed_in");
    }
    internal virtual void InitializePort() { }
}
