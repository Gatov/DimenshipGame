# Production, Planning and Task Execution — Design

Date: 2026-07-30
Status: Approved

## Goal

Replace the simulation kernel's placeholder production model with the real one: schematics as authoritative recipes, facilities as queue-owning executors that decide their own work each tick, transport as a first-class executor, tasks with explicit states and structured postponement reasons, and a planner that expands a player goal into production, transport, and acquisition requirements.

The UI Shell slice deliberately shipped "the thinnest viable seed of the simulation kernel" — enough for three panels to display something true. This spec replaces that seed with the production model the game is actually about. It is core-only plus the minimum shell adaptation needed to keep the Godot project building; queue and task *views* are explicitly out of scope and deserve their own spec.

## Source specifications

Two documents define the behaviour and are the authority for everything below. Both are now held in the repository:

- **`docs/specs/dimenship-schematics.md`** — the schematic record, its role in planning, and the production task.
- **`docs/specs/dimenship-planning-and-task-execution.md`** — planning versus tasks, the four task states, executor state, runtime task selection, batch and partial execution, and switch-over.

Where those specs are silent, this document records a decision and marks it as such. Where they are illustrative rather than exhaustive, this document says so rather than encoding the illustration as a requirement.

## Relationship to the GDD

This subsystem is the one the GDD's processing chain (§6.2) and global energy constraint (§6.3) describe, and it is the largest single producer of the machine-readable telemetry §8 calls "core feedback loop, not flavor text". Every decision an executor makes — selection, postponement, switch-over, completion — emits a structured event.

Nothing in the UI Shell spec is superseded. The snapshot contract, the panel contract, and the reference direction between assemblies all survive unchanged.

## Current state

`src/Dimenship.Core/Simulation/` is 399 lines across six files and implements a continuous flow model:

- `FacilityDefinition` hard-wires one optional input and one optional output at a fixed per-tick rate.
- `SimulationEngine.Tick()` walks `WorldDefinition.Facilities` in definition order; each facility either runs or blocks on power, input, or output room.
- Stocks are one flat `ResourceId → long` dictionary with a per-resource capacity. There is one implicit global store.
- `FacilityKind` is `Extractor`, `Smelter`, `StabilizationField` — a display label, not something a recipe is matched against.

There are no items, schematics, storage locations, executors, queues, tasks, transport, or planning. None of the vocabulary in the two specs exists in the code.

The Godot shell reads `WorldSnapshot.Resources`, `FacilityState`, and `EventCode` directly from `OverviewFocus`, `EnergyBudgetPanel`, and `EventLogPanel`.

## Decisions

| Question | Decision |
| :--- | :--- |
| Unit of simulation | Stays the tick. A run is a countdown of ticks fixed at start, not accumulated fractional work. |
| Run duration | `ceil(EffortPerRun / WorkRatePerTick)` ticks, computed when the run starts. |
| Input consumption | At run start, from the executor's **own** local storage. Output deposited there at run end. |
| Facility energy | Flat standing draw every tick regardless of activity, plus a production charge proportional to work done. |
| Switch-over energy | Standing draw only. Reconfiguration does no work, so it incurs no production charge. |
| Storage capacity | A per-storage permille of each item's hold capacity. Per-item override tables deferred. |
| Tick order | Power sinks → transport executors → production executors → power reconciliation. |
| Switch-over cost | A per-executor constant, not a per-schematic one. |
| Postpone reasons in telemetry | One `EventCode` per reason. `SimEvent`'s shape is unchanged. |
| Plan versus task | The planner is pure and never mutates the engine. Tasks exist only after `Commit`. |
| Committing with shortages | Allowed. Shortages ride on the snapshot for the shell to surface. |
| `Resources` on the snapshot | Retained as a vessel-wide roll-up so existing panels keep working. |
| Shell scope | Minimal adaptation of the three existing panels. No new panels. |

## Constraints carried forward

Unchanged from the UI Shell spec, and binding here:

