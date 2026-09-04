# Launch Pad — Stage 0–4 Review

Date: 2026-09-04
Reviews: `docs/superpowers/plans/2026-09-03-launch-pad.md` stages 0–4, at `dc1cc6b`.

Received as-is. The adjudication is
`docs/reviews/2026-09-04-launch-pad-review-analysis.md`; read it alongside this, because the review
was written against an earlier draft of the spec and five of its ten findings are decisions the
spec had already made the other way.

## Verdict

The direction is right. Task/script collapse, `Programs`/conditions, `Built` enforced in every
place the spec names, the commissioning phase, the loader rules, and the quiet scenario all match
the spec's shape. The problems are at the edges: the launch-pad plan was rewritten mid-way
(`fc8892c`) in ways that diverge from the verb spec, and the code follows the plan where the two
disagree. Two findings are against the spec's own contracts (1, 2), two make the headline plan
impossible to run on the shipped vessel (8, 9), and the rest are small.

## Issues

| # | Severity | Issue | Where |
|---:|---|---|---|
| 1 | Bug | **Save drops task conditions.** `Capture` writes an empty condition list for every task and `Restore` rebuilds every script unconditioned. `ConditionDto` / `OperandDto` exist but are never populated. `EnumRef` is not saved by name. `OperandDto` carries an ordinal. No round-trip test covers conditions. | `State/Save/WorldSave.cs` |
| 2 | Bug | **Transfer conditions are a one-shot gate.** `ReadyToMove` skips conditions once anything has moved. A transfer has nothing in flight between ticks (withdraw and deposit are the same tick); the spec's carve-out is for a producer run. No test enqueues a conditioned transfer. | `SimulationEngine.ReadyToMove` |
| 3 | Divergence | **Commissioned facility produces next tick, not the same tick.** `Advance` lists built producers before `CommissionFacilities()`. Spec §3 says same tick. A test locks in the wrong rule. | `SimulationEngine.Advance`, `CommissioningTests:52` |
| 4 | Divergence | **Loader capacity check counts built-at-start only.** Spec §2 wants every authored slot. Numbers: built 8,100 / authored 8,700 against 10,000, so the spec's sum rule passes; the change bought nothing. | `JsonContentSource` standing-draw sum |
| 5 | Divergence | **First-plan recipe simplified.** `assemble_dock_unit` takes 400 Basic Metals only; spec: components + Basic Metals + Technical Materials, with a completion-time acceptance test. No test. | `schematics.json` |
| 6 | Divergence | **Star legs: four routes instead of three, on the narrow stage lines.** Spec adds three routes on two generic archetypes; code adds four (`factory_b_feed_components` duplicates `factory_b_feed`'s route) on the 13/tick and 7/tick stage archetypes, and the generic `factory_feed` / `factory_return` are unused. The 13/tick return alone takes 77 ticks to move one unit. Spec's 7,900 / 8,500 standing figures are exactly the three-leg vessel. | `default_vessel.json`, `transports.json` |
| 7 | Naming | `ConditionKind.ExecutorStatus` (spec: `ExecutorStatus`) — a wire word once 1 is fixed. `Snapshot TaskState` / `CommittedPlastane` (spec: `TaskEntry` with `Script`, `PlanEntry`). `Enqueue` (schematic, runs, executor) / `EnqueueTransfer` deleted rather than kept as conveniences. `BaseGraphLayout.For(scenario, state)` discards state. Dangling (`see cref="TransportTask"`) in `Ledgers.cs`. | Various |
| 8 | Blocker | **The construction-unit schematic is not in `unlockedSchematics`.** The planner reports `LockedSchematic`; Build Launch Pad 1 cannot be planned on the shipped vessel. Nothing tests the shipped plan end to end. | `default_vessel.json` |
| 9 | Blocker (spec) | **One unit fills a factory buffer exactly, which deadlocks the factory making it.** Spec §9 chose `holdCapacity 40,000` so one unit fills a `facility_buffer`. `ReservedVolume` holds back a whole run's output for the facility's schematic and `RoomForDelivery` subtracts it, so deliverable room for every input is zero once the factory is set up for the unit. The reservation rule is deliberate; the content must change (80,000 → half a buffer). | `spec §9`, `items.json` |
| 10 | Blocker (spec) | **120 ticks is unreachable with the current planner and switch-over.** The planner assigns by declaration order and load; none of the chosen facilities is configured for the work, and a switch-over starts only once inputs are present, so three 30-tick switch-overs run in series. With 100/tick star legs the plan lands near tick 132. The spec's "a fresh vessel's first plan pays no switch-over" does not hold. | `ProductionPlanner.ChooseFacility`, `SelectAndStart` |

## Missing tests noted along the way

- Unknown `TargetRef` refused at enqueue.
- A `Produce` on a line and a `Transfer` refused (today they fail with "No executor", not the kind mismatch).
- A slot whose archetype name has no unit never builds.
- The hold-legs order resets `Any`, not `Single`.
