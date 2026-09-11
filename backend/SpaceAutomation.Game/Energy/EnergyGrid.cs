namespace SpaceAutomation.Game.Energy;

public sealed record EnergyGrid(string[] Members, double Generation, double Demand, double Charge, double Capacity)
{
    public Dictionary<string, object?> ToDictionary() => new(){
        ["members"] = Members,
        ["generation"] = Generation,
        ["demand"] = Demand,
        ["charge"] = Charge,
        ["capacity"] = Capacity
    };
}
