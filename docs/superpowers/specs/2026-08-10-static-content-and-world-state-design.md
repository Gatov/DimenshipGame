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
specifications describe, which has unlockable schematics, missions that acquire what production
cannot, plans that outlive the tick that created them, and enough items and recipes that authoring them
in C# means a rebuild per balance change.

This spec establishes:

1. **A four-tier data model** — Catalog (rules), Scenario (this campaign's authored layout), World
   State (live), and Authored Content (what the player wrote) — and the rule that sorts anything into
   one of them.
2. **A JSON content layer** in `Dimenship.Core`, loaded and validated at startup, with a file-system
   seam so `Dimenship.Core` still never references Godot.
3. **An explicit, serializable `WorldState`** owned by the engine, covering vessel operations, player
   progress, committed plans, and missions.
4. **The save contract** — what round-trips, what a save may reference, and the tests that keep the
   split honest.

## Source material

- `docs/Game Design v0.8.md` — the foundation GDD. Operational time, the SCADA schematic, energy and
  compute, quests and readiness, missions, robots, and an explicit list of what a save must preserve.
  **The most authoritative document here**, and the one that decides vocabulary where others differ.
- `docs/Dimenship Programming v0.1.md` — programs as first-class objects: presets, rule cards, found and
  player-authored programs, installation slots, conflicts, and program telemetry.
- `docs/specs/dimenship-schematics.md` — schematics, unlocks, facility upgrades, recursive expansion.
- `docs/specs/dimenship-planning-and-task-execution.md` — plans versus tasks, task and executor states,
  postponement, switch-over, acquisition as the answer to a raw-resource shortage.

The last two are transcriptions of the project owner's handwritten pages. Duplicate copies sit at
`docs/Dimenship_Planning_and_Task_Execution_Spec.md` and `docs/dimenship_schematics_specification.md`;
the `docs/specs/` copies are the ones cited here.

Three notes on the source material itself, none of which change the design but all of which affect
reading it:

- **`docs/Dimenship Programming v0.1.md` had a duplicated block, now removed.** Sections 2.2 through 4.2
  appeared twice — 122 byte-identical lines, joined mid-line at *"> Greedy exploration versus safe
  extraction.## 2.2 Recommended internal representation"*, which is the signature of a bad paste rather
  than deliberate repetition. One copy was deleted and the joined line repaired. **Its section 4.2 still
  contains only Example A**; Examples B and C are referenced by lettering but were never present in
  either copy, so they appear to have been lost before the document reached the repository.
- **Terminology: "expedition" versus "mission".** The handwritten schematics page says *expedition*; the
  GDD says *mission* throughout, names *Mission Dock* as a facility, and lists Mining, Scavenging and
  Investigation as MVP mission types. This spec follows the GDD: **mission** is the entity, and an
  acquisition mission is what resolves a `ShortageKind.RawResource`.
- **The GDD specifies .NET 10; the repository targets `net8.0`.** Every `csproj` and both existing plans
  say net8.0. That is a build decision rather than a data one and this spec does not resolve it, but it
  should be resolved deliberately rather than discovered during a release.

### Appendix 1 is a requirement, not a musing

The GDD's Appendix 1 — the project owner's post-document review — contradicts the body of the GDD in
places and wins where it does. It fixes the production graph at one **global, central Resource Storage**,
three to four interconnected factories, two to three connected refineries, mission docks connected only
to storage, and a **Power Core that consumes refined material as fuel**. It confirms simplified power
delivery with no power lines. And it reinstates progressive reveal: *"a fixed layout, revealing lines and
facilities as they are built"*.

That last clause changes this spec's model of node placement, and is dealt with below.

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
| Node placements | `BaseGraphLayout.ForDefaultWorld()` | Separate authored table kept in sync by hand; no notion of a slot not yet built |
| TimeFlow, alerts, RNG seeds, utilization windows | Nowhere | All four are listed by the GDD as things a save must preserve |
| Compute budget, reactor fuel | Nowhere | Energy is modelled as a free pool with a fixed cap |
| Programs, robots, case graph | Nowhere | Three domains the new documents make first-class |
| Stock, queues, task progress, tick, energy counters | `SimulationEngine` private fields | Not addressable, not serializable, not enumerable in a defined order from outside |
| Committed plans | Nowhere. `Commit` returns `TaskId`s and forgets | Nothing can answer "how far along is *Produce 4 Armor Plates*?" |
| Missions | Nowhere | `ShortageKind.RawResource` names a problem with no system to solve it |

Two properties of the current code are load-bearing and are preserved verbatim:

- **Iteration order comes from declaration lists, never from dictionary enumeration.** It is what makes
  the simulation deterministic.
- **A task references its schematic by id and never copies inputs, output, effort or energy.** It is
  what lets a facility upgrade change execution without touching work in flight.

## The concept

### Four tiers, and the question that sorts them

> **Does it change during play? Does it differ between two players running the same build?**

| Answer | Tier | Lifetime | Example |
| :--- | :--- | :--- | :--- |
| No, and no | **Catalog** | Ships with the game. Identical for every save. | *Alloy is smelted from 40,000 ore, costs 100 work and 1,650 energy, needs a refinery.* |
| No, but it describes this campaign | **Scenario** | Ships with the game. Pinned by id in the save, re-read every run. | *This vessel has a smelter slot right of the hold, and begins with 5 alloy.* |
| Yes | **World State** | The save file. | *Smelter A is built, 40 work into its third run, and postponed for ore.* |
| Yes, and the player wrote it | **Authored content** | The save file. | *The player's own* Refinery Shortage Recovery *program.* |

The middle tier is the one that is easy to get wrong, in either of two directions.

**It is not a mutable structure.** A scenario is never consulted at tick 500 to find out what a
facility's work rate is — by then that facility is a live instance, possibly upgraded. That is what
`WorldDefinition` gets wrong today: `SimulationEngine` keeps `_definition` and reads
`producer.WorkRatePerTick` on every run, which quietly makes the seed permanent and makes the upgrades
the schematics spec asks for impossible to express.

**But it is not discarded either.** An earlier draft of this spec called it a seed, read once and
thrown away. Appendix 1's *"fixed layout, revealing lines and facilities as they are built"* rules that
out: a layout that reveals things as they are built is a layout whose slots are authored in advance,
including for facilities that do not exist yet. Those authored slots are content, they must be there on
every load, and copying them into the save would mean an edited layout never reaching an existing game.

So the scenario is **immutable authored reference data with a lifetime as long as the catalog's** —
consulted for what was authored, never for what has changed. It is read at tick 500, but only ever for
answers that cannot have changed since tick 0.

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

### The fourth tier: authored content

`docs/Dimenship Programming v0.1.md` breaks the model above, and it is worth being precise about how,
because the break is real and the temptation is to paper over it.

A player-authored program is **a definition that did not ship with the game**. It has the shape of
catalog data — an id, rules, conditions, actions, a complexity budget — and none of its provenance. It
cannot live in the catalog, which is immutable, process-wide and shared by every save. It is not a
delta from anything, so `NameOverride`-style indirection does not apply. And it must be serialised in
full, because nothing else in the world can reconstruct it.

The same is true of a **found program the player has edited**, and of a **corrupted program** whose
rules differ from the pristine definition it came from.

So the model gains a fourth tier:

| Tier | Authored by | Lives in | Mutable |
| :--- | :--- | :--- | :--- |
| Catalog | The developers | `content/` | Never |
| Scenario | The developers | `content/scenarios/` | Never |
| **Authored content** | **The player, or a mission reward** | **The save** | **Yes, by the player** |
| World state | The simulation | The save | Every tick |

Rules for the new tier, each of which exists to stop it becoming a loophole:

- **Separate id space.** Authored ids are minted with a distinguishing prefix (`user:`) and validated to
  be unrepresentable as catalog ids. A player program can then never shadow or collide with a shipped
  one, and a save can never smuggle a redefinition of `smelt_alloy` past the catalog.
- **Authored content is data, never behaviour.** A program is a rule list interpreted by the engine, not
  code. This is what the programming document's §2.2 asks for — a deterministic AST or command list, not
  embedded scripting — and §9.1's sandboxing note is the same requirement stated from the security side.
- **It validates on load, like catalog content does.** Same two-phase parse-and-link, same collected
  errors. A save carrying a program that references an item the catalog dropped must report it, not
  crash mid-tick 400 ticks later.
- **It is the only exception.** Nothing else in the save may hold definition-shaped data. If a second
  candidate appears, it goes in this tier explicitly or it does not go in at all.

This is also the honest answer to "why not put unlocks in the catalog": the catalog is the one thing
that is provably identical between two players, and everything that is not identical has to live
somewhere that says so.

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
| Scenario | Immutable reference data, **retained** and pinned in the save by id. It authors every node slot, including unbuilt ones |
| Authored content | A fourth tier: player-written and player-edited programs live in the save, in a `user:`-prefixed id space that cannot collide with catalog ids |
| Global budgets | Energy and compute, two explicit ledgers. Not a generalised budget dictionary |
| Utilization | Bucketed trailing-window counters per executor. Measured, therefore saved |
| Determinism | The engine never reads a clock or an unseeded `Random`. Per-domain seeded streams, saved |
| Alerts | A ledger separate from the journal, because acknowledgement and pinning are player state |
| Root cause | `PostponeReason` gets a declared total order and one shared comparer |
| Catalog lifetime | Loaded once per process, immutable, shared by every save. Never serialised into one |
| Facility model | Archetype (catalog) + instance (state). Same for transport lines **and storages** |
| What state may hold | Ids, deltas, player overrides, genuinely dynamic values, topology. Nothing the catalog can answer |
| Instance names | `string? NameOverride`, null meaning "use the archetype's label", so content renames still land |
| Node placement | Seeded from the scenario onto the **instance**. It is topology, and it must survive a load |
| World state | One explicit `WorldState` record tree, owned by `SimulationEngine`, fully serialisable |
| Unlocks | A `HashSet<SchematicId>` in `WorldState.Progress`, not a flag on `SchematicDefinition` |
| Plans | Persist as `CommittedPlan` entities linking a goal to the tasks it spawned |
| Missions | Shape declared, mechanics deferred. Enough to hold a state and resolve a raw-resource shortage |
| Save format | JSON, `saveVersion` integer plus a `contentVersion` stamp, both on the envelope and neither inside `WorldState`. Refuse a newer version, report content drift |
| Runtime ids | Every registry that mints ids saves its own counter |
| Randomness | Streams indexed by an append-only `RngDomain`; a saved value is advanced state, not a seed |
| Program language | Specified by `2026-08-11-programming-view-design.md`. This spec assigns tiers and does not restate it |
| Program parameters | Bounds on the definition, tuned values in `ProgramInstance`, cooldowns per `RuleId` |
| Reservations | A ledger in `VesselState`, owned by `ProgramInstanceId`. They change what a tick produces, so they are saved |
| TimeFlow | Session state. Every load resumes at 0×; `AutoPauseOnCriticalAlert` is saved |
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
    storages.json        storage archetypes
    reactors.json        reactor archetypes
    programs.json        shipped preset programs
    strata.json          mission target strata, deferred mechanics
  scenarios/
    default_vessel.json
```

Robot frames and module definitions get no file here. They are declared in Part 2 and deferred, and
a content file for a subsystem with no mechanics would be a schema nobody can fill. When robots
ship they add `robots.json` and `modules.json` and two fields on `ContentCatalog`, which is the
same shape every other archetype family already has.

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
    IReadOnlyList<ReactorArchetype> Reactors,
    IReadOnlyList<ProgramDefinition> Programs,   // shipped presets only; authored ones are in the save
    IReadOnlyList<StratumDefinition> Strata);
```

`Programs` is the catalog half of the one type that lives in two tiers. `ReactorArchetype` and
`ProgramDefinition` are specified in Part 2, with the domains they belong to; they are listed here
because a type specified with no home in the catalog is a type the loader will not load.

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
- Every id is unique within its file and matches `^[a-z][a-z0-9_]*$`. The colon in the authored tier's
  `user:` prefix is unrepresentable under that pattern, which is what makes the two id spaces provably
  disjoint rather than disjoint by convention.
- Every scenario facility names a known archetype; every scenario storage a known storage; every route's
  two endpoints known storages, and not the same one.
- A facility's initial schematic is compatible with its archetype's `FacilityType`.
- Every scenario **storage and facility** has a graph placement and no two share a cell; both endpoints
  of every route are placed. Routes carry no placement of their own — they are edges, drawn between the
  storages they join.
- Opening stock fits the storage that holds it.
- Total standing draw does not exceed the scenario's energy capacity.
- Every initially-unlocked schematic exists.
- Every catalog program validates clean against the programming spec's `ProgramValidator`, and every
  parameter's `Default` lies within its own `Min` and `Max`. A shipped preset that cannot be activated
  is a content bug, and it should be caught by the loader rather than by the player.

Placement, opening stock, standing draw and initial unlocks are `SimulationEngine`'s current
constructor checks, moved to where content authors will actually meet them.

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

public sealed record ScenarioStorage(
    StorageId Id,
    StorageArchetypeId Archetype,
    string? NameOverride,
    IReadOnlyList<ItemAmount> Initial,  // opening stock: a campaign's starting position, not a
                                        // property of what a storage is
    NodePlacement Placement);

public sealed record ScenarioFacility(
    ExecutorId Id,
    FacilityArchetypeId Archetype,
    string? NameOverride,               // "Smelter A"; null leaves the archetype's label
    StorageId LocalStorage,
    SchematicId? InitialSchematic,
    bool BuiltAtStart,                  // false authors a revealed-when-built slot
    NodePlacement Placement);

/// <summary>A transport line the campaign starts with. It is an edge, so it has no placement:
/// the graph draws it between the storages its route joins.</summary>
public sealed record ScenarioRoute(
    ExecutorId Id,
    TransportArchetypeId Archetype,
    string? NameOverride,
    StorageId From,
    StorageId To,
    bool BuiltAtStart);

public sealed record ScenarioTask(SchematicId Schematic, int Runs, ExecutorId Executor);

public sealed record ScenarioTransfer(
    ItemId Item, long Quantity, StorageId From, StorageId To, ExecutorId Executor);
```

`ScenarioTask` and `ScenarioTransfer` are today's `InitialTask` and `InitialTransfer`, renamed for
the tier they belong to and otherwise unchanged.

`BuiltAtStart` is what makes Appendix 1's progressive reveal expressible from content: a slot
authored with a placement and `BuiltAtStart: false` is a facility the campaign will have and does
not yet. The seeder creates its instance with `Built = false`; nothing else in the pipeline needs
to know the difference.

Placement rides on the node it places, and **stays in the scenario** — which means the scenario is
*retained*, not discarded.

This reverses an earlier draft of this spec, and the reason is Appendix 1. A layout that reveals
facilities *as they are built* is a layout whose slots are **authored in advance**, including for
facilities that do not exist yet. The player never moves a node and never places one — the GDD's
interaction table forbids both — so a placement is authored content that happens to be per-scenario, and
what is dynamic about it is only whether the thing standing in that slot has been built.

So:

- The **scenario is immutable reference data**, loaded from content on every run and pinned in the save
  by `scenarioId`. It authors every node slot the campaign will ever show, with its placement.
- `WorldState` holds, per slot, whether it is **built** — and the instance, once it is.
- `BaseGraphLayout` is projected from `(scenario, state)`: slots the state says are unbuilt render as
  reveal-pending or not at all.

This keeps placement out of the save entirely, which is what the rule wanted all along, and it is what
the GDD means by listing `VesselGraphDefinition` as a *static content definition of facility nodes,
visual grouping, line definitions, overlay categories, and navigation targets*.

The earlier draft moved placement onto the instance to fix a real defect — a discarded scenario would
have produced a correct graph on a new game and an empty one on every load. The defect was real; the fix
reached for the wrong tier. Retaining the scenario fixes it without duplicating authored data into every
save, and without the drift that duplication invites: edit a layout in content, and old saves would have
kept the old one.

`BaseGraphLayout.ForDefaultWorld()` and its two parallel dictionaries are still deleted.

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
    public required string ScenarioId { get; set; }     // the retained scenario this world runs on

    public required OperationalClock Clock { get; init; }
    public required RandomState Random { get; init; }

    public required VesselState Vessel { get; init; }
    public required TaskRegistry Tasks { get; init; }
    public required ProgressLedger Progress { get; init; }
    public required PlanRegistry Plans { get; init; }
    public required MissionLedger Missions { get; init; }
    public required AlertLedger Alerts { get; init; }
    public required JournalLedger Journal { get; init; }

    // Declared, deferred. Each is a domain of its own, not a field to fill in here.
    public required ProgramLedger Programs { get; init; }
    public required RobotLedger Robots { get; init; }
    public required CaseLedger Case { get; init; }
}
```

Mutable classes rather than records-with-`with`: the engine mutates this hundreds of times per tick, and
the immutability that matters — the shell's — is `WorldSnapshot`'s.

`SaveVersion` and `ContentVersion` are deliberately **not** here. They describe the file, not the
world, and Part 3's envelope already carries both; holding them in two places is holding two
answers to one question, and the day they disagree the loader has no way to tell which is right.

> **Every registry that mints an id carries its own counter, and the counter is saved.**

`TaskRegistry.NextTaskId` is the one this spec originally named, and it is not the only one:
`PlanRegistry`, `MissionLedger`, `AlertLedger` and `ProgramLedger` all hand out ids at runtime too.
A registry whose counter is not saved restarts from zero after a load and mints an id that is
already in use — which is not a crash, and not visible, until two entities that were never meant to
be the same one are indistinguishable. Stating it as a rule over registries rather than as a field
on each is what makes it hold for the fifth registry as well as the first.

**`VesselState`** — storages and their stock, facility instances, transport instances, reactor
instances, the sinks this vessel has, the energy and compute ledgers, and the reservation ledger.

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
    List<StoredItem> Stock;             // capacity is the archetype's, never copied here
}

