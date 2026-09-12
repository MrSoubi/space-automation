using System.Numerics;

namespace SpaceAutomation.Game;

public interface IMovable
{
    CommandResult Move(Vector2 direction, float speed);
}

// Anything a survey scanner can reveal. The scanner never checks concrete
// classes: "obj is IScanable" finds every scanable thing in the world, and
// the object itself decides what being revealed means.
public interface IScanable
{
    void Reveal();
}

// Anything the rover can take material from. Collect() reports whether
// anything was actually taken — a depleted site changes nothing.
public interface ICollectable
{
    bool Collect();
}