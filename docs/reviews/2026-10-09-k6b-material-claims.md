# K6b — Material Claims, Measured Against the K6a Reference

Date: 2026-10-09. Ticket: K6b (#59) in
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`. Decision source:
`docs/superpowers/specs/2026-09-24-demand-objectives-and-material-claims-design.md`, Decision 5.

## Goal

Give committed plans ownership of the stock their unstarted withdrawals need, so that contested
stock goes by priority and plan age rather than by which executor happens to be visited first. D3
asks K6b to change contested outcomes on purpose and to commit the difference as the measurement of
the change. **The reports at the end are the new reference.**

## What was built

- **`ClaimLedger`** in `WorldState` stores `held(plan, storage, item)` and nothing else.
  - `ClaimMath` derives need, inbound and outstanding from the plan's tasks, exactly as D3's
    formulas state.
  - The engine and the save's validation share it, so they cannot disagree.
- **Commit.** A newly committed plan claims the free stock aboard at every storage it withdraws
  from.
- **Arrival.** A delivery or deposit made by a plan's own task is held for that plan, up to what
  its withdrawals there still need. Whatever is left is free, and is offered to outstanding claims
  by priority descending, then plan id ascending, skipping held plans. That allocation emits
  `ClaimAllocated`. The plan's own cargo keeping its owner emits nothing.
- **Withdrawals.**
  - A plan's run inputs and transfer pickups take the plan's holding first, then free stock, and
    never another plan's holding.
  - A task with no plan, and commissioning, take free stock only.
  - Stock that is present but held for someone else postpones with `MaterialClaimed`, appended
    last.
- **A finished plan** releases anything it still holds back into allocation. A whole-run surplus
  can leave a holding its withdrawals never drew.
- **Commands.**
  - `Relinquish` gives held stock back to the allocation order, possibly to the same plan if it
    still ranks first.
  - `Reassign` moves held stock between plans, bounded by what the receiver is short.
- **Snapshot.** `ItemStock.Held` and `WorldSnapshot.Claims`.
- **Save version 3.** Claims are written sorted by plan, storage and item, and a version 2 save
  upgrades with none.
  - A load reports a holding by a plan that is not active, a holding beyond its plan's need, and
    holdings at a storage that exceed the stock present. None is clamped.
- **`ClaimInvariantViolations`** checks D3's invariant. The kernel suite asserts it after every
  tick of a busy shipped run with four overlapping plans.

### One decision taken here: the planner counts only free stock in the hold

D3 says K6b changes no planning arithmetic. That was written when the planner counted vessel-wide
coverage. Since M3, the planner's supply is the main hold and nothing else (`IWorldView.InHold`).
Under that rule, a new plan that counted stock held for another plan would plan no production,
then wait on `MaterialClaimed` for good. That is the same race the claims exist to end, with a
better label on it.

So `InHold` now returns the hold's **free** stock. It is the same rule, "only what is in the main
hold", applied to stock that is in the hold but is not this plan's to take. The planner test that
pins two plans for one goal now expects the second to see 10 free ore rather than 60.

## The measurement

| Situation | Ready, before → after | Changeover ticks, before → after |
|---|---:|---:|
| A | 8 / 12 → **12 / 12** | 2,880 → **1,560** |
| A, expedition 3 frames at High | 7 / 12 → **12 / 12** | 5,302 → **1,560** |
| B | 5 / 5 → 5 / 5 | 840 → 960 |
| B, frames at High | 5 / 5 → 5 / 5 | 840 → 960 |
| B alone | 1 / 1 → 1 / 1 | 360 → 360 |

### Situation A: the race is gone, and switching nearly halves

Every stall in A was two plans counting the same hold stock, with the losers waiting on their input
for good. With claims, each plan holds what it counted, a later plan orders production for the rest,
and all twelve demands finish. Expedition 4's frames, which never finished before, are ready in 362
ticks.

Changeover ticks fall from 2,880 to 1,560, mostly because the factories no longer start work they
cannot finish. Factory Beta makes one changeover, where it made six before.

Some demands finish *later* than in the K6a reference. Expedition 1's modules take 2,412 ticks
against 1,159, and the component reserve 662 against 290. Before claims they finished early by
taking stock that other plans had counted on, which is the race. Those other plans then never
finished. Readiness of an individual demand is not comparable across the change; completions and
total switching are.

### Priority now buys time, and the ping-pong is gone

With expedition 3's frames at High:

- They are ready in **684** ticks, against 1,062 at Normal. Before claims, they never finished at
  either priority.
- Changeovers are 1,560 ticks, identical to the Normal run, against 5,302 before claims.
- Factory Beta, which ping-ponged through 26 changeovers before, makes one.

That is the outcome the K2 review predicted when the project owner chose to keep D1 unchanged: the
urgent plan now holds its input, so it never runs dry between deliveries and never hands the machine
back. Priority is not free. Expedition 1's modules (2,867 against 2,412), expedition 2's modules
(1,917 against 1,462) and the component reserve (1,662 against 662) all wait longer. That is the
trade a priority is supposed to make, and it is now visible in the report.

### Situation B: claims change the expansion, not the frames

B's expedition frames finish at 1,669 under claims, the same tick as before, at either priority.
The delay, as the K2 review traced, is the planner sending their pressing to Factory Gamma, whose
only line home carries 4 a tick. Claims do not move it. Location-aware planning (K5a, K5b) and
K4's revisit chain are what give B its intended tension.

The expansion's own builds shift. Factory Gamma is ready at 180 against 492, but Reactor Beta, the
Launch Pad and the upgrade components finish later. Each build's plan now holds its own basic
metals, where before whichever factory was visited first took them.

## What this changes for the rest of the plan

- **K6c** (hold, release, cancel, amend) is unblocked.
  - Setting `Held` directly, as the tests do, does not re-run allocation on release. K6c's
    `Release` command must call it, which D3 already requires: "a released plan's claims rejoin
    allocation".
- **K8** can name the holding plan behind every `MaterialClaimed`.
- **E2's** replenishment controller should be written against free stock, which is what
  `InHold` now returns.

---

## Report: situation A

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 9000
- Interventions (commands applied): 12

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 181 | 181 | 1000 | 0 | Normal |
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 316 | 316 | 1000 | 0 | Normal |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 337 | 337 | 1000 | 0 | Normal |
| expedition_1_frames | robot_frame | 500 | 1200 | 1200 | 2141 | 941 | 500 | 0 | Normal |
| expedition_1_modules | module | 500 | 1500 | 1500 | 3912 | 2412 | 500 | 0 | Normal |
| expedition_2_frames | robot_frame | 500 | 2400 | 2400 | 3166 | 766 | 500 | 0 | Normal |
| expedition_2_modules | module | 500 | 2700 | 2700 | 4162 | 1462 | 500 | 0 | Normal |
| component_reserve | component | 2000 | 3000 | 3000 | 3662 | 662 | 2000 | 0 | Normal |
| expedition_3_frames | robot_frame | 500 | 3600 | 3600 | 4662 | 1062 | 500 | 0 | Normal |
| expedition_3_modules | module | 500 | 3900 | 3900 | 5040 | 1140 | 500 | 0 | Normal |
| expedition_4_frames | robot_frame | 500 | 4800 | 4800 | 5162 | 362 | 500 | 0 | Normal |
| expedition_4_modules | module | 500 | 5100 | 5100 | 5374 | 274 | 500 | 0 | Normal |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 60 | 2520 |
| hydrogen | 7 | 10 |
| basic_metals | 29 | 400 |
| technical_materials | 7 | 254 |
| component | 16 | 236 |
| module | 6 | 124 |
| robot_frame | 1 | 50 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 765 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 12 | 600 |
| factory_b_buffer | 39 | 475 |
| factory_c_buffer | 147 | 804 |
| dock_a_hold | 645 | 999 |
| dock_b_hold | 551 | 999 |

#### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 4 | 480 | 0 |
| factory_b | 1 | 120 | 0 |
| factory_c | 7 | 840 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 240 | 8568 | 72 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 8820 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 768 | 5956 | 1795 | 1 | 0 | 480 | 0 |
| factory_b | 704 | 6206 | 1655 | 0 | 0 | 120 | 0 |
| factory_c | 1160 | 6616 | 48 | 0 | 0 | 840 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `9c95823949f18723234638822a95b0ae2b3610df03e4045ac974f6a22dbbad7d`

## Report: situation B

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 6000
- Interventions (commands applied): 5

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 180 | 180 | 1000 | 0 | Normal |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 757 | 757 | 1000 | 0 | Normal |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 892 | 892 | 1000 | 0 | Normal |
| upgrade_components | component | 4000 | 0 | 0 | 602 | 602 | 4000 | 0 | Normal |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1669 | 1069 | 500 | 0 | Normal |

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
| factory_a_buffer | 67 | 778 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 27 | 475 |
| dock_a_hold | 245 | 475 |
| dock_b_hold | 740 | 800 |

#### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 5 | 600 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 2 | 240 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 5244 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 528 | 4478 | 394 | 0 | 0 | 600 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 320 | 4765 | 496 | 0 | 0 | 240 | 0 |
| dock_a | 0 | 5109 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `a7e890cd4529d7ea24b78628b13ef74e5823ce7509e59f88996e0d1c81f6d171`

## Report: situation B alone

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 6000
- Interventions (commands applied): 1

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1402 | 802 | 500 | 0 | Normal |

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

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 2 | 240 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

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

Final state SHA-256: `480704b5a86e6635dd2a68c291cb0c177e6511b711d7bf366fd2c6dc8e32531f`

## Report: situation A with expedition 3 frames at High

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 9000
- Interventions (commands applied): 12

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 181 | 181 | 1000 | 0 | Normal |
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 316 | 316 | 1000 | 0 | Normal |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 337 | 337 | 1000 | 0 | Normal |
| expedition_1_frames | robot_frame | 500 | 1200 | 1200 | 2141 | 941 | 500 | 0 | Normal |
| expedition_1_modules | module | 500 | 1500 | 1500 | 4367 | 2867 | 500 | 0 | Normal |
| expedition_2_frames | robot_frame | 500 | 2400 | 2400 | 3166 | 766 | 500 | 0 | Normal |
| expedition_2_modules | module | 500 | 2700 | 2700 | 4617 | 1917 | 500 | 0 | Normal |
| component_reserve | component | 2000 | 3000 | 3000 | 4662 | 1662 | 2000 | 0 | Normal |
| expedition_3_frames | robot_frame | 500 | 3600 | 3600 | 4284 | 684 | 500 | 0 | High |
| expedition_3_modules | module | 500 | 3900 | 3900 | 5040 | 1140 | 500 | 0 | Normal |
| expedition_4_frames | robot_frame | 500 | 4800 | 4800 | 5162 | 362 | 500 | 0 | Normal |
| expedition_4_modules | module | 500 | 5100 | 5100 | 5374 | 274 | 500 | 0 | Normal |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 60 | 2520 |
| hydrogen | 7 | 10 |
| basic_metals | 29 | 400 |
| technical_materials | 7 | 200 |
| component | 16 | 224 |
| module | 6 | 124 |
| robot_frame | 1 | 50 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 765 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 12 | 600 |
| factory_b_buffer | 47 | 475 |
| factory_c_buffer | 147 | 765 |
| dock_a_hold | 639 | 999 |
| dock_b_hold | 551 | 999 |

#### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 4 | 480 | 0 |
| factory_b | 1 | 120 | 0 |
| factory_c | 7 | 840 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 240 | 8568 | 72 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 8820 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 768 | 5865 | 1886 | 1 | 0 | 480 | 0 |
| factory_b | 704 | 6115 | 1746 | 0 | 0 | 120 | 0 |
| factory_c | 1160 | 6616 | 48 | 0 | 0 | 840 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `2f038d0b98038bd9338da6ae6db5612caab98db4f94175a6a11b12b06793f28e`

## Report: situation B with the expedition frames at High

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 6000
- Interventions (commands applied): 5

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 180 | 180 | 1000 | 0 | Normal |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 757 | 757 | 1000 | 0 | Normal |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 892 | 892 | 1000 | 0 | Normal |
| upgrade_components | component | 4000 | 0 | 0 | 602 | 602 | 4000 | 0 | Normal |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1669 | 1069 | 500 | 0 | High |

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
| factory_a_buffer | 67 | 778 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 27 | 475 |
| dock_a_hold | 245 | 475 |
| dock_b_hold | 740 | 800 |

#### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 5 | 600 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 2 | 240 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 5244 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 528 | 4478 | 394 | 0 | 0 | 600 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 320 | 4765 | 496 | 0 | 0 | 240 | 0 |
| dock_a | 0 | 5109 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `c8cf0e137f37bcf76aafee4c68e9cbd51ea79fb49961d3dbd415f42ec6ac881b`
