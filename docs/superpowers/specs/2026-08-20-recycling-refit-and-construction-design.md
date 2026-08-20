# Recycling, Refit and Construction — Design

Date: 2026-08-20
Status: Draft

## Goal

Describe how a component is replaced on a robot, and how a facility is built or upgraded, **without
adding a single content type, task type or schematic field.** Both operations are plans composed of
the production and transport tasks that already exist.

The prize is that the two most-requested progression verbs — *upgrade my bot* and *upgrade my
vessel* — cost the kernel nothing new. The risk this document exists to prevent is the opposite: a
`RefitOrder` record, an `UpgradeTask` state machine and a `RecycleSchematic` tier arriving as three
parallel systems that each duplicate the queueing, postponement and energy accounting the production
model already got right.

## Source material

- **`docs/specs/dimenship-schematics.md` §2** — the `SchematicDefinition` record. This document adds
  no field to it.
- **`docs/specs/dimenship-planning-and-task-execution.md`** — planning versus tasks, partial
  execution, switch-over.
- **`docs/superpowers/specs/2026-07-30-production-planning-design.md`** — the executor/storage/task
  model everything below is composed from. Its open item *"Facility upgrades — the shape allows it,
  nothing implements it"* is the gap this document closes.
- **`docs/Game Design v0.9.md` §5.8** — the Factory "builds components, robot frames/modules,
  equipment and facility upgrades", and the material chain
  `MISSIONS -> DOCKS -> STORAGE -> MATTER REACTORS -> STORAGE -> FACTORIES`. v0.9 has no Robotics
  Bay node, so a robot is refitted **at a Factory**.
- **`docs/Game Design v0.9.md` §5.10** — added in v0.9.1 alongside this document, and **authoritative
  over it**. The GDD states the material model and the vocabulary: a module is an item in a slot,
  salvage returns a part's own build materials reduced by a recovery fraction, and a facility is
  upgraded in place by a Factory-built construction unit. This document states the mechanism only —
  which executor runs
  what, in which order, and how it stalls. Where the two appear to disagree the GDD wins, as it does
  everywhere else; this split exists so that a balance change to the material model never has to be
  made in two places.

## The load-bearing idea: an installed module is an item in a slot

Everything in this document follows from one reframing.

> **A robot's loadout and a facility's upgrade sockets are storages.** An installed module is not a
> property of the machine; it is an item sitting in a storage that belongs to that machine. A
> machine reads its effective work rate, energy efficiency, capacity and capability from **what is
> currently in its slots**.

Once that is true, *install* and *remove* stop being new verbs. They are `TransportTask`s, moving an
item between a slot storage and a facility's local storage — the same primitive that already moves
alloy from a hold to a factory buffer, with the same latency, the same `DestinationFull`
postponement and the same telemetry.

This is what makes refit emergent rather than merely *described as* emergent. The alternative — a
bespoke install step that reaches into a robot record and swaps a field — would have needed its own
duration model, its own failure states and its own save representation, and none of them would have
been reachable by a player program.

Two consequences fall out for free, and both are desirable:

- **Downtime is real and readable.** Between removing the old core and installing the new one, the
  slot is genuinely empty, so the machine genuinely runs at its unequipped rating. Nobody has to
  model "the bot is 60% upgraded".
- **A part exists somewhere at every instant.** The old core is in a slot, in transit, in a factory
  buffer, or consumed by a recycle run. There is no moment where it has been removed but does not
  yet exist as an item, which is exactly the moment a save would otherwise lose it.

### Why the transport line only sometimes exists

A robot's slot storage is reachable by transport **only while that robot is docked at the facility
doing the work.** This is the entire mechanical reason a bot must be recalled for a refit, and it
means the rule "a deployed robot cannot be reconfigured during a mission", asserted in the Mission
Visualization Proposal §5, is enforced by the topology rather than by a guard clause somebody can
forget to write.

## Recycling

Recycling a part built from `n` of A and `k` of B returns `n×p` of A and `k×p` of B, for a recovery
fraction `p < 1`. It returns the **actual materials**, proportionally, not a bulk substitute.

```text
recycle power_core_mk1              p = 500‰

Consumes: 1 Power Core Mk1
Returns:  6 Basic Metals            (from 12)
          3 Technical Materials     (from 6)
          1 Rare Metals             (from 2)
Facility: Matter Reactor
```

### Decision: a recycle is the build schematic run backwards, not a schematic of its own

