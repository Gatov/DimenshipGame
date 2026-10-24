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
behaviour: the replay tests check that a hash line *exists*, not what it says. Pinning the hashes was
considered and declined, because the scheduling logic is still changing and a golden table would
churn with every tuning commit. R0 instead compares each refactor with the commit it starts from,
and every later step is held to that comparison.

## Global Constraints

- **Zero behaviour change, proved by R0.** Every step keeps all suites green **and** passes
  `tools/replay-diff.sh <the commit it starts from>`: every replay report identical, final-state
  hash included. A step that changes a report is not a refactor. It goes in its own commit, whose
  subject says behaviour changed.
- **Kernel invariants.** `Dimenship.Core` references neither Godot nor `Dimenship.Shell`, and has no
  `float` or `double` (`CoreAssemblyTests`). `Dimenship.Shell` does not reference `Dimenship.Core`.
- **Determinism.** Do not reorder anything that is iterated in declaration order. Do not restructure
  `Tick` into separate select / grant / advance passes: CLAUDE.md records that doing so reorders the
  journal. The phases stay where `Tick` calls them; only the files they live in change.
- **Save format.** No save DTO changes shape, and `saveVersion` stays 4, unless a step says otherwise.
- **Commits.** Conventional prefix plus a lowercase behavioural sentence, with prose bodies, per
  CLAUDE.md. Moves go in their own commits, apart from edits, so `git log --follow` and blame stay
  usable.
- **Toolchain.** The cloud container has no `dotnet` preinstalled, but Ubuntu's archive is
  reachable: `apt-get install -y dotnet-sdk-8.0` gives a working .NET 8 SDK (8.0.132 when this was
  written), and the three suites and the replays run on it. Microsoft's own download hosts are
  blocked by the network policy. The on-demand CI workflow (`.github/workflows/ci.yml`,
  `workflow_dispatch`) is the other option. Never record a step as verified without a run.

## Order

```
R0 ──► R1
   ├─► R4 (each item independent)
   └─► R2.1 ──► R3 ──► R2.2 ──► R2.3
R5, R6 (Godot layer) may proceed alongside the kernel work after R0
R7 last
```

---

### Task R0: A behaviour lock — Built

**Fixes:** nothing yet; this is the prerequisite for everything below.

**Decision:** compare against a base commit, never pin. A golden-hash table was the first draft
of this task, and it was dropped: the scheduling logic is far from settled, and a table that every
tuning commit has to regenerate gets regenerated without anyone reading it, which stops it
guarding anything. A comparison with the commit a refactor starts from asks exactly what a
refactor has to answer, and it needs no upkeep.

**Files:**
- Create: `tools/replay-diff.sh`

**Steps:**

- [x] `tools/replay-diff.sh [base-ref]` (default `master`) checks the base out into a temporary
      worktree, builds the replay harness there and in the working tree, and runs every
      `tools/Dimenship.Replay/scripts/*.json` under every policy (`queue-order`, `replenishment`,
      `improved`) on both builds. Both builds read the **working tree's** content and scripts, so
      only the code differs. It prints a unified diff for each report that differs, and exits 0
      only when all reports match and none failed to run. A replay that fails is reported, not
      compared, because two identical crashes would compare equal and prove nothing.
- [x] Self-check against `HEAD`: 48 identical (16 scripts × 3 policies), exit 0, about 5 minutes.
- [x] It catches a real change: doubling every facility's work rate in `SimulationEngine.WorkRate`
      made all 48 differ, with exit 1. (A first try, `/ 1000` → `/ 999`, changed nothing, because
      with a work rate of 1 the result still rounds down to 1 in integer division.)
- [x] Determinism across a save is already covered: the content tree ships one scenario,
      `default_vessel`, and `DeterminismSurvivesASave_WhichIsWhatCatchesAFieldLivingOnlyInTheEngine`
      runs it 500 + save + load + 500 against 1,000. No new test is needed until a second scenario
      ships.
