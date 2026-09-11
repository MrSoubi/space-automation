namespace SpaceAutomation.Game;

/// <summary>Shared gameplay validation helpers.</summary>
public static class Rules
{
    public static bool Nonnegative(float value) => float.IsFinite(value) && value >= 0;

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
