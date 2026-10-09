# K5b (narrow) — Facility Choice Weighs the Lines a Stage Must Cross

Date: 2026-10-09
Ticket: K5b (#64), narrowed by the project owner; K5a (#63) alongside

## Goal

Situation B's frames waited because the planner sent their pressing to Factory Gamma, the
least-loaded factory by run count, whose only line home carries 4 a tick. Two thousand components
then trickled back to the hold for about 500 ticks (K2, K6b and C0 reviews).

The project owner narrowed K5b on 2026-10-09 to location-aware **facility choice** only. The
hold-only supply rule stays: no stock outside the main hold is counted. Buffer-to-buffer routing
waits for K3 and K4, which need D2's workpieces first.

## What was built

- **K5a, stock by location.** `Presentation/StockLocations.For(snapshot, item)` lists every
  storage, belt and run in progress holding an item. `ExecutorState.RunOutput` is the one fact the
  snapshot did not already carry. It is a projection for U2's inspector and deliberately not on
  `IWorldView`, so the planner is offered no stock it must not count.
- **K5b, estimated finish.** `PlanDraftEditor`'s facility choice ranks compatible facilities by:
  1. Unoccupied first (no standing order), as before.
  2. Then estimated finish, lowest first:
     - the work queued ahead, in ticks;
     - plus the longer of the stage's own work and the slowest hold line it must cross;
     - plus each belt's length once.
  3. Then declaration order, as before.

  A belt carries while the facility works, so the slower of the two paces the stage rather than
  their sum.
- `PlannerFacility.QueuedRuns` became `QueuedTicks`: each queued task's remaining runs at its own
  run length. A run count weighed a pressing run and a frames run as equal, and could not be added
  to a line's time.
- A facility the hold cannot reach both ways ranks last.

## Measured

Situation B, demand readiness in ticks from commit:

| Script | Demand | Before | After |
| :--- | :--- | ---: | ---: |
| situation-b | expedition_frames | 1,069 | **717** |
| situation-b | build_dock_a | 892 | 1,452 |
| situation-b-priority (frames High) | expedition_frames | 1,069 | **542** |
| situation-b-priority | build_reactor_b / build_dock_a | 757 / 892 | 1,278 / 1,413 |
| situation-b-contested (frames High) | expedition_frames | 1,213 | **715** |
| situation-b-contested | build_reactor_b / build_dock_a | 1,077 / 1,212 | 1,237 / 1,372 |
| situation-b-hold | expedition_frames | 1,213 | **715** |

- **The frames now beat the quiet vessel.** Alone they take 802 ticks, and that script is
  byte-identical, because only one factory is built there.
- **The frames' work now shares Factory Alpha with the expansion.** Pressing goes to Alpha, at 50
  a tick home, and Gamma keeps one later stage of twenty runs (160 working
  ticks each way: Alpha 528 → 688, Gamma 320 → 160). Factory Alpha's waiting-input ticks fall from 394
  to 18. The dock build pays for it: it waits behind the frames' pressing on Alpha.
- **Priority now has something to act on in B.** Before, the frames were ready at the same tick at
  Normal and at High, because no frames task ever met another at a selection boundary (K2 review).
  Now High buys 175 ticks (717 → 542), and the two builds pay for it. That is the trade situation B
  was designed to show.

Situation A: all 12 demands are ready, before and after.

| Demand | Before | After |
| :--- | ---: | ---: |
| expedition_1_frames | 941 | 571 |
| expedition_1_modules | 2,412 | 518 |
| expedition_2_frames | 766 | 571 |
| expedition_2_modules | 1,462 | 396 |
| component_reserve | 662 | 289 |
| expedition_3_frames | 1,062 | 500 |
| expedition_3_modules | 1,140 | 335 |
| expedition_4_frames | 362 | 571 |
| expedition_4_modules | 274 | 396 |

- **Total readiness halves**, from 9,915 to 4,981 ticks.
- **Changeovers rise** from 13 (1,560 ticks) to 20 (2,400 ticks). Work that used to pile onto
  Factory Gamma, and wait on its slow return, now spreads over Alpha and Beta, and each switches
  stage more often.
- Expedition 4 is slower than before, because earlier orders no longer leave Gamma's queue for it
  to have to itself.
- **`situation-a-priority` now reads exactly as `situation-a`**, the hash aside. Raising expedition
  3 buys nothing, because its work no longer queues behind anything.

`smoke` and `situation-b-alone` are byte-identical. Every kernel, shell and replay test passed
unchanged; one planner test was added.

## Not done, on purpose

- **No stock outside the hold is counted.** Allocating stock already in the right buffer, and
  routing buffer to buffer, are the rest of the plan's K5b, and wait for K3 and K4.
- **Line load is not in the estimate.** The estimate reads each route's fastest line. The actual
  leg still goes to the least-loaded line (`ChooseTransport`), so the two can disagree when a fast
  line is busy.
- **A queue's waiting is not in the estimate.** Queued ticks are work, not the time that work
  will spend waiting on input. B's dock build is where that shows.
