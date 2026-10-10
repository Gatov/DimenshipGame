# Production Scheduling and Automation — Ticket Plan

Status: Draft

**Goal:** Turn the exploratory design in `docs/production scheduling and automation - gameplay
design.md` (2026-09-23) into a sequence of tickets that can be picked up one at a time, in an order
that answers the design's open questions before committing to its most expensive changes. The first
deliverable is a measured result, not a feature: whether costly changeovers and player-controlled
priority create real scheduling decisions on the vessel that already ships.

**Design:** `docs/production scheduling and automation - gameplay design.md`. Its status is *Draft —
exploratory*, and it says in so many words that it "does not supersede the GDD or authorise
implementation". This plan respects that: Phase 0 turns each of its §7 open decisions into a dated
spec, and no kernel ticket starts before the decision it depends on has one.

**Tracking:** parent issue [#44](https://github.com/Gatov/DimenshipGame/issues/44) references this
plan; D1–D4 are #45–#48, M1–M3 are #49–#51, K1 is #52 and K2 is #53. Tickets that can start or be decided now
are its sub-issues; tickets still gated on a decision are a checklist in the parent and get an issue
of their own when the gate closes, so the issue list never holds work nobody can pick up yet.

**Prior art it must not contradict:**
`docs/superpowers/specs/2026-09-04-conveyor-belt-design.md` (cargo on a belt is physically present
and always arrives; blocked means cargo it cannot put down),
`docs/superpowers/specs/2026-09-16-editable-production-plans-design.md` (a draft never reaches
`Commit`; approval flattens to an ordinary `ProductionPlan`),
`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md` (salvage reverses the
build schematic; never author a recycle recipe; fitted equipment never sits in Resource Storage), and
the GDD, which wins on vocabulary and currently routes ordinary components through Resource Storage.

## Why

The design asks for scheduling depth from a small vessel: two reactors, a few factories, and choices
between competing uses of the same machines, space and material. Most of what it proposes touches
the kernel's deepest contracts at once — the planner's routing, the storage model the GDD specifies,
and the save format — and its authors were careful to call it a direction to test rather than a
contract.

Building it in the order the design *describes* it (mechanics, then control, then situations) would
spend the large tickets first and learn whether they were worth it last. This plan inverts that:

1. **Decide** the four questions that change what the code would look like (§7.1–§7.4).
2. **Measure** today's vessel under plain queue order, with telemetry and a headless replay harness
   that every later ticket reuses for its before/after number.
3. **Build the cheapest slice that could show the effect** — setup identity and priority on the
   existing content — and measure again.
4. Only then take on local-only intermediates, location-aware planning and material claims, which
   are the large and risky tickets.

The code already provides more of the foundation than the design's §2 suggests, which is what makes
step 3 cheap:

- **Setup exists at recipe granularity.** `FacilityInstance.Configured` is a `SchematicId`,
  `switchOverTicks` is authored per archetype (30 on every shipped one), and
  `SimulationEngine.SelectAndStart` already prefers the current task, then any queued task on the
  configured schematic, then anything runnable. Priority lands in exactly that function.
- **Fixed runs exist** (`Produce.Runs`), and a finished run that cannot deposit already holds the
  facility without losing its inputs.
- **In-flight cargo is already a place.** The conveyor belt makes "priority must respect cargo
  already travelling" true by construction.

And some of it is genuinely absent, which is what makes step 4 expensive:

- Every shipped route passes through `resource_storage` except `factory_link_ab` and
  `factory_link_bc`. There is no factory↔reactor link, so a facility revisit is a content change
  as well as a planner change.
- There is no priority, objective, claim or controller anywhere in the kernel. `Programs/` holds only
  `Condition` and `ConditionEvaluator`, and `catalog/programs.json` must stay empty.
- Contention for stock and power is decided by executor visit order. The design names "hidden
  executor order decides success" as a reason to revisit the whole direction; the claims ledger
  (K6b) is the ticket that removes it.

## Decisions

1. **Decisions are specs, not tickets that quietly land in code.** Each Phase 0 ticket produces a
   dated file under `docs/superpowers/specs/` with a Goal, Source material and decisions with their
   reasoning, in the house format. The kernel ticket it gates cites that filename.
2. **Measure before mechanics.** M1–M3 depend on nothing in Phase 0 and should start immediately.
   Every later ticket states its effect as a harness result against the M3 baseline.
3. **The harness needs no Godot.** It loads a scenario through the ordinary content loader, applies
   scripted demand at given ticks, advances the engine and prints a metrics table. Scripted demand
   stands in for the mission system, as the design's §7 proposes. Whether it lives under `tests/`
   or as a small tool project is M2's first decision; a tool project makes the tables readable
   outside NUnit output.
4. **Telemetry is kernel state, integers only.** It is counted in the tick, so it is deterministic,
   saved like any other ledger, and survives `DeterminismSurvivesASave`. A figure derivable from the
   snapshot is derived on the snapshot instead of stored.
5. **One command surface for players and controllers (C0).** Set priority, hold, release, allocate,
   amend demand and recover are kernel commands with one entry point. `ShellActions` calls it and a
   future controller calls the same thing. Building the Operations controls before this would give
   the player commands a program can never have, which is the parity the design's §5 forbids.
6. **Reference controllers are C#, not programs.** The program language does not exist, and
   `programs.json` is required to be empty. E2's three policies are written against C0 in the
   harness, and are explicitly not a program runtime.
7. **The GDD is amended before any new item id lands.** Local-only intermediates contradict the
   GDD's central-storage model, and item ids in this repository come from the GDD. D2 includes the
   GDD edit; K3 and K4 do not start without it.
8. **Upgrades and specialisation are out of the first experiment.** The design lists them as
   proposed core, but its §7 experiment deliberately tests changeovers, restricted intermediates,
   revisits and competing demands without them, and they depend on sockets and refit from the
   recycling spec, none of which exists.

## Tickets

Sizes are S (a day or so), M (a few days), L (a week or more, likely split again when it opens).
**Issue now** marks a ticket opened as a sub-issue immediately; the rest open when their gate closes.

### Phase 0 — Decisions (docs only, parallel)

| # | Ticket | Resolves | Issue now |
|---|---|---|---|
| D1 | Setup identity and interruption boundary | Design §7.1 | Yes |
| D2 | Storage topology, direct routes, and the GDD amendment | Design §7.2 | Yes |
| D3 | Demand, objectives and material claims | Design §7.3 | Yes |
| D4 | Recovery of unwanted intermediates | Design §7.4 | Yes |

**D1 — Setup identity and interruption boundary.** Decide whether setup identity is the schematic
(today's `Configured`), a process family authored on schematics, or something else; and the safe
boundary at which a higher-priority task may displace the configured one — at a run start only, or
also by abandoning a switch-over already under way. Decide the final-remainder rule: whether a
remainder shorter than a full run consumes inputs and energy pro rata or rounds, in integers.
*Out:* a spec, and the list of engine behaviours in `SelectAndStart` that change. Gates K1, K2.
**Decided:** `docs/superpowers/specs/2026-09-24-setup-identity-and-interruption-boundary-design.md`.
Setup identity stays the schematic, and no process family is added, so K1 is a content rebalance
and a test. Priority ranks ahead of setup preference among ready tasks. A run, a paused run and a
held deposit are never displaced. A switch-over can be retargeted, cancelled or restarted by a
strictly higher priority. The final remainder rounds up to a whole run.

**D2 — Storage topology and routes.** Decide which intermediates are excluded from Resource Storage,
what each local buffer may accept, and which factory↔reactor connections exist on the shipped
vessel. Record the GDD amendment and keep the vocabulary distinct from the recycling spec's fitted
equipment and from the shipped `module` commodity. *Out:* a spec and a GDD edit. Gates K3, K4, K5b.
**Decided:** `docs/superpowers/specs/2026-09-24-storage-topology-and-direct-routes-design.md`, with
the GDD amended at v0.9.2. A new tier, the **workpiece**, is never held in Resource Storage, and no
shipped item joins it. A storage accepts a workpiece only if it is the buffer of a facility whose
type has a schematic consuming or producing it. That rule is derived from the catalog, and ordinary
items are unrestricted. The revisit chain is `plate_blank` → `hardened_blank` (workpieces) →
`bulkhead` (stored). Four built treatment lines join Factory Alpha to both reactors. K3's refusal
moves from the belt head to `Enqueue` and the draft (`WorkpieceNotAccepted`, structural), because
a destination that will never accept its cargo would freeze a belt for good. K3 is
behaviour-neutral on shipped content. K4 re-records the M3 baseline.

**D3 — Demand, objectives and claims.** Decide what an objective is at runtime, how its urgency
passes to prerequisites (promoting only a final assembly task is explicitly inadequate), how claims
on material are made, and how they change or release on hold, completion and cancel. Include the
deterministic tie-break and starvation rule. This is the largest decision and may split into
objectives-and-urgency (gating K6a) and claims (gating K6b). Gates K6a–c, K8.
**Decided:** `docs/superpowers/specs/2026-09-24-demand-objectives-and-material-claims-design.md`,
as one document with two halves that gate separately. The objective is the committed plan, which
gains a priority and a held flag. There is no new `Objective` entity, because the GDD uses that
word for story objectives. A plan's tasks read its priority live, and there is no per-task
override, so promotion reaches every prerequisite stage. Promotion never crosses plans and never
takes held stock. Coverage is today's `Uncommitted` arithmetic, and a held plan still covers, so
repeated scans never duplicate work. *(Amended 2026-10-09: the planner now reads only the main
hold, `InHold`. Coverage survives as a controller's reading, not the planner's; see M3.)* A claim stores only what is held. Need is derived from the
plan's tasks. Cargo keeps its owner on arrival, and free stock goes by priority, then plan age.
Power is granted by priority, then task age. Nothing ages automatically; starvation is visible
(`Outranked`, `MaterialClaimed`, a waiting-plan alert) and traces back to a command. Hold keeps
claims, cancel truncates to the work already started, and amend is cancel-and-replan under one id.

**D4 — Recovery.** Decide how unwanted intermediates are cleared: moved, dismantled or discarded.
Dismantling reuses the recycling spec's reverse-the-build-schematic rule rather than a new recipe;
discarding, if allowed, is journaled so no material disappears unaccounted. The vessel's basic
recovery path must survive. Gates K7.

### Phase 1 — Measure first (no gate)

| # | Ticket | Size | Issue now |
|---|---|---|---|
| M1 | Scheduling telemetry in the kernel | M | Yes |
| M2 | Headless replay harness with scripted demand | M | Yes |
| M3 | Baseline: plain queue order on the shipped vessel | S | Yes |

**M1 — Scheduling telemetry.** Split by who reads each figure, so the kernel carries nothing only
the experiment needs.

1. **Fill `UtilizationWindow`.** It is declared, seeded and saved today, and nothing fills it. Each
   tick a facility lands in exactly one category, mapped from `Status` and `BlockReason`, so the
   categories still sum to the measured window. A seventh category, **Held**, is appended for
   `ConditionNotMet` and `SafetyLock`: a facility gated by a condition or a lock is neither idle
   nor short of input, and folding it into either would misreport the cause the GDD requires a
   percentage to name. A save without the Held array loads with zeros, as the existing arrays
   already do. K2's *outranked* signal, when it lands, is a category decision of its own.
2. **Task lifecycle ticks.** `EnqueuedAtTick`, `FirstStartedAtTick` and `CompletedAtTick` on
   `TaskInstance`, nullable and saved. Start minus enqueue is queue wait, which an executor-level
   window cannot attribute to a task; a committed plan's last completion is its readiness.
3. **Derived, never stored:** buffer occupancy (`FillPermille`) and material committed to
   unfinished work (inputs held by running or awaiting-deposit runs, plus cargo on belts) are
   projected on the snapshot, per Decision 4.

Whole-run totals (changeover count and ticks, means and peaks) are the harness's to integrate,
not kernel counters. *Accept:* the window and the projections appear on the snapshot; the save
round-trip and `DeterminismSurvivesASave` still pass; a facility's categories sum to `Measured`.
**Built** (2026-10-09). `UtilizationWindow.CategoryOf` is the one status-to-category mapping, and
every built facility records into its window after its step. The ring advances on the ticks it
records, not on the clock, and clears the oldest bucket whole. Reactor windows stay empty because
nothing steps a reactor yet. `RouteUnsafe` files under waiting output, and `PrerequisiteMissing`
under Held. The snapshot carries `ExecutorState.Utilization` (ticks, never permille),
`TaskInstanceState`'s three lifecycle ticks, and `WorldSnapshot.InProcess` (per item: `InRuns`, the
inputs of every task whose run is active, held deposits included; `OnBelts`, belt cargo). Buffer
occupancy was already `StorageState.FillPermille`. Tests: `Simulation/TelemetryTests.cs`, plus a
`WorldSaveTests` case that loads a save without `held`.

**M2 — Replay harness.** A tool project, `tools/Dimenship.Replay` (console, `net8.0`, referencing
`Dimenship.Core` only), added to the solution and to CI's build-and-test step. It loads a content
root given as an argument, so one script can be run against content before and after a change.
It applies a JSON script of `(tick, demand)` events through the same path the shell uses:
`SimulationDriver.Draft` is a thin wrapper over `PlanDraftEditor.Create`, so the harness calls
`Create` → `Approve` → `SimulationEngine.Commit` and nothing diverges. It steps one tick at a time,
drains events through `RecentEvents` / `TotalEventsEmitted` (completions must be caught as they
happen, since the task registry retires into a bounded window), and integrates the design's §7
metrics:

| Metric | Definition |
|---|---|
| Readiness | Ticks from a demand's commit to its plan's last completion; a demand not ready by the end tick is reported so, never dropped. |
| Useful completions | Goal quantity delivered by the end tick, per demand. |
| Material tied up | Mean and peak of unfinished-work material, per item. |
| Space tied up | Mean and peak `FillPermille`, per storage. |
| Changeover cost | Count and ticks, per facility. |
| Interventions | Commands applied. Equal to the demand count under queue order; E2 makes it vary. |

The report is integers only (ratios in permille), in declaration order, formatted with the
invariant culture, with no wall-clock reading, and ends with a SHA-256 of the final save so state
divergence the metrics do not show is still caught. No policy hook yet: controllers need C0.
*Accept:* a test referencing the tool runs one script twice and gets byte-identical reports.
**Built** (2026-10-09). `tools/Dimenship.Replay`, with `tests/Dimenship.Replay.Tests` beside the
other suites and in CI. Usage is `dotnet run --project tools/Dimenship.Replay -- <content-root>
<script.json>`, and the report goes to stdout as Markdown, ready to commit under `docs/reviews/`.
A script names a scenario, an `endTick` and its demands, each with an optional `destination` and
an optional `assemble` that makes it a construction draft. Definitions the table above left open:

- *Delivered* is the goal less the plan's unplannable shortfall once the plan is ready, and zero
  before that.
- A plan that spawned no tasks is ready the tick it was committed.
- An approval refusal is reported with its issue count and is not counted as an intervention.
- *Material tied up* lists only items that were ever in process.

The report also gives a *Facility time* table, the whole-run integral of each facility's
utilization categories while built, and a check that its switching column agrees with the
changeover ticks. `scripts/smoke.json` is a fixture for the determinism test, not a baseline.

**M3 — Baseline.** Situations A and B from the design, approximated on the shipped chain (components,
modules, frames, construction units), run under plain queue order. The report is committed under
`docs/reviews/` as the reference every later ticket compares against.
**Built** (2026-10-09): `docs/reviews/2026-10-09-scheduling-baseline.md`, from
`tools/Dimenship.Replay/scripts/situation-a.json`, `situation-b.json` and `situation-b-alone.json`.
The first recording found the planner covering a new demand with stock held anywhere on the vessel
and with other plans' output. A second order for an item planned only a final haul and stalled
for good, which hid the work that changeovers and priority act on.

The project owner then set the planner's supply to what is in the main hold and nothing else
(`IWorldView.InHold`, replacing `Uncommitted`). A raw material nothing produces is still planned
around in full and reported as a `MaterialShortage` supply note. The baseline was re-recorded
under that rule:

- **A:** 10 of 12 demands ready, 27 changeovers costing 810 ticks.
- **B:** every demand ready, and the expedition frames are faster than alone, because the
  expansion builds Factory Gamma in time. B needs K4's revisit to carry its intended tension.

Expedition 4 in A stalls because two plans counted the same hold stock. That is the trade-off the
new rule accepts, and it belongs to K6b and the player, not to K1 or K2.

### Phase 2 — Core kernel mechanics

| # | Ticket | Depends on | Size | Issue now |
|---|---|---|---|---|
| K1 | Setup identity and changeover rebalance | D1 | S–M | Yes |
| K2 | Task and plan priority in selection | D1 | M | Yes |
| K3 | Local-only items | D2 | S | — |
| K4 | Revisit chain content and direct routes | D2, K3 | M | — |
| K5a | Stock by location in the world view | — | M | — |
| K5b | Location-aware planner | K5a, K3 | L | — |
| K6a | Priority, hold and membership on committed plans | D3, K2 | M | — |
| K6b | Material claims ledger | D3 | L | — |
| K6c | Hold, release, cancel and amend plans | K6a, K6b | M | — |
| K7 | Recovery commands | D4, K3 | M | — |
| K8 | Selection and waiting explanations | K2, K6b | M | — |

**K1 — Setup identity and rebalance.** D1 keeps setup identity as the schematic and rejects a
process family, so nothing in `SelectAndStart` or `StepProducer` changes and no loader rule is
added. K1 is a content rebalance and two tests. Shorter fixed runs (`effortPerRun`) and a
significantly larger `switchOverTicks` on the archetypes it selects (uniform or per archetype is
D1's open item). One test runs two *separate* tasks on one schematic back to back with no
`SwitchOverStarted`, because the existing single-task test does not prove it. The other runs
`separate_basic` → `synthesize_basic` on one reactor and expects a full switch-over, although both
produce `basic_metals`. *Accept:* M2 shows the changeover cost moving against the M3 baseline.
**Built** (2026-10-09): `docs/reviews/2026-10-09-k1-changeover-rebalance.md`, whose reports are
the new reference. Chosen from a sweep of eight variants: production-chain runs halved (16 ticks
to 8, every quantity and energy halved with them) and `switchOverTicks` 120 on every archetype,
uniform as D1 suggested. Under queue order:

- **Changeovers:** A's changeover ticks rise from 810 to 2,880 with almost the same count (27 to
  24). Queue order groups nothing, which is the headroom for a better policy.
- **Unfinished work:** peak material tied up roughly halves.
- **Situation B:** gains its tension, with the urgent frames 267 ticks slower than alone where
  they were 219 faster.
- **Construction:** slows, since each unit type is its own schematic.
- **A's completions:** fall from 10 to 8, because of the hold-stock race the M3 supply rule
  accepts. That is K6b's to fix, not K1's.

**K2 — Priority.** A priority on tasks (and plans, which lend it to their tasks) that ranks above
setup preference at D1's safe boundary, with a deterministic tie-break that is not executor
declaration order in disguise. A new status or postpone reason distinguishes *outranked* from
*physically blocked*, appended per the append-only enum rules. Priority is saved. *Accept:* an urgent
task displaces a continuously supplied one at the boundary and never mid-run; M2 reports the
difference against M3.
**Built** (2026-10-09): `docs/reviews/2026-10-09-k2-priority.md`.

- **What exists.** Tasks carry `Low`/`Normal`/`High`/`Critical` priority. Commands
  `SetPriority(TaskId)` and `SetPriority(PlanId)` exist; a plan lends its priority to every task it
  spawned. Selection, switch-over abandonment and transport loading follow D1 Decisions 2–4.
  `Outranked` is appended last. Priority is saved by name.
- **Neutral at default.** Every default report matches K1's in every metric. `Outranked` is
  recorded only for a strictly higher priority, not at equal priority as D3 suggests, because that
  would change the default journal.
- **The measured result.** Raising one demand never made it finish sooner in A or B, and always
  raised changeover cost. In A, up to 5,302 ticks against 2,880.
- **Why it buys nothing.** B's delay is the planner's line choice, not selection. A's stalls are
  the hold-stock race.
- **The ping-pong.** A trickle-fed urgent order makes its facility ping-pong, which D1's "no
  automatic flapping" did not cover. Factory Beta settles into one urgent run, 120 ticks of
  switching, one outranked run and 120 back, for about 6% useful time.
- **Decided: keep D1 unchanged.** There is no setup hysteresis. K6b and K6c's allocation is the
  cure, and priority is re-measured once they land.

**K3 — Local-only items.** D2's *workpiece* tier: a required `workpiece` boolean on every item
(every shipped item `false`), and the loader rules of D2 Decision 4. Acceptance is derived from the
catalog into an engine-constructor index, never saved: a storage accepts a workpiece only if it is
the buffer of a facility whose type has a schematic consuming or producing it. A misplaced workpiece
is refused **at `Enqueue` and in the draft** (`DraftIssueKind.WorkpieceNotAccepted`, structural,
appended last), never at the belt head, because a destination that will never accept its cargo
would freeze that belt for good. `Room` / `RoomForDelivery` answer 0 for it, and a save holding one
somewhere it is not accepted is reported as content drift. *Accept:* loader tests break exactly one
thing each, and the shipped vessel advances byte-identically, so the M3 baseline stays valid.

**Built** (2026-10-09, #69).

- **The flag.** `ItemDefinition.Workpiece`, required in `items.json`; every shipped item is false.
- **The rule.** `Content/WorkpieceAcceptance` is the derived rule. The loader builds it to check a
  scenario, and the engine builds it as an index that is never saved.
- **Enforcement.** `Enqueue` refuses with one sentence, and `Room` / `RoomForDelivery` answer 0.
  The draft marks `WorkpieceNotAccepted`, which is structural, on every move and on a goal that
  would leave one in the hold. A save gets a drift pass listing stock, belt cargo and transfer
  tasks.
- **Neutral.** All eight replay scripts reported byte-identically against the commit before,
  hashes included. A test pins that no shipped item is a workpiece and every storage accepts
  everything.
- **Not decided here.** The spec's open item, whether `Enqueue` should refuse producing a
  workpiece where no route leads to a consumer, is still open.

**K4 — Revisit chain.** D2's illustrative chain (form at a factory, treat at a reactor, finish at a
factory) as items, schematics and direct factory↔reactor routes in `default_vessel.json`, with
authored `lengthTicks` on each route.

**Built** (2026-10-10, #70): `docs/reviews/2026-10-10-k4-revisit-chain.md`.

- **Content.** `plate_blank` and `hardened_blank` are workpieces, and `bulkhead` is stored. The
  recipes are `form_blanks`, `harden_blanks` (twice a separation run's energy) and
  `assemble_bulkheads` (a hardened blank plus components). The four treatment lines run on a new
  `treatment_line` archetype, 13 a tick. `contentVersion` is `0.2.0`.
- **Saves.** A storage, facility or route the scenario has and a save lacks is drift, one error
  per node. A pre-K4 save names all four lines and is never given them silently.
- **Drawing.** The spec's placement worry was real: both treatment edges crossed Resource
  Storage's card. An elbow that would cross a card now moves to the nearest clear gutter, and
  every edge that was already clear keeps its midpoint.
- **Baseline.** All eight scripts report identical metrics, with only the version and hash
  changed, because no script orders a bulkhead and none is throttled.
- **Energy.** A fully built vessel running everything now exceeds capacity: 10,661 against
  10,000, where it had 139 to spare. The draw stays 200, by D2's own test.
- **Not done.** Nothing plans the chain yet: a bulkhead draft is refused with
  `WorkpieceNotAccepted`, and only tasks queued by hand run it. E1 needs a harness command to
  queue a task, or buffer-to-buffer planning.

**K5a — Stock by location.** `IWorldView` answers what is where: Resource Storage, each facility
buffer, cargo on each belt, and expected output of runs in progress. Pure reading, no behaviour
change; it can start before D2.
**Built** (2026-10-09, #63), not on `IWorldView`. `Presentation/StockLocations.For(snapshot, item)`
lists every storage (amount and held), belt and run in progress holding an item. `ExecutorState`
gains `RunOutput`, the one fact the snapshot lacked. The planner is deliberately not given it: its
supply stays the main hold's free stock, by the project owner's decision, so K5b's narrow form
needs nothing from here. U2's inspector is the reader.

**K5b — Location-aware planner.** The planner allocates stock already on hand in the right buffer
before ordering new production, and routes buffer to buffer over direct links rather than through
Resource Storage. The editable-draft invariants hold: `RequirementKey` identity, merge at flatten,
and an unedited draft committing byte-identically where the topology has not changed. Expected to
split further when it opens.
**Built narrow** (2026-10-09, #64): `docs/reviews/2026-10-09-k5b-route-aware-facility-choice.md`.
The project owner narrowed it to facility choice and kept the hold-only supply rule; allocating
buffer stock and buffer-to-buffer routing wait for K3 and K4.

- **Estimated finish.** A facility is chosen by queued ticks ahead, plus the longer of the stage's
  work and its slowest hold line, plus belt lengths. Unoccupied first and declaration order last,
  as before. `PlannerFacility.QueuedRuns` became `QueuedTicks`.
- **Measured.** B's frames are ready in 717 ticks against 1,069, and 542 at High. Priority now buys
  something in B, and the builds pay for it. Situation A's total readiness halves (9,915 to 4,981)
  for 7 more changeovers.

**Workpiece routing built** (2026-10-10, #71): `docs/reviews/2026-10-10-k5b-workpiece-routing.md`.
This is D2's K5b paragraph, and only that: allocating buffer stock is still out, by the hold-only
supply rule.

- **The rule.** A leg whose item the hold refuses goes buffer to buffer over a built line. Its
  producer delivers straight into the consumer's buffer, and no hold leg is emitted.
- **Eligibility.** A facility with no line to that buffer is skipped rather than ranked last. So is
  a consumer that no producer can reach. A workpiece leg is timed on its own line in estimated
  finish.
- **Neutral.** All eight existing scripts are byte-identical.
- **Measured.** `scripts/revisit.json` orders two batches of 500 bulkheads, ready in 761 and 614
  ticks. Reactor Alpha does all the treating, because it is free and its line is shorter. Reactor
  Beta treats when Alpha is occupied.

**K6a — Priority, hold and membership on committed plans.** The committed plan is the runtime
objective (D3 Decision 1). It gains a priority that its tasks read live and a held flag. Power is
granted by priority, then task age.
**Built** (2026-10-09, #58).

- **Plan owns priority.** `CommittedPlan.Priority` and `Held` exist. A plan task's effective
  priority is its plan's, through `PriorityOf`, and setting one on a plan task is refused, naming
  the plan.
- **Held.** A held plan's unstarted work postpones with `SafetyLock`; what is physically committed
  finishes.
- **Power.** It is granted to runs in progress by (priority, task id) before any facility steps. A
  run starting this tick draws on what is left, in visit order. This deviates from D3's literal
  three-pass order, which would break the enough-power identity D3 requires; D3 records why.
- **Save.** The save is now version 2, with an upgrader from version 1. A version 2 load reports a
  plan task carrying a priority, or a plan-less task missing one.
- **Empty plans.** D3's open item is decided: a plan with no tasks is still recorded.
- **Measured.** Every replay, default and priority variants alike, matches the K2 reference in
  every table. No situation ever starves for power, so power-by-priority is exercised by kernel
  tests only.

**K6b — Claims ledger.** A ledger in `WorldState` of material claimed by objective and location;
withdrawals respect claims. This is what removes executor visit order as the arbiter of contested
stock.
**Built** (2026-10-09, #59): `docs/reviews/2026-10-09-k6b-material-claims.md`, whose reports are
the new reference.

- **What exists.** D3 Decision 5 as specified: commit and arrival allocation, enforcement at every
  withdrawal, `Relinquish` and `Reassign`, and save version 3 with validation that reports rather
  than clamps.
- **The invariant** is asserted after every tick of a busy shipped run.
- **One decision beyond D3:** the planner's `InHold` now counts only *free* stock in the hold, so a
  new plan never counts another plan's holding.
- **Measured.** Situation A finishes 12 of 12 demands, up from 8, and changeover ticks fall from
  2,880 to 1,560. With expedition 3 raised, its frames are ready in 684 ticks against 1,062 at
  Normal, and the K2 ping-pong is gone: Factory Beta makes 1 changeover, down from 26.
- **Situation B is unchanged for the frames.** That delay is the planner's line choice, K5's
  ground.

**K6c — Hold, release, cancel and amend.** Holding a plan keeps its claims but takes no new
stock. Releasing resumes it. Cancel truncates the plan to the work already started and releases
its claims. Amend is cancel-and-replan under the same plan id (D3 Decisions 6–7). Active runs and
in-flight cargo are respected.
**Built** (2026-10-09, #60).

- **What exists.** `Hold`, `Release` and `Cancel` on a plan, or on a task queued by hand, and
  `Amend(plan, quantity)`, which returns the composer's `PlanApproval`. Save version 4 carries a
  hand-queued task's held flag.
- **Cancel is truncation.** Each task's script is cut back to the work already started, and the
  task finishes by the ordinary path. A switch-over toward a task cut to nothing completes first.
- **Amend under the hold-only rule** plans the new goal as a fresh order would, counting the
  plan's own holdings in the hold as supply. D3 carries a dated note.
- **Tested.** No hold or cancel interrupts a run or strands cargo. Cancel creates and destroys
  nothing. Release offers free stock at once. Amend keeps the plan's id, priority and held flag,
  and trims its holdings. The claim invariant holds on every tick of a shipped run that
  interleaves all four commands.
- **Not measured.** The replay harness has no command actions, so situation B's "hold unnecessary
  component work" cannot be scripted yet. That waits for C0.

**K7 — Recovery.** D4's recovery operations as kernel commands, never losing track of consumed
material, active runs or cargo.

**K8 — Explanations.** Extend `TaskAttempt` history so a task can say why it was selected and why
another waited, including "components claimed by objective X", which is the report the design's §5
asks for.
**Built** (2026-10-09, #62): `docs/reviews/2026-10-09-k8-explanations.md`.

- **What a postponement names.** `Outranked` names the task chosen instead. `MaterialClaimed`
  names the plan holding the stock. `DestinationFull` names the other plan whose stock holds the
  room. The cause is on the attempt, the snapshot, the journal event and the save.
- **In words.** `Presentation/WaitCause` turns a cause into a sentence. Operations shows it under
  a waiting task, and the replay's unfinished-work table gains a Behind column.
- **The first kernel alert.** `AlertCode.PlanWaiting` is raised after an operational hour without
  progress while a plan waits behind another. It names that plan and clears when the condition
  does, and nothing is corrected. Alerts reach the snapshot.
- **Neutral.** Every script's metric tables are unchanged; only the final hashes moved, because
  of the new saved fields and the event data.

### Phase 3 — One command surface, then the shell

| # | Ticket | Depends on | Issue now |
|---|---|---|---|
| C0 | Kernel command surface shared by shell and controllers | K2 | — |
| U1 | Operations: priority, hold/release, objectives and prerequisites | C0, K6a | — |
| U2 | Inspector: stock by location, claims, incoming cargo, expected output | K5a, K6b | — |
| U3 | Executor card: current setup, changeover countdown, waiting versus blocked | K1, K2 | — |

C0 opens as soon as K2 lands and grows as each later command appears; the U tickets follow the
existing shell rules (all commands through `ShellActions`, all colours through `ShellPalette`).

**C0 Built** (2026-10-09, #61): `docs/superpowers/specs/2026-10-09-kernel-command-surface-design.md`,
measured in `docs/reviews/2026-10-09-c0-command-surface.md`.

- **What exists.** `SimulationEngine.Execute(Command)` over fourteen command records, with refusals
  returned rather than thrown. The shell's APPROVE goes through it, and replay scripts carry
  `commands`.
- **Neutral.** Every earlier script reports byte-identically, the hash included.
- **Measured.** In a contested situation B, holding the upgrade buys the frames nothing, and costs
  two construction builds 1,398 ticks: the held plan's stock fills Factory Alpha's shared buffer.
  K8 should name that cause, and K5 remains the lever for the frames.
- **Not built.** Recovery (K7), reserves, pre-emptive setup, and the controller hook (E2).

**U1 Built** (2026-10-09, #66). The Operations plan detail gains priority, hold/release, cancel
(a second press confirms) and amend, each through `ShellActions.Execute`. The kernel decides what
is allowed and the detail shows its refusal as worded. The controls are built once and refreshed
per snapshot, so a quantity being typed survives the tick. The plan list shows a non-Normal priority
and HELD. Prerequisites are the plan's own tasks, which the detail already lists with their wait
cause (K8). Task-level controls for hand-queued tasks, and relinquish/reassign, are not in the
shell yet. Exercised in the real shell scene by a headless scripted run: approve, hold, High,
release, amend and a confirmed cancel each reached the kernel and showed on the plan list.

**U2 Built** (2026-10-09, #67). The Facility Inspector reads K5a and K6b. Display only.

- **Expected output.** A facility with a run in progress shows an OUTPUT row: what the run will
  deposit.
- **Claims.** Each item in a storage says how much of it is held, and HELD FOR PLANS lists each
  plan's holding there, marked when that plan is on hold.
- **Incoming.** Belt cargo bound for the storage, then runs that will deposit into it, from the new
  `StockLocations.BoundFor`. Queued work not yet started is left out, because it may still wait on
  input.
- **Stock by location.** A selected storage lists, per item, how much is elsewhere aboard, summed
  as stored, on belts and in runs rather than place by place.

Exercised on the shipped vessel by a headless scripted run of the shell: with a build plan
committed, Factory Alpha's buffer showed held metals, the plan's claim and the feed line's cargo,
and Resource Storage showed the extractor's run and the metals in transit.

**U3 Built** (2026-10-09, #68). `Presentation/ExecutorCondition.For` is one reading of a
production facility, shared by the executor card, the inspector and the status bar's alert count.

- **Waiting versus blocked.** A facility is blocked only by output it cannot put down, the rule a
  transport line already follows. Missing or claimed input is waiting, short energy is throttled,
  and a hold or a gate is held. The categories are `UtilizationWindow.CategoryOf`'s. Only blocked
  takes the fault colour and counts as an alert.
- **A task that cannot deposit outranks a held one.** The engine's root cause ranks a hold first,
  so a facility with one held task and one that cannot deposit reported the hold. The card now
  reports the full buffer, which is the open item from the K8 review.
- **Setup and changeover.** The card's detail line shows the setup, and `OLD → NEW` while a
  changeover loads. The status counts it down, and the run bar fills with it. The snapshot gains
  `SwitchingTo` and `SwitchOverTicksTotal`.
- The three shell copies of the postpone-reason wording are now one, `Conditions.Describe`, which
  is item 10 of #65.

Not changed: the construction phase still says BLOCKED for a unit whose production waits on
input. That word belongs to `ConstructionProgress`, and moving it is a separate decision.

### Phase 4 — The experiment

| # | Ticket | Depends on | Issue now |
|---|---|---|---|
| E1 | Situations A, B and one held-out situation as scripted fixtures | M2, K4 | — |
| E2 | Reference controllers: queue order, simple replenishment, improved | C0, E1 | — |
| E3 | Experiment report and go/no-go | E2 | — |

**E1 built** (2026-10-10, #72): `docs/reviews/2026-10-10-e1-situations.md`. Four scripts carry
the bulkhead chain, under `tools/Dimenship.Replay/scripts/`. All are recorded under queue order
as E2's baseline, and every demand is delivered.

- **A, `e1-a-sustained.json`.** Two reactors and three factories, and four expeditions of frames,
  modules and bulkheads, a quarter unit each, into the pad holds. Reactor Beta never works: Reactor
  Alpha gets everything, and pays 8 changeovers for 280 working ticks. Factory Alpha switches 21
  times.
- **B, `e1-b-urgent.json` and its control `e1-b-alone.json`.** An upgrade of modules and Technical
  Materials holds Factory Alpha and Reactor Alpha. The expedition's bulkheads take 1,621 ticks
  against 577 alone, and Factory Alpha's buffer fills.
- **C, `e1-c-held-out.json`, held out.** Recurring bulkhead repairs, a frames campaign and a
  factory built mid-campaign. Repairs wait up to 1,997 ticks, and Factory Beta arrives after the
  campaign it could have helped. E2 does not tune on it.

**E2 built** (2026-10-10, #73): `docs/reviews/2026-10-10-e2-reference-controllers.md`. The
harness takes a controller, `IController`, called once per tick. It acts only through `Execute`
and the composer's draft, approve and commit path, and both are counted as interventions. The
policy is the replay's third argument: `queue-order` (the default), `replenishment` or `improved`.
The report adds a policy line, the controller's orders and commands, and the stock at the end.
Queue order's hashes are E1's.

- **Simple replenishment** keeps a learned target of every demanded item. It reorders the hold's
  shortfall every five minutes without counting work in progress. It nearly halves A's readiness
  sum (2,558 against 4,841), but never delivers B's expedition and leaves six of C's seven repairs
  undelivered. Duplicate orders are the cause in both.
- **Improved** is tuned on A and B only.
  - What it does: departing demands go to High, and a restock covers twice the target, counting
    what is coming. Stock-building that shares a facility with higher-ranked work is held.
  - Results: A's readiness sum falls to 2,032 at queue order's changeover count, and B's
    expedition takes 497 ticks against 1,621.
  - On held-out C it gains 7%: every demand there departs, so priority separates nothing.
- **For E3:**
  - No static policy wins everywhere.
  - A plan's facility is fixed at commit, and no command moves it: Reactor Beta idles in A and
    Factory Beta in C.
  - No policy needed transfer handling.

E3 is judged against the design's own revisit conditions: one obvious static policy winning
everywhere, hidden executor order deciding success, or progress requiring repetitive transfer
handling. Its result decides whether the design moves from *exploratory* to a contract.

**E3 built** (2026-10-10, #74): `docs/reviews/2026-10-10-e3-experiment-report.md`. **Recommendation:
go**, for the core combination tested. None of the three revisit conditions is met. The project
owner accepted it, and the design's status line now reads as a contract for that combination, with
the candidate and deferred mechanics, upgrades included, still exploratory.

- **The fourth policy, manual scheduling.** A scripted demand may carry `assign`, a list of
  schematic and facility pairs. Before approval, each pair moves every step running that schematic,
  using the composer picker's `SetExecutor` edit, and each move counts as an intervention. There
  are two fixtures:
  - `e1-a-manual.json` moves A's hardening to Reactor Beta. Four decisions save 352 ticks and six
    changeovers.
  - `e1-b-manual.json` raises B's expedition to High. One decision readies it in 504 ticks, against
    497 for the improved controller.
- **Condition 1, not met.** Each situation is won by a different move, and the first policy a
  player writes fails two of three. Moving factory work in A trades changeovers against line length
  and loses. Manual and improved combined lose to improved alone.
- **Condition 2, not met.** Stock, power and selection no longer go by visit order, and the facility
  is shown and editable in the draft. Two gaps remain, both in control rather than visibility. The
  planner's estimate is blind to changeovers. No command moves committed work, which is the §5
  *assign eligible work* control after commit.
- **Condition 3, not met.** No run queued a transfer by hand.
- **Recommended next**, in the order the evidence supports them:
  1. A changeover-aware facility estimate.
  2. A command that reassigns committed work.
  3. Urgency on a demand.
  4. Assignments for controllers' orders.
  5. Recovery (D4, K7), which no run needed.

## First slice

**D1 → M1, M2, M3 → K1, K2**, on the shipped vessel with no storage change. If costly changeovers
and priority already produce decisions a better policy wins, that is evidence for the direction
before the GDD amendment and the two L tickets. If they do not, D2 and D3 are answered with that
result in hand rather than on faith.

## Not in scope

Deferred until E3 reports, per the design's own classification: upgrades and specialisation (they
need sockets and refit from the recycling spec), expensive substitutable material, direction-switching
transport, forecastable capacity changes, an additional bottleneck facility, reusable tooling, special
fuel, and the progression curve (§7.5–§7.6). No program runtime is built by any ticket here.

## Resolved items

- **Where the M2 harness lives:** a tool project, `tools/Dimenship.Replay`, so its tables read
  outside NUnit output; a test referencing it pins determinism.
- **Cumulative counters or bounded windows (M1):** both, by consumer. The kernel fills the bounded
  `UtilizationWindow`, which the player reads; the harness integrates whole-run totals tick by tick.