public sealed record StoredItem(ItemId Item, long Amount);   // no capacity: it is derived

public sealed class FacilityInstance
{
    ExecutorId Id; FacilityArchetypeId Archetype;
    string? NameOverride;
    StorageId LocalStorage;             // topology: buildable, therefore state
    bool Built;                         // Appendix 1: slots are authored, revealed as built
    long WorkRatePermille;              // 1000 = the archetype's rate. Upgrades move this.
    long EnergyEfficiencyPermille;      // 1000 = the schematic's energy. Upgrades move this.
    long IntegrityPermille;             // 1000 = undamaged. GDD: degraded / damaged / maintenance
    SchematicId? Configured;            // survives idling, per the schematics spec
    long SwitchOverRemaining;
    TaskId? SwitchTarget;
    List<TaskId> Queue;                 // insertion order; task priority reorders selection
    TaskId? Current;
    List<ProgramInstanceId> Programs;   // installed controllers, in evaluation order
    ExecutorStatus Status;
    PostponeReason? BlockReason;
    UtilizationWindow Utilization;      // measured, therefore saved — see below
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

/// <summary>Material withheld from consumers by an installed program. State, because
/// <c>Available()</c> subtracts it — a reservation that does not survive a load changes what the
/// next tick produces, not merely what the next frame shows.</summary>
public sealed record Reservation(
    StorageId Storage, ItemId Item, long Quantity, ProgramInstanceId Owner);

public sealed class ReservationLedger { List<Reservation> Held; }   // declaration order
```

`Reservation` is owned by a **`ProgramInstanceId`, not a `ProgramId`**. Two installations of one
program on two facilities each hold their own reservations, and clearing one must not clear the
other; keying by the definition would make the two indistinguishable at exactly the moment the
engine needs to tell them apart.

`NameOverride` resolves as `instance.NameOverride ?? catalog.Archetype(instance.Archetype).Label`, in
one helper, so no call site can forget the fallback. `WorldSnapshot` keeps its plain `Label` field —
resolving the override is the projection's job, and the shell should never learn that overrides exist.

`EnergyLedger.Capacity` is state rather than content on purpose: vessel capacity is the kind of thing
upgrades and damage move, and there is no vessel archetype to hold a base for it to diverge from. If a
vessel archetype ever appears, capacity becomes a permille like the others.

### Utilization is measured, so it is saved

The GDD asks for something the engine does not currently produce. §5.6's node inspector reads
*utilization 70%, input wait 31% of recent operational window, power throttling 0%, output blocked 4%*,
and §12 makes it a rule: **a percentage without a cause category is not enough**.

That is not derivable from a snapshot. It is an accumulation over a trailing window, and §11.2 requires
it to be *reproducible after save/load* — so the accumulator is state.

```csharp
/// <summary>Ticks spent in each disposition over a trailing window, as bucketed counters.</summary>
public sealed class UtilizationWindow
{
    long WindowTicks;                   // e.g. 10 operational minutes = 600
    long BucketTicks;                   // resolution; buckets rotate, so this is a ring
    int  Head;
    long[] Working;                     // per bucket, ticks doing chargeable work
    long[] Idle;                        // no task queued
    long[] WaitingInput;                // postponed for material
    long[] WaitingOutput;               // run held, nowhere to deposit
    long[] Throttled;                   // refused energy or compute
    long[] SwitchingOver;               // reconfiguring: real time, no work
}
```

Bucketed counters rather than a per-tick event list: the window has to survive a save without the save
growing with it, and a player-facing "31% input wait over the last ten minutes" needs no finer grain
than a bucket. The disposition categories are chosen to sum to the elapsed window exactly, so no cause
can be silently unattributed.

**The divisor is ticks elapsed into the window, capped at `WindowTicks` — not `WindowTicks`.** A ring
that has not yet filled has counted fewer ticks than the window is wide, and dividing by the full
window makes every category read low: a facility that has worked every tick since the world began
reads 8% utilized two minutes into a new game, and the six categories sum to 8 rather than 100. The
percentages total 100 from the first tick only if the denominator is what was actually measured.

`WorldSnapshot` gains the derived percentages, not the buckets. The shell should never see a ring.

### Compute is a second global budget

Energy is not the only vessel-wide pool. The GDD's blocked-reason glossary lists **compute deferred**,
§9 has *Archive Decoder blocked by compute reservation* and *analysis queue power-throttled*, and the
programming document gives every program a compute cost of none, low, medium or high.

So compute is a budget with the same shape as energy — a capacity, a draw, a refusal that lands on a
task as a postponement — and it gets the same ledger:

```csharp
public sealed class ComputeLedger { long Capacity; long DrawLastTick; int CapHits; int StarvedTicks; }
```

They stay two ledgers rather than a generalised `Dictionary<ResourceKind, Budget>`. There are two, the
engine charges them at different points in a tick, and a dictionary would buy generality nothing has
asked for while making the charging order — which is what determinism rests on — implicit.

### The reactor burns fuel

Appendix 1: *"Power Core (probably will need refined materials as a fuel)"*, and the programming
document's Example E is a Reactor Fuel Balancer that converts refined material to `ReactorFuel` and
preserves a reserve during stabilization.

This breaks an assumption in the current model. `EnergyCapacity` is a constant of the world definition
and sinks only draw against it. A fuel-burning power core makes **energy production a production
chain**: capacity becomes a function of fuel on hand and burn rate, and running out is a survivable
failure state with a readable cause.

`PowerSinkDefinition` survives unchanged — the stabilization field still just draws. What is new is a
power *source*:

```csharp
public sealed record ReactorArchetype(          // catalog
    ReactorArchetypeId Id, string Label,
    ItemId Fuel, long FuelPerTick, long EnergyPerFuel, long CapacityCeiling);

public sealed class ReactorInstance                // state
{
    ExecutorId Id; ReactorArchetypeId Archetype; string? NameOverride;
    bool Built; long IntegrityPermille;
    StorageId FuelStore;
    long OutputPermille;                           // throttled by program or player
    List<ProgramInstanceId> Programs;
    ExecutorStatus Status; PostponeReason? BlockReason;
    UtilizationWindow Utilization;
}
```

A reactor is an executor: it has a status, it can be starved, and its starvation is a postponement with
a reason. `PostponeReason` gains `InsufficientFuel`. Whether it also carries a queue — that is, whether
fuel conversion is a schematic run on a facility rather than a reactor-specific mechanism — is left
open below, because Example E reads both ways.

### The clock, the seed, and the alerts

Three things the GDD names as saved state and this spec did not have.

```csharp
public sealed class OperationalClock
{
    long Tick;                          // moves from WorldState's root to here
    TimeFlow Flow;                      // Paused, X1, X2, X4
    bool AutoPauseOnCriticalAlert;
}

public enum TimeFlow { Paused, X1, X2, X4 }
```

`Flow` is held here because the engine needs somewhere to keep it, and is **not written to the save**
— every load resumes at 0×, for the reasons under *Closed since the first draft*.
`AutoPauseOnCriticalAlert` is saved: it is a preference the player set rather than a speed they
happened to leave running.
§11.2's operational timestamp is `Tick`, which is saved either way.

`Tick` is still the only thing the simulation advances on: **TimeFlow scales how many ticks a real
second buys and nothing else.** A tick must cost the same whatever the flow, or determinism dies and
4× becomes a different game rather than a faster one.

```csharp
/// <summary>Members are append-only and never reordered: the index is the save format.</summary>
public enum RngDomain { Mission, Hazard, Salvage, Analysis }

public sealed class RandomState { ulong[] Streams; }   // indexed by RngDomain
```

§11.2 requires saving *random seeds if any*, and missions with outcomes mean there are. Seeds are
per-domain streams — missions, hazards, salvage — not one global generator, so that drawing a mission
result cannot shift what a later production tie-break returns. A single stream makes every consumer
order-coupled to every other, which is the classic way a deterministic simulation stops being one.

Two properties of that array are the whole of its contract, and neither is obvious from its type:

- **`RngDomain` is append-only.** The array is indexed by the enum, so inserting a domain in the
  middle re-points every stream in every existing save at a different domain. New domains go on the
  end, and a save shorter than the current enum extends with fresh seeds rather than failing.
- **A stream value is the generator's advanced state, not the seed it started from.** Saving the
  seed would replay every draw the world has already made the next time it loads. `Mission.RngStream`
  is the same: it is that mission's generator as it now stands, which is what lets a mission in
  flight resolve identically across a save.

Determinism is a stated pillar (GDD §3, §11.2), so the rule is: **the engine never calls a clock, a
`Guid`, or an unseeded `Random`.** Anything non-deterministic enters through `RandomState` or not at
all.

```csharp
public sealed class Alert
{
    AlertId Id; AlertSeverity Severity; AlertCode Code;
    string SubjectId;                   // the node, line or quest it is about
    long RaisedAtTick;
    PostponeReason? RootCause;
    bool Acknowledged; bool Pinned;     // player state — the reason alerts are saved at all
}
```

Alerts are **not** journal events, and conflating them would be a mistake. An event is a historical
fact, already emitted, immutable, and bounded to the last 512. An alert is a *live condition* that
persists until its cause clears, carries a severity and a root cause, and — because §5.4 lets the player
acknowledge and pin them — carries player state that a save must keep. §11.2 lists "alert state"
separately from telemetry for exactly this reason.

### Root cause needs a defined order

§11.2: *blocked reasons must be stable, explainable, and prioritized consistently.* The glossary's
vocabulary is wider than the engine's six `PostponeReason` values, adding **compute deferred**, **route
unsafe** and **prerequisite missing** to the existing set, and the reactor adds **insufficient fuel**.

"Root cause" is defined as the highest-priority explanation among several true ones, so the enum needs a
**total order declared once**, in the enum's own declaration, and a single comparer that every surface
uses. Two panels disagreeing about why a factory is stalled is precisely the small lie the base-graph
spec already refuses to tell.

`CapHits` and `StarvedTicks` are cumulative counters, so they are state, not derivation. `Draw` is
last tick's granted total and is saved for the same reason `MovedLastTick` is: a snapshot rebuilt
immediately after a load must show the vessel as it was, not as a cold start.

**`TaskRegistry`** — every task, by id, plus `NextTaskId`. Executors hold ids; the registry holds
bodies. Two reasons: a task is referenced from an executor queue, a plan, and the journal, and only one
of those can own it; and the current `ProductionTask` and `TransportTask` classes already carry
everything needed, so this is a move rather than a redesign. `WorkDoneThisRun`,
`EnergyChargedThisRun`, `RunActive`, `RunAwaitingDeposit`, `LastReason`, `PostponedAtTick`, the
`Priority` the programming view adds, and the bounded `History` all come along — a save that dropped
`WorkDoneThisRun` would silently refund a half-finished run, and one that dropped `Priority` would
quietly undo every ordering a program had established.

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
that grouped them. It is also what a later "add an acquisition mission to this plan" affordance hangs off:
`dimenship-planning-and-task-execution.md` §2 suggests exactly that, against a plan that has already
been committed.

**`MissionLedger`** — renamed from the draft's expedition ledger, to the GDD's vocabulary.

```csharp
public sealed record StratumDefinition(       // catalog
    StratumId Id, string Label,
    IReadOnlyList<ItemAmount> Yields,
    long TravelTicks, long EnergyCost, long HazardPermille);

public enum MissionKind { Mining, Scavenging, Investigation }   // GDD MVP set
public enum MissionPhase { Preparing, Outbound, Working, Inbound, Delivered, Lost }

public sealed class Mission                   // state
{
    MissionId Id; StratumId Target; MissionKind Kind; MissionPhase Phase;
    ExecutorId Dock;                          // the dock that launched it
    List<RobotId> Group;                      // who went
    long DepartedAtTick; long ArrivesAtTick;
    List<ItemAmount> Manifest; StorageId Destination;
    PlanId? ForPlan;                          // the shortage this was mounted to fix
    ulong RngStream;                           // this mission's own draw, so it replays
}

public sealed class MissionLedger
{
    List<Mission> Active;
    List<StratumId> Known;
    StratumId? Location;                      // where the vessel is; null while in transit
}
```

The source documents name acquisition as the answer to a raw-resource shortage and name mission docks
among the executors. This spec fixes the **shape and the seam** and leaves the mechanics to a spec of
its own. Three things about the shape are load-bearing and worth committing to now:

- `ForPlan` is what turns "41 raw material missing" into a tracked resolution rather than a warning the
  player has to remember.
- A mission dock is **an executor with a queue** like any other — it selects among queued acquisition
  tasks, postpones with a reason, and reports the same `ExecutorStatus`. The executor abstraction
  generalises; nothing here should be built as a parallel system. Appendix 1 connects docks only to
  storage, which makes their routing trivial and is worth taking as a constraint rather than a coincidence.
- `RngStream` per mission, not per world. A mission that draws from its own stream replays identically
  whether or not another mission ran first, which is what makes a saved mission reproducible.

### Three domains declared, not specified

The GDD and the programming document introduce three subsystems this spec is not the right place to
design. Each gets its tier assignment, its id space, and the seam it attaches to — enough that adding
them later is additive rather than a redesign — and nothing more.

**Programs.** The largest of the three, and the one that motivated the authored-content tier.

`ProgramDefinition`, `Rule`, `Statement`, `Condition`, `Operand` and `ActionKind` are specified in
`2026-08-11-programming-view-design.md` §*The program model*, which is written after this document
and is authoritative on the language. This spec does not restate them, and an earlier draft that did
is the reason the two disagreed on six field names. What belongs here is the tier assignment, the id
space, and the three places where the language's shape and the tier rule meet.

```csharp
public sealed class ProgramInstance           // state: an installed copy with its own settings
{
    ProgramInstanceId Id; ProgramId Definition;
    string TargetId;                          // the facility, array, dock or robot group it runs on
    Dictionary<string, long> Parameters;      // the tuned values. Bounds stay on the definition.
    Dictionary<RuleId, long> Cooldowns;       // §9.1 lists cooldown state as saved
    bool Enabled;
}
```

`ProgramDefinition` is the type that lives in **both** the catalog and the save, which no other type
does. A shipped preset is catalog; a player-authored or player-edited one is authored content with a
`user:` id. The programming document's §9.1 asks to save *installed programs, parameter values, rule
cards, compiled representation, cooldown state, and version* — everything but the compiled
representation, which is a cache and is rebuilt on load rather than trusted from a file.

Three points of contact, each of which is a correction to one document or the other:

- **A tuned parameter is state, not definition.** The programming spec's `ProgramParameter` carries
  `Min`, `Max`, `Default` **and `Current`** in one record on the definition. The first three are
  content — they are what the program's author validated against — and `Current` is a player
  override, which the table in §*State versus snapshot* puts in the save. So `Current` leaves
  `ProgramParameter` and becomes an entry in `ProgramInstance.Parameters`; a missing entry means
  "use `Default`", which is the same null-means-ask-content indirection `NameOverride` uses. Left
  as written, the first save of a tuned preset writes a player's number into a shape the catalog
  also loads, and rebalancing a preset's default would never reach a player who had tuned it.
- **Cooldown is per rule, not per instance.** The programming spec puts `CooldownTicks` on `Rule`
  and offers a `TicksSinceRuleFired` condition, so one instance holds as many cooldowns as it has
  rules. A single `CooldownRemaining` cannot express that. This is also what makes the programming
  spec's *"`RuleId` is stable across an edit"* a save requirement rather than a telemetry nicety:
  the ids are dictionary keys in the save file, and an edit that re-mints them silently clears
  every cooldown it touches.
- **`ComputeCost` is deferred, not dropped.** The programming spec omits it deliberately — compute
  is a balancing resource in the design documents and in no system — and this spec's
  `ComputeLedger` is the system it is waiting for. It returns as a field on `ProgramDefinition`
  when the ledger is charged, and it is content, because what a program costs to run is not
  something a save may disagree about.

Two constraints worth fixing now, because retrofitting either is expensive: **rule evaluation order must
be a declared total order**, since §6.2 makes conflicts a reported gameplay feature and a conflict report
that cannot say which rule won is worthless; and **programs are evaluated at defined points in a tick**,
not whenever a value changes, or the determinism pillar goes. The programming spec closes both — rules
run in installation order then definition order, at phase 0 of the tick — and §*What the programming
view hands over* below records what that costs the save.

**Robots.** Archetype and instance again, and nothing new in kind.

```csharp
public sealed record RobotFrame(RobotFrameId Id, string Label, /* slots, mass, base stats */);
public sealed record ModuleDefinition(ModuleId Id, string Label, ModuleKind Kind, /* effects */);

public sealed class Robot                     // state
{
    RobotId Id; RobotFrameId Frame; string? NameOverride;
    List<ModuleId> Installed;
    long IntegrityPermille;
    RobotGroupId? Group;
    MissionId? OnMission;
}
```

**Case and quests.** `QuestNode`, leads, contradictions, witnesses and verification questions.
§11.2 lists *case graph state* as saved, so it is state; the GDD's `ReadinessEvaluator` maps state to
readiness, which makes readiness **derived** and therefore snapshot, never state. That distinction is
the whole reason it is safe to defer this domain: a readiness evaluator that accidentally stored its
conclusions would be a second source of truth, and the rule already forbids it.

The Case Board is also explicitly a *different graph* from the vessel schematic (GDD §13, and the
glossary's warning about confusing the two). It should not reuse `BaseGraphLayout`, `NodePlacement` or
the base graph's projection — sharing them would be the exact confusion the GDD asks to avoid.

**`JournalLedger`** — the bounded `SimEvent` ring the engine already keeps, plus `TotalEventsEmitted`.
Saved, because a console that goes blank on load is a bug report.

### What the programming view hands over

`2026-08-11-programming-view-design.md` ships before this spec does, and it says so plainly:
*"Persistence — none. Programs are world state, and world state is not saved."* That is true when
written and false the moment this spec lands, so the hand-over is written down here rather than
left to be rediscovered. Four things cross the line, and each is already placed above:

| What the programming view creates | Where it lands |
| :--- | :--- |
| Player-authored and player-edited `ProgramDefinition`s | Authored content, `user:` id space |
| Installed programs, their tuned parameters and per-rule cooldowns | `ProgramLedger`, as `ProgramInstance` |
| Reservations held by an installed program | `VesselState`'s `ReservationLedger` |
| `ProductionTask.Priority` | `TaskRegistry`, with the rest of the task body |

Three lines in that spec go stale when this one is implemented and should be amended then, not now:
the Decisions row quoted above, its open item 1, and `ProgramValidator.Validate`'s `WorldDefinition`
parameter — a record this spec deletes, whose replacement is `(ContentCatalog, WorldState)` or the
`IWorldView` the planner already takes.

There is one more consequence, and it is the sharpest of them. The programming spec's **phase 0
evaluates programs against the snapshot published at the end of the previous tick.** That promotes
a whole class of field in this document from cosmetic to load-bearing: `MovedLastTick` is justified
above as *"a snapshot rebuilt after load must not read zero"*, which reads as a display concern, but
a program condition can read that value and act on it. A field missing from the save no longer
misdraws one frame — it changes what the vessel does on the first tick after a load.

> **Every field the snapshot projection reads must be reconstructible from `(catalog, state)`.**

That is the same guarantee Part 3's first rule already asks for, stated over the projection instead
of over the engine, and it is the version a test can check.

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

Two more are worth having as guards rather than behaviour tests, both stated once and enforced against
every field added later:

4. **No type under `State/` has a field whose type is declared under `Content/`**, other than an id. A
   reflection test over the two namespaces.
5. **Every value the snapshot projection reads comes from `(catalog, state)`** — nothing from an
   engine field, a static, or a clock. Building a snapshot from a state tree, round-tripping that
   state through the save, rebuilding, and comparing the two snapshots catches it: any value sourced
   outside `(catalog, state)` differs, and with programs running at phase 0 that difference is a
   behaviour change rather than a cosmetic one.

## Layering

```
Dimenship.Core
  Content/       catalog records, JSON contracts, loader, validation   — no reference to State
  State/         WorldState tree, seeder, save/load, migrations        — references Content ids only
  Simulation/    engine, over (catalog, state)
  Production/    schematics, task bodies
  Planning/      planner, IWorldView
  Presentation/  BaseGraphLayout, projected from (scenario, state)
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
| `BaseGraphLayout.ForDefaultWorld()` | Projected from the retained scenario's slots plus each slot's built flag |
| `SimulationEngine` private collections | `WorldState`, exposed as `State` |
| `WorldSnapshot.Tick`, engine `_tick` | `WorldState.Clock`, which also carries TimeFlow |
| `EnergyState` alone | `EnergyLedger` **and** `ComputeLedger`; a fuel-burning `ReactorInstance` |
| `PostponeReason` (6 values) | Plus `ComputeDeferred`, `RouteUnsafe`, `PrerequisiteMissing`, `InsufficientFuel`, and a declared total order |
| Constructor validation | Content link phase, collected rather than thrown |
| `Commit` returns `TaskId[]` | Also records a `CommittedPlan`; return type unchanged |
| `IWorldView.Schematics.IsUnlocked` | `IWorldView.IsUnlocked` |
| `ProgramValidator.Validate(program, WorldDefinition, gate)` | `(program, ContentCatalog, WorldState, gate)`, or `IWorldView` in place of the pair |
| `ProgramVocabulary`'s `WorldDefinition.CreateDefault()` call | The catalog and state the shell already holds, through `ShellContext` |
| `ProgramId`, `RuleId` in `Ids.cs` | Plus `ProgramInstanceId`: a definition, a rule within it, and an installed copy are three different things to name |

`WorldSnapshot`, every panel, `Dimenship.Shell` and every `.tscn` are untouched. The existing 100-plus
tests should survive on a rebuilt `WorldBuilder`, and any that do not are pointing at real behaviour
change and should be read rather than patched.

## Out of scope

- **Mission mechanics** — travel, hazard, yields, dock behaviour. Shape only, here.
- **Program semantics** — the rule-card vocabulary, the condition and action sets, conflict resolution,
  and the compiled command model. This spec assigns programs a tier and an id space and stops.
- **Robots, modules and doctrines**, beyond the frame/instance shape.
- **The case graph and readiness evaluation.** Readiness is derived, so the rule already covers it.
- **What raises and clears an alert.** The ledger is specified; the conditions are a diagnostics spec.
- **TimeFlow presentation** — how 2x and 4x group events, and what auto-pause interrupts on.
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

## Closed since the first draft

Four of the original eight are answerable from material that has arrived since, and leaving a
decidable question open is how a spec acquires the reputation of not deciding anything.

- **Upgrades are permille fields, not levels.** This was the first draft's own recommendation and
  nothing has argued against it. A `Level` referencing an upgrade table stays available and would
  write to the permille fields anyway, so it is a later addition rather than a different design.
- **`TimeFlow` is session state. Every load resumes at 0×.** The programming view gates `ACTIVATE`
  on TimeFlow being 0×, so a load at 0× puts the player in the one state where every program action
  is available, and a save that resumed at 4× would resume a vessel moving before its owner had
  looked at it. §11.2's operational-timestamp requirement is satisfied by `Tick`, which is saved
  regardless. `OperationalClock` keeps `Flow` as a field — the engine still needs to hold it — and
  the save simply does not carry it. `AutoPauseOnCriticalAlert` **is** saved: it is a preference the
  player set, not a speed they left the vessel at.
- **The journal is saved in full, at 512 events.** A few tens of kilobytes, and the revisit now has
  a trigger rather than a vague "if a save gets large": the programming spec's open item 6 observes
  that verbose automation tracing evicts a 512-entry buffer within seconds. Whatever answers that —
  a per-category buffer, a larger bound — answers this at the same time, and the two should be
  decided together.
- **The utilization window stays on the executor.** It must be saved and it is inherently
  per-executor; a telemetry service would need the same per-executor storage plus a lookup, and
  would still be state.

## Open questions

1. **Does a facility's buffer belong to the archetype or the instance?** Archetype here, on the grounds
   that buffer size is what a *Mk. II Refinery* is. If a buffer becomes independently upgradable, it
   moves to the instance and the seeder changes.
2. **Is a mission dock a fourth `FacilityType`, or an executor kind of its own?** It has a queue and a
   status like a facility, but it runs acquisitions rather than schematics. Deferred with missions.
3. **Is reactor fuel conversion a schematic, or a reactor mechanism?** Example E reads both ways. As a
   schematic it needs no new machinery — the reactor becomes a facility whose output is `ReactorFuel` —
   but "convert the most abundant refined material" is a choice no schematic can express, because a
   schematic's inputs are fixed. Recommendation: schematic for the conversion, program for the choice.
4. **How does a save survive a program's vocabulary changing?** §9.1 asks for migration rules when an
   update changes resources or actions. `contentVersion` detects it; what to *do* about a player program
   referencing a deleted action — refuse the save, disable the program, or drop the rule — is a real
   product decision and is not this spec's to make.

## Amendment log

- **2026-08-10, initial.** Three tiers, JSON content, serialisable `WorldState`.
- **2026-08-10, after review.** State is held to ids and deltas: `CapacityPermille` and three `Label`
  fields left the tree, `StorageArchetype` was added for symmetry, names became `NameOverride`.
- **2026-08-10, after `Game Design v0.8` and `Programming v0.1`.** A fourth tier, authored content, for
  player-written programs. Scenario retained rather than discarded, which returns placement to content
  and reverses the previous amendment on that one point. Clock and TimeFlow, seeded RNG, an alert
  ledger, utilization windows, a compute budget, a fuel-burning reactor, facility integrity, a wider and
  now ordered `PostponeReason`. Programs, robots and the case graph declared and deferred. "Expedition"
  became "mission".
- **2026-08-12, completeness pass and reconciliation with the programming view.** Every type the
  document names now has a home: `reactors.json` and `programs.json` in the catalog layout, `Reactors`
  and `Programs` on `ContentCatalog`, the four undeclared scenario records written out, reactors and
  both ledgers named in `VesselState`. Registries save their own id counters, `RngDomain` makes the
  stream array indexable and its values advanced state rather than seeds, the version stamps stop being
  held in two places, and the utilization divisor became elapsed ticks so the percentages total 100
  before the window fills.

  Against `2026-08-11-programming-view-design.md`: the program language is cited rather than restated,
  which removes six field-level disagreements between the two documents; `ProgramParameter.Current`
  moves out of the definition and into `ProgramInstance.Parameters`, where the tier rule already put
  it; cooldowns become per-`RuleId`; reservations gain a ledger and are owned per instance rather than
  per definition; `Priority` joins the task body. Phase 0 evaluating programs against the previous
  tick's snapshot makes the projection's inputs load-bearing, so a fifth guard test asserts that every
  value the snapshot reads comes from `(catalog, state)`. Four open questions closed — permille
  upgrades, a full journal, utilization on the executor, and TimeFlow as session state.
