# E2 — Reference Controllers on the Experiment's Situations

Date: 2026-10-10
Ticket: E2 (#73) in `docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`

## Goal

The design's §7 asks for identical states and events to be replayed under basic queue order, simple
replenishment and an improved controller. It asks for them to be compared on readiness, useful
completions, material and space tied up, changeover cost and interventions, and for the comparison
to include a situation no controller was tuned on. E2 builds the hook those policies run through,
writes the three policies, tunes the improved one on E1's situations A and B, and then runs held-out
situation C once.

Manual scheduling, the design's fourth policy, is what the C0 command scripts already are
(`situation-b-priority.json`, `situation-b-hold.json`). E2 does not repeat it.

## How to reproduce

```bash
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-a-sustained.json improved
```

The third argument is `queue-order` (the default), `replenishment` or `improved`, and the scripts
are E1's four. Every run's final hash is listed at the end. A rerun on the same content and kernel
matches it.

## What was built

**The hook.** `IController.Decide(ControllerContext)` is called once per tick. It runs after that
tick's scripted demands and commands and before the engine advances. A controller reads the
snapshot and the plans the run has committed. It acts through two doors only:

- `Execute(Command)`, which is `SimulationEngine.Execute`.
- `Order(goal, destination)`, which is the composer's draft, approve and commit path, the one a
  scripted demand takes.

Both doors are counted. An accepted command or a committed order is an intervention, and a refused
command counts for nothing, as it does for a script. A controller is C#, not a program, by the
plan's Decision 6. Because the door is the player's own, a policy that wins here is one a player
could carry out by hand.

**The report** gains four things:

- A `Policy` line.
- A *Controller orders* table in the demands' format. Orders are named `{item}#{n}`.
- A *Controller commands* table, which counts commands by kind rather than listing them.
- *Stock at the end*: every item a demand or order asked for, as the vessel's storages hold it at
  the end. This is where over-production shows.

Under queue order the final hash is unchanged for every E1 script, and for every older script.
`QueueOrder_IsTheRunWithNoController_ByteForByte` pins it.

**The three controllers.**

- **Queue order** decides nothing. Its runs are E1's recorded baseline.
- **Simple replenishment** keeps a target stock of every item any demand has asked for. The target
  is the largest such demand. Every five operational minutes it reads the hold's free stock and
  orders the shortfall. It does not count what is already being made, which is the flaw §6 A
  describes. It is learned rather than configured so that it is the policy a player writes first,
  with nothing to tune. The five-minute reading is there because a reading every tick would order
  hundreds of duplicates per shortage, and that would measure only the queue's length.
- **Improved** adds the next rungs of §6's learning progression, using existing commands only:
  1. *Stock for what leaves.* Its targets are learned only from demands bound off the vessel, to
     anywhere but the hold.
  2. *Outstanding work counts.* A reading counts free stock plus the goal of every active plan
     bound for the hold with that item, its own or the script's.
  3. *Departing work first.* A departing plan is set to High the tick it appears. That reaches
     every prerequisite, because a plan task's priority is its plan's.
  4. *Holding what can wait.* There are three ranks: its own restock, then work the script
     ordered into the hold, then departing work. A plan bound for the hold is held while it has
     production outstanding at a facility where a higher-ranked plan also does, and is released
     once it has not.
  5. *Campaigns.* A restock orders two targets' worth, less what is there or coming.

## Tuning, on A and B only

Every step below was judged on A and B. C was not run until the last row was fixed.

| Improved, version | A readiness sum | A changeovers | B expedition | B upgrade (modules / technical) | B changeovers |
| :--- | ---: | ---: | ---: | :--- | ---: |
| 1. Learn from every demand; hold everything bound for the hold while any departing plan is open | 3,634 | 45 / 5,400 | 497 | not ready / 1,091 | 46 / 5,419 |
| 2. Learn only from departing demands; count all hold-bound plans as coming; hold only while departing production is outstanding | 4,019 | 55 / 6,535 | 497 | 2,042 / 1,084 | 15 / 1,800 |
| 3. Hold only at facilities the departing production uses | 2,523 | 51 / 6,120 | 497 | 2,042 / 944 | 15 / 1,800 |
| 3 with campaign ×2 | 2,032 | 40 / 4,661 | 497 | 3,962 / 944 | 31 / 3,720 |
| 3 with campaign ×3 | 2,515 | 37 / 4,375 | 497 | not ready / 944 | 46 / 5,426 |
| 4. The ordered rank: own restock yields to the script's hold-bound work, campaign ×1, ×2 or ×3 | 2,523, **2,032**, 2,515 | 51, **40**, 37 | 497 | 2,042 / 944 at all three | 15 / 1,800 |

Changeovers are count and ticks. Readiness is in ticks.

- **Version 1** learned a target of 2,500 modules from the upgrade's own order to the hold. It
  ordered the upgrade a second time, and Factory Alpha thrashed between the two.
- **Version 2** held stock-building wherever it ran. In A that idled Factories Beta and Gamma for
  over 3,000 ticks each, holding restock that no expedition was waiting on.
- **Campaign ×2** helped A and doubled the upgrade's delay in B, because the larger bulkhead
  restock fought the resumed upgrade at Factory Alpha.
- **Version 4** is what made B insensitive to campaign size. Campaign ×2 was chosen on A.

## Results

### A — sustained preparation

Readiness per expedition is listed as frames / modules / bulkheads.

| | Queue order | Simple replenishment | Improved |
| :--- | ---: | ---: | ---: |
| Expedition 1 | 483 / 376 / 457 | 480 / 673 / 591 | 483 / 376 / 457 |
| Expedition 2 | 361 / 337 / 477 | 4 / 4 / 4 | 402 / 286 / 4 |
| Expedition 3 | 361 / 337 / 477 | 4 / 353 / 4 | 4 / 4 / 4 |
| Expedition 4 | 361 / 337 / 477 | 4 / 4 / 433 | 4 / 4 / 4 |
| **Readiness sum** | **4,841** | **2,558** | **2,032** |
| Delivered | all 12 | all 12 | all 12 |
| Changeovers (count / ticks) | 39 / 4,680 | 44 / 5,280 | 40 / 4,661 |
| Interventions | 15 | 37 | 54 |
| Controller orders | — | 22 | 9 |
| End stock: modules / frames / bulkheads | 1,000 / 1,000 / 1,000 | 1,250 / 1,250 / 1,500 | 1,500 / 1,450 / 1,250 |
| Factory Alpha's buffer, mean / peak ‰ | 53 / 600 | 89 / 773 | 80 / 617 |
| Material tied up, sum of item means | 82 | 109 | 132 |
| Reactor Beta, working ticks | 0 | 176 | 72 |

- **Queue order.** Every expedition after the first takes 337 to 477 ticks per order.
- **Simple replenishment.** It nearly halves the readiness sum, because later expeditions are served from
  stock. The cost is 5 more changeovers, a fuller Factory Alpha buffer, and 500 bulkheads more
  than it needs.
- **Improved.** It readies expeditions 3 and 4 from stock. Its changeovers are queue order's (40
  against 39). It ends with 500 modules, 450 frames and 250 bulkheads beyond what it delivered.