Nothing about that return is new information. The build schematic already lists `12 Basic Metals,
6 Technical Materials, 2 Rare Metals`, and the return is that list scaled by `p`. **Authoring a
recycle schematic would be transcribing data the catalog already holds**, and the failure that
invites is the one the four-tier model exists to prevent: the day someone rebalances
`power_core_mk1`'s inputs and forgets its recycle twin, the vessel quietly becomes a material
source.

So a recycle task references the **build** schematic id and a direction. There is no recycle
schematic to author, none to keep in sync, and none to forget. Rebalancing a recipe rebalances its
salvage in the same edit, by construction.

This also makes the GDD's conservation invariant true by arithmetic rather than by vigilance. Output
is input × p with p < 1, per material, so no sequence of building and recycling yields more of
anything than went in, and expedition-exclusive materials come back only from parts that contained
them. **There is no abuse to balance against** — not a tuned discouragement, an arithmetic
impossibility.

Rejected on the way:

| Option | Why it was rejected |
| :--- | :--- |
| A recycle schematic per part | Duplicates the build schematic's inputs into a second record that can drift from it. The drift is silent and its symptom is an economy exploit. |
| Recycle into bulk Matter Mix, reactor separates it | Loses which materials the part contained. A cheap basic-metals part would yield mix that a processing mode could separate into Phase Materials — a phase source in two queue entries. Recoverable only by splitting Matter Mix into graded items, which is content surface bought to fix a problem this option created. |
| One recycle schematic per component-material pair | A five-material part becomes five schematics, five queue entries and five reconfigurations to salvage one core. |

### The engine change this does require, stated plainly

A run must be able to deposit **more than one** `ItemAmount`. That is the one thing in this document
that is not already true, and it is worth being explicit that it costs:

- the executor's end-of-run deposit path takes a list;
- `DestinationFull` gains the partial case, where some outputs fit and others do not. **The run
  holds all of them until all fit** — depositing what fits and dropping the rest would destroy
  material and violate conservation as surely as any exploit.

`SchematicDefinition` itself is untouched: forward runs still have exactly one output, and the
multi-amount deposit exists for the reverse direction. Schematics stay simple, which was the
starting constraint.

**Integer floor is where the loss actually lands.** `n × p` is integer division, so a part built with
1 Rare Metal at `p = 500‰` returns zero of it. That is correct and is the "portion might be lost"
made concrete: salvaging a part with trace amounts of something precious does not recover the trace.
It also means `p` is not the whole story for small quantities, and balance passes should read yields
from worked examples rather than from the percentage alone.

### Where `p` lives

Per build schematic, defaulting to a global constant. A per-schematic value lets phase-tier
components be made deliberately poor to recover, which §5.9 wants; a global default means the
overwhelming majority of content authors nothing. `p` is permille like every other ratio in the
kernel.

### Modules are not stored in MVP, and recycling is therefore not optional

A **module** — a weapon, a core, a sensor, anything that occupies a slot — has exactly three homes
in MVP: a machine's slot, a facility's buffer, or a transport in flight between them. It is never a
line in Resource Storage. This is narrower than it first sounds and it settles several questions at
once:

- **There is no spare-parts inventory.** Modules cannot be stockpiled, pre-built against a future
  refit, or kept as spares. A newly built module goes factory buffer → slot; a removed one goes
  slot → reactor buffer. Neither passes through storage.
- **A removed part has one destination.** An earlier draft of this document said recycling was
  optional and the old part could sit in storage as a spare. That is wrong for MVP: with nowhere to
  sit, removal and recycling are one flow, and the refit plan does not offer the choice.
- **Slot-occupying modules are the only thing this restricts.** Materials and components — metals,
  feedstock, control chips — are ordinary stored commodities and are unaffected.

This is worth having rather than merely tolerable. Resource Storage stays a materials ledger instead
of becoming an equipment manager; upgrade downtime is real, because a player cannot soften it by
having built the replacement in advance; and it sidesteps a modelling problem the storage tier is
not shaped for. Storage is `ItemId → long`, which holds a *quantity* of an interchangeable thing. A
stockpiled module that accumulates wear or damage would need per-instance identity, and adding that
to the ledger to support a spares box is a poor trade.

**What it defers:** moving a removed part onto a different robot, which is a reasonable expectation
and is post-MVP. It needs modules to be storable, which is the per-instance change above, not a
content edit.

**The transport holds and retries; nothing is dropped.** A module whose destination is not ready —
the reactor is mid-run, its input buffer is full — stays with the transport, which postpones and
retries until the destination frees. There is no fallback line, no overflow storage and no timeout,
because a module has no third place to be and inventing one would reintroduce exactly the spares box
this section removes.