- [ ] Revisit pinned hashes once the scheduling logic settles, if a refactor ever needs a
      reference older than its own base.

**Done when:** the script passes against `HEAD` and fails on a deliberate change. Done.

---

### Task R1: The save loader reports what it does not find (A3) — Built

**Decision:** the save gets its own reader, `SaveFields`. The content loader's helpers are not
moved into a shared one. Content errors carry a file as well as a path, and content enums are
refused by the serializer at parse time, while the save writes its enums as strings and resolves
them itself. A shared reader would have been thin wrappers over two different error shapes.

**Files:**
- Create: `src/Dimenship.Core/State/Save/SaveFields.cs`
- Modify: `src/Dimenship.Core/State/Save/WorldSave.cs`
- Modify: `tests/Dimenship.Core.Tests/State/WorldSaveTests.cs`

**Steps:**

- [x] `SaveFields`: `Value`, `Text`, `List` and `Branch` report `is required.` at the field's path
      and answer a placeholder, so one load reports every gap. `Enum` / `OptionalEnum` / `Named`
      accept only a declared member's exact name, refusing `"42"`, a comma-joined list and a
      wrong case.
- [x] Replaced the 94 `??` fallbacks and the 24 `Enum.Parse` calls in `WorldSave.Restore`. The
      condition and operand enums, which used bare `TryParse`, go through `Named` too. Every
      error path is index-based (`vessel.facilities[0].built`); the plan priority check used to
      name the plan by id.
- [x] Optional, because the state holds them as optional: name overrides, `configured`,
      `switchTarget`, `current`, `blockReason`, `lastReason`, the four task timestamps, a produce
      task's `runs` and a transfer's `quantity` (absent is a standing order), an attempt's
      `reason` / `byTask` / `byPlan`, a plan's `destination`, a mission's `forPlan`, an alert's
      `rootCause` / `relatedSubjectId`, and a robot's `group` / `onMission`.
- [x] One documented exception: a utilization window's `held` may be absent and loads as zeros,
      because it was appended after saves existed without a version bump (CLAUDE.md already says
      so). Every other bucket array is required.
- [x] Beyond missing fields: a bucket array whose length differs from `working`, and a `head`
      outside the buckets, are reported. Both loaded before and then threw on a later tick.
- [x] The envelope's `savedAtTick` (read by nothing, so never noticed) and `contentVersion` are
      required, and a missing `state` is reported at the path `state`.
- [x] Upgraders: no change needed. Every existing version-1 to version-3 test passes unchanged,
      so the three upgraders already supply what older versions lacked.
- [x] Tests: `EveryFieldTheSaveWrites_IsReportedWhenMissing_UnlessTheStateHoldsItAsOptional`
      walks a written save of a populated world (with conditions, a robot and an alert) and
      removes each field shape once. A required field must be reported at its own path or an
      ancestor's. An optional one must never be reported as required, though its absence may
      still be refused for what it means: dropping `runs` makes a standing order, which the claims
      check refuses. Because the walk reads the save rather than a hand-kept list, a field added
      later is covered the day it is written. Mutation-checked: putting back `Built = f.Built ?? true`
      fails it with `state.vessel.facilities[0].built: loaded without it`. Plus a bad enum name
      (four cases), a short bucket array, and a head out of range (two cases).
- [x] CLAUDE.md's save-format bullet now names `SaveFields` and the walk test.

**Done when:** no `Enum.Parse` and no default fallback is left in `State/Save/` beyond the
documented `held` and the two plan-membership `Held ?? false` reads that are checked right after,
the suites are green, and `replay-diff.sh` passes against the starting commit.

---

### Task R2: Split `SimulationEngine` (A1, A7)

#### R2.1 — Partial files, moves only — Built

- [x] Split into `SimulationEngine.cs` plus eleven partials. The core (396 lines) keeps the fields,
      constructor, public properties, `Advance`, `Tick`, `Emit`, `TotalOf`, `PriorityOf` /
      `IsHeld`, and the archetype, rate and queue resolution every phase reads. The rest:
      `.Storage` (255), `.Commands` (287), `.Validation` (216), `.WorldView` (165, with
      `AmendView`), `.Production` (584, with commissioning), `.Transport` (401), `.Power` (80),
      `.Claims` (201), `.PlanCommands` (630), `.Explanations` (259) and `.Snapshot` (315).
