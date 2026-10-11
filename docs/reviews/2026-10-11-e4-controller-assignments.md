# E4 — A Controller's Order Can Assign Stages

Date: 2026-10-11
Ticket: E4 (#78), follow-up 4 of `docs/reviews/2026-10-10-e3-experiment-report.md`

## Goal

E3 gave scripts the composer's facility picker: a demand may carry `assign`. Controllers could not
do the same. `ControllerContext.Order` took a goal and a destination and nothing else, so a
controller could schedule by priority and hold, but never by placement. Placement is the manual
move that decided situation A. E3 left one question to this follow-up as well: why the improved
controller's restocks ran long once A's hardening was moved by hand.

## What was built

- **`ControllerContext.Order(goal, destination, assign)`.** The new optional `assign` takes the
  same `ScriptedAssignment` list a scripted demand does. It runs through the same code: before
  approval, each pair moves every production step running that schematic to that facility, by
  `SetExecutor`. Each move is an intervention and is counted under facility assignments.
- **An assignment the draft cannot honour** makes approval refuse the order, as the composer
  would, and counts for nothing. A facility of the wrong type is the example tested.
- **No policy uses it.** The reference controllers are unchanged, so every recorded replay is
  byte-identical: all 27 script and policy pairs measured after K5c.

## The question E3 left open

At E3, the manual-plus-improved run on A read 3,330 against the improved controller's 2,032 alone,
with 50 changeovers against 40. After K5c, the same pair reads **889 against 853**, and the whole
gap is on Reactor Beta.

| | Improved | Improved, hardening assigned to Reactor Beta |
| :--- | ---: | ---: |
| Readiness sum | 853 | 889 |
| Reactor Beta changeovers / ticks | 0 / 0 | 3 / 360 |
| Restock `module#6`, readiness | 337 | 406 |
| `expedition_3_modules`, readiness | 4 | 38 |

- **Before K5c,** the planner's estimate could not see a changeover. The scripted bulkheads'
  hardening went to Reactor Beta, by assignment. The controller's unassigned restocks went to
  Reactor Alpha, so both reactors hardened and both switched. That was most of the 3,330.
- **After K5c,** restocks follow the setups the estimate sees, and the residual is the
  assignment's own cost. Reactor Beta opens set up for Technical Materials. Once the hand
  assignment gives it hardening as well, it switches between the two. The Technical Materials for
  the modules restock wait behind that, and expedition 3's modules with them.
- **So the hand assignment is the wrong move after K5c.** K5c's review already found that
  `e1-a-manual.json` is now 12 ticks behind plain queue order. Giving controllers the same
  assignment would not help a policy here, because the planner already places A's stages well.
  What this ticket adds is the ability. A policy that uses it is a separate decision. If it is
  tuned on A or B, it is also a tuning decision.

## Tests

`ControllerTests` gains two tests. Both fail with the assignment dropped inside `Order`.

- **An order assigned to Reactor Alpha.** The order is for Technical Materials, which the hold
  lacks and Reactor Beta is set up to make. Reactor Alpha then does the separating. It counts one
  assignment and three interventions: the build, the order and the move. A control run assigned
  to Reactor Beta, where the stage already is, edits nothing and leaves Reactor Alpha idle.
- **An order assigned to Factory Alpha,** which cannot separate. Approval refuses it, the order
  never commits, and it adds no intervention and no assignment.

Suites: Core 449, Shell 108, Replay 56.

## Not done

- **No reference controller uses assignments.** See above.
- **Urgency on a demand (follow-up 3)** stays with the mission system's design.
- **Recovery (follow-up 5, D4/K7)** is next in E3's list.
