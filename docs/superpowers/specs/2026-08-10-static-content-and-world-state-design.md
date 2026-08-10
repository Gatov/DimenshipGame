# Static Content and World State — Design

Date: 2026-08-10
Status: Draft

## Goal

Name and separate the two kinds of data the game holds, and give each a home.

Today there is one kind: `WorldDefinition`, a hand-written C# object that is simultaneously the game's
rulebook, this vessel's build sheet, its opening stock, and its first two tasks. Everything that changes
afterwards lives in `SimulationEngine`'s private fields, where nothing can address it, nothing can save
it, and the only way to read it is the lossy projection the UI already gets.

That holds for a two-item, four-executor demonstration vessel. It does not hold for the game the new
specifications describe, which has unlockable schematics, expeditions that acquire what production
cannot, plans that outlive the tick that created them, and enough items and recipes that authoring them
in C# means a rebuild per balance change.

This spec establishes:

1. **A three-tier data model** — Catalog (rules), Scenario (seed), World State (live) — and the rule
   that decides which tier anything belongs to.
2. **A JSON content layer** in `Dimenship.Core`, loaded and validated at startup, with a file-system
   seam so `Dimenship.Core` still never references Godot.
3. **An explicit, serializable `WorldState`** owned by the engine, covering vessel operations, player
   progress, committed plans, and expeditions.
4. **The save contract** — what round-trips, what a save may reference, and the two tests that keep the
   split honest.

## Source material

- `docs/specs/dimenship-schematics.md` — schematics, unlocks, facility upgrades, recursive expansion.
- `docs/specs/dimenship-planning-and-task-execution.md` — plans versus tasks, task and executor states,
  postponement, switch-over, expeditions as the acquisition source.

Both are transcriptions of the project owner's handwritten pages and are treated as requirements. Two
duplicate copies of the same text sit at `docs/Dimenship_Planning_and_Task_Execution_Spec.md` and
`docs/dimenship_schematics_specification.md`; the `docs/specs/` copies are the ones cited here.

## Relationship to prior specs

`2026-07-28-ui-shell-design.md`, `2026-07-30-production-planning-design.md` and
`2026-08-01-base-graph-design.md` all stand. The zone model, the panel contract, the snapshot-and-poll
binding, the reference direction between assemblies, and the integer-only rule in `Dimenship.Core` are
binding here.

This spec changes what is *behind* the snapshot, not the snapshot itself. `WorldSnapshot` keeps its
shape and its contract: replaced wholesale, never mutated, reference equality is an exact change test.
No shell code changes because of this spec.

## Current state

| Thing | Where it lives now | What is wrong with that |
| :--- | :--- | :--- |
| Item, storage, facility, route, sink definitions | `WorldDefinition.CreateDefault()`, hardcoded | Rulebook and build sheet in one record; a balance change is a rebuild |
| Schematics | `SchematicCatalog`, constructed inline | Correct shape, wrong home |
| Which schematics are unlocked | `SchematicCatalog._unlocked` | Player progress stored inside a static catalog |
| Node placements | `BaseGraphLayout.ForDefaultWorld()` | Separate authored table that must be kept in sync by hand |
| Stock, queues, task progress, tick, energy counters | `SimulationEngine` private fields | Not addressable, not serializable, not enumerable in a defined order from outside |
| Committed plans | Nowhere. `Commit` returns `TaskId`s and forgets | Nothing can answer "how far along is *Produce 4 Armor Plates*?" |
| Expeditions | Nowhere | `ShortageKind.RawResource` names a problem with no system to solve it |

Two properties of the current code are load-bearing and are preserved verbatim:

- **Iteration order comes from declaration lists, never from dictionary enumeration.** It is what makes
  the simulation deterministic.
- **A task references its schematic by id and never copies inputs, output, effort or energy.** It is
  what lets a facility upgrade change execution without touching work in flight.

## The concept

### Three tiers, and the question that sorts them

> **Does it change during play? Does it differ between two players running the same build?**

