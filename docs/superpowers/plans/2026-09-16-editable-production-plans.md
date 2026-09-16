# Editable Production Plans — Implementation Plan

Status: Built

**Goal:** The Operations composer stops being a preview and becomes an editor. The planner still
proposes a whole plan, but the proposal keeps the requirement graph it discovered, shows it as rows
in dependency order, and lets the player pin a quantity, reassign an executor, or add a direct move
— after which adjustment reworks only what is still uncommitted and still unlocked. A construction
plan ends with an explicit ASSEMBLE row rather than appearing to finish at a delivery. Approval
still commits ordinary task scripts, and nothing about the editor reaches runtime state.

**Design:** Issue [#40](https://github.com/Gatov/DimenshipGame/issues/40) (*Editable Production
Plans — Design*, 2026-09-16). Its text is the requirement; the decisions below are how it lands in
this repository. Task 0 transcribes it into
`docs/superpowers/specs/2026-09-16-editable-production-plans-design.md` so the code can cite a
filename the way every other subsystem does.

**Prior art it must not contradict:**
`docs/superpowers/specs/2026-07-30-production-planning-design.md` (the planner is pure, a plan with
shortages is committable), `docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md`
(Decisions 6–8: the Build composer, DISCARD and the double-approve, and construction phase as a
`Presentation/` projection), and `docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`
(no refit state machine; every intermediate state is "a part is in a storage").

**Architecture:** One new kernel folder, `src/Dimenship.Core/Planning/Draft/`, holding an immutable
`PlanDraft` — the requirement graph `ProductionPlanner` already builds and currently throws away at
the door — plus the pure adjustment that reworks one against a world view. `ProductionPlanner.Plan`
becomes `PlanDraftEditor.Create(...).Flatten()`, so there is one expansion algorithm rather than two
that drift, and every existing planner test keeps passing unchanged as the proof. `ProductionPlan`
is **unmodified**: approval flattens, and draft metadata is structurally incapable of reaching
`SimulationEngine.Commit`. The rest is Operations: a row list with per-field locks, four new
controls, and a revision stack for Undo/Redo held in the Godot layer.

## Why

The planner already knows everything the editor needs. `ProductionPlanner.Expansion.Require`
recurses an item's requirement into its schematic's inputs, reserves facility load before it
recurses, credits batch surplus back to a running budget, and merges legs of the same route so four
runs are one haul of sixty. Every relationship the issue's tree diagram draws is computed in that
method — and then `Build` pours it into two flat lists (`_transferTasks`, `_runTasks`) and returns a
`ProductionPlan` in which nothing remembers what wanted what.

That discard is the whole problem. A preview built from the flat list can say *150 basic metals move
hold → Factory Alpha*; it cannot say which requirement asked for them, so it cannot say what should
change when the player halves that row, and it cannot tell a row the player pinned from a row the
next adjustment happens to emit in the same position. The obvious implementation — let the player
edit `ProductionPlan.Tasks` in place before APPROVE — fails on the first re-plan, because the second
plan's list is a different list and a row index is not an identity. The issue says this in one
sentence (*identity follows the requirement relationship, never the row index*); it is the single
decision the whole feature rests on.

The second tempting mistake is to put the draft in `WorldState`, because it has player edits in it
and player edits feel like state. A draft is none of the four tiers: not catalog, not scenario, not
world state, not authored content. It is a proposal, and `ProductionPlan`'s own doc comment already
says nothing exists in the world until `Commit` injects it. Putting it in state would mean a save
DTO, a `WorldSave.CurrentVersion` bump, and a determinism test failing over an editor that cannot
affect a tick. The draft lives where `ConstructionProgress` lives — `Dimenship.Core`, pure,
never saved, never on the snapshot — with one difference worth stating: `ConstructionProgress` is
projected and thrown away within a frame, while a draft is held by the shell across frames because
the player is editing it. Held by the shell, not by the kernel.

