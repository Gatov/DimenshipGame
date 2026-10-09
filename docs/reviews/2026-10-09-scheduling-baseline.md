# Scheduling Baseline (M3) — Plain Queue Order on the Shipped Vessel

Date: 2026-10-09. Ticket: M3 (#51) in
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`.

## Goal

This records the numbers every later scheduling ticket (K1, K2 and on) compares against. It runs
the design's situations A and B, approximated on the shipped vessel and chain (components,
modules, frames, construction units), under today's only policy: plain queue order.

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
orders the same frames at the same tick on a quiet vessel. The difference between the two
readiness times is meant to be the opportunity cost of queue order.

Three approximations the design's wording does not survive:

- **Equipment goes to a Launch Pad hold.** Nothing consumes it yet, since there are no missions.
  Left in Resource Storage, one expedition's frames would cover the next order without any work.
- **There is no treatment revisit.** The shipped vessel has no factory–reactor route and no
  workpiece (D2, K3, K4). B's "plates waiting for reactor treatment" is therefore represented
  only by contention for Factory Alpha.
- **Factory Gamma is ordered at tick 300 in A, not at tick 0**, because of finding 1 below. With
  both factory units ordered at tick 0, Gamma's plan never completes, for the reason that finding
  gives.

## Results

| Situation | Demands | Ready by the end | Notes |
|---|---:|---:|---|
| A | 12 | 6 | Every expedition order after the first stalls for good. |
| B | 5 | 4 | The expedition frames stall for good. |
| B alone | 1 | 1 | Frames ready 624 ticks after commit. |

Changeovers are low throughout, at 30 ticks each:

| Situation | Count | Ticks | Where |
|---|---:|---:|---|
| A | 7 | 210 | Factory Alpha 4, Factory Gamma 2, Reactor Alpha 1 |
| B | 4 | 120 | Factory Alpha 3, Reactor Alpha 1 |
| B alone | 3 | 90 | Factory Alpha 2, Reactor Alpha 1 |

They are low because so little work completes: in both situations the run ends with the factories
waiting on input that will never come.

Readiness among the demands that did finish:

| Situation | Demand | Readiness (ticks) |
|---|---|---:|
| A | expedition 1 frames | 829 |
| A | expedition 1 modules | 1,824 |
| A | component reserve | 215 |
| B | construction (Factory Gamma, Reactor Beta, Launch Pad Alpha) | 410 / 458 / 503 |
| B | upgrade components | 359 |
| B alone | expedition frames | 624 |

Reactor Beta is built in both situations and never runs, idling 8,910 ticks in A and 5,543 in B.
Reactor Alpha covers all reactor work on its own, at 64 working ticks in A and 48 in B. Reactor
capacity is not contended anywhere on this chain.

## Findings

### 1. The planner covers a new demand with stock that is already spoken for

`SimulationEngine.Uncommitted` starts from `TotalOf(item)`, which is every storage on the vessel,
and adds the expected output of every unfinished production task. It does not subtract what
another plan's transfers are about to carry away, and it does not exclude stock already delivered
into another demand's destination. A second demand for an item the vessel holds anywhere, or is
already making, is planned as a single transfer out of Resource Storage. That transfer waits
forever with `InsufficientSourceMaterial`:

- **Two construction orders for one unit type at the same time.** Factory Beta's plan presses a
  unit and carries it to its slot. Factory Gamma's plan reads that same unit as supply, emits only
  `factory_c_feed_modules` from Resource Storage, and never completes. A player can do this from
  the Operations composer by queuing two factory builds back to back.
- **A, expeditions 2–4.** Each order sees the frames and modules already sitting in a Launch Pad
  hold and plans only the final haul (`dock_*_supply ... 0/500`, postponed).
- **B, expedition frames.** The plan reads the upgrade's four units of components, sitting in
  Launch Pad Beta's hold, as its own supply. It orders no pressing, so module assembly waits with
  zero runs done, and so does everything downstream.

The scheduling design names this gap in its §2 ("does not give a plan ownership of stock"), and
the plan's K6b, the claims ledger, removes it. The finding here is that on the shipped vessel the
gap decides the outcome before scheduling does. Under queue order, an ordinary demand pattern
stalls permanently, and no ordering of the queues would rescue a plan whose production was never
ordered.

### 2. The baseline cannot yet show what K1 and K2 change

The plan's first slice assumed that costly changeovers and priority could be measured on the
shipped vessel with no storage change. These results do not support that. The stalled demands
never order the production that changeovers and priority would act on, so K1's rebalance and K2's
priority would mostly move numbers on work that already completes:

- the first expedition's frames and modules;
- the reserve;
- the construction units.

Changeover cost is 210 ticks over 9,000 in A.

### 3. Smaller observations

- **Reactor Beta's buffer** peaks at 475 permille in both situations. That is its construction unit
  arriving, and the reactor never draws on that buffer again.
- **Launch Pad Beta's hold in B** sits at a mean of 774 permille, holding the upgrade components,
  the stock that finding 1 lets the frames plan count as its own. Launch Pad Alpha's hold in A sits
  at 393 permille for the same kind of reason.
- **Factory Alpha in B** spends 5,418 of 6,000 ticks waiting on input. That is the stalled frames
  plan's module task, and it is a stall, not a shortage that will resolve.

## What this means for the plan

Committing these numbers as the reference is correct: they are what the vessel does today. But
these scripts cannot yet measure changeovers and priority, which is what they are for. Three ways
forward, for the project owner to choose:

1. **Fix the coverage arithmetic first, then re-record M3** (recommended). Make `Uncommitted`
   stop counting stock and output already promised to another destination. The narrowest rule
   is Resource Storage stock plus outputs bound for it, less outstanding transfers out of it. This
   is smaller than K6b and is not a claims ledger; it removes a double count that a player can
   trigger by hand. It changes planner output, so `EveryShippedUnbuiltSlot_...` and the draft
   byte-identity tests are where it would show.
2. **Pull K6b forward** ahead of K1 and K2, and accept that D3's full claims model arrives before
   the cheap first slice.
3. **Keep the order and reshape the scripts** so no item is ordered twice while an earlier order
   is in stock or in flight. That would make K1 and K2 measurable at once, but by tuning the
   situations around the defect rather than measuring the vessel.

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
| build_factory_b | factory_construction_unit | 1000 | 0 | 0 | 135 | 135 | 1000 | 0 |
| build_factory_c | factory_construction_unit | 1000 | 300 | 300 | 360 | 60 | 1000 | 0 |
| expedition_1_frames | robot_frame | 500 | 1200 | 1200 | 2029 | 829 | 500 | 0 |
| expedition_1_modules | module | 500 | 1500 | 1500 | 3324 | 1824 | 500 | 0 |
| expedition_2_frames | robot_frame | 500 | 2400 | 2400 | not ready | — | 0 | 0 |
| expedition_2_modules | module | 500 | 2700 | 2700 | not ready | — | 0 | 0 |
| component_reserve | component | 2000 | 3000 | 3000 | 3215 | 215 | 2000 | 0 |
| expedition_3_frames | robot_frame | 500 | 3600 | 3600 | not ready | — | 0 | 0 |
| expedition_3_modules | module | 500 | 3900 | 3900 | not ready | — | 0 | 0 |
| expedition_4_frames | robot_frame | 500 | 4800 | 4800 | not ready | — | 0 | 0 |
| expedition_4_modules | module | 500 | 5100 | 5100 | not ready | — | 0 | 0 |

#### Unfinished work

Every task of a not-ready demand still open at the end, as the engine last described it.

| Demand | Task | Executor | Work | State | Reason |
| :--- | ---: | :--- | :--- | :--- | :--- |
| expedition_2_frames | 40 | dock_b_supply | robot_frame resource_storage → dock_b_hold 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_2_modules | 41 | dock_b_supply | module resource_storage → dock_b_hold 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_3_frames | 45 | dock_a_supply | robot_frame resource_storage → dock_a_hold 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_3_modules | 46 | dock_a_supply | module resource_storage → dock_a_hold 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_frames | 47 | dock_b_supply | robot_frame resource_storage → dock_b_hold 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_4_modules | 48 | dock_b_supply | module resource_storage → dock_b_hold 0/500 | Postponed | InsufficientSourceMaterial |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 30 | 4040 |
| hydrogen | 7 | 10 |
| basic_metals | 22 | 550 |
| technical_materials | 3 | 200 |
| component | 7 | 220 |
| module | 2 | 224 |
| robot_frame | 0 | 50 |
| matter_reactor_construction_unit | 0 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 794 | 802 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 0 | 117 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 6 | 532 |
| factory_b_buffer | 15 | 493 |
| factory_c_buffer | 19 | 475 |
| dock_a_hold | 393 | 499 |
| dock_b_hold | 0 | 0 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 30 |
| reactor_b | 0 | 0 |
| factory_a | 4 | 120 |
| factory_b | 0 | 0 |
| factory_c | 2 | 60 |
| dock_a | 0 | 0 |
| dock_b | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 9000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 64 | 8872 | 34 | 0 | 0 | 30 | 0 |
| reactor_b | 0 | 8910 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 368 | 7826 | 686 | 0 | 0 | 120 | 0 |
| factory_b | 160 | 8144 | 562 | 0 | 0 | 0 | 0 |
| factory_c | 320 | 8241 | 20 | 0 | 0 | 60 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `99a3dd9b73a7af916e98e4ad3232b76260d8fa28cca7e4abed06230f6a0d7556`

---

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
| expedition_frames | robot_frame | 500 | 600 | 600 | not ready | — | 0 | 0 |

#### Unfinished work

Every task of a not-ready demand still open at the end, as the engine last described it.

| Demand | Task | Executor | Work | State | Reason |
| :--- | ---: | :--- | :--- | :--- | :--- |
| expedition_frames | 19 | factory_c_feed_modules | component resource_storage → factory_c_buffer 0/2000 | Postponed | InsufficientSourceMaterial |
| expedition_frames | 23 | factory_c_return | module factory_c_buffer → resource_storage 0/1000 | Postponed | InsufficientSourceMaterial |
| expedition_frames | 24 | factory_a_feed | module resource_storage → factory_a_buffer 0/1000 | Postponed | InsufficientSourceMaterial |
| expedition_frames | 25 | factory_a_return | robot_frame factory_a_buffer → resource_storage 0/500 | Postponed | InsufficientSourceMaterial |
| expedition_frames | 27 | factory_c | assemble_modules 0/10 runs | Postponed | InsufficientInputMaterial |
| expedition_frames | 28 | factory_a | assemble_frames 0/10 runs | Postponed | InsufficientInputMaterial |
| expedition_frames | 29 | dock_a_supply | robot_frame resource_storage → dock_a_hold 0/500 | Postponed | InsufficientSourceMaterial |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 34 | 4040 |
| hydrogen | 7 | 10 |
| basic_metals | 27 | 478 |
| technical_materials | 0 | 254 |
| component | 4 | 200 |
| mission_dock_construction_unit | 1 | 300 |
| matter_reactor_construction_unit | 1 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 785 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 0 | 117 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 5 | 564 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 118 | 475 |
| dock_a_hold | 0 | 475 |
| dock_b_hold | 774 | 800 |

#### Changeovers

| Facility | Count | Ticks |
| :--- | ---: | ---: |
| extractor_01 | 0 | 0 |
| reactor_a | 1 | 30 |
| reactor_b | 0 | 0 |
| factory_a | 3 | 90 |
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
| reactor_b | 0 | 5543 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 368 | 123 | 5418 | 1 | 0 | 90 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 0 | 191 | 5400 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 5498 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `5b2ef6f3e2d25e268664d04116e148b7263d0f07f15f3ecd61284043f1c493a8`

---

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
