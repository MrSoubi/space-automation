namespace SpaceAutomation.Game.Resources;

[GameType("mineral")]
public class Mineral : GameObject, IScanable, ICollectable
{
    [Observed("discovered", "True once a survey scan has found this site.")]
    public bool Discovered { get; set; }

    [Observed("amount", "Units of material left at this site.")]
    public int Amount { get; set; } = 3;

    // Undiscovered minerals stay out of the player-facing state until a scan
    // reveals them. The save keeps them either way: saves are authoritative
    // truth, /state is what players may see.
    public override bool IsPlayerVisible => Discovered;

    // The scanner calls this when a scan finds the site in range.
    public void Reveal() => Discovered = true;

    // The rover calls this when collecting: taking from a depleted site
    // changes nothing and reports failure.
    public bool Collect()
    {
        if (Amount <= 0)
        {
            return false;
        }

        Amount--;
        return true;
    }
}
