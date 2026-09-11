using System.Numerics;

namespace SpaceAutomation.Game.Items;

// A survey scan is a request that takes time. Scan() answers immediately by
// storing the intent; Update() counts it down once per tick — exactly how
// Rover.Move + PendingMovement work. Commands never wait: the tick loop is
// the game's clock.
[GameType("survey-scanner")]
public class SurveyScanner : GameObject
{
    [Observed("range", "How far the scan reaches, in meters.")]
    public float Range { get; set; } = 5;

    [Observed("scan_time", "How many ticks a scan takes.")]
    public int ScanTime { get; set; } = 2;

    // One field is the whole scan state: zero means idle, anything above
    // means a scan is counting down. It is saved, so a scan in progress
    // survives a restart, and observed, so players can watch the countdown.
    [Observed("ticks_remaining")]
    public int TicksRemaining { get; private set; }

    // The scanner's memory: the ids of everything it has revealed so far.
    // Ids, never object references — saves are JSON, and a player looks each
    // id up in /state. Persisted and player-visible like every [Observed].
    [Observed("scanned")]
    public List<string> Scanned { get; private set; } = [];

    public CommandResult Scan()
    {
        if (TicksRemaining > 0)
        {
            return CommandResult.Reject("already_scanning");
        }

        TicksRemaining = ScanTime;

        return CommandResult.Ok();
    }

    public override void Update()
    {
        if (TicksRemaining == 0)
        {
            return;
        }

        TicksRemaining--;

        if (TicksRemaining > 0)
        {
            return;
        }

        // The countdown just reached zero: reveal every IScanable thing in
        // range and remember it. "obj is IScanable scanable" matches the
        // object and binds the interface view in one step — no cast, and no
        // need for the interface to expose the object itself.
        foreach (var obj in World.Objects.Values)
        {
            if (obj is not IScanable scanable)
            {
                continue;
            }

            if ((obj.Position - Position).Length() > Range)
            {
                continue;
            }

            scanable.Reveal();

            if (!Scanned.Contains(obj.Id))
            {
                Scanned.Add(obj.Id);
            }
        }
    }

    public override void ValidateState()
    {
        base.ValidateState();

        Rules.Require(Range > 0, "Scanner range must be positive");
        Rules.Require(ScanTime > 0, "Scan time must be at least one tick");
        Rules.Require(TicksRemaining >= 0 && TicksRemaining <= ScanTime, "Invalid remaining scan ticks");
        Rules.Require(Scanned is not null && Scanned.Distinct().Count() == Scanned.Count, "Scan memory must hold unique object ids");
    }
}
