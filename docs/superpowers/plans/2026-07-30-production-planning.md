# Production, Planning and Task Execution — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the kernel's placeholder flow model with the real production model — schematics as authoritative recipes, facilities as queue-owning executors that pick their own work each tick, transport as a first-class executor, tasks with explicit states and structured postponement reasons, and a pure planner that expands a player goal into production, transport, and acquisition requirements.

**Architecture:** All work lands in `Dimenship.Core`, still engine-free and still deterministic. `Simulation` keeps the world definition, snapshot, engine, and events. Two new folders: `Production` holds schematics and runtime tasks, `Planning` holds the planner and its plan records. The Godot shell is adapted only far enough to keep building.

**Tech Stack:** .NET 8, C# 12, NUnit 4. Godot 4.7.1-mono for the shell adaptation only.

**Spec:** `docs/superpowers/specs/2026-07-30-production-planning-design.md`

## Global Constraints

- Target framework is `net8.0` for every project. No new assemblies are created by this plan.
- `Dimenship.Core` must never reference `GodotSharp`, must never contain `using Godot`, and must never reference `Dimenship.Shell`.
- **No `float` or `double` anywhere in `Dimenship.Core`.** Determinism must not rest on floating-point reproducibility. The energy charge in Task 2 is integer-exact by construction — do not reach for a `double` to divide it.
- Iteration order comes from `WorldDefinition`'s lists, never from dictionary enumeration. Two engines built from one definition must emit byte-identical event streams.
- No wall clock in core. Time enters only through `Advance(long ticks)`.
- The Godot editor is not on `PATH` here. **No task may claim any visual behaviour is verified.** Visual confirmation is handed to the project owner in Task 5.
- Every task ends with `dotnet build DimenshipGame.sln` clean at **zero warnings** and `dotnet test` green. A task that leaves the tree red is not complete.
- One commit per task, on `claude/game-description-core-updates-hb52ux`.

## Baseline

`aed3b97`, working tree clean, **38 tests passing** — 30 in `Dimenship.Core.Tests`, 8 in
`Dimenship.Shell.Tests`. The UI shell plan's "35" was stale.

Toolchain note: this environment had no .NET SDK. Installing 8.0.129 alone is **not** enough —
`NUnit 4.6.1`'s `Assert.Throws<T>(Action)` and `Assert.Throws<T>(TestDelegate)` overloads are
ambiguous under C# 12, so every `Assert.Throws` with a lambda fails to compile. The SDK 10
compiler resolves them. Both SDKs are installed; `dotnet` picks 10 with no `global.json`, and
none is added — the repository's toolchain selection stays the project owner's to make.

---

## Execution status

Tasks 0 to 2 complete.

| Task | State | Commits | Tests after |
| :--- | :--- | :--- | :--- |
| 0 — Design and plan documents | Complete | `0b95abe` | 38 |
| 1 — Items, storage, schematics | Complete | `9ecc548` | 59 |
| 2 — Production executors and tasks | Complete | see below | 84 |
| 3 — Transport | | | |
| 4 — Planner | | | |
| 5 — Shell adaptation | | | |

---

### Task 0: Design and plan documents

**Files:**
- Create: `docs/superpowers/specs/2026-07-30-production-planning-design.md`
- Create: `docs/superpowers/plans/2026-07-30-production-planning.md`

- [x] **Step 1:** Write the design document, recording every decision the two source specs left open.
- [x] **Step 2:** Write this plan.
- [x] **Step 3:** Commit both on their own, before any code. The plan exists in the repository whether or not the implementation finishes in one session.

---

### Task 1: Items, storage, and the schematic catalog

A refactor with **no behaviour change**. The engine keeps its current flow model; only the vocabulary and the shape of stock storage change. This is what makes Task 2's rewrite a change to one thing rather than two.

**Files:**
- Modify: `src/Dimenship.Core/Simulation/Ids.cs`
- Create: `src/Dimenship.Core/Simulation/Quantities.cs`
- Modify: `src/Dimenship.Core/Simulation/WorldDefinition.cs`
- Modify: `src/Dimenship.Core/Simulation/WorldSnapshot.cs`
- Modify: `src/Dimenship.Core/Simulation/SimulationEngine.cs`
- Create: `src/Dimenship.Core/Production/SchematicDefinition.cs`
- Create: `src/Dimenship.Core/Production/SchematicCatalog.cs`
- Test: `tests/Dimenship.Core.Tests/Production/SchematicCatalogTests.cs`
- Test: `tests/Dimenship.Core.Tests/Simulation/StorageTests.cs`
- Modify: `tests/Dimenship.Core.Tests/Simulation/SimulationEngineTests.cs` (renames only)

