# Editable Production Plans — Design

Date: 2026-09-16
Status: Approved

## Goal

Turn the Operations preview into a collaborative plan editor. The planner generates a complete
proposal; the player may change production quantities, assign executors, and add direct logistics
steps. Edited values are locked; automatic adjustment updates only what remains uncommitted and
unlocked.

A draft is still a proposal. Runtime tasks are created only on approval.

## Source material

- **[Issue #40 — Editable Production Plans](https://github.com/Gatov/DimenshipGame/issues/40)** —
  the brief this document transcribes. Authoritative for editing rules, adjustment, assembly as an
  outcome milestone, and the acceptance criteria.
- **`docs/superpowers/plans/2026-09-16-editable-production-plans.md`** — how the brief lands in this
  repository (D1–D10 below are that plan's decisions, carried here so code cites a spec filename).
- **`docs/superpowers/specs/2026-07-30-production-planning-design.md`** — the planner is pure; a plan
  with shortages is committable.
- **`docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md`** — Decisions 6–8:
  the Build composer, DISCARD and double-approve, construction phase as a `Presentation/`
  projection.
- **`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`** — no refit state
  machine; every intermediate state is "a part is in a storage".
- **`docs/Game Design v0.9.md`** — passive sources (`commandable: false`) are unschedulable; stated
  three times.

## Vocabulary

| Word | Meaning here | Why not the obvious alternative |
| :--- | :--- | :--- |
| **Plan draft** | An immutable proposal that retains the requirement graph until approval. Held by the shell across frames; never saved; never on the snapshot. | Not `WorldState` — that would drag in a save DTO for an editor that cannot affect a tick. Not one of the four tiers. |
| **Requirement key** | The identity of a draft step: parent key + role + item. Locks and ids follow this, never the row index. | A list index slides onto the wrong row on the first re-plan. |
| **Assembly step** | A draft-only `ASSEMBLE` / `COMMISSION` row that is the root of a construction draft's dependencies. Flattens to **no** task. | Not a `TaskAction` — commissioning is already a tick phase; `ConstructionProgress.For` already reads it. |
| **Adjustment** | Pure `(draft, world, edit) → draft`. One edit and all automatic consequences are one revision (one Undo). | Not a command/inverse pair — a hand-written inverse can disagree with the forward path. |
| **Structural issue** | An error that makes the draft uncommittable (bad executor, no route, unknown endpoint, non-positive quantity, unbuilt / not commandable). | Distinct from supply shortfalls, which remain committable as `APPROVE PARTIAL PLAN`. |

## Core model

Planning starts at the final outcome and expands requirements backwards. A facility-construction
draft, for example, reads:

```
Assemble facility
└─ Construction unit at target
   └─ Move construction unit: Factory → target
      └─ Produce construction unit
         ├─ Move alloy → Factory
         │  └─ Produce alloy
         │     └─ Move raw material → Reactor
         └─ Move components → Factory
```

The planner already discovers these relationships recursively and then discards them when it emits
a flat task list. Editing requires a core-layer `PlanDraft` that retains this requirement graph
until the plan is approved. Each step has a stable `DraftStepId`, an Automatic or Manual origin, and
independent locks for quantity and executor. Identity follows the requirement relationship, never
the row index, so locks survive insertion and removal of neighbouring steps.

`ProductionPlan` remains the flat, immutable proposal accepted by `SimulationEngine.Commit`.
Approval validates the latest draft, strips editor metadata, and emits ordinary task scripts in
deterministic order. Draft metadata does not become runtime state.

## Decisions

### 1. The draft is the requirement graph; the plan is its flattening

`PlanDraft` holds `IReadOnlyList<DraftStep>` in expansion order, each carrying a parent. `Flatten`
emits transfers, then runs, then the final delivery leg — the order existing planner tests already
pin. `ProductionPlanner.Plan` becomes `PlanDraftEditor.Create(...).Flatten()` so there is one
expansion algorithm rather than two that drift.

**Rejected: editing `ProductionPlan.Tasks` in place.** A row index is not an identity; the first
re-plan produces a different list and locks slide.

### 2. Identity is the requirement path, not the row

Each step carries a `RequirementKey`: parent key plus a role (`Goal`, `Assembly`, `Output`,
`Input(ordinal)`, `Delivery`, `Manual(ordinal)`) and the item it concerns. A `DraftStepId` is
minted once per key and reused while that key survives an adjustment. Locks are carried by key.

### 3. Legs merge at flatten; the line is chosen per route

The draft keeps one move per requirement. `Flatten` merges moves sharing
`(item, from, to, executor)` in first-appearance order, summing quantity and availability. Automatic
line choice is per route per draft, not per step — otherwise one haul splits across two lines and an
unedited draft's estimate climbs.

### 4. Adjustment is a pure function returning a whole new draft

`PlanDraftEditor.Adjust(PlanDraft, IWorldView, DraftEdit)`. Immutable revisions: copying a draft of
tens of steps is cheap, and Undo that restores a revision cannot disagree with the forward path.
One edit plus its automatic consequences is one revision.

### 5. Backward adjustment per requirement node

At every node:

```
remaining = required
          - available stock
          - committed incoming work
          - locked draft contributions
          - retained valid automatic contributions
```

`IWorldView.Uncommitted` already nets committed tasks. Positive remaining creates or resizes work;
zero leaves the branch alone; unlocked steps that contribute nothing are removed; locked excess
stays as visible surplus and may satisfy another compatible requirement. Batch recipes round up to
whole runs and report surplus. Committed runtime tasks count as supply or demand but are never
edited by the draft.

After each player edit, automatic adjustment:

1. Freezes committed work, manual facts, and locked fields.
2. Walks requirements backwards, retaining still-valid automatic contributions.
3. Recalculates residual demand and batch surplus.
4. Resizes or adds production, then recursively expands its inputs.
5. Rebuilds unlocked logistics and selects only missing or invalid unlocked executors.
6. Removes unused unlocked work and recomputes issues, coverage, and duration.

Energy demand and executor competition stay shell-side readings of the flattened preview (Decision
7).

### 6. Stability is a rule; RE-ADJUST and UNLOCK ALL are explicit

A surviving unlocked executor choice is retained while valid rather than re-picked whenever queue
depth moves. `RE-ADJUST` drops retained choices and re-optimises every unlocked value, keeping
locks. `UNLOCK ALL` clears locks only; a manual row is a fact the player added and is removed by its
own control.

### 7. The kernel computes what determines the plan; the shell keeps what only describes it

The draft carries steps, issues, coverage and `EstimatedTicks`. Energy and competition remain
rendered in Operations from the flattened preview, the catalog and the snapshot — presentation
arithmetic must not land in the determinism-guarded assembly.

### 8. Structural errors block approval; shortfalls do not

Structural issue kinds (`IncompatibleExecutor`, `NoSuchRoute`, `UnknownEndpoint`,
`NonPositiveQuantity`, `UnbuiltExecutor`, `NotCommandable`) make `IsCommittable` false. Supply
kinds (`MaterialShortage`, `GoalShortfall`, and existing `UnplannableReason`s) leave it true; the
button reads `APPROVE PARTIAL PLAN` and names the uncovered requirement. Extends the 2026-07-30 rule
(*a plan with shortages is committable*) to player-created shortfalls, and no further.

### 9. Approval re-adjusts against the live world, then flattens

`PlanDraftEditor.Approve` runs one final adjustment, refuses a structurally invalid draft, and
returns a `ProductionPlan` with no draft metadata. `ShellActions.PlanApproved` becomes
`Func<PlanDraft, PlanApproval>` — a deliberate exception to the command-as-`Action` table, because
parking a refusal on `ShellContext` for the next frame would show it after the composer had moved
on.

### 10. A world refresh replaces the current revision; it never pushes one

Re-adjusting on each snapshot keeps availability and validation honest. Pushing each refresh onto
the Undo stack would make Undo walk ticks instead of player edits.

### 11. A passive source is unschedulable in the planner and at enqueue

`IWorldView.Facilities` skips non-`Commandable` archetypes the way it already skips unbuilt ones;
`EnqueueProduce` refuses one by name. Accepted consequence: Hydrogen on the shipped vessel is
honestly unplannable (`NoExecutorOrLine`) rather than quietly queued on the Emergency Hydrogen
Extractor.

### 12. Draft work is its own hierarchy, not `TaskAction`

`DraftAssemble` must be incapable of reaching `Enqueue`. A throwing case on `TaskAction` would be a
runtime failure where a separate type is an impossibility. Tracking assembly after approval is
`ConstructionProgress.For` reading `Built`.

## Editing rules

**Production** — the player may edit:

- **Quantity:** this step's exact contribution, not a plan-wide cap. Batch recipes round up to whole
  runs and show surplus before acceptance. A reduced contribution may be covered by stock, another
  recipe, or another facility; the planner must not create an identical unlocked step on the same
  executor.
- **Facility:** any built facility of the required type. Changing it rebuilds unlocked input and
  output legs around its local storage. An unroutable choice may remain as a visible issue but
  cannot be committed until repaired.

**Logistics** — quantity and transport executor; the selected line must run the step's exact route.
The player may add or remove a manual move (item, source, destination, quantity, line). A manual
move participates in the requirement graph: a locked Reactor → Factory alloy move supplies Factory
directly, and adjustment reduces the corresponding hold-mediated legs by what it carries. Rejected
if no direct line exists. It does not create material — missing source stock still postpones at
runtime.

## Interface

Operations shows plan rows in dependency/execution order: action (`ASSEMBLE` / `PRODUCE` / `MOVE`),
quantity, item or schematic, route, executor, availability, origin, and validation state. Quantity
and executor have separate lock controls. Edited fields use the palette accent and a lock glyph;
manual rows carry a manual glyph; automatically changed rows briefly show `REPLANNED`. Summary
keeps materials, energy, competition, unplannable and the estimate, and gains goal coverage plus an
issue list linked to affected rows. Controls: `ADD MOVE`, `RE-ADJUST`, `UNLOCK ALL`, Undo, Redo.

## Acceptance criteria

- Locked quantities and executors survive automatic adjustment, explicit re-adjustment, and world
  updates; invalid locks gain precise issues rather than being silently replaced.
- Changing a production quantity or facility recalculates only its affected unlocked dependencies.
- Adding a valid direct move reduces the equivalent hold-mediated flow; removing it restores the
  automatically selected route.
- Approval commits the latest validated revision as ordinary runtime tasks; construction displays
  and tracks its final assembly outcome via existing commissioning / `ConstructionProgress`.

## Not built

- Choosing an alternative schematic (still `candidates[0]` in declaration order).
- Multi-hop manual routing (one direct transport line only).
- Saved or named drafts (reload has no draft; Undo dies with the view).
- Softer executor preference (hard locks only).
- Timed assembly (no construction executor, assembly energy, or duration — outcome milestone only).
- Editing, cancelling or abandoning a committed plan (`PlanState.Abandoned` still unsettable).
- Conditions in a draft (every emitted `TaskScript` still carries empty conditions).
- Critical-path estimates (`EstimatedTicks` remains the busiest executor's total).

## Open items

- **Coverage as a ratio.** `Covered` is milli-units beside `Goal.Quantity`; a UI percentage is
  permille at the point of use, never a float.
- **Issue ordering in the summary.** Declaration order matches `PostponeReasons.RootCause`; the
  Operations issue list follows draft issue order (structural and supply interleaved as emitted).
- **Where a manual move sits in emitted order.** Graph position rather than add-order, so two
  structurally identical drafts enqueue identically.
