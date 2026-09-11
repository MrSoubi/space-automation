---
title: Energy System
tags:
  - technical-design
  - energy
status: implemented-foundation
---

# Energy System

Related: [[Developing Game Objects]] · [[Technical Foundations]] · [[Game Design]]

## Object model

A rover owns an `EnergyPort` referencing three **real game objects** by ID:

```text
Rover "rover-1"
└── energy: EnergyPort
    ├── storage_id  → Battery "rover-1-battery"
    │                 └── storage: EnergyStorage(capacity, charge)
    ├── consumer_id → Motor "rover-1-motor"
    │                 └── consumer: EnergyConsumer(demand, received)
    └── source_id   → PortableSolarPanel "rover-1-solar-panel"
                      └── source: EnergySource(production_per_tick, energy_generated)
```

The Battery, Motor, and PortableSolarPanel have their own IDs, position, capabilities, and saved state. They can be inspected independently, removed, and replaced. `EnergyStorage`, `EnergyConsumer`, and `EnergySource` are capability records owned by those objects, not substitutes for the objects themselves.

Each component has its own port. A standalone battery references itself in `storage_id`. An installed component records its host in `installed_in`; the host and component references must agree. Installed parts travel with the rover.

## Files to edit

| File | Responsibility |
| --- | --- |
| `Game/Energy/EnergyPort.cs` | Connections and references to capability providers. |
| `Game/Energy/Capabilities.cs` | Capability records (`EnergySource`, `EnergyConsumer`, `EnergyStorage`) and capability interfaces (`IProducer`, `IConsumer`, `IStorage`, `IEnergyNode`, `IMobile`, `IComponentHost`). |
| `Game/Energy/EnergyEquipment.cs` | Installable equipment: port, `installed_in`, connection commands. |
| `Game/Energy/EnergySystem.cs` | Connectivity, validation, grouping, and allocation policy. |
| `Game/Objects/Battery.cs` | Battery object and storage validation. |
| `Game/Objects/Motor.cs` | Drive capability, speed, and energy cost. |
| `Game/Objects/SolarPanel.cs` | Stationary and portable panels. |
| `Game/Objects/Rover.cs` | Movement demand, powered movement, component replacement. |
| `Game/Scenario.cs` | Starter assemblies and the hub network. |

## Build equipment as a developer

```csharp
using SpaceAutomation.Game;
using SpaceAutomation.Game.Energy;
using SpaceAutomation.Game.Objects;

var world = new World([
    .. Scenario.RoverAssembly("rover-1"),
    new Battery { Id = "replacement", Storage = new() { Capacity = 40, Charge = 30 } },
]);
world.Validate();
```

The factory returns ordinary objects; the rover does not instantiate a hidden battery or manufacture parts during replacement. A bare `Rover` is allowed but has no installed components until configured; movement without a motor is rejected.

Objects can be added in any order because references are checked after assembly (`world.Validate()`, also run by save/load and every tick), not individually during insertion.

## Player commands

```bash
B=http://127.0.0.1:8377
curl $B/objects/rover-1                                # energy: {connections, source_id, consumer_id, storage_id}
curl $B/objects/rover-1-battery                        # charge, capacity, installed_in
```

The state is a projection of the live world — read it as often as you like; change it only through commands.

### Connect a star network

```bash
curl -X POST $B/objects/hub/connect -d '{"target":"battery-a"}'
curl -X POST $B/objects/hub/connect -d '{"target":"battery-b"}'
```

Every port supports multiple connections. Connections are undirected and reciprocal. Connected paths form grids; direct links from every pair are unnecessary. Removing a link splits the grid only if no other path remains. Cycles are valid and never duplicate stored energy or production.

```bash
curl -X POST $B/objects/rover-1/connect -d '{"target":"hub"}'
curl $B/objects/hub/grid_status     # {members, generation, demand, charge, capacity}
curl -X POST $B/objects/rover-1/disconnect -d '{"target":"hub"}'
```

`grid_status` reports members, generation per tick, current demand, charge, and capacity, computed from current authoritative state.

Installed components connect externally through their host — connect the rover, not its installed battery. This prevents a removed cable from leaving a hidden external battery link. Detached components can be linked independently.

The current foundation has no cable length, connection-count limit, cable cost, or transmission loss.

### Replace rover components

While the rover is at the component's position, unconnected, with no pending movement:

```bash
curl -X POST $B/objects/rover-1/replace_component -d '{"slot":"battery","component":"spare-battery"}'
curl -X POST $B/objects/rover-1/replace_component -d '{"slot":"motor","component":null}'   # null removes the part
```

Prototype replacement rules:

- The replacement must exist and have a compatible class.
- It must be uninstalled, externally disconnected, and at the rover's position.
- The rover must be externally disconnected with no pending movement.
- A battery or motor cannot belong to two rovers simultaneously.
- The old object remains at the rover's position, charge and state preserved.
- Replacement is immediate and free in this foundation; manufacturing and workshop restrictions remain future gameplay.

A rover accepts a Battery, a Motor, and a PortableSolarPanel (including subclasses). An ordinary stationary SolarPanel cannot be installed in its portable slot. Motor replacement affects effective max speed and movement energy cost.

## Tick phases

1. Game objects run `PrepareTick()` to declare work and demand.
2. `EnergySystem.Resolve()` allocates this tick's power once per connected grid.
3. Game objects run `Update()` to perform work with the energy received.
4. Simulation time advances.

Rover movement requests establish motor demand. A successful `move()` return admits the order; it does not promise sufficient energy. At resolution, full power allows motion; insufficient power leaves the rover in place, clears the order, and sets `last_move_result` to `not_enough_energy`. A rover with external energy links rejects movement as `connected_to_grid`; pending movement also prevents creating a new cable connection.

## Allocation policy and units

All energy quantities are joules; production and movement costs are per simulation tick. Velocity is meters per tick.

- Active sources produce once per tick.
- Consumers are considered in stable object-ID order.
- A consumer receives its full request or zero; an unmeetable request does not prevent a later smaller one.
- Generation supplies consumption first; batteries cover any deficit.
- Batteries discharge and charge in stable ID order.
- Surplus generation charges available capacity; remaining excess is discarded.
- Charge remains on each Battery, including when the grid splits or a part is removed.

There are no charge/discharge rate limits yet. A disconnected rover forms a grid with its installed parts: the portable panel slowly replenishes its battery. With no battery, only same-tick generation is available.

The default rover uses a 20 J battery, a 2 J movement cost, and 0.2 J/tick portable generation. These are tunable prototype values. The default hub has separate battery and panel objects connected in a star.

## Persistence

Capability records and ports are `[ValueType]` records handled by the generic save codec; equipment is registered by `[GameType]`. No energy-specific persistence code exists. Version-3 saves (rovers with a vestigial `installed_in`) upgrade to version 4 in memory on load; links, component identities, charge, ownership, and pending movement persist.

Scenario edits do not add hub/spares to an existing save. Try the complete starter setup with an unused path:

```bash
just run --paused --save /tmp/energy-demo.json
```
