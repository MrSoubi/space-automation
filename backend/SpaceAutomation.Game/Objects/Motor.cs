using SpaceAutomation.Game.Energy;
namespace SpaceAutomation.Game.Objects;

[GameType("motor")]
public class Motor : EnergyEquipment, IConsumer
{
    [Observed("consumer")]
    public EnergyConsumer Consumer { get; set; } = new();

    [Observed("max_speed", "Maximum speed in meters per tick.")]
    public double MaxSpeed { get; set; } = 3;

    [Observed("energy_per_move", "Joules per nonzero movement tick.")]
    public double EnergyPerMove { get; set; } = 2;

    internal override void InitializePort() => Energy.ConsumerId ??= Id;
    
    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(Consumer is not null, "Consumer required");
        Consumer!.Validate();
        Rules.Require(Rules.Nonnegative(MaxSpeed) && MaxSpeed > 0 && Rules.Nonnegative(EnergyPerMove), "Invalid motor capabilities");
        Rules.Require(Energy.ConsumerId == Id, "Motor port must reference itself");
    }
}
