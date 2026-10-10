# E1 — Situations A, B and a Held-Out Situation, Under Queue Order

Date: 2026-10-10
Ticket: E1 (#72) in `docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`

> **Since K5c (2026-10-10).** The planner's estimate now counts changeovers, which changed every
> situation A run and situation C under queue order. The figures and hashes here are the record as
> of this review. Current ones are in `docs/reviews/2026-10-10-k5c-changeover-aware-estimate.md`.

## Goal

These are the fixtures the experiment runs on. The design's situations A and B are written on the
shipped vessel with the revisit chain in them (K4, orderable since K5b-w). A third situation is
written now and held out. All of them are recorded under today's only policy, plain queue order.
These reports are E2's baseline: every reference controller is replayed against the same scripts
and compared with them.

M3's `situation-*.json` scripts stay unchanged. They are the record that the M3–K5b reviews cite,
and they predate the chain.

## How to reproduce

```bash
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-a-sustained.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-b-urgent.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-b-alone.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-c-held-out.json
```

The reports at the end are the tool's output, with only their headings demoted. A rerun on the
same content and kernel matches them byte for byte, final hash included. Since E2, a report also
prints its policy line and a *Stock at the end* table. Those are additions, and the hashes are
unchanged (`2026-10-10-e2-reference-controllers.md`).

## The situations

**A — sustained preparation** (`e1-a-sustained.json`, 9,000 ticks).

- The opening builds Reactor Beta and Factories Beta and Gamma, so two reactors and three
  factories share the work.
- From tick 1,200, four expeditions' equipment arrives every 1,200 ticks: robot frames, then
  modules 300 ticks later, then bulkheads 300 ticks after that, alternating between the two Launch
  Pad holds.
- Modules and bulkheads both take components, and bulkheads take a reactor's treatment, so
  pressing and treatment are shared work.
- Each order is a quarter unit, so two expeditions fit one pad hold at about 70%. At M3's half
  units the holds filled, and the script measured the hold rather than the policy.

**B — urgent completion during expansion** (`e1-b-urgent.json`, with `e1-b-alone.json` as its
control).

- At tick 0 the vessel commits an expansion: Factory Gamma, plus an upgrade of 2,500 Robot
  Modules and 6,000 Technical Materials into Resource Storage.
- The modules keep Factory Alpha pressing and assembling. The Technical Materials keep Reactor
  Alpha separating, after a changeover.
- At tick 300, 500 expedition bulkheads are ordered for Launch Pad Alpha. Their blanks need Factory
  Alpha and Reactor Alpha, both busy with the upgrade.
- The upgrade uses nothing the expedition does, so neither order can count the other's stock.
- The first draft sent the upgrade into Launch Pad Beta's hold, which, as a 25‰ buffer, could
  never hold it. The delivery stalled on `DestinationFull`, so the upgrade goes to the hold.

**C — held out** (`e1-c-held-out.json`, 7,000 ticks). **Not to be used to tune E2's
controllers.** A policy's result here says whether it carries over to a situation it was not
fitted to, which the design asks for.

- Small bulkhead repairs (100 each) arrive every 500 ticks from tick 300 for Launch Pad Beta's
  hold. Each needs the whole revisit: form, harden, finish.
- A 1,000-frame campaign is ordered at tick 600 on the opening vessel, so Factory Alpha must
  interleave pressing for it with the repairs' forming and finishing.
- Factory Beta is ordered at tick 900: a capacity change in mid-campaign that a policy can plan
  for or ignore.

## The baseline under queue order

Every demand in every script is delivered by its end tick.

### A

| | Ticks |
| :--- | ---: |
| Readiness, 12 expedition orders, sum | 4,841 |
| Changeovers: 39, total | 4,680 |
| Reactor Alpha working / switching | 280 / 960 |
| Reactor Beta working | **0** |
| Factory Alpha working / switching | 968 / 2,520 |
| Factory Gamma working | 40 |

- **Reactor Beta never works.** Treatment and separation all go to Reactor Alpha. It is free when
  the planner looks, and its treatment line is a tick shorter, and estimated finish does not count
  the changeover it is about to pay. Alpha pays 8 changeovers for 280 ticks of work.
- **Factory Alpha switches 21 times.** It spends 2,520 ticks switching against 968 working,
  because queue order groups nothing.
- **Factory Gamma is almost idle,** with 40 working ticks.

These are the design's own words for situation A: a better policy "groups compatible orders" and
uses the capacity it has.

### B

| | Alone | During the expansion |
| :--- | ---: | ---: |
| Expedition bulkheads ready after | 577 | **1,621** |
| Factory Alpha buffer, peak fill (‰) | 400 | **1,000** |
| Factory Alpha changeovers | 2 | 7 |
| Reactor Alpha changeovers | 1 | 2 |

- **The opportunity cost is 1,044 ticks.** The expedition waits behind the upgrade at both
  machines its blanks must visit.
- **Factory Alpha's buffer fills to the brim.** That is the design's "local space is limited".
- The upgrade's modules are ready in 1,438 ticks, and its Technical Materials in 479.

### C

| Demand | Committed | Readiness |
| :--- | ---: | ---: |
| repair_1 | 300 | 449 |
| campaign_frames | 600 | 1,741 |
| repair_2 | 800 | **1,997** |
| build_factory_b | 900 | 1,779 |
| repair_3 | 1,300 | 1,513 |
| repair_4 | 1,800 | 1,301 |
| repair_5 | 2,300 | 817 |
| repair_6 | 2,800 | 613 |
| repair_7 | 3,300 | 409 |

- **Repairs ordered during the campaign wait behind it,** the worst at more than four times
  the first repair's 449 ticks.
- **Factory Beta, the capacity change, arrives at tick 2,679,** after the campaign it could have
  helped. It does 32 ticks of work.
- **Factory Alpha's buffer peaks full** (mean 209‰). It pays 16 changeovers (1,920 ticks).
- **Reactor Alpha waits for input for 2,534 ticks:** blanks trickle in from a factory that is
  pressing.

## What E2 compares

E2 replays these four scripts under each reference controller: queue order (these reports),
simple replenishment and an improved controller. It compares the §7 metrics the report already
gives:
- readiness;
- useful completions;
- material and space tied up;
- changeover cost;
- interventions.

E2 tunes on A and B only. C is run once a controller is fixed. If one static policy wins all three
by the same margin, or the result turns on declaration order, the design's revisit conditions
apply (E3).

## Reports

### e1-a-sustained

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 9000
- Interventions (commands applied): 15

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 181 | 181 | 1000 | 0 | Normal |
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 316 | 316 | 1000 | 0 | Normal |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 337 | 337 | 1000 | 0 | Normal |
| expedition_1_frames | robot_frame | 250 | 1200 | 1200 | 1683 | 483 | 250 | 0 | Normal |
| expedition_1_modules | module | 250 | 1500 | 1500 | 1876 | 376 | 250 | 0 | Normal |
| expedition_1_bulkheads | bulkhead | 250 | 1800 | 1800 | 2257 | 457 | 250 | 0 | Normal |
| expedition_2_frames | robot_frame | 250 | 2400 | 2400 | 2761 | 361 | 250 | 0 | Normal |
| expedition_2_modules | module | 250 | 2700 | 2700 | 3037 | 337 | 250 | 0 | Normal |
| expedition_2_bulkheads | bulkhead | 250 | 3000 | 3000 | 3477 | 477 | 250 | 0 | Normal |
| expedition_3_frames | robot_frame | 250 | 3600 | 3600 | 3961 | 361 | 250 | 0 | Normal |
| expedition_3_modules | module | 250 | 3900 | 3900 | 4237 | 337 | 250 | 0 | Normal |
| expedition_3_bulkheads | bulkhead | 250 | 4200 | 4200 | 4677 | 477 | 250 | 0 | Normal |
| expedition_4_frames | robot_frame | 250 | 4800 | 4800 | 5161 | 361 | 250 | 0 | Normal |
| expedition_4_modules | module | 250 | 5100 | 5100 | 5437 | 337 | 250 | 0 | Normal |
| expedition_4_bulkheads | bulkhead | 250 | 5400 | 5400 | 5877 | 477 | 250 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 30 | 2520 |
| hydrogen | 7 | 10 |
| basic_metals | 23 | 400 |
| technical_materials | 4 | 246 |
| component | 10 | 226 |
| module | 3 | 50 |
| robot_frame | 0 | 25 |
| plate_blank | 2 | 100 |
| hardened_blank | 2 | 100 |
| bulkhead | 0 | 50 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 776 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 8 | 100 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 53 | 600 |
| factory_b_buffer | 17 | 475 |
| factory_c_buffer | 3 | 475 |
| dock_a_hold | 463 | 699 |
| dock_b_hold | 372 | 699 |

##### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 8 | 960 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 21 | 2520 | 0 |
| factory_b | 9 | 1080 | 0 |
| factory_c | 1 | 120 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 280 | 7028 | 732 | 0 | 0 | 960 | 0 |
| reactor_b | 0 | 8820 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 968 | 4763 | 748 | 1 | 0 | 2520 | 0 |
| factory_b | 800 | 6483 | 322 | 0 | 0 | 1080 | 0 |
| factory_c | 40 | 8325 | 179 | 0 | 0 | 120 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `0952baa7d499ec0e638966210fd4cb76eb196538f14e293db46d9f2cc854bbb6`

### e1-b-urgent

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 6000
- Interventions (commands applied): 4

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 180 | 180 | 1000 | 0 | Normal |
| upgrade_modules | module | 2500 | 0 | 0 | 1438 | 1438 | 2500 | 0 | Normal |
| upgrade_technical | technical_materials | 6000 | 0 | 0 | 479 | 479 | 6000 | 0 | Normal |
| expedition_bulkheads | bulkhead | 500 | 300 | 300 | 1921 | 1621 | 500 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 129 | 2520 |
| hydrogen | 7 | 10 |
| basic_metals | 24 | 478 |
| technical_materials | 8 | 128 |
| component | 13 | 240 |
| module | 1 | 50 |
| plate_blank | 2 | 100 |
| hardened_blank | 2 | 100 |
| bulkhead | 0 | 50 |
| factory_construction_unit | 1 | 300 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 799 | 807 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 19 | 306 |
| reactor_b_buffer | 0 | 0 |
| factory_a_buffer | 164 | 1000 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 7 | 475 |
| dock_a_hold | 137 | 200 |
| dock_b_hold | 0 | 0 |

##### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 2 | 240 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 7 | 840 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 1 | 120 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 424 | 4226 | 1110 | 0 | 0 | 240 | 0 |
| reactor_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 976 | 4086 | 98 | 0 | 0 | 840 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 80 | 5615 | 6 | 0 | 0 | 120 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `236ff96e551f611276638410d1dc7eef4d375f2e2cff77cac5b67774bddca308`

### e1-b-alone

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 6000
- Interventions (commands applied): 1

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| expedition_bulkheads | bulkhead | 500 | 300 | 300 | 877 | 577 | 500 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| hydrogen | 7 | 10 |
| basic_metals | 6 | 278 |
| component | 2 | 100 |
| plate_blank | 2 | 100 |
| hardened_blank | 2 | 100 |
| bulkhead | 0 | 50 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 798 | 803 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 4 | 200 |
| reactor_b_buffer | 0 | 0 |
| factory_a_buffer | 19 | 400 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 0 | 0 |
| dock_a_hold | 172 | 200 |
| dock_b_hold | 0 | 0 |

##### Changeovers

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

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 80 | 5570 | 230 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 240 | 5430 | 90 | 0 | 0 | 240 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `d54175c7889ea59c2e992a47526912b1c9ab65456c1e384f76877667d417b933`

### e1-c-held-out

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 7000
- Interventions (commands applied): 9

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| repair_1 | bulkhead | 100 | 300 | 300 | 749 | 449 | 100 | 0 | Normal |
| campaign_frames | robot_frame | 1000 | 600 | 600 | 2341 | 1741 | 1000 | 0 | Normal |
| repair_2 | bulkhead | 100 | 800 | 800 | 2797 | 1997 | 100 | 0 | Normal |
| build_factory_b | factory_construction_unit | 1000 | 900 | 900 | 2679 | 1779 | 1000 | 0 | Normal |
| repair_3 | bulkhead | 100 | 1300 | 1300 | 2813 | 1513 | 100 | 0 | Normal |
| repair_4 | bulkhead | 100 | 1800 | 1800 | 3101 | 1301 | 100 | 0 | Normal |
| repair_5 | bulkhead | 100 | 2300 | 2300 | 3117 | 817 | 100 | 0 | Normal |
| repair_6 | bulkhead | 100 | 2800 | 2800 | 3413 | 613 | 100 | 0 | Normal |
| repair_7 | bulkhead | 100 | 3300 | 3300 | 3709 | 409 | 100 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 25 | 2000 |
| hydrogen | 7 | 10 |
| basic_metals | 20 | 400 |
| technical_materials | 4 | 128 |
| component | 9 | 228 |
| module | 3 | 206 |
| robot_frame | 0 | 25 |
| plate_blank | 2 | 165 |
| hardened_blank | 2 | 100 |
| bulkhead | 0 | 50 |
| factory_construction_unit | 0 | 250 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 780 | 800 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 6 | 170 |
| reactor_b_buffer | 0 | 0 |
| factory_a_buffer | 209 | 1000 |
| factory_b_buffer | 1 | 475 |
| factory_c_buffer | 0 | 0 |
| dock_a_hold | 458 | 666 |
| dock_b_hold | 167 | 280 |

##### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 3 | 360 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 16 | 1920 | 0 |
| factory_b | 1 | 120 | 0 |
| factory_c | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 7000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 192 | 3914 | 2534 | 0 | 0 | 360 | 0 |
| reactor_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 1280 | 3598 | 202 | 0 | 0 | 1920 | 0 |
| factory_b | 32 | 4160 | 10 | 0 | 0 | 120 | 0 |
| factory_c | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `8b9aaf7e1e72ee05f9524ff2271daa25e3a514da29322766495b25efbabbca32`