| Answer | Tier | Lifetime | Example |
| :--- | :--- | :--- | :--- |
| No, and no | **Catalog** | Ships with the game. Identical for every save. | *Alloy is smelted from 40,000 ore, costs 100 work and 1,650 energy, needs a refinery.* |
| No, but it is where a game starts | **Scenario** | Read once, at new-game. Never read again. | *This vessel begins with a smelter, a feed line, and 5 alloy in the hold.* |
| Yes | **World State** | The save file. | *Smelter A is 40 work into its third run and postponed for ore.* |

The middle tier is the one that is easy to get wrong. A scenario is **a seed, not a structure**: it
produces a world state and is then finished with. It is not consulted at tick 500 to find out what a
facility's work rate is — by then that facility is a live instance in the world state, possibly
upgraded, possibly not the one the scenario placed.

This is what `WorldDefinition` gets wrong today. `SimulationEngine` keeps `_definition` and reads
`producer.WorkRatePerTick` on every run, which quietly makes the seed permanent and makes the upgrades
the schematics spec asks for impossible to express.

### Archetype and instance

Splitting the seed from the state splits the facility record in two.

- **`FacilityArchetype`** (catalog) — what a *Mk. II Refinery* is. Base work rate, standing draw,
  switch-over ticks, which `FacilityType` it counts as, its buffer size.
- **`FacilityInstance`** (world state) — what *Smelter A* is. Which archetype, what the player calls it,
  which storage it works, its upgrade level, its configured schematic, its queue, its run in progress.

Effective work rate is `archetype.WorkRatePerTick * instance.WorkRatePermille / 1000`. Upgrades move the
permille. The schematic is untouched, exactly as `dimenship-schematics.md` §2 requires.

The same split applies to transport lines and, when they exist, to mission docks.

### State versus snapshot

`WorldState` and `WorldSnapshot` are both "the dynamic data", and they are not the same object.

| | `WorldState` | `WorldSnapshot` |
| :--- | :--- | :--- |
| Purpose | Resume the simulation | Draw a frame |
| Completeness | Everything, including `WorkDoneThisRun`, `EnergyChargedThisRun`, queue order, attempt history | Only what a panel reads |
| Derived values | None. If it can be recomputed, it is not stored | Many, deliberately — `TotalAmount`, `RunTicksRemaining`, `NetRatePerTick` |
| Audience | The engine, and the save file | The shell |
| Lifetime | Mutated in place across a tick | Rebuilt whole on every change |

The rule that keeps them apart:

> **Anything required to resume the simulation identically goes in `WorldState`. Anything required only
> to draw it is derived into `WorldSnapshot` and stored nowhere.**

And the rule that keeps `WorldState` from turning into a second rulebook:

> **World state stores ids and deltas. Never definitions, and never a value the catalog can already
> answer.** A state record holding a `SchematicDefinition` would serialise the rulebook into every save
> and would let two saves disagree about what alloy costs.

The second clause is the one that takes discipline, because the leaks are individually harmless-looking.
A `CapacityPermille` copied onto a storage instance means rebalancing a hold in content leaves every
existing save on the old number. A `Label` copied onto a facility means renaming *Smelter* to *Refinery*
in content never reaches a save, and a later localisation pass reaches nothing at all. Neither is a
crash; both are content changes that silently fail to apply, which is worse.

So state carries a value only when it is one of:

| Kind | Example | Why it must be state |
| :--- | :--- | :--- |
| **A reference** | `Archetype`, `Configured`, `LocalStorage` | It names content; it is not content |
| **A delta from content** | `WorkRatePermille` = 1000 unless upgraded | The catalog holds the base; state holds the divergence |
| **A player override** | `NameOverride`, null unless renamed | Null means "ask content", so content changes still land |
| **Genuinely dynamic** | stock, queues, `WorkDoneThisRun`, `CapHits` | The catalog has no opinion about it |
| **Topology** | a route's `From`/`To`, a facility's `LocalStorage` | Buildable, therefore mutable, therefore not content |

Unlocks are the first kind, which is why they are a set of `SchematicId` rather than a flag on
`SchematicDefinition`. Everything else in the tree should be checkable against this table, and the
three fields below that were not are the reason this section exists.

### Progress is not catalog

