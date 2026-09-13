# Vessel Construction Interface — Design

Date: 2026-09-13
Status: Draft

## Goal

Finish the verb the launch pad step started. A player selects an unbuilt slot on the schematic,
reads what would stand there and what it would cost, opens a prefilled Build plan, approves it, and
watches the schematic report the plan's actual phase until the facility activates in place.

The kernel half already works. `SimulationEngine.CommissionFacilities` builds a slot from a whole
construction unit sitting in its local storage; `ProductionPlan` carries a `Destination` so a plan
can end at that storage; `OperationsFocus` composes and commits one. What is missing is entirely
interface, and the previous spec said so in as many words:

> Inspector: an unbuilt executor shows `STATUS / UNBUILT` and `PLAN / Plan construction…` **as
> display-only text this step**. The Operations view is where the plan is composed.
>
> — `2026-09-03-launch-pad-design.md`, Decision 8

That text is a label. The composer's build target is the string `"dock_a"`, resolved once at
`_Ready` (`OperationsFocus.cs:656-673`). Launch Pad 2 has nothing pointed at it. A plan in flight is
invisible on the schematic, and the only thing an unbuilt card says about itself is that it is drawn
at forty per cent alpha. This step makes each of those real.

**Upgrades are deliberately out.** See *Not built*.

## Source material

