namespace SpaceAutomation.Game.Resources;

[GameType("mineral")]
public class Mineral : GameObject, IScanable, ICollectable
{
    [Observed("discovered", "True once a survey scan has found this site.")]
    public bool Discovered { get; set; }

    [Observed("volume", "Litres of material left at this site.")]
    public float Volume { get; set; } = 3;

    // The save stores only the data key, like Unity serializing a
    // ScriptableObject reference as an id; the catalog resolves it on load,
    // so no mineral data ever lives inside a save.
    [Saved("data")]
    public string DataKey { get; init; } = "";

    // Resolved from the catalog on every read — never serialized.
    public MineralData Data => MineralCatalog.Get(DataKey);

    // A scan only finds a node: "there is a mineral here". The definition
    // travels with the type, and the type is only known once one sample of
    // it has been analyzed at a facility.
    [Observed("identified", "True once the type has been analyzed at a facility.", Persist = false)]
    public bool Identified => World.Knows(DataKey);

    [Observed("data", "The full mineral definition, once identified.", Persist = false)]
    public Dictionary<string, object?>? DataView => Identified ? Data.Observe() : null;

    // Undiscovered minerals stay out of the player-facing state until a scan
    // reveals them. The save keeps them either way: saves are authoritative
    // truth, /state is what players may see.
    public override bool IsPlayerVisible => Discovered;

    // The scanner calls this when a scan finds the site in range.
    public void Reveal() => Discovered = true;

    // The rover calls this when scooping: only a full scoop leaves the site,
    // so a near-depleted one changes nothing and reports failure.
    public bool Collect(float volume)
    {
        if (Volume < volume)
        {
            return false;
        }

        Volume -= volume;
        return true;
    }

    public float Density => Data.Density;

    public override void AdjustObservation(Dictionary<string, object?> fields)
    {
        // An unanalyzed type hides its definition: the null data field is
        // removed so the shape itself tells the player "unknown mineral".
        if (fields["data"] is null)
        {
            fields.Remove("data");
        }
    }

    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(MineralCatalog.Contains(DataKey), $"Unknown mineral data: {DataKey}");
        Rules.Require(Volume >= 0, "Mineral volume must be non-negative");
    }
}