The third is to make assembly a `TaskAction`. The engine has no assembly task and does not need
one: commissioning is already a phase of the tick, placed between transport and production, and its
placement *is* the determinism contract — a unit delivered this tick commissions this tick.
`ConstructionProgress.For` already reads that phase off a snapshot. So the ASSEMBLE row is a draft
step that flattens to no task at all, and tracking it after approval is a projection that already
exists. Adding `Assemble : TaskAction` would put a fourth case in `Enqueue`'s switch that must throw
— a runtime failure where the type system can give an impossibility.

And there is a live bug sitting in the road. `IWorldView.Facilities` filters on `Built` and not on
`Commandable`, and `EnqueueProduce` checks neither the flag nor anything downstream of it. The only
extractor on the shipped vessel is the Emergency Hydrogen Extractor, `commandable: false`, and
`extract_hydrogen` is unlocked at campaign start, so Hydrogen is already an option in the Produce
composer: planning it today picks the passive source and APPROVE queues a run on it. The content
loader refuses exactly this for a scenario task, with a comment explaining that queueing work on a
passive source says the player ordered it. The GDD states the rule three times. An editor that hands
the player an executor picker makes a latent bug into an offered choice, so it is fixed first, in
its own task, before anything else is built on top of the picker.

## Decisions

**D1. The draft is the requirement graph; the plan is its flattening.** `PlanDraft` holds
`IReadOnlyList<DraftStep>` in the order expansion produced them, each carrying a parent. `Flatten`
walks the same post-order the current `Expansion` walks and emits transfers, then runs, then the
final delivery leg — the order `PlanTasks_KeepTheOrderCommitWouldEnqueueThem` pins today.

**D2. Identity is the requirement path, not the row.** Each step carries a `RequirementKey`:
its parent's key plus a role (`Goal`, `Assembly`, `Output`, `Input(ordinal)`, `Delivery`, `Manual(ordinal)`)
and the item it concerns. A `DraftStepId` is minted once per key and reused for as long as that key
survives an adjustment, so an id held by a pending edit, a lock, an issue or a UI selection keeps
pointing at the same requirement after neighbours are inserted or removed. Locks are carried by key.
The failure this prevents is the one the issue names: a lock sliding onto the wrong row.

**D3. Legs merge at flatten, and the line is chosen per route.** The draft keeps one move per
requirement — that is what makes a per-row quantity edit mean something — and `Flatten` merges moves
sharing `(item, from, to, executor)` in first-appearance order, summing `Quantity` and
`AvailableAtSource` exactly as `Expansion.Move` does now. For the merge to reproduce today's output
the automatic line choice must be per route per draft, not per step: two requirement rows on one
route reuse the first row's choice unless the player locked a different one. Choosing per step would
split one haul across two lines, each paying the belt length once, and the estimate would climb for
a draft nobody edited.

**D4. Adjustment is a pure function of (draft, world, edit), returning a whole new draft.**
`PlanDraftEditor.Adjust(PlanDraft, IWorldView, DraftEdit)`. Immutable revisions rather than a
command/inverse pair: a draft is tens of steps, so copying is cheap, and Undo that restores a
revision cannot disagree with the forward path the way a hand-written inverse can. One edit and all
of its automatic consequences are one revision, which is exactly the issue's Undo transaction.

**D5. Backward adjustment, per node, in the issue's terms.**
`remaining = required − available − committed incoming − locked contributions − retained automatic
contributions`. `IWorldView.Uncommitted` already nets committed tasks, so "committed incoming" costs
nothing new. Positive remaining creates or resizes work; zero leaves the branch alone; unlocked
steps contributing nothing are removed; locked excess stays as visible surplus and is credited to the
running budget so a later compatible requirement can spend it. Batch recipes round up to whole runs
and report the surplus, as `Require` already does.

**D6. Stability is a rule, not an accident.** A surviving unlocked executor choice is *retained*
while it is still valid, rather than re-picked whenever queue depth moves; only new or invalidated
steps pick fresh. `RE-ADJUST` is the explicit opposite — it drops retained choices and re-optimises
every unlocked value, keeping locks. `UNLOCK ALL` clears locks only: a manual row is a fact the
player added, not a lock, and it is removed by its own ✕.

