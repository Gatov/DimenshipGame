# Playable Inspector and the Move Control — Design

Date: 2026-10-11
Status: Draft, awaiting the project owner's review (U4)

## Goal

The kernel gained a lot of control between K2 and K6d: priority, hold, claims, wait causes,
treatment lines, and moving committed work. The shell shows little of it on the base graph, where
the player spends their time.

- **Building a facility** means reading the inspector, pressing *PLAN CONSTRUCTION…*, landing in
  Operations, composing and approving there, then finding the way back.
- **A busy facility** says that it is busy, but not what it is busy with, or for which order.
- **The move command (K6d)** has no control at all.

This design makes three changes:

1. An unbuilt slot can be built from the inspector, after a preview and a confirming press.
2. A built facility's inspector shows its work queue, and each row opens its plan in Operations.
3. A committed plan's detail in Operations gains a MOVE control.

## Source material

- The project owner's request (2026-10-11): "building a facility is non-trivial, it should be
  possible to go without switching (click facility, a panel with status should appear and it has
  options to build or upgrade)".
- The project owner's answers:
  - In the panel: build, see current status and queue, and navigate quickly to a plan. Plan
    commands stay in Operations.
  - BUILD previews, then confirms on a second press.
  - The panel is the existing docked inspector, opened on selection.
- `docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md`, Decisions 3 to 5.
  This design reverses Decision 3.
- `docs/superpowers/specs/2026-10-10-moving-committed-work-design.md` lists "a control in the
  Operations view" under *Not built*.
- CLAUDE.md, *The Godot shell*:
  - commands go through `ShellActions`;
  - U1's controls are built once and refreshed;
  - nothing in the shell decides what the kernel refuses;
  - no hard-coded colour.

## Decisions

### 1. Upgrades are out of scope

The request names upgrades. The kernel has none: `WorkRatePermille` and its siblings exist, but
nothing changes them. The scheduling plan defers upgrades until sockets and refit exist, as the
recycling spec designs them. The inspector shows no upgrade button, not even a disabled one,
because a control that can never work is noise. Upgrades get their own design when the kernel
work is scheduled.

### 2. Feasibility moves into the inspector, as a projection (reverses construction Decision 3)

Construction Decision 3 kept feasibility in Operations, so the inspector would never compose a
draft. The project owner now wants building done without switching, and a preview needs a draft.
So the inspector composes one, through `ShellContext.ComposeDraft`, the composer's own door. It
renders the draft through a new kernel projection:

```csharp
// Dimenship.Core/Presentation/BuildQuote.cs
public sealed record BuildQuote(
    ExecutorId Slot,
    ItemId Unit,
    ExecutorId? MadeAt,                       // the DraftProduce step's executor, if one
    IReadOnlyList<ItemAmount> FromHold,       // every DraftMove out of the hold, merged by item
    IReadOnlyList<ItemAmount> Short,          // MaterialShortage issues, merged by item
    IReadOnlyList<DraftIssue> Blocking,       // issues that make the draft uncommittable
    long EstimatedTicks)
{
    public static BuildQuote For(PlanDraft draft);
}
```

- **A projection, not shell code.** It reads only the draft, so it is unit-tested without Godot,
  like `ConstructionProgress`. The inspector cannot word a shortage one way while Operations
  words it another.
- **Cost is what leaves the hold.** It is not the unit schematic's inputs. A unit whose inputs must
  first be made costs their raw materials, and those are what the player is short of.
- **Shortages never block.** `MaterialShortage` is a supply kind, so the draft still commits, and
  the work waits on its input. The quote lists shortages separately from blocking issues so the
  inspector can say *will wait for…* rather than refuse.

### 3. BUILD is a two-press control, and the commit composes afresh

The inspector's unbuilt-slot view keeps PURPOSE and REQUIRES. Below them it shows:

- **MADE AT:** the facility the draft chose.
- **COST:** one row per item leaving the hold.
- **SHORT:** only when non-empty, worded "will wait for {quantity} {item}".
- **EST:** the estimated ticks, as sim time.

The draft behind the rows is recomposed on each snapshot while the slot is selected and not yet
ordered. That is one planner pass for one goal per frame.

The action button then has these states:

| State | Text | Press does |
| :--- | :--- | :--- |
| Not ordered, draft committable | BUILD | arms |
| Armed | CONFIRM BUILD | composes a fresh draft and approves it through `ShellActions.PlanApproved` |
| Not ordered, draft blocked | BUILD (disabled) | nothing; the blocking issues are shown |
| Ordered | VIEW PLAN | opens Operations on the plan (unchanged) |

- **The confirm composes afresh**, so a plan committed after the preview, or a facility built
  since, is accounted for. The confirming press is also what removes the
  approve-twice risk construction Decision 7 dealt with in the composer.
- **A refusal on confirm** shows the planner's issues in a feedback line under the button, and the
  button disarms.
- **The button disarms** when the selection changes, when the slot becomes ordered, and when the
  draft stops being committable. This matches the cancel control's rule (U1).
- **The commit carries no priority choice.** Construction queues at Normal, as it does from the
  composer. The plan's priority is one click away through VIEW PLAN.
