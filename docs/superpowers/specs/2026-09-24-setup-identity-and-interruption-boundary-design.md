# Setup Identity and the Priority Interruption Boundary — Design

Date: 2026-09-24
Status: Decided (D1)

## Goal

Answer the three questions of the scheduling design's §7.1 before any kernel code is written
against them:

1. **Setup identity.** What a facility is configured *for*, so that two orders for the same part
   never pay a changeover and two genuinely different processes always do.
2. **Interruption boundary.** The exact points in a facility's tick at which a higher-priority task
   may displace the work it is doing, and the points at which it may not.
3. **Final remainder.** Whether a demand smaller than one run's output consumes inputs and energy
   pro rata, or rounds.

The output is this document and the list of engine behaviours that change, in
[§ Engine behaviours that change](#engine-behaviours-that-change). It gates K1 (setup identity and
changeover rebalance) and K2 (priority in selection). It changes no code, no content and no save
format.

## Source material

[Issue #45](https://github.com/Gatov/DimenshipGame/issues/45), ticket D1 of
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`:

> **Setup identity:** is it the schematic (today's `FacilityInstance.Configured`), a process family
> authored on schematics, or something else? Two orders for the same part must never pay a
> changeover.
> **Interruption boundary:** at what point may a higher-priority task displace the configured one?
> Only at a run start, or also by abandoning a switch-over already under way? Never mid-run.
> **Final remainder:** does a remainder shorter than a full run consume inputs and energy pro rata
> or round? Integers only.

`docs/production scheduling and automation - gameplay design.md`, §4 *Production Campaigns and
Setup*:

> The proposed setup cost follows recipe configuration: two orders for the same part can run
> consecutively without reconfiguration. Switching to another process costs significant time.
> Priority must be able to override setup preference at a safe boundary; otherwise an urgent order
> cannot interrupt a continuously supplied task. Inputs and work already committed to a batch remain
> accounted for, and cargo in transit remains physically present.

And §5: *"Priority changes must respect active batches and in-flight cargo."*

`docs/specs/Dimenship_Planning_and_Task_Execution_Spec.md` for executor queues, partial execution
and changeovers; `docs/superpowers/specs/2026-09-04-conveyor-belt-design.md` for cargo in flight;
`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md` for the rule that a
recovery path must never become a material source.

## What the engine does today

`SimulationEngine.StepProducer` gives a facility one of four things to do each tick, in this order,
and returns after the first that applies:

1. **A switch-over under way** (`SwitchOverRemaining > 0`) counts down. It is committed: nothing
   re-examines the queue until it finishes, and the target is not re-checked for readiness.
   `Configured` still names the *old* schematic for the whole countdown and changes only on the
   tick it completes.
2. **A finished run awaiting deposit** (`RunAwaitingDeposit`) retries the deposit. Its inputs are
   already consumed.
3. **A run in progress** (`RunActive`) advances, and pauses in place if the vessel cannot supply
   its energy this tick. Its inputs are already consumed.
4. **Otherwise `SelectAndStart`**, which takes the first of:
   1. the current task, if its next run can start;
   2. the first queued task on the `Configured` schematic that can start;
   3. the first queued task on any schematic that can start, which starts a switch-over of the
      archetype's `switchOverTicks` (30 on every shipped archetype) — free for a facility that was
      never configured;
   4. nothing, in which case every unfinished task is postponed with its root cause.

"Can start" is `ReadyToStart`: conditions met, inputs present in the local buffer, and room for the
output after the inputs are consumed. A run is whole: `StartRun` withdraws every input of the
schematic at once, `AdvanceRun` charges energy cumulatively so a completed run has charged exactly
`energyPerRun`, and `TryDeposit` deposits exactly the schematic's output quantity. There is no
partial run anywhere. The planner turns a quantity into runs by ceiling division
(`PlanDraftEditor`: `(deficit + output − 1) / output`).

Setup is therefore already keyed to the schematic, and already free between two tasks on one
schematic. Step 2 matches on `task.Produce.Schematic`, never on the task id. What is missing is any
notion of one task outranking another.

## Decisions

### 1. Setup identity is the schematic, with no process family

`FacilityInstance.Configured : SchematicId` remains what a facility is set up for. A changeover is
due exactly when the next run's schematic differs from `Configured`, and at no other time. Two
tasks on one schematic, from the same plan or from two different plans, run back to back with no
switch-over. That is already true, and K1 pins it with a test that uses two tasks, not one task of
several runs. The existing `SwitchOver_IsFreeWhenTheSchematicDoesNotChange` uses a single task, so
it does not prove the property the issue asks for.

**"The same part" means the same schematic, not the same output item.** The shipped reactor makes
`basic_metals` two ways: `separate_basic` (4000 matter mix, 4800 energy) and `synthesize_basic`
(12000 hydrogen, 19200 energy). Those are different processes with different inputs and a
fourfold difference in energy per run. Moving between them is a real reconfiguration, and letting it
be free because the output id matches would erase the distinction the reactor's two recipes exist to
make. Which schematic an order uses is decided by the plan that names it, and an order does not
silently change it.

**A process family was considered and rejected for now.** An authored `setupFamily` on schematics
would let several recipes share one setup. On the shipped vessel it has nothing to group: every
facility kind's schematics differ in inputs, and a family would only make some changeovers free.
That reduces exactly the scheduling pressure the first experiment is meant to measure. It stays a
clean later extension. `Configured` stays a `SchematicId`, and the changeover test becomes
`FamilyOf(Configured) != FamilyOf(next)` with the default family being the schematic itself. That
is a catalog and loader change with **no save-format change**. Add it when an E-phase result shows
recipe-level setup is too harsh, not before.

**The individual order is rejected outright.** Charging per task id would make two orders for the
same part pay, which the design forbids by name ("avoid charging merely for a different order ID").

K1 is therefore a content rebalance and a test, not an identity change: shorter fixed runs and a
more expensive `switchOverTicks`, with M2 measuring the effect.

### 2. Priority ranks first; setup preference breaks ties within a priority

K2 gives every task a priority, an integer where a higher number outranks a lower one and every
existing task has the same default. Selection takes the **highest-priority task that can start**.
Among tasks of equal priority, it keeps today's order exactly: current task, then a task on
`Configured`, then queue order.

That ordering is the whole of the "priority overrides setup preference" requirement:

- A higher-priority ready task on another schematic beats the current task, even while the current
  task could keep running. It pays the switch-over. This is what lets an urgent order interrupt a
  continuously supplied task (`Produce.Runs` null), which today's step 1 would otherwise keep
  forever.
- A higher-priority ready task on the *same* schematic becomes current with no switch-over, per
  Decision 1.
- **A task that cannot start does not hold the facility, whatever its priority.** Selection only
  ranks tasks that are ready. An urgent task still waiting for inputs does not stop lower-priority
  ready work from running. Idling a machine to wait for material is a reservation, and reservations
  are D3's (claims, hold and release), not a selection rule. Without this, one urgent task missing
  its ore would stall every facility it was queued on.
- **Equal priority never pre-empts.** Only a strictly higher priority displaces anything. With every
  task at the default, `SelectAndStart` makes the same choice it makes today, tick for tick. The
  shipped determinism tests, `EveryShippedUnbuiltSlot_CommissionsFromAQuietVessel_…`, and the M3
  baseline stay valid without re-recording.

The tie-break within a priority is **queue order on that executor**, which is enqueue order and
therefore commit order. It is not executor declaration order in disguise. It orders work on one
machine, never one machine against another. Contention *between* executors, for stock in Resource
Storage or for power, is still decided by visit order. Removing that is K6b's job under D3, and this
document does not touch it.

### 3. The boundary is between runs; a run is never interrupted

A facility may change what it is doing only at **selection**, the point where `StepProducer` reaches
`SelectAndStart` because it has no active run, no run awaiting deposit and no switch-over (with
the single exception in Decision 4). Concretely:

| Facility state | May a higher-priority task displace it? | Why |
|---|---|---|
| Run in progress | **No** | Its inputs are consumed. Abandoning it either destroys them or needs a refund rule, which is D4's recovery question and not a scheduling one. |
| Run paused for energy | **No** | Still a run in progress. The pause is the vessel's shortage, not a free slot. |
| Run awaiting deposit | **No** | The output exists and must land. Starting other work would need room the held run is already waiting for. |
| Between runs of one task | **Yes** | This is the "run start" boundary. A continuous task gives it up at the end of every run. |
| Idle, or all tasks postponed | **Yes** | Nothing is committed. |
| Switch-over under way | **Only by a strictly higher priority**, as Decision 4 describes | No material is committed, only time. |

With short fixed runs (K1), the wait at the boundary is at most one run's duration. That is the
design's argument for short runs in the first place.

Transport lines have no setup and are outside `SelectAndStart`, but the same rule carries over to
K2 at their own boundary. Priority chooses which transfer the next free tail slot **loads**. Cargo
already on a belt is never unloaded, reordered or diverted. Per the conveyor spec, a transfer's
`Current` clears once it is entirely aboard, so the line is between transfers at exactly that
point.

### 4. A switch-over may be abandoned, and material decides what that costs

A switch-over commits no material. It is time and standing power. Holding it committed against a
more urgent task would make the urgent task wait for a reconfiguration to something nobody now
wants, and then pay a second one. So at the top of `StepProducer`, while `SwitchOverRemaining > 0`,
K2 checks for a ready task whose priority is **strictly higher than the switch target's**. If one
exists, the switch-over is abandoned on that tick and redirected according to the new task's
schematic:

| New task's schematic | Result |
|---|---|
| Same as the switch target's | **Retarget only.** `SwitchTarget` becomes the new task and the countdown continues. Two orders for the same part never pay twice (Decision 1). |
| Same as `Configured`, the setup still loaded | **Cancel.** `SwitchOverRemaining` becomes 0, the setup never changed, and the new task starts its run on this tick. The elapsed ticks are lost, and nothing else is. This is physically honest *because* `Configured` does not change until a switch-over completes, and it must stay that way. |
| Anything else | **Restart.** The countdown restarts at the full `switchOverTicks` toward the new task, and this tick is its first tick. Partial progress toward one schematic is not progress toward another. Crediting it would make a switch-over divisible, so a player could pre-pay changeovers in slices. |

A switch-over is *not* abandoned when its target merely stops being ready, or when an equal-priority
task appears. Both behave as today. The countdown finishes, and selection afterwards takes whatever
is ready. Abandonment is a priority decision only. Because it needs a strictly higher priority, and
priorities change only by command, the number of abandonments is bounded by the number of commands.
There is no automatic flapping.

A switch-over is only ever started *toward a ready task*, as today. Setting a machine up ahead of
its inputs arriving is a legitimate tactic, but it is a player or controller command (C0), not
something selection infers.

### 5. The final remainder rounds up to a whole run; there are no partial runs

A run consumes all of its inputs and all of its energy and deposits all of its output, always. A
demand that is not a multiple of one run's output is met by ceiling division, as the planner already
does. The overshoot is less than one run's output, and it lands in the output buffer as ordinary
stock that the next plan already counts as available.

Pro rata was rejected because integers cannot do it honestly. A remainder run scales every input,
the energy and the output by `remainder / output`, and each of those divisions rounds on its own.
Rounding inputs down makes a partial run a small material discount. Split one order into many
remainders and the vessel becomes a material source, which is the exploit the recycling spec was
written to forbid. Rounding inputs up makes it a material sink. Either way, the ratio a schematic
authors stops being the ratio the vessel obeys. A partial run would also shorten the unit of time
that a changeover is traded against, and the design's whole mechanic rests on that unit.

Rounding up costs at most one run's worth of surplus per order. K1's shorter runs shrink that
surplus directly. Construction units are exact by construction (one run, 1000 milli-units, one unit).

## Engine behaviours that change

**K1, setup identity and rebalance.** Nothing in `SelectAndStart` or `StepProducer` changes.

- Content: shorter fixed runs (`effortPerRun`) and a significantly larger `switchOverTicks` on the
  archetypes K1 selects, measured against the M3 baseline.
- Test: two *separate* tasks on one schematic run back to back with no `SwitchOverStarted`.
- Test: `separate_basic` → `synthesize_basic` on one reactor pays a full switch-over although both
  produce `basic_metals`.

**K2, priority.**

1. **`SelectAndStart` ranks by priority first.** It chooses among ready tasks of the highest
   priority present. Within that tier, today's three steps run unchanged: current, then
   `Configured`, then queue order. A higher-priority ready task on another schematic starts a
   switch-over even while the current task could continue.
2. **Not-ready tasks never hold the facility.** An unready higher-priority task does not block a
   lower-priority ready one (Decision 2). Step 4's postponement of unready tasks is unchanged.
3. **`StepProducer`'s switch-over branch can abandon.** Before `AdvanceSwitchOver`, a ready task of
   strictly higher priority than `SwitchTarget` retargets, cancels or restarts the switch-over, per
   Decision 4's table. The existing rule that the deciding tick is the first tick of the countdown
   applies to a restart. Cancelling back to `Configured` starts the run on the same tick.
4. **The run-in-progress and awaiting-deposit branches are untouched.** They are the "never
   mid-run" guarantee, and K2 adds a test that an urgent task committed mid-run starts only after
   that run deposits.
5. **New outcomes are appended, never inserted.** The plan requires a way to distinguish
   *outranked* from *physically blocked* (a status or postpone reason, K2's choice). It goes last,
   after `ConditionNotMet`, so `PostponeReasons.RootCause` keeps preferring a physical cause. A
   `SwitchOverAbandoned` event code is appended to `EventCode`, carrying the abandoned and the new
   task ids, and precedes the `SwitchOverStarted` of a restart.
6. **Default priority is behaviour-neutral.** A world where no task's priority was ever changed
   advances byte-identically to today. That is K2's regression test, and it keeps the M3 baseline
   comparable.
7. **Priority is state and is saved.** It sits beside the task in `SaveFile`, as a nullable DTO
   field under the save-format rules. The abandonment rule needs no new state:
   `SwitchOverRemaining`, `SwitchTarget` and `Configured` already describe a switch-over fully,
   and `RoundTrip_KeepsASwitchOverUnderWay` already covers them.

**Unchanged by either.** Energy allocation between facilities, and withdrawals from Resource
Storage, still follow executor visit order (D3 / K6b). The planner's ceiling division is unchanged.
No partial run exists anywhere. Cargo on a belt is never touched by priority.

## Acceptance criteria

- Two tasks on one schematic never cause a switch-over between them.
- Two schematics with the same output item cause a full switch-over.
- An urgent task displaces a continuously supplied one at the next run boundary, never before the
  current run deposits.
- A switch-over is retargeted, cancelled or restarted by a strictly higher priority, and by nothing
  else.
- With every task at default priority, a shipped-vessel run is byte-identical to one on the code
  before K2.
- No test, content file or save introduces a run that consumes or produces a fraction of its
  schematic.

## Not built

- **A process family** (`setupFamily`). Deliberately deferred (Decision 1). The extension path needs
  no save change.
- **Pre-emptive setup**, switching toward work whose inputs have not arrived. It belongs to the C0
  command surface if it is wanted at all.
- **Starvation.** A low-priority task can wait indefinitely behind a stream of higher-priority work.
  The rule that bounds this belongs to D3, with the other fairness and tie-break questions across
  objectives.
- **Priority across executors** for shared stock and power. That is D3 / K6b.

## Open items

- Whether the *outranked* signal of K2 point 5 is an `ExecutorStatus`, a `PostponeReason`, or
  both. The postpone reason reads naturally in `TaskAttempt` history (K8), and the status reads
  naturally on the card (U3).
- Whether K1's rebalance moves `switchOverTicks` uniformly or per archetype. Uniform is the
  cheaper first measurement.
