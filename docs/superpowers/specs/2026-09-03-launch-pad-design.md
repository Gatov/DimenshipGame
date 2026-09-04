# Launch Pad 1 — Design

Date: 2026-09-03
Status: Draft

## Goal

Give the player one verb: compose a plan, read it, approve it, watch its tasks run, and get the
vessel back when it completes. The first plan is **Build Launch Pad 1**. The vessel **opens quiet**
— both Mission Docks unbuilt so that plan has somewhere to land, and no factory standing orders so
the plan is the first work the factories see.

Today the vessel runs itself and the player watches. Nothing in `dimenship/` ever calls
`SimulationEngine.Enqueue`. This step is the first player command into the kernel.

## Source material

- **Launch Pad Brief** (`plan-poc.md`, 2026-09-03) — the decisions below are that brief, settled.
  Authoritative for scope and for what is not added.
- **`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`** — construction
  unit → socket, and the `module` / `slot` collision. Binding on vocabulary and on the rule that
  building a facility fills an authored slot rather than creating geometry. This step ships a
  deliberate interim: local storage stands in for the socket until sockets exist (see Decision 3).
- **`docs/superpowers/specs/2026-08-11-programming-view-design.md` §Conditions / Operands
  (lines 262–305)** — the `Condition` / `Operand` / `Comparison` records this step lifts into
  `Dimenship.Core/Programs/`. Two kinds ship; the rest stay on that document.
- **`docs/superpowers/specs/2026-07-30-production-planning-design.md`** — the executor / storage /
  task model plans are composed from. Shortages as a planner output are retired here; see
  Decision 6.
- **`docs/Game Design v0.9.md` §5.8 / §5.9** — facility types and the material chain. Vocabulary
  tiebreaker when documents disagree.

## Vocabulary

Three words collide with shipped concepts if left unnamed. Ruled here before any code:

| Word | Meaning in this step | Why not the obvious alternative |
| :--- | :--- | :--- |
| **`mission_dock_construction_unit`** | An **item** — *Mission Dock Construction Unit*, `holdCapacity` 80000. One whole unit is 1000 milli-units. Produced by a Factory schematic, consumed by commissioning. | The recycling/refit spec's "construction unit" is the same idea. It is **not** a fitted `module` (the shipped bulk commodity) and **not** a socket. Naming it as an item id keeps the collision visible and the storage rules ordinary. |
| **`slot`** | Stays the authored **facility node position** (`FacilityState.BuiltAtStart` false). Placement stays in the scenario; construction fills a slot that already exists. | The recycling/refit spec's equipment socket is a different concept. That document already prefers **socket** for the holder; this step does not reopen `slot`. |
| **Local storage as socket stand-in** | Until upgrade sockets exist, an unbuilt facility's **local storage** holds the construction unit, and commissioning withdraws it from there. | A real socket storage is the recycling/refit design. Inventing one here would ship half a subsystem. The stand-in is temporary and stated; when sockets land, commissioning becomes "transport into the socket" without changing the construction-unit item. |

## Decisions

### 1. Opening: unbuilt pads, star topology, quiet vessel

Both Mission Docks are authored unbuilt and renamed Launch Pad 1 and 2; their hold lines are built.
The two factory interconnects stay authored but unbuilt. Four new hold-star routes replace the
interconnect stages so the planner can still reach every factory through the hold. Loader rule:
every commandable facility has a built route from the scenario Hold to its `localStorage` and a
built route back (the extractor is exempt by being non-commandable).

**Standing orders: wipe `initialTasks`.** The vessel opens quiet — Approve is what starts factory
and reactor work. No task on either dock — Decision 2 forbids it and the loader enforces it. Drop
factory and reactor standing transfers too; do not seed standing star hauls — the plan's own
transfers move the metal. Star **routes** (hold ↔ every commandable facility, plus dock hold
lines) stay authored built. Factory interconnects stay authored unbuilt. The extractor's out-haul
is the only standing transfer.

Build Launch Pad 1 is aimed at **near 120 ticks** from a fresh quiet start — roughly two simulated
minutes at 1×. That is a tuning target, not a hard limit and not a test assertion. With factories
idle there is no in-flight run to finish first; content is tuned so the plan lands near that window.

