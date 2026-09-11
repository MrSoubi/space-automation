"""Object lookup and generic persistence; no knowledge of equipment rules."""
from dataclasses import fields, is_dataclass
from .codec import decode, encode
from .objects import GameObject


class ObjectRegistry:
    def __init__(self, types):
        self.types = dict(types)
        if len(set(self.types.values())) != len(self.types):
            raise ValueError('Register each object class once')
        for cls in self.types.values():
            if not issubclass(cls, GameObject) or not is_dataclass(cls):
                raise TypeError('Registered types must be GameObject dataclasses')

    def key_for(self, obj):
        for key, cls in self.types.items():
            if type(obj) is cls:
                return key
        raise ValueError(f'Unregistered object class: {type(obj).__name__}')

    def snapshot(self, obj):
        return {'type': self.key_for(obj), 'id': obj.id,
                'properties': {item.name: encode(getattr(obj, item.name)) for item in obj.observations()}}

    def dump(self, obj):
        return {'type': self.key_for(obj),
                'state': {item.name: encode(getattr(obj, item.name)) for item in fields(obj)}}

    def restore(self, record):
        cls = self.types[record['type']]
        values = record['state']
        names = {item.name for item in fields(cls)}
        if not isinstance(values, dict) or set(values) - names:
            raise ValueError('Unexpected saved object fields')
        obj = cls(**{key: decode(value) for key, value in values.items()})
        obj.validate_state()
        return obj
