---
title: Developing Game Objects
tags:
  - technical-design
  - development
status: implemented
---

# Developing Game Objects

Related: [[Technical Foundations]] · [[First Prototype]]

> [!abstract] Start here
> Write gameplay in `game/objects/`. The engine handles transport, interpreter execution, object discovery, and persistence. A game object is a Python dataclass with fields, ordinary methods, and a simulation update method.

## Where to work

| File or folder | Your responsibility |
| --- | --- |
| `game/objects/rover.py` | Rover capabilities, movement validation, pending orders, and motion. |
| `game/objects/solar_panel.py` | A small second-object example: enable/disable and energy generation. |
| `game/catalog.py` | Register each equipment type once under a stable identifier. |
| `game/scenario.py` | Choose objects for a new expedition. Existing saves are unaffected. |
| `player/main.py` | Player automation, not game implementation. |
| `expedition/generated.py` | Generated typed player wrappers. Do not maintain these by hand. |
| `space_automation/engine/` | Shared object registration, encoding, command dispatch, and storage. |
| `space_automation/terminal.py`, `tui.py`, `runtime.py`, `protocol.py` | Application plumbing; adding equipment does not require editing these. |

The authoritative `Rover` class lives in `game/objects/rover.py`. The generated player-side `Rover` has the same documented public shape but forwards calls to that real object. There are two processes, so these cannot be the same Python instance. Only the authoritative class contains the gameplay implementation.

## Define an object

The included solar panel illustrates the intended extension path. Its essential structure is:

```python
from dataclasses import dataclass
from space_automation.engine import GameObject, command, observed
from space_automation.types import CommandResult

@dataclass
class SolarPanel(GameObject):
    enabled: bool = observed(default=True, doc="Whether generation is enabled.")
    energy_per_tick: float = observed(default=1.0, doc="Joules generated per tick.")
    energy_generated: float = observed(default=0.0, doc="Total joules generated.")

    @command
    def set_enabled(self, enabled: bool) -> CommandResult:
        """Enable or disable this panel."""
        if type(enabled) is not bool:
            return CommandResult(False, "invalid_enabled")
        self.enabled = enabled
        return CommandResult(True)

    def update(self, dt: float) -> None:
        if self.enabled:
            self.energy_generated += self.energy_per_tick
```

The complete source also validates restored state. The panel is registered as an example but is not spawned in the initial two-rover scenario. Its generated energy is not yet connected to a grid.

### Fields

Use `observed()` for a persisted field players may read. Supply a type annotation and a short documentation string. Generated properties are read-only snapshots, not setters on the simulation object.

Use an ordinary dataclass field for internal persisted state. For example, the rover's `_movement` stores its accepted displacement for the unfinished tick. Players cannot query that field through the public API.

Mutable defaults use `observed(default_factory=list, ...)` or normal `dataclasses.field(default_factory=...)`.

All dataclass fields are saved automatically. Currently supported values are JSON scalars, lists, string-keyed dictionaries, `Vector2`, and `CommandResult`. Use identifiers to store relationships between objects, rather than storing live Python object references. Tuple values round-trip as lists. Non-finite numbers are representable for command validation, but should be rejected by gameplay state validation.

### Commands

Mark a method with `@command` to make it callable by players. Keep its annotations and docstring: these become the generated IDE-facing API.

The engine checks the target object, current tick, exposed method name, and argument binding. The object's method checks gameplay conditions and decides what to change or reject. Unmarked methods such as `update()` and `validate_state()` are not callable through the command dispatcher.

A command can return a `CommandResult` or another supported value. Returning a rejection is an ordinary gameplay outcome; unexpected exceptions become runtime errors for inspection. There is no automatic transaction rollback: validate before mutating if a rejected command must have no side effects.

The once-per-tick movement rule belongs to `Rover.move()`. It is not imposed on all methods or all equipment. The panel's `set_enabled()` can be called multiple times in a tick.

### Simulation updates and interactions

The world calls every object's `update(dt)` once per physical step, in stable id order. This is an engine lifecycle for game implementation. It is separate from the player's single `main.update(dt)` entry point.

Objects added to a world can access `self.world.tick` and resolve another simulation object through `self.world.objects[identifier]`. This context is not a dataclass field, is not saved, and is never exposed to the player. Prefer explicit object ids for persisted references.

Object updates are currently sequential, so direct interactions can depend on update order. More advanced shared-system phases can be designed later; the prototype does not provide an energy-grid scheduler.

### State validation

Override `validate_state()`, call `super().validate_state()`, and reject invalid state with `ValueError`. This runs when adding or restoring an object. Examples include a negative speed capability, an invalid position type, or a pending movement exceeding the rover's capability.

New saved fields with defaults can be absent in older saves. Renaming a field or changing its meaning requires a save migration; unknown saved fields are rejected rather than silently discarded.

## Register and expose the type

Add the class to `game/catalog.py`:

```python
from game.objects.solar_panel import SolarPanel

OBJECT_TYPES = {
    # Keep existing registered types here too.
    "solar_panel": SolarPanel,
}
```

Registration keys are stable save identifiers. Then regenerate the player API:

```bash
.venv/bin/python -m tools.generate_api
```

This writes typed wrappers and exports under `expedition/`. Commit the generated files with the gameplay change. After adding a new exposed method or observed field, run this command again. You do not edit a socket handler, add a packet type, or hand-write a second method implementation.

Check that generated files match definitions with:

```bash
.venv/bin/python -m tools.generate_api --check
```

Use the shared types and Python built-in annotations in exposed signatures. Adding new custom wire value types is a separate engine extension; the generator does not infer arbitrary Python serialization.

## Place equipment in a new world

Edit `game/scenario.py`, import the new object, and add an instance to `initial_objects()`:

```python
SolarPanel(id="panel-1", energy_per_tick=2.5)
```

Start with a separate, unused save path to try the changed scenario without replacing your expedition:

```bash
./space-automation --paused --save /tmp/panel-experiment.json
```

If that file already exists, it is restored instead of rebuilding the scenario. Scenario edits intentionally do not inject equipment into existing saves.

## Use it as a player

```python
from expedition import SolarPanel, station

panel = station.get_objects(SolarPanel)[0]
print(panel.energy_generated)
result = panel.set_enabled(False)
```

`station.get_objects()` lists all exposed objects; passing a generated class filters the list. `station.get_object("panel-1")` looks up a specific id. `station.get_fleet()` remains a typed compatibility shortcut for rovers.

The same calls work from the interpreter and `player/main.py`. Observation properties refresh at execution boundaries, while command return values give immediate feedback. No knowledge of sockets is needed to use or implement `set_enabled()`.

## Test gameplay without launching the application

```python
from game.objects.rover import Rover
from space_automation.types import Vector2

rover = Rover(id="test-rover")
assert rover.move(Vector2(3, 4), 2).accepted
rover.update(1.0)
assert rover.position == Vector2(1.2, 1.6)
```

Use ordinary unit tests for equipment rules. Integration tests separately cover generated proxies, generic dispatch, persistence, actual process communication, and the terminal.

## Existing saves

The engine reads original version-1 rover saves and converts them in memory to the generic version-2 object format. Positions, tick count, capabilities, and unfinished movement slots are preserved. The next normal save writes version 2. Loading does not rewrite the save file.

The legacy rover adapter is deliberately isolated in the persistence module. It is historical compatibility code, not a template for adding future equipment.
