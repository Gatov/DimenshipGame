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

**K3 — Local-only items.** D2's *workpiece* tier: a required `workpiece` boolean on every item
(every shipped item `false`), and the loader rules of D2 Decision 4. Acceptance is derived from the
catalog into an engine-constructor index, never saved: a storage accepts a workpiece only if it is
the buffer of a facility whose type has a schematic consuming or producing it. A misplaced workpiece
is refused **at `Enqueue` and in the draft** (`DraftIssueKind.WorkpieceNotAccepted`, structural,
appended last), never at the belt head, because a destination that will never accept its cargo
would freeze that belt for good. `Room` / `RoomForDelivery` answer 0 for it, and a save holding one
somewhere it is not accepted is reported as content drift. *Accept:* loader tests break exactly one
thing each, and the shipped vessel advances byte-identically, so the M3 baseline stays valid.

**K4 — Revisit chain.** D2's illustrative chain (form at a factory, treat at a reactor, finish at a
factory) as items, schematics and direct factory↔reactor routes in `default_vessel.json`, with
authored `lengthTicks` on each route.

**K5a — Stock by location.** `IWorldView` answers what is where: Resource Storage, each facility
buffer, cargo on each belt, and expected output of runs in progress. Pure reading, no behaviour
change; it can start before D2.

**K5b — Location-aware planner.** The planner allocates stock already on hand in the right buffer
before ordering new production, and routes buffer to buffer over direct links rather than through
Resource Storage. The editable-draft invariants hold: `RequirementKey` identity, merge at flatten,
and an unedited draft committing byte-identically where the topology has not changed. Expected to
split further when it opens.

**K6a — Priority, hold and membership on committed plans.** The committed plan is the runtime
objective (D3 Decision 1). It gains a priority that its tasks read live and a held flag. Power is
granted by priority, then task age.

**K6b — Claims ledger.** A ledger in `WorldState` of material claimed by objective and location;
withdrawals respect claims. This is what removes executor visit order as the arbiter of contested
stock.

**K6c — Hold, release, cancel and amend.** Holding a plan keeps its claims but takes no new
stock. Releasing resumes it. Cancel truncates the plan to the work already started and releases
its claims. Amend is cancel-and-replan under the same plan id (D3 Decisions 6–7). Active runs and
in-flight cargo are respected.

**K7 — Recovery.** D4's recovery operations as kernel commands, never losing track of consumed
material, active runs or cargo.

**K8 — Explanations.** Extend `TaskAttempt` history so a task can say why it was selected and why
another waited, including "components claimed by objective X", which is the report the design's §5
asks for.

### Phase 3 — One command surface, then the shell

| # | Ticket | Depends on | Issue now |
|---|---|---|---|
| C0 | Kernel command surface shared by shell and controllers | K2 | — |
| U1 | Operations: priority, hold/release, objectives and prerequisites | C0, K6a | — |
| U2 | Inspector: stock by location, claims, incoming cargo, expected output | K5a, K6b | — |
| U3 | Executor card: current setup, changeover countdown, waiting versus blocked | K1, K2 | — |

C0 opens as soon as K2 lands and grows as each later command appears; the U tickets follow the
existing shell rules (all commands through `ShellActions`, all colours through `ShellPalette`).

### Phase 4 — The experiment

| # | Ticket | Depends on | Issue now |
|---|---|---|---|
| E1 | Situations A, B and one held-out situation as scripted fixtures | M2, K4 | — |
| E2 | Reference controllers: queue order, simple replenishment, improved | C0, E1 | — |
| E3 | Experiment report and go/no-go | E2 | — |

E3 is judged against the design's own revisit conditions: one obvious static policy winning
everywhere, hidden executor order deciding success, or progress requiring repetitive transfer
handling. Its result decides whether the design moves from *exploratory* to a contract.

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
