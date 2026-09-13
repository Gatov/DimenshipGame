# Vessel Construction Interface — Implementation Plan

Status: Draft

**Goal:** A player selects an unbuilt slot on the schematic, reads what would stand there and what
it needs, opens a prefilled Build plan from the inspector, reads a preview that names materials,
factories, energy, dependencies and duration, approves or discards it, and watches the schematic
report the plan's phase until the facility activates in place.

**Spec:** `docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md`.
**Issue:** [#36](https://github.com/Gatov/DimenshipGame/issues/36).

**Architecture:** The kernel gains one catalog field (`purpose`) and one projection
(`Presentation/ConstructionProgress`) and nothing else — every other reading the ticket asks for is
already derivable from `ProductionPlan`, `WorldSnapshot` and the catalog. Everything else is shell:
the Build composer enumerates unbuilt slots from the snapshot instead of resolving `"dock_a"` from
content, the preview is assembled from the plan and the catalog, the inspector's display-only
`Plan construction…` label becomes a button that carries a target to Operations through
`ShellContext`, and an unbuilt card gains a phase word and a dashed outline beside the alpha it
already has.

## Why

The launch pad step built the whole machine and deliberately left the handle off. Its Decision 8
says so: the inspector's `PLAN / Plan construction…` is *"display-only text this step"*, and the
composer's build target is the literal string `"dock_a"` resolved once at `_Ready`
(`OperationsFocus.cs:656-673`). Launch Pad 2 ships unbuilt with nothing pointed at it, by that
spec's own *Not built* list.

So a player today can build exactly one facility, by finding a view they were not sent to, choosing
a target they cannot change, from a preview that lists task instructions and a tick count. Once
approved, the schematic says nothing at all about the plan: the dock stays at forty per cent alpha
until it abruptly is not, and an unbuilt card's own text — `MISSION DOCK · UNCONFIGURED`,
`0 TASKS QUEUED` — is what an idle working facility says too.

Ticket #36 is the step that closes that gap, and it closes it almost entirely in the shell. That is
the finding worth recording: the ticket reads like a demand for a construction subsystem, and the
subsystem already exists. Writing it as one — a `ConstructionTask`, a plan record carrying rendered
costs, a phase field on `FacilityInstance` — is the mistake this plan exists to prevent, and the
refit spec already forbids most of it.

Upgrades are the one part of the ticket not built here. No upgrade exists anywhere in the kernel or
content, and the refit spec leaves four questions open that an upgrade path must answer before any
of it is code. The ticket asks for the upgrade action *when an applicable upgrade exists*; none
does. See *Not built*.

## Tasks

- [ ] **1. Content and the projection.** `purpose` on `FacilityArchetype` (required, authored for all
      four shipped archetypes), the `FacilityDto` field, the loader link, and `facilities.json`.
      `Presentation/ConstructionProgress` — a pure static projection over `(WorldSnapshot,
      ExecutorId)` returning phase, root-cause `PostponeReason?` and `PlanId?`, attributing a plan to
      a slot by `CommittedPlanState.Destination` against `ExecutorState.LocalStorage`. Blocked is
      reported over the other phases and uses `PostponeReasons.RootCause`, never the first reason
      found. A task the registry has retired resolves as complete. Kernel-only; no state, no save
      change, no float.

- [ ] **2. The composer generalises, and stops committing twice.** `ResolveBuildTarget` becomes a
      per-snapshot enumeration of every scenario facility whose archetype names a `ConstructionUnit`
      and whose snapshot row reports `Built` false, in scenario declaration order, behind an
      `OptionButton` like Produce's; `NOTHING LEFT TO BUILD` when the list empties. Quantity stays
      hidden in Build mode — one slot takes one whole unit. `DISCARD` beside `APPROVE`, clearing the
      draft and resetting the composer. Approving clears `_currentPlan` and disables the button
      until the composer changes, which is what Decision 8 already claimed happened and what stops
      a second press queuing the same plan again. Neither button touches TimeFlow.

- [ ] **3. The preview earns its name.** Per the spec's Decision 1 table: target and capability
      gained; required / available / to-produce per material from `Transfer.Quantity` and
      `PlannedTask.AvailableAtSource`; the assigned factory per `Produce`; energy demand from
      `EnergyPerRun × runs` against `EnergyState`, plus the target's `StandingPowerDraw` as the
      standing cost after it is built; delivery dependencies as the transfers in plan order with the
      line each runs on; `EstimatedTicks` labelled a minimum; and what is already queued on each
      executor the plan names, from `snapshot.Tasks`, as the competition reading. `Unplannable`
      keeps its section and gains the sentence explaining that the rest still proceeds. Nothing here
      re-derives a number the planner computed — the estimate is `EstimatedTicks` verbatim.

- [ ] **4. The inspector acts.** A third child on `FacilityInspectorPanel`'s column holding one
      `ApplyGlass` button, outside the index-recycled `DetailRow`s: `PLAN CONSTRUCTION…` on an
      unbuilt slot with no active plan, `VIEW PLAN` when one targets it, hidden otherwise — no
      upgrade state. `ShellActions.OperationsRequested` carries an `ExecutorId` or a `PlanId`;
      `ShellRoot` parks it on `ShellContext` and calls the existing `FocusRequested(ProcessesId)`;
      `OperationsFocus.OnMount` consumes and clears it. The unbuilt branch at
      `FacilityInspectorPanel.cs:118-128` gains `PURPOSE` and `REQUIRES` rows — the construction
      unit and the schematic that makes it, read from the catalog. The panel does **not** call
      `ComposePlan`.

- [ ] **5. The schematic reports.** `ExecutorCard` prints the construction phase word where a built
      card prints its schematic — `UNBUILT`, `QUEUED`, `PRODUCING`, `IN TRANSIT`, `BLOCKED` — plus
      the root cause when blocked. A dash helper on `ShellTheme` with dash and gap as `ShellPalette`
      constants, called from `NodeCard` for an unbuilt card's outline and from `GraphCanvas` for an
      unbuilt edge, so the two unbuilt factory interconnects read the same way the cards do.
      `UnbuiltModulate` is unchanged and remains the silhouette; no second colour ramp. `_zoom` and
      `_pan` move to `ShellContext` so the camera survives a trip to Operations. Stability joins
      `PowerCard` as a second compact reading. `CLAUDE.md` updated for the new catalog field, the
      projection, the dash vocabulary and the `OperationsRequested` command.

## Tests

New in `tests/Dimenship.Core.Tests/Presentation/ConstructionProgressTests.cs`:

- `ASlotWithNoPlan_ReadsUnplanned`
- `AnUnstartedPlan_ReadsQueued`
- `TheFactoryRunning_ReadsProducingTheUnit`
- `TheUnitOnTheBelt_ReadsInTransit`
- `ACommissionedSlot_ReadsComplete`
- `ABlockedPlan_NamesItsRootCause_AndItsPlan`
- `BlockedOutranksProgress_BecauseTheCauseIsWhatTheCardOwes`
- `TwoSlotsWithTwoPlans_DoNotReadEachOthersPhase`
- `ARetiredTask_ReadsComplete_NotMissing`

Extended in `tests/Dimenship.Core.Tests/Content/`:

- Loader: a facility archetype with no `purpose` is reported, not defaulted — one broken thing in the
  copied tree, per the house rule.
- `Shipped`: every facility archetype in the shipped catalog authors a non-empty `purpose`.

`tests/Dimenship.Shell.Tests` — unchanged. Nothing new lands in `Dimenship.Shell`: the camera is
Godot-side state on `ShellContext`, and the projection is in `Dimenship.Core` precisely because
`Dimenship.Shell` cannot reference it. The two reflection guards in `CoreAssemblyTests` cover
`Presentation/ConstructionProgress` for free.

The Godot assembly still has no test project, which is why stage 1 carries all of the logic a test
can reach and stages 2–5 carry none.

## Verification

```bash
dotnet build DimenshipGame.sln
dotnet test DimenshipGame.sln
```

If the Godot 4.7.1 SDK is not on the feed, build and test the four non-Godot projects directly per
`CLAUDE.md`. Do not claim the solution builds if only the kernel did.

End-to-end, in the editor (`dimenship/project.godot`):

1. New game — both Launch Pads and both factory interconnects draw dimmed **and dashed**; each pad's
   card reads `UNBUILT`.
2. Select Launch Pad 2 — the inspector shows `STATUS / UNBUILT`, its purpose, `REQUIRES` naming the
   construction unit and `assemble_dock_unit`, and a live `PLAN CONSTRUCTION…` button.
3. Press it — Operations opens with Build selected and Launch Pad 2 as the target, not Launch Pad 1.
4. The preview names the factory, the materials with what is aboard against what must be produced,
   the energy the runs draw, the hauls in order, and the estimate as a minimum.
5. `DISCARD` — the draft clears and APPROVE disables. Re-compose and `APPROVE` — the button disables
   and a second press commits nothing. Time flow does not change on either.
6. Back on the schematic (`Ctrl+1`): the pad's card reads `QUEUED`, then `PRODUCING`, then
   `IN TRANSIT`; selection and camera are where they were left.
7. The pad commissions — the card un-dims, loses its dash, and the event log shows `BUILT`. The
   inspector's button is gone.
8. Pause mid-plan, drop the factory's input, and confirm the card reads `BLOCKED` with the same root
   cause the Operations detail shows — not a different one.
9. Build both pads, then open the Build composer: `NOTHING LEFT TO BUILD`.
10. Save and reload mid-plan — the phase resumes at the same tick. Nothing here is saved, so this is
    a check that nothing needed to be.

## Not built

- **Upgrades, in every form**: no socket storage, no upgrade schematic, no effective-rate
  resolution, no downtime, and no `PLAN UPGRADE…` action.
  `docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md` leaves four
  questions open that an upgrade must answer first — whether a socket holds one module or several,
  how several modules' modifiers combine, whether commissioning takes the target offline, and what
  the equipment tier is called given the shipped `module` commodity. `WorkRatePermille` and
  `EnergyEfficiencyPermille` stay dials nothing moves.
- **The ticket's own exclusions**, unchanged: no facility placement, no route drawing, no separate
  construction queue, no queue editing on the schematic.
- **Plan editing, cancelling or abandoning.** `PlanState.Abandoned` still has nothing that sets it;
  `DISCARD` throws away a draft, not a committed plan.
- **Line construction.** The two unbuilt interconnects draw as unbuilt and stay unbuilt.
- **Critical-path estimates.** `EstimatedTicks` remains the busiest executor's total, labelled a
  minimum.
- **Anything that reaches the save.** No new state, no new DTO, no `WorldSave.CurrentVersion` bump.