### B — urgent completion during expansion

| | Queue order | Simple replenishment | Improved |
| :--- | ---: | ---: | ---: |
| Expedition bulkheads | 1,621 | **not ready** | **497** |
| Upgrade modules | 1,438 | 1,918 | 2,042 |
| Upgrade Technical Materials | 479 | 479 | 944 |
| Bulkheads alone (`e1-b-alone.json`) | 577 | 657 | 577 |
| Opportunity cost (expedition less alone) | 1,044 | — | −80 |
| Changeovers (count / ticks) | 10 / 1,200 | 85 / 10,052 | 15 / 1,800 |
| Interventions | 4 | 32 | 12 |
| Controller orders | — | 28 | 1 |
| End stock: Technical Materials / modules / bulkheads | 6,100 / 2,500 / 500 | 18,900 / 6,100 / 0 | 6,100 / 2,500 / 1,500 |
| Factory Alpha's buffer, mean / peak ‰ | 164 / 1,000 | 913 / 1,000 | 223 / 1,000 |
| Material tied up, sum of item means | 187 | 1,280 | 214 |

- **Improved.** The expedition is ready 1,124 ticks sooner than under queue order. The upgrade pays
  604 ticks on modules and 465 on Technical Materials, which is the trade the design's §6 B
  describes: a reactor changeover accepted and component work held.
