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

**Tracking:** one parent GitHub issue references this plan. Tickets that can start or be decided now
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

**D2 — Storage topology and routes.** Decide which intermediates are excluded from Resource Storage,
what each local buffer may accept, and which factory↔reactor connections exist on the shipped
vessel. Record the GDD amendment and keep the vocabulary distinct from the recycling spec's fitted
equipment and from the shipped `module` commodity. *Out:* a spec and a GDD edit. Gates K3, K4, K5b.

**D3 — Demand, objectives and claims.** Decide what an objective is at runtime, how its urgency
passes to prerequisites (promoting only a final assembly task is explicitly inadequate), how claims
on material are made, and how they change or release on hold, completion and cancel. Include the
deterministic tie-break and starvation rule. This is the largest decision and may split into
objectives-and-urgency (gating K6a) and claims (gating K6b). Gates K6a–c, K8.

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

**M1 — Scheduling telemetry.** Per executor, counted in the tick: changeover ticks and changeover
count, ticks waiting in queue versus ticks physically unable to run, buffer occupancy, and material
committed to unfinished work (inputs consumed by active or awaiting-deposit runs, plus cargo on
belts). Save DTOs and `WorldSave` mapping follow the save-format rules. *Accept:* counters appear on
the snapshot, and the save round-trip and determinism tests still pass.

**M2 — Replay harness.** Load a scenario, apply a scripted list of `(tick, demand)` events through
the same commit path the shell uses, advance, and report the design's §7 comparison metrics:
readiness time, useful completions, material and space tied up, changeover cost, and manual
interventions. Runs with a plain .NET SDK. *Accept:* two runs of one script produce byte-identical
reports.

**M3 — Baseline.** Situations A and B from the design, approximated on the shipped chain (components,
modules, frames, construction units), run under plain queue order. The report is committed under
`docs/reviews/` as the reference every later ticket compares against.

### Phase 2 — Core kernel mechanics

| # | Ticket | Depends on | Size | Issue now |
|---|---|---|---|---|
| K1 | Setup identity and changeover rebalance | D1 | S–M | Yes |
| K2 | Task and plan priority in selection | D1 | M | Yes |
| K3 | Local-only items | D2 | S | — |
| K4 | Revisit chain content and direct routes | D2, K3 | M | — |
| K5a | Stock by location in the world view | — | M | — |
| K5b | Location-aware planner | K5a, K3 | L | — |
| K6a | Objectives as a runtime entity | D3, K2 | M | — |
| K6b | Material claims ledger | D3 | L | — |
| K6c | Hold and release work | K6a, K6b | M | — |
| K7 | Recovery commands | D4, K3 | M | — |
| K8 | Selection and waiting explanations | K2, K6b | M | — |

**K1 — Setup identity and rebalance.** Implement D1's identity (for instance a `setupFamily` on
schematics, validated by the loader) and rebalance shipped content to short fixed runs with a
significantly more expensive changeover. Content edits only where D1 allows. *Accept:* M2 shows the
changeover cost moving; loader tests break exactly one thing each.

**K2 — Priority.** A priority on tasks (and plans, which lend it to their tasks) that ranks above
setup preference at D1's safe boundary, with a deterministic tie-break that is not executor
declaration order in disguise. A new status or postpone reason distinguishes *outranked* from
*physically blocked*, appended per the append-only enum rules. Priority is saved. *Accept:* an urgent
task displaces a continuously supplied one at the boundary and never mid-run; M2 reports the
difference against M3.

**K3 — Local-only items.** A catalog flag marking an item as never held in Resource Storage, loader
validation for it, and Resource Storage refusing a deposit of one with a reported reason.

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

**K6a — Objectives.** A runtime objective that tasks carry an id of, with priority passed down to
prerequisites per D3.

**K6b — Claims ledger.** A ledger in `WorldState` of material claimed by objective and location;
withdrawals respect claims. This is what removes executor visit order as the arbiter of contested
stock.

**K6c — Hold and release.** Holding work releases or keeps its claims per D3; releasing resumes it.
Active runs and in-flight cargo are respected.

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

## Open items

- Whether D3 is one decision or two (objectives and urgency; claims). Decide when D3 opens.
- Where the M2 harness lives: under `tests/`, or as a tool project.
- Whether telemetry windows (M1) are cumulative counters or bounded windows like `UtilizationWindow`.
