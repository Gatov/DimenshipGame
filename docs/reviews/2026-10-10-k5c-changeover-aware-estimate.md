# K5c — The Facility Estimate Counts the Changeover

Date: 2026-10-10
Ticket: K5c (#75), follow-up 1 of `docs/reviews/2026-10-10-e3-experiment-report.md`

## Goal

K5b chooses a facility for each production stage by estimated finish:

- the work queued ahead;
- plus the longer of the stage's own work and its slowest line;
- plus the belt lengths.

Setup appears nowhere in it. E3 found the cost. In situation A every hardening went to Reactor
Alpha, which switched eight times, while Reactor Beta stood built and idle all run. Moving the
hardening to Beta by hand saved 352 ticks. K5c makes the default do what that hand did: a stage
that would make its facility reconfigure pays the switch-over in the estimate.

## The rule

A facility's **setups** are every schematic it is set up for or about to be:

- the configured one;
- the one a switch-over in progress is loading;
- each unfinished queued task's.

A stage whose schematic is among them is grouped with that work. Selection already prefers the
configured schematic, so once the facility switches for the queued task, the new stage follows it
without a second changeover. A stage whose schematic is not among them adds one switch-over to that
facility's estimate. A facility with no setups at all, never configured and with nothing queued,
pays nothing, as in selection, where a first configuration is free.

Stages the same draft has already placed at a facility count as setups too, alongside their ticks,
which the draft already tracked.

- **`PlannerFacility`** gains `Setups` and `SwitchOverTicks`, defaulting to an empty list and
  zero. A world view that predates them estimates as it did, and so does a facility with no
  switch-over cost.
- **`SimulationEngine`'s world view** fills both from the facility and its queue.
- **`EstimatedFinish`** adds `SwitchIn`, which is either zero or one switch-over.

**Only the switch in is charged.** Putting hardening on Reactor Alpha also costs the switch back to
refining, but that switch belongs to whatever refining work comes next. The estimate cannot see
work nobody has ordered yet, and charging a guess at it would make one order's choice depend on
orders that may never come.

**Not done:** switch-overs among work already queued, which `QueuedTicks` still leaves out. They
are the same for every candidate stage at a facility, so they would shift that facility's estimate
without being the stage's cost. Counting them would be a separate decision.

## Measured

Every script under every policy, before and after. Readiness sums exclude construction demands.

| Script | Policy | Readiness sum, before | After | Changeovers, before | After | Final state SHA-256, after |
| :--- | :--- | ---: | ---: | ---: | ---: | :--- |
| `e1-a-sustained.json` | queue order | 4,841 | **2,910** | 39 | **15** | `1ce19e6f41e42bbc9b10289247fc1d957f508057b9b00dba3fe39cf3dc34b715` |
| `e1-a-sustained.json` | replenishment | 2,558 | 1,426 | 44 | 21 | `f134da4eaa8649320aeb4405b142b39e8303e530642de0d8cf8a91ec04cf5eeb` |
| `e1-a-sustained.json` | improved | 2,032 | 853 | 40 | 16 | `b286a6fe5bd4e0c8ce6f0329c54cd162de89e3f5a1a3ddbc9eae4f6481a926da` |
| `e1-a-manual.json` | queue order | 4,489 | 2,922 | 33 | 16 | `5709a4479b1785eaabf34a4e51e5fbe63c8384e737dc9234e5d9ee94b10ccb84` |
| `e1-a-manual.json` | replenishment | 2,560 | 1,425 | 44 | 22 | `df146e76a880fc933fd8d154cdee538675197d0ba0e01ecab90dcd9718e558ea` |
| `e1-a-manual.json` | improved | 3,330 | 889 | 50 | 19 | `18b513e15b645131e95a2c2b2103a2913484b2ef6d54bdabe2ea8d753320a270` |
| `e1-c-held-out.json` | queue order | 8,840 | 8,904 | 20 | 20 | `8194c8f97fb594c1b507096d24a67ff998dc5ccc76dbf3837375a1f2091b93e9` |
| `situation-a.json` | queue order | 4,147 | **1,689** | 20 | **3** | `9e597b1a06703f51419dae4090d7fd8baeee969e310a5c3175cab566b95c9081` |
| `situation-a-priority.json` | queue order | 4,147 | 1,689 | 20 | 3 | `92e46f7ac7f855d5c57c6309c777130ca5aab20c886ddb83ddff5d035d1a1560` |

**Unchanged, byte for byte:**

- Every `e1-b-*` run under every policy. Situation B builds no second reactor, and no facility
  choice in it changed.
- `e1-c-held-out.json` under replenishment and improved.
- `smoke.json`, `revisit.json`, and every `situation-b*.json`.

### A — every facility's opening setup now draws its work

Under queue order, before and after:

| Stage | Before | After |
| :--- | :--- | :--- |
| Technical Materials (`separate_technical`) | Reactor Alpha | Reactor Beta, set up for it from the scenario |
| Hardening | Reactor Alpha | Reactor Alpha |
| Pressing components | Factory Alpha or Factory Beta, by order | Factory Alpha, set up for it |
| Modules | Factory Alpha, once Factory Gamma | Factory Beta, set up for it |
| Frames | Factory Beta | Factory Gamma, set up for it |
| Readiness per expedition (frames / modules / bulkheads) | 361 / 337 / 477 | 250 / 70 / 377 |

- **Changeovers fall from 39 to 15**, and Reactor Alpha's from 8 to 1.
- **Reactor Beta, Factory Beta and Factory Gamma all work.** Under queue order before K5c, Reactor
  Beta worked 0 ticks and Factory Gamma 40.
- **The split differs from E3's manual one.** By hand, the hardening went to Beta. The estimate
  sends Technical Materials there instead, because Beta's scenario setup is `separate_technical`,
  and keeps the hardening on Alpha once Alpha is set up for it. That is the better split:
  `e1-a-manual.json` is now 12 ticks behind plain queue order (2,922 against 2,910). Its first
  assignment puts Beta on hardening, and the estimate then sends the rest there itself, so it needs
  1 facility pick instead of 4. Beta then switches between both kinds of work.
- **Every policy gains, and the ranking holds.** Improved is 853, simple replenishment 1,426 and
  queue order 2,910. Duplicate orders still cost replenishment the most, at 21 changeovers against
  improved's 16.

### C — held out, and slightly worse

C was not used to design the rule, and was run once after it was fixed.

- **Queue order is 64 ticks slower (0.7%)**, with the same 20 changeovers. Repairs 1–3 are
  unchanged, and repairs 4–7 each finish 16 ticks later.
- **The cause is one pressing stage.** Factory Beta is built mid-campaign, set up for modules.
  Before K5c it pressed components for repairs 6 and 7. Now it presses only for repair 7, so it
  works 16 ticks instead of 32, and repair 6's pressing goes to Factory Alpha, which is set up for
  pressing. Charging Beta the switch-over tipped a close call that the idle facility had been
  winning. Repairs 4 and 5 are still unfinished at Factory Alpha when repair 6 is ordered at tick
  2800. Its 16 ticks of pressing land ahead of their remaining work, so every repair from 4 on
  finishes 16 ticks later.
- **Improved and replenishment are unchanged.** C's real problem, a campaign committed to Factory
  Alpha before Factory Beta existed, is follow-up 2 of the E3 report (reassigning committed work),
  and an estimate made at commit cannot reach it.

## What this does to the E3 verdict

None of it changes. A's numbers all move down, but the order among the policies is the same, and
B and the held-out C are unchanged except for C's 64 ticks. The improved controller is still best
on all three situations' readiness, by 71% in A, 69% on B's expedition and 7.5% in C. It was tuned
on two of them and gains least on the one it was not. Condition 2's first gap is closed: the
default estimate now sees the changeover. The second gap, moving work after commit, remains.

## Not done

- **The switch back is not charged.** See *The rule*.
- **C's regression is not chased.** Tuning the rule until C recovered would be tuning on C.
- **E1's, E2's and E3's recorded A and C hashes are now historical.** Each review has a note
  pointing here.

## Tests

`ProductionPlannerTests` gains three tests, and each fails with the switch-in removed:

- a facility set up for the stage beats a twin that would reconfigure, until its queue costs more
  than the changeover;
- a facility never set up pays nothing;
- work queued for the stage's schematic counts as set up.

`ExperimentTests` pins A under queue order: every reactor and factory works, Reactor Alpha switches
at most once, and the total is under half of E1's 39. E3's A test now pins only what the
assignment itself does, because the planner makes most of the choice that test was about.
