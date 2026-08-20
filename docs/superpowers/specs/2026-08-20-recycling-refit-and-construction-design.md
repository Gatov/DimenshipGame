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

Recycling is an ordinary schematic. It has an input and an output and a facility type, and the
loader, the planner, the queue and the energy model do not know it is special.

```text
recycle_power_core_mk1

Output: 40 Matter Mix
Inputs: 1 Power Core Mk1
Effort: 25 work units
Energy: 6 energy units
Facility: Matter Reactor
```

### Decision: a recycle schematic outputs Matter Mix, not the original materials

`SchematicDefinition.Output` is a single `ItemAmount`. A core built from basic metals, technical
materials and chemical feedstock cannot be un-built into three items by one schematic, and the two
ways around that are both worse than the third:

| Option | Why it was rejected |
| :--- | :--- |
| Give the schematic a list of outputs | A field on the record, a change to every executor deposit path, and a new `DestinationFull` case where *some* outputs fit. It buys nothing the third option does not. |
| One recycle schematic per component-material pair | Content explosion: a five-material component becomes five schematics, five queue entries and five reactor reconfigurations to salvage one part. |
| **Output Matter Mix** | Chosen. |

Recycling yields **Matter Mix** — the same bulk material an expedition brings home. The salvaged
core re-enters the chain at exactly the point raw cargo does, and the Matter Reactor separates it
under a processing mode the player already chooses.

This is not a workaround that happens to fit. It is the better model:

- **The loss is two-stage and both stages are legible.** The recycle schematic's output quantity is
  the fixed fraction the player was promised; the reactor's processing mode then governs what that
  bulk separates into. A player who wants rare metals back from a salvaged weapon sets the reactor
  accordingly and accepts the yield tradeoff §5.8 already describes.
- **It needs no new item.** Matter Mix exists, the reactor already consumes it, storage already
  holds it.
- **It composes.** Salvage recovered from a mission and salvage recovered from your own obsolete
  parts are the same substance, so one reactor queue serves both and no screen needs a second
  vocabulary for scrap.

The "fixed percentage of the material used to construct it" is therefore authored as the recycle
schematic's output quantity, tuned against the build schematic's inputs. It is a content number, not
an engine rule, so a designer can make phase-tier components deliberately unrecoverable by authoring
a poor return — or no recycle schematic at all.

### Recycling is optional

Nothing forces the old part into a reactor. It can sit in storage as a spare, be installed on a
different robot, or be sold or delivered if a later system wants that. The refit plan proposes the
recycle run because it is usually what the player wants; **the plan is editable before commit**,
consistent with the planner being pure and `Commit` being the only mutation.

## Refit: replacing a component on a robot

A refit is a **plan**, not an object. The planner emits the ordered tasks below; `Commit` injects
them; the executors run them under the ordinary selection rules. No new state machine observes the
sequence, and there is no `RefitId` in the save.

Worked example — upgrading a robot from Power Core Mk1 to Mk2:

| # | Task | Kind | Executor | Note |
| :--- | :--- | :--- | :--- | :--- |
| 1 | Recall robot to Factory Alpha | Transport | Mission Dock / transport | Establishes the slot-storage route. Until it completes, tasks 2 and 6 postpone on `OutputRouteUnavailable`. |
| 2 | Move Power Core Mk1 — robot slot → factory buffer | Transport | Transport | The removal. Robot now runs unequipped. |
| 3 | Move Power Core Mk1 — factory buffer → reactor buffer | Transport | Transport | Only if recycling. |
| 4 | Run `recycle_power_core_mk1` ×1 | Production | Matter Reactor | Yields Matter Mix into the reactor's buffer. |
| 5 | Run `power_core_mk2` ×1 | Production | Factory Alpha | Ordinary production. Inputs arrive by ordinary transport. |
| 6 | Move Power Core Mk2 — factory buffer → robot slot | Transport | Transport | The install. |
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
- **A destroyed or lost robot is not a special case.** Tasks addressed to a storage that no longer
  exists postpone on `OutputRouteUnavailable`, which is the same treatment as any other unreachable
  destination.
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
- **No new field on `SchematicDefinition`.** Recycle schematics, component schematics and upgrade
  schematics are structurally identical; only their content differs.
- **No multi-output schematics.** Rejected above, with the reason.
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
- **Recycle yields per tier.** The percentages are content and need a balance pass; this document
  fixes only where the number lives.
