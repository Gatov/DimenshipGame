# Bot Composition — World Data Structure Review

Date: 2026-08-21
Reviews: `docs/superpowers/specs/2026-08-21-bot-composition-design.md` (Draft)

## Verdict

The design is implementable against the current kernel, and its one declared state change —
`Robot.Installed` — is correctly identified as the largest. But it is not the only one, and it is
not the one with a deadline attached.

Six further changes are load-bearing and unstated. Three of them are places where the current code
does not merely lack the feature but would **actively do the wrong thing** if a socket storage
appeared in it: the vessel-wide stock roll-up, the scenario loader's storage-placement rule, and the
postponement reason a category-mismatched transport would report. Two are gaps in the state model
that nothing has needed until now: how a socket is addressed, and the absence of any way to mint a
storage or executor id at runtime. One is a vocabulary collision of exactly the kind the design
spends a section preventing, which it then misses.

Nothing in `src/` is changed by this review. It records what a build-out must do and what it must
not assume.

## What was reviewed

`State/Ledgers.cs`, `State/StateIds.cs`, `State/WorldState.cs`, `State/VesselState.cs`,
`State/ScenarioSeeder.cs`, `State/Save/SaveFile.cs`, `State/Save/WorldSave.cs`,
`Content/Archetypes.cs`, `Content/ContentIds.cs`, `Content/ContentCatalog.cs`,
`Content/Scenario.cs`, `Content/JsonContentSource.cs`, `Content/Json/ContentFiles.cs`,
`Simulation/SimulationEngine.cs` (storage, volume and snapshot paths), `Planning/IWorldView.cs`,
`Presentation/BaseGraphNodes.cs`, and the shipped content tree under `dimenship/content/`.

---

## Part 1 — What the design names, checked against the code

### 1.1 `Robot.Installed` is wrong, and the window to fix it for free is open now

The design says a `List<ModuleId>` cannot express an empty socket, and that is right
(`State/Ledgers.cs:395`). What it does not say is that **the reshape costs nothing today and will
cost a save version tomorrow**, which is the fact that should decide when it happens.

Three things are true at once right now:

- Nothing mints a robot. `ScenarioSeeder` builds `new RobotLedger()` and `Scenario` has no robots
  to seed from, so `RobotLedger.Robots` is empty in every world that has ever existed.
- `WorldSave.Capture` projects that empty list, so a saved file contains `"robots": []` and **no
  `RobotDto` object at all** — the `installed` key has never been written to any save.
- `RobotDto` carries `[JsonUnmappedMemberHandling(Disallow)]` (`State/Save/SaveFile.cs:491`), so
  the moment one *is* written, renaming or reshaping the field turns every save carrying it into a
  load error rather than a silent default.

So the change is free until the first robot is minted, and from then on it needs `saveVersion` 2
and the first entry in `WorldSave.Upgraders`, which is `Array.Empty<ISaveUpgrader>()` and has never
been exercised. **Reshape `Installed` before anything constructs a `Robot`, not after.** That is a
sequencing constraint on the build-out, not a design question.

The same window covers two renames the design already wants and one it does not mention:

| Change | Why now |
| :--- | :--- |
| `ModuleId` → `FittingId` | The design says it, and says it is free. It is: no content names one, no save holds one. |
| `RobotFrameId`, `ModuleId`/`FittingId` move to `Content/ContentIds.cs` | Both carry the doc comment *"Catalog, when the domain arrives"* while living in `State/StateIds.cs` under namespace `Dimenship.Core.State`. Every other catalog id — `FacilityArchetypeId`, `StorageArchetypeId`, `ReactorArchetypeId`, `StratumId`, `PowerSinkId` — lives in `Content/ContentIds.cs`, and state references them qualified, as `Mission.Target` does with `Content.StratumId`. Two catalog ids filed under state is the tier boundary blurred in the one place the four-tier model is easiest to get wrong. |

### 1.2 §9 is a live defect, not a precaution

The design asks that socket storages be excluded from `WorldSnapshot.Resources` and planner
availability. The code makes this mandatory rather than advisable, and it is worth being exact
about why.

A socket has to be a real `StorageInstance` in `VesselState.Storages`: `TransportTask.Destination`
is a `StorageId`, and the engine resolves one through `_storagesById`, which is built from that
list. Nothing can be transported into a storage that is not in it. But the vessel-wide total is:

