namespace SpaceAutomation.Game.Vehicles;
using System.Numerics;

[GameType("rover")]
public class Rover : GameObject, IMovable
{
    [Observed("speed_limit", "Speed limit in meters per tick.")]
    public float MaxSpeed { get; set; } = 3;

    [Observed("last_move_result")]
    public string? LastMoveResult { get; private set; }

    [Saved("_movement")]
    public Vector2? PendingMovement { get; private set; }

    public bool HasPendingMovement => PendingMovement is not null;

    public CommandResult Move(Vector2 direction, float speed)
    {
        if (direction == Vector2.Zero)
        {
            return CommandResult.Reject("invalid_direction");
        }

        if (!Rules.Nonnegative(speed))
        {
            return CommandResult.Reject("invalid_speed");
        }

        if (PendingMovement is not null)
        {
            return CommandResult.Reject("movement_already_requested");
        }

        PendingMovement = Vector2.Normalize(direction) * Math.Min(speed, MaxSpeed);

        return CommandResult.Ok();
    }

    public CommandResult Collect(string id){
        if (World.Find(id) is not ICollectable collectable)
        {
            return CommandResult.Reject("invalid_id");
        }

        // To be done in Update !
        // Check available space in rover's inventory
        collectable.Collect();
        // Add it to the rover's inventory

        return CommandResult.Ok();
    }

    public override void Update()
    {
        if (PendingMovement is not null)
        {
            Vector2 movement = PendingMovement.Value;
            Position += movement;
            PendingMovement = null;
        }
    }

    public override void ValidateState()
    {
        base.ValidateState();
    }
}