**Standing power barely moves.** Two unbuilt docks (−200) and two unbuilt interconnects (−400)
against four new star lines (+800) puts draw at rest near 8,100 of 10,000. `energyCapacity` does
not move. CapHits under the approved plan are what to watch when playing it.

### 2. `Built` is enforced

`FacilityInstance.Built` already exists and the engine never reads it. An unbuilt facility today
draws standing power, is stepped, is offered to the planner, and is accepted by `Enqueue`. That
ends here.

An unbuilt executor: draws nothing (`PowerDrawLastTick = 0`), steps nothing, is invisible to the
planner, is refused by `Enqueue` (Decision 4 collapses the two entry points into one), and reserves
no room. An unbuilt dock's hold
must still accept the construction unit — holding back room for output it cannot produce would be
what stops commissioning.

Scenario `initialTasks` / `initialTransfers` may not name an unbuilt executor (same shape as the
existing `Commandable` check).

### 3. Commissioning consumes one whole unit from local storage

`FacilityArchetype` gains `ItemId? ConstructionUnit` — required in JSON, `null` on facilities that
are never commissioned this way. Loader resolves it against known items.

A new tick phase, **after transport and before production**, in declaration order: for each unbuilt
facility whose archetype names a construction unit and whose local storage holds ≥ 1000 milli-units
of it, withdraw exactly 1000, set `Built = true`, emit `FacilityBuilt`. Placement between the two
loops is the determinism contract: a unit delivered this tick commissions this tick, and the
facility produces from the next.

Nothing builds a line. A line's `Built` is authored and stays authored this step.

**Until sockets exist, do not also implement socket delivery.** The recycling/refit design
commissions by transporting the unit into an upgrade socket (`2026-08-20-recycling-refit-and-
construction-design.md`, construction section). This step uses local storage and a consume phase
instead. Stage 3 implements only the interim; when sockets land, commissioning becomes delivery
into the socket and this phase goes away — the construction-unit item does not change.

**Rejected:** a construction timer on the target facility. The recycling/refit document already
rejected that for upgrades — factory occupancy is the cost, and a second scheduler beside the real
one is invisible to utilization and unreachable by programs. Commissioning here is "the unit
arrived", not a second duration model.

### 4. A task is a script

Production and transport collapse into one shape:

```csharp
public abstract record TaskAction;
public sealed record Produce(SchematicId Schematic, int? Runs) : TaskAction;
public sealed record Transfer(ItemId Item, long? Quantity, StorageId From, StorageId To) : TaskAction;

public sealed record TaskScript(IReadOnlyList<Condition> Conditions, TaskAction Action);
```

`TaskInstance` is one mutable sealed class with a flat progress union (runs / work / energy / moved
quantity) rather than a nested `Progress` record — one save DTO is what makes the file diffable.

One `Enqueue(TaskScript, ExecutorId)` replaces both entry points. One registry list, one snapshot
list, one save DTO. **`WorldSave.CurrentVersion` stays 1** — there are no saves in the wild; an
upgrader for a format nobody wrote would be a fiction.

Conditions gate **starting** only: checked at the top of selection, before any other reason; false
→ `Postpone(..., ConditionNotMet)`, retry next tick. They never touch a run already in flight.
Empty `Conditions` means attempt every tick — attempt semantics stay byte-identical to today for
every task the planner and the scenario produce.

**"In flight" is a producer's word, and a transfer has no equivalent.** A run spans ticks, so
`StepProducer` returns before selection while one is active and the gate never sees it. A transfer
withdraws and deposits within the same tick, so a part-moved haul is a task that has started this
many times, not cargo hanging in a tube: its conditions are evaluated on **every tick it moves**.
Gating only the first tick would leave the rest of a long haul unconditioned, which is the opposite
of what a condition is for.

`PostponeReason.ConditionNotMet` is **appended last**, with event `PostponeConditionNotMet`.
Declaration order is root-cause priority; a task whose condition is false and whose inputs are also
missing should report the missing inputs.

This **adds** a `PostponeReason` and an event code. The recycling/refit design listed "no new
`PostponeReason`" among what facility construction must not invent
(`2026-08-20-recycling-refit-and-construction-design.md`, *What is explicitly not being added*).
That rule still holds for construction and refit themselves — commissioning does not stall on a new
reason. `ConditionNotMet` belongs to the task-script / program layer this step also ships, and is
the deliberate exception.

