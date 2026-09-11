namespace SpaceAutomation.Game.Resources;

[GameType("mineral")]
public class Mineral : GameObject, IScanable
{
    [Observed("discovered", "True once a survey scan has found this site.")]
    public bool Discovered { get; set; }

    // Undiscovered minerals stay out of the player-facing state until a scan
    // reveals them. The save keeps them either way: saves are authoritative
    // truth, /state is what players may see.
    public override bool IsPlayerVisible => Discovered;

    // The scanner calls this when a scan finds the site in range.
    public void Reveal() => Discovered = true;
}