**D7. The kernel computes what determines the plan; the shell keeps what only describes it.** The
draft carries steps, issues, coverage and `EstimatedTicks`. Energy demand and executor competition
stay where they are today — rendered in `OperationsFocus` from the flattened preview, the catalog
and the snapshot — because they are readings about a plan rather than inputs to it, and moving them
into the kernel would put presentation arithmetic in a determinism-guarded assembly.

**D8. Structural errors block approval; shortfalls do not.** `DraftIssue` kinds split in two.
Structural — `IncompatibleExecutor`, `NoSuchRoute`, `UnknownEndpoint`, `NonPositiveQuantity`,
`UnbuiltExecutor`, `NotCommandable` — make `IsCommittable` false. Supply — `MaterialShortage`,
`GoalShortfall`, and the existing `UnplannableReason` set — leave it true, and the button reads
`APPROVE PARTIAL PLAN` naming the uncovered requirement. This is the 2026-07-30 spec's rule
(*a plan with shortages is committable*) extended to player-created shortfalls, and no further.

**D9. Approval re-adjusts against the live world, then flattens.** `PlanDraftEditor.Approve` runs
one final adjustment, refuses a structurally invalid draft, and returns a `ProductionPlan` with no
draft metadata on it. Refusal needs a return path, so `ShellActions.PlanApproved` becomes
`Func<PlanDraft, PlanApproval>` rather than an `Action` — a deliberate exception to the command
shape, because parking the refusal on `ShellContext` for the next frame would show it after the
composer had already moved on. `ShellContext.ComposePlan` is already a `Func`, for the same reason.

**D10. A world refresh replaces the current revision; it never pushes one.** Re-adjusting on each
snapshot delivery is what keeps availability and validation honest, and pushing each of those onto
the Undo stack would make Undo walk backwards through ticks instead of through the player's edits.

## Model sketch

```csharp
// src/Dimenship.Core/Planning/Draft/
public readonly record struct DraftStepId(long Value);

public enum DraftRole { Goal, Assembly, Output, Input, Delivery, Manual }
public enum DraftOrigin { Automatic, Manual }

public sealed record RequirementKey(DraftStepId? Parent, DraftRole Role, int Ordinal, ItemId Item);

/// Draft work is its own hierarchy, not TaskAction: DraftAssemble must be incapable of
/// reaching Enqueue, and a case that throws is a runtime failure where a separate type is an
/// impossibility.
public abstract record DraftWork;
public sealed record DraftProduce(SchematicId Schematic, long Runs) : DraftWork;
public sealed record DraftMove(ItemId Item, long Quantity, StorageId From, StorageId To) : DraftWork;
public sealed record DraftAssemble(ItemId Unit, ExecutorId Target) : DraftWork;

public sealed record DraftStep(
    DraftStepId Id,
    RequirementKey Key,
    DraftWork Work,
    ExecutorId? Executor,
    long AvailableAtSource,
    bool QuantityLocked,
    bool ExecutorLocked,
    DraftOrigin Origin,
    bool Replanned);           // set by the adjustment that changed it; the UI's REPLANNED flash

public sealed record PlanDraft(
    ItemAmount Goal,
    StorageId? Destination,
    ExecutorId? AssemblyTarget,
    IReadOnlyList<DraftStep> Steps,
    IReadOnlyList<DraftIssue> Issues,
    long Covered,              // milli-units of Goal.Quantity the draft actually covers
    long EstimatedTicks)
{
    public bool IsCommittable { get; }   // no structural issue
    public bool IsComplete => Covered >= Goal.Quantity;
}

public abstract record DraftEdit;
public sealed record SetQuantity(DraftStepId Step, long Quantity) : DraftEdit;
public sealed record SetExecutor(DraftStepId Step, ExecutorId Executor) : DraftEdit;
public sealed record SetLock(DraftStepId Step, DraftField Field, bool Locked) : DraftEdit;
public sealed record AddMove(ItemId Item, StorageId From, StorageId To, long Quantity, ExecutorId Line) : DraftEdit;
public sealed record RemoveStep(DraftStepId Step) : DraftEdit;
public sealed record ReAdjust : DraftEdit;
public sealed record UnlockAll : DraftEdit;
```

