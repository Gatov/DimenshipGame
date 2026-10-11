# U4 — Building from the Inspector, a Facility's Queue, and the Move Control

Date: 2026-10-11
Ticket: U4 (#77)
Spec: `docs/superpowers/specs/2026-10-11-playable-inspector-and-move-control-design.md`
Plan: `docs/superpowers/plans/2026-10-11-playable-inspector-and-move-control.md`

## Goal

The project owner asked for the base graph to be playable without switching views. Building a
facility should be possible from the panel that opens when a facility is clicked. A facility
should say what it is busy with and for whom. And K6d's move command needed a control.

## What was built

- **`Presentation/BuildQuote`** reads a construction draft. It gives where the unit is made, every
  item leaving the hold for it, shortages, warnings, blocking issues and the estimate. Which kinds
  block is decided by asking `PlanDraft.IsCommittable` one issue at a time, so the rule stays in
  one place.
- **`Presentation/FacilityQueue`** lists a facility's unfinished production, current task first,
  then queue order. Each row carries its plan and goal, the effective priority and hold, the last
  reason, and K8's `WaitCause`.
- **`Presentation/MoveOptions`** lists a plan's stages with unstarted runs, and the built
  facilities of the right type each could move to.
- **The inspector, unbuilt slot.** It shows MADE AT, COST, SHORT, NOTE, BLOCKED and EST rows from
  the quote. Then comes BUILD, then CONFIRM BUILD, and the confirming press composes a fresh draft
  and approves it. A refusal shows under the button. Any other selection disarms it. PLAN
  CONSTRUCTION… is gone.
- **The inspector, built facility.** QUEUE renders `FacilityQueue` in up to three lines per task,
  at most eight tasks. A line naming a plan opens Operations on that plan.
- **Operations' plan controls.** A MOVE row has a stage list, a facility list and a button, and
  issues `MoveWork`. The kernel's refusal shows as worded.
- **`Labels.Issue`.** The draft-issue wording moved from a private `OperationsFocus` method, so the
  composer, the plan controls and the inspector share one copy.

## Where it departs from the spec

Each departure is now recorded in the spec itself.

1. **Clickable rows, not a button pool.** The spec planned a separate pool of buttons, because the
   inspector's recycled `DetailRow`s could not be clicked. The queue already sat among those rows,
   between RUN and LOCAL STORAGE, and a pool beside them would have split the panel's order in two.
   A `DetailRow` now takes a press when its line names a plan.
2. **A warnings list on the quote.** A construction draft with no line to its slot reports
   `NoExecutorOrLine`, which does not block approval. Without warnings, the inspector would offer a
   clean BUILD over a plan that can never deliver.
3. **The unit's own leg is not a cost.** An ordinary unit travels factory, hold, slot. The first
   quote counted its trip out of the hold, and charged the build for itself. The shipped-vessel
   test caught it.
4. **Move eligibility is "another facility holds runs".** It is not "no facility holding runs",
   and it does not check that the target is commandable. The snapshot does not carry
   commandability, and the kernel refuses that case anyway.
5. **An accepted move says "MOVED {stage} TO {facility}".** It does not give a run count. The
   `WorkMoved` event lands on the next snapshot, after the press.

## Exercised

A headless run of the real shell scene drove each surface. The game stayed paused throughout, so
the run also shows that a command's rebuilt snapshot reaches the next frame without a tick. The
output, verbatim:

```
SMOKE made at: FACTORY ALPHA cost: 0.400 BASIC METALS est: T+00:00:23
SMOKE build button: true
SMOKE armed: true
SMOKE disarmed on reselect: true
SMOKE ordered: true
SMOKE queue rows: 1 → 1.000 FACTORY CONSTRUCTION UNIT
SMOKE operations open: true
SMOKE move row: true disabled=true
```

The move row is disabled because on a new campaign Factory Alpha is the only factory built, so
there is nowhere to move to. The accepted path is covered by the K6d kernel tests and the
`move.json` replay, not by this run.

## Final review

A fresh reviewer read the whole branch. Four findings were fixed in one pass.

1. **Queue rows took a press only on their text.** A plain `Control` stops the mouse by default,
   and the row's spacer and bar sat between its labels. The spacer and the bar now ignore the
   mouse.
   - Reproduced by a real click through the viewport onto the spacer, mapped through the canvas
     transform. The scripted `_gui_input` calls above skip hit-testing, so they could not catch it.
   - The click failed before the fix and opens Operations after it.
2. **MOVE stayed disabled after choosing another stage while paused.** Its enabled state was
   recomputed only per snapshot, and a paused game sends none. Choosing a stage now recomputes it.
   No smoke reaches this: it needs a second factory built while paused, and the opening vessel has
   one. It is verified by reading.
3. **The quote's cost was wrong for a multi-stage unit, and for a unit already in stock.** An
   in-plan intermediate's leg out of the hold was charged on top of its raw material, and a
   stocked unit was quoted as costing nothing. Moves out of the hold are now netted against the
   plan's moves in. That also replaced the special case for the unit's own leg. Two tests failed
   before the fix and pass after it.
4. **The inspector threw on every frame after a kernel fault.** The driver then returns an empty
   draft that assembles nothing. The inspector now skips the quote for such a draft. This is
   verified by reading, because no test raises a fault through the shell.

Deferred minors:
- `Labels.Issue` words `NoExecutorOrLine` and three other kinds only by their enum name. A NOTE row
  therefore reads `NOEXECUTORORLINE · …`.
- `MoveOptions` reads "unstarted" from the snapshot's task state. A run that is powered down or
  waiting to deposit counts as unstarted. The kernel refuses such a move, so nothing breaks.
- A refused BUILD whose refusal has no planner issues shows `REFUSED: ` with nothing after it.
- Stale comments:
  - The action button's comment still says "three states" and "rows cannot be clicked".
  - The BEHIND line opens the row's own plan, not the one it waits behind.
  - "+N MORE · SEE OPERATIONS" also counts hand-queued tasks.
- A refusal can stay on screen if the same slot is later built by other means while selected.

## Not done

- **Upgrades.** The kernel has none (spec Decision 1).
- **Plan commands in the inspector,** and ordering production from it. Neither was chosen.
- **A priority on BUILD.** Construction queues at Normal, as it does from the composer.
- **A queue longer than eight tasks** ends with a count and points to Operations. It does not
  page.
- **An accepted MOVE in the shell** was not exercised, for the reason above.

## Tests

- **Core**: 449 (14 new). `BuildQuoteTests` has 6, and `FacilityQueueTests` and
  `MoveOptionsTests` have 4 each.
- **Shell**: 108.
- **Replay**: 54. Every replay is unchanged, because no kernel behaviour changed.
- **The Godot assembly** builds.
