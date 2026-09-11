"""Object collection and simulation clock. No equipment-specific behavior."""
from pathlib import Path
from .engine import persistence
from .engine.registry import ObjectRegistry


class World:
    def __init__(self, objects=None, registry=None):
        if registry is None:
            from game.catalog import OBJECT_TYPES
            registry = ObjectRegistry(OBJECT_TYPES)
        if objects is None:
            from game.scenario import initial_objects
            objects = initial_objects()
        self.registry = registry
        self.tick = 0
        self.objects = {}
        for obj in objects:
            self.add(obj)

    def add(self, obj):
        obj.validate_state()
        self.registry.key_for(obj)
        if obj.id in self.objects:
            raise ValueError(f'Duplicate object id: {obj.id}')
        if hasattr(obj, '_world') and obj._world is not self:
            raise ValueError('Object already belongs to another World')
        obj._world = self
        self.objects[obj.id] = obj

    def snapshot(self):
        return {'tick': self.tick, 'objects': [self.registry.snapshot(obj)
                for obj in sorted(self.objects.values(), key=lambda obj: obj.id)]}

    def advance(self):
        for obj in sorted(self.objects.values(), key=lambda obj: obj.id):
            obj.update(1.0)
        self.tick += 1
        return self.snapshot()

    def save(self, path):
        persistence.write(path, {'version': 2, 'tick': self.tick,
                                'objects': [self.registry.dump(obj) for obj in self.objects.values()]})

    @classmethod
    def load(cls, path, registry=None):
        if not Path(path).exists():
            return cls(registry=registry)
        try:
            state = persistence.read(path)
            if type(state['tick']) is not int or state['tick'] < 0 or not isinstance(state['objects'], list):
                raise ValueError('Invalid world state')
            world = cls(objects=[], registry=registry)
            for record in state['objects']:
                world.add(world.registry.restore(record))
            world.tick = state['tick']
            return world
        except (ValueError, KeyError, TypeError, OverflowError, AttributeError) as exc:
            raise ValueError(f'Invalid or unsupported save file: {path}') from exc