`SchematicCatalog` currently holds `_unlocked`. That is player progress inside the rulebook: two saves
of the same game would need two catalogs, and a catalog loaded from a shared content file could not
carry it at all.

Unlocks move to `WorldState.Progress`. `SchematicCatalog` becomes purely static and shareable. The
planner reads unlocks through `IWorldView`, which is where every other piece of world knowledge already
reaches it from.

## Decisions

| Question | Decision |
| :--- | :--- |
| Static content format | JSON under `content/`, parsed into the existing immutable records |
| Parser | `System.Text.Json` with a source-generated context. No reflection, no external package, survives Godot's trimming and AOT export |
| Unknown JSON fields | A load error, not ignored. A typo in a content file must not silently become a default |
| Validation | Two-phase — parse, then link. All errors collected and reported together, not first-throw |
| File access | `IContentFileSystem` seam. `Dimenship.Core` gains no Godot reference; the Godot layer supplies a `res://` implementation |
| Content ids | Lowercase snake_case strings, stable forever. The only thing a save may reference |
| Scenario | A JSON document that seeds a `WorldState` and is then discarded |
| Catalog lifetime | Loaded once per process, immutable, shared by every save. Never serialised into one |
| Facility model | Archetype (catalog) + instance (state). Same for transport lines **and storages** |
| What state may hold | Ids, deltas, player overrides, genuinely dynamic values, topology. Nothing the catalog can answer |
| Instance names | `string? NameOverride`, null meaning "use the archetype's label", so content renames still land |
| Node placement | Seeded from the scenario onto the **instance**. It is topology, and it must survive a load |
| World state | One explicit `WorldState` record tree, owned by `SimulationEngine`, fully serialisable |
| Unlocks | A `HashSet<SchematicId>` in `WorldState.Progress`, not a flag on `SchematicDefinition` |
| Plans | Persist as `CommittedPlan` entities linking a goal to the tasks it spawned |
| Expeditions | Shape declared, mechanics deferred. Enough to hold a state and resolve a raw-resource shortage |
| Save format | JSON, `saveVersion` integer plus a `contentVersion` stamp. Refuse a newer version, report content drift |
| Snapshot | Unchanged. No shell change results from this spec |
| Floats | Still banned in `Dimenship.Core`. Ratios are permille integers |

## Part 1 — The content layer

### Layout

```
content/
  manifest.json          content version, unit conventions, file list
  catalog/
    items.json
    schematics.json
    facilities.json      production archetypes
    transports.json      transport archetypes
    expeditions.json     sites, deferred mechanics
  scenarios/
    default_vessel.json
```

`manifest.json` carries a `contentVersion` string. It is stamped into every save and compared on load;
it is how a save made against different content is reported rather than silently mangled.

### Catalog records

The records that already exist keep their shape. `ItemDefinition`, `SchematicDefinition`,
`ItemAmount`, `WorkAmount`, `EnergyAmount` are unchanged — they are already pure content.

New, from splitting the executor definitions:

```csharp
namespace Dimenship.Core.Content;

/// <summary>What a class of production facility is, before one is built and named.</summary>
public sealed record FacilityArchetype(
    FacilityArchetypeId Id,
    string Label,
    FacilityType Type,
    long WorkRatePerTick,
    long StandingPowerDraw,
    long SwitchOverTicks,
    long BufferPermille);

/// <summary>What a class of transport line is. It has no configuration and so no switch-over.</summary>
public sealed record TransportArchetype(
    TransportArchetypeId Id,
    string Label,
    long ThroughputPerTick,
    long StandingPowerDraw);

/// <summary>What a class of storage is. Storages get an archetype for the same reason facilities
/// do: capacity is a property of the kind of hold, not of the save.</summary>
public sealed record StorageArchetype(
    StorageArchetypeId Id,
    string Label,
    long CapacityPermille);

/// <summary>Everything static, linked and validated. Immutable, and shared by every save.</summary>
public sealed record ContentCatalog(
    string ContentVersion,
    SchematicCatalog Schematics,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<StorageArchetype> Storages,
    IReadOnlyList<FacilityArchetype> Facilities,
    IReadOnlyList<TransportArchetype> Transports,
    IReadOnlyList<ExpeditionSite> Expeditions);
```