- [x] Pure moves, done mechanically with a throwaway Roslyn tool rather than by hand. It moved each
      member whole, with its leading comments, in its original relative order, and then checked
      that the twelve files hold exactly the original's 147 members, text for text. The only new
      lines are each file's usings, namespace and class line; each file keeps only the usings the
      compiler finds it needs (CS8019).
- [x] Commit: `refactor: the engine is read a phase at a time, one file per concern`.

**Done when:** no partial is over about 900 lines (the largest is 630) and `replay-diff.sh` passes
against the starting commit.

#### R2.2 — Stateless collaborators — Built

- [x] `StorageMath`: Available, Free, TotalOf, Room, RoomForDelivery and its reservation, the
      volume helpers, FillPermille, RoomAfterConsuming and Deposit / Withdraw / Set, with the
      storage, item and facilities-by-storage indexes. `Free` moved with it from the claims file,
      because the planner's view needs it and it is a storage reading.
- [x] `Set` throws `InvalidOperationException` on an unknown storage (A15), in its own commit.
      Every caller asks room, stock or a validated transfer first, and each answers nothing there,
      so it is unreachable today.
- [x] `SnapshotBuilder`: `Build(lastDelta)` and its helpers. The per-item change over the last
      tick is the one figure not in the state, so it is an argument. The hold capacity per item
      moved into its constructor.
- [x] `EngineWorldView`: the `IWorldView` implementation. `AmendView` is a decorator over any
      `IWorldView` plus the claim ledger.
- [x] **Departure: a fourth collaborator, `Resolver`.** The archetype lookups, work rate,
      throughput, belt capacity, switch-over ticks, `Queued`, `CurrentOf`, `PriorityOf`, `IsHeld`
      and the sinks are read by all three of the others. Handing them the engine to get these would
      have handed them its scratch too, which is what the split rules out.
- [x] **Departure: the engine keeps its surface.** Its public storage readings, `IsUnlocked`,
      `InHold` and its `IWorldView` implementation forward to the collaborators, so the shell, the
      harness, the controllers and the tests are unchanged. The private resolution helpers stay as
      one-line forwards, so the phases read as before; the 86 call sites were not rewritten.
- [x] All four are internal; the kernel tests see them through `InternalsVisibleTo`. Direct tests:
      `StorageMathTests` (4), `SnapshotBuilderTests` (2), `EngineWorldViewTests` (2), built by
      `Collaborators.Over` with no engine.
- [x] The `.Storage`, `.Snapshot` and `.WorldView` partials are gone; the engine is nine files.

#### R2.3 — `Amend` validates before it mutates — Built

- [x] `CutPlan(tasks)` is pure: each unfinished task and the action it would be cut to, in order.
      `CutBack` is now `ApplyCuts(CutPlan(tasks))`.
- [x] `Amend` plans against `EngineWorldView.AfterCut`, a view that reads each production task at
      its cut run count, wrapped in `AmendView`. It checks every replanned task with
      `RequireQueueable`, and only then applies the cut and appends. Only a production task's run
      count reaches the planner: a line's load counts unfinished transfers, and a cut finishes none
      until after the append, exactly as before.
- [x] `ApplyCut(task, action)` holds the `line.Current` rule, used by `CutBack` (so cancel and
      amend) and by `Move`.
- [x] Both undo blocks are deleted, and with them the `try` / `catch`.
- [x] **The bug was real.** Both undo blocks restored the scripts and never a line's `Current`,
      which the cut clears when it leaves a loading transfer entirely aboard.
      `ARefusedAmend_LeavesEveryTasksScript_AndEveryLinesTransferInHand_ExactlyAsTheyWere` fails
      on the old code with exactly that ("the line let go of a transfer still loading").