- **[Ticket #36 — Vessel Construction Interface](https://github.com/Gatov/DimenshipGame/issues/36)**
  — the brief. Authoritative for scope, for the readings the preview owes the player, and for the
  *Scope* list this document's *Not built* section carries forward.
- **`docs/superpowers/specs/2026-09-03-launch-pad-design.md`** — Decisions 3, 6, 7 and 8 are the
  machinery this step drives. Binding: the `processes` panel id does not move, commissioning
  consumes one whole unit from local storage, and a plan is its tasks with no shortage list.
- **`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`** — binding on
  vocabulary (**socket**, never *slot*, for the equipment holder) and on the rule that construction
  fills an authored slot and never creates geometry. Its *What is explicitly not being added* list
  holds here too: no `ConstructionTask` type, no new `TaskState` or `PostponeReason`.
- **`docs/Game Design v0.9.md` §5.5, §5.6, §5.10** — the diagnostic-quality rule (every serious
  warning answers what is wrong, why, and where to fix it), the node inspector's shape, and the
  ruling that progression is expressed through upgrades and construction using the production layer
  already described, never a parallel system.
- **`docs/reviews/2026-09-04-launch-pad-review-analysis.md`** — the adjudication that settled what
  Decision 8 shipped, and the standing warning that a document quoting a spec which has since moved
  re-opens findings that were closed.

## Vocabulary

Two words are ruled here so the decisions below can be read literally.

| Word | Meaning in this step | Why not the obvious alternative |
| :--- | :--- | :--- |
| **Construction phase** | A **derived reading**, not state: where a slot's committed plan has got to — queued, producing the unit, in transit, blocked, or complete. Computed from the snapshot and thrown away each frame. | A phase field on `FacilityInstance` would be state the save must carry and the engine must maintain, for a value the plan's own tasks already answer. It would also put UI vocabulary in the kernel, which is the thing `WorldSnapshot` exists to avoid. |
| **Slot** | Unchanged from the launch pad spec: the **authored facility node position** with `BuiltAtStart` false. Construction fills one. | Still not the equipment socket. This step does not reopen the question, because it does not touch equipment. |

*Upgrade* is used in this document only to say what is not being built.

## Decisions

### 1. The kernel gains one projection and nothing else

The ticket asks the preview to show the target, the capability gained, required / available /
to-produce materials, the assigned factories, energy demand, delivery dependencies, an estimated
minimum duration, and how the work competes with everything else the vessel is doing. That reads
like a demand for a new plan record. It is not. Every one of those readings is already derivable
from what `ProductionPlanner` returns plus the catalog:

| Reading | Where it comes from |
| :--- | :--- |
| Target | The composer's own selection; label via `WorldState.NameOf` as projected onto `ExecutorState.Label`. |
| Capability gained | The target archetype's `purpose` — the one new field, Decision 2. |
| Required | `Transfer.Quantity` on each `PlannedTask`, and the schematic inputs behind each `Produce`. |
| Available | `PlannedTask.AvailableAtSource` — exactly what it was added to say. |
| To produce | `Transfer.Quantity` minus `AvailableAtSource`. |
| Assigned factories | `PlannedTask.Executor` on each `Produce`. |
| Energy demand | `SchematicDefinition.EnergyPerRun` × runs, against `EnergyState.Capacity` and `Reserve`; plus the target's `StandingPowerDraw`, which is what the vessel pays *after* it is built. |
| Delivery dependencies | The `Transfer` tasks in plan order, each with its `From`, `To` and the line `PlannedTask.Executor` names. |
| Estimated minimum duration | `ProductionPlan.EstimatedTicks`, already documented as the busiest executor's total and a lower bound. The ticket's word is *minimum*, which is what that number honestly is. |
| Competition with other work | `snapshot.Tasks` filtered to the executors the plan names — what is already queued on the factory this plan wants. |
| What proceeds, what waits | `AvailableAtSource` per task, per Decision 6 of the launch pad spec. |
| Missing schematics or routes | `Unplannable` with `LockedSchematic` / `NoExecutorOrLine` / `CyclicSchematic`. |

So the preview is **assembled in the shell from the plan and the catalog**. `ProductionPlan` does
not grow display fields.

**Rejected: widening `ProductionPlan`.** A `PlannedTask` carrying its own rendered cost line would
be the kernel deciding what a preview says, and a second place for a number the catalog already
holds. The four-tier model's rule — state stores no value the catalog can already answer — applies
to a projection for the same reason it applies to state: two sources drift, and the symptom is a
preview that disagrees with the node inspector about what a run costs.

The one thing the shell must not do is re-derive a number the planner already computed. In
particular the estimate is `EstimatedTicks` verbatim, not a second sum over the same tasks.

### 2. One new catalog field: `purpose` on `FacilityArchetype`

`FacilityArchetype` (`Content/Archetypes.cs:36-45`) carries exactly one human-readable string,
`Label`. `facilities.json` has a `notes` field, but the loader parses it into
`FacilityDto.Notes` (`Content/Json/ContentFiles.cs:121`) and discards it — it never reaches the
archetype, and it is written for the author of the JSON, not the player.

The ticket asks the inspector to show an unbuilt slot's **purpose**, and the preview to show the
**capability gained**. Both are the same sentence: *what this machine is for*. Nothing can derive
it. The nearest thing in the repository is the XML doc comment on `FacilityType`, which is a
comment.

`FacilityArchetype` gains a required `purpose` string, authored for all four shipped archetypes.
It is tier-one data: immutable, loaded once per process, referenced by id from a save and never
copied into one. The loader treats a missing `purpose` the way it treats every other missing
required field — a collected error, not a silent default.

**Rejected: deriving purpose from `FacilityType` in the shell.** Four hard-coded sentences in a
Godot script is content living in code, and it makes a fifth facility type a code change. The
content pipeline exists precisely so that adding a facility means editing JSON.

**Rejected: reusing `notes`.** The note field is the author's margin — `items.json`'s note explains
why a hold capacity is 80000 — and reading it to the player would make every future note a
user-visible string nobody reviewed.

### 3. Prerequisites are static and derived; feasibility stays in Operations

The ticket asks the inspector to show **prerequisites**. There are two readings of that word and
only one belongs in a panel.

The static prerequisite is content: *this slot is commissioned by one Mission Dock Construction
Unit, which `assemble_dock_unit` makes in a Factory*. That is a lookup —
`FacilityArchetype.ConstructionUnit`, then the schematics whose output names it — and the inspector
shows it.

The dynamic one is feasibility: *is that schematic unlocked, is there a factory, is there a route,
is there material*. Answering it means composing a plan. The inspector does **not** do that.
Decision 8 of the launch pad spec put the composer in one place on purpose, and a panel that ran
the planner for whatever happens to be selected would run it on every snapshot for every selection
change — for an answer the player gets a click later, in the surface built to explain it.

So: the inspector says what a slot needs and offers the action; Operations says whether it can be
had and why not.

### 4. The inspector's action is a button, and it lives outside the recycled rows

`FacilityInspectorPanel` renders through a `Line` list that `Sync()` (`:466-484`) reconciles against
recycled `DetailRow` children **by index**. A row is a name, a value, a colour, an optional fill and
an optional icon. Nothing in that mechanism can be clicked, and making it clickable would mean every
row carrying a nullable callback for the one row that has one.

The panel's `_Ready` builds a `column` holding `head` and `_rows`. A third child is appended: an
action area holding one button, styled with `ShellTheme.ApplyGlass`. The precedent is
`EventLogPanel.cs:51-76`, which puts a row of filter buttons above its output in the same way.

The button has three states and no fourth:

| When | Reads | Does |
| :--- | :--- | :--- |
| Unbuilt slot, no active plan for it | `PLAN CONSTRUCTION…` | Opens Operations with a Build plan prefilled for this slot. |
| Unbuilt slot, an active plan targets it | `VIEW PLAN` | Opens Operations with that plan selected. |
| Anything else | — | Hidden. |

**`PLAN UPGRADE…` is not among them**, because no upgrade exists to apply. The ticket's wording —
facilities offer it *when an applicable upgrade exists* — is satisfied by a button that is never
shown rather than by a disabled control that promises a feature. A greyed-out affordance for a
subsystem nobody has designed is a worse lie than its absence.

### 5. A focus switch that carries a payload

`ShellActions.FocusRequested` takes a `PanelId` and nothing else, and `Zone.Show` frees the outgoing
panel and constructs the incoming one (`Zone.cs:73-104`). There is therefore no object to hand a
target to: the `OperationsFocus` that will receive it does not exist when the button is pressed.

One new command joins the table — `ShellActions.OperationsRequested` — carrying what Operations
should open with. `ShellRoot` handles it by parking that value on `ShellContext`, exactly as it
already parks `CurrentSelection`, and then invoking the existing `FocusRequested(ProcessesId)` so
the layout persistence and rail highlighting stay in one place. `OperationsFocus.OnMount` reads the
pending value and clears it, so a later return to the view opens on the composer as before.

One value covers both of Decision 4's cases: a build target (an `ExecutorId`) or a plan to show (a
`PlanId`).