**Interfaces produced:**
- `ItemId`, `SchematicId`, `TaskId`, `ExecutorId`, `StorageId` — readonly record structs.
- `ItemAmount(ItemId Item, long Quantity)`, `WorkAmount(long Value)`, `EnergyAmount(long Value)`.
- `ItemDefinition(ItemId Id, string Label, long HoldCapacity)`.
- `StorageDefinition(StorageId Id, string Label, long CapacityPermille, IReadOnlyList<ItemAmount> Initial)`.
- `ItemStock(ItemId Id, long Amount, long Capacity)`, `StorageState(StorageId Id, string Label, IReadOnlyList<ItemStock> Items)`.
- `SchematicDefinition`, `SchematicCatalog`.

- [x] **Step 1: Rename `ResourceId` to `ItemId`** across core, tests, and the Godot scripts. Mechanical; the shell keeps compiling because `WorldSnapshot.Resources` keeps its name.
- [x] **Step 2: Add the new identifiers and `Quantities.cs`.** Readonly record structs with `ToString()` returning the underlying value, matching the existing `ResourceId`/`FacilityId` pattern.
- [x] **Step 3: Add `SchematicDefinition`** exactly as the design document gives it — `required` init-only properties, `sealed record`.
- [x] **Step 4: Add `SchematicCatalog`** over an ordered `IReadOnlyList<SchematicDefinition>` plus an unlocked-id set. Members: `TryGet(SchematicId, out SchematicDefinition)`, `Get(SchematicId)` throwing `KeyNotFoundException` with the id in the message, `IsUnlocked(SchematicId)`, `ForOutput(ItemId)` returning candidates **in definition order**. Build the id index once in the constructor; never enumerate a dictionary to produce ordered output.
- [x] **Step 5: Introduce storages in the engine.** Replace the flat `_amounts`/`_capacities` dictionaries with per-storage stocks keyed by `(StorageId, ItemId)`. `WorldDefinition` gains `Items` and `Storages`; `CreateDefault()` declares a single `main_hold` carrying today's ore and alloy so behaviour is unchanged.
- [x] **Step 6: Add helpers for stock movement** on the engine — `Available(StorageId, ItemId)`, `Room(StorageId, ItemId)`, `Deposit`, `Withdraw`. Tasks 2 and 3 both need these and must not each grow their own copy.
- [x] **Step 7: Extend the snapshot** with `IReadOnlyList<StorageState> Storages`, built in definition order. `Resources` becomes a roll-up summed across storages in item-definition order — same type, same name, same meaning to the shell.
- [x] **Step 8: Tests.** Catalog lookup, unlocked filtering, `ForOutput` ordering with two schematics producing one item, and the missing-id exception message. Storage deposit/withdraw at and past the cap, and that the roll-up equals the sum across storages.

**Verification:** `dotnet test` green. The existing `Advance_InOneCall_MatchesManySingleTickCalls`, `DefaultWorld_FirstTick_EmitsExactEventSequence`, and `TwoEnginesFromTheSameDefinition_ProduceIdenticalEventStreams` must pass with **rename-only** edits. If a behavioural assertion needs changing in this task, something has gone wrong — stop and re-read the step.

**Outcome:** All 30 existing core tests passed with construction-only edits — no assertion
changed, so behaviour is provably unchanged. 21 new tests added (51 core, 59 total).

Deviation from the plan, approved as an improvement: storage capacity is **not** a flat
`CapacityPerItem`. How much of an item fits is a property of the item (`HoldCapacity`) scaled by
a per-storage `CapacityPermille`. A flat per-storage cap would have silently changed the default
world's alloy capacity from 500,000 to 2,000,000 and broken the "no behaviour change" promise
this task rests on. The design document was updated to match.

---

### Task 2: Production executors, tasks, batch execution, switch-over

The behaviour change. `Tick()`'s facility loop is replaced by the executor state machine.

**Files:**
- Modify: `src/Dimenship.Core/Simulation/Ids.cs` (enums)
- Modify: `src/Dimenship.Core/Simulation/WorldDefinition.cs`
- Modify: `src/Dimenship.Core/Simulation/WorldSnapshot.cs`
- Modify: `src/Dimenship.Core/Simulation/SimulationEngine.cs`
- Create: `src/Dimenship.Core/Production/ProductionTask.cs`
- Create: `src/Dimenship.Core/Production/TaskAttempt.cs`
- Test: `tests/Dimenship.Core.Tests/Production/ProductionExecutorTests.cs`
- Test: `tests/Dimenship.Core.Tests/Production/EnergyTests.cs`
- Rewrite: `tests/Dimenship.Core.Tests/Simulation/SimulationEngineTests.cs`

