# Space Automation prototype

A Linux-only Python programming sandbox. One command starts a terminal supervisor, a player-code runtime, and an authoritative simulation connected through private Unix domain sockets.

Requires Python **3.10 or newer**, with Textual for the terminal interface. Verified with Python 3.12 and Textual 8.2.8 on Linux.

Install dependencies once:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
```

The launcher automatically uses the project's `.venv` when present.

## Run

```bash
./space-automation
```

Interactive launch opens a scrollable output panel, a compact status bar containing the session name and current tick, and a fixed command field at the bottom. Output cannot overwrite what you are typing. Use Up/Down for command history and the mouse wheel to scroll results.

Set the displayed session name with `--session "Survey Alpha"`. This is a display label; `--save` still selects the persisted world. `--plain` retains the basic interpreter, and redirected input/output automatically uses plain mode.

To inspect the initial world without advancing time:

```bash
./space-automation --paused
```

Edit `player/main.py` in your preferred editor. The game calls `startup()` on launch/restart and `update(dt)` each tick. Only this file has game-managed callbacks; import your own modules and organize controllers however you prefer.

The world is saved on quit in `.space-automation/world.json`. Relaunch restores positions, time, and pending commands. Python variables are recreated by startup; the world does not advance while the application is closed. Saves are local and there is no autosave yet.

Use `--script /path/to/main.py` and `--save /path/to/world.json` for separate experiments. A missing entry point is created; existing scripts are never overwritten. A corrupt save is reported and preserved.

## First commands

At the Python prompt:

```python
from expedition import Vector2
rover = station.get_fleet()[0]
rover.position
rover.move(Vector2(3, 4), 2)
```

When paused, advance one step with:

```text
:step
```

Then query:

```python
rover.position  # Vector2(x=1.2, y=1.6) for a fresh world
```

`Vector2` is a small immutable type included with this project. Python's standard `math` module does not supply a general-purpose 2D vector class. It supports `x`, `y`, addition, subtraction, scalar multiplication, `length()`, and `normalized()`.

Positions are in meters. `move(direction, speed)` normalizes direction and caps speed to `rover.max_speed` (3 meters per tick initially). A request moves for **one tick only**, with no inertia. First valid request per rover per tick wins. A zero-speed request still consumes that tick's slot; invalid requests do not. An accepted order does not immediately change the snapshot.

## Write automation

Replace the starter with your own code, for example:

```python
from expedition import station, Vector2

enabled = True
rover = None

def startup():
    global rover
    rover = station.get_fleet()[0]

def update(dt):
    if enabled:
        rover.move(Vector2(1, 0), rover.max_speed)

def display_status():
    print(rover.id, rover.position)
```

Use `:restart` to load edits, then `:resume`. Restart uses a fresh Python process, including fresh imported modules, and leaves time paused. Accepted world commands are preserved.

Interpreter commands execute in the live globals of `main.py`:

```python
display_status()
enabled = False
rover.move(Vector2(0, 1), 1.5)
```

The assignment changes what the next update reads. Multiline functions work as in a normal Python prompt; finish a compound statement with a blank line. Documentation is supplied here and in API docstrings for IDE tooling.

## Session controls

| Command | Effect |
| --- | --- |
| `:pause` | Pause after any executing step finishes. |
| `:resume` | Resume at one simulated tick per second. |
| `:step` | Execute one update and one physical step while paused. |
| `:restart` | Replace player runtime, run startup, remain paused. |
| `:quit` | Save the world and stop both children, even if player code is stuck. |

Ctrl+C requests a pause and keeps session controls available. Ctrl+Q saves and quits the Textual interface. Use `:restart` to recover a stuck callback or interpreter submission. A lifecycle exception requires restart before resuming. Ordinary interpreter exceptions pause the world and preserve the live namespace.

These controls are handled by the supervisor, so they remain accessible when player code loops forever. Input and updates execute serially; typing an unfinished line does not block the simulation. Slow callbacks slow real-time execution without skipping simulation steps.

Script `print()` and tracebacks are forwarded to the output panel by the supervisor. Multiline Python uses the same continuation convention as before: enter each line, then an empty line to finish. The input placeholder indicates continuation. Output scrollback is bounded to 10,000 rendered lines. Completion, map rendering, and gameplay dashboards are not included.

## Develop game objects

Start with [Developing Game Objects](SpaceAutomation/Developing%20Game%20Objects.md).

Gameplay lives in `game/objects/`: real Python classes with their own state and methods. Mark public fields with `observed()` and player-callable methods with `@command`. Register new types in `game/catalog.py`, then generate the typed player API:

```bash
.venv/bin/python -m tools.generate_api
```

The socket handler, interpreter, and world loop do not need a branch for each new method. `game/scenario.py` controls initial equipment for new saves. The included solar panel is an extension example; the default scenario still has only two rovers.

Existing prototype saves are migrated in memory and preserve pending movement. Your player scripts retain `station.get_fleet()` and `rover.move(...)`.

## Validate

```bash
.venv/bin/python -m unittest discover -s tests -v
```

Integration tests start real processes, Unix sockets, and Linux pseudo-terminals. They need an environment that permits those facilities. Tests use temporary scripts/saves and do not modify your expedition.

Design notes live in the Obsidian folder [SpaceAutomation](SpaceAutomation/Technical%20Foundations.md). The prototype deliberately excludes energy, mining, research, and the first mission.