- **The expedition beats its own control.** Factory Gamma, which the expansion builds at tick 180,
  presses the expedition's components (10 runs) while Factory Alpha forms blanks. The control has
  no Gamma, so Alpha does both, one after the other. The "−80" is a property of the situation,
  not of the policy.
- **Simple replenishment.** It reorders all 500 bulkheads every five minutes from tick 300, and
  module and Technical Materials restock behind the upgrade. Factory Alpha switches 38 times
  (4,510 ticks) and Factory Gamma 43 times (5,062 of its 5,821 built ticks). The expedition never
  arrives, and the hold ends with three times the Technical Materials anyone asked for.

### C — held out, run once per policy after tuning was fixed

| | Queue order | Simple replenishment | Improved |
| :--- | ---: | ---: | ---: |
| Repairs 1–7 | 449, 1,997, 1,513, 1,301, 817, 613, 409 | 777, then **6 not ready** | 449, 2,013, 1,529, 1,045, 561, 471, 427 |
| Campaign frames | 1,741 | 1,373 | 1,741 |
| Factory Beta's build | 1,779 | **not ready** | 2,981 |
| **Readiness sum** (repairs and frames) | **8,840** | — | **8,236** |
| Changeovers (count / ticks) | 20 / 2,400 | 48 / 5,760 | 29 / 3,170 |
| Interventions | 9 | 54 | 23 |
| Controller orders | — | 45 | 2 |
| End stock: frames / bulkheads | 1,000 / 700 | 1,800 / 300 | 1,000 / 900 |
| Factory Alpha's buffer, mean / peak ‰ | 209 / 1,000 | 854 / 1,000 | 595 / 1,000 |
| Material tied up, sum of item means | 72 | 859 | 180 |
| Factory Beta, working ticks once built | 32 | — | 0 |

- **Improved carries over poorly.** It saves 604 ticks, 7%, against 2,809 (58%) in A and 1,124 on
  B's expedition.
  - *Every demand in C departs.* So every demand is High, and its one urgency rule separates
    nothing. The second repair still waits 2,013 ticks behind the frames campaign.
  - *Its learned target assumes demand recurs.* The one-off 1,000-frame campaign became a
    2,000-frame restock at tick 600. That restock is still not ready at the end, and it is why
    Factory Alpha's buffer averages 595‰.
  - *Factory Beta's build is outranked.* Construction stays at Normal while every repair and the
    campaign are High, so the build lands 1,202 ticks later, after which Beta does no work at all.
- **Simple replenishment fails C outright.** Six repairs and Factory Beta's build never finish.
  Its one gain is the frames campaign, 368 ticks sooner.

## Findings for E3

1. **No static policy wins everywhere.** This is the design's first revisit condition, and it is
   not met.
   - Simple replenishment wins A's readiness and fails B and C.
   - The improved controller wins A and B, the situations it was tuned on, and gains little on C.
   - Queue order loses A and B and is the only policy that never fails to deliver.
