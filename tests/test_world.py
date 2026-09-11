import math
from pathlib import Path
import tempfile
import unittest
from expedition import Vector2
from game.objects.rover import Rover
from space_automation.world import World


class MovementTests(unittest.TestCase):
    def setUp(self):
        self.rover = Rover(id='rover-1')

    def test_normalization_cap_and_no_persistent_motion(self):
        self.assertTrue(self.rover.move(Vector2(3, 4), 100).accepted)
        self.rover.update(1.0)
        self.assertAlmostEqual(self.rover.position.x, 1.8)
        self.assertAlmostEqual(self.rover.position.y, 2.4)
        previous = self.rover.position
        self.rover.update(1.0)
        self.assertEqual(previous, self.rover.position)

    def test_invalid_inputs_slots_and_independent_rovers(self):
        for direction, speed, reason in [(Vector2(0, 0), 1, 'invalid_direction'),
                                          ({'x': 1, 'y': 0}, 1, 'invalid_direction'),
                                          (Vector2(1, 0), -1, 'invalid_speed'),
                                          (Vector2(1, 0), math.inf, 'invalid_speed'),
                                          (Vector2(1, 0), True, 'invalid_speed')]:
            self.assertEqual(self.rover.move(direction, speed).reason, reason)
        self.assertTrue(self.rover.move(Vector2(1, 0), 0).accepted)
        self.assertEqual(self.rover.move(Vector2(1, 0), 1).reason, 'movement_already_requested')
        other = Rover(id='other')
        self.assertTrue(other.move(Vector2(1, 0), 1).accepted)
        self.rover.update(1.0)
        self.assertEqual(self.rover.position, Vector2(0, 0))
        self.assertTrue(self.rover.move(Vector2(1, 0), 1).accepted)

    def test_huge_direction_is_normalized_without_overflow(self):
        self.assertTrue(self.rover.move(Vector2(1e308, 1e308), 2).accepted)
        self.rover.update(1.0)
        self.assertAlmostEqual(self.rover.position.x, math.sqrt(2))

    def test_save_preserves_open_round_and_invalid_save_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'world.json'
            world = World(objects=[self.rover])
            self.rover.move(Vector2(3, 4), 2)
            world.save(path)
            loaded = World.load(path)
            self.assertEqual(loaded.objects['rover-1'].move(Vector2(1, 0), 1).reason,
                             'movement_already_requested')
            loaded.advance()
            self.assertAlmostEqual(loaded.objects['rover-1'].position.x, 1.2)
            path.write_text('{broken')
            with self.assertRaises(ValueError):
                World.load(path)
            self.assertEqual(path.read_text(), '{broken')

    def test_vectors_immutable_and_arithmetic(self):
        vector = Vector2(3, 4)
        self.assertEqual(vector.length(), 5)
        self.assertEqual(vector - Vector2(1, 2), Vector2(2, 2))
        with self.assertRaises(AttributeError):
            vector.x = 7
        with self.assertRaises(ValueError):
            Vector2(float('nan'), 0)
