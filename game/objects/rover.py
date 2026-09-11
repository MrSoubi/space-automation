"""Rover gameplay: capabilities, movement admission, and per-tick motion."""
from __future__ import annotations
from dataclasses import dataclass
import math
from space_automation.engine import GameObject, command, observed
from space_automation.types import CommandResult, Vector2


def finite_number(value) -> bool:
    try:
        return type(value) in (int, float) and math.isfinite(value)
    except OverflowError:
        return False


@dataclass
class Rover(GameObject):
    """A surface vehicle controlled through one movement request per tick."""
    max_speed: float = observed(default=3.0, doc='Maximum speed in meters per tick.')
    max_energy: float = observed(default=20.0, doc='Maximum energy stored.')
    energy_recharging_speed: float = observed(default=1.0, doc='Energy recharged per tick.')
    energy_consumption: float = observed(default=2.0, doc='Energy consumed per tick when moving.')

    # Private, persisted state. None = no order; a zero vector still reserves a slot.
    _movement: Vector2 | None = None
    _energy: float = 0.0

    @command
    def move(self, direction: Vector2, speed: float) -> CommandResult:
        """Request movement for this tick. Direction is normalized; speed is capped.

        The first valid request wins. Invalid requests do not consume the slot.
        """
        if not isinstance(direction, Vector2) or direction == Vector2(0, 0):
            return CommandResult(False, 'invalid_direction')
        if not finite_number(speed) or speed < 0:
            return CommandResult(False, 'invalid_speed')
        if self._movement is not None:
            return CommandResult(False, 'movement_already_requested')
        if self._energy < self.energy_consumption:
            return CommandResult(False, 'not_enough_energy')
        self._movement = direction.normalized() * min(speed, self.max_speed)
        self._energy -= self.energy_consumption
        return CommandResult(True)

    def update(self, dt: float) -> None:
        if self._movement is not None:
            self.position = self.position + self._movement
            self._movement = None
        else:
            self._energy = max(self.max_energy, self._energy + self.energy_recharging_speed)

    def validate_state(self) -> None:
        super().validate_state()
        if not isinstance(self.position, Vector2):
            raise ValueError('Rover position must be a Vector2')
        if not finite_number(self.max_speed) or self.max_speed <= 0:
            raise ValueError('Rover max_speed must be positive and finite')
        if self._movement is not None:
            if not isinstance(self._movement, Vector2) or self._movement.length() > self.max_speed * (1 + 1e-12):
                raise ValueError('Invalid pending rover movement')
