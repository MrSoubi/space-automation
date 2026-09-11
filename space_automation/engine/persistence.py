"""Atomic storage and a one-time adapter for the original prototype saves."""
import json
import os
from pathlib import Path
import tempfile


def migrate(state):
    if type(state.get('version')) is not int:
        raise ValueError('Missing save version')
    if state['version'] == 1:
        pending = state['pending']
        rovers = state['rovers']
        if not isinstance(pending, dict) or not isinstance(rovers, list):
            raise ValueError('Invalid legacy save')
        if set(pending) - {r['id'] for r in rovers}:
            raise ValueError('Legacy order targets missing rover')
        def vector(value):
            if not isinstance(value, dict) or set(value) != {'x', 'y'}:
                raise ValueError('Invalid legacy vector')
            return {'$type': 'Vector2', **value}
        state = {'version': 2, 'tick': state['tick'], 'objects': [
            {'type': 'rover', 'state': {
                'id': rover['id'], 'position': vector(rover['position']),
                'max_speed': rover['max_speed'],
                '_movement': vector(pending[rover['id']]) if rover['id'] in pending else None,
            }} for rover in rovers]}
    if state['version'] != 2:
        raise ValueError('Unsupported save version')
    return state


def read(path):
    return migrate(json.loads(Path(path).read_text()))


def write(path, state):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    name = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as output:
            name = output.name
            json.dump(state, output, allow_nan=False)
            output.flush()
            os.fsync(output.fileno())
        os.replace(name, path)
        directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if name and os.path.exists(name):
            os.unlink(name)
