---
title: First Prototype
tags:
  - technical-design
  - prototype
status: implemented-prototype
---

# First Prototype

Related: [[Technical Foundations]] · [[Game Design]] · [[First Mission]] · [[Developing Game Objects]]

> [!abstract] Goal
> Launch one Linux application that runs three communicating processes, executes a player's `main.py`, and provides a Python interpreter in that script's live namespace. Demonstrate that both automation and manual commands can inspect and control the same simulated vehicles.

> [!note] Scope
> This architecture prototype is implemented. It is not the first mission. The constants, API names, control commands, and wire format below are prototype choices that can evolve before the full game.

## Implementation and validation

Launch the implemented prototype with `./space-automation` from the project root. See [the usage guide](../README.md) for commands, automation examples, and session controls.

The simulation and player API use Python standard-library modules, with a project-owned immutable `Vector2` type exposed by `expedition`. The terminal supervisor uses Textual, installed from the project requirements file.

Automated tests cover object rules, generic API extension, save migration, and real three-process sessions using Linux pseudo-terminals. Run `.venv/bin/python -m unittest discover -s tests -v` in an environment that permits Unix sockets and pseudo-terminals.

The default interactive terminal provides a Textual output panel, bottom command field with session history, and a small status bar showing only the session name and current tick. Input remains separate from background output, including while player code hangs. `--plain` retains basic line input for compatibility; redirected streams use it automatically. Completion is not implemented.

## Deliverable

From the project directory, the player runs:

```bash
./space-automation
```

The application loads its saved world, initializes player code, begins simulation, and presents a Python prompt. On first launch it creates a small default world and supplies a minimal `player/main.py` if one is missing. It must never overwrite an existing player script.

The player can edit scripts externally, inspect rover state, call their own functions, change live variables, pause, step, and exit with a save. No graphical or terminal map is included.

## Three processes

| Process | Owns | Does not execute |
| --- | --- | --- |
| Terminal supervisor | Terminal input/output, child lifecycles, session controls, runtime recovery. | Player Python code. |
| Player runtime | `main.py` module namespace, imports, API proxies, interpreter evaluation, lifecycle callbacks. | Authoritative world updates. |
| Simulation | World state, movement validation, tick admission slots, clock, save file. | Player scripts. |

Use Unix domain sockets and length-prefixed UTF-8 JSON messages. Keep connections private to the launched session. Socket paths, if used, belong in a session-specific directory and are cleaned up on exit. No D-Bus service or network listener is required.

The supervisor communicates with both children; the runtime communicates directly with the simulation. Each request has a unique identifier and each response identifies its request. Include a protocol version in the initial handshake, and a tick identifier on world commands. Reject stale tick requests explicitly.

Only the supervisor writes to the terminal. Runtime output, expression results, and tracebacks are forwarded to it. Simulation diagnostics use the same route. Input buffering must allow ticking while the user types; complete Python submissions are queued for execution.

## Minimal world

- Two rovers, `rover-1` and `rover-2`, starting at `Vector2(0.0, 0.0)` and `Vector2(5.0, 0.0)`.
- An unbounded, obstacle-free continuous 2D plane, with positions in meters.
- Positive `x` points east; positive `y` points north. Arbitrary directions are allowed.
- `max_speed = 3.0` meters per tick for both rovers.
- Fixed simulation step: one simulated second.
- Normal execution target: one tick per wall-clock second, without catch-up bursts after slow code.

Each accepted `move(direction, speed)` applies displacement once, at the end of the current tick. Normalize direction and cap speed to `max_speed`:

```text
position_next = position + normalized(direction) * min(speed, max_speed)
```

For example, `Vector2(3.0, 4.0)` at speed `2.0` moves by `(1.2, 1.6)` meters. Direction magnitude must not affect speed. Fractional coordinates are preserved without snapping to cells.

No accepted request means no movement for that tick. There is no inertia, persistent velocity, or multi-tick movement job; continuous travel requires a request each tick. Because speed is measured in meters per tick, do not multiply displacement by `dt` in seconds.

Two rovers may occupy the same position. There is no grid, collision, terrain, energy, inventory, resource collection, research, or manufacturing in this prototype.

## Player entry point

The game provides this minimal file:

```python
# player/main.py
from expedition import station

def startup():
    pass

def update(dt):
    pass
```

Both callbacks must exist and be callable. Missing or invalid callbacks produce a clear initialization error and leave the session paused.