- `net8.0` everywhere; every library referenced by `dimenship/Dimenship.csproj` declares `<Configurations>Debug;Release;ExportDebug;ExportRelease</Configurations>`.
- `Dimenship.Core` references neither `GodotSharp` nor `Dimenship.Shell`.
- All simulation quantities are `long`. **No `float` or `double` anywhere in `Dimenship.Core`.**
- Iteration order comes from `WorldDefinition` list order, never dictionary order.
- No wall clock in core. Time enters only through `Advance(long ticks)`.
- The Godot editor is not on `PATH` in this environment. No task may claim visual behaviour is verified.

## Repository layout

```
src/Dimenship.Core/
  Simulation/
    Ids.cs                     grows: new ids and enums
    Quantities.cs              NEW  ItemAmount, WorkAmount, EnergyAmount
    WorldDefinition.cs         rewritten: items, storages, executors, sinks
    WorldSnapshot.cs           grows: storages, executors, tasks, shortages
    SimEvent.cs                unchanged
    SimulationEngine.cs        rewritten tick
    Units.cs                   unchanged
  Production/                  NEW
    SchematicDefinition.cs
    SchematicCatalog.cs
    ProductionTask.cs
    TransportTask.cs
    TaskAttempt.cs
  Planning/                    NEW
    ProductionPlan.cs
    ProductionPlanner.cs
    IWorldView.cs
tests/Dimenship.Core.Tests/
  Simulation/                  grows
  Production/                  NEW
  Planning/                    NEW
```

No new assemblies. The reference direction is untouched.

## Vocabulary

`ResourceId` becomes `ItemId`. The specs treat materials, components, and finished items as one namespace — a schematic input may be any of them, and a schematic output may be an input to another — so two identifier types would only invite conversion code between them. No saves exist yet, so the rename costs nothing.

New identifiers, all readonly record structs alongside the existing ones: `SchematicId`, `TaskId`, `ExecutorId`, `StorageId`.

`FacilityKind` becomes `FacilityType { Extractor, Refinery, Factory }` — the thing a schematic's `RequiredFacilityType` is matched against, not a display label. `StabilizationField` leaves the enum: it owns no queue and executes no schematic, and the existing code comment at its definition already admits it "draws power unconditionally and produces nothing". It becomes a `PowerSinkDefinition`.

## Schematics

```csharp
public sealed record SchematicDefinition
{
    public required SchematicId Id { get; init; }
    public required ItemAmount Output { get; init; }
    public required IReadOnlyList<ItemAmount> Inputs { get; init; }
    public required WorkAmount EffortPerRun { get; init; }
    public required EnergyAmount EnergyPerRun { get; init; }
    public required FacilityType RequiredFacilityType { get; init; }
}
```

Taken verbatim from the Schematics spec. A task references a schematic by id and never copies its contents, so a facility upgrade that changes work rate or energy efficiency changes execution without touching either the schematic or any task in flight.

`SchematicCatalog` wraps an ordered list with lookup by id, `IsUnlocked(SchematicId)`, and `ForOutput(ItemId)` returning candidates in definition order. Multiple schematics may produce the same output; the player selects one, and delegating that choice to an unlocked AI assistant is future work the catalog's shape already allows.

Switch-over duration is **not** on the schematic. The Schematics spec says selecting any different schematic causes "a full standard reconfiguration" — one constant per facility, not a per-recipe cost.

## Storage

```csharp
public sealed record ItemDefinition(ItemId Id, string Label, long HoldCapacity);

public sealed record StorageDefinition(
    StorageId Id, string Label, long CapacityPermille, IReadOnlyList<ItemAmount> Initial);

// capacity(storage, item) = item.HoldCapacity * storage.CapacityPermille / 1000
```

How much of an item fits is a property of the **item** — ore is bulky, alloy is not — scaled by how big the storage is. A full-sized hold is 1000‰; a facility's local buffer is a few permille of one, for every item at once.

This is one number per storage rather than a per-item table that has to be revisited every time an item is added, and it keeps ore and alloy at the different capacities the world already gave them. Per-item overrides are a deliberate omission: nothing in the production model needs them, and adding them later changes no call site.

Integer division, floor, no `double` — a storage too small to hold one unit of something holds none of it, which is the honest answer.

