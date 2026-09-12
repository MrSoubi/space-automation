namespace SpaceAutomation.Game.Buildings;

using SpaceAutomation.Game.Resources;

// Turns an unknown sample into expedition-wide knowledge: analysis consumes
// the sample and records its mineral type as known to the whole world. After
// that, every node and sample of the type shows its data without being
// analyzed again.
[GameType("analysis-facility")]
public class AnalysisFacility : GameObject
{
    [Observed("analysis_time", "How many ticks an analysis takes.")]
    public int AnalysisTime { get; set; } = 3;

    // One field is the whole state: zero means idle, anything above means an
    // analysis is counting down. Saved, so an analysis survives a restart.
    [Observed("ticks_remaining", "Countdown while analyzing; zero means idle.")]
    public int TicksRemaining { get; private set; }

    // The sample waiting for analysis, or mid-analysis.
    [Saved("sample")]
    public MineralSample? Sample { get; private set; }

    // A rover hands a sample over at tick time. Returns false while the
    // facility is busy; the sample stays with the rover then.
    public bool Receive(MineralSample sample)
    {
        if (Sample is not null || TicksRemaining > 0)
        {
            return false;
        }

        Sample = sample;
        return true;
    }

    public CommandResult Analyze()
    {
        if (TicksRemaining > 0)
        {
            return CommandResult.Reject("already_analyzing");
        }

        if (Sample is null)
        {
            return CommandResult.Reject("no_sample");
        }

        TicksRemaining = AnalysisTime;

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

        // The countdown just reached zero: consume the sample and record the
        // type as known to the whole expedition.
        if (Sample is not null)
        {
            World.Learn(Sample.DataKey);
            Sample = null;
        }
    }

    // The held sample is projected by hand so its mineral definition stays
    // hidden until the type has been analyzed.
    public override void AdjustObservation(Dictionary<string, object?> fields)
    {
        fields["sample"] = Sample is null
            ? null
            : Sample.Observe(World.Knows(Sample.DataKey));
    }

    public override void ValidateState()
    {
        base.ValidateState();

        Rules.Require(AnalysisTime > 0, "Analysis time must be at least one tick");
        Rules.Require(TicksRemaining >= 0 && TicksRemaining <= AnalysisTime, "Invalid remaining analysis ticks");
        Rules.Require(TicksRemaining == 0 || Sample is not null, "Analyzing without a sample");

        Sample?.Validate();
    }
}
