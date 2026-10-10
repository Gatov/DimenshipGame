# K5b-w — Workpiece Legs Go Buffer to Buffer

Date: 2026-10-10
Ticket: #71, the workpiece part of K5b (D2's K5b paragraph), after K4 (#70)

## What was built

`PlanDraftEditor` routes a leg whose item the hold refuses straight from the buffer that makes it
to the buffer that uses it. The test is `IWorldView.Accepts(Hold, item)`, which K3 already
answers, so no new interface was needed.

- **Routing.** A workpiece input is required with `deliverTo` set to the consumer's buffer. The
  producer's output move goes there instead of the hold, and the consumer emits no hold leg. When
  producer and consumer share a buffer there is no move at all.
- **Facility choice.** A facility that cannot take part in the chain is **skipped, not ranked
  last**:
  - a facility with no line from its buffer to the buffer the workpiece must reach;
  - a consumer with no line in from any buffer that makes its workpiece.

  Ranking last was not enough. When the eligible facility has a standing order, "unoccupied
  first" picks the stray one, and the draft plans a blank into a buffer it can never leave.
  `AFactoryWithNoLineToTheReactor_IsNeverChosenToFormTheBlank` fails without the skip.
- **Estimated finish** times a workpiece leg on the line it will take, not on a hold line.
- **Budget.** A workpiece surplus is not credited to the hold budget, because it is not in the
  hold. The supply rule is unchanged: the planner counts only the hold's free stock, so a blank
  already sitting in a buffer is never counted.

With no reachable producer the draft reports `NoExecutorOrLine`, a supply kind, as any unmakeable
input does. It never emits a hold leg for the blank.

## Measured

- **Neutral.** All eight existing scripts produce byte-identical reports, hashes included,
  against K4. Every changed branch is gated on an item the hold refuses.
- **The chain from Operations.** `scripts/revisit.json` orders Reactor Beta and 500 bulkheads at
  tick 0, sending the bulkheads on to Launch Pad Beta's hold. At tick 1200 it orders 500 more.
  - Both batches are delivered, in 761 and 614 ticks.
  - Factory Alpha pays 7 changeovers (840 ticks) moving between pressing, forming and finishing,
    and works 496 ticks.
  - Reactor Alpha treats all 20 runs. Reactor Beta is eligible once built, but Alpha is free and
    its line is one tick shorter, so estimated finish keeps choosing it.
  - `OnTheShippedVessel_WithReactorAlphaOnAStandingOrder_ReactorBetaTreats_OverItsOwnLines` shows
    Beta taking the work, over its own lines, when Alpha is occupied.

## Not built

- **Allocating stock already in a buffer.** That is the rest of K5b, and the hold-only supply rule
  still stands.
- **A preference between treaters beyond estimated finish.** For example, keeping Reactor Alpha on
  separation because it is already set up for it. Changeover cost is not in the estimate, which is
  the same today for every stage.
- **D2's open item.** `Enqueue` still accepts forming at a factory with no route to a reactor. The
  planner no longer plans it.

## Report

#### Replay report

- Scenario: `default_vessel`
- Content version: `0.2.0`
- Ticks run: 4000
- Interventions (commands applied): 3

##### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 181 | 181 | 1000 | 0 | Normal |
| bulkheads_1 | bulkhead | 500 | 0 | 0 | 761 | 761 | 500 | 0 | Normal |
| bulkheads_2 | bulkhead | 500 | 1200 | 1200 | 1814 | 614 | 500 | 0 | Normal |

##### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| hydrogen | 7 | 10 |
| basic_metals | 22 | 478 |
| component | 6 | 228 |
| plate_blank | 6 | 165 |
| hardened_blank | 6 | 100 |
| bulkhead | 1 | 50 |
| matter_reactor_construction_unit | 1 | 300 |

##### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 792 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 6 | 200 |
| reactor_b_buffer | 1 | 475 |
| factory_a_buffer | 91 | 780 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 0 | 0 |
| dock_a_hold | 0 | 0 |
| dock_b_hold | 163 | 200 |

##### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 7 | 840 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

##### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 4000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 160 | 3276 | 444 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 3820 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 496 | 2636 | 28 | 0 | 0 | 840 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_a | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `14e2059ef6cee19e70b43e76eb92dea5613e3b304f24dc6a7f424663ef511692`
