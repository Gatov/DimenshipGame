# Launch Pad — Stage 0–4 Review, Adjudicated

Date: 2026-09-04
Reviews: `docs/reviews/2026-09-04-launch-pad-review.md`
Against: `docs/superpowers/specs/2026-09-03-launch-pad-design.md`,
`docs/superpowers/plans/2026-09-03-launch-pad.md`, at `dc1cc6b`.

## Verdict on the verdict

Five findings are real and are fixed. Two of them — 8 and 9 — stop the headline feature dead, and
neither was reachable by any existing test, because every planner and commissioning test builds its
own world through `WorldBuilder` and only the shipped content carries the collision.

The other five are the review reading against a spec that no longer says what it quotes. The
document was rewritten twice during implementation — `fc8892c` ("quiet opening and ConditionNotMet
naming") and `eb969be` ("vessel opens quiet on a hold-star topology") — and the code follows the
rewrites. The review calls that "the code follows the plan where the two disagree", which is the
right description of the evidence and the wrong conclusion about which document is current: the
spec at HEAD **is** the decision, and the brief it cites (`plan-poc.md`) is not in the repository.

That is worth stating plainly rather than leaving to a later reader, because four of the five would
be re-opened by anyone who found the review first.

## Accepted

### 1. Save drops task conditions — fixed

`Capture` wrote `Conditions = Array.Empty<ConditionDto>()` for every task and `Restore` rebuilt
every script with `Array.Empty<Condition>()`. The DTOs existed and nothing populated them.

The round-trip test could not see it: it asserts `Write(reloaded) == written`, and both sides wrote
`[]`. That is the general shape of the miss — a canonical-output comparison proves two writers
agree, not that either wrote everything.

Fixed with `Capture`/`Restore` pairs for `Condition` and `Operand`, `EnumNames` for the by-name
enum translation the `EnumRef` doc comment already promised, `OperandDto.EnumValue` deleted
outright, and `CheckDrift` extended over the ids a condition names — the same question `Enqueue`
asks at creation, which a save bypasses. `Restore(TaskActionDto?)` was also swallowing an unknown
action kind into `new Produce(SchematicId(""), 0)`; it reports now, which is the file's own stated
contract.

`WorldSave.CurrentVersion` stays 1. Deleting a field from a format nobody has written is not a
migration.

### 2. Transfer conditions were a one-shot gate — fixed

`ReadyToMove` returned early on `task.State == TaskState.Running || task.MovedQuantity > 0`. The
review's reasoning is exactly right: `TryMove` withdraws and deposits in the same tick, so nothing
is in flight between ticks and every tick a haul moves is a start. A thousand-unit haul on a 13/tick
line was gated once and unconditioned for the following seventy-six ticks.

The producer carve-out is untouched and stays structural: `StepProducer` returns on `RunActive` /
`RunAwaitingDeposit` before selection is reached, so a run in flight never meets the gate. The
asymmetry is now stated in the spec rather than implied by a flag.

Worth recording as the sharper version of the bug: `MovedQuantity` was saved and the conditions were
not, so a reloaded half-moved haul came back with the bypass set and the gate gone. Fixing 1 alone
would have left that; fixing 2 alone would have left conditions unsaved. They were one defect.

### 8. `assemble_dock_unit` was not unlocked — fixed

`ProductionPlanner` filters producers by `IsUnlocked` and reports `LockedSchematic` when none
survives; `Enqueue` refuses the task on the same test. Build Launch Pad 1 could not be composed at
all on the shipped vessel. One line of scenario content.

`LaunchPadTests.TheShippedVessel_CanPlanItsFirstLaunchPad` covers it. The loader validates that
every id in `unlockedSchematics` exists, not that every schematic something needs is listed, which
is why nothing caught it.

### 9. A whole unit filled a facility buffer exactly — fixed

The arithmetic: `CapacityOf = holdCapacity × capacityPermille / 1000`, `facility_buffer` is 25‰, so
at 40,000 a buffer held exactly 1,000 milli-units — and `assemble_dock_unit` outputs exactly 1,000.
`ReservedVolume` holds back one run's output at the schematic a facility is set up for and
`RoomForDelivery` subtracts it, so a factory configured for the unit reserved its entire buffer and
reported zero room for every item, its own 400 Basic Metals included.

Two things make it worse than the review says, and both argue the same fix. `Configured` is only
ever written by `AdvanceSwitchOver`, so a factory left on that schematic never recovers: it cannot
be fed, and it cannot be re-tasked without being fed. And `RoomAfterConsuming` would have required
the buffer to be exactly empty after the run's inputs came off for the output to fit at all — zero
margin at both ends of the same run.

`holdCapacity` 80000, so one unit is half a buffer. The reservation rule stays as designed; it is
the thing keeping the shipped chain out of a permanent deadlock, and the spec's own hold-volume
document argues it at length. `items.json`'s note and the spec's §9 now say why the number is what
it is — the relationship was load-bearing and written down nowhere.

### 4. Loader capacity check — accepted as an improvement, not as stated

The review is right that the check counts `builtAtStart` only, and right that it changes nothing on
the shipped vessel: 8,100 built and 8,700 authored, both under 10,000. It is wrong that spec §2
asks for the authored sum — §2 is about `Built` enforcement and says nothing about it, and the
loader rule in §1 is about routes.

Taken anyway, because the underlying point stands on its own: a scenario authors two vessels, the
one it opens with and the one it can reach, and a campaign that authors more than it can ever power
is a content mistake an author can fix at load rather than a brownout a player finds hours in. Both
sums are now checked, and the authored one is reported only when the opening one fits, so an author
reads one error about the vessel they wrote.

### 7, in part

- **`BaseGraphLayout.For(scenario, state)` discarded `state`** — the body was `_ = state;` under a
  comment explaining why. Parameter dropped. A signature that says the layout reads the world is an
  invitation to filter the unbuilt out of it, which is the one thing it must not do.
- **Dangling `<see cref="TransportTask"/>` in `Ledgers.cs`** — the type went in the task/script
  collapse. It names `Transfer` now.
- **The "No executor" message** (from the missing-tests list) — facilities and lines live in
  separate indexes, so a `Produce` addressed at a line read as an executor that is not aboard. The
  message names the mismatch now.

### 6 and 10, on their merits rather than as stated

Both findings misdescribe the spec — §1 specifies four routes, and the standing figures the review
attributes to it (7,900 / 8,500) appear nowhere in the repository. But underneath each is something
true.

Six: the generic `factory_feed` / `factory_return` archetypes were defined, documented, and used by
no route, while `transports.json`'s own header note claimed the star legs were the exception those
archetypes exist for. Content and its note disagreed.

Ten: the timing claim is stale — with the shipped one-input recipe there is one run, not three, and
so one switch-over rather than three — but 13/tick and 7/tick star legs *are* the dominant cost. The
return of one construction unit took 77 ticks by itself and the plan landed near tick 141.

Both resolve the same way: the four hold-star legs now run on the hub archetypes at 50/tick. That is
what those archetypes were added for, it removes content nothing referenced, and the plan lands near
tick 84 — inside the soft 120 the spec names and no test asserts. Standing draw is unchanged, so the
8,100 figure holds.

## Declined

### 3. Commissioning and the tick a facility first produces

The review reads spec §3 as "same tick" and calls the test wrong. §3 says the opposite, in the
sentence that explains the phase's placement:

> Placement between the two loops is the determinism contract: a unit delivered this tick
> commissions this tick, and the facility produces from the next.

The code lists built producers before `CommissionFacilities()` precisely to hold that. The test
named in the finding —
`AFacilityCommissions_TheTickItsUnitArrives_AndProducesTheTickAfter` — locks in the rule the spec
states. No change.

### 5. The first-plan recipe

Spec §9 is explicit and gives its reason:

> `assemble_dock_unit` (inputs: **only** `basic_metals` — a brief extension so the first plan is one
> factory run rather than a recursive expansion of the whole chain)

A three-input recipe is the pre-`fc8892c` position. Reinstating it would turn the first plan into a
three-stage chain across three factories and three switch-overs, which is the arithmetic finding 10
describes — the review is, in effect, reporting the consequence of finding 5 as a separate bug. No
change.

### 7, in part — the naming half

- `ConditionKind.ExecutorStatus` **is** the spec's name (§5 records the rename from the
  programming-view's `ExecutorStatusIs` and why). The finding's own text asks for what the code has.
- `TaskInstanceState` and `CommittedPlanState` are §7's names, verbatim. `TaskEntry`, `Script` and
  `PlanEntry` appear in no spec and in no commit in this repository.
- `EnqueueTransfer` was deleted deliberately: §4 says "One `Enqueue(TaskScript, ExecutorId)`
  replaces both entry points." One stale sentence in §2 still named it; that prose is corrected, the
  API is not.

## What this did not touch

Stages 5 (`ProductionPlan` with `Destination`, `PlanCompleted`, deleting `ShortageKind.RawResource`)
and 6 (the Operations view) are unbuilt and stay unbuilt. Their checkboxes are unchanged.

That bounds what "the first plan works" can mean here. The planner routes hold ↔ facility buffer and
stops; the final `resource_storage → dock_a_hold` leg is Stage 5's `Destination`.
`LaunchPadTests.TheFirstLaunchPad_CommissionsFromAQuietVessel` enqueues that leg by hand and runs
the plan through to `FacilityBuilt`, which is the most the kernel can be asked to prove today.
