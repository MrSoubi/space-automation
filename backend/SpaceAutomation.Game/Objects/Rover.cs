using SpaceAutomation.Game;
namespace SpaceAutomation.Game.Objects;

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

    public CommandResult Move(Vector2 direction,
                              float speed)
    {
        if (!direction.IsFinite || direction == Vector2.Zero)
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

        PendingMovement = direction.Normalized() * Math.Min(speed, MaxSpeed);

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