## Tasks

- [x] **0. The design lands in `docs/` first.** Transcribe issue #40 into
      `docs/superpowers/specs/2026-09-16-editable-production-plans-design.md` in the house spec shape
      — Goal, Source material, Vocabulary, numbered Decisions with their reasoning, *Not built*, Open
      items — carrying D1–D10 above as the decisions the issue leaves to the implementer. No code
      cites an issue number where a spec filename belongs.

- [x] **1. A passive source is unschedulable, in code and not only in content.** `PlannerFacility`
      gains `Commandable`, `SimulationEngine`'s `IWorldView.Facilities` skips a non-commandable
      archetype the way it already skips an unbuilt one, and `EnqueueProduce` refuses one by name
      with the loader's own sentence. Independent of everything below and separately committable.
      Accepted consequence: planning Hydrogen now returns one `NoExecutorOrLine` entry and no tasks
      instead of quietly queueing a run on the emergency extractor. The composer keeps offering the
      item and the preview names the reason.

- [x] **2. The graph survives expansion.** `Planning/Draft/` — `PlanDraft`, `DraftStep`, `DraftWork`,
      `RequirementKey`, `DraftIssue`, and `PlanDraftEditor.Create` / `.Flatten`. `Expansion` keeps
      its arithmetic exactly (budget, reserved facility load, batch surplus, the route-merged legs)
      and records a step per requirement instead of appending to two lists; `Flatten` re-merges per
      D3 and emits transfers, runs, then the final leg. `ProductionPlanner.Plan` becomes
      `Create(...).Flatten()` and keeps its signature and doc comment. **The acceptance test for
      this task is that every existing planner and worked-example test passes untouched**, including
      `PlanTasks_KeepTheOrderCommitWouldEnqueueThem` and `Estimate_IsTheBusiestExecutorsTotal`. No
      float, no new state, nothing on the snapshot.

- [x] **3. Adjustment, locks and edits.** `PlanDraftEditor.Adjust(draft, world, edit)` implementing
      D4–D6: freeze committed work, manual facts and locked fields; walk requirements backwards
      retaining valid automatic contributions; recompute residual demand and batch surplus; resize
      or add production and recursively expand its inputs; rebuild unlocked logistics and reselect
      only missing or invalid unlocked executors; drop unused unlocked work; recompute issues,
      coverage and duration. A quantity edit is that step's own contribution, never a plan-wide cap,
      and a reduced contribution must not cause an identical unlocked step to reappear on the same
      executor. A facility change rebuilds the unlocked legs around the new local storage. A manual
      move joins the requirement graph and reduces the hold-mediated legs it supplies by what it
      carries; it is rejected outright when no single line runs its route.

- [x] **4. Assembly, and the approval path.** `DraftAssemble` as the root of a Build draft, consuming
      the construction unit at the target and depending on the delivery that brings it — flattening
      to no task, because commissioning is already a tick phase and `ConstructionProgress.For`
      already reads it. `PlanDraftEditor.Approve(draft, world)` → `PlanApproval` (committed plan, or
      refused with the issues that refused it). `SimulationDriver` gains `Draft`, `Adjust` and
      `Approve`: the first two are pure reads that return an empty draft after a fault, exactly as
      `Plan` returns an empty plan; `Approve` is wrapped in the same try/catch `Commit` has now.
      `ShellActions.PlanApproved` becomes `Func<PlanDraft, PlanApproval>` per D9;
      `ShellContext.ComposePlan` becomes `ComposeDraft`.