Every executor owns exactly one local storage. That is the "own storage" a run draws its inputs from and deposits its output into. Moving material between a facility's local storage and a central hold is transport's job and nobody else's — which is what makes transport a real system with real latency rather than an accounting convenience.

## Executors

```csharp
public sealed record ProductionExecutorDefinition(
    ExecutorId Id, FacilityType Type, StorageId LocalStorage,
    long WorkRatePerTick, long StandingPowerDraw, long SwitchOverTicks,
    SchematicId? InitialSchematic);

public sealed record TransportExecutorDefinition(
    ExecutorId Id, long ThroughputPerTick, long StandingPowerDraw);

public sealed record PowerSinkDefinition(string Id, long PowerDraw);
```

`WorldDefinition` becomes the ordered composition of items, storages, producers, transports, sinks, and the schematic catalog. List order remains the determinism contract.

### Executor state

Per the Planning spec, executor state is separate from task state:

`ExecutorStatus { RunningTask, SwitchingOver, NoTasksQueued, AllQueuedTasksBlocked }`

A transport task therefore never reports "waiting for transport". The transport executor decides whether the task can run; if the source lacks material the *task* becomes `Postponed` with `InsufficientSourceMaterial`.

## Tasks

```csharp
public enum TaskState { NotStarted, Running, Postponed, Complete }

public enum PostponeReason
{
    InsufficientInputMaterial,
    InsufficientSourceMaterial,
    DestinationFull,
    InsufficientEnergy,
    OutputRouteUnavailable,
    SafetyLock,
}
```

`ProductionTask` carries its schematic id, requested runs, executor id, state, completed runs, the current run's remaining ticks and energy charged so far, the last postpone reason and the tick it was recorded, and a bounded attempt history. `TransportTask` carries item, requested and moved quantities, source, destination, executor, and the same state and history fields.

Both are mutable runtime classes projected into immutable snapshot records — the split the engine already uses for `FacilityState`.

Attempt history is bounded exactly the way the engine's event buffer is. An unbounded per-task list of every attempt grows without limit across a long session, and a task that is postponed and retried each tick is the normal case, not the pathological one.

## The tick

Fixed order, pinned by test:

1. **Power sinks** claim their draw. Unconditional, as today.
2. **Transport executors**, in definition order. Running first means material delivered this tick is available to production this tick.
3. **Production executors**, in definition order. Each one either:
   - **is switching over** — decrement the counter, draw standing power only, and on reaching zero emit `SwitchOverCompleted`. The target task becomes `Running` on the *following* tick: the Planning spec is explicit that it does not become `Running` until switch-over is complete.
   - **has a run in progress** — decrement `RunTicksRemaining` and take the tick's production charge. On reaching zero, deposit the output into local storage, increment `CompletedRuns`, and complete the task when `CompletedRuns == RequestedRuns`. If the output does not fit, the task is `Postponed` with `DestinationFull` and holds the finished run until room appears.
   - **is between runs** — select a task, and if one can start, consume its inputs from local storage and begin a run of `ceil(EffortPerRun / WorkRatePerTick)` ticks.
4. **Power reconciliation.** `PowerCapReached`, `CapHits`, and `StarvedTicks` keep the deliberately separate meanings documented on `EnergyState`.

### Task selection

Straight from the Planning spec, and it belongs to the executor rather than to a fixed plan sequence:

1. Continue the current task when its next run can start.
2. Otherwise prefer a runnable queued task using the currently configured schematic.
3. Otherwise take a runnable task on a different schematic and enter switch-over.
4. If nothing is runnable, report `AllQueuedTasksBlocked` and record each queued task's reason.

The bias toward the current configuration is the whole point: unnecessary switching costs throughput, so continuous production is what the default behaviour produces.

### Batch and partial execution

A production task does not need materials for its full requested quantity before starting — it starts when one run's inputs exist. A 20-unit alloy task may produce 6, postpone on `InsufficientInputMaterial`, and resume when material arrives. Transport behaves the same way: a request to move 60 with 19 at the source moves 19 now and postpones the rest.

This is what lets production and transport overlap instead of each waiting on the other to finish completely.

## Energy

Two separate charges.

