"""The extension seam: a new class/method needs no transport or world changes."""
from dataclasses import dataclass
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from expedition import SolarPanel as PlayerSolarPanel, Vector2
from expedition.station import Station
from game.objects.rover import Rover
from game.objects.solar_panel import SolarPanel
from space_automation.engine import GameObject, command, observed
from space_automation.engine.codec import decode, encode
from space_automation.engine.commands import CommandDispatcher
from space_automation.engine.registry import ObjectRegistry
from space_automation.types import CommandResult
from space_automation.world import World
from tools.generate_api import render


@dataclass
class Counter(GameObject):
    value: int = observed(default=0, doc='Current count.')
    _secret: int = 9

    @command
    def increment(self, amount: int = 1, *, twice: bool = False) -> int:
        """Increment and return the new count."""
        self.value += amount * (2 if twice else 1)
        return self.value

    def private_method(self):
        raise AssertionError('Must never dispatch')


class ObjectTests(unittest.TestCase):
    def call(self, world, method, *args, tick=None, object_id='panel', **kwargs):
        return decode(CommandDispatcher(world).execute(tick=world.tick if tick is None else tick,
            object_id=object_id, method=method, args=encode(args), kwargs=encode(kwargs)))

    def test_new_type_new_method_proxy_and_persistence_without_core_edits(self):
        registry = ObjectRegistry({'counter': Counter})
        world = World([Counter('counter-1')], registry)
        namespace = {'__name__': 'expedition.test_generated', '__package__': 'expedition'}
        exec(render({'counter': Counter}), namespace)
        station = Station()
        station._transport = lambda op, **payload: CommandDispatcher(world).execute(**payload)
        with patch.dict('expedition.station.PROXY_TYPES', namespace['PROXY_TYPES']):
            station._refresh(world.snapshot())
            counter = station.get_objects()[0]
            self.assertEqual(counter.increment(3, twice=True), 6)
            self.assertEqual(counter.value, 0)  # Snapshot boundary, not direct state sharing.
            station._refresh(world.snapshot())
            self.assertIs(counter, station.get_object('counter-1'))
            self.assertEqual(counter.value, 6)
            self.assertFalse(hasattr(counter, '_secret'))
            self.assertFalse(hasattr(counter, 'private_method'))
            with self.assertRaises(AttributeError):
                counter.value = 100
        with tempfile.TemporaryDirectory() as directory:
            save = Path(directory) / 'world.json'
            world.save(save)
            restored = World.load(save, registry)
            self.assertEqual(restored.objects['counter-1'].value, 6)
            self.assertEqual(restored.objects['counter-1']._secret, 9)

    def test_generic_dispatch_allowlist_stale_tick_and_bad_signature(self):
        world = World([SolarPanel('panel')])
        self.assertEqual(self.call(world, 'update', 1).reason, 'unknown_command')
        self.assertEqual(self.call(world, '__class__').reason, 'unknown_command')
        self.assertEqual(self.call(world, 'set_enabled', tick=3).reason, 'stale_tick')
        self.assertEqual(self.call(world, 'set_enabled').reason, 'invalid_arguments')
        self.assertEqual(self.call(world, 'set_enabled', False, extra=1).reason, 'invalid_arguments')
        self.assertEqual(self.call(world, 'set_enabled', 1).reason, 'invalid_enabled')
        self.assertTrue(self.call(world, 'set_enabled', False).accepted)
        world.advance()
        self.assertEqual(world.objects['panel'].energy_generated, 0)
        self.assertTrue(self.call(world, 'set_enabled', True).accepted)
        world.advance()
        self.assertEqual(world.objects['panel'].energy_generated, 1)

    def test_existing_save_migration_preserves_pending_zero_speed_and_progress(self):
        legacy = {'version': 1, 'tick': 42, 'rovers': [
            {'id': 'rover-1', 'position': {'x': 4.2, 'y': 1.6}, 'max_speed': 3}],
            'pending': {'rover-1': {'x': 0, 'y': 0}}}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'world.json'
            path.write_text(json.dumps(legacy))
            world = World.load(path)
            self.assertEqual(world.tick, 42)
            self.assertEqual(world.objects['rover-1'].position, Vector2(4.2, 1.6))
            self.assertEqual(world.objects['rover-1'].move(Vector2(1, 0), 1).reason,
                             'movement_already_requested')
            self.assertEqual(json.loads(path.read_text())['version'], 1)  # No write during load.
            world.save(path)
            self.assertEqual(json.loads(path.read_text())['version'], 2)

    def test_malformed_object_save_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'world.json'
            world = World([Rover('r')])
            world.save(path)
            state = json.loads(path.read_text())
            state['objects'][0]['state']['max_speed'] = -3
            path.write_text(json.dumps(state))
            with self.assertRaises(ValueError):
                World.load(path)