2. **Executor choice is fixed at commit, and nothing a controller can do moves it.**
   - The planner picks a facility when a plan is committed, by estimated finish without changeover
     cost (K5b).
   - Reactor Beta works 0 ticks in A under queue order, though it is built and free from tick 181.
     The estimate preferred Reactor Alpha's shorter line every time, and left out the changeover
     that choice cost.
   - Factory Beta works 32 ticks in C under queue order and none under improved. The campaign had
     been bound to Factory Alpha before Beta existed.
   - The design's §5 lists *assign eligible work* as a control, and there is no such command. This
     is the second revisit condition, "hidden executor order decides success", in a narrow form:
     the order is not hidden, but no one can change it.
3. **A destination is not an urgency.** The improved controller ranks by where a demand goes. In C
   a repair and a campaign go to the same kind of place, so there is nothing to rank by. A demand
   that carries its urgency, or a player who sets it, is the missing input. The commands to act on
   it already exist.
4. **Duplicate orders are the dominant failure.** Simple replenishment's losses in B and C all
   trace to ordering a shortage again while it is being made. The design's warning holds exactly.
5. **No policy needed transfer handling.** Every controller order was an ordinary plan and every
   command a plan command. Not one hand-queued task or transfer was needed, so the third revisit
   condition, "repetitive transfer handling", is not met.
6. **Interventions scale with the policy's ambition.** The improved controller records 54
   interventions in A against queue order's 15, and both counts include the 15 scripted commits.
   Its own 39 decisions over 9,000 ticks are about one every 230 ticks for a player doing the same
   by hand.

## Not done

- **Manual scheduling is not a fourth controller.** It is the C0 scripts.
- **No controller reads the alert list or the waiting causes.** Both are available on the
  snapshot, and a later controller could use them.
- **The improved controller's numbers are not optimal.** Its tuning stopped at the first version
  that was robust on B, and the five-minute reading period was never varied.

## Final hashes

| Script | Policy | Final state SHA-256 |
| :--- | :--- | :--- |
| `e1-a-sustained.json` | queue order | `0952baa7d499ec0e638966210fd4cb76eb196538f14e293db46d9f2cc854bbb6` |
| `e1-a-sustained.json` | simple replenishment | `c85128876b10b581002c58f6a178ac4cfa69e298eef649262b3ab630018a5b79` |
| `e1-a-sustained.json` | improved | `c0e475888aabe7bdf22db6a704d47f9c6fd7e0780dea9f336b0ece18b01429e5` |
| `e1-b-urgent.json` | queue order | `236ff96e551f611276638410d1dc7eef4d375f2e2cff77cac5b67774bddca308` |
| `e1-b-urgent.json` | simple replenishment | `94446f1431a9a37174b8de3a0ac7840a4f022acc13000883fcfe7c6becd3beae` |
| `e1-b-urgent.json` | improved | `70278ad4517beda4adc487bfc701f51ad11ad79046c6ed8bdf61d15e0039e948` |
| `e1-b-alone.json` | queue order | `d54175c7889ea59c2e992a47526912b1c9ab65456c1e384f76877667d417b933` |
| `e1-b-alone.json` | simple replenishment | `90b20fd09cabafba6dd376fc033e5263e5338139818157b8f2acc94d63b3a82d` |
| `e1-b-alone.json` | improved | `cd4ebf572041c067056f6bbf973f52b5b05673d60511892a4f9cb1f100af913b` |
| `e1-c-held-out.json` | queue order | `8b9aaf7e1e72ee05f9524ff2271daa25e3a514da29322766495b25efbabbca32` |
| `e1-c-held-out.json` | simple replenishment | `6f63b16a9ce5a11c1be3140aaa0a105e3d4dddb5a42f24ef5da50def283934c8` |
| `e1-c-held-out.json` | improved | `dddf629ba6048c11e4d93bb469fc8ed63359a2d3676072d50534392ad326688e` |

The queue-order hashes are E1's, unchanged.
