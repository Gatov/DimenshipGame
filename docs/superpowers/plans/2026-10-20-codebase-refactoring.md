# Codebase Refactoring — Implementation Plan

Status: Draft

> Steps use checkbox (`- [ ]`) syntax for tracking. Tick a box in the same commit that does the work.

**Goal:** Remove the drift-prone copies, split `SimulationEngine` into reviewable pieces, make the save
loader keep its own contract, and give the shell's composer logic a home that can be tested without
Godot. None of this may change what the simulation does.

**Review:** `docs/reviews/2026-10-20-codebase-architecture-review.md`. The finding ids used here
(A1–A15) are that document's. Each finding's evidence, with file and line, is there and is not
repeated here.

## Why

The kernel's enforced rules hold. What has grown is size and duplication. `SimulationEngine` is 3,695
lines, and every scheduling ticket since K2 has had to change it. Several rules now exist in two or
three copies that nothing compares:

- which draft issues block approval
- the planner's line choice
- the construction goal for a slot
- the milli-unit constant

Each copy is a future bug where one place gets the new rule and the other keeps the old one. The save
loader quietly defaults 94 fields that its contract says must be reported. Fixing all of this is
cheap now, and gets more expensive with every ticket that copies the pattern again.

The order matters more than any single step. Today nothing would catch a refactor that changed
behaviour: the replay tests check that a hash line *exists*, not what it says. So R0 comes first, and
every later step is held to it.

## Global Constraints

- **Zero behaviour change, proved by R0.** Every step keeps all suites green **and** every pinned
  replay hash identical. A step that needs a hash to change is not a refactor. It goes in its own
  commit whose subject says behaviour changed, and it updates the table in that same commit.
- **Kernel invariants.** `Dimenship.Core` references neither Godot nor `Dimenship.Shell`, and has no
  `float` or `double` (`CoreAssemblyTests`). `Dimenship.Shell` does not reference `Dimenship.Core`.
- **Determinism.** Do not reorder anything that is iterated in declaration order. Do not restructure
  `Tick` into separate select / grant / advance passes: CLAUDE.md records that doing so reorders the
  journal. The phases stay where `Tick` calls them; only the files they live in change.
- **Save format.** No save DTO changes shape, and `saveVersion` stays 4, unless a step says otherwise.
- **Commits.** Conventional prefix plus a lowercase behavioural sentence, with prose bodies, per
  CLAUDE.md. Moves go in their own commits, apart from edits, so `git log --follow` and blame stay
  usable.
- **Toolchain.** The cloud container has no `dotnet`. Run the suites wherever a toolchain exists, or
  through the on-demand CI workflow (`.github/workflows/ci.yml`, `workflow_dispatch`). Never record a
  step as verified without a run.

## Order

```
R0 ──► R1
   ├─► R4 (each item independent)
   └─► R2.1 ──► R3 ──► R2.2 ──► R2.3
R5, R6 (Godot layer) may proceed alongside the kernel work after R0
R7 last
```

---

### Task R0: A behaviour lock

**Fixes:** nothing yet; this is the prerequisite for everything below.