`SchematicCatalog` loses `_unlocked`, `IsUnlocked` and `UnlockedForOutput`. `ForOutput` stays;
the planner filters it against `IWorldView.IsUnlocked`.

`StorageDefinition` is replaced by `StorageArchetype` plus a scenario placement. Opening stock is a
scenario concern, not a definition of what a storage *is*, and capacity is a property of the kind of
hold. A facility's own buffer still comes from its archetype's `BufferPermille`.

### The catalog's lifetime

The catalog is loaded **once per process**, is immutable, and is shared by every save opened during
that run. It is never copied into a save file and never varies per save: a save references it by
`contentVersion` and by ids, and by nothing else.

This is the strong form of "static", and it is worth naming because the weak form — a catalog that is
merely a separate object, but is constructed per world and can carry per-save fields — is what the
current `SchematicCatalog` is, and is how `_unlocked` came to live in it.

### JSON shape

Arrays, not objects keyed by id: array order is declaration order, and declaration order is what makes
the simulation deterministic.

```json
{
  "schematics": [
    {
      "id": "smelt_alloy",
      "output": { "item": "alloy", "quantity": 8000 },
      "inputs": [ { "item": "ore", "quantity": 40000 } ],
      "effortPerRun": 100,
      "energyPerRun": 1650,
      "requiredFacilityType": "refinery"
    }
  ]
}
```

Every number is an integer in the milli-units `Units` already documents. There is no float anywhere in
the format, which is also why the parser can reject a fractional literal outright.

### Loading and validation

```csharp
public interface IContentFileSystem
{
    string ReadAllText(string relativePath);
    bool Exists(string relativePath);
}

public interface IContentSource
{
    ContentLoadResult Load();
}

public sealed record ContentLoadResult(
    ContentCatalog? Catalog,
    IReadOnlyList<ContentError> Errors);

public sealed record ContentError(string File, string Path, string Message);
```

`JsonContentSource(IContentFileSystem)` is the real implementation. The Godot layer supplies a
`res://`-backed file system, so exported builds read packed content and `Dimenship.Core` still names no
Godot type. Tests supply an in-memory dictionary; **no test needs a file on disk**, and `WorldBuilder`
keeps building catalogs in code.

Loading is two phases, and the second is the point:

1. **Parse.** Each file to its records. Malformed JSON, unknown fields, fractional numbers and unknown
   enum names are errors here.
2. **Link.** Every id reference resolved against every other file, and every invariant checked.

Errors are **collected, not thrown on the first one**. A content author fixing eleven dangling item ids
should see eleven messages, not eleven runs. This replaces `SimulationEngine`'s current
throw-on-first-problem constructor for content-shaped problems; the constructor keeps throwing for
programmer errors, which is what an exception is for.

Link-phase rules, each of which is a test:

- Every schematic input and output names a known item.
- No schematic lists its own output among its inputs. Longer cycles stay the planner's business — it
  already reports `ShortageKind.CyclicSchematic`.
- Every facility archetype's `WorkRatePerTick` is positive; every transport archetype's
  `ThroughputPerTick` is positive.
- Every id is unique within its file and matches `^[a-z][a-z0-9_]*$`.
- Every scenario facility names a known archetype; every scenario storage a known storage; every route's
  two endpoints known storages, and not the same one.
- A facility's initial schematic is compatible with its archetype's `FacilityType`.
- Every scenario node has a graph placement, no two nodes share a cell, and both endpoints of every
  route are placed.
- Opening stock fits the storage that holds it.
- Total standing draw does not exceed the scenario's energy capacity.
- Every initially-unlocked schematic exists.

The last four are `SimulationEngine`'s current constructor checks, moved to where content authors will
actually meet them.

### The scenario

