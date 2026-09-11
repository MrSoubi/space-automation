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

C# (.NET 10) implements the simulation and the server. The **game is an HTTP API**: players write external programs in any language and any editor, talking to the simulation over localhost HTTP with JSON.

Reads are projected mechanically from the same `[Observed]` metadata the save system persists; commands are hand-written routes in `SpaceAutomation.Server/ServerApi.cs`. No schema, no generated proxies, no reflection-driven command layer outside the save/projection machinery.

There is no built-in documentation browser, map, or dashboard. Markdown documents provide concepts and examples; the API and the player's own tools are the inspection surface.

## Application architecture

One process, two roles. `./space-automation` (or `just run`) builds if needed and starts `SpaceAutomation.Server`, which owns:

- **The game thread** — the world, the clock, the command queue, and saving. The only thread that touches authoritative state.
- **Kestrel on 127.0.0.1** (default port 8377, `--port` to change) — HTTP handlers submit *calls* to the game thread and answer from the published snapshot. Request handlers never touch the world.

The old designs bracket this one. The first prototype (three processes, Unix sockets, generated Python clients) failed on mechanics. The single-process C# host (embedded Lua, instruction budgets, Terminal.Gui TUI) fixed the mechanics but locked players into a sandboxed runtime. The server keeps that host's world, persistence, and clock, and replaces the embedded runtime with an open surface — the first prototype's shape with boring, universal mechanics. A hung or slow client cannot stall the simulation, because the simulation never calls into player code: **isolation replaces the instruction budget**.

A tiny ops console (`:pause :resume :step :save :quit`) stays available in the server's own terminal when stdin is attached; clients can do the same over HTTP.

## Startup sequence

1. Load the last saved world, or create the starter scenario.
2. Bind Kestrel to localhost and print the address.
3. Begin ticking and accept HTTP requests.

Simulation time excludes time spent paused. There is no offline progress.

## The player surface

```
GET  /state                          → {tick, running, objects:[…]}   (published snapshot)
GET  /objects/{id}                   → one object's observed fields
GET  /objects/{id}/grid_status       → grid report
POST /objects/{id}/move              {direction:{x,y}, speed}
POST /objects/{id}/connect           {target}
POST /objects/{id}/disconnect        {target}
POST /objects/{id}/replace_component {slot, component|null}
POST /objects/{id}/set_enabled       {enabled}
POST /session/pause · resume · step · save
```

Every command returns `{"accepted":true}` or `{"accepted":false,"reason":"…"}` — the domain's own `CommandResult` values. Rejection reasons are part of the public API: stable and lowercase. Unknown ids answer `404 unknown_object`, unsupported commands `400 unsupported_object`, unparseable bodies `400 invalid_body`; JSON bodies are accepted regardless of the `Content-Type` header so plain `curl -d` works.

`/state` serves an immutable snapshot published after **every tick and every command** — the only world state request handlers ever read. Commands execute on the game thread through a request/response queue (a `TaskCompletionSource` completed by the game thread), so callers get their synchronous answer; worst-case latency while running is the time to the next tick boundary.

## Commands and observations

Player actions are POSTs validated by the simulation. Acceptance is synchronous; **completion happens over simulation time**. Reading a rover right after an accepted move still shows the old position — the move resolves in the tick's allocation phase.

The first accepted movement per rover per tick wins (`movement_already_requested`); slots reset when the tick commits. Paused commands reserve slots without advancing.

## Movement contract

Positions are continuous 2D meters. `max_speed` is meters per tick:

```text
position_next = position + normalized(direction) * min(speed, max_speed)
```

Direction is normalized, so its magnitude cannot extend travel. Requested speed above `max_speed` is capped, not rejected. Non-finite or negative speeds and zero or non-finite directions are invalid (`invalid_direction`, `invalid_speed`). Zero speed with a valid direction is an accepted no-op that still consumes the slot. No inertia, no multi-tick movement jobs: continuous travel means one request per tick. Destination navigation is player-written.

## Simulation time

One tick per wall-clock second while running; pause and single-step are first-class. A tick round is: drain queued calls, resolve systems (`PrepareTick` → energy → `Update`), advance the clock, autosave, publish state. The server never waits for a client — a client that is slow, paused in a debugger, or hung simply misses ticks. Player tools polling a few times per second keep up trivially.

## Persistence

The authoritative world autosaves every tick and on exit, atomically. Corrupt or unsupported saves are reported and preserved, never overwritten. Save format is versioned; upgrades happen in memory on load (`SaveMigrations`). Players are stateless: their programs can stop and restart freely, and everything that matters lives in the save.

## Extension boundary

Equipment classes live in `SpaceAutomation.Game/Objects/`, shared systems in `Energy/` (for energy) or wired through `World.Advance` (for new domains). Registration is `[GameType]`; persistence and the `/state` projection are generic over `[Observed]` fields — new equipment becomes queryable with no API-side edits; commands are hand-written routes. See [[Developing Game Objects]] for the complete recipes and a full walkthrough.

## Energy implementation

[[Energy System]] documents the capability records, real replaceable component objects, and the shared allocation phase. `PrepareTick()` declares demand, the energy system allocates once per connected grid, and `Update()` performs the work with the energy actually received.
