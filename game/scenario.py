"""Edit initial equipment here. This only runs when there is no saved world."""
from game.objects.rover import Rover
from space_automation.types import Vector2


def initial_objects():
    return [
        Rover(id='rover-1', position=Vector2(0, 0)),
        Rover(id='rover-2', position=Vector2(5, 0)),
    ]