```csharp
public sealed record Scenario(
    string Id,
    string Label,
    long EnergyCapacity,
    IReadOnlyList<ScenarioStorage> Storages,
    IReadOnlyList<ScenarioFacility> Facilities,
    IReadOnlyList<ScenarioRoute> Routes,
    IReadOnlyList<PowerSinkId> Sinks,   // by id: a sink is content, and has no per-instance state
    IReadOnlyList<SchematicId> UnlockedSchematics,
    IReadOnlyList<ScenarioTask> InitialTasks,
    IReadOnlyList<ScenarioTransfer> InitialTransfers);

public sealed record ScenarioFacility(
    ExecutorId Id,
    FacilityArchetypeId Archetype,
    string? NameOverride,               // "Smelter A"; null leaves the archetype's label
    StorageId LocalStorage,
    SchematicId? InitialSchematic,
    NodePlacement Placement);
```

Placement rides on the node it places, and **is seeded into the instance, not read from the scenario at
draw time.** Placement is topology: it is per-instance, it survives a save, and a buildable facility
would be placed by the player rather than by content. `FacilityInstance` and `StorageInstance` each
carry a `NodePlacement`, `BaseGraphLayout` is projected from `WorldState`, and
`BaseGraphLayout.ForDefaultWorld()` and its two parallel dictionaries are deleted.

Getting this wrong is easy and the failure is quiet: a scenario is discarded after seeding, so a
placement that lived only there would produce a correct graph on a new game and an empty one on every
load. "Every node is placed" also stops being a rule two hand-maintained tables have to keep agreeing
on — it becomes an invariant of a single record.

`WorldDefinition.CreateDefault()` becomes `content/scenarios/default_vessel.json`, carrying the same
numbers and, in a `notes` field the loader ignores, the same reasoning the current comments hold. The
`static readonly` ids on `WorldDefinition` (`Ore`, `MainHold`, `SmelterA` …) stay as constants for the
tests and the default-world helpers that name them.

`ScenarioSeeder.Seed(catalog, scenario) -> WorldState` is the only thing that ever reads a `Scenario`.

## Part 2 — World state

### The tree

```csharp
namespace Dimenship.Core.State;

public sealed class WorldState
{
    public required int SaveVersion { get; set; }
    public required string ContentVersion { get; set; }
    public required long Tick { get; set; }

    public required VesselState Vessel { get; init; }
    public required TaskRegistry Tasks { get; init; }
    public required ProgressLedger Progress { get; init; }
    public required PlanRegistry Plans { get; init; }
    public required ExpeditionLedger Expeditions { get; init; }
    public required JournalLedger Journal { get; init; }
}
```

Mutable classes rather than records-with-`with`: the engine mutates this hundreds of times per tick, and
the immutability that matters — the shell's — is `WorldSnapshot`'s.

**`VesselState`** — storages and their stock, facility instances, transport instances, sinks, energy.

Sinks are the one thing here that gets no instance type. A `PowerSinkDefinition` is `(Id, Label,
PowerDraw)` and a sink has no queue, no configuration and nothing that changes, so `VesselState` holds
a `List<PowerSinkId>` — which sinks this vessel has — and the draw is read from the catalog. An
archetype/instance pair for a record with no dynamic half would be ceremony.

State types are named `…Instance` and `…Ledger` rather than `…State`, because `StorageState`,
`EnergyState` and `ExecutorState` are already taken by `WorldSnapshot`'s projections and the two sets
sit one namespace apart. A save record and a draw record with the same name is a mistake waiting to be
made in a `using` line.

Every instance is `(id, archetype, overrides, dynamic fields)` and nothing else. Capacity, throughput,
work rate, standing draw and switch-over ticks are all read through the archetype at the point of use.

