"""Player-side object discovery and observations, independent of gameplay code."""
from typing import Callable, TypeVar, overload
from .generated import PROXY_TYPES, Rover
from .proxy import ObjectProxy

T = TypeVar('T', bound=ObjectProxy)


class Station:
    """Connection to planetary equipment, available inside space-automation."""
    def __init__(self):
        self._observations = {}
        self._objects = {}
        self._tick = 0
        self._transport: Callable | None = None

    @property
    def tick(self) -> int:
        """Number of completed simulation steps in the current snapshot."""
        return self._tick

    @overload
    def get_objects(self, kind: type[T]) -> list[T]: ...

    @overload
    def get_objects(self, kind: None = None) -> list[ObjectProxy]: ...

    def get_objects(self, kind=None):
        """List objects, optionally filtered by an exported player API class."""
        objects = [self._objects[key] for key in sorted(self._observations)]
        return [obj for obj in objects if kind is None or isinstance(obj, kind)]

    def get_object(self, identifier: str) -> ObjectProxy:
        """Look up any object by its stable id. Raises KeyError if absent."""
        if identifier not in self._observations:
            raise KeyError(identifier)
        return self._objects[identifier]

    def get_fleet(self) -> list[Rover]:
        """Compatibility shortcut for get_objects(Rover)."""
        return self.get_objects(Rover)

    def _request(self, operation, **arguments):
        if self._transport is None:
            raise RuntimeError('The expedition API must run inside space-automation')
        return self._transport(operation, **arguments)

    def _refresh(self, snapshot):
        records = {record['id']: record for record in snapshot['objects']}
        for identifier, record in records.items():
            try:
                cls = PROXY_TYPES[record['type']]
            except KeyError as exc:
                raise RuntimeError('Player API is stale; run python -m tools.generate_api') from exc
            if identifier not in self._objects or type(self._objects[identifier]) is not cls:
                self._objects[identifier] = cls(self, identifier)
        self._tick = snapshot['tick']
        self._observations = records


station = Station()
