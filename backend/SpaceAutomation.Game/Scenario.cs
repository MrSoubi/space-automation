using SpaceAutomation.Game.Items;
using SpaceAutomation.Game.Resources;
using SpaceAutomation.Game.Vehicles;
using System.Numerics;

namespace SpaceAutomation.Game;

public static class Scenario
{
    public static World Create()
    {
        List<GameObject> objects = [];

        // List.Add puts the object into the list; List.Append (LINQ) returns
        // a new sequence instead and silently leaves the list empty.
        objects.Add(new Rover { Id = "rover-1", Position = Vector2.Zero });
        objects.Add(new SurveyScanner { Id = "scanner-1", Position = new Vector2(2, 3) });
        objects.Add(new Mineral { Id = "mineral-1", Position = new Vector2(2, 5) });
        objects.Add(new Mineral { Id = "mineral-2", Position = new Vector2(6, 10) });

        var world = new World(objects);

        world.Validate();
        return world;
    }
}