### 5. Conditions ship as mechanism only

Lifted into `Dimenship.Core/Programs/` from the programming-view design:

- `Condition(ConditionKind, IReadOnlyList<Operand>, Comparison, Operand)`
- `Comparison`
- `Operand` hierarchy: `Literal`, `TargetRef`, `EnumRef`; `ParameterRef` declared and **refused at
  enqueue** (a parameter has no binding outside a program). Unknown targets are refused at enqueue
  as well (brief Decision 5).

`ConditionKind` ships two members only: `StorageItemAmount` and **`ExecutorStatus`**. The
programming-view table named the latter `ExecutorStatusIs`; this step takes the shorter name for the
enum member that reads `ExecutorState.Status`. Evaluated against **live state**, not the snapshot —
a task is gated by what is true now. `EnumRef` is saved by name (wire), even though the in-memory
record carries `(string Kind, int Value)`: `EnumNames` holds both directions of that translation,
and no ordinal reaches the file, because an ordinal on disk is a number whose meaning is a C#
declaration order.

A save carries a task's conditions as it carries its action. Every id a condition names is checked
against the catalog and the world on load, the same question `Enqueue` asks when a task is created —
a save bypasses that path, and a condition that can never be true is a task that never starts for a
reason nobody can see.

Every shipped task has empty `Conditions`. The first real caller is the program runtime. Building
and unit-testing the evaluator with no shipped caller is deliberate: the records belong in Core now
so the task script is not a second condition language later.

### 6. A plan is its tasks

```csharp
public sealed record ProductionPlan(
    ItemAmount Goal,
    StorageId? Destination,
    IReadOnlyList<PlannedTask> Tasks,
    IReadOnlyList<Unplannable> Unplannable,
    long EstimatedTicks);

public sealed record PlannedTask(TaskScript Script, ExecutorId Executor, long AvailableAtSource);
public sealed record Unplannable(ItemId Item, long Quantity, UnplannableReason Reason);
public enum UnplannableReason { LockedSchematic, NoExecutorOrLine, CyclicSchematic }
```

**`ShortageKind.RawResource` is deleted, not renamed.** An item nothing produces is no longer a
shortage: the planner still emits the transfer for the full amount, and that transfer postpones on
`InsufficientSourceMaterial` until material arrives. `AvailableAtSource` on each transfer replaces
the shortage display.

**Ordering is preserved exactly.** Tasks are built in the order `Commit` used to enqueue them:
every transfer of a schematic's inputs, then its run. Declaration order is the determinism contract;
a merge that reorders would be a silent bug.

**`Destination`** appends one final transfer, hold → destination, for the goal amount. Build Launch
Pad 1 is `Plan({mission_dock_construction_unit, 1000}, destination: dock_a_hold)`.

**`EstimatedTicks`** is the busiest executor's total — a documented lower bound. It ignores
switch-over, queueing behind existing work, and energy contention.

**`Commit`** enqueues `plan.Tasks` in order, records a `CommittedPlan` with `Destination` and
without `Shortages`, emits `PlanCommitted` and one `PlanUnplannable` per entry. `PlanShortage` is
deleted. **`PlanCompleted`**: when the last spawned task retires, the plan finishes and the driver
pauses — the player's next turn.

### 7. Snapshot and events

Snapshot gains: `Built` on facility and transport executor states; one `Tasks` list of
`TaskInstanceState`; a `Plans` list of `CommittedPlanState` (`Id`, `Goal`, `Destination`,
`CommittedAtTick`, `SpawnedTasks`, `CompletedTasks`, `State`) with **no shortages** — a stale
shortage is worse than none.

Events: `FacilityBuilt`, `PlanCompleted`, `PlanUnplannable`, `PostponeConditionNotMet`. Category
for `FacilityBuilt` is `Production`.

### 8. Shell: Operations is live; the graph stopgap ends

The `processes` panel id does not move (persisted in `user://layout.json`). The view is titled
**Operations** so alphabetical focus order keeps `Ctrl+2` where players already have it — Base
Graph, Operations, Programs, Robotics.