**Interfaces produced:**
- `FacilityType { Extractor, Refinery, Factory }` replacing `FacilityKind`.
- `ExecutorStatus { RunningTask, SwitchingOver, NoTasksQueued, AllQueuedTasksBlocked }` replacing `FacilityStatus`.
- `TaskState { NotStarted, Running, Postponed, Complete }`.
- `PostponeReason` with the six reasons from the Planning spec.
- `ProductionExecutorDefinition`, `PowerSinkDefinition`.
- `ProductionTask`, `TaskAttempt`, `ProductionTaskState`.
- `ExecutorState(ExecutorId Id, FacilityType Type, ExecutorStatus Status, SchematicId? Configured, TaskId? CurrentTask, long PowerDraw, long RunTicksRemaining, PostponeReason? BlockReason)` replacing `FacilityState`.
- `SimulationEngine.Enqueue(ProductionTask)`.

- [x] **Step 1: Replace the enums.** `FacilityKind` → `FacilityType` with `StabilizationField` removed; `FacilityStatus` → `ExecutorStatus`. Add `TaskState`, `PostponeReason`, and the new `EventCode` members from the design document — one code per postpone reason.
- [x] **Step 2: Add `PowerSinkDefinition`** and move the stabilization field onto it. It draws unconditionally and first, exactly as today.
- [x] **Step 3: Add `ProductionExecutorDefinition`** and replace `WorldDefinition.Facilities` with `Producers` and `Sinks`. List order stays the determinism contract.
- [x] **Step 4: Add `ProductionTask`** — mutable runtime class holding `Id`, `SchematicId`, `RequestedRuns`, `ExecutorId`, `State`, `CompletedRuns`, `RunTicksRemaining`, `WorkDoneThisRun`, `EnergyChargedThisRun`, `LastReason`, `PostponedAtTick`, and a **bounded** attempt history. Bound it with the same constant discipline as `SimulationEngine.EventBufferCapacity` — a task postponed every tick for an hour is normal, not pathological.
- [x] **Step 5: Write the executor step.** Per producer, in definition order:
  - switching over → decrement, standing draw only, emit `SwitchOverCompleted` at zero, target task becomes `Running` on the **following** tick;
  - run in progress → decrement `RunTicksRemaining`, take the proportional charge, and on zero deposit output, `CompletedRuns++`, complete at `RequestedRuns`, or postpone `DestinationFull` holding the finished run if the output does not fit;
  - between runs → select per Step 6, and on starting withdraw the run's inputs from local storage and set `RunTicksRemaining = ceil(EffortPerRun / WorkRatePerTick)`.
- [x] **Step 6: Write task selection**, Planning spec §6: continue current → runnable task on the configured schematic → runnable task on another schematic, entering switch-over → `AllQueuedTasksBlocked` with each queued task's reason recorded.
- [x] **Step 7: Write the energy model.** Standing draw claimed first, unconditional, never refused. Production charge proportional to work done, cumulative and integer-exact:
  ```
  targetTotal   = EnergyPerRun * WorkDoneThisRun / EffortPerRun
  tickCharge    = targetTotal - EnergyChargedThisRun
  ```
  A refused charge postpones with `InsufficientEnergy` and **holds** `RunTicksRemaining` — the run resumes rather than voiding its already-consumed inputs. Keep `CapHits` and `StarvedTicks` semantically distinct; `EnergyState`'s doc comment explains why and stays accurate.
- [x] **Step 8: Replace `FacilityState` with `ExecutorState`** in the snapshot and add `ProductionTasks`.
- [x] **Step 9: Rebuild `WorldDefinition.CreateDefault()`** from schematics: `extract_raw` on an extractor, `alloy` on a refinery, `chip` and `armor_plate` on factories, per the design document's table. Seed an extraction task and an alloy task so the shell has activity from tick one. The refinery's inputs reach it only once Task 3 adds transport — until then it blocks on `InsufficientInputMaterial`, which is correct and must be pinned by the first-tick event test rather than worked around.
- [x] **Step 10: Tests.**
  - Run duration is `ceil(effort / rate)` — cover ratios that do and do not divide evenly.
  - Inputs are withdrawn at run start, output deposited at run end, nothing in between.
  - **Batch/partial execution** (§7): a 20-run task produces 6, postpones on `InsufficientInputMaterial`, resumes when material arrives, completes the remaining 14, and records the postponement in its history exactly once per attempt.
  - **Switch-over** (§8): same schematic starts immediately; a different one spends exactly `SwitchOverTicks` in `SwitchingOver`, is not `Running` during it, and draws exactly `StandingPowerDraw` on each of those ticks.
  - **Energy**: idle draws exactly `StandingPowerDraw`; a completed run charges exactly `EnergyPerRun` on top of it for several effort/rate ratios; a run postponed part-way has charged strictly less, proportional to work done.
  - **Determinism**, extending the existing patterns: `Advance(60)` equals sixty `Advance(1)` calls across storages, executors, and tasks; two engines from one definition emit identical event streams; the default world's first-tick sequence is pinned exactly.