**Standing draw.** `StandingPowerDraw` is a flat per-tick cost, identical whatever the executor is doing — idle, switching over, or running. It is claimed the same way a power sink's draw is, before any production charge, and is never refused.

**Production charge.** `EnergyPerRun` is charged proportionally to the work actually done that tick — not up front, and not in equal slices. Switch-over does no work and so costs nothing beyond the standing draw.

Only the production charge is refusable. If granting it would exceed capacity the executor is refused, its task is `Postponed` with `InsufficientEnergy`, and `RunTicksRemaining` holds where it is: the run resumes when power frees up rather than being voided and its inputs wasted.

The charge is integer-exact with no drift, tracked cumulatively on the task:

```
workDone     += workThisTick                        // WorkRatePerTick; less on the final tick
targetTotal   = EnergyPerRun * workDone / EffortPerRun
tickCharge    = targetTotal - EnergyCharged
EnergyCharged = targetTotal
```

Because the final tick's `workDone` equals `EffortPerRun` exactly, a completed run has charged exactly `EnergyPerRun` — the rounding remainder settles itself on the last tick, with no special case and no accumulated error over thousands of runs. A run interrupted part-way has charged only for the work it did.

Effort and energy stay independent, as the Schematics spec requires: a low-energy item may take a long time, and a fast one may be expensive.

## Planning

The planner is pure. It reads a world view and returns a plan; it never mutates the engine. The Planning spec's distinction is load-bearing — planning data may contain proposed requirements before commitment, and those are not runtime tasks and have no execution state.

```csharp
public sealed record ProductionPlan(
    ItemAmount Goal,
    IReadOnlyList<PlannedRun> Runs,
    IReadOnlyList<PlannedTransfer> Transfers,
    IReadOnlyList<PlanShortage> Shortages);

public sealed record PlannedRun(SchematicId Schematic, ExecutorId Executor, int Runs);
public sealed record PlannedTransfer(ItemId Item, long Quantity, StorageId From, StorageId To);
public sealed record PlanShortage(ItemId Item, long Missing, ShortageKind Kind);

public enum ShortageKind
{
    RawResource,        // nothing aboard produces it; only an expedition fixes this
    LockedSchematic,    // a schematic exists but is not unlocked; a mission fixes this
    CyclicSchematic,    // the chain re-entered itself; a content error
    NoCompatibleExecutor, // no facility of the required type, or no transport line
}
```

Expansion follows the Schematics spec: use what is available, determine the missing quantity, expand the inputs of the schematic that produces it, and recurse. It stops when an input is already available or allocated, can be produced by an unlocked schematic, is a raw resource that must be acquired, or requires a schematic the player has not unlocked. A visited set guards against cyclic recipes and a depth cap guards against a chain long enough to be a content bug.

Availability nets out reservations: material on hand, minus quantities already claimed by committed tasks, plus output expected from tasks in flight. Without that, planning the same goal twice would double-count the same stock.

Executor selection prefers a compatible `FacilityType`, tie-broken by fewest queued runs and then definition order. Deterministic, and it spreads load without needing a scheduler.

`SimulationEngine.Commit(ProductionPlan)` injects the tasks into their executors' queues and returns the created `TaskId`s. **A plan with shortages is committable** — the Planning spec is explicit that the available portion may begin immediately while the player is notified of what is missing. An expedition is optional and is the player's call, so the shortages ride on the snapshot rather than blocking the commit.

An unknown schematic is different from a missing resource: a shortage of raw material allows partial execution and suggests an expedition, while an unlocked-schematic gap prevents the planner from creating that production branch at all. Both are reported; only one is fixable by hauling.

### Worked example

The Planning spec's example is the acceptance test for the whole change. Goal `Produce 4 Armor Plates`, with Alloy 5, Chips 10, and Raw Material 19 on hand, must produce additional Alloy 20 and Chips 5, and exactly one shortage: 41 Raw Material.

That constrains the fixture's schematics, which are chosen to reproduce the spec's arithmetic:

