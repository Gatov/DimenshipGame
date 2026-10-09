# C0 — The Command Surface, and What a Hold Costs

Date: 2026-10-09
Ticket: C0 (#61), to `docs/superpowers/specs/2026-10-09-kernel-command-surface-design.md`

## Goal

Record what C0 built, check that it changed nothing on its own, and take the first measurement a
scripted command makes possible: holding the upgrade in situation B, which the design names as
part of an improved controller's answer.

## What was built

- **`SimulationEngine.Execute(Command)`**, the one door. One record per command: `OrderGoal`,
  `CommitPlan`, `QueueTask`, `SetPlanPriority`, `SetTaskPriority`, `HoldPlan`, `ReleasePlan`,
  `CancelPlan`, `AmendPlan`, `HoldTask`, `ReleaseTask`, `CancelTask`, `RelinquishStock` and
  `ReassignStock`.
- **A refusal is an answer.** An `ArgumentException` becomes `CommandRefused`, carrying the
  kernel's sentence and, for a refused draft, its issues. Any other exception still propagates.
- **A refused command changes nothing.** `Commit` now checks every task before it queues any. Before,
  a plan whose last task was invalid left the others queued with no plan. `Amend` restores its cut
  if the replanned tasks fail the same check.
- **The shell** reaches the kernel through `SimulationDriver.Execute`, bound to
  `ShellActions.Execute`. APPROVE is `Execute(CommitPlan)`, and `SimulationDriver.Commit` is gone.
  A command the kernel refuses no longer faults the game.
- **Replay scripts** gain `commands`: priority, hold, release, cancel, amend, relinquish and
  reassign. Each names a demand, never a plan id. Accepted commands count as interventions. A
  refused one is listed with its reason and counts for nothing.

## Neutrality

Every script that existed before C0 gives a byte-identical report, the final-state hash included:
smoke, situation A, situation A at priority, situation B, B alone and B at priority. The surface
is a door, not a second path, and `AnOrder_ThroughExecute_IsTheComposersPath_ByteForByte` pins
that for the kernel.

## The measurement

In situation B on the shipped vessel, the upgrade's 4,000 components are ready at tick 602, before
the frames are even ordered. A hold there would act on nothing. So two new scripts raise the upgrade
to 8,000, which keeps it running when the frames arrive. Dock B's hold takes 5,000, so the upgrade
cannot finish in either run.

- `situation-b-contested.json` is the control.
- `situation-b-hold.json` is the same script, tick for tick, with the upgrade held at tick 600, when
  the frames are ordered, and released at tick 2,000.

| | Contested | Held 600–2,000 |
| :--- | ---: | ---: |
| Expedition frames ready (ordered at 600) | 1,813 | 1,813 |
| Reactor Beta built | 1,077 | 2,475 |
| Dock A built | 1,212 | 2,610 |
| Factory Alpha, ticks in Held | 0 | 1,398 |
| Factory Alpha buffer, mean fill (permille) | 62 | 189 |
| Interventions | 5 | 7 |

### The hold buys the frames nothing

The frames are ready at tick 1,813 either way. Their delay is the planner's line choice, which K6b's
review traced to pressing on Factory Gamma and its 4-a-tick return line. The upgrade's work at
Factory Alpha is not on that path, so stopping it frees nothing the frames use. This is the same
conclusion K6b reached, now measured by the command the design proposes: on this vessel, location
is the lever, not allocation (K5a, K5b).

### The hold costs two construction builds 1,398 ticks

Holding the upgrade delays Reactor Beta and Dock A by exactly the length of the hold. In the
shorter probe run, their assembly runs at Factory Alpha postpone with `DestinationFull`. The
upgrade's cargo already on its way when the hold landed still arrived, as D3 Decision 7 requires.
It is held for the upgrade, as Decision 6 requires, and it sits in Factory Alpha's buffer. That
buffer is shared with every other plan's runs there. The held plan does no work and keeps its
space. Releasing it at 2,000 lets the upgrade's runs consume that material, and both builds follow.

This is D3 working as specified, and it is the design's situation B in miniature: *"Local space is
limited … Finishing the equipment releases space."* A hold stops a plan's work. It does not move
the plan's material. That makes it the player's or a controller's lever, with a cost the report now
shows:

- To free the space, a controller has to move the material out, with a transfer queued by hand, or
  not hold a plan whose stock is sitting in a shared buffer.
- `Relinquish` gives up the claim and not the space. The stock stays where it is, free for anyone,
  which helps only a plan that consumes that item at that buffer.

### What the report hides

Factory Alpha reads **Held** for those 1,398 ticks, not **Waiting output**. Its block reason is
the root cause across its queue, and `SafetyLock` outranks `DestinationFull` in declaration order.
That is correct for "why is this facility not working": it was told to stop. But it hides why the
*other* plans' runs there could not start. That is K8's ground: the explanation should name the
held plan whose stock fills the buffer, as it will name the plan behind a `MaterialClaimed`.

## What this changes for the rest of the plan

- **U1** builds the Operations controls on `ShellActions.Execute`. The kernel needs no more
  commands for priority, hold, release, cancel or amend.
- **E1 and E2** script and compare policies through `commands`, then through a controller hook
  written against `Execute`.
- **K8** gains a concrete case from this measurement: a run postponed for want of room should be
  able to say whose held stock is taking it.
- **K5a and K5b** remain the lever for situation B's frames. Allocation and holds have now both
  been measured against them and moved nothing.

## Report: situation B, contested

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
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 1077 | 1077 | 1000 | 0 | Normal |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 1212 | 1212 | 1000 | 0 | Normal |
| upgrade_components | component | 8000 | 0 | 0 | not ready | - | 0 | 0 | Normal |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1813 | 1213 | 500 | 0 | High |

#### Unfinished work

Every task of a not-ready demand still open at the end, as the engine last described it.

| Demand | Task | Executor | Work | State | Reason |
| :--- | ---: | :--- | :--- | :--- | :--- |
| upgrade_components | 18 | dock_b_supply | component resource_storage  dock_b_hold 5000/8000 | Postponed | DestinationFull |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 15 | 2000 |
| hydrogen | 7 | 10 |
| basic_metals | 37 | 478 |
| technical_materials | 2 | 150 |
| component | 99 | 224 |
| module | 2 | 50 |
| robot_frame | 0 | 24 |
| mission_dock_construction_unit | 1 | 300 |
| matter_reactor_construction_unit | 1 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 776 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 62 | 778 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 69 | 533 |
| dock_a_hold | 237 | 475 |
| dock_b_hold | 919 | 1000 |

#### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 4 | 480 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 3 | 360 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 4924 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 688 | 4814 | 18 | 0 | 0 | 480 | 0 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 480 | 4621 | 360 | 0 | 0 | 360 | 0 |
| dock_a | 0 | 4789 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `f218a9f83125df4c8eb7ba33a1c88e4aaf355209bb8a4ccef7d25ff443b4e523`

## Report: situation B, upgrade held

### Replay report

- Scenario: `default_vessel`
- Content version: `0.1.0`
- Ticks run: 6000
- Interventions (commands applied): 7

#### Demands

Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.

| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| build_factory_c | factory_construction_unit | 1000 | 0 | 0 | 180 | 180 | 1000 | 0 | Normal |
| build_reactor_b | matter_reactor_construction_unit | 1000 | 0 | 0 | 2475 | 2475 | 1000 | 0 | Normal |
| build_dock_a | mission_dock_construction_unit | 1000 | 0 | 0 | 2610 | 2610 | 1000 | 0 | Normal |
| upgrade_components | component | 8000 | 0 | 0 | not ready | - | 0 | 0 | Normal |
| expedition_frames | robot_frame | 500 | 600 | 600 | 1813 | 1213 | 500 | 0 | High |

#### Commands

Scripted commands, in script order. A refused command changed nothing and is not an intervention.

| At | Command | Demand | Detail | Outcome |
| ---: | :--- | :--- | :--- | :--- |
| 600 | hold | upgrade_components | - | accepted |
| 2000 | release | upgrade_components | - | accepted |

#### Unfinished work

Every task of a not-ready demand still open at the end, as the engine last described it.

| Demand | Task | Executor | Work | State | Reason |
| :--- | ---: | :--- | :--- | :--- | :--- |
| upgrade_components | 18 | dock_b_supply | component resource_storage  dock_b_hold 5000/8000 | Postponed | DestinationFull |

#### Material tied up

Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.

| Item | Mean | Peak |
| :--- | ---: | ---: |
| matter_mix | 15 | 2000 |
| hydrogen | 7 | 10 |
| basic_metals | 37 | 478 |
| technical_materials | 2 | 150 |
| component | 76 | 200 |
| module | 2 | 50 |
| robot_frame | 0 | 24 |
| mission_dock_construction_unit | 1 | 300 |
| matter_reactor_construction_unit | 1 | 300 |
| factory_construction_unit | 1 | 300 |

#### Space tied up

Storage fill in permille of its shared volume, sampled every tick.

| Storage | Mean | Peak |
| :--- | ---: | ---: |
| resource_storage | 774 | 799 |
| extractor_buffer | 1 | 4 |
| reactor_a_buffer | 1 | 90 |
| reactor_b_buffer | 0 | 475 |
| factory_a_buffer | 189 | 778 |
| factory_b_buffer | 0 | 0 |
| factory_c_buffer | 69 | 533 |
| dock_a_hold | 237 | 808 |
| dock_b_hold | 872 | 1000 |

#### Changeovers

| Facility | Count | Ticks | Abandoned |
| :--- | ---: | ---: | ---: |
| extractor_01 | 0 | 0 | 0 |
| reactor_a | 1 | 120 | 0 |
| reactor_b | 0 | 0 | 0 |
| factory_a | 4 | 480 | 0 |
| factory_b | 0 | 0 | 0 |
| factory_c | 3 | 360 | 0 |
| dock_a | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 |

#### Facility time

Ticks under each utilization category over the whole run, counted while built.

| Facility | Working | Idle | Waiting input | Waiting output | Throttled | Switching | Held |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| extractor_01 | 6000 | 0 | 0 | 0 | 0 | 0 | 0 |
| reactor_a | 40 | 5831 | 9 | 0 | 0 | 120 | 0 |
| reactor_b | 0 | 3526 | 0 | 0 | 0 | 0 | 0 |
| factory_a | 688 | 3416 | 18 | 0 | 0 | 480 | 1398 |
| factory_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| factory_c | 480 | 4621 | 360 | 0 | 0 | 360 | 0 |
| dock_a | 0 | 3391 | 0 | 0 | 0 | 0 | 0 |
| dock_b | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Final state SHA-256: `bb106e7d5574c836fb299bb1d03c24dc44726dd45345e380a95ff6b7aee01fe1`