**Verification:** `dotnet test` green. The Godot project does **not** build after this task — `Dimenship.Core` compiles, but the shell scripts still reference `FacilityState`. That is expected and is repaired in Task 5.

**Outcome:** 76 core tests passing, build clean at zero warnings.

Two things the plan did not anticipate, both found by the tests it specified:

- The tick that *decides* to reconfigure was spending itself on the decision, so a five-tick
  switch-over occupied six ticks. The deciding tick is now the first tick of the reconfiguration.
- `StorageState` holds an `IReadOnlyList`, so record equality compares it by reference and the
  bulk-versus-single-tick assertion could never have passed. Compared as a projection now, the
  same way `SimEvent` already had to be. Any future snapshot record holding a collection has
  this property — it is structural, not incidental.

Also deviating from the plan: `CreateDefault()` keeps both facilities on the main hold rather
than gaining the armour-plate chain. That chain needs transport to move anything between
storages, and transport is Task 3 — building it here would have shipped a default world that
deadlocks the moment a local buffer fills. The worked example's schematics live in the Task 4
fixture, which is where they are actually asserted on.

---

### Task 3: Transport executors and transfer tasks

**Files:**
- Modify: `src/Dimenship.Core/Simulation/WorldDefinition.cs`
- Modify: `src/Dimenship.Core/Simulation/WorldSnapshot.cs`
- Modify: `src/Dimenship.Core/Simulation/SimulationEngine.cs`
- Create: `src/Dimenship.Core/Production/TransportTask.cs`
- Test: `tests/Dimenship.Core.Tests/Production/TransportTests.cs`

**Interfaces produced:**
- `TransportExecutorDefinition(ExecutorId Id, long ThroughputPerTick, long StandingPowerDraw)`.
- `TransportTask`, `TransportTaskState`.
- `SimulationEngine.Enqueue(TransportTask)`.

- [ ] **Step 1: Add `TransportExecutorDefinition`** and `WorldDefinition.Transports`.
- [ ] **Step 2: Add `TransportTask`** with `Item`, `RequestedQuantity`, `MovedQuantity`, `Source`, `Destination`, and the same state, reason, and bounded-history fields as `ProductionTask`.
- [ ] **Step 3: Write the transport step**, running **before** production in the tick so material delivered this tick is usable this tick. Each transport executor selects from its queue with the same continue-then-switch discipline, moves up to `ThroughputPerTick`, and completes at `RequestedQuantity`.
- [ ] **Step 4: Partial transfer and postponement.** Move what is there, postpone the rest with `InsufficientSourceMaterial`. A full destination postpones with `DestinationFull`. Both cases keep `MovedQuantity` and resume later — no work is lost and nothing is silently dropped.
- [ ] **Step 5: Finish `CreateDefault()`** — add a transport executor and the transfer tasks that feed the refinery and return its output, so the default world runs a complete chain.
- [ ] **Step 6: Tests.** The spec's §7 case exactly: move 60 with 19 at source → 19 move, task postpones with `InsufficientSourceMaterial`, and completes when more arrives. Plus: throughput caps a tick's movement; a full destination postpones without losing the moved count; transport-before-production is pinned by a test that would fail under the opposite order.

**Verification:** `dotnet test` green. Transport ordering is a behavioural contract — assert it, do not merely document it.

---

### Task 4: Planner, plan commit, and shortages

**Files:**
- Create: `src/Dimenship.Core/Planning/IWorldView.cs`
- Create: `src/Dimenship.Core/Planning/ProductionPlan.cs`
- Create: `src/Dimenship.Core/Planning/ProductionPlanner.cs`
- Modify: `src/Dimenship.Core/Simulation/SimulationEngine.cs` (`Commit`, `IWorldView`)
- Modify: `src/Dimenship.Core/Simulation/WorldSnapshot.cs` (`Shortages`)
- Test: `tests/Dimenship.Core.Tests/Planning/ProductionPlannerTests.cs`
- Test: `tests/Dimenship.Core.Tests/Planning/WorkedExampleTests.cs`

