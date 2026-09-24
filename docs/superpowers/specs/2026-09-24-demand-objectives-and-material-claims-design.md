# Demand, Objectives and Material Claims — Design

Date: 2026-09-24
Status: Decided (D3)

## Goal

Answer the scheduling design's §7.3 before any kernel code is written against it:

1. **Objective.** What an objective is at runtime, and how tasks relate to it.
2. **Urgency.** How promoting an objective reaches its prerequisites, when promoting only its
   final assembly task is explicitly inadequate.
3. **Claims.** How material is claimed, and how a claim changes or is released on hold, completion
   and cancel.
4. **Arbitration.** The deterministic tie-break at every point where two requests compete, and the
   starvation rule. Neither may be executor declaration order in disguise.
5. **Coverage.** How a player or an agent tells desired stock apart from demand already covered,
   so that repeated scans never create duplicate jobs.

The output is this document. It gates K6a (priority, hold and membership on committed plans), K6b
(the claims ledger), K6c (hold, release, cancel and amend) and K8 (selection and waiting
explanations). It changes no code, no content, no save format and no GDD text.

## Source material

[Issue #47](https://github.com/Gatov/DimenshipGame/issues/47), ticket D3 of
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`:

> What is an objective at runtime, and how do tasks relate to it? How does urgency pass to
> prerequisites? The design is explicit that promoting only the final assembly task is inadequate.
> How is material claimed, and how do claims change or release on hold, completion and cancel?
> What are the deterministic tie-break and the starvation rule? They must not be executor
> declaration order in disguise, because the design names "hidden executor order decides success"
> as a reason to revisit the direction. How does an agent tell desired stock apart from demand
> already covered, so repeated scans never create duplicate jobs?

`docs/production scheduling and automation - gameplay design.md`, §5:

> An agent must distinguish desired stock from demand already covered by production or transfers.
> Repeated scans must not create duplicate jobs. Promoting an objective must meaningfully affect its
> prerequisites and access inputs; changing only its final assembly task is inadequate. Shared
> prerequisites require an explicit allocation policy rather than indiscriminate promotion.
>
> Manual overrides, competing program commands, tie-breaking, and starvation treatment must be
> deterministic and visible. Priority changes must respect active batches and in-flight cargo.
> Useful reports name causes and consequences; for example, a repair waited because its components
> were allocated to an upgrade.

The §5 control table's rows *Add or amend demand; change priority*, *Allocate material or protect a
reserve* and *Hold or release work*, and §6 situation B, where "an improved controller recognises
the missing treatment, assigns material to the expedition, holds unnecessary component work, and
accepts a reactor changeover".

`docs/Dimenship Programming v0.1.md` §2 (a program's priority is "numeric or tiered") and its
conflict report ("Reactor reserve rule won due to Critical priority … Quest item delayed by
00:18:40"): a conflict is resolved by a stated rule and then reported, not hidden.

`docs/superpowers/specs/2026-09-24-setup-identity-and-interruption-boundary-design.md` (D1), which
already decides priority *on one executor* and defers everything that crosses executors to this
document. `docs/superpowers/specs/2026-09-16-editable-production-plans-design.md` for the draft,
`RequirementKey`, merge at flatten and the rule that committed tasks are never edited by a draft.
`docs/superpowers/specs/2026-09-04-conveyor-belt-design.md` for cargo in flight.
`docs/superpowers/specs/2026-09-24-storage-topology-and-direct-routes-design.md` (D2) for the
workpiece tier and location-aware planning.

## What the vessel does today

- **A committed plan already groups a goal's tasks.** `SimulationEngine.Commit` enqueues every
  flattened task and records a `CommittedPlan` (`PlanId`, `Goal`, `Destination`,
  `CommittedAtTick`, `SpawnedTasks`, `CompletedTasks`, `State`). `PlanRegistry.Owning(task)` finds
  a task's plan by scanning. A task queued by hand belongs to none. `PlanState.Abandoned` is
  declared and nothing sets it.
- **Every task of a plan came from that plan's own expansion.** The planner walks the whole chain
  backwards and emits every prerequisite as a task of the same plan: the reactor run, each haul, the
  factory run. Legs merge at flatten *within* one draft, never across plans. No task is ever shared
  by two plans.
- **Coverage is already counted, vessel-wide.** `IWorldView.Uncommitted(item)` is everything aboard,
  less what unstarted committed runs will consume, plus what committed runs will produce. A standing
  order counts only its run in flight. The draft's backward adjustment subtracts it, so planning one
  goal twice does not spend the same stock twice.
- **Nothing owns stock at runtime.** Withdrawals happen in three places: a line loading its tail
  (`TryLoad`, from the transfer's source), a facility starting a run (`StartRun`, from its own
  buffer), and commissioning (from the target's buffer). Each takes whatever is `Available`. When
  two lines load from Resource Storage for two plans and stock covers only one, **the line declared
  first wins**. That is the "hidden executor order" the design warns about.
- **Power is granted in facility order.** `AdvanceRun` charges each facility in turn and pauses the
  first one that no longer fits. When the vessel is short, the facility declared first runs.
- **"Reservation" already means space.** `RoomForDelivery` holds back the buffer volume a facility's
  next run needs. It is a reservation of room, not of material, and this document does not change
  it.
- **Hold and cancel don't exist.** There is no command that pauses a plan, removes its work,
  or changes its goal.

## Vocabulary

| Word | Meaning here | Why not the obvious alternative |
| :--- | :--- | :--- |
| **Plan** (committed plan) | The runtime objective: a goal the player or a controller committed, the tasks it spawned, its priority and whether it is held. | *Objective* is the design's word, but the GDD uses it for story and case objectives (§8 "active story objectives", §2 "current case objectives"). A production goal is not a story objective, and a later case objective may *spawn* plans, so the two must stay distinct. |
| **Priority** | One of four ordered tiers: `Low`, `Normal` (default), `High`, `Critical`. Higher outranks lower. | D1's "an integer" still holds. The tiers are integers with names, bounded so that two competing controllers cannot escalate past each other one point at a time. The names are the programming doc's own. |
| **Claim** | Material at one storage that is set aside for one plan's unstarted withdrawals there. | Not *reservation*: in the kernel that already means buffer room held for a facility's next run (`RoomForDelivery`). The GDD's "resource reservation" in §5.8 is what a claim delivers. |
| **Held** (of stock) | The quantity of a claim that is physically present and set aside. | — |
| **Free** (of stock) | Stock at a storage that no claim holds. | — |
| **Coverage** | Stock plus committed finite incoming work, less committed unstarted consumption. `Uncommitted`, per location once K5a lands. | Not "stock": stock alone is what makes a replenishment loop order the same thing every scan. |
| **Hold / release** | Stop and resume a plan's work. The design's and plan's words. | *Release* is never used for claims, to keep "release the work" and "give up the material" apart. |
| **Relinquish / reassign** | Return a plan's held stock to free, or move it to another plan. | — |

A plan being *held* and stock being *held* are different uses of one word. The spec keeps both
because both are established (the design's "hold work" and the ordinary sense of holding stock),
and it always writes "a held plan" or "held stock", never "held" alone.

## Decisions

This is one decision document with two halves that gate separately. Decisions 1–4 (objective,
urgency, coverage, arbitration and starvation) gate K6a and K8's selection half. Decisions 5–7
(claims) gate K6b, K6c and K8's claim half. They are one document, not two issues, because a claim
is keyed by the plan (Decision 1), and the starvation rule (Decision 4) is not complete until
held stock (Decision 5) is in it. Deciding claims first would have to guess the key, and deciding
objectives first would leave "promotion reaches access to inputs" half answered.

### 1. The objective is the committed plan, and the kernel adds no new entity

`CommittedPlan` becomes the runtime objective. It gains a **priority** and a **held** flag. It
already has an id, a goal, a state and the list of tasks it spawned, and it is already saved. The
ticket name "objectives as a runtime entity" is met by extending it, not by a parallel `Objective`
that owns plans.

- **Membership is the plan's list, and nothing else.** A task belongs to at most one plan, the
  one whose `SpawnedTasks` names it. The task does not store its plan: the engine rebuilds the
  task → plan index from the registry at construction and never saves it, like every other index
  (CLAUDE.md, *Dictionaries built in the engine constructor are indexes rebuilt from state*).
  Storing the id on the task as well would be two answers to one question.
- **A plan's tasks have no priority of their own.** A plan task's effective priority is its plan's,
  read live. Changing a plan's priority changes all of its tasks in the same tick, at every stage
  of the chain. A task queued by hand, with no plan, keeps the per-task priority K2 gives it. That is
  the only place a task carries one.
- **There is no per-task override inside a plan.** It would be exactly the design's inadequate
  promotion: raise the final assembly and leave its prerequisites behind at the old priority. It
  also creates a priority inversion inside one objective, where an urgent task waits on a
  prerequisite that its own plan ranks lower. A player who wants part of a chain to be urgent
  commits that part as its own plan.
- **Hold is plan-level for the same reason** (Decision 6). Holding one task of a plan would leave
  the rest of the chain holding stock for work that cannot finish.

*Rejected: a separate `Objective` entity owning one or more plans.* It would be a second grouping
of the same tasks. Every question it could answer ("how far along is it", "how urgent is it")
already has one owner in the plan, and the day the two disagreed nothing could say which was right.
If a case objective later needs several plans, it references their ids, the way
`Mission.ForPlan` already references one.

### 2. Urgency reaches prerequisites because they belong to the plan

The design's requirement, "promoting an objective must meaningfully affect its prerequisites and
access inputs", is met by three effects of one plan priority. None needs a propagation pass.

1. **Every prerequisite task is promoted.** The planner already made every stage a task of the
   plan (the reactor run, each haul, each factory run), and Decision 1 makes them read its
   priority. Promotion therefore reaches the reactor producing the plan's metals and the line
   hauling them, not only the factory at the end. At each executor, D1's selection rule does the
   rest: highest ready priority first, at a run boundary, never mid-run, and cargo on a belt is
   never touched.
2. **The plan gets first call on new stock.** Free stock goes to outstanding claims by priority
   (Decision 5), so a promoted plan receives the next metals any source delivers ahead of a
   lower-priority plan that is waiting for the same item.
3. **The plan gets first call on power.** When the vessel cannot power every run this tick, charges
   are granted by priority (Decision 4).

**Promotion never crosses plans.** If plan B counted, when it was planned, on surplus output of plan
A's committed work, promoting B does not promote A. Inheriting priority across plans is the design's
"indiscriminate promotion": it would raise the whole of A's chain to deliver a remainder. The
explicit policies are (a) promote A, (b) amend B so it plans its own supply (Decision 7), or
(c) reassign stock A already holds (Decision 5). All three are commands, and all three show up in
the report.

**Promotion never takes stock another plan already holds.** Priority decides the future, meaning
who gets stock that has not been allocated yet. What has already been allocated moves only by a
command. That is "an explicit allocation policy rather than indiscriminate promotion": situation
B's controller *assigns material to the expedition*, and raising the expedition's priority alone
does not do it.

### 3. Coverage is arithmetic, and held work still covers

A controller or a player maintaining desired stock orders `desired − coverage`, and commits only a
positive result:

```
coverage(item [, place]) = on hand
                         + finite committed incoming work   (runs yet to deposit, transfers yet to deliver)
                         − finite committed unstarted consumption (runs yet to start, transfers yet to load)
```

Vessel-wide, that is today's `IWorldView.Uncommitted` unchanged. Per place, it is K5a's
stock-by-location reading with the same terms restricted to one storage. A second scan cannot
create a duplicate job, because `Commit` changes the world synchronously and the first scan's tasks
are already in the second scan's coverage, whether the second scan comes in the same tick or a later
one. The rules that make this hold:

- **A standing order is not coverage.** It counts only its run in flight, as today. A replenishment
  loop that uses a standing order and then reads a deficit has read the truth: a standing order
  commits to no quantity.
- **A held plan still covers.** Holding a plan pauses its work but does not withdraw its demand.
  If held work stopped counting, every hold would read as a shortage, and a replenishment loop would
  order a duplicate of every plan the player paused. That is the bug this decision exists to
  prevent.
- **A cancelled plan stops covering** at the moment of cancel, except for the work it cannot take
  back: a run in progress and cargo already aboard (Decision 7). Their output still arrives, so it
  still counts.
- **Surplus from whole runs counts.** D1's rounding lands as ordinary stock, and the next scan reads
  it.
- **Claims do not change coverage.** Every claim backs a withdrawal that coverage already subtracts
  (Decision 5), so subtracting held stock as well would count it twice. That is why K6b changes no
  planning arithmetic.
- **An unplannable shortfall is not coverage.** A goal nothing can make stays uncovered, and a loop
  that re-orders it every scan gets the same `Unplannable` report every scan. That is honest, and K8
  makes it visible.

*Rejected: keyed demand*, where a controller names each demand with a key and `Commit` with a key
already in use amends that plan instead of adding one. It guards against a controller that miscounts
by hiding the miscount, and the design wants the miscount to be the lesson ("a better policy counts
incoming supply and outstanding jobs", situation A). Identity for amending is the `PlanId` that
`Commit` returns (Decision 7).

### 4. Arbitration: priority, then oldest; starvation is visible, never corrected silently

Every point where two requests compete now has a stated order. None of them falls back to executor
declaration order to choose between two plans:

| Where requests compete | Today | Decided order | Ticket |
| :--- | :--- | :--- | :--- |
| Which task a facility runs next | Current, configured, queue order | D1: highest ready priority; within it current, configured, queue order | K2 |
| Which transfer a line loads next | Current, queue order | Highest ready priority; within it current, queue order | K2 |
| Who receives free stock at a storage | Whichever line or run withdraws first, in visit order | Priority, then **plan commit order** (`PlanId`) | K6b |
| Stock already held for a plan | — | That plan only, until a command moves it | K6b |
| Power, when the vessel is short this tick | Facility visit order | Priority, then **task id** | K6a |
| Free stock taken by work with no plan | Visit order | Visit order, unchanged | — |
| Buffer room | Visit order and the delivery reservation | Unchanged (see *Not built*) | — |

**The tie-break is age, not position.** Plan ids and task ids are minted in commit order, and a
plan's tasks are minted consecutively, so "task id" and "plan id, then place in the plan" give the
same order. Oldest request first is a rule the player can see (the Operations list is in commit
order) and control (commit later, or raise priority). Executor declaration order is neither
visible nor controllable. It is the fixed order of cards on the schematic, and it settled every
contested unit of stock and power the day the content was written.

**Power by priority.** The production phase keeps its shape, with one change. It selects work and
computes each active run's charge for the tick in facility order, as today. It then grants charges
greedily in (effective priority descending, task id ascending) order, skipping a charge that no
longer fits exactly as today's loop does. Finally it advances each run in facility order with the
grants decided. Advancing in facility order keeps the event journal's order, so on a tick with
enough power the result is byte-identical to today's. On a starved tick it differs, and that
difference is the point.

**The one visit order that stays.** Free stock taken by work with no plan, such as a hand-queued
haul or the extractor's standing transfer, is still first come in visit order. That work has
declared no demand, and nothing a plan asked for is decided by it: by Decision 5's invariant, free
stock exists only when every eligible claim at that storage is fully held.

**Starvation rule: nothing is starved except by an explicit instruction, and every starvation is
visible.** No priority ages automatically. Work can wait indefinitely in exactly these cases, each
of which traces back to a command:

- behind a strictly higher priority, which is what the player said priority means;
- while its plan is held;
- for stock another plan holds, which moves only by reassign, relinquish or cancel;
- at equal priority, behind D1's setup preference for a continuously ready configured task. That is
  the campaign mechanic ("how long the player keeps compatible work running"), and a raised priority
  or a hold ends it.

At equal priority, claims are served oldest plan first, and plans are finite, so equal-priority
claims cannot starve each other.

*Rejected: aging*, where effective priority rises with time spent waiting. It makes one command
mean different things at different moments. A background plan left long enough would outrank the
urgent plan the player committed after it, and a controller written against priorities would find
them drifting under it. The design asks for a policy the player can explain; a rule that silently
reorders their work is the hidden decider it tells us to remove. What the design asks of starvation
treatment is that it be *deterministic and visible*, and K8 makes it visible:

- A task that was ready and not selected records **`Outranked`** (K2's appended reason) and names
  the task that was chosen instead. K8 derives the cause by comparing the two: *higher priority*, or
  *setup preference* at equal priority.
- A task that could not take stock because another plan holds it records **`MaterialClaimed`**
  (appended by K6b after K2's reason) and names the holding plan. "The repair waited because its
  components were claimed by the upgrade" is this reason, rendered.
- M1's waiting-versus-blocked counters split this waiting from physical inability to run. A plan
  that has had ready work passed over, and made no progress, for longer than a threshold raises a
  Warning alert naming the plan it is waiting behind. The threshold is tuning (see *Open items*).

### 5. A claim is what a plan's unstarted withdrawals still need, held against arrivals

**Shape.** Claims are keyed `(plan, storage, item)`. The quantity a plan *needs* at a storage is
derived from its tasks, and only the part that is *held* is state:

```
need(P, S, X)        = Σ over P's unfinished runs at a facility whose buffer is S:
                           (runs not yet started) × input quantity of X
                     + Σ over P's transfers of X from S: requested − loaded
inbound(P, S, X)     = Σ over P's runs depositing X into S: (runs not yet deposited) × output quantity
                     + Σ over P's transfers of X to S: requested − delivered
outstanding(P, S, X) = max(0, need − held − inbound)
```

The claims ledger stores `held(P, S, X)` and nothing else. Need and inbound are read from the
plan's tasks, the same way `Uncommitted` reads them, so a claim can never disagree with the work it
backs. A standing order has no finite need and claims nothing. A task with no plan claims nothing.
Counting the plan's own inbound work is what stops a plan from holding a second copy of material
its own haul is already bringing.

**Held stock belongs to its plan.** At every storage, `free = available − Σ held`. A withdrawal by
a task of plan P draws from `held(P)` first, then from free. It never draws another plan's held
stock. A task with no plan draws from free only. Commissioning draws from free only. No plan
claims a construction unit at its target (the assembly root flattens to no task), so in practice
this changes nothing there. A task that finds stock present but held by others records
`MaterialClaimed` (Decision 4).

**Arrivals.** Stock enters a storage in three ways: a belt unloads, a run deposits, or a load seeds
it. Each arrival is allocated in two steps.

1. **Cargo keeps its owner.** A delivery or deposit made by plan P's task is held for P, up to
   `need(P, S, X) − held(P, S, X)`. Material a plan produced or hauled for itself arrives as its
   own. A higher-priority plan does not take it at the destination.
2. **Anything left is free, and free stock is offered to outstanding claims** at that storage and
   item. The order is priority descending, then plan id ascending, skipping held plans. Each claim
   takes `min(outstanding, free)`.

**The invariant**, checked after every tick in tests: *at every storage and item, either no stock
is free, or no eligible (not held) plan has an outstanding claim there.* It is re-established at
every change that can break it. Those changes are an arrival, a commit, an amend, a relinquish, a
cancel and the release of a held plan. The plan's commit is where it first claims stock already
aboard. Whatever the vessel has free when a plan commits goes to that plan's claims, because every
earlier plan was already fully held or had nothing outstanding there.

**Withdrawals shrink claims naturally.** When P's task withdraws from its held stock, `held`
falls by the amount and `need` falls with it, since that withdrawal is no longer unstarted. A
completed plan has zero need everywhere, so it holds nothing (Decision 7).

**Which enforcement points change:**

| Withdrawal | Today | With claims |
| :--- | :--- | :--- |
| `TryLoad` / `CanLoad` (a line picking up) | `Available(from, item)` | `held(P) + free`, or `free` for a task with no plan |
| `CanStart` / `StartRun` (a run's inputs) | `Available(buffer, item)` per input | Same substitution, per input |
| `CommissionFacilities` | `Available(buffer, unit)` | `free` |
| `Unload`, `TryDeposit`, seeding | Deposit | Deposit, then allocate per the two steps above |

`Available` itself keeps meaning *physically present*. The snapshot, the inspector and the fill
figures read it, and a claim does not make material disappear from the hold.

### 6. Priority changes the future; commands change the present

| Event | Held stock | Outstanding claims |
| :--- | :--- | :--- |
| **Priority raised or lowered** | Unchanged. Priority never moves stock already held. | Re-ranked for the next arrival. |
| **Plan held** | Kept. | Skipped by allocation while held. The plan's own cargo in flight still arrives held for it (step 1). |
| **Plan released** (resumed) | Kept. | Rejoin allocation, and are offered free stock immediately (the invariant). |
| **Relinquish** (P, storage, item, quantity) | That quantity becomes free and goes back through the allocation order, P included. | Unchanged. |
| **Reassign** (from P to Q, storage, item, quantity) | Moves from P to Q, up to Q's outstanding there. Any rest stays with P. | — |
| **Plan completes** | Zero, because need is zero. | None. |
| **Plan cancelled** | All released to free, then allocated in order. | Removed. |
| **Plan amended** | Kept up to the new need, and the rest released. | Recomputed from the new tasks. |

**Holding keeps claims.** A hold is temporary. If holding released claims, resuming would find the
material gone, and a hold-then-release pair would become a quiet way to move stock between plans.
That is the kind of allocation this document puts behind an explicit command. Situation B needs
both moves, and they are separate commands: *holds unnecessary component work* frees Factory
Alpha's time, and *assigns material to the expedition* moves stock.

**A held plan receives no new stock.** Otherwise a paused plan would keep absorbing every arrival it
had an outstanding claim on, and holding a large plan would starve every other plan of that item
while doing no work. Its own deliveries in flight are the exception, because that cargo was loaded
for it.

**Relinquish re-runs the order; reassign names the winner.** A relinquish returns stock to
allocation, which may give it straight back to P if P still ranks first. That is deliberate. The
command means "let the current order decide again", which is what a player wants after lowering
P's priority. Skipping P for the stock it just gave up would leave free stock beside P's
outstanding claim, which breaks Decision 5's invariant. A player who wants the stock to go to a
particular plan reassigns it, and a player who wants P to stop receiving stock holds P.

**Reassign is bounded by the receiver's outstanding claim.** Moving stock to a plan that does not
need it would create held stock with no withdrawal behind it. That is a hoard, which Decision 5's
shape cannot represent and must not be able to.

### 7. Hold, release, cancel and amend respect D1's boundary and the belt

All four are plan-level commands for plan tasks. The first three also apply to a task with no plan.
Each respects everything already physically committed. The table mirrors D1's.

| Executor state for the plan's task | Hold | Cancel |
| :--- | :--- | :--- |
| Run in progress, paused or not | Finishes and deposits, held for the plan. | Finishes and deposits. The output is free. |
| Run awaiting deposit | Deposits when room appears. | Deposits when room appears. The output is free. |
| Switch-over toward it | Completes. D1 Decision 4: a switch-over is abandoned only by a strictly higher priority, never because its target stopped being ready. | Completes, for the same reason. |
| Between runs, or queued | Not selectable. It postpones with `SafetyLock`, the existing reason for "the player, or a program, stopped it". | Removed: no further runs. |
| Transfer, cargo aboard | Arrives, held for the plan. | Arrives. The cargo is free. |
| Transfer, not yet loaded | Nothing more is loaded. It postpones with `SafetyLock`. | Nothing more is loaded. |

**Cancel is truncation, not deletion.** Each task of the plan is cut back to the work it has
already physically started. A production task keeps its completed runs plus the one in progress, if
any, and a transfer keeps what it has loaded. Every task then finishes by the ordinary completion
path, so no new task state is needed. A task with nothing started finishes at once. The plan's
state becomes `PlanState.Abandoned`, which is already declared. Its held stock is released. Output
and cargo that were already committed arrive as free stock, and they count toward coverage until
they land (Decision 3). No material is destroyed and none is created: everything that existed
still exists, in a buffer or on a belt.

**Release resumes.** A released plan's tasks are selectable again at each executor's next boundary,
and its claims rejoin allocation.

**Amend is cancel-and-replan under one id.** `Amend(plan, new goal quantity)` truncates the plan's
work exactly as cancel does, without releasing held stock yet. It then plans the new goal against
the live world through the ordinary draft path. Coverage now includes the plan's surviving work,
which is its runs in progress and cargo aboard. The new tasks are appended to the same plan, which
keeps its id, priority and held flag. Finally, held stock is trimmed to the new need, and the
surplus is released. Adjusting committed tasks in place was rejected: the editable-plans spec
says committed tasks count as supply or demand and are never edited by a draft, and amend keeps
that rule, not an exception to it.

## What changes, by ticket

**K2 (already specified by D1).** Unchanged, with one clarification: a plan lends its priority to
its tasks totally and live. K2 may store the value on each task. K6a then moves the source of
truth to the plan, and after that a task stores a priority only when it has no plan.

**K6a — priority, hold and membership on committed plans.**

- `CommittedPlan` gains `Priority` (default `Normal`) and `Held` (default false). `SpawnedTasks`
  becomes appendable, for amend in K6c. The task → plan index is built at construction and never
  saved.
- A plan task's effective priority is its plan's. Setting a priority on a plan task directly is
  refused, and the refusal names the plan.
- Power is granted by (priority, task id) and advanced in facility order (Decision 4). Test: a
  starved tick powers the higher-priority run, whichever facility is declared first. Test: a tick
  with enough power advances byte-identically to the code before K6a.
- Save: `Priority` and `Held` on the plan DTO, nullable, and saved by name. A plan task's saved
  priority is absent. A plan-less task's is present. A load that finds a plan task carrying its
  own priority, or a plan-less task without one, reports it.
- Default behaviour-neutral: a world where no plan's priority was changed, and none was held,
  advances byte-identically to the code before K6a on any tick with enough power. If the M3
  baseline ever starves, K6a re-records it and commits both reports.

**K6b — the claims ledger.**

- A `ClaimLedger` in `WorldState` holding `held(plan, storage, item)` only. Entries with zero
  held are not stored. Entries are saved sorted by plan, storage, then item.
- Need, inbound and outstanding are derived per Decision 5. Allocation on arrival, on commit and
  on relinquish follows Decision 5's two steps. The invariant is asserted after every tick in the
  kernel suite.
- The four enforcement points of Decision 5's table. `PostponeReason.MaterialClaimed` is appended
  after whatever K2 appends. A `ClaimAllocated` event (plan, storage, item, quantity) is appended to
  `EventCode`, for K8 and the console.
- `Relinquish` and `Reassign` as kernel commands. They reach the shell only through C0.
- Snapshot: held and free per storage and item, and held per plan. U2 reads these.
- Save: a held entry whose plan is not active, or whose total at a storage exceeds the stock there,
  or which exceeds its plan's need, is reported and never clamped. A clamp would be a vessel
  silently changing who owns its material across a load. An unknown storage or item is content
  drift, listing every reference, as for any other ledger.
- Planning arithmetic is unchanged (Decision 3). Test: two plans committed for the same scarce
  stock in Resource Storage, on lines declared in either order, deliver to the older plan first.
  Swapping the lines' declaration order changes nothing.
- K6b changes contested outcomes on purpose. It re-runs M3 and commits the diff as the measurement
  of the change itself.

**K6c — hold, release, cancel and amend.** The four commands of Decisions 6 and 7, with
`SafetyLock` as the hold reason and `PlanState.Abandoned` for cancel. Tests: a hold never stops a
run mid-run and never strands cargo; cancel destroys and creates no material, measured as the
vessel-wide total plus belt cargo before and after; a held plan still counts toward coverage, so a
replenishment scan commits nothing new; amend keeps the plan's id and priority.

**K8 — explanations.** `Outranked` names the chosen task, and K8 derives *higher priority* or
*setup preference*. `MaterialClaimed` names the holding plan. The waiting-plan alert of Decision 4
is a new `AlertCode`, appended, and the first alert anything in the kernel raises.
Test on the design's own example: a repair plan waiting on components claimed by an upgrade plan
reports that plan by id.

**C0 — the command surface.** It gains `SetPlanPriority`, `HoldPlan`, `ReleasePlan`, `CancelPlan`,
`AmendPlan`, `Relinquish` and `Reassign`. The shell and every future controller reach these through
the same entry point, as the plan's Decision 5 requires.

**Unchanged by all of them.** The planner's expansion, merge at flatten and line choice. `Uncommitted`.
D1's selection order within a priority. The belt's unload–advance–load order and the rule that
cargo aboard always arrives. The delivery reservation of buffer room. The workpiece acceptance rule.

## Acceptance criteria

- Promoting a plan promotes every task it spawned, at every stage, and nothing another plan owns.
- Scanning desired stock twice, in the same tick or across ticks, commits once. Holding a plan
  does not make the next scan order a duplicate.
- Contested stock and contested power are never decided by executor declaration order between
  two plans. Swapping two lines' or two facilities' declaration order changes no outcome for any
  plan.
- Stock held for a plan is withdrawn by that plan or moved by a command, and by nothing else,
  whatever the priorities.
- At every storage, free stock and an eligible outstanding claim never coexist at the end of a
  tick.
- Hold, cancel and amend never interrupt a run, never divert cargo aboard, and never destroy or
  create material.
- With every plan at default priority, none held, no claim contested and enough power, the
  shipped vessel advances byte-identically to the code before K6a and K6b.

## Not built

- **Reserves**: protecting stock with no plan behind it, the design's "protect a reserve" and the
  programming doc's "preserve ReactorFuel reserve ≥ 500". The ledger's shape admits one as a claim
  whose need is authored rather than derived, with its own owner id and priority. It would also have
  to reduce `Uncommitted`, because unlike a plan claim, it backs no withdrawal that coverage already
  subtracts. It lands with C0 when a controller needs it, together with its ordering against plans
  at equal priority.
- **Claims on buffer room.** Space is contested too: one plan's delivery can fill a buffer that
  another plan's delivery needs. It stays with today's delivery reservation. The E-phase decides
  whether space needs claims of its own.
- **Priority inheritance across plans** (Decision 2). Deliberately absent.
- **Keyed demand** (Decision 3). Deliberately absent.
- **Aging** (Decision 4). Deliberately absent.
- **Pre-emptive claiming for work not yet planned.** A plan claims only what its own tasks will
  withdraw.

## Open items

- **The waiting-plan alert threshold.** One operational hour (`Units.TicksPerHour`) is the obvious
  first value. It is tuning, measured against M3's scripted situations, and belongs to K8.
- **Whether a plan with no tasks should be recorded at all.** Today `Commit` records one. For a
  controller that re-scans an unplannable goal, that creates an empty plan per scan. Refusing to
  record it would reduce clutter, but it changes an existing contract, so K6a decides it with a
  test in either direction.
- **Whether the draft should flag reliance on a lower-priority plan's committed output** (Decision
  2's inversion). Aggregate `Uncommitted` cannot attribute a unit of coverage to the plan
  producing it. Once K5a's per-location reading exists, K8 can report it at runtime as a claim
  waiting on inbound work that belongs to another plan.
- **GDD vocabulary.** The GDD's §5.8 uses "reservation" for what this document calls a claim, and
  the kernel already uses "reservation" for buffer room. When K6b lands, Appendix A should gain
  **Claim**, and §5.8's "resource reservation" should read "material claims". That keeps the GDD
  the tiebreaker without the kernel renaming `RoomForDelivery`.