- [x] **Departure: the test is refused by approval, not by `RequireQueueable`.** No reachable world
      approves a draft that `RequireQueueable` then refuses: approval checks the same built,
      commandable, unlocked and route facts, so that check is a safety net. The approval refusal
      went through the same incomplete undo. The fixture is a workpiece plan committed directly
      with no destination, which the replanned draft refuses as `WorkpieceNotAccepted`. A locked
      schematic, the example `Amend`'s comment used to give, does not refuse at all
      (`LockedSchematic` is a supply issue), so the comment now names the workpiece case.
- [x] `AViewAfterACut_ReadsTheCutRunCounts_AndLeavesTheStateAlone` tests the view directly.

**Done when:** `replay-diff.sh` passes, the new test passes, and `Amend` contains no rollback code.

---

### Task R3: One executor interface (A2) — Built

**Depends on:** R2.1.

- [x] `internal interface IExecutorInstance` (Id, Built, Queue, Current, Status, BlockReason,
      PowerDrawLastTick) in `State/VesselState.cs`, implemented by `FacilityInstance` and
      `TransportInstance`. The classes stay sealed and the vessel's lists separate and in
      declaration order. A reactor has no queue and does not implement it.
- [x] The `executor.Current` / `chosen` difference is real, and is kept. `TryLoad` clears a line's
      `Current` the moment a transfer is entirely aboard, which can be the load that won, so a line
      must name its winner; a facility's `Current` always is its winner. The shared scan takes the
      winner as `chosen` and still skips whatever is in `Current`, which is exactly what both copies
      did. `AnOutrankedTransfer_NamesTheTransferThatLoaded_EvenWhenItWentEntirelyAboard` pins the
      line path, which nothing tested before.
- [x] `TopReadyPriority` and `RecordPassedOver` are written once in the core file, taking an
      eligibility test (a line also skips a transfer entirely aboard), a `Readiness` delegate and a
      reason-to-category function (a facility files by `CategoryFor`, a line files everything under
      logistics). The facility and line versions are now three-line wrappers.
- [x] Collapsed: `Queued`, `CurrentJob` / `CurrentTransfer` (now `CurrentOf`), and
      `FinishIfCutShort`'s two branches (through `ExecutorOf`, keeping only the different event).
- [x] **Not collapsed, on reading them.** The two `Postpone` overloads differ on purpose: a
      facility's records its `BlockReason`, and a line's must not touch the line, because blocked
      means cargo it cannot put down. `Tick`'s two standing-draw loops read different archetypes,
      and the line's also resets its intake and delivery counters, so merging them would interleave
      two concerns to save four lines. The `line.Current` clearing in `Move` and `CutBack` is
      R2.3's `ApplyCut`.
- [x] Timings: the replay suite ran in 53–55 s on the three runs before R3; see the commit for the
      run after.

**Done when:** `replay-diff.sh` passes and no executor helper exists in a facility copy and a line
copy, except where the two genuinely differ (the overloads above).

---

### Task R4: One home for each rule (A4, A5, A6, A8, A9, A11, A14) — Built

Built as six commits on one branch rather than six PRs, each with the suites green; one
`replay-diff.sh` run against `6baa4ec`, the commit before R4, covers them all. Where the build
departed from the plan, the entry says so.

- [x] **Blocking issues (A4).** `DraftIssueKinds.IsStructural()`, an exhaustive switch that throws
      on an unlisted kind, used by `PlanDraft.IsCommittable`, `PlanDraftEditor.MergeIssues` and
      `BuildQuote.For`. Named for the draft's own vocabulary (structural and supply), not
      `Blocks()`. A **third copy** turned up in the Godot shell (`OperationsFocus.PlanEditor`'s
      issue colours), and it had drifted: it lacked `WorkpieceNotAccepted`, so a blocking workpiece
      issue drew in the warning colour. It uses the shared rule now, which is the one visible change
      in R4. A test spells out both lists against every member.