**Interfaces produced:**
- `IWorldView` — read-only access to the catalog, storages, executors, and committed tasks.
- `ProductionPlan`, `PlannedRun`, `PlannedTransfer`, `PlanShortage`, `ShortageKind`.
- `ProductionPlanner.Plan(ItemAmount goal, IWorldView world)`.
- `SimulationEngine.Commit(ProductionPlan)` returning the created `TaskId`s.

- [ ] **Step 1: Define `IWorldView`** and implement it on `SimulationEngine`. The planner takes the interface, not the engine — that is what keeps it pure and testable against a hand-built world.
- [ ] **Step 2: Define the plan records** exactly as the design document gives them.
- [ ] **Step 3: Write the recursive expansion.** Use available, determine the deficit, expand the producing schematic's inputs, recurse. Stop on: available or allocated, producible by an unlocked schematic, a raw resource to acquire, or a locked schematic. **Visited set** against cyclic recipes and a **depth cap** against a runaway chain — a content bug must produce a diagnosable failure, not a stack overflow.
- [ ] **Step 4: Net out reservations.** Availability is on hand, minus quantities claimed by committed tasks, plus output expected from tasks in flight. Without this, planning the same goal twice double-counts the same stock — cover it with a test that plans twice.
- [ ] **Step 5: Emit transfers.** For each planned run, the inputs move from the hold to the executor's local storage and the output moves back. Emit the **complete** set; the source spec's four-transfer list is illustrative and omits the chip legs.
- [ ] **Step 6: Executor selection** — compatible `FacilityType`, tie-broken by fewest queued runs then definition order. Deterministic, no dictionary enumeration.
- [ ] **Step 7: Shortages.** `RawResource` for an unproducible raw material, `LockedSchematic` for a branch the player cannot build. A locked schematic must return a shortage, **not** throw.
- [ ] **Step 8: `Commit`.** Inject tasks into queues, emit `PlanCommitted` and one `PlanShortage` per shortage, return the ids. A plan with shortages commits — the available portion begins immediately.
- [ ] **Step 9: The worked example as an acceptance test.** Fixture per the design document's schematic table; on hand Alloy 5, Chips 10, Raw 19, plus silicon and conductive material. Goal `4 Armor Plate` must yield additional production of Alloy 20 and Chips 5, exactly one shortage of 41 Raw Material, and the four transfers the Planning spec names. Assert those four are **present**; do not assert the total count, which would encode the spec's abbreviation as a requirement.
- [ ] **Step 10: Commit-then-run test.** Commit the plan on a world with enough material and advance until the goal is produced, proving plan and runtime agree.

**Verification:** `dotnet test` green, with the worked example passing on the spec's exact numbers.

---

### Task 5: Shell adaptation and handover

**Files:**
- Modify: `dimenship/scripts/ui/panels/OverviewFocus.cs`
- Modify: `dimenship/scripts/ui/panels/EnergyBudgetPanel.cs`
- Modify: `dimenship/scripts/ui/panels/EventLogPanel.cs`
- Modify: `dimenship/scripts/ui/StatusBar.cs` if its alert count reads facility status

- [ ] **Step 1: `OverviewFocus`** — its facility list reads `ExecutorState`; `Describe` covers the new postpone codes. Resource tiles are untouched: `Resources` kept its name and meaning.
- [ ] **Step 2: `EnergyBudgetPanel`** — the per-consumer breakdown iterates producers, transports, and sinks.
- [ ] **Step 3: `EventLogPanel`** — the code-to-severity switch gains every new code. A code with no case is a silent formatting gap, so cover them all.
- [ ] **Step 4: Alert count** — derived from executors whose status is `AllQueuedTasksBlocked`, plus postponed tasks. Keep it derived, never stored.
- [ ] **Step 5: Build the whole solution** clean at zero warnings, `dotnet test` green.
- [ ] **Step 6: Hand visual verification to the project owner.** The editor is not on `PATH`; list what to look at — executors switching schematics, tasks postponing and resuming, the event log carrying the new codes — and claim nothing about how it looks.

**Verification:** `dotnet build DimenshipGame.sln` clean, `dotnet test` green, and an explicit statement that visual behaviour is unverified here.

---

## Out of scope

Queue and task panels, the planning UI, and the expedition prompt. Each needs its own spec. This plan makes the systems they will display real and correct; it does not display them.
