#!/usr/bin/env python3
"""Space Automation player script with Textual TUI dashboard."""

import math
from api import Game
from textual.app import App, ComposeResult
from textual.widgets import Header, Footer, DataTable, Digits, Static


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
        table.add_columns("ID", "Position (X, Y)", "Speed Limit", "Cargo Stored", "Last Collect Result")

        # Run tick update 10 times per second
        self.set_interval(0.1, self.game_tick)

    def game_tick(self) -> None:
        """Refresh game state, run player logic, and update Textual display."""
        state = self.game.refresh()

        # Update tick display
        self.query_one("#tick_display", Digits).update(str(state["tick"]))

        # Execute rover automation logic
        self.update_game_logic()

        # Render rovers in the DataTable
        table = self.query_one("#rover_table", DataTable)
        table.clear()

        # game.objects("rover") returns Obj instances from api.py
        rovers = self.game.objects("rover")

        for rover in rovers:
            pos_str = f"({rover.position.x:.1f}, {rover.position.y:.1f})"
            
            # Safely check optional attributes using getattr or Obj's getattr handling
            speed = getattr(rover, "speed_limit", "N/A")
            stored = getattr(rover, "stored", 0)
            last_collect = getattr(rover, "last_collect_result", None) or "Idle"

            table.add_row(
                str(rover.id),
                pos_str,
                str(speed),
                str(stored),
                str(last_collect)
            )

    def update_game_logic(self) -> None:
        """Game automation logic driving rover-1 to minerals."""
        rover = self.game.object("rover-1")
        scanner = self.game.object("scanner-1")

        if rover is None or scanner is None:
            return

        # Phase 1: Survey sector
        if not scanner.scanned:
            if scanner.ticks_remaining == 0:
                self.game.scan(scanner.id)
            return

        # Phase 2: Drive to mineral site
        minerals = self.game.objects("mineral")
        if not minerals:
            return

        target = minerals[0]
        offset = (target.position.x - rover.position.x, target.position.y - rover.position.y)
        distance = math.hypot(*offset)

        if distance <= 0.5:
            if target.amount > 0 and (rover.last_collect_result is None or rover.last_collect_result == "collected"):
                self.game.collect(rover.id, target.id)
            return

        direction = (offset[0] / distance, offset[1] / distance)
        speed = min(rover.speed_limit, distance)
        self.game.move(rover.id, direction, speed)


def main():
    game = Game()
    app = RoverDashboard(game)
    app.run()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("bye")