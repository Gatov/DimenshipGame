
# Production Scheduling and Automation — Gameplay Design

**Date:** 2026-09-23  
**Status:** Draft — exploratory

## 1. Purpose and Desired Experience

Make production reward manual optimisation and player-written automation across sessions, with two reactors and a few factories. Depth comes from competing uses, scarce space and material, and changing demand.

The player observes jobs, allocates capacity, runs the simulation, understands consequences, and improves a reusable policy. Programming develops from replenishment toward coordinating dependencies, anticipating demand, and balancing urgency against efficiency. Manual play and programs use the same controls and information.

Success means explaining a policy’s result and carrying an improvement into another situation. High utilisation alone is insufficient: efficiently producing the wrong item can delay the goal.

### Source Material and Authority

- **Game Design v0.9**, especially §§4–5.10: vessel scale, production, recovery, and equipment.
- **Planning and Task Execution:** executor queues, partial execution, and changeovers.
- **Programming and Automation:** controller progression, conflicts, and feedback.
- **Conveyor Belt Design** and **Editable Production Plans:** current execution and planning boundaries.
- **Production gameplay exploration of 2026-09-22.**

This draft proposes changing the GDD’s central-storage model, which permits ordinary components in Resource Storage and routes production through it. It does not supersede the GDD or authorise implementation.

## 2. Current Baseline

Committing a plan immediately appends all its tasks to assigned executor queues. Runtime scheduling visits transport lines, commissions facilities, then visits production facilities in fixed world order. Plans group tasks and track completion; their display order is not execution priority.

A factory continues its active batch, then prefers its current runnable task, then runnable work matching its configured recipe, then queue order. A line prefers its current loadable transfer, then queue order. Missing inputs can allow another queued task to run; an active production batch or cargo unable to unload can retain the executor.

Planning accounts for outstanding production inputs and expected outputs, but does not give a plan ownership of stock. Runtime withdrawals and power allocation follow executor order. There is no implemented player-controlled priority, fairness, or material-allocation policy.

Most ordinary recipes take 16 ticks; recipe changes cost 30 ticks. The component–module–frame chain is deliberately balanced. The planner routes production through central storage. These provide an execution foundation, but little changing competition. The programming interface is a concept editor; executable vessel controllers remain future work.

## 3. Mechanic Decisions

Proposed core identifies the preferred direction to test, not an approved implementation contract. Candidate mechanics need evidence; deferred alternatives are excluded from this exploration.

| Mechanic | Status | Player decision created | Constraint or open issue |
|---|---|---|---|
| Short fixed runs, expensive changeovers | Proposed core | Continue efficient production or switch for another objective. | Define setup identity; avoid charging merely for a different order ID. |
| Few common intermediates, local storage only | Proposed core | When to start work and where to allocate components. | Direct delivery, location-aware planning, and recovery from full buffers are required. |
| Products revisit facilities | Proposed core | Allocate reactors between material production and treatment; factories between starting and finishing. | Use selected chains with reachable stages and valid routes. |
| Specialisation through upgrades | Proposed core | Invest in throughput, flexibility, efficiency, or buffering. | Advantages need opportunity costs rather than an eventual best-at-everything machine. |
| Changing, competing demands | Proposed core | Balance repairs, expedition readiness, and future capacity. | Different situations must reward different policies. |
| Predictable temporary capacity changes | Candidate | Prepare for launches, upgrades, and other vessel activities. | Changes should be understandable and forecastable. |
| Expensive broadly substitutable material | Candidate | Spend prepared material to shorten an urgent production path. | Limit substitutions and decide stockpiling rules. |
| Direction-switching transport | Candidate | Group outgoing supplies and returning products. | Adds a second changeover problem; cargo already travelling needs clear treatment. |
| Additional bottleneck facility | Deferred | Schedule access to another shared processing resource. | First establish whether reactor revisits provide sufficient competition. |
| Reusable tooling or fixtures | Deferred | Reserve, position, and release scarce equipment. | Alternative to another bottleneck facility; adds ownership and movement rules. |
| Special fuel for advanced upgrades | Deferred | Trade a supply requirement for production advantages. | Start with time and energy trade-offs before adding another resource chain. |
| Separate variable-batch system | Dropped as a requirement | — | Grouping fixed runs between costly changeovers supplies the intended trade-off. |
| Forced facility specialisation | Dropped | — | Roles should emerge from player-selected upgrades and assignments. |
| Player-operated logistics hubs | Dropped for now | — | Keep the focus on production scheduling and allocation. |

## 4. How the Mechanics Work Together

### Production Campaigns and Setup

Production uses short fixed runs, with a final remainder where supported. The amount produced before a changeover emerges from how long the player keeps compatible work running. No separate batch-size control is required. Exact remainder handling remains an implementation decision.

The proposed setup cost follows recipe configuration: two orders for the same part can run consecutively without reconfiguration. Switching to another process costs significant time. Priority must be able to override setup preference at a safe boundary; otherwise an urgent order cannot interrupt a continuously supplied task. Inputs and work already committed to a batch remain accounted for, and cargo in transit remains physically present.

### Local Intermediates and Facility Revisits

A few common components serve several products. Keeping them outside central storage commits local space and material before a useful final outcome exists.

An illustrative chain is:

```text
Raw material → Factory: form plate → Reactor: harden plate → Factory: finish assembly
```

Reactors choose between making material and treating products; factories choose between starting work and finishing products that release space. Changeovers, transfers, and local capacity link these decisions.

