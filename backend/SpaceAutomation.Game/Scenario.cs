using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;
namespace SpaceAutomation.Game;

public static class Scenario
{
    public static GameObject[] RoverAssembly(string id, Vector2 position = default, double charge = 20)
    {
        var battery = new Battery { Id = $"{id}-battery", Position = position, Storage = new() { Charge = charge }, InstalledIn = id };
        var motor = new Motor { Id = $"{id}-motor", Position = position, InstalledIn = id };
        var panel = new PortableSolarPanel { Id = $"{id}-solar-panel", Position = position, InstalledIn = id };
        var rover = new Rover { Id = id, Position = position, Energy = new() { StorageId = battery.Id, ConsumerId = motor.Id, SourceId = panel.Id } };
        return [rover, battery, motor, panel];
    }
    public static World Create()
    {
        var world = new World([
            .. RoverAssembly("rover-1"), .. RoverAssembly("rover-2", new(5, 0)),
            new Hub { Id = "hub", Energy = new() { Connections = ["hub-battery", "hub-solar-panel"] } },
            new Battery { Id = "hub-battery", Storage = new() { Capacity = 100, Charge = 50 }, Energy = new() { Connections = ["hub"] } },
            new SolarPanel { Id = "hub-solar-panel", Source = new() { ProductionPerTick = 5 }, Energy = new() { Connections = ["hub"] } },
            new Battery { Id = "spare-battery", Storage = new() { Capacity = 40, Charge = 30 } },
            new Motor { Id = "spare-motor", EnergyPerMove = 1 },
            new PortableSolarPanel { Id = "spare-solar-panel", Source = new() { ProductionPerTick = .5 } }
        ]);
        world.Validate(); return world;
    }
}
