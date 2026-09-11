# Space Automation

A Linux programming sandbox. You remotely operate a planetary expedition: machines provide the capabilities, you write the behavior that coordinates them. Inspirations: *Screeps*, *The Farmer Was Replaced*, peaceful *Factorio*.

The game is an **HTTP API**: one C# server owns the authoritative simulation and serves it on localhost. Your automation — and any tools you build around it — is an external program in any language. The server ticks once per second and never waits for a client.

## Requirements

- Linux, .NET 10 SDK.
- Any HTTP client (curl, Python, JavaScript, Rust, ...).

## Run

```bash
./space-automation              # start the server on 127.0.0.1:8377
./space-automation --paused     # start paused for inspection
python3 player/main.py          # the reference player client
```

The launcher builds if needed, then runs `backend/SpaceAutomation.Server`. Options:

| Option | Meaning |
| --- | --- |
| `--save path` | Save file (default `.space-automation/world-host.json`). |
| `--port n` | Port to serve on (default 8377, localhost only). |
| `--paused` | Start paused. |

The server's own terminal accepts `:pause :resume :step :save :quit`; `Ctrl+C` saves and exits. Every client — including any monitor or map you build — is an equal HTTP peer.

## The API in one minute

```bash
B=http://127.0.0.1:8377

curl $B/state                                          # {tick, running, objects:[...]}
curl $B/objects/rover-1                                # one object's observed state
curl $B/objects/hub/grid_status                        # {members, generation, demand, charge, capacity}

curl -X POST $B/objects/rover-1/move \
     -d '{"direction":{"x":1,"y":0},"speed":2}'        # {"accepted":true}
curl -X POST $B/objects/rover-1/move \
     -d '{"direction":{"x":0,"y":0},"speed":1}'        # {"accepted":false,"reason":"invalid_direction"}

curl -X POST $B/objects/rover-1/connect -d '{"target":"hub"}'
curl -X POST $B/objects/rover-1/disconnect -d '{"target":"hub"}'
curl -X POST $B/objects/rover-1/replace_component -d '{"slot":"battery","component":"spare-battery"}'
curl -X POST $B/objects/rover-1-solar-panel/set_enabled -d '{"enabled":false}'

curl -X POST $B/session/pause -d '{}'
curl -X POST $B/session/resume -d '{}'
curl -X POST $B/session/step -d '{}'                   # one tick, only while paused
curl -X POST $B/session/save -d '{}'
```

No `Content-Type` header is needed; `curl -d` just works.

### Semantics

- Every command answers synchronously with `{"accepted":true}` or `{"accepted":false,"reason":"..."}` — **completion happens over simulation time**. Reading `rover-1` right after an accepted move still shows the old position; the move resolves at the next tick.
- Unknown object ids are `404 unknown_object`; commands the object does not support are `400 unsupported_object`; unparseable bodies are `400 invalid_body`. Well-formed but invalid gameplay values are normal rejections (`invalid_direction`, `invalid_speed`, ...).
- The first accepted movement per rover per tick wins (`movement_already_requested`); slots reset when the tick commits.
- `/state` reflects the world after every tick and every command. `objects` carries each object's `[Observed]` fields: `id`, `type`, `position`, `energy` (port: `connections`, `source_id`, `consumer_id`, `storage_id`), `charge`, `capacity`, `max_speed`, `enabled`, ... Filter the list client-side.
- A connected rover cannot move (`connected_to_grid`); insufficient energy leaves it in place and sets `last_move_result` to `not_enough_energy`.

### The player loop

```
read /state  ->  decide  ->  POST commands  ->  repeat
```

At one tick per second, a client that polls a few times per second never misses a tick — but if it does (slow code, breakpoints), the world moves on without it. There is no in-game runtime: your program is the runtime, in whatever language you like. `player/main.py` is a complete dependency-free example.

## Persistence

The world autosaves every tick and on exit (atomic writes). Corrupt or unsupported saves are reported, never overwritten. A missing save file creates the starter scenario. Restarts resume from the last tick.

## Project layout

| Path | Responsibility |
| --- | --- |
| `backend/SpaceAutomation.Game` | Authoritative domain: world, clock, rovers, energy system. No I/O. |
| `backend/SpaceAutomation.Persistence` | Save/load: `[GameType]`/`[Observed]`/`[Saved]` metadata, JSON codec, migrations. |
| `backend/SpaceAutomation.Server` | The application: game loop, call channel, HTTP API on localhost. |
| `backend/SpaceAutomation.Tests` | Domain, persistence, HTTP API and session tests. |
| `player/main.py` | Reference client; rewrite or replace in any language. |
| `SpaceAutomation/` | Design notes and journals. |

Attributes exist only in the persistence layer; reads are projected mechanically from `[Observed]` metadata, while commands are hand-written routes in `SpaceAutomation.Server/ServerApi.cs` — the same capability set for every client.

## Tests

```bash
just test        # or: dotnet run --project backend/SpaceAutomation.Tests
```

## Development

Install [just](https://github.com/casey/just#installation) once (`cargo install just`, or the installer script on that page), then:

```bash
just build    # build every project
just test     # run the test suite
just run      # start the server; extra arguments pass through (just run --paused)
just client   # run the reference client against a running server
just clean    # remove build artifacts
```

The equivalent `dotnet` commands are in the `justfile`. To add equipment, systems, and gameplay rules, read [SpaceAutomation/Developing Game Objects.md](SpaceAutomation/Developing%20Game%20Objects.md).

## Save format history

Version 4 (current). Version-3 saves (rovers carrying a meaningless `installed_in`) are upgraded in memory on load.