Transport should deliver between appropriate local buffers over fixed, automatic routes. A component restriction cannot work with the current assumption that every intermediate returns to central storage. Existing components must be located and allocated before new production is ordered. Buffers must also support recovery from an unwanted allocation; moving, dismantling, or discarding work are options whose rules remain open. Fitted equipment and common components retain distinct storage rules and terminology.

### Upgrades and Optional Shortcuts

Upgrades can create:

- A fast machine with slow changeovers.
- A flexible machine with moderate throughput.
- An economical machine with more buffer space.
- A machine with more power draw but higher output.

Throughput, power draw per tick, and total energy per product are separate balancing dimensions. Limited slots, exclusive branches, or investment costs should preserve meaningful choices.

A reactor-produced advanced material could replace selected intermediate stages through explicit alternative recipes. Its purpose would be to reward expensive preparation investment, not to eliminate ordinary production or bypass progression-critical material requirements. Producing it during an existing reactor shortage may be a poor choice, while holding a limited reserve could be valuable. Its scope, production cost, and storage treatment need to be considered together.

## 5. Player and Agent Control

Both interfaces need the same bounded commands and explanations. These proposed capabilities leave APIs and programming language open.

| Observe | Decide or act | Required feedback |
|---|---|---|
| Outstanding objectives, jobs, and prerequisites | Add or amend demand; change priority | Which outcome and prerequisite work were affected. |
| Stock by location, allocations, incoming cargo, and expected output | Allocate material or protect a reserve | Who can use a quantity and why another job cannot. |
| Configurations, capabilities, queues, and setup costs | Assign eligible work; choose when to switch | Expected delay and the reason one job was selected. |
| Local occupancy and unfinished work | Hold or release work | Which space or downstream condition prevents progress. |
| Power availability and known capacity changes | Prepare or defer production | What constraint applies and when it changes. |

An agent must distinguish desired stock from demand already covered by production or transfers. Repeated scans must not create duplicate jobs. Promoting an objective must meaningfully affect its prerequisites and access inputs; changing only its final assembly task is inadequate. Shared prerequisites require an explicit allocation policy rather than indiscriminate promotion.

Manual overrides, competing program commands, tie-breaking, and starvation treatment must be deterministic and visible. Priority changes must respect active batches and in-flight cargo. Useful reports name causes and consequences; for example, a repair waited because its components were allocated to an upgrade. Reports should distinguish queue waiting from physical inability to run, and expose repeated configuration changes or excessive unfinished work.

## 6. Worked Situations and Progression

These are proposed experiments, not measured outcomes or implemented mission behaviour.

### A. Sustained Preparation

The vessel needs equipment for several future expeditions, with no immediate departure pressure. Two reactors and three factories share components and treatment work. A controller groups compatible orders, uses the machine upgraded for throughput, and releases enough intermediate work to keep downstream stages supplied without filling their buffers.

A policy that immediately responds to every low-stock reading repeatedly changes recipes. A better policy counts incoming supply and outstanding jobs, tolerates temporary shortages, and continues a useful production campaign while space and demand justify it. Compare useful equipment completed, changeover time, occupied buffers, and material committed to unfinished work.

### B. Urgent Completion During Expansion

A factory is producing components for an upgrade. Plates for expedition equipment are waiting for reactor treatment; that reactor is making material for the upgrade. Local space is limited, and delaying expedition readiness has a visible opportunity cost.

Raising only the equipment’s final-task priority changes little. An improved controller recognises the missing treatment, assigns material to the expedition, holds unnecessary component work, and accepts a reactor changeover. Finishing the equipment releases space; the upgrade can then resume. An upgraded flexible factory may justify a different assignment from situation A.

The player’s learning progression is:

1. Replenishment.
2. Accounting for outstanding work.
3. Grouping compatible jobs.
4. Following dependencies.
5. Limiting unfinished work.
6. Anticipating demand and capacity changes.

Introduce these pressures gradually. New situations should expose assumptions in existing controllers while preserving useful earlier logic.

## 7. Open Decisions and Validation

Resolve these before turning the direction into an implementation contract:

1. **Setup identity and interruption:** Is setup identity based on a recipe, process family, or individual job? At what boundary can priority change selection, and how does final-remainder execution consume inputs and energy?
2. **Storage and routes:** Which intermediates are excluded from central storage, what can each local buffer accept, and which factory–reactor connections exist? Record the resulting GDD changes.
3. **Demand and allocation:** How do objectives share production, inherit urgency, and claim material? How are claims changed or released when work is held, completed, or cancelled?
4. **Recovery:** How can unwanted intermediates be cleared without losing track of consumed material, active runs, or cargo? Preserve the vessel’s basic recovery path.
5. **Progression:** Which upgrade branches remain distinct, and which demand changes create new reasoning rather than simply larger quantities?
6. **Candidates:** When do advanced material, transport reversal, or another shared resource improve decisions enough to justify their additional rules?

The main implementation implications are location-aware planning, runtime links between objectives and prerequisites, controllable scheduling and allocation, and telemetry for decisions. These extend the existing executor model; their data structures and migration belong in a later plan.

First test the core combination on the existing small vessel: costly changeovers, restricted intermediates, selected facility revisits, and competing demands. Scripted demand events can stand in for a complete mission system. Keep candidate mechanics out of the initial comparison so their contribution can be assessed separately.

Replay identical states and events with:

- Basic queue order.
- Manual scheduling.
- Simple replenishment.
- An improved controller.

Compare readiness time, useful completions, material and space tied up, changeover cost, and manual interventions. Include situations not used for tuning.

Revisit the design if one obvious static policy wins everywhere, hidden executor order decides success, or progress requires repetitive transfer handling. Session depth remains a playtesting question.

