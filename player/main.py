#!/usr/bin/env python3
"""Space Automation player script with Textual TUI dashboard.

The full gameplay loop: scan finds mineral *nodes* (locations only — no
mineral data), collecting brings unknown samples on board, delivery hands
them to the analysis facility, and analysis consumes a sample to reveal its
mineral definition. From then on every node and sample of that type shows
its data without being analyzed again.
"""

import math

from api import Game
from textual.app import App, ComposeResult
from textual.widgets import Header, Footer, DataTable, Digits, Static

# How close the rover must be to count as standing on something.
REACH = 0.1


def distance(a, b):
    return math.hypot(b.x - a.x, b.y - a.y)


class RoverDashboard(App):
    CSS = """
    Screen {
        layout: vertical;
        padding: 1;
    }
    #tick_box {
        height: 3;
        margin-bottom: 1;
    }
    DataTable {
        height: 100%;
        border: solid green;
    }
    """

    def __init__(self, game: Game):
        super().__init__()
        self.game = game

    def compose(self) -> ComposeResult:
        yield Header()
        yield Static("Current Simulation Tick:")
        yield Digits("0", id="tick_display")
        yield DataTable(id="rover_table")
        yield Footer()

    def on_mount(self) -> None:
        """Initialize table columns and trigger the update interval."""
        table = self.query_one("#rover_table", DataTable)
        table.add_columns("ID", "Position (X, Y)", "Speed Limit", "Cargo (volume / weight)", "Last Collect Result")
        self.set_interval(0.1, self.game_tick)

    def game_tick(self) -> None:
        """Refresh game state, run player logic, and update Textual display."""
        state = self.game.refresh()
        self.query_one("#tick_display", Digits).update(str(state["tick"]))

        self.update_game_logic()

        table = self.query_one("#rover_table", DataTable)
        table.clear()
        for rover in self.game.objects("rover"):
            pos_str = f"({rover.position.x:.1f}, {rover.position.y:.1f})"
            stored = getattr(rover, "stored", [])
            cargo = f"{getattr(rover, 'used_volume', 0):.1f} L / {getattr(rover, 'used_weight', 0):.2f} kg"
            last = getattr(rover, "last_collect_result", None) or "Idle"
            table.add_row(str(rover.id), pos_str, str(rover.speed_limit), cargo, str(last))

    def update_game_logic(self) -> None:
        """Automation: scan, collect, deliver, analyze — in that order."""
        rover = self.game.object("rover-1")
        scanner = self.game.object("scanner-1")
        facility = self.game.object("facility-1")
        if rover is None or scanner is None or facility is None:
            return

        # Phase 1: survey the sector once, then wait for the scan to finish.
        if not scanner.scanned:
            if scanner.ticks_remaining == 0:
                self.game.scan(scanner.id)
            return

        # Phase 2: run the analysis whenever a sample is waiting and idle.
        sample = facility.sample
        if sample is not None and facility.ticks_remaining == 0 and not sample.get("identified", False):
            self.game.analyze(facility.id)
            return

        # Phase 3: keep collecting while the cargo can still grow; once a
        # rejection says otherwise, drive to the facility and deliver.
        if self._can_collect(rover):
            self._collect_step(rover)
        elif rover.stored:
            self._deliver_step(rover, facility)

    def _can_collect(self, rover) -> bool:
        return rover.last_collect_result not in ("inventory_full", "overloaded", "empty")

    def _collect_step(self, rover) -> None:
        minerals = self.game.objects("mineral")
        if not minerals:
            return

        target = minerals[0]
        if distance(rover.position, target.position) > REACH:
            self._move_to(rover, target.position)
            return

        self.game.collect(rover.id, target.id)

    def _deliver_step(self, rover, facility) -> None:
        if facility.sample is not None:
            return  # busy: wait for the analysis to finish

        if distance(rover.position, facility.position) > REACH:
            self._move_to(rover, facility.position)
            return

        self.game.deliver(rover.id, rover.stored[0]["id"], facility.id)

    def _move_to(self, rover, target) -> None:
        offset = (target.x - rover.position.x, target.y - rover.position.y)
        dist = math.hypot(*offset)
        if dist <= REACH:
            return

        direction = (offset[0] / dist, offset[1] / dist)
        self.game.move(rover.id, direction, min(rover.speed_limit, dist))


def main():
    game = Game()
    app = RoverDashboard(game)
    app.run()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("bye")
