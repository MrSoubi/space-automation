---
title: Developing Game Objects
tags:
  - technical-design
  - development
status: implemented
---

# Developing Game Objects

Related: [[Technical Foundations]] · [[Energy System]] · [[First Mission]]

> [!abstract] Start here
> Gameplay lives in `backend/SpaceAutomation.Game`. A game object is a plain C# class: fields, ordinary methods, and two lifecycle hooks. The persistence layer saves it automatically; the server's projection makes it readable over HTTP with zero API code. No code generation, no socket handlers.

## Where to work

| File or folder | Your responsibility |
| --- | --- |
| `backend/SpaceAutomation.Game/Objects/` | One file per equipment type. |
| `backend/SpaceAutomation.Game/Energy/` | Ports, capability records, capability interfaces, `EnergySystem`. |
| `backend/SpaceAutomation.Game/World.cs` | Object collection, clock, tick phases, system wiring. |
| `backend/SpaceAutomation.Game/Scenario.cs` | Objects present in a new expedition (existing saves are unaffected). |
| `backend/SpaceAutomation.Server/ServerApi.cs` | Hand-written command routes; reads need nothing here. |
| `backend/SpaceAutomation.Tests/Program.cs` | Equipment rules, save round-trips, HTTP surface. |
| `player/main.py` | Reference client, never game implementation. |

## Define an object

`Battery` is the smallest complete example:

```csharp
[GameType("battery")]
public class Battery : EnergyEquipment, IStorage
{
    [Observed("storage", "Battery capacity and charge in joules.")]
    public EnergyStorage Storage { get; set; } = new();

    internal override void InitializePort() => Energy.StorageId ??= Id;

    public override void ValidateState()
    {
        base.ValidateState();
        Rules.Require(Storage is not null, "Storage required");
        Storage!.Validate();
        Rules.Require(Energy.StorageId == Id, "Battery port must reference its own storage");
    }
}
```

Rules of the system:

- **`[GameType("battery")]`** is the stable save identifier. Types declared in the Game assembly are discovered automatically; types in other assemblies (like test fixtures) must be registered manually in `Model.Objects`.
- **`[Observed("name", "doc")]`** marks a persisted, player-readable property. Computed read-only properties use `Persist = false` (see `Rover.MaxSpeed`).
- **`[Saved("name")]`** marks persisted-but-private state, like the rover's `_movement`.
- Store **object IDs, never object references** — saves are JSON, and links are re-resolved through the world.
- Persisted values may be JSON scalars, `List<>`, `Vector2`, and `[ValueType]` records (`EnergyPort`, `EnergyStorage`, ...). Define new record shapes with `[ValueType]` in `Energy/Capabilities.cs`.
- Renaming a persisted field or changing its meaning requires a **save migration** in `SaveMigrations.cs`; unknown fields in a save are rejected, never silently dropped.
- Override **`ValidateState()`**, call `base.ValidateState()`, and reject bad state with `Rules.Require(condition, "message")`. It runs when objects are added, restored from a save, and every tick through the energy system — keep it cheap and side-effect free.

### Tick lifecycle

`World.Advance()` runs one tick in three phases:

1. Every object's `PrepareTick()` — declare intentions (the rover sets motor demand).
2. Shared systems — `EnergySystem.Resolve()` allocates power per connected grid.
3. Every object's `Update()` — act on what was allocated (the rover moves or reports `not_enough_energy`).

Objects run in stable ID order. Declare in `PrepareTick`, act in `Update`; never mutate neighbors directly — go through a system or a command.

## Expose it to players

Reads and writes travel different roads, by design.

**Reads are free.** `GET /state` and `GET /objects/{id}` are projected mechanically from `[Observed]` declarations — the same metadata the save codec persists. Mark a property `[Observed]` (with `Persist = false` for computed values like `charge` or `max_speed`) and it appears in every client's next poll. There is no projection code to write and nothing can drift.

**Commands are hand-written routes** in `SpaceAutomation.Server/ServerApi.cs`. Each route binds a small request record, validates the shape, and calls the domain method through the session's call channel — the domain answers with the `CommandResult` the client sees:

```csharp
app.MapPost("/objects/{id}/deposit", (string id, DepositRequest? body) =>
    WithObject<CargoStorage>(session, id, async storage =>
    {
        if (body is null)
            return Results.Json(CommandResult.Reject("invalid_body"), Json, statusCode: 400);

        if (body.Amount is not { } amount)
            return Results.Json(CommandResult.Reject("invalid_amount"), Json);

        return Results.Json(await session.Call(_ => storage.Deposit(amount)), Json);
    }));

public sealed record DepositRequest(double? Amount)
{
    public static async ValueTask<DepositRequest?> BindAsync(HttpContext context)
    {
        try { return await JsonSerializer.DeserializeAsync<DepositRequest>(context.Request.Body, ServerApi.Json); }
        catch (JsonException) { return null; }
    }
}
```