```csharp
private long TotalOf(ItemId item)          // SimulationEngine.cs:1196
{
    var total = 0L;
    foreach (var storage in State.Vessel.Storages)
        total += Available(storage.Id, item);
    return total;
}
```

`TotalOf` feeds `WorldSnapshot.Resources` (`SimulationEngine.cs:1226`) **and** `Uncommitted`
(`SimulationEngine.cs:545`), which is `IWorldView`'s availability and therefore what
`ProductionPlanner` spends. So the instant a socket storage joins the list, a drill bolted into a
working robot is counted as stock the planner may allocate — the exact failure §9 describes,
arriving without anyone having to write a line of new code.

**The exclusion needs a property the engine can read, and the archetype is the place for it.** A
socket archetype is excluded from the roll-up; everything else is not. That is one predicate, read
in two call sites, and it does not need a second list on `VesselState` that could disagree with the
first.

### 1.3 §2's accept-filter falls out of the existing volume math — and lands on the wrong reason

The good news first. `CapacityOf` already documents the behaviour a category filter needs:

> *"An item the storage has no capacity for gets no room at all rather than a division: a storage
> that cannot hold a thing has nowhere to put it."* — `RoomIn`, `SimulationEngine.cs:267`

A socket archetype whose `CapacityOf` returns 0 for a fitting of the wrong category therefore
rejects it through machinery that already exists, with no new branch in `Room`, `OccupiedVolume` or
`RoomAfterConsuming`. The design's estimate of the cost — one nullable field and one branch in the
room calculation — is accurate for this half.

