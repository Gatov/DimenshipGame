# Scheduling Baseline (M3) — Plain Queue Order on the Shipped Vessel

Date: 2026-10-09. Ticket: M3 (#51) in
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`.

## Goal

This records the numbers every later scheduling ticket (K1, K2 and on) compares against. It runs
the design's situations A and B, approximated on the shipped vessel and chain (components,
modules, frames, construction units), under today's only policy: plain queue order.

**K1 has since changed the content these reports were recorded on.** The reference for K2 and
later is `docs/reviews/2026-10-09-k1-changeover-rebalance.md`. This document stays as the record of
the content before K1.

The first recording showed a planner defect that decided the outcome before scheduling could. The
project owner chose to fix it, and **the reports below were recorded after that fix.** The first
recording, the defect and the decision are kept at the end under *History*.

## How to reproduce

```bash
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/situation-a.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/situation-b.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/situation-b-alone.json
```

Each report at the end of this document is the tool's output, with only its headings demoted to
nest here. Run against the same content and the same kernel, a rerun must match it byte for byte
apart from those headings, final hash included. If a rerun differs, the behaviour changed, and the
change is the thing to explain.

## The situations as scripted

**A — sustained preparation** (`situation-a.json`, 9,000 ticks). The opening builds Reactor Beta
and Factories Beta and Gamma, so two reactors and three factories share the later work. From tick
1,200 on, four expeditions' equipment arrives on a fixed rhythm with no departure pressure: half
a unit of robot frames, then half a unit of modules, alternating between the two Launch Pad holds,
plus one component reserve in Resource Storage.

**B — urgent completion during expansion** (`situation-b.json`, 6,000 ticks). At tick 0 the
vessel commits an expansion: Factory Gamma, Reactor Beta and Launch Pad Alpha, plus four units of
components for the upgrade, set aside in Launch Pad Beta's hold. At tick 600, while that work
holds Factory Alpha, expedition frames are ordered for Launch Pad Alpha. `situation-b-alone.json`
orders the same frames at the same tick on a quiet vessel, as the control.

Two approximations the design's wording does not survive:

- **Equipment goes to a Launch Pad hold.** Nothing consumes it yet, since there are no missions.
  Left in Resource Storage, one expedition's frames would cover the next order without any work.
- **There is no treatment revisit.** The shipped vessel has no factory–reactor route and no
  workpiece (D2, K3, K4). B's "plates waiting for reactor treatment" is therefore represented
  only by contention for the factories.

## Results

| Situation | Demands | Ready by the end | Changeovers | Changeover ticks |
|---|---:|---:|---:|---:|
| A | 12 | 10 | 27 | 810 |
| B | 5 | 5 | 7 | 210 |
| B alone | 1 | 1 | 3 | 90 |

### A — sustained preparation

| Demand | Committed | Readiness (ticks) |
|---|---:|---:|
| Reactor Beta, Factory Beta, Factory Gamma | 0 | 91 / 136 / 157 |
| expedition 1 frames / modules | 1,200 / 1,500 | 1,934 / 1,159 |
| expedition 2 frames / modules | 2,400 / 2,700 | 737 / 241 |
| component reserve | 3,000 | 559 |
| expedition 3 frames / modules | 3,600 / 3,900 | 235 / 186 |
| expedition 4 frames / modules | 4,800 / 5,100 | not ready / not ready |

Changeovers are now the cost the design means them to be: 27 of them, 810 ticks, spread evenly
across the three factories (Alpha 9, Beta 8, Gamma 9) and one on Reactor Alpha. Expedition 1's
frames take 1,934 ticks, against 624 for the same order on a quiet vessel, because they share
three factories with the modules ordered 300 ticks later and every factory keeps switching
between pressing, module assembly and frame assembly.

**Expedition 4 never finishes, and that is the cost of the new supply rule, not a defect.** Its
frames and modules were planned against technical materials and components that were in the hold
at the time. Each plan counted the same stock, nobody ordered more, and under queue order the
plans that lost the race wait on their input for good: the frames at 6 of 10 module runs, the
modules at 0 of 1,000 components hauled. This is the double-spend the planner now leaves to the
player (see *History*). It is what K6b's claims and a controller's sequencing are for, and it
holds Factories Alpha and Beta waiting on input for 5,608 and 5,531 ticks.

Unfinished work peaks at 950 milli-units of basic metals, 428 of components and 304 of
technical materials. The Launch Pad holds fill to 999 and 866 permille with delivered equipment.
Reactor Beta is built and never runs (8,910 idle ticks): Reactor Alpha's 224 working ticks are all
the reactor work this chain asks for.

### B — urgent completion during expansion

Every demand finishes. The expedition frames are ready **405** ticks after commit, against
**624** alone. That is faster under the expansion, not slower. Factory Gamma, which the expansion
builds by tick 410, is online in time to take frame work off Factory Alpha, so the expansion adds
capacity before the frames need it. On this chain B shows no opportunity cost for queue order to
charge. Its intended tension, a reactor torn between material for the upgrade and treatment for
the expedition, needs K4's revisit chain. E1's fixtures are where B gets its real form.

### What K1 and K2 should move

- **K1 (changeover rebalance):** A's 27 changeovers and 810 ticks, and the readiness of
  expeditions 1–3, which all paid for switching.
- **K2 (priority):** with three factories contended in A, an urgent order can now displace
  continuously supplied work. B as scripted gives priority nothing to win, and should be reshaped
  before K2 reports against it.
- **Neither K1 nor K2 should move expedition 4.** It is an allocation failure, not a scheduling
  one. A K1 or K2 result that "fixes" it is changing something else.

## History: the first recording and the supply decision

The first recording used the planner's former supply reading, `SimulationEngine.Uncommitted`.
That reading started from the stock in every storage on the vessel, Launch Pad holds included, and
added the expected output of every unfinished production task. Nothing subtracted what another
plan's transfers would carry away. A second order for an item already made, or already being
made, was planned as a single haul out of Resource Storage, and that haul waited forever. Under it:

- **A:** 6 of 12 demands finished. Every expedition order after the first planned only the final
  haul, because it read the previous expedition's equipment in a Launch Pad hold as its own.
  Factory Gamma had to be ordered at tick 300. Ordered at tick 0, it read Factory Beta's unit as
  supply and was never built, a stall a player could reach from the composer by queuing two
  builds.
- **B:** the expedition frames never finished. Their plan read the upgrade's components in Launch
  Pad Beta's hold as its own supply and ordered no pressing.
- **Changeovers:** 7 changeovers (210 ticks) in A, because the stalled plans never ordered the
  production that changeovers act on.

**Decision (project owner, 2026-10-09):** the planner's supply is what is in the main hold, and
nothing else. Not output heading to the hold, not stock in a facility buffer or Launch Pad hold,
and not cargo on a belt. This is the point of optimization the game hands to the player. A plan
moves material only out of the hold. When an input is not there, the plan orders its production.
When a raw material nothing aboard produces is absent, the work that needs it is still planned in
full, and the player acquires the material while that work runs.

The decision is built as `IWorldView.InHold`, which replaces `Uncommitted`. An absent raw material
is reported as `DraftIssueKind.MaterialShortage`, a supply note that never blocks approval. Two
plans ordered back to back now each order their own production, and two plans that read the same
hold stock may both count on it. A's expedition 4 above is that second case.
`2026-09-24-demand-objectives-and-material-claims-design.md` Decision 3 is amended to match.

---

## Report: situation A

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 9000
- Interventions (commands applied): 12

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 91 | 91 | 1000 | 0 |
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 136 | 136 | 1000 | 0 |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 157 | 157 | 1000 | 0 |
| expedition_1_frames | robot_frame | 500 | 1200 | 1200 | 3134 | 1934 | 500 | 0 |
| expedition_1_modules | module | 500 | 1500 | 1500 | 2659 | 1159 | 500 | 0 |
| expedition_2_frames | robot_frame | 500 | 2400 | 2400 | 3137 | 737 | 500 | 0 |
| expedition_2_modules | module | 500 | 2700 | 2700 | 2941 | 241 | 500 | 0 |
| component_reserve | component | 2000 | 3000 | 3000 | 3559 | 559 | 2000 | 0 |
| expedition_3_frames | robot_frame | 500 | 3600 | 3600 | 3835 | 235 | 500 | 0 |
| expedition_3_modules | module | 500 | 3900 | 3900 | 4086 | 186 | 500 | 0 |
| expedition_4_frames | robot_frame | 500 | 4800 | 4800 | not ready | — | 0 | 0 |
| expedition_4_modules | module | 500 | 5100 | 5100 | not ready | — | 0 | 0 |

#### Unfinished work

Every task of a not-ready demand still open at the end, as the engine last described it.

| Demand | Task | Executor | Work | State | Reason |
| :--- | ---: | :--- | :--- | :--- | :--- |
| expedition_4_frames | 92 | factory_b_feed_components | technical_materials resource_storage → factory_b_buffer 600/1000 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 93 | factory_b_return | module factory_b_buffer → resource_storage 600/1000 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 94 | factory_a_feed | module resource_storage → factory_a_buffer 600/1000 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 95 | factory_a_return | robot_frame factory_a_buffer → resource_storage 300/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 98 | factory_b | assemble_modules 6/10 runs | Postponed | InsufficientInputMaterial |
| expedition_4_frames | 99 | factory_a | assemble_frames 6/10 runs | Postponed | InsufficientInputMaterial |
| expedition_4_frames | 100 | dock_b_supply | robot_frame resource_storage → dock_b_hold 300/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_modules | 102 | factory_c_return | component factory_c_buffer → resource_storage 0/1000 | Postponed | InsufficientSourceMaterial |
| expedition_4_modules | 103 | factory_c_feed_modules | component resource_storage → factory_c_buffer 0/1000 | Postponed | InsufficientSourceMaterial |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 105 | 4520 |
| hydrogen | 7 | 10 |
| basic_metals | 49 | 950 |
| technical_materials | 12 | 304 |
| component | 25 | 428 |
| module | 9 | 224 |
| robot_frame | 1 | 50 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 767 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 117 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 11 | 600 |
| factory_b_buffer | 104 | 493 |
| factory_c_buffer | 48 | 475 |
| dock_a_hold | 671 | 999 |
| dock_b_hold | 489 | 866 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 30 |
| reactor_b | 0 | 0 |
| factory_a | 9 | 270 |
| factory_b | 8 | 240 |
| factory_c | 9 | 270 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 224 | 8644 | 102 | 0 | 0 | 30 | 0 |
| reactor_b | 0 | 8910 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 784 | 2337 | 5608 | 1 | 0 | 270 | 0 |
| factory_b | 656 | 2438 | 5531 | 0 | 0 | 240 | 0 |
| factory_c | 1040 | 7287 | 247 | 0 | 0 | 270 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `5cfb826d2aaccb93270cf1f439cb494f68df58559022c19390c800bbb4985cb2`

## Report: situation B

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 6000
- Interventions (commands applied): 5

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 410 | 410 | 1000 | 0 |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 458 | 458 | 1000 | 0 |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 503 | 503 | 1000 | 0 |
| upgrade_components | component | 4000 | 0 | 0 | 359 | 359 | 4000 | 0 |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1005 | 405 | 500 | 0 |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 34 | 4040 |
| hydrogen | 7 | 10 |
| basic_metals | 39 | 478 |
| technical_materials | 3 | 254 |
| component | 11 | 400 |
| module | 3 | 124 |
| robot_frame | 0 | 50 |
| mission_dock_construction_unit | 1 | 300 |
| matter_reactor_construction_unit | 1 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 778 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 0 | 117 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 10 | 564 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 8 | 475 |
| dock_a_hold | 282 | 475 |
| dock_b_hold | 774 | 800 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 30 |
| reactor_b | 0 | 0 |
| factory_a | 5 | 150 |
| factory_b | 0 | 0 |
| factory_c | 1 | 30 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 48 | 5905 | 17 | 0 | 0 | 30 | 0 |
| reactor_b | 0 | 5543 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 688 | 5125 | 36 | 1 | 0 | 150 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 160 | 5322 | 79 | 0 | 0 | 30 | 0 |
| dock_a | 0 | 5498 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `ce08781788ded281714a7eb425fd707267008e209f44ae7ca9a399eadfd6eab6`

## Report: situation B alone

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 6000
- Interventions (commands applied): 1

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1224 | 624 | 500 | 0 |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 34 | 4040 |
| hydrogen | 7 | 10 |
| basic_metals | 12 | 478 |
| technical_materials | 3 | 178 |
| component | 7 | 228 |
| module | 3 | 228 |
| robot_frame | 0 | 50 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 796 | 803 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 0 | 117 |
| reactor_b_buffer | 0 | 0 |
| factory_a_buffer | 21 | 514 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 0 | 0 |
| dock_a_hold | 269 | 333 |
| dock_b_hold | 0 | 0 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 30 |
| reactor_b | 0 | 0 |
| factory_a | 2 | 60 |
| factory_b | 0 | 0 |
| factory_c | 0 | 0 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 48 | 5905 | 17 | 0 | 0 | 30 | 0 |
| reactor_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 480 | 5383 | 77 | 0 | 0 | 60 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `0ee0389dfbf3ad14c8294c540f5f1733b77daa20f45442af4f3c46a70537f04a`
