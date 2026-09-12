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

    [Saved("_collect")]
    public string? PendingCollect { get; private set; }
    public bool HasPendingCollect => PendingCollect is not null;

    [Observed("capacity", "Cargo slots on board.")]
    public int Capacity { get; set; } = 5;

    [Observed("stored")]
    public List<string> Stored { get; private set; } = [];


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

    // Collecting works like moving: the command only validates and stores the
    // intent — Update() takes the material on the next tick.
    public CommandResult Collect(string id)
    {
        if (PendingCollect is not null)
        {
            return CommandResult.Reject("collect_already_requested");
        }

        GameObject? target = World.Find(id);

        // "is not ICollectable" without a variable is a guard clause: nothing
        // to call, just reject.
        if (target is not ICollectable)
        {
            return CommandResult.Reject("invalid_id");
        }

        if (target.Position != Position)
        {
            return CommandResult.Reject("out_of_reach");
        }

        if (Stored.Count >= Capacity)
        {
            return CommandResult.Reject("inventory_full");
        }

        PendingCollect = id;

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

        if (PendingCollect is not null)
        {
            // "is ICollectable collectable" matches and binds in one step:
            // the object exists, it is collectable, and it is safe to call.
            if (World.Find(PendingCollect) is ICollectable collectable)
            {
                if (collectable.Collect())
                {
                    Stored.Add(PendingCollect);
                }
            }

            PendingCollect = null;
        }
    }

    public override void ValidateState()
    {
        base.ValidateState();

        Rules.Require(Capacity > 0, "Cargo capacity must be positive");
        Rules.Require(Stored.Count <= Capacity, "Cargo exceeds capacity");
        Rules.Require(PendingCollect is null || PendingCollect.Length > 0, "Invalid pending collect");
    }
}
