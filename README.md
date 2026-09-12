# Space Automation

A Linux programming sandbox. You remotely operate a planetary expedition: machines provide the capabilities, you write the behavior that coordinates them. Inspirations: *Screeps*, *The Farmer Was Replaced*, peaceful *Factorio*.

The game is an **HTTP API**: one C# server owns the authoritative simulation and serves it on localhost. Your automation — and any tools you build around it — is an external program in any language. The server ticks once per second and never waits for a client.

## Requirements

- Linux desktop, .NET 10 SDK, and SDL2 (`sudo apt install libsdl2-2.0-0` on Debian/Ubuntu).
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
| `--save path` | Save file (default `~/.space-automation/world-host.json`, shared by every launch method). |
| `--port n` | Port to serve on (default 8377, localhost only). |
| `--paused` | Start paused. |

The server opens a draggable, borderless grey window at 82% opacity, containing only a flowing particle field. Blue particles move normally while the simulation runs; red particles drift at 4.5% speed while paused. Window transparency requires compositor support; otherwise the background stays opaque.

Drag anywhere to move the window. **Space** pauses/resumes; **Escape** or **Alt+F4** saves and closes the window and server. `Ctrl+C` also saves and exits. There is no interactive terminal. Session controls remain available over HTTP, and every client is an equal HTTP peer.

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

curl -X POST $B/objects/scanner-1/scan                 # survey scan, takes a few ticks
curl -X POST $B/objects/rover-1/collect -d '{"target":"mineral-1"}'
curl -X POST $B/objects/rover-1/deliver -d '{"sample":"sample-rover-1-1","target":"facility-1"}'
curl -X POST $B/objects/facility-1/analyze             # consumes the sample, reveals its type

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
- `/state` reflects the world after every tick and every command. `objects` carries each object's `[Observed]` fields: `id`, `type`, `position`, `speed_limit`, `stored`, `used_volume`, `used_weight`, `volume`, `identified`, ... Filter the list client-side; undiscovered minerals are absent until a scan reveals them.
- A scan finds mineral *nodes* — a location, nothing more. Collecting requires standing on the node and free cargo space (`out_of_reach`, `empty`, `inventory_full`, `overloaded`): each scoop takes a fixed volume (1 L) and weighs its volume times the mineral's density. Cargo holds at most 5 L and 10 kg; `stored` lists samples with `volume` and measured `weight`.
- A sample's mineral definition is hidden until one sample of that type is delivered to the analysis facility and analyzed — which consumes it and reveals the type for every node and sample of that kind, forever.

### The player loop

```
read /state  ->  decide  ->  POST commands  ->  repeat
```

At one tick per second, a client that polls a few times per second never misses a tick — but if it does (slow code, breakpoints), the world moves on without it. There is no in-game runtime: your program is the runtime, in whatever language you like. The Python side ships `player/api.py`, a small dependency-free client library — `Game`, a `ticks()` loop, live object views — and `player/main.py`, a behavior example built on it.

## Persistence

The world autosaves every tick and on exit (atomic writes). A missing save file creates the starter scenario; a save this version cannot read (an older world model, a broken file) is kept aside as `<save>.broken-<timestamp>` and a fresh expedition starts, with a message explaining what happened. Restarts resume from the last tick.

## Project layout

| Path | Responsibility |
| --- | --- |
| `backend/SpaceAutomation.Game` | Authoritative domain: world, clock, rovers, energy system. No I/O. |
| `backend/SpaceAutomation.Persistence` | Save/load: `[GameType]`/`[Observed]`/`[Saved]` metadata, JSON codec, migrations. |
| `backend/SpaceAutomation.Server` | The application: game loop, call channel, HTTP API on localhost. |
| `player/api.py` + `player/main.py` | The Python starter: a small client library and an example behavior on top of it. Rewrite or replace in any language. |
| `SpaceAutomation/` | Design notes and journals. |

Attributes exist only in the persistence layer; reads are projected mechanically from `[Observed]` metadata, while commands are hand-written routes in `SpaceAutomation.Server/ServerApi.cs` — the same capability set for every client.

## Development

Install [just](https://github.com/casey/just#installation) once (`cargo install just`, or the installer script on that page), then:

```bash
just build    # build every project
just run      # open the particle window and start the server; extra arguments pass through (just run --paused)
just client   # run the reference client against a running server
just clean    # remove build artifacts
```

The equivalent `dotnet` commands are in the `justfile`. To add equipment, systems, and gameplay rules, read [SpaceAutomation/Developing Game Objects.md](SpaceAutomation/Developing%20Game%20Objects.md).

## Save format history

Version 5 (current): mineral nodes carry litres, rovers carry sample records, and the save tracks which mineral types the expedition has analyzed. Version 4 saves (integer units, cargo as node ids) and version-3 saves (rovers carrying a meaningless `installed_in`) are upgraded in memory on load.