| Schematic | Output | Inputs | Facility |
| :--- | :--- | :--- | :--- |
| `armor_plate` | 4 Armor Plate | 25 Alloy, 15 Chips | Factory |
| `alloy` | 5 Alloy | 15 Raw Material | Refinery |
| `chip` | 5 Chips | 5 Refined Silicon, 5 Conductive Material | Factory |

Raw material has **no** schematic in this fixture. It is the thing that must be acquired, and
giving it an extraction recipe would have the planner expand it into extraction runs and report
no shortage at all — contradicting the number the example exists to demonstrate. Extraction is a
schematic in the default world, where it is a facility doing work; it is not one here.

Four armour plates is one run of `armor_plate`, needing 25 Alloy and 15 Chips against 5 and 10 on hand. Twenty alloy is four runs of `alloy` at 15 raw each — 60 raw against 19 on hand, missing 41. Chips expand to silicon and conductive material, which the fixture stocks, so raw material is the only shortage.

**The spec's transfer list is illustrative, not exhaustive.** It names four transfers and omits both the chip inputs moving to the factory and the finished chips moving back to storage. The planner emits the complete, consistent set; the test asserts that the four transfers the spec names are present, and does not treat their absence from the spec's list as a requirement that the others not exist.

## Telemetry

`SimEvent` keeps its shape. `EventCode` grows, with **one code per postpone reason** rather than a reason field encoded into `Data`: the console's category and severity mapping is a switch over codes, so a reason expressed as a code is filterable and a reason buried in a dictionary is not.

New codes: `TaskQueued`, `RunStarted`, `RunCompleted`, `TaskCompleted`, `SwitchOverStarted`, `SwitchOverCompleted`, `TransferStarted`, `TransferCompleted`, `PlanCommitted`, `PlanShortage`, `AllTasksBlocked`, and `PostponeInsufficientInput`, `PostponeInsufficientSource`, `PostponeDestinationFull`, `PostponeInsufficientEnergy`, `PostponeOutputRoute`, `PostponeSafetyLock`.

The Planning spec's example log — task selected, task postponed, reason with the concrete numbers — is exactly what the `Data` dictionary carries: `have` and `need` on a postponement, as the current engine already does for `BlockMissingInput`.

## Snapshot

`WorldSnapshot.Resources` is retained as a vessel-wide roll-up, summed across all storages in item-definition order. Storage locations are new, but "how much alloy does this vessel have" remains the question the overview and status bar ask, and keeping it means the existing panels need no change on that axis.

Added: `Storages`, `Executors` (an `ExecutorState` replacing `FacilityState`), `Transports`, `Sinks`, `ProductionTasks` and `TransportTasks`.

Shortages are deliberately **not** on the snapshot. They belong to the plan the caller holds and to the `PlanShortage` events; a copy carried on the snapshot would have no way to clear itself as the vessel acquired what it was missing, and a stale shortage is worse than none.

## Shell impact

Minimal and deliberate:

- `OverviewFocus` — its facility list reads `ExecutorState` instead of `FacilityState`, and its block-reason description covers the new codes.
- `EnergyBudgetPanel` — its per-consumer breakdown iterates executors and sinks instead of facilities.
- `EventLogPanel` — its code-to-severity switch gains the new codes.

Queues, task states, postponement histories, the planning UI, and the expedition prompt are **not** built here. Each needs its own spec, and the point of this slice is to make the systems they will display real and correct first.

## Open items

- **Transport energy.** A transport line draws its standing power and nothing more. A per-unit haulage charge would be the analogue of a schematic's energy, and neither specification asks for one.
- **Standing orders.** Extraction is expressed as a task with a large `RequestedRuns`, which is a stand-in for a repeating task the spec does not yet describe. A real standing-order concept belongs to a later slice.
- **Expeditions.** Shortages suggest one; nothing yet acquires raw material from outside the vessel.
- **Facility upgrades.** Work rate and energy efficiency are per-executor constants. The Schematics spec anticipates upgrades changing them without modifying schematics — the shape allows it, nothing implements it.
- **Multiple schematics per output.** The catalog returns candidates in order and the planner takes the first unlocked one. Player selection and AI delegation are future work.
- **Per-item storage capacity overrides.** Deferred in favour of an item hold capacity scaled by a per-storage permille, as recorded above.