This needs no new mechanism: `DestinationFull` already postpones and retries, and the transport
already carries its payload while postponed. The only thing worth stating is that waiting is the
*complete* answer here rather than a first attempt before some recovery path — for a module there is
no recovery path to fall through to, and that is deliberate.

Waiting is also bounded in practice. Nothing in the design destroys a facility or a robot, so a
destination that is busy becomes free; it does not disappear. A part in flight is therefore never
orphaned, and the invariant that a part is always somewhere the player can see holds without a
special case.

### The factory disassembles; the reactor purifies

The old part is not simply carried to a reactor intact. The Factory that removed it performs basic
disassembly as part of the removal, and what travels to the reactor is the broken-down part; the
reactor then purifies that into standardized materials. The reversed run of the previous section is
the purification step, and it belongs to the **Matter Reactor** — which resolves the reactor-vs-
factory question this document previously left open, and resolves it as *both, in sequence* rather
than as either.

Disassembly is a stated assumption rather than a modelled run: it adds no schematic, no intermediate
item id and no second queue entry. The part travels as itself. Modelling it as a real Factory run
would need an item to represent a broken-down core, and that item would exist only to be consumed
one step later.

The division also reads correctly against §5.8, which makes the reactor the separating tier. Pulling
a part apart is assembly work and belongs to the Factory; turning the results into clean standardized
resources is separation and belongs to the reactor. Each facility does the kind of work it already
does.

## Refit: replacing a component on a robot

A refit is a **plan**, not an object. The planner emits the ordered tasks below; `Commit` injects
them; the executors run them under the ordinary selection rules. No new state machine observes the
sequence, and there is no `RefitId` in the save.

Worked example — upgrading a robot from Power Core Mk1 to Mk2:

| # | Task | Kind | Executor | Note |
| :--- | :--- | :--- | :--- | :--- |
| 1 | Recall robot to Factory Alpha | Transport | Mission Dock / transport | Establishes the slot-storage route. Until it completes, tasks 2 and 6 postpone on `OutputRouteUnavailable`. |
| 2 | Move Power Core Mk1 — robot slot → factory buffer | Transport | Transport | The removal, and the assumed disassembly. Robot now runs unequipped. |
| 3 | Move Power Core Mk1 — factory buffer → reactor buffer | Transport | Transport | Not optional: a module has nowhere else to go. |
| 4 | Run `power_core_mk1` reversed ×1 | Production | Matter Reactor | The purification. Yields the part's inputs × `p` into the reactor's buffer. |
| 5 | Run `power_core_mk2` ×1 | Production | Factory Alpha | Ordinary production. Inputs arrive by ordinary transport. |
| 6 | Move Power Core Mk2 — factory buffer → robot slot | Transport | Transport | The install. Never via storage. |
| 7 | Release robot | Transport | Mission Dock | Robot is mission-capable again. |

Every row is a task type that exists today. Every row can postpone for a reason that exists today.
Every row appears in the event log under codes that exist today.

### What this buys, stated plainly

- **Partial execution works without being designed for.** Materials for the Mk2 core arrive while
  the Mk1 core is still being hauled to the reactor. The overlap is the ordinary batch behaviour of
  the production model, not a refit feature.
- **Energy accounting is already correct.** The recycle run and the build run charge proportionally
  to work done, and a brownout mid-refit postpones on `InsufficientEnergy` and resumes — it does not
  void a half-finished refit.
- **A busy or full destination is not a special case.** A task addressed to a storage that is not
  ready postpones on `DestinationFull` or `OutputRouteUnavailable` and retries, which is the same
  treatment every other task in the vessel gets. Nothing in the design destroys a facility or a
  robot, so an unready destination is always a temporarily unready one.
- **Cancellation is not a special case.** Cancelling a refit is cancelling its remaining tasks. The
  world is left in a coherent state by construction, because every intermediate state is "a part is
  in a storage".

### The cost admitted

The plan's steps are not atomic, so a player can cancel after removal and before install and leave a
robot with an empty socket. **This is correct and is not to be prevented.** A machine missing a part
runs at its unequipped rating, which is a legible consequence the SCADA schematic can show, and
guarding against it would require exactly the transactional refit object this design exists to
avoid.

## Construction and upgrade of facilities

A facility cannot be recalled to a factory. The GDD's fixed layout means it does not move at all, so
the robot pattern does not transfer — and the fix is not to invent a builder unit that walks.

### Decision: the Factory produces a construction unit; installing it is transport into a socket

A facility upgrade is authored as an ordinary Factory schematic whose output is a **specialized
construction unit** — a real item, in storage, with a hold cost.

```text
reactor_throughput_upgrade_mk1

Output: 1 Reactor Throughput Module Mk1
Inputs: 12 Basic Metals, 6 Technical Materials, 2 Rare Metals
Effort: 900 work units
Energy: 140 energy units
Facility: Factory
```

