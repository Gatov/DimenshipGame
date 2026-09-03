# Launch Pad 1 — Implementation Plan

Status: Draft

**Goal:** The player composes, approves and watches a plan; the first plan commissions Launch Pad 1
from an unbuilt Mission Dock, with `Built` enforced, tasks unified as scripts, and the Operations
view live. The vessel opens quiet — factory standing production seeds wiped.

**Spec:** `docs/superpowers/specs/2026-09-03-launch-pad-design.md`.

**Architecture:** Content authors the construction unit and the unbuilt pads; the kernel enforces
`Built`, commissions by consuming one unit from local storage, collapses production/transport into
`TaskScript` / `TaskInstance`, and treats a plan as an ordered list of those scripts with
completion when the last task retires. The shell's Operations view is the first live
`Enqueue`/`Commit` path from the Godot layer; the base-graph stopgap ends so commissioning is
visible.

## Why

Today the vessel runs itself and the player watches. Five standing orders were seeded by the
scenario, the three focus views that exist are read-only, and `SimulationDriver` exposes pause /
step / speed only — nothing in `dimenship/` ever calls `SimulationEngine.Enqueue`. There is no
player verb.

Four things in the shipped code block that verb:

1. `FacilityInstance.Built` exists but the engine never reads it — an unbuilt dock authored today
   would be a ghost that works.
2. Nothing commissions anything; `FacilityArchetype` has no construction field.
3. Production and transport are two task types with two engine paths, two snapshot lists and two
   save DTOs — a plan cannot be one list.
4. `BaseGraphFocus` takes its layout from a second world (`ShellContent.NewWorld()`) and builds
   cards once — built-ness that changes mid-run cannot reach the screen.

This plan is the work that closes those four, in that order, behind the design in the companion
spec. Conditions ship as mechanism only; the first real caller is the program runtime.

**Branch note:** the brief named `claude/launch-pad`; this step lands on `master` by request, one
commit per stage.

## Tasks

- [x] **0. Design docs.** Spec and this plan under `docs/superpowers/`, citing the brief,
      `2026-08-20-recycling-refit-and-construction-design.md`, and the programming-view Condition /
      Operand records (`2026-08-11-programming-view-design.md:262–305`). Vocabulary ruling recorded:
      `mission_dock_construction_unit` is an item, `slot` stays the authored node position, local
      storage stands in for the socket.

- [ ] **1. Content.** `mission_dock_construction_unit` item; `assemble_dock_unit` schematic (inputs
      only `basic_metals`); `constructionUnit` on `mission_dock` (required field, `null` elsewhere);
      `factory_feed` / `factory_return` transport archetypes (catalog present; construction hauls
      will use them). Scenario: Launch Pads unbuilt, interconnects unbuilt, hold-star routes built,
      **`initialTasks` empty**, only extractor out-haul seeded. Soft target: plan completes near 120
      ticks (~2 min at 1×), not a hard limit.

- [ ] **2. `Built` is enforced.** Skip standing draw, step, and reservation for unbuilt executors;
      filter them from `IWorldView`; `Enqueue` refuses them; loader refuses initial tasks/transfers
      on unbuilt executors and requires a built hold route both ways for every commandable facility.

- [ ] **3. Commissioning.** `FacilityArchetype.ConstructionUnit`; tick phase between transport and
      production consumes exactly 1000 milli-units and sets `Built`; `EventCode.FacilityBuilt`.
      Local-storage interim only — do not also implement socket delivery. Nothing builds a line.

- [ ] **4. A task is a script.** `Programs/` conditions (`StorageItemAmount`, `ExecutorStatus` —
      renamed from the programming-view's `ExecutorStatusIs`); `TaskScript` / `TaskAction` /
      `TaskInstance`; one registry, one `Enqueue`, one snapshot list, one save DTO; conditions gate
      start only; **`ConditionNotMet`** appended last in `PostponeReason` (deliberate exception to
      recycling/refit's "no new PostponeReason"); `WorldSave.CurrentVersion` stays 1. Move every
      downstream caller of the two lists with it.

- [ ] **5. A plan is its tasks.** `ProductionPlan` with `Destination`, `Unplannable`,
      `EstimatedTicks`; delete `ShortageKind.RawResource` and `PlanShortage`; preserve commit order;
      `PlanCompleted` when the last spawned task retires.

- [ ] **6. Shell.** `OperationsFocus` replaces the Processes placeholder (`id` `"processes"`);
      driver `Plan` / `Commit` and pause on `PlanCompleted`; fix the base-graph stopgap (driver's
      world, re-chrome on `FacilityBuilt`, dim unbuilt via palette); inspector display-only rows;
      `CLAUDE.md` updates for the panel id, commissioning phase, and `ConditionNotMet` ordering.

## Tests

New or extended in `tests/Dimenship.Core.Tests` (names from this plan):

- `UnbuiltFacility_DrawsNothing_StepsNothing_AndReservesNoRoom`
- `Enqueue_RefusesAnUnbuiltExecutor`
- `Planner_DoesNotRouteThroughAnUnbuiltLine`
- `AFacilityCommissions_TheTickItsUnitArrives_AndProducesTheTickAfter`
- `Commissioning_ConsumesExactlyOneWholeUnit_AndLeavesTheRemainder`
- `AConditionThatIsFalse_PostponesWithConditionNotMet_AndRetriesNextTick`
- `AConditionNeverStops_ARunAlreadyInFlight`
- `ConditionNotMet_LosesToEveryOtherReason`
- `Enqueue_RefusesAParameterRef`
- `APlanWithNoProducerForAnInput_StillEmitsTheTransfer`
- `PlanTasks_KeepTheOrderCommitWouldEnqueueThem`
- `Estimate_IsTheBusiestExecutorsTotal`
- `APlanCompletes_WhenItsLastTaskRetires`
- `DeterminismSurvivesASave` extended over the merged task list and `Built`
- Loader: initial task on unbuilt executor refused; commandable facility missing a built hold route
  refused (one broken thing per test)

`tests/Dimenship.Shell.Tests` — unchanged. The two reflection guards in `CoreAssemblyTests` cover
`Programs/` for free.

## Verification

```bash
dotnet build DimenshipGame.sln
dotnet test DimenshipGame.sln
```

If the Godot 4.7.1 SDK is not on the feed, build and test the four non-Godot projects directly per
`CLAUDE.md`. Do not claim the solution builds if only the kernel did.

End-to-end, in the editor (`dimenship/project.godot`):

1. New game — Launch Pad 1 and 2 dimmed, interconnects dimmed; factories idle (no standing factory
   tasks); extractor still hauls hydrogen.
2. Status bar draw at rest near 8,100 of 10,000.
3. `Ctrl+2` → Operations → Build → Launch Pad 1 — preview lists haul, run, deliveries; estimate
   near **120** ticks (~2 min); Unplannable empty.
4. Approve — tasks appear on an idle `factory_a` and the lines; no switch-off of a prior standing
   schematic.
5. Near tick **120** (soft) — `FacilityBuilt`, card un-dims, `PlanCompleted`, driver pauses. CapHits
   under the plan are what to watch.
6. Save and reload mid-plan — plan and tasks resume to the same tick.

## Not built

Routing, line construction, plan editing or cancel, sockets, missions, the program runtime, stored
plan labels, critical-path estimates — and the second dock stays unbuilt with nothing pointed at it.