- [x] **Line choice (A5).** `Planning/LineChoice.Best` and `LineChoice.Runs`, used by
      `ChooseTransport`, `SimulationEngine.Leg` and both `RouteExists`. Public, not internal: the
      kernel exposes no internals to its tests. Tested on the rule itself (load, then throughput,
      then declaration order, with placed legs counting as load), not by comparing a draft with a
      move, since both now call the same function.
- [x] **Commit returns its plan (A6).** `Commit` returns `Committed(PlanId Plan, Tasks)`, and
      Dispatch, the claim allocation inside `Commit` and the replay stop reading `Plans[^1]`.
      **Not done:** `Amend` keeps its signature (its dispatch slices `SpawnedTasks`, which is exact),
      and the test helpers keep `Plans[^1]` straight after their own commit, where it is correct
      and plain. A shared `TestPlans` would have been twenty sites of churn.
- [x] **Construction order (A8).** `Presentation/ConstructionOrder.For(archetype, slot, buffer)`,
      used by the inspector, the build composer (whose `BuildTarget` carries its order) and the
      test that commissions every shipped slot, a fourth hand-built copy. The replay keeps its
      one-line destination default, because a script names its own quantity.
- [x] **Milli-units (A9).** `Units.MilliPerUnit`. `WholeConstructionUnit` is kept, now public and
      defined from it, because commissioning's amount deserves its own name. `UnitsToMilli`,
      `LaunchPadUnits`, `MilliPerUnit` (Operations) and `WholeUnit` (inspector) are gone, and so
      is the bare `Units.Format(1000)`. `FromWhole` / `ToWhole` were not added: every site
      multiplies or divides once, and a helper would only rename that.
- [x] **World view read once (A11).** `Expansion` captures `Facilities` and `TransportLines` in
      its constructor and reads only those. Not indexed: the lists are a handful of entries, and
      the cost was rebuilding them, not scanning them. No speed-up was measured.
- [x] **Event inverse (A14).** One table in `PostponeReasons`, read both ways by `EventCodeOf` and
      `FromEventCode`; `CodeFor` delegates to it, and the event log colours any recognised
      postponement by its reason and keeps only its tags. Tests round-trip every journalled reason
      and check that exactly the `Postpone*` codes name one.

**Done when:** each commit is green and `replay-diff.sh` passes against `6baa4ec`.

---

### Task R5: A testable shell model (A10) — Built, pending a check in Godot

- [x] `src/Dimenship.ShellModel` (references Core and Shell, no Godot) and
      `tests/Dimenship.ShellModel.Tests`, in the solution, CI (restore, build, its own test step)
      and CLAUDE.md's layout. `ShellModelAssemblyTests` is the reflection guard: no Godot reference.
      The Godot assembly references the new project.
- [x] `ComposerModel` holds mode, targets, quantity, draft, the revision / undo stack with the
      quantity-edit session, the approve lock, the open plan and step, and `ToState()` /
      `FromState()` for `OperationsComposerState`, which moved out of `ShellContext` with it.
      `ComposerTargets` resolves the build and produce lists from content.
- [x] **Shape decided in the build:** the kernel is reached through three delegates the view binds
      on mount (`Compose`, `Adjust`, `Approver`), as the view read them off its context before.
      Choices (mode, target, quantity) only set state and unlock; the view follows each with
      `Refresh(snapshot, freshCompose: true)`, whose return says whether the build picker changed,
      so the picker is still rebuilt only then. The REPLANNED flash keeps its frame clock in the
      view, fed by the model's `Adjusted` event; `RevisionsCleared` clears it and hides the
      add-move row, which were the two things every revision clear did beside the model's work.
- [x] `ConfirmGate` (arm, confirm, disarm) replaces `_buildArmed` in the inspector and
      `_cancelArmed` in plan controls. It holds no subject: what disarms it stays with each owner.