- **PLAN CONSTRUCTION… goes**, because the inspector now does what it led to. Build mode in
  Operations stays, for a player who wants to edit the draft.

### 4. A built facility shows its queue, as a projection

```csharp
// Dimenship.Core/Presentation/FacilityQueue.cs
public sealed record FacilityQueueRow(
    TaskId Task,
    PlanId? Plan,                 // null for a task queued by hand
    SchematicId Schematic,
    int CompletedRuns,
    int? Runs,                    // null for a standing order
    bool Current,                 // the run in progress, or the switch-over's target
    ItemAmount? PlanGoal,
    Priority Priority,            // the plan's, read as selection reads it
    bool Held,
    WaitCause? Waiting);          // the existing K8 projection, null when not waiting

public static class FacilityQueue
{
    public static IReadOnlyList<FacilityQueueRow> For(WorldSnapshot snapshot, ExecutorId facility);
}
```

- **Order:** the current task first, then queue order, which is what selection reads. Finished
  tasks are omitted.
- **Plan membership** is found through `CommittedPlanState.SpawnedTasks`. The snapshot already
  carries it, so nothing is added to it.
- **Wording** uses `WaitCause` and `Conditions.Describe`, as the Operations detail does.

The inspector adds a QUEUE heading after STATUS, SETUP and POWER. Each row reads:

```
▶ ASSEMBLE MODULES   12/40   → 1,000 ROBOT FRAME   HIGH
  waiting: components held by plan 4
```

- The first line carries the current marker, the schematic, runs done over total, the plan's
  goal, and the priority when it is not Normal. HELD replaces the priority when the plan is held.
- The second line is the wait sentence, shown only when the task is waiting.
- **A row with a plan is a button.** Pressing it opens Operations on that plan, through the
  existing `PendingOperationsTarget(Plan)`. A hand-queued row reads BY HAND and does not navigate.
- **An empty queue** reads "QUEUE — nothing queued".
- **The rows are a pool of persistent buttons,** grown on demand and hidden when unused. They are
  refreshed per snapshot and never rebuilt, because the inspector's recycled `DetailRow`s cannot
  be clicked. Construction Decision 4 gave the one action button the same treatment for the same
  reason. A pool rebuilt each tick would drop a press that landed between frames.
- **At most 8 rows,** then "+N more — open Operations". The inspector is a side panel, and a
  campaign's queue is not its job to page through.

### 5. MOVE joins the plan controls in Operations

A third row in `OperationsFocus.PlanControls` reads:

```
MOVE: [schematic ▾] TO [facility ▾] [MOVE]
```

- **Schematic list:** the selected plan's production schematics with unstarted runs, from the
  snapshot's tasks: `Runs` minus `CompletedRuns`, less one for a run in progress.
- **Facility list:** built, commandable facilities whose type runs that schematic, excluding every
  facility already holding the plan's runs of it. Both lists are rebuilt only when their contents
  change, so an open list survives a snapshot. That is U1's rule for the priority list.
- **MOVE** issues `ShellActions.Execute(new MoveWork(plan, schematic, facility))`.
  - The kernel's refusal shows in the existing `_commandFeedback` label.
  - An accepted move shows "moved N runs to {facility}", read from the `WorkMoved` event's `runs`.
- **The row is disabled** when either list is empty, and for a finished plan, like the other rows.
  Whether material has already been sent is the kernel's to refuse. The shell does not check it,
  per U1's rule that a second copy of the kernel's rules in the shell drifts.

### 6. Nothing new reaches the engine, and nothing is saved

The inspector's build goes through `ComposeDraft` and `PlanApproved`. The move goes through
`Execute`. Both projections are pure reads.

- The snapshot, the save format and the layout file are unchanged.
- The armed state of BUILD is session-local and discarded on selection change.

## Not built

- **Upgrades.** See Decision 1.
- **Plan commands in the inspector,** such as priority, hold and cancel. These stay in Operations,
  as the project owner chose.
- **Ordering production from a facility's inspector.** This was offered and not chosen.
- **A priority choice on BUILD.**
- **Editing a construction draft in the inspector.** Build mode in Operations remains for that.
- **A floating popover by the card.** This was offered and not chosen.

## Acceptance criteria

- **Building from the inspector.** Selecting an unbuilt slot on a new campaign shows MADE AT,
  COST and EST. Two presses commit a construction plan, and the slot's card moves to QUEUED with
  no view switch. A third press is impossible, because the button now reads VIEW PLAN.
- **Shortage wording.** A slot whose unit needs material the hold lacks still builds. Its SHORT
  row says what it will wait for.
- **The queue.** A built facility with queued work lists it current-first. Pressing a planned row
  opens Operations with that plan selected.
- **The move control.** MOVE on a plan whose stage can move appends tasks at the chosen facility.
  A refused move shows the kernel's sentence and changes nothing.
- **Tests.**
  - `BuildQuoteTests` and `FacilityQueueTests` (Core) pin both projections on the shipped vessel
    and on `WorldBuilder` worlds.
  - A headless Godot smoke script drives the inspector's build, a queue row's navigation and one
    move, as the U1 to U3 smokes did.
  - Every existing suite and replay is unchanged.
