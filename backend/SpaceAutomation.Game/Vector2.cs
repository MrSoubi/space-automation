namespace SpaceAutomation.Game;

/// <summary>Double precision position/direction in meters.</summary>
public readonly record struct Vector2(double X, double Y)
{
    public static Vector2 Zero => new(0, 0);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public double Length => Math.Sqrt(X * X + Y * Y);
    
    public Vector2 Normalized()
    {
        var scale = Math.Max(Math.Abs(X), Math.Abs(Y));

        if (!IsFinite || scale == 0){
            throw new ArgumentException("Direction must be finite and nonzero");
        }

        var x = X / scale; var y = Y / scale;
        var length = Math.Sqrt(x * x + y * y);
        return new(x / length, y / length);
    }
    
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator *(Vector2 a, double scale) => new(a.X * scale, a.Y * scale);
}
