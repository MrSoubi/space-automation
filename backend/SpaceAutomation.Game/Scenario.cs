using SpaceAutomation.Game.Objects;
namespace SpaceAutomation.Game;

public static class Scenario
{
    public static World Create()
    {
        GameObject[] objects =
        {
            new Rover { Id = "rover-1", Position = Vector2.Zero },
            new Hub { Id = "hub"}
        };
        var world = new World(objects);

        world.Validate();
        return world;
    }
}
