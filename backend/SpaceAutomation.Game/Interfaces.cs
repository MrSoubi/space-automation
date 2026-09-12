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

// Anything the rover can take material from. A scoop takes a fixed volume;
// Collect(volume) reports whether the full scoop was actually taken — a
// near-empty site changes nothing. Volume is what remains on site, and
// Density (kilograms per litre) feeds the rover's cargo weight checks.
public interface ICollectable
{
    float Volume { get; }

    float Density { get; }

    string DataKey { get; }

    bool Collect(float volume);
}