`WithObject<T>` resolves the target on the game thread and answers `404 unknown_object` / `400 unsupported_object` before your route runs. Null check the body (`BindAsync` returns null for unparseable JSON), check each field for absence, and turn both into the same rejection reasons the domain uses — `invalid_amount`, not a framework error. Domain rules (ranges, finiteness, slot conflicts) stay in the domain class; the route only translates shapes.

## Place it in a new world

Edit `Scenario.Create()`:

```csharp
new CargoStorage { Id = "hub-storage", Capacity = 200 },
```

Scenario changes only affect worlds created fresh; existing saves restore their own object list. Try a changed scenario against a throwaway save: `just run --paused --save /tmp/experiment.json`.

## Add a shared system

A **system** is a cross-object phase that no single object can own — energy allocation is the reference implementation (`EnergySystem`). The recipe:

1. A sealed class taking the `World` (`public sealed class MySystem(World world)`).
2. Discover participants **by interface**, never by class name — `world.Objects.Values.OfType<ISomething>()`. This is why `IProducer`/`IConsumer`/`IStorage`/`IEnergyNode` exist: the grid serves any present or future equipment that declares a capability.
3. A `Resolve()` that computes the shared phase, and a `Validate()` that checks invariants across objects.
4. Wire it into `World`: create it in the constructor, call `Resolve()` in `Advance()` between `PrepareTick()` and `Update()`, call `Validate()` from `World.Validate()`.

For example, the planned sample analysis could become `AnalysisSystem`: facilities declare `IAnalysisFacility`, samples declare `IAnalyzable`, and the system resolves queues and research credits in one phase. Resist putting such coordination inside a single object — two facilities sharing one input stream is exactly the case objects cannot decide alone.

## Add gameplay rules

- **Commands are ordinary public methods returning `CommandResult`** — `Ok()` or `Reject("reason")`. Rejection reasons are part of the player API; keep them stable and lowercase.
- **Validate before mutating.** There is no rollback: a rejected command must leave no side effects.
- **Per-tick admission slots** follow the rover pattern: a nullable field (`PendingMovement`) is the slot; the first accepted command consumes it (`movement_already_requested`); `Update()` clears it. Anything else may be called repeatedly within a tick.
- **Energy-consuming work** implements a capability interface (`IConsumer`, `IProducer`, `IStorage`), sets demand in `PrepareTick()`, and checks `Received` in `Update()`. The grid handles allocation; the object never reaches into another object's storage.
- **Host-installed components** (battery, motor, panel) live under `EnergyEquipment` and record their host in `InstalledIn`. Hosts like the rover implement `IEnergyNode` + `IComponentHost`.

## Test it

Add a case to `backend/SpaceAutomation.Tests/Program.cs` using the local helpers:

```csharp
Test("cargo storage deposits, withdraws and validates", () => {
    var storage = new CargoStorage { Id = "s" };
    var world = new World([storage]);
    Check(storage.Deposit(50).Accepted);
    Check(storage.Withdraw(80).Reason == "insufficient_contents");
    Check(storage.Withdraw(30).Accepted);
    Near(storage.Contents, 20);
    var restored = WorldStore.Deserialize(WorldStore.Serialize(world));
    Near(restored.Find<CargoStorage>("s")!.Contents, 20);
});
```

Cover the rules, the rejections, and the save round-trip. Then `just test`. Use `just run --paused --save /tmp/...` to try it live, from any shell:

```bash
curl -X POST localhost:8377/objects/hub-storage/deposit -d '{"amount": 10}'
curl localhost:8377/objects/hub-storage          # {"id":"hub-storage","contents":10,...}
```

## Complete walkthrough: hub storage

First Mission gives the hub surface storage. Four small edits:

1. **`Objects/CargoStorage.cs`** — the class shown above (`: GameObject`, `Capacity`/`Contents` observed, `Deposit`/`Withdraw` returning `CommandResult`, `ValidateState` rejecting negative or overflowing contents).
2. **`ServerApi.cs`** — the `deposit`/`withdraw` routes and request records shown above; `contents`/`capacity` need nothing.
3. **`Scenario.cs`** — add `new CargoStorage { Id = "hub-storage", Capacity = 200 }` next to the hub.
4. **`Tests/Program.cs`** — the domain test shown above plus an HTTP case through the `StartServer` harness.

No persistence edits, no engine edits, no registration list: `[GameType]` is the registration, the save codec handles the fields, the projection handles the reads, and hand-written routes are the only maintained surface. That is the whole extension story.