Commissioning it is a `TransportTask` delivering that module into the target facility's **upgrade
socket storage**. The socket is occupied; the facility reads its raised work rate from what the
socket holds.

This is the same rule as the robot slot, and that is the point: one sentence — *an installed module
is an item in a slot storage* — covers both machines, so the engine has one mechanism and the player
has one mental model.

### Why the cost is factory occupancy

The expensive part of an upgrade is the Factory being **busy** for the duration of a 900-effort run.
That single number is doing a lot of work:

- It is **visible on the schematic**, because factory utilization is already a node metric with a
  cause attached. "Factory Alpha 100%, current job: Reactor Throughput Module" is exactly the
  diagnostic §5.5 demands.
- It is **a real opportunity cost**, competing with armour plates and mission loadouts in the same
  queue under the same priorities. Upgrading the vessel means not building something else, which is
  the strategic tension the layer is for.
- It is **schedulable, pausable and programmable**, because it is a queue entry like any other.

A construction timer attached to the target facility would have delivered none of that. It would
have been a second scheduler running beside the real one, invisible to factory utilization, immune
to energy pressure, and unreachable by a player program.

### Facility downtime is content, not engine

Whether commissioning takes the target facility offline is expressible without new mechanism: author
the socket so that installing into it requires the facility idle, or accept a hot-swap. **Neither is
decided here** — it is a balance question, and the shape supports both. What is decided is that if
downtime exists it is a property of the socket, not a hard-coded rule in the engine.

### New facilities

Building a facility that does not yet exist is the same operation against a slot the scenario
already declares. The four-tier model requires that placement lives in the **Scenario** and is
retained rather than discarded — the scenario "holds every node slot the campaign will ever show,
including ones nothing has been built in yet". Construction therefore fills an authored empty slot;
it never creates geometry, and the schematic layout stays fixed as §5.3 requires.

## What is explicitly not being added

Recorded so a later reader does not assume an oversight:

- **No `RefitOrder`, `UpgradeTask` or `ConstructionTask` type.** A refit is a set of ordinary tasks
  and nothing tracks it as a unit.
- **No new field on `SchematicDefinition`.** Component schematics and upgrade schematics are
  structurally identical, and recycling reuses the build schematic rather than adding a record.
- **No multi-output *schematics*.** A forward run still produces one output. The multi-amount
  deposit exists only for the reverse direction; no authored recipe gains a second output.
- **No recycle schematic, and no `RecycleSchematicId`.** A recycle names a build schematic and a
  direction.
- **No new `TaskState` or `PostponeReason`.** Every way a refit can stall is a way a production or
  transport task can already stall.
- **No transactional guarantee across a plan.** Deliberate; see the admitted cost above.

## Open items

- **Do slot storages appear in the vessel-wide `Resources` roll-up?** An installed core is aboard,
  but counting it as available stock would let the planner allocate a part that is currently bolted
  into a working robot. The likely answer is that slot storages are excluded from the roll-up and
  from planner availability, but this needs stating before it is implemented.
- **Grouping for presentation.** The tasks are independent to the engine, but a player looking at a
  queue wants to see "Refit: Scout-2 power core" rather than seven unrelated rows. This is a
  presentation-layer correlation id at most, and must not become an engine concept.
- **Effective-stat resolution order.** When several modules occupy several sockets, how their
  modifiers combine is undefined here. It must be order-independent or ordered by declaration, per
  the determinism contract.
- **Does an upgrade socket accept only one module, or several?** Scale and progression pacing
  question, unanswered.
- **Recovery fractions.** `p` per tier is content and needs a balance pass read from worked yields,
  not from the percentage, because of the integer floor. This document fixes only where `p` lives.
- **Effort and energy of a reverse run.** The build schematic's `EffortPerRun` and `EnergyPerRun`
  describe assembly, and disassembly is usually cheaper. A second fraction against the build cost is
  the obvious answer and is not decided here.
- **Recycling a part built from sub-assemblies.** Reversing one level returns the components, not
  the raw materials inside them. Components *are* storable, so unlike modules they can simply sit
  there, and the question is whether the planner offers to cascade — reversing those in turn — or
  stops. Cascading multiplies `p` at each level, which compounds the loss quickly and may be the
  honest deterrent against deep salvage chains.
- **Whether a transport holding a module for a long time needs its own SCADA affordance.** The rule
  below makes waiting correct and bounded, but a part can be in flight for a while and the player
  should be able to find it. The transport line display may already be enough; this has not been
  checked against the panel.