**Rejected: widening `FocusRequested`.** Every call site — the rail's buttons, the `Ctrl+1..9`
accelerator — would carry a parameter that means nothing to it, to serve one caller.

**Rejected: the inspector calling `ComposePlan` and handing over a finished plan.** The composed
plan would then be a hypothesis built against one snapshot and rendered against a later one, and it
would make the inspector a second composer. `ShellContext.ComposePlan` stays Operations' to call.

### 6. The Build composer enumerates unbuilt slots, from the snapshot

`ResolveBuildTarget()` looks up the literal `"dock_a"` in the scenario, once, in `_Ready`. Two
things are wrong with that for this step: Launch Pad 2 is unreachable, and built-ness is **state**,
so a target list resolved from content is a list that never shrinks when a pad is built.

Build gets the same treatment Produce already has — an `OptionButton` — populated per snapshot from
every scenario facility whose archetype names a `ConstructionUnit`, filtered to those the snapshot
reports `Built` false, in scenario declaration order. Declaration order because it is the ordering
contract everywhere else, and because a list that re-sorts itself as the world changes moves the
item under the player's cursor. When nothing is left, one disabled entry reads
`NOTHING LEFT TO BUILD`, mirroring Produce's `NOTHING UNLOCKED`.

What a Build plan **is** does not change:
`Plan({archetype.ConstructionUnit, 1000}, destination: facility.LocalStorage)`. No new task kind, no
`ConstructionTask`, nothing in the kernel learns the word *build*. This is the refit spec's central
claim — construction is a plan of ordinary tasks — and it is already true.