- [x] Tests (13), against a real engine on the shipped vessel, driven as the view drives the
      model: no second APPROVE after the next snapshot recomposes; RE-ADJUST keeps the lock and
      UNLOCK ALL clears it; UX-01 park and restore keeps draft and undo and refreshes rather than
      recomposes; a quantity being typed is committed when parked; the selected slot commissioning
      falls back to the first and unlocks; another slot commissioning keeps the selection on its
      facility and the lock; a slot commissioning does not unlock a Produce order; an index past the
      end names the last target; discard resets both modes; undo and redo. Each was checked by
      putting the bug back: no lock, unlocking in Produce mode, restoring without tracking the goal,
      parking without committing the quantity, and clamping the index instead of following the
      facility each fail their test.
- [x] **Done early from R7:** `DirectoryContentFileSystem` moved into Core, both copies deleted, so
      the new test project did not add a third.
- [ ] Check in Godot that Operations and the inspector behave as before. Not possible in the
      container, which has no Godot; the assembly compiles.

The fields left in `OperationsFocus` are widgets, the model, the context, the last snapshot, and
view-only state: the flash clock, two render flags (`_addMoveVisible`, `_applyingDraft`), the
plan controls' move-option cache and the cancel gate. `OperationsFocus.cs` went from 1,533 to
1,246 lines and the editor partial from 1,053 to 892.

**Done when:** every field left in `OperationsFocus` is a widget or the model, and the composer
tests run without Godot.

---

### Task R6: Shell hygiene (A12 `ShellActions`, A13) — Built, pending a check in Godot

- [x] Six label styles as `ThemeTypeVariation`s in `ShellTheme.Build`, named by role beside the
      typeface spec's `Value` and `Prose`: `Caption`, `Note`, `Micro`, `Tag`, `Strong`,
      `Heading`, the six colour and size pairs a tally of the overrides found. One factory,
      `ShellTheme.Text(text, style)`. Views converted one area per commit: chrome, base graph,
      Operations, panels, the loadout mock, the programs mock.
- [x] The conversion was mechanical and conservative. A pair became a style only on a node
      declared as a `Label` with no other variation, so `Value` readings and a `Button` keep their
      overrides. A `font_size` of `FontBody` set alone was deleted, because it is the theme's
      default size and so was a no-op. No override in the shell is ever removed, so a style under
      an override never shows through. Inline overrides went from 120 `font_color` and 150
      `font_size` to 48 and 33: state colours, single-use pairs, and sizes on `Value` readings.
- [x] **Departure:** most private label factories stay. They set alignment, overrun or a colour
      passed in, and now use a style or keep that colour. The two that were a style and nothing
      else, plan controls' `Caption` and the editor's `ActionChip`, are gone.
- [x] `ShellMath.Fill(amount, capacity)` in `Dimenship.Shell`, with a test, replaces the four
      copies. Two had tested `== 0` and two `<= 0`; it keeps `<= 0`.
- [x] `Labels.Refusal(CommandRefused)` and `Labels.Refusal(PlanApprovalRefused)` replace both
      `"REFUSED:"` formatters. The one visible change: an approval refused with no issues reads
      `REFUSED`, not `REFUSED: `.
- [x] `ShellActions`' thirteen handlers are required init-only properties, built in one
      initializer by `ShellRoot.WireActions`; `ShellContext.ComposeDraft` / `AdjustDraft` too.
      Every `?.Invoke` on a handler is a direct call. `Handle` and `Suspended` are unchanged. The
      `IShellCommands` alternative was not taken: required members give the same guarantee
      without a second type.
- [ ] Check each changed view in Godot. Not possible in the container, which has no Godot; the
      assembly compiles.

---

### Task R7: Encapsulation and leftovers (A12 `PlanRegistry`, A15)

- [ ] `PlanRegistry.Plans` becomes `IReadOnlyList<CommittedPlan>`. The save restore goes through
      `Record`.
- [ ] Move `DefaultVessel` from Core to the test project. The programs mock reads its ids another
      way.
- [x] Move `DirectoryContentFileSystem` into Core next to `IContentFileSystem`, and delete the test
      copy. Done early, with R5: the harness's copy is gone too.
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
