"""Small declarative contract between gameplay and the engine."""
from dataclasses import MISSING, dataclass, field, fields
from inspect import getmembers
from typing import TYPE_CHECKING, Callable, TypeVar

if TYPE_CHECKING:
    from space_automation.world import World

F = TypeVar('F', bound=Callable)


def command(method: F) -> F:
    """Expose this method to player scripts. Unmarked methods remain private."""
    method.__player_command__ = True
    return method


def observed(*, default=MISSING, default_factory=MISSING, doc: str = ''):
    """A persisted dataclass field visible as a read-only player property."""
    return field(default=default, default_factory=default_factory,
                 metadata={'observed': True, 'doc': doc})


@dataclass
class GameObject:
    """Base for authoritative game objects. Every dataclass field is saved.

    Use observed() for player-readable fields, @command for player-callable
    methods, update() for per-tick behavior, and validate_state() for save checks.
    """
    id: str
    position: Vector2 = observed(default = Vector2(0,0), doc='Position coordinates.')
    
    @property
    def world(self) -> 'World':
        """Simulation context for object interactions; not saved or player-visible."""
        if not hasattr(self, '_world'):
            raise RuntimeError('Add this object to a World before accessing its context')
        return self._world

    def update(self, dt: float) -> None:
        """Advance one simulation tick. Override for time-dependent behavior."""

    def validate_state(self) -> None:
        """Reject invalid construction/restored state with ValueError."""
        if not isinstance(self.id, str) or not self.id:
            raise ValueError('Game objects need a nonempty string id')

    @classmethod
    def commands(cls) -> dict[str, Callable]:
        return {name: method for name, method in getmembers(cls, callable)
                if getattr(method, '__player_command__', False) and not name.startswith('_')}

    @classmethod
    def observations(cls):
        return [item for item in fields(cls) if item.name == 'id' or item.metadata.get('observed')]
