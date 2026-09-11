---
title: Technical Foundations
tags:
  - technical-design
status: working-draft
---

# Technical Foundations

Related: [[Game Design]] · [[First Mission]] · [[Developing Game Objects]] · [[Energy System]]

> [!abstract] Scope
> This page records the implemented technical direction. Detailed gameplay design lives in [[Game Design]]; the developer extension guide lives in [[Developing Game Objects]].

## Language and workspace

C# (.NET 10) is the implementation language of the simulation and application. Players write **Lua** scripts in a dedicated folder, using their preferred external editor.

The player-facing API is hand-written and explicit (`SpaceAutomation.Host/LuaApi.cs`); no schema, no generated wrappers, no reflection-driven command layer. Reflection appears only inside the save system, reading `[GameType]`/`[Observed]`/`[Saved]`/`[ValueType]` metadata.

There is no built-in documentation browser or in-game help. Markdown documents provide concepts and examples; the interpreter is the inspection tool.

## Application architecture

One process. `./space-automation` (or `just run`) builds if needed and starts `SpaceAutomation.Host`, which owns three threads:

| Thread | Responsibility |
| --- | --- |
| Game | The world, the clock, the Lua runtime, tick admission, saving. The only thread that touches authoritative state. |
| UI | Terminal.Gui: buttons, tabs, log, live object table. Reads published snapshots and drains an output queue on a 100 ms timer. |
| Input | Terminal line reading; lines are queued as commands for the game thread. |

The old three-process design existed for one reason: killing a hung player runtime. That guarantee is now provided in-process — every Lua invocation runs under an **instruction budget** (MoonSharp's per-instruction debugger hook), so a runaway `while true do end` is aborted deterministically, the world is unharmed, and the session pauses until an explicit restart. No Unix sockets, no wire protocol, no request identifiers; terminals submit commands through a plain queue (`GameSession`).

A plain line-based console replaces the TUI automatically when streams are redirected (`--plain` forces it), which keeps the game scriptable and testable.

## Startup sequence

1. Load the last saved world, or create the starter scenario.
2. Create `player/main.lua` if missing (never overwrite an existing one).
3. Load it and call `startup()` once; a load or startup failure latches the session paused until restart.
4. Begin ticking and accept interpreter input.

Simulation time excludes time spent paused. There is no offline progress.

## One player entry point

```lua
-- player/main.lua
function startup()
end

function update(dt)
end
```

`startup()` runs once per runtime load (also after `:restart`). `update(dt)` runs once per tick, before the physical step, and may issue commands. All other organization — controllers, modules, schedulers — is player code; the game attaches scripts to nothing and manages no registrations.

## Interpreter

Interpreter submissions execute in the **same global environment** as `main.lua` (MoonSharp `Globals`). Assignments affect the variables `update()` later reads; a player function called from the prompt submits commands normally. Bare expressions echo their value; `↑`/`↓` browse history.

An exception from `startup()` or `update()` reports a traceback and latches the session paused; already-accepted commands remain accepted, and the interpreter stays available for inspection. A REPL error only reports — a typo should not stop the world. Recovery requires an explicit restart, which reloads `main.lua` from disk, re-calls `startup()`, and stays paused.

## World commands and observations

Player actions are method calls through the station API, validated by the simulation:

```lua
local result = rover:move(vector(1, 0), 2)
-- {accepted=true}  or  {accepted=false, reason="invalid_speed"}
```

Acceptance is synchronous; **completion happens over simulation time**. Reading `rover.position` immediately after an accepted move still shows the old position — resolution happens in the tick's allocation phase.

The first accepted movement per rover per tick wins (`movement_already_requested`); slots reset when the tick commits. Paused commands reserve slots without advancing.

## Movement contract

Positions are continuous 2D meters; `vector(x, y)` builds values. `max_speed` is meters per tick:

```text
position_next = position + normalized(direction) * min(speed, max_speed)
```

Direction is normalized, so its magnitude cannot extend travel. Requested speed above `max_speed` is capped, not rejected. Non-finite or negative speeds and zero or non-finite directions are invalid (`invalid_direction`, `invalid_speed`). Zero speed with a valid direction is an accepted no-op that still consumes the slot. No inertia, no multi-tick movement jobs: continuous travel means one request per tick. Destination navigation is player-written.

## Simulation time

One tick per wall-clock second while running; pause and single-step are first-class. A tick round is: admit queued interpreter lines, run `update(dt)`, resolve systems (`PrepareTick` → energy → `Update`), advance the clock, autosave, publish state. Slow player code slows real time rather than stretching `dt` or skipping updates; the instruction budget aborts instead of hanging.

## Reload and persistence

The game manages the lifecycle of `main.lua` only; imported player modules and live variables are not reloaded piecemeal. `:restart` (or the Restart button) recreates the Lua runtime from disk.

The authoritative world autosaves every tick and on exit, atomically. Corrupt or unsupported saves are reported and preserved, never overwritten. Player variables are never persisted. Save format is versioned; upgrades happen in memory on load (`SaveMigrations`).

## Terminal

Terminal.Gui v2 provides a mouse-driven TUI: a Console tab (log, results), an Objects tab (live world table), a Lua input line, and buttons replacing commands (Pause/Resume, Step, Restart, Quit). `:pause :resume :step :restart :quit` remain accepted in the input line; `Ctrl+Q` quits.

## Gameplay extension boundary

Equipment classes live in `SpaceAutomation.Game/Objects/`, shared systems in `Energy/` (for energy) or wired through `World.Advance` (for new domains). Registration is `[GameType]`; persistence is generic over `[Observed]`/`[Saved]` fields; the player surface is hand-written in `LuaApi.cs`. See [[Developing Game Objects]] for the complete recipes (objects, systems, commands, capabilities) and a full walkthrough.

## Energy implementation

[[Energy System]] documents the capability records, real replaceable component objects, and the shared allocation phase. Object `PrepareTick()` declares demand, the energy system allocates once per connected grid, and `Update()` performs the work with the energy actually received.