The composer's quantity control stays hidden in Build mode. One slot takes one whole unit; a
quantity box that could say two would be offering to commission a facility twice.

### 7. DISCARD, and the double-approve it exposes

The ticket asks for **Approve** and **Discard**, and says neither resumes time.

There is no discard control anywhere in the repository today. Adding one is straightforward — clear
the draft, reset the composer to its default target and quantity, re-preview — but writing it
surfaced a live defect beside it. `OnApprovePressed` (`:428-434`) invokes `PlanApproved` and does
nothing else: `_currentPlan` still holds the plan, and `_approve` is still enabled. **Pressing
APPROVE twice commits the same plan twice**, queuing two runs and two hauls for one launch pad.

Decision 8 of the launch pad spec already states the intended behaviour — *"the composed plan is
discarded on approve and never stored"* — so this is the code catching up with its own spec rather
than a new rule. Approving clears the draft and disables the button until the composer changes.
DISCARD does the same clearing without the commit.

**Neither touches TimeFlow.** That is already true and is recorded here so it stays true: the only
automatic pause in the shell is `SimulationDriver.CheckPlanCompleted`, which fires on a
`PlanCompleted` event, and a commit cannot emit one. A player who approves a plan while paused stays
paused, which is the point — approving is a decision, not a start.

### 8. Construction phase is a `Presentation/` projection

The schematic must report where a plan has got to, and blocked work must name its cause and link to
its plan. That is a function of the snapshot, and it belongs somewhere both surfaces can call and a
test can reach.

`src/Dimenship.Core/Presentation/ConstructionProgress.cs` — a pure static projection over
`(WorldSnapshot, ExecutorId)` returning the phase, the root-cause `PostponeReason` when there is
one, and the `PlanId` to link to. It sits beside `BaseGraphLayout`, which is the established
precedent for presentation logic that names Core ids, carries no rendering type and no float, and is
tested in the kernel suite.

Attribution needs no new field. `CommittedPlanState.Destination` is the storage a plan ends at, and
a slot's `ExecutorState.LocalStorage` is that storage — so the plan building a given slot is the
active plan whose destination matches it. This is the payoff of Decision 6's `Destination`, used for
something it was not added for and fits exactly.

The phases, and what each is read from:

| Phase | Read from |
| :--- | :--- |
| Unplanned | No `Active` plan whose `Destination` is this slot's `LocalStorage`. |
| Queued | A plan is active and no spawned task has left `TaskState.NotStarted`. |
| Producing the unit | The plan's `Produce` task is `Running`. |
| In transit | The final `Transfer`'s `LoadedQuantity` exceeds its `MovedQuantity` — cargo is on a belt and has not landed. |
| Blocked | Any spawned task is `Postponed`; the cause is `PostponeReasons.RootCause` over their `LastReason`s. |
| Complete | `ExecutorState.Built`. |

Blocked is reported **over** the other phases rather than beside them, because the GDD's diagnostic
rule asks what is wrong before it asks how far along. `RootCause` is used rather than the first
reason found, so the schematic and the Operations detail cannot disagree about why a plan is stuck —
the same requirement that put a total order on `PostponeReason` in the first place.

A task the registry has already retired past its 512-entry window resolves to nothing; the
projection treats a missing task as complete, which is what retirement means.

**Rejected: a phase field on `ExecutorState`.** It would make the kernel own the interface's
vocabulary, and it would have to be computed every tick for every slot whether or not anything is
looking.

