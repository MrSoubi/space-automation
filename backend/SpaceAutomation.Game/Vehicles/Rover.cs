namespace SpaceAutomation.Game.Vehicles;

using System.Numerics;
using SpaceAutomation.Game.Buildings;
using SpaceAutomation.Game.Resources;

[GameType("rover")]
public class Rover : GameObject, IMovable
{
    // Standing "on" something means within arm's reach: exact equality would
    // break on the floating-point rounding a move leaves behind.
    private const float Reach = 0.1f;

    [Observed("speed_limit", "Speed limit in meters per tick.")]
    public float MaxSpeed { get; set; } = 3;

    [Observed("last_move_result")]
    public string? LastMoveResult { get; private set; }

    [Observed("last_collect_result", "Outcome of the most recent collection attempt.")]
    public string? LastCollectResult { get; private set; }

    [Observed("last_deliver_result", "Outcome of the most recent delivery attempt.")]
    public string? LastDeliverResult { get; private set; }

    [Saved("_movement")]
    public Vector2? PendingMovement { get; private set; }
    public bool HasPendingMovement => PendingMovement is not null;

    [Saved("_collect")]
    public string? PendingCollect { get; private set; }
    public bool HasPendingCollect => PendingCollect is not null;

    [Saved("_deliver")]
    public string? PendingDeliverSample { get; private set; }

    [Saved("_deliver_to")]
    public string? PendingDeliverTarget { get; private set; }
    public bool HasPendingDeliver => PendingDeliverSample is not null;

    [Observed("max_volume", "Cargo volume limit, in litres.")]
    public float MaxVolume { get; set; } = 5;

    [Observed("max_weight", "Cargo weight limit, in kilograms.")]
    public float MaxWeight { get; set; } = 10;

    [Observed("collection_volume", "Litres taken from a site per collection.")]
    public float CollectionVolume { get; set; } = 1;

    // The cargo bay: samples with a volume and a weight, not slots. Saved as
    // sample records; projected manually in AdjustObservation so unanalyzed
    // minerals stay hidden.
    [Saved("stored")]
    public List<MineralSample> Stored { get; private set; } = [];

    // Volume and weight in use right now. The weight is what the rover's
    // scale reports: each sample's volume times its density.
    [Observed("used_volume", "Cargo volume in use, in litres.", Persist = false)]
    public float UsedVolume => Stored.Sum(sample => sample.Volume);

    [Observed("used_weight", "Cargo weight in kilograms.", Persist = false)]
    public float UsedWeight => Stored.Sum(sample => sample.Weight);

    // Sample ids must stay unique even after old cargo leaves the bay, so a
    // monotonic counter names them.
    [Saved("_sample_seq")]
    public int SampleSeq { get; private set; }

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

        // "is not ICollectable collectable" is a guard clause: nothing to
        // call, just reject.
        if (target is not ICollectable collectable)
        {
            return CommandResult.Reject("invalid_id");
        }

        if ((target.Position - Position).Length() > Reach)
        {
            return CommandResult.Reject("out_of_reach");
        }

        if (collectable.Volume < CollectionVolume)
        {
            return CommandResult.Reject("empty");
        }

        if (UsedVolume + CollectionVolume > MaxVolume)
        {
            return CommandResult.Reject("inventory_full");
        }

        if (UsedWeight + CollectionVolume * collectable.Density > MaxWeight)
        {
            return CommandResult.Reject("overloaded");
        }

        PendingCollect = id;

        return CommandResult.Ok();
    }

    // Delivery works the same way: validate now, hand the sample over at the
    // next tick. The sample must already be in cargo and the rover must be
    // standing at the facility, which must be idle.
    public CommandResult Deliver(string sampleId, string facilityId)
    {
        if (PendingDeliverSample is not null)
        {
            return CommandResult.Reject("deliver_already_requested");
        }

        if (!Stored.Any(sample => sample.Id == sampleId))
        {
            return CommandResult.Reject("unknown_sample");
        }

        if (World.Find(facilityId) is not AnalysisFacility facility)
        {
            return CommandResult.Reject("invalid_target");
        }

        if ((facility.Position - Position).Length() > Reach)
        {
            return CommandResult.Reject("out_of_reach");
        }

        if (facility.Sample is not null || facility.TicksRemaining > 0)
        {
            return CommandResult.Reject("facility_busy");
        }

        PendingDeliverSample = sampleId;
        PendingDeliverTarget = facilityId;

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

        if (PendingDeliverSample is not null)
        {
            LastDeliverResult = DeliverPending();
            PendingDeliverSample = null;
            PendingDeliverTarget = null;
        }

        if (PendingCollect is not null)
        {
            LastCollectResult = CollectPending();
            PendingCollect = null;
        }
    }

    // Runs at tick time for the accepted delivery intent. Every condition is
    // rechecked: acceptance was only a promise to try, and a failed attempt
    // leaves the sample in the cargo bay.
    private string DeliverPending()
    {
        MineralSample? sample = Stored.FirstOrDefault(s => s.Id == PendingDeliverSample);
        if (sample is null)
        {
            return "unknown_sample";
        }

        if (World.Find(PendingDeliverTarget!) is not AnalysisFacility facility)
        {
            return "invalid_target";
        }

        if ((facility.Position - Position).Length() > Reach)
        {
            return "out_of_reach";
        }

        if (!facility.Receive(sample))
        {
            return "facility_busy";
        }

        Stored.Remove(sample);
        return "delivered";
    }

    // Runs at tick time for the accepted collect intent. The checks from
    // Collect() run again — the world may have changed between acceptance
    // and the tick.
    private string CollectPending()
    {
        GameObject? target = World.Find(PendingCollect!);
        if (target is not ICollectable collectable)
        {
            return "unknown_target";
        }

        if ((target.Position - Position).Length() > Reach)
        {
            return "out_of_reach";
        }

        if (UsedVolume + CollectionVolume > MaxVolume)
        {
            return "inventory_full";
        }

        if (UsedWeight + CollectionVolume * collectable.Density > MaxWeight)
        {
            return "overloaded";
        }

        if (!collectable.Collect(CollectionVolume))
        {
            return "empty";
        }

        SampleSeq++;
        Stored.Add(new MineralSample
        {
            Id = $"sample-{Id}-{SampleSeq}",
            Source = target.Id,
            DataKey = collectable.DataKey,
            Volume = CollectionVolume,
        });

        return "collected";
    }

    // Cargo samples are projected by hand: each sample hides its mineral
    // definition until the type has been analyzed.
    public override void AdjustObservation(Dictionary<string, object?> fields)
    {
        fields["stored"] = Stored
            .Select(sample => (object?)sample.Observe(World.Knows(sample.DataKey)))
            .ToList();
    }

    public override void ValidateState()
    {
        base.ValidateState();

        Rules.Require(MaxVolume > 0, "Cargo volume limit must be positive");
        Rules.Require(MaxWeight > 0, "Cargo weight limit must be positive");
        Rules.Require(CollectionVolume > 0, "Collection volume must be positive");
        Rules.Require((PendingDeliverSample is null) == (PendingDeliverTarget is null), "Incomplete delivery intent");

        // Cargo may legitimately exceed the limits: cargo migrated from the
        // old slot system can be heavier than the new scale allows. It loads
        // fine; Collect() refuses further scoops until it is unloaded.

        foreach (MineralSample sample in Stored)
        {
            sample.Validate();
        }
    }
}
