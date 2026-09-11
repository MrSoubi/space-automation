using SpaceAutomation.Game.Energy;
namespace SpaceAutomation.Game.Objects;

[GameType("solar_panel")]
public class SolarPanel : EnergyEquipment, IProducer
{
    [Observed("enabled")]
    public bool Enabled { get; set; } = true;

    [Observed("source")]
    public EnergySource Source { get; set; } = new();

    [Observed("energy_per_tick", Persist = false)]
    public double EnergyPerTick => Source.ProductionPerTick;

    [Observed("energy_generated", Persist = false)]
    public double EnergyGenerated => Source.EnergyGenerated;

    public CommandResult SetEnabled(bool enabled){
        Enabled = enabled; return CommandResult.Ok();
    }

    internal override void InitializePort() => Energy.SourceId ??= Id;

    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(Source is not null, "Source required");
        Source!.Validate();
        Rules.Require(Energy.SourceId == Id, "Panel port must reference itself");
    }
}

[GameType("portable_solar_panel")]
public class PortableSolarPanel : SolarPanel
{
    public PortableSolarPanel() {
        Source.ProductionPerTick = 0.2;
    }
}