```csharp
public sealed class StorageInstance
{
    StorageId Id; StorageArchetypeId Archetype;
    string? NameOverride;               // null = the archetype's label
    NodePlacement Placement;            // per-instance, therefore state, therefore saved
    List<StoredItem> Stock;             // capacity is the archetype's, never copied here
}

public sealed record StoredItem(ItemId Item, long Amount);   // no capacity: it is derived

public sealed class FacilityInstance
{
    ExecutorId Id; FacilityArchetypeId Archetype;
    string? NameOverride;
    NodePlacement Placement;
    StorageId LocalStorage;             // topology: buildable, therefore state
    long WorkRatePermille;              // 1000 = the archetype's rate. Upgrades move this.
    long EnergyEfficiencyPermille;      // 1000 = the schematic's energy. Upgrades move this.
    SchematicId? Configured;            // survives idling, per the schematics spec
    long SwitchOverRemaining;
    TaskId? SwitchTarget;
    List<TaskId> Queue;                 // order is the executor's starting point, not a schedule
    TaskId? Current;
    ExecutorStatus Status;
    PostponeReason? BlockReason;
}

public sealed class TransportInstance
{
    ExecutorId Id; TransportArchetypeId Archetype;
    string? NameOverride;
    StorageId From; StorageId To;       // the route is the line's, not the transfer's — and topology
    long ThroughputPermille;
    List<TaskId> Queue; TaskId? Current;
    long MovedLastTick;                 // saved: a snapshot rebuilt after load must not read zero
    ExecutorStatus Status; PostponeReason? BlockReason;
}

public sealed class EnergyLedger { long Capacity; long DrawLastTick; int CapHits; int StarvedTicks; }
```

`NameOverride` resolves as `instance.NameOverride ?? catalog.Archetype(instance.Archetype).Label`, in
one helper, so no call site can forget the fallback. `WorldSnapshot` keeps its plain `Label` field —
resolving the override is the projection's job, and the shell should never learn that overrides exist.

`EnergyLedger.Capacity` is state rather than content on purpose: vessel capacity is the kind of thing
upgrades and damage move, and there is no vessel archetype to hold a base for it to diverge from. If a
vessel archetype ever appears, capacity becomes a permille like the others.

`CapHits` and `StarvedTicks` are cumulative counters, so they are state, not derivation. `Draw` is
last tick's granted total and is saved for the same reason `MovedLastTick` is: a snapshot rebuilt
immediately after a load must show the vessel as it was, not as a cold start.

**`TaskRegistry`** — every task, by id, plus `NextTaskId`. Executors hold ids; the registry holds
bodies. Two reasons: a task is referenced from an executor queue, a plan, and the journal, and only one
of those can own it; and the current `ProductionTask` and `TransportTask` classes already carry
everything needed, so this is a move rather than a redesign. `WorkDoneThisRun`,
`EnergyChargedThisRun`, `RunActive`, `RunAwaitingDeposit`, `LastReason`, `PostponedAtTick` and the
bounded `History` all come along — a save that dropped `WorkDoneThisRun` would silently refund a
half-finished run.

**`ProgressLedger`**

```csharp
public sealed class ProgressLedger
{
    HashSet<SchematicId> UnlockedSchematics;   // serialised as a sorted array
    HashSet<ItemId> DiscoveredItems;           // what the codex may show
    HashSet<string> Flags;                     // one-shot story/tutorial marks
}
```

`SimulationEngine.Enqueue`'s unlock check reads this. `IWorldView` gains `bool IsUnlocked(SchematicId)`
and the planner stops asking the catalog.

**`PlanRegistry`** — the piece with no equivalent today.

```csharp
public readonly record struct PlanId(long Value);

public enum PlanState { Active, Complete, Abandoned }

public sealed class CommittedPlan
{
    PlanId Id;
    ItemAmount Goal;                  // "4 armor plates" — the thing the player actually asked for
    long CommittedAtTick;
    List<TaskId> SpawnedTasks;        // production and transport alike, in commit order
    IReadOnlyList<PlanShortage> Shortages;   // what it could not supply, at commit time
    PlanState State;
}
```

`SimulationEngine.Commit` keeps returning `TaskId`s and additionally records a `CommittedPlan`. A plan
becomes `Complete` when every spawned task is `Complete`.

This exists because **the goal is the only level at which progress is legible.** Tasks are per-executor
by design — the planning spec is explicit that execution order belongs to executors, not to the plan —
so nothing in the task list can answer "how far along is *Produce 4 Armor Plates*?" without the plan
that grouped them. It is also what a later "add an expedition to this plan" affordance attaches to:
`dimenship-planning-and-task-execution.md` §2 suggests exactly that, against a plan that has already
been committed.

**`ExpeditionLedger`** — declared, and deliberately thin.

