"""Generic remote object plumbing. Generated API classes only name members."""
from typing import TYPE_CHECKING
from space_automation.engine.codec import decode, encode

if TYPE_CHECKING:
    from .station import Station


class ObjectProxy:
    def __init__(self, owner: 'Station', identifier: str):
        self._owner = owner
        self._id = identifier

    @property
    def id(self) -> str:
        """Stable identifier of this game object."""
        return self._id

    def _read(self, name):
        try:
            return decode(self._owner._observations[self.id]['properties'][name])
        except KeyError as exc:
            raise RuntimeError(f'Object {self.id!r} or property {name!r} is no longer available') from exc

    def _call(self, method, *args, **kwargs):
        result = self._owner._request('command', tick=self._owner.tick, object_id=self.id,
                                      method=method, args=encode(args), kwargs=encode(kwargs))
        return decode(result)
