using SpaceAutomation.Game.Energy;
namespace SpaceAutomation.Game.Objects;

[GameType("battery")]
public class Battery : EnergyEquipment, IStorage
{
    [Observed("storage", "Battery capacity and charge in joules.")]
    public EnergyStorage Storage { get; set; } = new();
    
    [Observed("charge", "Stored energy in joules.", Persist = false)]
    public double Charge => Storage.Charge;

    [Observed("capacity", "Capacity in joules.", Persist = false)]
    public double Capacity => Storage.Capacity;

    internal override void InitializePort() => Energy.StorageId ??= Id;

    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(Storage is not null, "Storage required");
        Storage!.Validate();
        Rules.Require(Energy.StorageId == Id, "Battery port must reference its own storage");
    }
}
