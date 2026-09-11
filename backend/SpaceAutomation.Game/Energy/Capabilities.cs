namespace SpaceAutomation.Game.Energy;

[ValueType]
public sealed class EnergyStorage
{
    [Observed("capacity")]
    public double Capacity { get; set; } = 20;

    [Observed("charge")]
    public double Charge { get; set; } = 20;

    public void Validate() => Rules.Require(Rules.Nonnegative(Capacity) && Rules.Nonnegative(Charge) && Charge <= Capacity, "Invalid battery charge or capacity");
}

[ValueType]
public sealed class EnergySource
{
    [Observed("production_per_tick")]
    public double ProductionPerTick { get; set; } = 1;

    [Observed("energy_generated")]
    public double EnergyGenerated { get; set; }

    public void Validate() => Rules.Require(Rules.Nonnegative(ProductionPerTick) && Rules.Nonnegative(EnergyGenerated), "Invalid generation values");
}

[ValueType]
public sealed class EnergyConsumer
{
    [Observed("demand")]
    public double Demand { get; set; }

    [Observed("received")]
    public double Received { get; set; }

    public void Validate() => Rules.Require(Rules.Nonnegative(Demand) && Rules.Nonnegative(Received), "Invalid consumption values");
}

// Capabilities let the grid handle new equipment without checking its class name.
public interface IProducer {
    EnergySource Source { get; }
    bool Enabled { get; }
}

public interface IConsumer {
    EnergyConsumer Consumer { get; }
}

public interface IStorage {
    EnergyStorage Storage { get; }
}

// Grid participation is a contract, not a base class: any game object with a port
// can join the energy system (rovers, hubs, installable equipment alike).
public interface IEnergyNode
{
    string Id { get; }

    EnergyPort Energy { get; }

    CommandResult Connect(string other_id);

    CommandResult Disconnect(string other_id);

    Dictionary<string, object?> GridStatus();
}

// Mobile nodes cannot tether to a grid while a move is pending.
public interface IMobile {
    bool HasPendingMovement { get; }
}

// Hosts carry installed components; their wiring is checked once the whole world exists.
public interface IComponentHost {
    void ValidateComponents();
}
