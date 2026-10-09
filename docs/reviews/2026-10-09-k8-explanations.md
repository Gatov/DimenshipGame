# K8 — Explanations, and the First Alert

Date: 2026-10-09
Ticket: K8 (#62), to D3's Decision 4 and *What changes, by ticket*

## Goal

Make a waiting task say who it waits behind, and make a plan starving behind another plan visible
without correcting it. The design's §5 asks for exactly this report: *"a repair waited because its
components were allocated to an upgrade."*

## What was built

- **A postponement names its other party.** `TaskAttempt` gains `ByTask` and `ByPlan`, resolved
  in `PostponeTask` from the world at the moment of postponing:

  | Reason | Names | Picked as |
  | :--- | :--- | :--- |
  | `Outranked` | the task chosen instead | the facility's current task, or the transfer the line loaded |
  | `MaterialClaimed` | the plan holding the stock | the input short of what the task may spend; the largest holder, then the oldest |
  | `DestinationFull` | the other plan holding stock where there is no room | the largest holder there, then the oldest; null when only free stock fills it |

  The names reach the snapshot (`WaitingOnTask`, `WaitingOnPlan`), the postpone event (`task`,
  `by`, `plan`) and the save (`AttemptDto.ByTask`, `ByPlan`). A save from before K8 loads with no
  causes, which is what it recorded.
- **In words.** `Presentation/WaitCause.For(snapshot, task)` turns a cause into a sentence and
  returns null for a reason with no other party, which the shell already words. Operations shows it
  under a waiting task, and the replay's unfinished-work table gains a **Behind** column naming the
  demand.
- **`AlertCode.PlanWaiting`, the first alert the kernel raises.** It fires when a plan has gone an
  operational hour without progress (`CommittedPlan.LastProgressAtTick`, saved) and one of its tasks
  waits behind another plan. The alert names that plan and clears the tick the condition clears.
  Held plans raise nothing, and a release or an amend restarts the clock. Nothing is corrected:
  aging was rejected in D3. Alerts now reach the snapshot, and the replay report lists them.
- **The shell's three copies of the reason vocabulary** had no word for `Outranked` or
  `MaterialClaimed` and rendered both as `UNKNOWN`. They now say `OUTRANKED` and
  `MATERIAL_CLAIMED`.

## Where this departs from D3

- **No *setup preference* explanation.** K2 records `Outranked` only for a strictly higher
  priority, so every recorded one means *higher priority*. Recording equal-priority passing-over
  would change every world where nobody set a priority, which K2 deliberately avoided. It waits for
  a ticket that needs it.
- **`DestinationFull` names a plan too.** D3 did not ask for it. C0's measurement did: a held
  plan's cargo filled Factory Alpha's buffer, and the construction runs it blocked could not say
  whose stock it was.

## Neutrality

Every script's metric tables are unchanged, tick for tick. Only the final hashes differ, because
plans now save `lastProgressAtTick` and postpone events carry the cause. No situation raises a
waiting alert.

## What the reports now say

Situation B with the upgrade held at tick 600 and never released, cut at tick 1,500. The runs that
C0's review traced by hand now name the cause themselves:

| Demand | Task | Executor | Work | State | Reason | Behind |
| :--- | ---: | :--- | :--- | :--- | :--- | :--- |
| build_reactor_b | 9 | factory_a | assemble_reactor_unit 0/1 runs | Postponed | DestinationFull | upgrade_components |
| build_dock_a | 13 | factory_a | assemble_dock_unit 0/1 runs | Postponed | DestinationFull | upgrade_components |

Run to tick 6,000, the same hold raises the first waiting alerts any run has raised:

| Demand | Behind | Reason | Raised | Cleared |
| :--- | :--- | :--- | ---: | ---: |
| build_reactor_b | upgrade_components | DestinationFull | 3635 | still raised |
| build_dock_a | upgrade_components | DestinationFull | 3651 | still raised |

The alert names a held plan as the one the builds wait behind, which is the truth: the hold is
what keeps that space occupied. Releasing the upgrade, or moving its stock out of the buffer, is
the player's or a controller's call. The alert only makes the call visible.

## Open

- **The threshold** is D3's first value, one operational hour. It should be retuned against E1's
  situations once they exist.
- **The executor's own reading** still reports a facility with a held task as Held, ahead of a
  `DestinationFull` behind it, because `SafetyLock` ranks first in root-cause order. The per-task
  cause is now there to read, so U3's executor card can show both.
