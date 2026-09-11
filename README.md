# Space Automation

A Linux programming sandbox. You remotely operate a planetary expedition: machines provide the capabilities, you write the behavior that coordinates them. Inspirations: *Screeps*, *The Farmer Was Replaced*, peaceful *Factorio*.

One C# process runs everything: the authoritative simulation, your Lua scripts, and a mouse-driven terminal UI.

## Requirements

- Linux, .NET 10 SDK, a terminal with mouse support.

## Run

```bash
./space-automation              # interactive TUI, one tick per second
./space-automation --paused     # start paused for inspection
```

The launcher builds if needed, then runs `backend/SpaceAutomation.Host`. Options:

| Option | Meaning |
| --- | --- |
| `--save path` | Save file (default `.space-automation/world-host.json`). |
| `--script path` | Player entry point (default `player/main.lua`). |
| `--budget n` | Lua instruction budget per `update()` (default 2000000). |
| `--paused` | Start paused. |
| `--plain` | Line-based console instead of the TUI (also automatic when streams are redirected). |

## The terminal

Buttons replace commands: **Pause/Resume**, **Step** (one tick while paused), **Restart** (reload `player/main.lua`), **Quit**. The same actions work as `:pause :resume :step :restart :quit` in the input line. `Ctrl+Q` quits.

Two tabs: **Console** (game output and your results) and **Objects** (live world table). The input line evaluates Lua in the same live namespace as `main.lua`; bare expressions echo their value; `↑`/`↓` browse history.

## Player scripting

The game calls `startup()` once after loading and `update(dt)` once per tick. Everything else — controllers, modules, scheduling — is yours to organize.

```lua
function startup()
    rover = station.get_fleet()[1]
end

function update(dt)
    rover:move(vector(1, 0), rover.max_speed / 2)
end
```

The interpreter shares this namespace, so you can inspect and override live:

```lua
return rover.position            -- {x=1.5, y=0}
enabled = false                  -- variables your update() reads
```

A runaway script (`while true do end`) is aborted at the instruction budget, the world is unharmed, and the session pauses until you fix the script and press Restart. Errors in `update()` pause and report the traceback; the interpreter stays available for inspection.

### API summary

```lua
station.get_tick()               -- completed ticks
station.get_fleet()              -- rovers, ordered by id
station.get_object(id)           -- proxy or nil
station.get_objects(type?)       -- all objects, optionally filtered ("battery", ...)
vector(x, y)                     -- position/direction value

rover.position                   -- {x=..., y=...}, read live
rover.max_speed, rover.battery.charge, rover.energy.storage_id, ...
rover:move(direction, speed)     -- returns {accepted=true} or {accepted=false, reason="..."}
rover:replace_component(slot, id_or_nil)
rover:connect(id) / rover:disconnect(id) / rover:grid_status()
panel:set_enabled(bool)
```

Command acceptance is synchronous; completion happens over simulation time. The first accepted movement per rover per tick wins; later requests are rejected as `movement_already_requested`.

## Persistence

The world autosaves every tick and on exit (atomic writes). Corrupt or unsupported saves are reported, never overwritten. Missing save files create the starter scenario; missing `player/main.lua` gets a minimal starter script (existing files are never touched).

## Project layout

| Path | Responsibility |
| --- | --- |
| `backend/SpaceAutomation.Game` | Authoritative domain: world, clock, rovers, energy system. No I/O. |
| `backend/SpaceAutomation.Persistence` | Save/load: `[GameType]`/`[Observed]`/`[Saved]` metadata, JSON codec, migrations. |
| `backend/SpaceAutomation.Host` | The application: Lua runtime (MoonSharp) with instruction budgets, game loop, Terminal.Gui TUI, plain console fallback. |
| `backend/SpaceAutomation.Tests` | Domain, persistence, Lua API and session tests. |
| `player/main.lua` | Your code. |
| `SpaceAutomation/` | Design notes and journals. |

Attributes exist only in the persistence layer; the player-facing Lua API in `SpaceAutomation.Host/LuaApi.cs` is hand-written.

## Tests

```bash
just test        # or: dotnet run --project backend/SpaceAutomation.Tests
```

## Development

Install [just](https://github.com/casey/just) once (`cargo install just`, or the installer script on that page), then:

```bash
just build    # build every project
just test     # run the test suite
just run      # launch the game; extra arguments pass through (just run --paused)
just clean    # remove build artifacts
```

The equivalent `dotnet` commands are in the `justfile`. To add equipment, systems, and gameplay rules, read [SpaceAutomation/Developing Game Objects.md](SpaceAutomation/Developing%20Game%20Objects.md).

## Save format history

Version 4 (current). Version-3 saves (rovers carrying a meaningless `installed_in`) are upgraded in memory on load.