- [x] **5. Operations becomes an editor.** The preview's fixed readings give way to a scrollable row
      list in dependency order: action (`ASSEMBLE` / `PRODUCE` / `MOVE`), quantity, item or
      schematic, route, executor, availability, origin and validation state, with separate lock
      controls per field. An edited field takes `ShellPalette`'s accent and a lock glyph; a manual
      row carries a manual glyph; a row the last adjustment changed shows `REPLANNED` briefly — a
      word and a palette colour, never a second colour ramp, following the unbuilt-card precedent.
      `ADD MOVE` opens an inline row (item, source, destination, quantity, line) rather than a modal,
      so `ShellActions.Suspended` is not involved. `RE-ADJUST`, `UNLOCK ALL`, `UNDO`, `REDO` beside
      it; the revision stack and its index live in `OperationsFocus`. The summary keeps materials,
      energy, competition, unplannable and the estimate, and gains goal coverage and an issue list
      whose entries select their row. `APPROVE` reads `APPROVE PARTIAL PLAN` and names the uncovered
      requirement whenever coverage is short; `_approveLocked` and `DISCARD` keep their current
      behaviour. Three new `assets/icons/control/` glyphs (lock, unlock, manual) with their `.import`
      and `.uid` sidecars committed.

- [x] **6. The record catches up.** `CLAUDE.md` gains the draft's tier placement (a proposal, not one
      of the four tiers, never saved), the identity-by-requirement rule, the flatten-merge rule, the
      assembly-step-emits-no-task rule, and the passive-source fix. The spec from Task 0 gets its
      *Not built* list reconciled with what actually shipped; this plan's `Status:` becomes Built.

## Tests

New — `tests/Dimenship.Core.Tests/Planning/PlanDraftTests.cs`:

- `AnUnadjustedDraft_FlattensToThePlanTheFlatPlannerEmitted`
- `ADraftStepId_SurvivesTheInsertionOfANeighbour`
- `ALockedQuantity_SurvivesAdjustment_ReAdjustment_AndAWorldChange`
- `ALockedExecutor_SurvivesAQueueDepthChange`
- `AnUnlockedChoice_IsRetainedWhileValid_AndReoptimisedOnlyByReAdjust`
- `AnInvalidLock_GainsAPreciseIssue_RatherThanBeingReplaced`
- `ReducingAProductionQuantity_RecalculatesOnlyItsOwnUnlockedDependencies`
- `ReducingAContribution_DoesNotRecreateAnIdenticalUnlockedStepOnTheSameExecutor`
- `ChangingAFacility_RebuildsTheUnlockedLegsAroundItsLocalStorage`
- `AnUnroutableFacilityChoice_IsAnIssue_AndRefusesApproval`
- `ABatchRecipe_RoundsUpToWholeRuns_AndShowsItsSurplus`
- `ALockedSurplus_SatisfiesACompatibleRequirement_InsteadOfBeingRemoved`
- `AManualMove_ReducesTheHoldMediatedLegsByWhatItSupplies`
- `RemovingAManualMove_RestoresTheAutomaticallySelectedRoute`
- `AManualMoveWithNoDirectLine_IsRejected`
- `UnlockAll_ClearsLocks_AndLeavesManualRowsStanding`
- `AShortfallThePlayerCreated_IsCommittable_AndNamesWhatIsUncovered`
- `TwoLegsOnOneRoute_CommitAsOneTask_WithTheirAvailabilitySummed`
- `TheAssemblyStep_EmitsNoTask_AndNoDraftMetadataReachesTheCommittedPlan`
- `Adjusting_ChangesNothingAboutTheWorld`
- `TwoIdenticalDraftsAgainstOneWorld_AdjustIdentically`

New — `tests/Dimenship.Core.Tests/Simulation/`:

- `APassiveSource_IsNeverChosenByThePlanner`
- `APassiveSource_RefusesARunEnqueuedOnItByName`

Unchanged and load-bearing: everything in `tests/Dimenship.Core.Tests/Planning/ProductionPlannerTests.cs`
and `WorkedExampleTests.cs`. They are the regression proof for Task 2 and must not be edited to
accommodate it — an edit there means the flattening changed behaviour.

`tests/Dimenship.Shell.Tests` — unchanged. Nothing new lands in `Dimenship.Shell`: the draft is in
`Dimenship.Core`, which `Dimenship.Shell` cannot reference, and the revision stack is Godot-side.
`CoreAssemblyTests`' two reflection guards cover the new folder for free.

## Verification

```bash
dotnet build DimenshipGame.sln
dotnet test DimenshipGame.sln
```

If the Godot 4.7.1 SDK is not on the feed, build and test the four non-Godot projects directly per
`CLAUDE.md`, and do not claim the solution builds when only the kernel did.

