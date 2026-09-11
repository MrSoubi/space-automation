from __future__ import annotations
from dataclasses import dataclass
import math
from space_automation.engine import GameObject, command, observed
from space_automation.types import CommandResult, Vector2


@dataclass
class AnalysisFacility(GameObject):
    """The first building available."""

    # Private, persisted state.

    def update(self, dt: float) -> None:
        pass

    def validate_state(self) -> None:
        super().validate_state()
