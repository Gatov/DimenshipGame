# Playable Inspector and the Move Control — Design

Date: 2026-10-11
Status: Built (2026-10-11, #77); review `docs/reviews/2026-10-11-u4-playable-inspector.md`

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
    IReadOnlyList<ItemAmount> FromHold,       // every DraftMove out of the hold but the unit's own, merged by item
    IReadOnlyList<ItemAmount> Short,          // MaterialShortage issues, merged by item
    IReadOnlyList<DraftIssue> Warnings,       // every other issue that does not block approval
    IReadOnlyList<DraftIssue> Blocking,       // issues that make the draft uncommittable
    long EstimatedTicks)
{
    public bool Committable { get; }          // Blocking is empty
    public static BuildQuote For(PlanDraft draft, SchematicCatalog schematics, StorageId hold);
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
- **Warnings are the third list** (added while planning). A construction draft with no line to its
  slot reports `NoExecutorOrLine`, which is not structural, so the draft is committable. Leaving it
  out would show a clean BUILD over a plan that can never deliver. The inspector shows each as a
  NOTE row.
- **The unit's own leg is not a cost** (found while building). An ordinary unit travels factory,
  hold, slot, so one of the draft's moves out of the hold is the product itself.

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
    PostponeReason? Reason,       // the last attempt's, so a wait with no other party is still worded
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
- **The rows are the inspector's recycled `DetailRow`s, made clickable** (changed while
  planning). This spec first planned a separate pool of buttons, on the reading that those rows
  could not be clicked. The queue already lives among them, between RUN and LOCAL STORAGE, and a
  pool beside them would have split the panel's order in two. A row now takes a press when its
  line names a plan, and it reads the plan from the line it holds at the time, never from one
  captured when the row was made. The rows are refreshed per snapshot and never rebuilt, so a press
  is never lost between frames.
- **At most 8 rows,** then "+N more — open Operations". The inspector is a side panel, and a
  campaign's queue is not its job to page through.

### 5. MOVE joins the plan controls in Operations

A third row in `OperationsFocus.PlanControls` reads:

```
MOVE: [schematic ▾] TO [facility ▾] [MOVE]
```

- **Schematic list:** the selected plan's production schematics with unstarted runs, from the
  snapshot's tasks: `Runs` minus `CompletedRuns`, less one for a run in progress.
- **Facility list:** built facilities whose type runs that schematic, where some *other* facility
  holds the plan's unstarted runs of it. `Move` takes runs from every facility but the target, so
  excluding every holder would offer nothing for a stage already split across two. Whether the
  target is commandable is left to the kernel, because the snapshot does not carry it, and the
  only passive type runs no schematic a plan can contain. Both lists are refilled only when their
  contents change, so an open list survives a snapshot. That is U1's rule for the priority list.
- **MOVE** issues `ShellActions.Execute(new MoveWork(plan, schematic, facility))`.
  - The kernel's refusal shows in the existing `_commandFeedback` label.
  - An accepted move shows "MOVED {stage} TO {facility}". Reading the run count from the
    `WorkMoved` event was dropped: the event lands on the next snapshot, after the press.
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