The bad news is the diagnostic. A transport that cannot deposit postpones with `DestinationFull`
(`Simulation/Ids.cs:141`, *"A run finished and its output would not fit. The work is held, not
lost."*). For a socket that is genuinely occupied that is exactly right and is the design's own
worked example. For a **drill routed to a Mobility socket** it is a lie of the specific kind this
repository legislates against: a permanent, unsatisfiable mismatch reported as transient
congestion, on a task that will retry forever and a telemetry panel that will show a full
destination that is empty.

This needs either a distinct `PostponeReason` — the enum is saved by name, not by index, so adding
one is safe (see 3.4) — or a rejection when the transfer is enqueued. It should not be left to fall
through `DestinationFull`.

### 1.4 The capacity-of-one rule has no unit

Every item quantity in the kernel is milli-units, and `TransportArchetype.ThroughputPerTick` is
milli-units per tick. "A socket holds exactly one fitting" does not say whether one fitting is `1`
or `1000`, and the answer is not cosmetic: it sets how long a transport line takes to move a part,
whether a partially-moved fitting is representable at all (`TransportTask.MovedQuantity` is a
`long` and the engine moves what it can), and what `CapacityOf` must return.

The clean answer is that a fitting is one whole unit, `1000` milli-units, and that a socket's
capacity is `1000` — partial movement is then invisible only if throughput exceeds it in one tick,
which shipped transports do not guarantee. The alternative — fittings counted in ones — makes a
fitting the only item in the game not in milli-units, which is the kind of exception that survives
review and then breaks a helper six months later.

**This must be settled before the first fitting is authored.** It is a one-line consequence of a
decision nobody has made.

---

## Part 2 — What the design does not name, and the code requires

### 2.1 How a socket is addressed is undefined, and the obvious answer is the `RngDomain` failure

The design says a frame declares an **ordered list of sockets** and that `Installed` becomes a
"socket-indexed structure of storage ids". Read literally, socket-indexed means indexed by
position, and position is the failure this repository already names in its most-quoted comment:

> *"Members are append-only and never reordered: **the index is the save format**. Inserting a
> domain in the middle re-points every stream in every existing save."* — `RngDomain`,
> `State/StateIds.cs`

A frame's socket list has precisely this property under positional addressing. An author who
reorders a frame's sockets in `frames.json` — to put Mobility first, to group the hardpoints —
silently re-points every saved robot's fittings onto different sockets. Nothing fails, nothing
reports, and a drill is now in the Systems socket. Content order becomes save format for a list
whose order the design explicitly wants authors to treat as presentational ("readable at a glance").

**Recommendation: sockets carry authored ids.** A `SocketId` per frame, matching the catalog id
pattern, and `Robot` holds `SocketId → StorageId`. Reordering is then free, renaming a socket is
content drift, and content drift is a thing `WorldSave.CheckDrift` reports and refuses to absorb —
which is the contract the rest of the save format keeps.

### 2.2 Nothing can mint a storage or an executor at runtime

Every entity the engine creates during play has a saved counter, for a reason `PlanId` states
plainly: *"a registry that restarts from zero after a load mints an id already in use, which is not
a crash and not visible until two entities that were never the same are indistinguishable."*
`TaskRegistry.NextTaskId`, `PlanRegistry.NextPlanId`, `MissionLedger.NextMissionId`,
`AlertLedger.NextAlertId`, `RobotLedger.NextRobotId`, `ProgramLedger.NextInstanceId` — all present,
all saved.

`StorageId` and `ExecutorId` have none, because nothing has ever created one mid-run. `VesselState`
is seeded wholesale from the scenario and never grows.

Both this design and the refit spec require that it grow:

- **A robot built during play needs its socket storages to exist**, and they cannot be authored in
  content because the robot was not.
- **The refit spec's docking rule is topology**: *"A robot's socket storage is reachable by
  transport only while that robot is docked at the facility doing the work"*, which is what makes
  "a deployed robot cannot be reconfigured" a fact about the graph rather than a guard clause. A
  line that exists only while docked is a `TransportInstance` created, repointed or unbuilt at
  runtime, and `TransportInstance.From`/`To` are settable, so the repointing half is already
  possible — but the executor still has to come from somewhere.

**Recommendation: derive socket storage ids rather than mint them.** A socket storage id built
deterministically from the robot id and the socket id needs no counter, cannot collide, replays
identically, and is legible in a save. The instances still have to be inserted into
`VesselState.Storages` in a defined order — declaration order is the determinism contract, so
"appended when the robot is built" is the rule, and it must be stated rather than assumed.

### 2.3 A starting robot cannot be authored: its sockets fail content load

§7 puts starting robots in the **Scenario** tier, alongside storages and facilities. Two things
block that today.

`Scenario` has no robots field at all (`Content/Scenario.cs`) — expected, and a straightforward
addition. The blocking one is not:

```csharp
// JsonContentSource.cs:666 — a storage with no cell of its own must be drawn inside some
// facility's card. One that is neither placed nor claimed would be dropped from the graph
// silently, which is the failure BaseGraphNodes returns nothing for rather than guessing at.
if (storage.Placement is null && !buffers.Contains(storage.Id))
    errors.Add(new ContentError(path, ..., $"'{storage.Id}' has no cell and is no facility's
        local storage, so nothing would draw it."));
```

A scenario-authored robot's socket storages are neither placed on the schematic nor any facility's
local buffer, so **authoring one is a content error today**, and the invariant is asserted a second
time in `BaseGraphLayoutTests.EveryStorage_IsEitherPlaced_OrOneFacilitysBuffer`.

The rule itself is sound and should not simply be relaxed — it exists because an unplaced,
unclaimed storage vanishes from the base graph without saying so. What it needs is a third
legitimate answer to "who draws this": *a socket belongs to a robot, and robots are drawn in the
Robotics screen rather than on the vessel schematic.* That is a real decision, and it overlaps the
design's own open item **"Do fittings appear on the base graph at all?"** — which turns out not to
be a presentation question that can wait, but a content-loader invariant that blocks the scenario
tier.

### 2.4 The authored-content tier has no home for a template

§7 puts a named loadout template in the **authored content** tier, `user:`-prefixed, "beside
programs". There is no beside. `ProgramLedger` is a counter and a comment:

```csharp
/// Installed programs, and the counter that mints their ids. Declared, not filled: the program
/// language and the authored-content tier are separate work, and this is the seat they take.
public sealed class ProgramLedger { public long NextInstanceId { get; set; } ... }
```

Templates are player-authored data and must be saved, so this needs a ledger, a save DTO with
every field nullable, and sorted-set/ordered-array write discipline like every other DTO. The id
type is the easy part — `ProgramId` already models exactly the `user:`-prefixed string, and a
`LoadoutTemplateId` should mirror it rather than invent a second convention.

Worth noting positively: §7's load-bearing rule — *a robot does not follow its template; it was
built to it* — means the template is referenced by **nothing** in `Robot`. No back-pointer, no
sync, no staleness. That is the cheap half and the design got it right.

### 2.5 `robot_frame` is a third collision, and the design misses it

The design spends a full section separating `fitting` / `socket` / `module` / `slot`, and it is
convincing. It then introduces `FrameArchetype` and inherits `RobotFrameId` without noticing that
**`robot_frame` is already a shipped bulk commodity**:

```json
{ "id": "robot_frame", "label": "Robot Frame", "holdCapacity": 60000 }
```

produced by `assemble_frames` from 100 `module` per 50 output, and the terminal product of the
shipped factory chain. So "Robot Frame" is a fungible item in Resource Storage, while
`FrameArchetype` / `RobotFrameId` names the class of machine a robot *is* — "Utility Frame", "Light
Frame". These are as different as the `module` pair the design separates, and they are one word
apart.

The likely resolution is benign and should be written down rather than left to be inferred:
**the `robot_frame` commodity is what a robot of a given `FrameArchetype` is built from** — the
chain terminates in a part, and the part plus fittings is a machine. But an implementer reading
`RobotFrameId` next to `ItemId("robot_frame")` with nothing between them will eventually decide
they are the same id space, and the symptom is a frame archetype that has to exist as an item or an
item that has to exist as a frame.

**This belongs in the design's vocabulary table as a fifth row**, in the same register as the other
four.

### 2.6 §3's "never in Resource Storage" is not expressible with the field §2 budgets for

§3 keeps fittings fungible, which keeps a socket an ordinary `ItemId → long` ledger. Inherited from
the refit spec, though, is a stronger claim: **fitted equipment is never in Resource Storage** —
only in a socket, a facility buffer, or in flight.

§2 adds an accept-category to the *socket* archetype. That says what a socket takes; it says
nothing about what the hold refuses, and `global_hold` accepts every item by construction —
`CapacityOf` is `item.HoldCapacity * archetype.CapacityPermille / 1000` and knows nothing else.

The tempting trick does not work. Authoring fittings with `holdCapacity: 0` would make `RoomIn`
return 0 for the hold and reject them elegantly — except that (a) the loader rejects it outright,
since `holdCapacity` goes through `Positive` (`JsonContentSource.cs:263`), and (b) a facility
buffer is 25 permille of the same number, so zero locks fittings out of buffers too, and a fitting
must be able to sit in a buffer or the entire refit path has nowhere to put a removed part.

So the rule needs **an item class on `ItemDefinition`** (material / component / fitting) and a
storage-side rule that reads it — a second field in a second record, not the one nullable field the
design budgets. It is still small. It is not what was estimated, and the estimate is the kind a
plan gets sized from.

### 2.7 §5 implies per-stat content with no record to hold it

*"Clamping is left to the stat rather than the sum: some stats floor at zero, some do not, and that
is per-stat content rather than a global rule."*

Per-stat content needs somewhere to be. Either the stat set is an enum with the floors compiled in
— which makes "per-stat content" untrue but is defensible for a fixed set — or there is a fourth
new catalog record holding `(id, label, floor)`. The design names `FrameArchetype` and
`FittingArchetype` and stops. Whichever way it goes, the frame's base values and the fitting's
deltas have to key against the same stat identifier, and nothing currently defines one.

---

## Part 3 — What needs no change, recorded so nobody changes it

### 3.1 Most of `Robot` is right

`Id`, `Frame`, `NameOverride`, `IntegrityPermille`, `Group`, `OnMission` all survive. §3 puts the
entire damage model on `IntegrityPermille`, which already exists with the right semantics — the
`FacilityInstance` comment applies verbatim: *"Degraded, damaged and in-maintenance are this field
read at different values, rather than three flags that can disagree."*

### 3.2 The two budgets add no state, and that is the design's best structural decision

§4's power comparison and mass comparison are integer checks performed when a loadout is assembled
and when a mission launches. Neither `EnergyLedger` nor `ComputeLedger` is touched, no third ledger
appears, and `RngDomain` gains no member because nothing here draws randomly. The refusal to model
field energy is what keeps that true, and it should be defended in review of any later change that
describes itself as "just tracking the bot's charge".

### 3.3 §6 and §8 are content and projection, not state

- **§6**, mobility efficiency: `StratumDefinition` gains terrain classes and mobility fittings gain
  a permille per class. `strata.json` ships empty, so there is no migration at all. The scaling
  lands once, at launch, on `Mission.ArrivesAtTick`, which is already state. Nothing new is stored.
- **§8**, launch readiness: derived from the robot, its sockets and its doctrine at the moment of
  launch, so it is a `WorldSnapshot` projection and must never be stored. The precedent is stated
  in `CaseLedger`: *"readiness is derived from the case graph, so it is snapshot-only and never
  state: a readiness evaluator that stored its conclusions would be a second source of truth."*
  `WorldSnapshot` has no readiness field yet; that is the work, and it is projection work.

### 3.4 Enum additions are safe here, unlike `RngDomain`

Worth stating because the `RngDomain` comment is emphatic enough to be over-generalised. Saved
enums are written by **name** — `a.Severity.ToString()`, `a.Code.ToString()`,
`a.RootCause?.ToString()` in `WorldSave.Capture`, read back through `Enum.Parse`. So a new
`PostponeReason` (1.3), a new `AlertCode`, or a `SocketCategory` in any order are all safe.
`RngDomain` is the exception precisely because it is indexed into an array, and that is why its
comment says what it says.

### 3.5 `CheckDrift` has no robot arm — an absence, not a defect

`WorldSave.CheckDrift` checks storage archetypes, stocked items, facility archetypes, configured
schematics, transport archetypes, sinks, task schematics, task items and unlocked schematics. It
checks nothing about robots, because there is nothing to check. When the domain lands it needs
frame ids, fitting ids and socket ids added, in the same collect-don't-throw style — and 2.1's
authored socket ids are what make the third of those checkable at all.

---

## Recommended order

1. **Settle the two vocabulary items** — `robot_frame` versus `FrameArchetype` (2.5), and whether a
   fitting is `1` or `1000` (1.4). Both are one-line decisions that constrain everything after.
2. **Take the free window** — reshape `Robot.Installed` to socket-keyed storage ids with authored
   `SocketId`s (1.1, 2.1), rename `ModuleId` → `FittingId`, and move both robot catalog ids to
   `Content/ContentIds.cs`. No save version, no upgrader, provided it lands before the first robot
   is minted.
3. **Catalog tier** — `FrameArchetype`, `FittingArchetype`, the stat identifier (2.7), the item
   class (2.6), and the accept-category and count capacity on `StorageArchetype`. Two new required
   catalog files, registered in `manifest.json`, `RequiredCatalogFiles`, `ContentFiles.cs` and
   `ContentJsonContext`, with link-phase validation. The `programs.json` precedent applies: the
   file exists, required and empty, before the schema that fills it.
4. **Engine seams** — the roll-up exclusion (1.2), the postponement reason (1.3), and deterministic
   socket-storage derivation with a stated insertion order (2.2).
5. **Scenario and authored tiers** — starting robots, the third answer to "who draws this storage"
   (2.3), and the loadout template ledger and DTO (2.4).
6. **Projection** — launch readiness in `WorldSnapshot` (3.3), and the Robotics panel, which is
   still a `PlaceholderPanel` and its own piece of work.

## Questions this review puts back to the design

- **Is a socket addressed by authored id or by position?** The review recommends authored id and
  gives the failure; the design should say so explicitly, because the wording currently reads as
  positional.
- **Where does a fitting item's definition live relative to its archetype?** §3 requires a fitting
  to be an item, since a socket is an `ItemId → long` ledger. Is a fitting one record in a new file
  that also implies an item, or a row in `items.json` plus a facet keyed by the same id? The design
  never says, and the loader's link phase needs to know which id space it is resolving into.
- **Does a socket storage get a graph presence?** Recorded in the design as an open item about
  fittings on the base graph; this review escalates it, because the loader's placement invariant
  (2.3) makes it a blocker on the scenario tier rather than a presentation preference.
- **Is the roll-up exclusion a property of the socket archetype?** The review recommends yes, so
  that one predicate serves both `TotalOf` and `Uncommitted` and there is no second list to
  disagree with the first.
