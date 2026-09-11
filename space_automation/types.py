"""Values shared by gameplay and the remote player API; no process dependencies."""
from dataclasses import dataclass
import math
from numbers import Real


@dataclass(frozen=True)
class Vector2:
    """Immutable two-dimensional vector; positions are measured in meters."""
    x: float
    y: float

    def __post_init__(self):
        for value in (self.x, self.y):
            if isinstance(value, bool) or not isinstance(value, Real) or not math.isfinite(value):
                raise ValueError('Vector2 components must be finite real numbers')
        object.__setattr__(self, 'x', float(self.x))
        object.__setattr__(self, 'y', float(self.y))

    def __add__(self, other: 'Vector2') -> 'Vector2':
        return Vector2(self.x + other.x, self.y + other.y)

    def __sub__(self, other: 'Vector2') -> 'Vector2':
        return Vector2(self.x - other.x, self.y - other.y)

    def __mul__(self, scalar: float) -> 'Vector2':
        return Vector2(self.x * scalar, self.y * scalar)

    __rmul__ = __mul__

    def length(self) -> float:
        """Return Euclidean magnitude."""
        return math.hypot(self.x, self.y)

    def normalized(self) -> 'Vector2':
        """Return a unit vector. A zero vector has no direction."""
        scale = max(abs(self.x), abs(self.y))
        if not scale:
            raise ValueError('Cannot normalize a zero vector')
        x, y = self.x / scale, self.y / scale
        length = math.hypot(x, y)
        return Vector2(x / length, y / length)


@dataclass(frozen=True)
class CommandResult:
    """Order admission result, not action completion."""
    accepted: bool
    reason: str | None = None