**Files:**
- Create: `tests/Dimenship.Replay.Tests/GoldenHashTests.cs`
- Create: `tests/Dimenship.Replay.Tests/golden-hashes.json` (or an inline table in the test, whichever matches the suite's existing style)
- Modify: `tests/Dimenship.Core.Tests/…` (the existing determinism-across-save test file) to add per-scenario cases

**Steps:**

- [ ] For every `tools/Dimenship.Replay/scripts/*.json`, under every name in `Policies.Names`, run
      the replay and record the `Final state SHA-256`. Write the table down in the order:
      script, then policy.
- [ ] Add `GoldenHashTests`: one `[TestCaseSource]` case per (script, policy) pair, asserting the
      report's hash equals the table's. When it fails, the message names the script and policy and
      shows both hashes.
- [ ] Add a guard test that every script on disk has a row for every policy, so a new script cannot
      skip the lock.
- [ ] For each shipped scenario, add an "advance 500, save, load, advance 500 equals advance 1,000"
      case, comparing `WorldSave.Write` output byte for byte. Read
      `DeterminismSurvivesASave_WhichIsWhatCatchesAFieldLivingOnlyInTheEngine` first and reuse its
      shape.
- [ ] Check the lock bites: temporarily swap two adjacent phases in `Tick` and confirm the golden
      test turns red, then revert. Record that this was done in the commit body.
- [ ] Commit: `test: every replay script's final state is pinned, so a refactor that changes the world fails`.

**Done when:** all suites are green, the deliberate reorder turns them red, and CI has run the new
tests once.

---

### Task R1: The save loader reports what it does not find (A3)

**Files:**
- Create: `src/Dimenship.Core/Content/FieldReader.cs` (internal, shared by both loaders)
- Modify: `src/Dimenship.Core/Content/JsonContentSource.cs` (its `Required` / `Flag` / `Positive` / `NonNegative` move to `FieldReader`)
- Modify: `src/Dimenship.Core/State/Save/WorldSave.cs`
- Modify: `tests/Dimenship.Core.Tests/State/…` (save tests)

**Steps:**

- [ ] Move the content loader's field helpers into `FieldReader`, generic over an error sink that
      takes a path and a message. Both `ContentError` and `SaveError` fit that. Move only, as its
      own commit, with R0 green.
- [ ] Add `FieldReader.Enum<T>`: `Enum.TryParse` plus `Enum.IsDefined`, refusing numeric strings,
      and reporting at the field's path.
- [ ] Replace each `?? default` in `WorldSave.Restore` with a `FieldReader` call. Keep values that
      are genuinely optional (`NameOverride`, `Configured`, `Current`, `SwitchTarget`) nullable on
      the state type, and don't default them.
- [ ] Replace all 24 `Enum.Parse` calls.
- [ ] For each field the 94 defaults covered, check whether a version 1–3 save could lack it. Where it
      could, the matching upgrader (`PriorityMovesOntoThePlan`, `ClaimsStartEmpty`,
      `NothingQueuedByHandWasHeld`) sets it explicitly. Where none could, it is now required.
- [ ] Tests: per DTO type, a save with one field removed gives one `SaveError` at that path. A
      `"status": "Bogus"` and a `"status": "42"` each give a `SaveError`, not an exception. Every
      existing round-trip and upgrade test stays green.
- [ ] Update the save-format bullet in CLAUDE.md only if the wording no longer matches.

**Done when:** `grep -c '?? ' WorldSave.cs` leaves only optional-reference fallbacks, there is no
`Enum.Parse` left in `State/Save/`, and R0 is identical.

---

### Task R2: Split `SimulationEngine` (A1, A7)

#### R2.1 — Partial files, moves only

- [ ] Split `src/Dimenship.Core/Simulation/SimulationEngine.cs` into
      `SimulationEngine.cs` (fields, constructor, `Advance`, `Tick`) plus
      `SimulationEngine.Storage.cs`, `.Commands.cs`, `.Validation.cs`, `.WorldView.cs`,
      `.Production.cs`, `.Transport.cs`, `.Power.cs`, `.Claims.cs`, `.PlanCommands.cs`,
      `.Explanations.cs` and `.Snapshot.cs`. The review's A1 table gives the method-to-file mapping.
- [ ] The diff is pure moves: no renames, no reformatting, no doc-comment edits.
- [ ] Commit: `refactor: the engine is read a phase at a time, one file per concern`.

**Done when:** no partial is over about 900 lines and R0 is identical.

#### R2.2 — Stateless collaborators

- [ ] Extract `StorageMath` (Available, Room, RoomForDelivery, the volume helpers, Deposit / Withdraw / Set) over the storage index and the catalog. `Set` throws on an unknown storage (A15) — that one line is a behaviour change for an impossible case and gets its own commit.
- [ ] Extract `SnapshotBuilder` (`BuildSnapshot` and its helpers). It may read only state, the catalog and the indexes it is handed, never the engine's per-tick scratch (`_powerGrants`, `_starvedThisTick`, `_reservedPower`).
- [ ] Extract `EngineWorldView` (the `IWorldView` implementation), with `AmendView` as a decorator over it.
- [ ] Give each collaborator at least one direct unit test.

#### R2.3 — `Amend` validates before it mutates

- [ ] Write a pure `CutPlan(tasks)` that returns the cut script per task without applying it.
- [ ] `Amend` plans against a view that sees the cut virtually, validates every new task, and only
      then applies the cut and appends.
- [ ] Add one `ApplyCut(task, action)` that holds the `line.Current` rule, used by `CutBack` and
      `Move`.
- [ ] Delete both undo blocks.
- [ ] Add a test: an amend refused by `RequireQueueable` leaves every task's script **and** every
      line's `Current` exactly as they were.

**Done when:** R0 is identical, the new test passes, and `Amend` contains no rollback code.

---

### Task R3: One executor interface (A2)

**Depends on:** R2.1.

- [ ] Add `internal interface IExecutorInstance` (Id, Built, Queue, Current, Status, BlockReason,
      PowerDrawLastTick), implemented by `FacilityInstance` and `TransportInstance`. The classes
      stay sealed, and `VesselState`'s lists stay separate and in declaration order.
- [ ] Before merging the two `RecordPassedOver` overloads, settle the difference between
      `byTask: executor.Current` (facility) and the `chosen` parameter (line). Show with a test that
      they give the same id on every path. If they don't, keep the difference and document it.
- [ ] Write `TopReadyPriority` and `RecordPassedOver` once, taking a readiness delegate and an
      event category.
- [ ] Collapse `Queued`, `CurrentJob` / `CurrentTransfer`, the two `Postpone` overloads,
      `FinishIfCutShort`'s two branches, and `Tick`'s two standing-draw loops.
- [ ] Compare replay wall-clock timings before and after. Record them in the commit body.

**Done when:** R0 is identical and no executor helper exists in a facility copy and a line copy.

---

### Task R4: One home for each rule (A4, A5, A6, A8, A9, A11, A14)

Each item is an independent PR. Any order works.

- [ ] **Blocking issues (A4).** Add `DraftIssueKind.Blocks()` next to the enum, and use it in
      `PlanDraft.IsCommittable`, `PlanDraftEditor.MergeIssues` and `BuildQuote.For`. Delete
      `IsStructuralKind` and the single-issue-draft workaround. Add a test listing every member of
      the enum, so a new kind needs a decision.
- [ ] **Line choice (A5).** Add `Planning/LineChoice.Best(lines, from, to, extraLoad)`, used by
      `PlanDraftEditor.ChooseTransport` and `SimulationEngine.Leg`. Delete the second
      `RouteExists`. Add a test that a draft and a `MoveWork` choose the same line for the same
      route and load.
- [ ] **Commit returns its plan (A6).** `Commit` and `Amend` return
      `Committed(PlanId Plan, IReadOnlyList<TaskId> Tasks)`. Dispatch and `Replay.cs` stop reading
      `Plans[^1]`. Replace the five test `Commit` helpers with one shared `TestPlans.Commit`.
- [ ] **Construction order (A8).** Add `Presentation/ConstructionOrder.For(catalog, snapshot, slot)`,
      returning goal, destination and target. Use it in `FacilityInspectorPanel`,
      `OperationsFocus.ComposerGoal` and `Replay.Commit`.
- [ ] **Milli-units (A9).** Add `Units.MilliPerUnit`, `Units.FromWhole` and `Units.ToWhole`. Delete
      `WholeConstructionUnit`, `UnitsToMilli`, `MilliPerUnit`, `WholeUnit` and the bare
      `Units.Format(1000)`.
- [ ] **World view read once (A11).** `Expansion` captures `Facilities` and `TransportLines` in its
      constructor, indexed by id and by `(from, to)`. `FindFacility`, `FindTransport`,
      `FastestLine`, `ChooseTransport` and `RouteExists` read the index.
- [ ] **Event inverse (A14).** Add `PostponeReasons.FromEvent(EventCode)` beside `CodeFor`, used by
      `EventLogPanel`. Add a round-trip test over every `PostponeReason`.

**Done when:** each PR is green with R0 identical.

---

### Task R5: A testable shell model (A10)

- [ ] Create `src/Dimenship.ShellModel` (references Core and Shell, no Godot) and
      `tests/Dimenship.ShellModel.Tests`. Add both to the solution, CI and CLAUDE.md's layout. Put
      a reflection guard test beside the existing ones: no Godot reference.
- [ ] Move `ComposerModel` out of `OperationsFocus`: mode, targets, quantity, draft,
      the revision / undo stack, the approve lock, and `ToState()` / `FromState()` for
      `OperationsComposerState`.
- [ ] Add `ConfirmGate` (arm, confirm, disarm on other input), used by inspector BUILD and plan
      CANCEL.
- [ ] Tests for what has regressed before: UX-01 park and restore, no second APPROVE, clamping the
      target index when a pad commissions and drops out of the list.
- [ ] Check in Godot that Operations and the inspector behave as before.

**Done when:** every field left in `OperationsFocus` is a widget or the model, and the composer
tests run without Godot.

---

### Task R6: Shell hygiene (A12 `ShellActions`, A13)

- [ ] Add label `ThemeTypeVariation`s to `ShellTheme` and one `ShellTheme.Text(text, variation)`
      factory. Replace the private label factories and inline `font_color` / `font_size`
      overrides, one view per commit.
- [ ] Add `ShellMath.Fill(amount, capacity)`, replacing the four copies.
- [ ] Add `Labels.Refusal(...)`, replacing both `"REFUSED:"` formatters.
- [ ] Make the `ShellActions` handlers required at construction, or move them behind
      `IShellCommands`. Leave `Handle(InputEvent)` and `Suspended` unchanged.
- [ ] Check each changed view in Godot. No suite covers visuals.

---

### Task R7: Encapsulation and leftovers (A12 `PlanRegistry`, A15)

- [ ] `PlanRegistry.Plans` becomes `IReadOnlyList<CommittedPlan>`. The save restore goes through
      `Record`.
- [ ] Move `DefaultVessel` from Core to the test project. The programs mock reads its ids another
      way.
- [ ] Move `DirectoryContentFileSystem` into Core next to `IContentFileSystem`, and delete the test
      copy.
- [ ] `Execute` rebuilds the snapshot once after a successful dispatch. The public command methods
      keep their own rebuild until the kernel tests move to `Execute`.
- [ ] Make `Units.Format` integer-only.
- [ ] Follow-up feature, not part of this plan: Save / Load in the shell, once R1 makes loading
      strict.

---

## Not in scope

- Any change to tick order, phase order, power granting or selection order.
- Any save-format version bump.
- Renaming persisted panel ids, content ids or `RngDomain` members.
- The program runtime, the robot domain, or anything else CLAUDE.md lists as designed but not built.
