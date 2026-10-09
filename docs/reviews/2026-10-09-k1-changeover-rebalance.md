# K1 — Changeover Rebalance, Measured Against the M3 Baseline

Date: 2026-10-09. Ticket: K1 (#52) in
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`. Decision source:
`docs/superpowers/specs/2026-09-24-setup-identity-and-interruption-boundary-design.md` (D1).

## Goal

Make changeovers expensive enough to be a scheduling decision, on short fixed runs, and measure
what that does under plain queue order against the M3 baseline
(`docs/reviews/2026-10-09-scheduling-baseline.md`). **The reports at the end of this document are
the new reference** for K2 and later tickets; the M3 reports describe the content before K1.

## What changed

D1 Decision 1 keeps setup identity as the schematic, with no process family, so K1 changes no
code. It is a content rebalance and two tests.

**Content.**

- **Production-chain runs are halved, from 16 ticks to 8.** This covers the six chain schematics:
  `separate_basic`, `separate_technical`, `press_components`, `assemble_modules` and
  `assemble_frames` go from 16 to 8 ticks, and `synthesize_basic` from 32 to 16. Every input,
  output and energy figure halves with the effort. Every ratio a schematic authors is therefore
  unchanged, and so is every per-tick rate the transport lines are sized to: a reactor still
  consumes 250 matter mix a tick.
- **Construction-unit schematics are unchanged.** Each is exactly one run per unit (D1 Decision
  5).
- **`switchOverTicks` goes from 30 to 120 on every facility archetype.** That is fifteen of the new
  runs, up from about two of the old ones. It is uniform, settling D1's open item: uniform was the
  cheaper first measurement, and nothing here has asked for per-archetype values yet.

**Tests** (`Production/SetupIdentityTests.cs`), both behaviours D1 says already hold, now pinned:

- **Two separate orders on one schematic** run back to back with no `SwitchOverStarted`. The
  existing single-order test could not tell schematic-keyed setup from order-keyed setup.
- **Two schematics with one output item** cost a full switch-over. A third test pins that the
  shipped reactor really does have two processes, `separate_basic` and `synthesize_basic`, for
  basic metals.

No existing test pinned the old figures; all 327 kernel tests pass on the new content.

## Choosing the numbers

Eight variants were run on copies of the shipped content: runs kept (`f`) or halved (`h`), crossed
with `switchOverTicks` of 30, 60, 120 and 240. `f30` reproduces the M3 reports byte for byte,
which shows the sweep was measuring the same thing.

| Variant | A ready | A changeovers / ticks | B frames readiness | B alone | B cost of queue order |
|---|---:|---:|---:|---:|---:|
| f30 (M3) | 10 / 12 | 27 / 810 | 405 | 624 | −219 |
| h30 | 6 / 12 | 27 / 810 | 397 | 622 | −225 |
| f60 | 11 / 12 | 21 / 1,260 | 465 | 684 | −219 |
| h60 | 7 / 12 | 27 / 1,620 | 457 | 682 | −225 |
| f120 | 11 / 12 | 23 / 2,760 | 1,098 | 804 | +294 |
| **h120 (chosen)** | **8 / 12** | **24 / 2,880** | **1,069** | **802** | **+267** |
| f240 | 12 / 12 | 24 / 5,760 | 1,714 | 1,088 | +626 |
| h240 | 10 / 12 | 33 / 7,920 | 1,706 | 1,070 | +636 |

**h120 is chosen** for three reasons:

- D1 asks for shorter fixed runs and a significantly larger changeover, and h120 does both. A
  changeover costs fifteen runs.
- It is the smallest changeover at which situation B shows queue order's cost. The urgent frames
  are 267 ticks slower under the expansion than alone, where up to 60 ticks of changeover they
  were faster.
- 240 makes a changeover cost more than most demands in these scripts take to make, which would
  measure little but the changeover.

The *A ready* column swings with run length for a reason K1 does not touch (see below). It was not
used to choose.

## What K1 moved, under queue order

### Changeovers became the cost the design means them to be

| Situation | Changeovers, M3 → K1 | Changeover ticks, M3 → K1 |
|---|---:|---:|
| A | 27 → 24 | 810 → 2,880 |
| B | 7 → 7 | 210 → 840 |
| B alone | 3 → 3 | 90 → 360 |

The count barely moves while the price quadruples. Queue order does not group work, so it pays
whatever a changeover costs. That is the headroom a better policy, grouping compatible orders, is
supposed to win (design §6 A), and it is now about a third of each factory's time in A: Factory
Alpha spends 1,080 ticks switching, against 656 working.

### Unfinished work shrank

Shorter runs hold fewer inputs at once. Peak material tied up in A:

| Item | M3 peak | K1 peak |
|---|---:|---:|
| matter mix | 4,520 | 2,520 |
| basic metals | 950 | 400 |
| components | 428 | 274 |

Means roughly halve as well. Factory buffers sit fuller on average: Factory Alpha's mean fill goes
from 11 to 102 permille and Factory Beta's from 104 to 198. Inputs wait in the buffer through
longer changeovers.

### Situation B now carries its tension

| | M3 | K1 |
|---|---:|---:|
| Expedition frames, under the expansion | 405 | 1,069 |
| Expedition frames, alone | 624 | 802 |
| Cost of queue order | −219 | +267 |

Before K1, Factory Gamma came online in time to make the expansion a net gain for the frames.
With 120-tick changeovers, the frames now queue behind the expansion's work on Factory Alpha and
pay for it. This is the opportunity cost K2's priority is meant to buy back, so B is usable for
K2 as scripted.

### Building got slower

Each construction unit is its own schematic, so one factory building three different units pays
two changeovers between them. In A, Reactor Beta, Factory Beta and Factory Gamma are ready at 181,
316 and 337 ticks, up from 91, 136 and 157. In B, the three builds are 82 to 262 ticks later.
That follows from D1's rule rather than being a defect, but it is a player-facing change: the
opening expansion now costs a few minutes of switching. Should that prove too harsh in play, D1's
deferred process family is the remedy it names, sharing one setup across the construction
recipes.

### Completions in A fell, and that is not K1's

A finishes 8 of 12 demands, down from 10. Every stalled plan is waiting on stock another plan took
from the hold. Expedition 1's frames, for instance, sit at 750 of 1,000 modules hauled and 12 of 20
frame runs, waiting on modules that other plans' hauls took out of the hold first. That is the race the M3 supply
decision accepted: the planner reads only the hold, and two plans may count the same stock. Halved
runs leave less rounding surplus lying in the hold to paper over it, which is why the h variants
stall more than the f variants at every changeover length. The fix belongs to K6b's claims, or to a
player or controller sequencing orders, not to scheduling.

**What this means for K2:** compare readiness and changeover ticks only on demands that finish in
both runs, and treat a change in the A completion count as allocation noise unless the unfinished
table shows a scheduling cause.

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
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 181 | 181 | 1000 | 0 |
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 316 | 316 | 1000 | 0 |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 337 | 337 | 1000 | 0 |
| expedition_1_frames | robot_frame | 500 | 1200 | 1200 | not ready | — | 0 | 0 |
| expedition_1_modules | module | 500 | 1500 | 1500 | 2659 | 1159 | 500 | 0 |
| expedition_2_frames | robot_frame | 500 | 2400 | 2400 | 5522 | 3122 | 500 | 0 |
| expedition_2_modules | module | 500 | 2700 | 2700 | 3664 | 964 | 500 | 0 |
| component_reserve | component | 2000 | 3000 | 3000 | 3290 | 290 | 2000 | 0 |
| expedition_3_frames | robot_frame | 500 | 3600 | 3600 | not ready | — | 0 | 0 |
| expedition_3_modules | module | 500 | 3900 | 3900 | 5323 | 1423 | 500 | 0 |
| expedition_4_frames | robot_frame | 500 | 4800 | 4800 | not ready | — | 0 | 0 |
| expedition_4_modules | module | 500 | 5100 | 5100 | not ready | — | 0 | 0 |

#### Unfinished work

Every task of a not-ready demand still open at the end, as the engine last described it.

| Demand | Task | Executor | Work | State | Reason |
| :--- | ---: | :--- | :--- | :--- | :--- |
| expedition_1_frames | 22 | factory_a_feed | module resource_storage → factory_a_buffer 750/1000 | Postponed | InsufficientSourceMaterial |
| expedition_1_frames | 23 | factory_a_return | robot_frame factory_a_buffer → resource_storage 300/500 | Postponed | InsufficientSourceMaterial |
| expedition_1_frames | 27 | factory_a | assemble_frames 12/20 runs | Postponed | InsufficientInputMaterial |
| expedition_3_frames | 68 | factory_b_feed | technical_materials resource_storage → factory_b_buffer 749/1000 | Postponed | InsufficientSourceMaterial |
| expedition_3_frames | 69 | factory_b_return | module factory_b_buffer → resource_storage 950/1000 | Postponed | InsufficientSourceMaterial |
| expedition_3_frames | 73 | factory_b | assemble_modules 19/20 runs | Postponed | InsufficientInputMaterial |
| expedition_4_frames | 89 | factory_a_feed | technical_materials resource_storage → factory_a_buffer 747/1000 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 90 | factory_a_return | module factory_a_buffer → resource_storage 850/1000 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 95 | factory_a | assemble_modules 14/20 runs | Postponed | InsufficientInputMaterial |
| expedition_4_frames | 97 | dock_b_supply | robot_frame resource_storage → dock_b_hold 300/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_modules | 103 | factory_b_feed_components | technical_materials resource_storage → factory_b_buffer 204/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_modules | 104 | factory_b_return | module factory_b_buffer → resource_storage 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_modules | 107 | factory_b | assemble_modules 0/10 runs | Postponed | InsufficientInputMaterial |
| expedition_4_modules | 108 | dock_b_supply | module resource_storage → dock_b_hold 50/500 | Postponed | InsufficientSourceMaterial |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 52 | 2520 |
| hydrogen | 7 | 10 |
| basic_metals | 28 | 400 |
| technical_materials | 6 | 250 |
| component | 14 | 274 |
| module | 6 | 200 |
| robot_frame | 1 | 27 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 768 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 102 | 600 |
| factory_b_buffer | 198 | 478 |
| factory_c_buffer | 58 | 475 |
| dock_a_hold | 627 | 999 |
| dock_b_hold | 369 | 716 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 120 |
| reactor_b | 0 | 0 |
| factory_a | 9 | 1080 |
| factory_b | 6 | 720 |
| factory_c | 8 | 960 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 208 | 8618 | 54 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 8820 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 656 | 893 | 6370 | 1 | 0 | 1080 | 0 |
| factory_b | 712 | 885 | 6368 | 0 | 0 | 720 | 0 |
| factory_c | 1040 | 5329 | 1335 | 0 | 0 | 960 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `eee9bc9798b3ca8f5676badd8309d60d4c7e7af02fe9252790324f8745a862f9`

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
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 492 | 492 | 1000 | 0 |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 630 | 630 | 1000 | 0 |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 765 | 765 | 1000 | 0 |
| upgrade_components | component | 4000 | 0 | 0 | 359 | 359 | 4000 | 0 |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1669 | 1069 | 500 | 0 |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 15 | 2000 |
| hydrogen | 7 | 10 |
| basic_metals | 23 | 600 |
| technical_materials | 2 | 128 |
| component | 8 | 100 |
| module | 2 | 100 |
| robot_frame | 0 | 24 |
| mission_dock_construction_unit | 1 | 300 |
| matter_reactor_construction_unit | 1 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 778 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 40 | 564 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 27 | 475 |
| dock_a_hold | 245 | 475 |
| dock_b_hold | 775 | 800 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 120 |
| reactor_b | 0 | 0 |
| factory_a | 4 | 480 |
| factory_b | 0 | 0 |
| factory_c | 2 | 240 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 5371 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 528 | 4478 | 513 | 1 | 0 | 480 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 320 | 4453 | 496 | 0 | 0 | 240 | 0 |
| dock_a | 0 | 5236 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `1d6d5976d9e54204665af751af93f7f1798305e9374b6e9967051d374bf98b7f`

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
| expedition_frames | robot_frame | 500 | 600 | 600 | 1402 | 802 | 500 | 0 |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 15 | 2000 |
| hydrogen | 7 | 10 |
| basic_metals | 6 | 278 |
| technical_materials | 2 | 104 |
| component | 4 | 180 |
| module | 2 | 206 |
| robot_frame | 0 | 25 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 796 | 802 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 0 |
| factory_a_buffer | 34 | 533 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 0 | 0 |
| dock_a_hold | 259 | 333 |
| dock_b_hold | 0 | 0 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 120 |
| reactor_b | 0 | 0 |
| factory_a | 2 | 240 |
| factory_b | 0 | 0 |
| factory_c | 0 | 0 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 480 | 5205 | 75 | 0 | 0 | 240 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `df901ec7663be49607be2eaa142bd9e925302c9bb54d4d0e5d24c7709e7ccbff`
