namespace SpaceAutomation.Game;

/// <summary>Single precision position/direction in meters.</summary>
public readonly record struct Vector2(float X, float Y)
{
    public static Vector2 Zero => new Vector2(0, 0);
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
    public float Length => MathF.Sqrt(X * X + Y * Y);

    public Vector2 Normalized()
    {
        var scale = MathF.Max(MathF.Abs(X), MathF.Abs(Y));

        if (!IsFinite || scale == 0)
        {
            throw new ArgumentException("Direction must be finite and nonzero");
        }

        var x = X / scale;
        var y = Y / scale;
        var length = MathF.Sqrt(x * x + y * y);

        return new Vector2(x / length, y / length);
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator *(Vector2 a, float scale) => new Vector2(a.X * scale, a.Y * scale);
}
