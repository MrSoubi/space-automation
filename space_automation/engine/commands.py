"""Route all exposed methods through one allowlisted dispatch path."""
import inspect
from .codec import decode, encode
from space_automation.types import CommandResult


class CommandDispatcher:
    def __init__(self, world):
        self.world = world

    def execute(self, *, tick, object_id, method, args, kwargs):
        def reject(reason):
            return encode(CommandResult(False, reason))
        if type(tick) is not int or tick != self.world.tick:
            return reject('stale_tick')
        obj = self.world.objects.get(object_id)
        if obj is None:
            return reject('unknown_object')
        if method not in obj.commands():
            return reject('unknown_command')
        if not isinstance(args, list) or not isinstance(kwargs, dict):
            return reject('invalid_arguments')
        function = getattr(obj, method)
        try:
            bound = inspect.signature(function).bind(*decode(args), **decode(kwargs))
        except (TypeError, ValueError):
            return reject('invalid_arguments')
        # Domain validation and side effects belong to the object method.
        return encode(function(*bound.args, **bound.kwargs))
