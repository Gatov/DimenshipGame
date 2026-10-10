# E3 — Experiment Report and Go/No-Go

Date: 2026-10-10
Ticket: E3 (#74) in `docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`

> **Since K5c (2026-10-10).** The planner's estimate now counts changeovers, which changed every
> situation A run and situation C under queue order. The figures and hashes here are the record as
> of this review. Current ones are in `docs/reviews/2026-10-10-k5c-changeover-aware-estimate.md`.
>
> **Since K6d (2026-10-10).** Follow-up 2 is built: `MoveWork` moves a plan's unstarted runs to
> another facility after commit (`docs/reviews/2026-10-10-k6d-moving-committed-work.md`). It was
> not measured on C, which stays held out.

## Goal

The scheduling design (`docs/production scheduling and automation - gameplay design.md`) is a
*Draft — exploratory*. Its §7 says how to find out whether it should become more than that. It
proposes replaying identical states and events under four policies: basic queue order, manual
scheduling, simple replenishment and an improved controller. It names three results that would send
the design back:

1. One obvious static policy wins everywhere.
2. Hidden executor order decides success.
3. Progress requires repetitive transfer handling.

E1 scripted the situations and E2 measured three of the four policies. E3 adds the fourth, manual
scheduling, judges every result against the three conditions, and recommends whether the design
moves from exploratory to a contract.

## Source material

- `docs/reviews/2026-10-10-e1-situations.md`: situations A, B (with its control) and held-out C.
- `docs/reviews/2026-10-10-e2-reference-controllers.md`: queue order, simple replenishment and the
  improved controller, with every tuning step.
- The design's §5 (the control table) and §7 (the experiment and its revisit conditions).

## What was built

E2 counted manual scheduling as the C0 command scripts. Those run on the older `situation-*`
fixtures, though, not on E1's, and they cannot make the decision the E2 review found mattered most:
which facility runs a stage.

**Facility assignment in a script.** A scripted demand may carry an `assign` list of
`{ schematic, facility }` pairs. Before approval, every production step in its draft that runs a
listed schematic is moved to the listed facility. The move is made by the composer's
`SetExecutor` edit, which is exactly what the Operations facility picker does. So the harness
schedules with the player's control and no other.

- The parser refuses an assignment to a facility whose type cannot run the schematic, since the
  picker never offers one. It also refuses an unknown schematic or facility, a schematic listed
  twice and an empty list, and it collects these errors like every other.
- An assignment to an unbuilt facility is refused at approval, as it would be in the composer, and
  the demand is reported refused.
- Each edit is an intervention, because picking a facility is a decision. A report with any
  assignments says how many on a line of its own. A report without them reads as before, and every
  E1 and E2 hash is unchanged.

**Two manual fixtures**, on the tuning situations only. C stays held out.

- `e1-a-manual.json`: situation A, with each bulkhead order's hardening assigned to Reactor Beta.
  That is four decisions.
- `e1-b-manual.json`: situation B, with the expedition's plan raised to High when it is ordered.
  That is one decision.

Each was the best of a handful of variants, and the losing variants are recorded below, because
they are evidence too.

## Manual scheduling

### A — the hardening moves to the reactor that was idle

| | Queue order | Manual | Simple replenishment | Improved |
| :--- | ---: | ---: | ---: | ---: |
| **Readiness sum** | 4,841 | 4,489 | 2,558 | **2,032** |
| Bulkheads, expeditions 2–4 | 477 each | 359 each | 4, 4, 433 | 4, 4, 4 |
| Reactor Alpha changeovers | 8 / 960 | 1 / 120 | 6 / 720 | 5 / 600 |
| Reactor Beta, working ticks | 0 | 160 | 176 | 72 |
| All changeovers (count / ticks) | 39 / 4,680 | 33 / 3,960 | 44 / 5,280 | 40 / 4,661 |
| Interventions | 15 | 19 | 37 | 54 |
| Material tied up, sum of item means | 82 | 82 | 109 | 132 |
| End stock: modules / frames / bulkheads | 1,000 each | 1,000 each | 1,250 / 1,250 / 1,500 | 1,500 / 1,450 / 1,250 |

- **Four decisions save 352 ticks (7%)** and six changeovers. That is no over-production and no
  material tied up beyond queue order's.
- **The planner never picks Reactor Beta.** Its estimate (K5b) prefers Reactor Alpha's shorter
  treatment line and leaves out the changeover the choice costs. So Alpha switches between refining
  and hardening twice per bulkhead order, while Beta sits built and idle from tick 181. Moved to
  Beta, the hardening keeps its setup for the whole run, and so does Alpha's refining.
- **Stock still beats scheduling in A.** The demand recurs and nothing is urgent, so the policies
  that make stock ahead win by far more than any assignment. Manual scheduling is the best policy
  that makes nothing it was not asked for.

What lost, all on top of the Reactor Beta assignment:

| Variant | Readiness sum | Changeovers |
| :--- | ---: | ---: |
| Modules (and their components) to Factory Beta | 5,568 | 32 / 3,840 |
| Frames (and their modules and components) to Factory Beta | 4,988 | 33 / 3,960 |
| Modules to Factory Beta; frames to Factory Gamma | 5,103 | 33 / 3,960 |
| Modules and frames to Factory Beta; all their components to Factory Gamma | 5,532 | 21 / 2,520 |

Spreading factory work is a real trade-off and not a free win. The last variant has 18 fewer
changeovers than queue order and is still 1,043 ticks slower than the Reactor Beta move alone,
because Factories Beta and Gamma sit at the far end of slower lines from the hold. The planner's
choice of factory, which counts lines, beat every alternative tried. Its choice of reactor, which
loses on a changeover the estimate cannot see, lost to the one alternative there is.

**Manual and improved together lose.** `e1-a-manual.json` under the improved controller gives a
readiness sum of 3,330, against 2,032 for the controller alone, with 50 changeovers. Its orders
cannot carry an assignment, so its bulkhead restock still goes to Reactor Alpha. Its own restocks
also run much longer: 2,983 ticks for the first bulkheads against 1,525, and 3,431 for the first
frames against 2,309. Expedition 2's bulkheads are therefore made rather than served from stock.
The mechanism behind the longer restocks was not traced (see *Not done*). Either way, two good
policies do not add up.

### B — one priority does the improved controller's work

| | Queue order | Manual | Simple replenishment | Improved |
| :--- | ---: | ---: | ---: | ---: |
| **Expedition bulkheads** | 1,621 | **504** | not ready | **497** |
| Upgrade modules | 1,438 | 2,041 | 1,918 | 2,042 |
| Upgrade Technical Materials | 479 | 799 | 479 | 944 |
| Changeovers (count / ticks) | 10 / 1,200 | 13 / 1,523 | 85 / 10,052 | 15 / 1,800 |
| Interventions | 4 | 5 | 32 | 12 |
| Factory Alpha's buffer, mean / peak ‰ | 164 / 1,000 | 231 / 1,000 | 913 / 1,000 | 223 / 1,000 |
| Material tied up, sum of item means | 187 | 187 | 1,280 | 214 |

- **One command gets the expedition to within 7 ticks of the improved controller**, which made 8
  decisions to get there. The upgrade's Technical Materials pay 145 ticks less.
- **This answers the design's own warning.** §6 B expects that "raising only the equipment's
  final-task priority changes little". A plan's priority is not its final task's. It is read live
  by every task the plan spawned (K6a, D3 Decision 1), so the reactor's treatment, the factory's
  forming and the components' pressing are all promoted together. That is the behaviour §5 asks
  for: "promoting an objective must meaningfully affect its prerequisites".
- **Holding the upgrade as well adds nothing.** With the expedition at High, holding the upgrade's
  modules from tick 300 to 800 moved the expedition by 0 ticks. Holding its Technical Materials too
  moved it by 7, and cost the Technical Materials 295. Priority already wins every contested start,
  and the claims ledger keeps what was ordered for whom.

## The verdict, condition by condition

### 1. One obvious static policy wins everywhere — **not met**

| Situation | Best measured | Runner-up | Worst |
| :--- | :--- | :--- | :--- |
| A, sustained | Improved, 2,032 | Simple replenishment, 2,558 | Queue order, 4,841 |
| B, urgent | Improved, 497 (manual 504, one decision) | Manual | Simple replenishment, never delivers |
| C, held out | Improved, 8,236 | Queue order, 8,840 | Simple replenishment, 6 of 7 repairs never delivered |

- **The winner in A fails in B and C.** Simple replenishment, the first policy a player would
  write, comes second in A and ruins the other two through duplicate orders.
- **The best policy on the tuning set carries over poorly.** The improved controller gains 58% in
  A and 69% on B's expedition, but only 7% on held-out C. There every demand departs, so its one
  urgency rule separates nothing, and its learned target turns a one-off campaign into a
  2,000-frame restock that clogs Factory Alpha.
- **The right manual move differs by situation.** In A it is a facility, and priority has nothing
  to rank. In B it is a priority, and a facility move is not available, because Reactor Beta is not
  built.
- **Within one situation, the moves are trade-offs.** Factory reassignments in A trade changeovers
  against line length, and both directions lose somewhere. Combining two good policies lost.

That is the design's intended shape: different situations reward different policies, and §6 says
"new situations should expose assumptions in existing controllers". The improved controller's
learned target is exactly such an assumption, and C exposed it.

### 2. Hidden executor order decides success — **not met, with two gaps**

*Executor order* in the design's §2 meant facility visit order deciding who gets contested stock and
power. That is gone:

- **Stock.** Contested stock goes by plan claims and priority (K6b), not by which facility the
  tick visits first.
- **Power.** On a starved tick, power goes by priority and task id, with runs already in progress
  granted first.
- **Selection.** Among ready tasks, a facility picks by priority, then current setup, then queue
  order (K2). That order is shown on the card and named in every wait (U3, K8).

Which facility runs a stage is not hidden either. The draft shows it on every row before approval,
and the picker changes it. A has a facility that decides success, and a player who looks can see
that and move it.

Two gaps remain, and both are about *control*, not visibility:

- **The default facility choice is blind to changeovers.** K5b's estimate counts queue and lines,
  not setup. In A that sends every hardening to Reactor Alpha, and a player has to know to override
  it four times. A changeover-aware estimate would make the default what the manual run found.
- **Nothing moves work after commit.** The design's §5 lists *assign eligible work* as a control,
  and the composer provides it only before approval. In C, Factory Beta is built while the frames
  campaign that could use it is already bound to Factory Alpha. It works 32 ticks under queue order
  and none under the improved controller. No command can rebind the campaign.

Neither gap is order deciding success behind the player's back. Both are reasons the player cannot
always act on what they see.

### 3. Progress requires repetitive transfer handling — **not met**

Fifteen runs were measured: 12 in E2, the two manual fixtures, and the manual A fixture under the
improved controller. None queued a transfer or a task by hand. Every order was an ordinary plan
whose legs the planner routed, workpieces included (K5b-w). Every command was a plan command, and
every manual decision was a priority or a facility pick. The treatment lines and buffer-to-buffer
legs that K4 added carry the revisit chain without a single hand-placed move.

## Recommendation: go

None of the three revisit conditions is met. The core combination the design set out to test has
been built on the shipped vessel, and it produces decisions that a better policy wins and a worse
one loses: costly changeovers, local-only workpieces, a facility revisit and competing demands. The
decisions differ by situation, and the obvious first policy fails.

The recommendation is that the design move from *exploratory* to a contract **for that core
combination**. The design's status line is the project owner's to change, and E3 does not edit it.

What a go does not cover:

- **Session depth** is still a playtesting question, as §7 says. A replay measures outcomes, not
  whether making these decisions for an hour is fun.
- **The candidate and deferred mechanics** (§3) were held out of the comparison on purpose. These
  are upgrades and specialisation, substitutable material, transport reversal and the rest. Each
  still needs its own evidence, and the plan's *Not in scope* list now releases them for
  scheduling.
- **The evidence is small.** It is one vessel, three situations and C# controllers, with the
  program language not yet built. The improved controller is a reference, not a tuned optimum.

## Follow-ups this report recommends

In the order the evidence supports them:

1. **A changeover-aware facility estimate** (kernel, K5b's follow-up). It would add the switch-over
   cost to the estimate when a facility's configured or last-queued setup differs from the stage's.
   A gives the target: the default should do what the four manual assignments did. This changes
   most replay hashes, so it is a ticket of its own, measured before and after.
2. **Reassign committed work** (kernel command, the §5 *assign eligible work* control after
   commit). It would move a plan's unstarted production tasks to another eligible facility, with
   their legs replanned. C's Factory Beta is the case. Leave runs in progress and cargo aboard
   alone, as hold does.
3. **Urgency on a demand.** A destination is not an urgency (E2 Finding 3). A demand that carries
   its due time or importance is what lets a controller rank C's repairs above its campaign. The
   commands to act on it already exist. This belongs with the mission system's design rather than
   in the kernel now.
4. **Assignments for controllers** (harness). `ControllerContext.Order` should take the same
   `assign` list a script does, so a controller can schedule the way the manual run did. That is
   small, and the combined run above shows why it matters.
5. **D4 and K7, recovery.** No run needed to clear an unwanted intermediate, so the experiment
   neither demands nor rules out recovery. Factory Alpha's buffer did fill (peak 1,000‰ in B and
   C), and recovery is still the basic path the design says must survive.

## Not done

- **No manual scheduling on C.** Scheduling C by hand is tuning on C, and C is held out.
- **Factory assignment was searched by hand, not exhaustively.** Four variants were tried after
  the Reactor Beta move. A better one may exist; the trade-off it would have to beat is recorded
  above.
- **Why the improved controller's restocks run long when the hardening is moved** was not traced.
  The run is reproducible (`e1-a-manual.json improved`), and that question belongs to follow-up 4.

## How to reproduce

```bash
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-a-manual.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-b-manual.json
dotnet run --project tools/Dimenship.Replay -- dimenship/content tools/Dimenship.Replay/scripts/e1-a-manual.json improved
```

| Script | Policy | Final state SHA-256 |
| :--- | :--- | :--- |
| `e1-a-manual.json` | queue order | `efa881a324af9c01d9e285b8aa5f8012121702590c881a8eb57d7ec7b58b68ed` |
| `e1-b-manual.json` | queue order | `22978fee08afe7ff572cc135dda008ffcc7bc707bbdadde006897651778d4e53` |
| `e1-a-manual.json` | improved | `1b902b829596c023e10901cb35e58c4b486fa23b5bd116889238db5c0b9ec9dd` |

All twelve E2 hashes were rerun and are unchanged.