**Rejected: a helper in `Dimenship.Shell`.** That assembly does not reference `Dimenship.Core` by
construction, so it cannot name a `CommittedPlanState`. Reducing the signature to primitives to fit
would push the real work — deciding which task is the run and which is the final delivery — back
into the Godot assembly, which has no test project at all.

### 9. Unbuilt reads as unbuilt without colour: a word and a dash

The ticket asks for subdued silhouettes, dashed outlines, and explicit **Unbuilt** labels, and
requires that selection and availability stay readable without relying on colour. Today the entire
vocabulary is `ShellPalette.UnbuiltModulate` — forty per cent alpha applied to the whole card
(`NodeCard.SetBuilt`) or the whole edge (`GraphCanvas`'s `Tint`).

The alpha stays, and it is the silhouette. CLAUDE.md's rule against a second colour ramp is
deliberate: a dimmed card is one value to keep in step with the built one, and a parallel palette is
two. Two signals are added beside it, and neither is a colour.

**A word.** `ExecutorCard` prints the construction phase where a built card prints its schematic —
`UNBUILT`, `QUEUED`, `PRODUCING`, `IN TRANSIT`, `BLOCKED`. An unbuilt dock today reads
`MISSION DOCK · UNCONFIGURED` and `0 TASKS QUEUED`, which is true of a working idle facility too.
The word is what makes the state legible in greyscale, and it is the same word the inspector and the
Operations detail use.

**A dash.** No dashed-stroke helper exists anywhere in the project; `GraphCanvas` draws with
`DrawPolyline` and `DrawPolygon`, and every box `ShellTheme` builds is a `StyleBoxFlat` with a
one-pixel solid border. One is added to `ShellTheme` — which already owns the project's one piece of
generated drawing, the `HSlider` grabber texture it builds from a palette colour, so a drawing
primitive living there is established rather than novel. Dash and gap lengths are `ShellPalette`
constants, and it is called from two places:
`NodeCard` for an unbuilt card's outline, and `GraphCanvas` for an unbuilt edge. Both, because
`factory_link_ab` and `factory_link_bc` ship unbuilt and a vocabulary that covers cards but not
lines would say two different things about one condition. It is themed centrally rather than at each
call site, per the house rule that a new drawing primitive belongs in `ShellTheme`.

Selection remains a border-colour change and nothing else, so a selected unbuilt card is a dashed
outline in the accent colour — the two signals compose rather than competing for the same channel.

### 10. Camera position is held in memory; selection already is

Returning from Operations must preserve schematic selection and camera position.

**Selection already survives.** `ShellRoot` stores it on `ShellContext.CurrentSelection` when it
changes, and `BaseGraphFocus.OnMount` seeds `_selection` from it, so a rebuilt view comes back
highlighted. Recorded here so a later reader does not "fix" what is not broken.

**Camera does not.** `_zoom` and `_pan` are private fields on `BaseGraphFocus`, and `Zone.Show`
frees the panel on every view change — so leaving for Operations and coming back resets the graph to
its resting zoom, centred. They move to `ShellContext` beside the selection, read in `OnMount` and
written whenever they change.

**Rejected: persisting the camera in `user://layout.json`.** `LayoutState` has seven fields and a
serializer that mirrors them exactly; adding two more would change a file every existing player
already has, to promise something the ticket does not ask for. "Returning from Operations" is a
session-lifetime promise, and `ShellContext` is where session-lifetime shell state lives.

### 11. The layout requirements are already met, and this step changes none of them

The ticket's *Layout and appearance* section is mostly a request to **preserve** what the concept
sketch established. All of it is already true, and recording where is the point — an implementer
reading the ticket cold would otherwise re-derive a layout that is authored content.

| Ticket asks | Where it already lives |
| :--- | :--- |
| Resource Storage centrally, reactors left, factories right, docks below | Authored `placement` cells in `dimenship/content/scenarios/default_vessel.json`, projected by `BaseGraphLayout.For(scenario)`. Reactors are column 0, factories column 4, docks row 4. |
| Inspector on the right | `FacilityInspectorPanel` in `ShellRoot`'s right-hand `ZoneKind.Panel` zone. |
| Horizontal navigation | `Rail`, ordered by focus-view title. |
| Visible TimeFlow controls | `StatusBar`, the last row of the root column. |
| Compact bottom event log | `EventLogPanel` in the console zone, `` Ctrl+` ``. |
| Console palette, flat icons, thin connectors, blue selection accents | `ShellPalette` / `ShellTheme` / `IconSlot`; `GraphCanvas.LineWidth` is 2px and selection is width, not hue. |

Nothing in this list is edited. The one gap is in Decision 12.

### 12. Stability joins the Power Core card rather than becoming a node

The ticket says *"Power Core and Stability remain compact status cards"*. `PowerCard` exists on the
graph and is authored a cell like every other node. The stabilization field does not: it is a
`PowerSinkState` and appears only in `EnergyBudgetPanel`.

The stabilization draw is added to `PowerCard` as a second compact reading rather than given a node
of its own. `PowerCard` is already the edgeless node — energy is a global pool and drawing power
lines would be a lie about how it works — and a stabilization field is the single largest draw
against that pool, with no route and no material. Giving it a placement would mean authoring a cell
for a node that can never have an edge, and every scenario would owe one.

This is the least certain decision in the document and the cheapest to reverse; it is listed under
*Open items* for that reason.

## Not built

Recorded so a later reader does not assume an oversight.

- **Upgrades, in every form.** No socket storage, no upgrade schematic, no effective-rate resolution,
  no downtime, and no `PLAN UPGRADE…` action. `2026-08-20-recycling-refit-and-construction-design.md`
  leaves four questions open that an upgrade must answer first: whether a socket holds one module or
  several, how several modules' modifiers combine, whether commissioning takes the target offline,
  and what the equipment tier is called given the shipped `module` commodity. Answering them is a
  design step, not an implementation detail, and the dials that would carry the result
  (`FacilityInstance.WorkRatePermille`, `EnergyEfficiencyPermille`) already exist and are already
  documented as moved by nothing. The ticket asks for the upgrade action *when an applicable upgrade
  exists*; none does.
- **The ticket's own exclusions**, unchanged: no facility placement, no route drawing, no separate
  construction queue, and no queue editing on the schematic.
- **Plan editing, cancelling or abandoning.** `PlanState.Abandoned` exists and nothing sets it.
  DISCARD throws away a *draft*; a committed plan still runs to completion.
- **Line construction.** `factory_link_ab` and `factory_link_bc` ship unbuilt, and this step makes
  them draw as unbuilt. Nothing builds a transport line; `TransportInstance.Built` is authored only.
- **Critical-path estimates.** `EstimatedTicks` stays the busiest executor's total. The preview
  labels it a minimum, which is what it is.
- **Anything that reaches the save.** No new state, no new DTO, no `WorldSave.CurrentVersion` bump.
  The one new field is catalog content, and the one new type is a projection over a snapshot.

## Open items

- **Stability as a second reading on `PowerCard`** (Decision 12) rather than its own authored node.
  Cheap to reverse either way; decide from a running game.
- **Whether the phase word replaces or joins the schematic line on a built card.** Decision 9 gives
  the word an unbuilt card's schematic slot, which is free because an unbuilt facility has no
  schematic. A built facility running a construction plan *for another slot* has both, and the card
  has one row. Today that is the factory's ordinary `Configured` reading and it stays; if the phase
  should surface there too, the card needs a row it does not have.
- **Whether `purpose` wants a second, longer field.** One sentence serves both the inspector row and
  the preview's *capability gained*. If the preview eventually wants a paragraph, that is a second
  field rather than a longer first one — an inspector row cannot wrap far.
- **When sockets land**, the construction phase gains a step between *in transit* and *complete*
  (delivery into the socket), and Decision 8's table grows a row. The projection is the right place
  for that change; nothing else here moves.