Load this as a real module and use its dictionary as the shared globals/locals for interpreter execution. Do not copy its namespace. Calls to `startup()` and `update()` use that same module.

Provide Python expression evaluation, statements, and multiline definitions. Evaluate a bare expression with normal interactive display behavior. Do not use `eval()` alone as the interpreter implementation.

Only the game callbacks are automatic. Player controller classes, additional modules, event systems, and controller update order are unrestricted player code.

## Minimal API

The prototype package is named `expedition`. Supply type annotations and docstrings, plus a short Markdown usage guide. No in-game help command is required.

| Member | Meaning |
| --- | --- |
| `station.tick` | Number of completed simulation steps in the current observation. |
| `station.get_fleet()` | Stable list of rover proxies ordered by identifier. |
| `rover.id` | Stable string identifier. |
| `Vector2(x, y)` | Immutable 2D value with numeric `x` and `y` components, imported from `expedition`. |
| `rover.position` | Observed `Vector2` position in meters. |
| `rover.max_speed` | Maximum speed in meters per tick. |
| `rover.move(direction, speed)` | Submit one tick of movement and synchronously return a `CommandResult`. |
| `CommandResult.accepted` | Boolean indicating acceptance of the order. |
| `CommandResult.reason` | `None` on success, otherwise a rejection code. |

Observation properties are read-only views of the latest snapshot. API proxy identity stays stable as snapshots refresh, so a rover stored during startup remains usable. Changing a local observation must not mutate authoritative state.

Commands are validated in the simulation. A response confirms admission, not completed movement. Reading `position` again in the same execution phase still reads the snapshot; command responses provide immediate admission feedback.

For structurally valid requests targeting an existing rover, apply these checks in order:

1. Current tick identifier.
2. Direction must be a `Vector2` with finite real components and nonzero magnitude. Speed must be a finite nonnegative real number. Booleans are not accepted as numeric inputs.
3. If the rover's movement slot is already consumed, reject as `movement_already_requested`.
4. Otherwise normalize direction, cap speed to `max_speed`, accept the request, and consume the slot.

Use `invalid_direction`, `invalid_speed`, and `stale_tick` for corresponding rejections. Excessive finite speed is capped, not rejected. Zero speed with a valid direction is accepted and consumes the slot without displacement. Invalid requests consume no slot. There is no `vehicle_busy` rejection for movement in this prototype.

Malformed protocol messages are protocol errors, not gameplay orders. Serialize vectors as explicit numeric `x`/`y` data; reconstruct immutable values in the runtime. No third-party vector library is required for the prototype.

There is no `move_to()`, automatic route planner, queue of future movements, or player controller framework. A generic status field is deferred until meaningful machine states are introduced.

## Tick and interpreter scheduling

Treat the next step as an open command-admission round with a fixed tick identifier. At the beginning of that round, distribute the current snapshot. Admission slots reset only when the previous step has committed, not after each interpreter submission.

For a running step:

1. Freeze the queue of complete interpreter submissions admitted to this round.
2. Execute those submissions serially in arrival order.
3. Call `main.update(1.0)` once.
4. After successful completion, advance the physical world by one step.
5. Increment the completed tick count, publish the new snapshot, and open the next round.

Input arriving after the queue boundary waits for the next round. No interpreter code runs simultaneously with `update()`. The simulation continues servicing command requests while waiting for player execution to finish, so synchronous API requests cannot deadlock against the tick barrier.

`startup()` runs before the first update, within the initial admission round. Its movement commands therefore share the first round's slots with interpreter commands and the first update.

### Paused behavior

While paused, execute interpreter submissions serially against the current snapshot and open admission round. Accepted commands reserve slots but do not advance physical movement. Repeated submissions do not create new rounds.

A step executes one update and commits one physical step, then remains paused. Resume continues the existing round; it does not discard accepted commands or reset slots. A pause requested during execution takes effect at the next safe boundary.

This makes conflicts reproducible: pause, submit two movements for one rover, and inspect both outcomes before advancing time.

## Supervisor controls

Provide a minimal set of session controls recognized by the terminal supervisor before Python evaluation. These are prototype lifecycle controls, not gameplay conveniences:

