namespace SpaceAutomation.Game;

/// <summary>Outcome of a gameplay command: acceptance now, completion later.</summary>
public sealed record CommandResult(bool Accepted, string? Reason = null)
{
    public static CommandResult Ok() => new(true);
    public static CommandResult Reject(string reason) => new(false, reason);
}