Layout follows the two shipped composers: plan list left, detail-or-composer right. Composer is
Build/Produce, target, quantity, live preview of tasks in instruction form, `EstimatedTicks`, and
`Unplannable`. **APPROVE** is live: `ShellActions.PlanApproved` → `SimulationDriver.Commit(plan)`.
The composed plan is discarded on approve and never stored.

`SimulationDriver` gains `Plan` and `Commit` — the first calls into the engine that are not
`Advance`. Both refuse after a fault; both rebuild the snapshot. It watches `PlanCompleted` in
`RecentEvents` and pauses.

**The graph stopgap is fixed in this step.** `BaseGraphFocus` takes its layout from the driver's
world (not a second `ShellContent.NewWorld()`), and a `FacilityBuilt` event re-chromes the card.
Unbuilt cards and lines draw dimmed through palette-driven style variants — no literal colours.
Without this fix a dock commissioned mid-run never un-dims and the payoff is invisible.

Inspector: an unbuilt executor shows `STATUS / UNBUILT` and `PLAN / Plan construction…` as
display-only text this step. The Operations view is where the plan is composed.

### 9. Content for the first plan

- Item `mission_dock_construction_unit`, schematic `assemble_dock_unit` (inputs: **only**
  `basic_metals` — a brief extension so the first plan is one factory run rather than a recursive
  expansion of the whole chain), `constructionUnit` on `mission_dock`, two generic factory line
  archetypes (`factory_feed` / `factory_return`) **which the four hold-star legs run on**, and the
  scenario edits in Decision 1 (including wiping factory `initialTasks`). The schematic must be in
  the scenario's `unlockedSchematics`: the planner reports `LockedSchematic` and `Enqueue` refuses
  the task otherwise, so an unlisted schematic is a headline plan that cannot be composed at all.

**A whole unit is half a facility buffer, and that is load-bearing.** `holdCapacity` 80000 against
`facility_buffer`'s 25‰ gives a buffer capacity of 2000, so one unit's 1000 milli-units is half of
it. It cannot be 40000. A facility's buffer reserves one run's output at the schematic it is set up
for and transport subtracts that reservation (`2026-08-20-shared-hold-volume-design.md`,
Decision 6), so at 40000 a factory configured for `assemble_dock_unit` reserves its **entire**
buffer and reports zero room for every item — including the 400 Basic Metals its next run needs.
`Configured` never clears, so that factory stays un-deliverable-to for the rest of the campaign. The
reservation rule is deliberate and stays; the content is what moves.

**The hold-star legs run on the hub archetypes**, not on the interconnect archetypes they stand in
for. A star leg carries whatever a plan puts on it rather than one stage's flow, which is what
`factory_feed` / `factory_return` were added for; the per-stage sizing rule's exception is theirs.
Sized to the component stage at 13 a tick, returning one construction unit took 77 ticks by itself
and the first plan landed near tick 141.

Near **120 ticks** (~2 simulated minutes at 1×) is the soft tuning target from a fresh quiet start
with `factory_a` chosen by declaration order; tune the JSON after playing it. No test asserts the
number. On the hub legs the plan runs roughly 16 ticks to feed, 30 of switch-over (`factory_a`
opens configured for `press_components`), 16 of run, 20 to return and 2 down the dock link — near
tick 84.

## Not built

Recorded so a later reader does not assume an oversight:

- Routing, line construction, plan editing or cancel.
- Real upgrade sockets; local storage is the stand-in (Decision 3). Do not implement socket delivery
  in the same step as the interim consume phase.
- Missions, the program runtime, stored plan labels, critical-path estimates.
- The second dock stays unbuilt with nothing pointed at it.
- Condition kinds beyond `StorageItemAmount` and `ExecutorStatus`; `ParameterRef` remains declared
  and refused.
- Anything that would bump `WorldSave.CurrentVersion`.

## Open items

- **When sockets land**, commissioning becomes delivery into a socket storage; the construction-unit
  item should not need a redesign, and the local-storage consume phase is removed.
- **Energy margin under the approved plan** after the star topology — CapHits may force a content
  tweak to `energyCapacity` or a line's standing draw; decide from a running game, not from this
  document.