End-to-end, in the editor (`dimenship/project.godot`):

1. New game, `Ctrl+1`, select Launch Pad 2, press `PLAN CONSTRUCTION…` — Operations opens on a row
   list that ends in `ASSEMBLE MISSION DOCK CONSTRUCTION UNIT AT LAUNCH PAD 2`, not at a delivery.
2. Halve the basic-metals production row — only its own input legs change, the sibling branch does
   not move, and the row shows the accent and the lock glyph while its neighbours show `REPLANNED`.
3. `UNDO` — one press restores the whole pre-edit draft, not one consequence of it. `REDO` returns.
4. Reassign the pressing run to Factory Gamma — the legs rebuild around Factory Gamma's local
   storage; assign one with no route and the row carries an issue and `APPROVE` disables.
5. `ADD MOVE`: Reactor Alpha → Factory Alpha, basic metals — the hold-mediated legs shrink by what
   it carries. Remove it; they come back. Try a route with no line; it is refused at entry.
6. Let time run while editing — availability and validation refresh, locks do not move, and `UNDO`
   still steps back one player edit rather than one tick.
7. `RE-ADJUST` — unlocked executors re-optimise, locks hold. `UNLOCK ALL` — locks clear, the manual
   row stays.
8. Cut a quantity below what the goal needs — the button reads `APPROVE PARTIAL PLAN` and names the
   uncovered requirement. Approve it; the committed plan's tasks are ordinary, and the detail view
   tracks the assembly row off `ConstructionProgress` to `COMMISSIONING` and then built.
9. Produce mode, Hydrogen — one unplannable entry naming no executor, and `APPROVE` disabled. Before
   Task 1 this queued a run on the Emergency Hydrogen Extractor.
10. Save and reload mid-plan — committed plans resume; the draft is gone, which is the intended
    behaviour and not a bug to fix here.

## Behaviour changes accepted

- **Hydrogen becomes unplannable on the shipped vessel**, because its only producer is a passive
  source. That is the GDD rule finally holding in code rather than only in the content loader.
- **The composer shows the requirement graph, not the task list.** Two rows on one route commit as
  one task. There is no merge indicator; the row list answers "what does this plan require", and the
  plan detail after approval answers "what is queued".
- **`ShellActions.PlanApproved` answers.** One command in the table returns a value; the reason is
  D9 and it is written beside it.

## Not built

- **Choosing an alternative schematic.** Production is editable by quantity and facility only, per
  the issue. `ProductionPlanner` still takes `candidates[0]` in declaration order.
- **Multi-hop manual routing.** A manual move requires one direct transport line.
- **Saved or named drafts.** Nothing reaches `WorldSave`, no DTO, no `CurrentVersion` bump; a reload
  has no draft, and the Undo stack dies with the view.
- **A softer executor preference.** Hard locks only.
- **Timed assembly.** No construction executor, no assembly energy, no assembly duration — the
  ASSEMBLE row is an outcome milestone read from `Built`, and a later runtime action can give it a
  cost without changing the requirement model.
- **Editing, cancelling or abandoning a committed plan.** `PlanState.Abandoned` still has nothing
  that sets it. DISCARD throws away a draft, never a commitment.
- **Conditions in a draft.** Every emitted `TaskScript` still carries empty conditions.
- **Critical-path estimates.** `EstimatedTicks` remains the busiest executor's total, labelled a
  minimum wherever it is shown.

## Open items

- **Coverage as a ratio.** `Covered` is carried in milli-units beside `Goal.Quantity`; if the UI
  wants a percentage it is permille at the point of use, never a float.
- **Issue ordering.** Root-cause priority for `DraftIssue` is declaration order, the way
  `PostponeReasons.RootCause` already is; whether a structural issue always outranks a supply one in
  the summary list is a presentation question this plan leaves to Task 5.
- **Where a manual move sits in the emitted order.** Decided here as its graph position rather than
  the order it was added, so two structurally identical drafts enqueue identically. Worth revisiting
  only if a player-visible ordering expectation contradicts it.
