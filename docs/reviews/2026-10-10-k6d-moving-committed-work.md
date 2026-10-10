# K6d — Moving Committed Work

Date: 2026-10-10
Ticket: K6d (#76), follow-up 2 of `docs/reviews/2026-10-10-e3-experiment-report.md`
Spec: `docs/superpowers/specs/2026-10-10-moving-committed-work-design.md`

## Goal

The design's §5 lists *assign eligible work* as a control. The composer's facility picker gives it
before approval, and until now nothing gave it after commit. E2 and E3 both found the gap: a
facility built in the middle of a campaign could not take work already bound to another.

## What was built

- **`MoveWork(PlanId, SchematicId, ExecutorId To)`**, an engine command
  (`SimulationEngine.Move`). It moves the plan's unstarted runs of that schematic from every other
  facility to `To`.
- **Only work whose material has not left its source moves.** For each old facility, the runs
  moved are the least of:
  - its unstarted runs;
  - each input's unloaded inbound remainder, over the input per run;
  - the output's unloaded outbound remainder, over the output per run.

  A run in progress stays. So does a run whose input is already aboard a belt or in the buffer.
- **Old tasks are cut, never redirected.** The cut is truncation, as cancel and amend do it. The
  produce task loses the moved runs, and its legs lose the moved quantity, last task first.
- **New tasks are appended to the plan**, which keeps its id, priority and held flag. They are an
  inbound leg per source and item, the runs at `To`, and an outbound leg per destination and item.
  Lines are chosen by the planner's route rule.
- **Every refusal is checked before anything changes:**
  - no active plan;
  - no such schematic;
  - `To` is the wrong kind, unbuilt, passive, of the wrong type, or the schematic is locked. These
    reuse `Enqueue`'s sentences;
  - no unstarted runs off `To`;
  - nothing can move because its material has already been sent;
  - no built line on a leg's route.
- **`EventCode.WorkMoved`**, appended last. It carries `plan`, `runs` and `tasks`.
- **No new state and no save change.** Scripts change and tasks are appended, both of which a save
  already carries.
- **The replay's `move` command** names a demand, a `schematic` and a `facility`. Its linking is
  shared with a demand's `assign`, so a facility of the wrong type is a parse error in both.

## One decision changed while building

The first draft of the spec settled claims after a move, the way an amend does. A test with that
settlement removed still held the claim invariant on every tick. So did a second test, with free ore
already in the old buffer, built to make a surplus. The reason is arithmetic:

- at the old buffer, a move lowers the plan's need and its inbound by the same quantity;
- at the new buffer, it raises both by the same quantity;
- in the hold, it changes neither.

No holding can end up beyond its need, and no outstanding claim is created. The settlement was
dead code and was removed. The spec's Decision 4 now says why, and the second test is kept as the
case that would break if the arithmetic ever stopped holding.

## Measured

`tools/Dimenship.Replay/scripts/move.json` is a demonstration on the opening vessel, not one of the
experiment's situations:

- At tick 0, a 1,000 robot-frame campaign is committed while Factory Alpha is the only factory.
- Factory Beta is ordered at High at the same tick, and is built by tick 179.
- At tick 200, the campaign's modules move to Factory Beta, which is configured for them. A move to
  Reactor Beta, still an empty slot, is refused.

The control is the same script with its commands removed.

| | Without the move | With it |
| :--- | ---: | ---: |
| Campaign readiness (ticks) | 1,721 | **1,041** |
| Factory Alpha changeovers / ticks | 6 / 720 | 3 / 360 |
| Factory Alpha working ticks | 976 | 656 |
| Factory Beta working ticks | 0 | 320 |
| Interventions | 2 | 3 |
| Final state SHA-256 | `ad3c80c5…80219dc5` | `b5d96818…48ab3300` |

All 40 module runs moved, because at tick 200 none of their components had been pressed yet. The
refused move appears in the report with the kernel's sentence and counts for nothing.

Every replay recorded after K5c is **byte-identical**: all 27 script and policy pairs. The command
changes nothing unless it is issued.

## Not done

- **No control in the Operations view.** Plan detail controls (U1) gain it in a shell ticket. The
  harness and the controllers reach it through `Execute` already.
- **No measurement on C.** C is held out, and a move there would be scheduling it by hand. The
  E3 report names C's Factory Beta as the case; under queue order Factory Beta is built at tick
  2,679, after C's campaign has finished, so a move there would also need the build raised first.
- **No split of a stage.** A move takes every movable run.
- **Material already sent stays.** Moving it would need a buffer-to-buffer leg the planner does not
  make for ordinary items.
- **Controllers do not use it yet.** E3's follow-up 4 (assignments for controllers' orders) is the
  natural place.

## Tests

- **`MoveWorkTests`** (Core, 6 tests):
  - runs and legs move, and the plan delivers its whole goal with no ore spent twice or stranded;
  - a run in progress finishes where it is;
  - work whose input is all aboard is refused, with the world unchanged byte for byte;
  - each refusal names its reason and changes nothing;
  - free ore in the old buffer leaves the invariant intact;
  - a world saved after a move resumes byte for byte.
- **`ReplayTests`** gains:
  - `move` parse errors, in the command error list;
  - the `move.json` run against its control;
  - the fixture in the shipped-parse cases.
- **Suites:** Core 435, Shell 108, Replay 54.
