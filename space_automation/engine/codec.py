"""Explicit JSON values, used for commands, observations, and saved fields."""
import math
from space_automation.types import CommandResult, Vector2


def encode(value):
    if isinstance(value, Vector2):
        return {'$type': 'Vector2', 'x': value.x, 'y': value.y}
    if isinstance(value, CommandResult):
        return {'$type': 'CommandResult', 'accepted': value.accepted, 'reason': value.reason}
    if isinstance(value, float) and not math.isfinite(value):
        # Preserve bad numeric command arguments so the object can reject them.
        return {'$type': 'Float', 'value': str(value)}
    if value is None or type(value) in (str, int, float, bool):
        return value
    if isinstance(value, (list, tuple)):
        return [encode(item) for item in value]
    if isinstance(value, dict) and all(isinstance(key, str) for key in value):
        if '$type' in value:
            raise ValueError('$type is reserved by the value codec')
        return {key: encode(item) for key, item in value.items()}
    raise TypeError(f'Unsupported API value: {type(value).__name__}')


def decode(value):
    if isinstance(value, list):
        return [decode(item) for item in value]
    if not isinstance(value, dict):
        return value
    kind = value.get('$type')
    if kind == 'Vector2' and set(value) == {'$type', 'x', 'y'}:
        return Vector2(value['x'], value['y'])
    if kind == 'CommandResult' and set(value) == {'$type', 'accepted', 'reason'}:
        return CommandResult(value['accepted'], value['reason'])
    if kind == 'Float' and value.get('value') in ('nan', 'inf', '-inf'):
        return float(value['value'])
    if kind is not None:
        raise ValueError(f'Unknown or malformed API value type: {kind}')
    return {key: decode(item) for key, item in value.items()}
