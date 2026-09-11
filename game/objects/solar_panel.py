"""Small extension example; registered but not spawned in the default scenario."""
from __future__ import annotations
from dataclasses import dataclass
import math
from space_automation.engine import GameObject, command, observed
from space_automation.types import CommandResult


@dataclass
class SolarPanel(GameObject):
    """Example producer. Accumulated energy is not yet connected to a grid."""
    enabled: bool = observed(default=True, doc='Whether the panel is producing energy.')
    energy_per_tick: float = observed(default=1.0, doc='Energy produced per tick in joules.')
    energy_generated: float = observed(default=0.0, doc='Total energy generated in joules.')

    @command
    def set_enabled(self, enabled: bool) -> CommandResult:
        """Enable or disable generation. Multiple calls are allowed in a tick."""
        if type(enabled) is not bool:
            return CommandResult(False, 'invalid_enabled')
        self.enabled = enabled
        return CommandResult(True)

    def update(self, dt: float) -> None:
        if self.enabled:
            self.energy_generated += self.energy_per_tick

    def validate_state(self) -> None:
        super().validate_state()
        if type(self.enabled) is not bool:
            raise ValueError('Panel enabled must be boolean')
        for value in (self.energy_per_tick, self.energy_generated):
            if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
                raise ValueError('Panel energy must be finite and nonnegative')