```csharp
public sealed record ExpeditionSite(          // catalog
    ExpeditionSiteId Id, string Label,
    IReadOnlyList<ItemAmount> Yields,
    long TravelTicks, long EnergyCost);

public enum ExpeditionPhase { Available, Outbound, Working, Inbound, Delivered }

public sealed class Expedition                // state
{
    ExpeditionId Id; ExpeditionSiteId Site; ExpeditionPhase Phase;
    long DepartedAtTick; long ArrivesAtTick;
    List<ItemAmount> Manifest; StorageId Destination;
    PlanId? ForPlan;                          // the shortage this was mounted to fix
}

public sealed class ExpeditionLedger
{
    List<Expedition> Active;
    List<ExpeditionSiteId> Known;
    ExpeditionSiteId? Location;               // where the ship is; null while in transit
}
```

The two source documents name expeditions as the answer to a raw-resource shortage and name mission
docks among the executors, and specify nothing further. So this spec fixes the **shape and the seam**
and leaves the mechanics to a spec of their own. Two things about the shape are load-bearing and worth
committing to now:

- `ForPlan` is what turns "41 raw material missing" into a tracked resolution rather than a warning the
  player has to remember.
- A mission dock, when it arrives, is **an executor with a queue** like any other — it selects among
  queued acquisition tasks, postpones with a reason, and reports the same `ExecutorStatus`. The executor
  abstraction generalises; nothing here should be built as a parallel system.

**`JournalLedger`** — the bounded `SimEvent` ring the engine already keeps, plus `TotalEventsEmitted`.
Saved, because a console that goes blank on load is a bug report.

### Ownership and the seam

```csharp
public sealed class SimulationEngine : IWorldView
{
    public SimulationEngine(ContentCatalog catalog, WorldState state);

    public static SimulationEngine NewGame(ContentCatalog catalog, Scenario scenario);

    public ContentCatalog Catalog { get; }
    public WorldState State { get; }          // authoritative; callers read, the engine writes
    public WorldSnapshot Snapshot { get; private set; }
}
```

`SimulationEngine`'s private collections are replaced by `WorldState`'s. The lookup dictionaries it
builds from them (`_executorsById`, `_holdCapacity`, `_items`) stay private, rebuilt on construction —
they are indices, not data, and indices do not belong in a save file.

## Part 3 — Saving

```json
{
  "saveVersion": 1,
  "contentVersion": "2026-08-10.1",
  "savedAtTick": 4820,
  "state": { "...": "the WorldState tree" }
}
```

Rules:

- **`(catalog, state)` is sufficient.** An engine reconstructed from those two and nothing else must
  behave identically. Anything that breaks this is a missing field in `WorldState`.
- **A newer `saveVersion` is refused** with a clear message. An older one runs through a chain of
  numbered upgraders. Version 1 has no upgraders and needs none, but the chain exists from day one
  because retrofitting it means guessing what version-1 saves looked like.
- **Content drift is reported, never absorbed.** A save naming an id the catalog no longer has fails
  with every missing id listed. A mapping table for renamed content is a later addition and would slot
  in at exactly this point.
- **Collections whose order matters serialise as arrays.** Sets serialise sorted, so two saves of the
  same state are byte-identical and a diff is meaningful.

Two tests keep the whole split honest, and they are the acceptance criteria for this spec:

1. **Round-trip.** `Load(Save(state))` deep-equals `state`, for a world advanced far enough to have a
   run in progress, a postponed task, a switch-over under way, a partially-moved transfer and a
   committed plan.
2. **Determinism across a save.** Advance 500 ticks, save, load, advance 500 more. Separately: advance
   1,000 ticks. The two resulting snapshots are equal, and so are the two event journals. This is the
   test that catches a field that lives only in the engine.
3. **Content still reaches old saves.** Save a world. Edit the catalog — rename an archetype, change a
   storage's `CapacityPermille`, change a facility's `WorkRatePerTick`. Load the save against the edited
   catalog. Every edited value is visible in the resulting snapshot, and any facility the player renamed
   keeps its own name. This is the test that catches a content value copied into state, and it is the
   one whose absence let `CapacityPermille` and three `Label` fields into the first draft.

A fourth is worth having as a guard rather than a behaviour test: **no type under `State/` has a field
whose type is declared under `Content/`**, other than an id. A reflection test over the two namespaces
states the rule once and enforces it against every field added later.