| Control | Behavior |
| --- | --- |
| `:pause` | Pause at a safe boundary. |
| `:resume` | Resume normal ticking if the runtime is healthy. |
| `:step` | Run one step while paused. |
| `:restart` | Stop the runtime, preserve world state, create a fresh runtime, reload player modules, call startup, and remain paused. |
| `:quit` | Stop execution safely, save authoritative state, close connections, and reap both children. |

A `--paused` launch option supports reproducible inspection and acceptance tests; normal launch runs automatically after successful initialization.

Supervisor controls must remain available while arbitrary player Python is blocked. They must not depend on executing a Python helper inside that runtime. Runtime restart is explicit and loses all unsaved player Python variables.

## Failures and recovery

An exception from player code produces a traceback and pauses progression. Keep the live namespace available for inspection if execution returned normally through the exception boundary. Do not automatically repeat a failed update: it may have already submitted commands or changed player variables.

For the prototype, require `:restart` after a failed lifecycle callback before resuming ticks. Successful commands submitted before failure remain accepted. Restart does not reset their admission slots or cancel already accepted movement requests. A new startup command may therefore be rejected, which is consistent with first-valid-command priority.

If player code hangs, the supervisor can terminate and replace the runtime while the simulation remains paused at the barrier. Do not claim that threads or asynchronous tasks alone make arbitrary code interruptible. On runtime socket loss, the simulation retains already accepted commands and does not advance the unfinished step.

If the simulation process fails or its connection is lost, stop runtime execution and report the failure. Do not continue against stale observations. On shutdown, use bounded waits and reap child processes; an unresponsive runtime must not prevent quitting.

## Persistence

Use one versioned local save file. Save completed tick count, rover positions and capabilities, and any accepted movement orders and consumed slots for the current unfinished round. This preserves first-command semantics across an exit while paused or a runtime failure.

Write saves atomically so an interrupted write does not replace a valid save with a partial document. Normal quit saves; periodic autosave is outside this prototype. A malformed or unsupported save produces an explicit error and is not silently overwritten.

Relaunch restores this world, creates a fresh runtime, and calls startup again. There is no offline advancement, automatic persistence of arbitrary Python variables, or restoration of call stacks.

## Example player-written automation

This belongs in the usage guide as an optional example, not in the mandatory starter file:

```python
from expedition import Vector2, station

enabled = True
rover = None

def startup():
    global rover
    rover = station.get_fleet()[0]

def update(dt):
    if enabled:
        rover.move(Vector2(1.0, 0.0), rover.max_speed)

def display_status():
    print(rover.id, rover.position)
```

The interpreter can call `display_status()`, assign `enabled = False`, and issue `rover.move(...)`. The assignment changes the global subsequently read by update. Disabling automation does not cancel a movement already accepted.

## Acceptance checks

- [x] One launcher starts the three application processes; only the supervisor owns terminal output.
- [x] Startup runs once per runtime launch; update runs once per committed normal step.
- [x] Typing an incomplete line does not stop updates.
- [x] Interpreter assignments and player-defined method calls share the real main-module namespace.
- [x] The API works from both startup/update and interpreter submissions.
- [x] A non-axis-aligned direction produces the expected fractional displacement without grid snapping.
- [x] Direction normalization prevents vector magnitude from increasing speed.
- [x] Speed above `max_speed` is capped; negative/non-finite speed and invalid directions are rejected.
- [x] Zero speed consumes the slot without displacement.
- [x] A movement request affects only its tick; a following tick without a request leaves position unchanged.
- [x] Two valid same-round calls for one rover accept only the first, including a manual call followed by update.
- [x] Invalid input does not consume a slot; different rovers have independent slots.
- [x] Paused commands do not move rovers or reset admission slots; stepping advances exactly one step.
- [x] Exceptions report a traceback and pause without losing authoritative state.
- [x] An intentional infinite loop can be recovered through supervisor restart, and quitting still works.
- [x] Quit/relaunch restores positions, capabilities, time, and unfinished-round admission state.
- [x] No child processes or session socket paths are left after normal exit.

Use focused integration tests for process communication, tick ordering, namespace identity, and save/resume. Use a real terminal session to verify multiline input, output, and recovery responsiveness.

## Deferred work

The prototype does not implement [[First Mission]], world generation, energy, research, production, exploration progress, hot reload of individual modules, persistent player-object graphs, multiplayer, or independent background operation after quitting the launcher.

After this prototype, use its results to write the detailed simulation, interpreter, API, runtime, and communication pages listed in [[Technical Foundations]].