## Layering

```
Dimenship.Core
  Content/       catalog records, JSON contracts, loader, validation   — no reference to State
  State/         WorldState tree, seeder, save/load, migrations        — references Content ids only
  Simulation/    engine, over (catalog, state)
  Production/    schematics, task bodies
  Planning/      planner, IWorldView
  Presentation/  BaseGraphLayout, built from a scenario
```

- `Content` must not reference `State`. The rulebook does not know about savegames.
- `State` references content **ids**, never content **records**.
- `Dimenship.Core` still references neither Godot nor `Dimenship.Shell`; `Dimenship.Shell` still
  references nothing. `IContentFileSystem` exists so the first of those survives shipping real files.
- No `float` or `double` in `Dimenship.Core`, including the JSON format.

## Migration

| Today | Becomes |
| :--- | :--- |
| `WorldDefinition.CreateDefault()` | `content/scenarios/default_vessel.json` + `ScenarioSeeder` |
| `WorldDefinition` (the record) | Deleted. Its id constants stay as a static class for tests |
| `ProductionExecutorDefinition` | `FacilityArchetype` + `ScenarioFacility` + `FacilityInstance` |
| `TransportExecutorDefinition` | `TransportArchetype` + `ScenarioRoute` + `TransportInstance` |
| `StorageDefinition` | `StorageArchetype` + `ScenarioStorage` + `StorageInstance` |
| `StorageDefinition.Initial` | `ScenarioStorage.Initial`; live stock is `StorageInstance.Stock` |
| `ItemDefinition.Label`, executor `Label`s | Archetype labels, resolved through `NameOverride ?? archetype.Label` |
| `SchematicCatalog._unlocked` | `WorldState.Progress.UnlockedSchematics` |
| `BaseGraphLayout.ForDefaultWorld()` | Projected from `WorldState`'s per-instance placements |
| `SimulationEngine` private collections | `WorldState`, exposed as `State` |
| Constructor validation | Content link phase, collected rather than thrown |
| `Commit` returns `TaskId[]` | Also records a `CommittedPlan`; return type unchanged |
| `IWorldView.Schematics.IsUnlocked` | `IWorldView.IsUnlocked` |

`WorldSnapshot`, every panel, `Dimenship.Shell` and every `.tscn` are untouched. The existing 100-plus
tests should survive on a rebuilt `WorldBuilder`, and any that do not are pointing at real behaviour
change and should be read rather than patched.

## Out of scope

- **Expedition mechanics** — travel, risk, yields, dock behaviour. Shape only, here.
- **Research** as a second unlock source. `ProgressLedger` holds the unlocks either way.
- **Building or demolishing facilities during play.** The archetype/instance split makes it expressible;
  no command exposes it.
- **Upgrade content.** `WorkRatePermille` and `EnergyEfficiencyPermille` exist and are seeded at 1,000.
  What moves them is a later spec.
- **Content hot-reload**, modding directories, user content paths.
- **Localisation.** Labels stay inline strings; when it matters they become keys, and that changes the
  loader and nothing else.
- **Autosave, save slots, save UI.** This spec defines the format and the guarantees, not the ceremony.
- **Multiple vessels.** `WorldState.Vessel` is singular, and that is a deliberate present-tense choice.

## Open questions

1. **Upgrades: permille fields or levels?** Two permille fields are the smallest thing that satisfies the
   schematics spec. A `Level` referencing a catalog upgrade table is tidier once upgrades have costs and
   prerequisites. Recommendation: ship the permille fields, since a level would write to them anyway.
2. **Does a facility's buffer belong to the archetype or the instance?** Archetype here, on the grounds
   that buffer size is what a *Mk. II Refinery* is. If a buffer becomes independently upgradable, it
   moves to the instance and the seeder changes.
3. **Is a mission dock a fourth `FacilityType`, or an executor kind of its own?** It has a queue and a
   status like a facility, but it runs acquisitions rather than schematics. Deferred with expeditions.
4. **Should the journal be saved in full or truncated?** Full, at 512 events, is a few tens of kilobytes.
   Revisit only if a save gets large.
