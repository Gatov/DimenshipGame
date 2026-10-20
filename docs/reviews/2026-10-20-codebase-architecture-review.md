# Codebase review: architecture, structure and duplication

Date: 2026-10-20. Scope: `src/`, `tools/`, `dimenship/scripts/`, and the test helpers, at `ddf8ab1`.
About 34,300 lines of production C# and 13,200 of tests.

This review proposes; it changes no code. Each finding names the file and line it was read
at, what goes wrong if it is left, and the refactor that removes it. The plans at the end are
ordered so the riskiest one, splitting the engine, comes after a safety net that can prove it
changed nothing.

No `dotnet` toolchain was available in the session that wrote this, so every claim here comes
from reading the code, not from running it. Line counts come from `wc -l`.

---

## Summary

The kernel's rules hold up well: Core references neither Godot nor Shell, it has no floating
point, the data tiers are kept apart, and declaration order is respected. The weak points are
size and duplication, not layering:

| # | Finding | Kind | Severity |
| :--- | :--- | :--- | :--- |
| A1 | `SimulationEngine` is 3,695 lines and does ten jobs | Structure | High |
| A2 | Facilities and lines share no interface, so most executor helpers are written twice | Structure / copy-paste | High |
| A3 | The save loader fills 94 missing fields with defaults and lets an unknown enum throw | Contract breach | High |
| A4 | The rule that decides which draft issues block approval is written twice | Copy-paste, can drift | High |
| A5 | The line-choice rule is copied from the planner into `SimulationEngine.Move` | Copy-paste, can drift | Medium |
| A6 | `Commit` returns no plan id, so callers read `Plans[^1]` | API shape | Medium |
| A7 | `Amend` mutates first and rolls back by hand, against the C0 "validate, then mutate" rule | Fragility | Medium |
| A8 | The construction goal for a slot is built in three places | Copy-paste | Medium |
| A9 | One unit (1000 milli-units) is defined under four names, plus one bare literal | Copy-paste | Medium |
| A10 | `OperationsFocus` holds 82 fields and 109 methods, and none of it is unit-tested | Structure | Medium |
| A11 | The engine's `IWorldView` lists are rebuilt on every property read, and the planner reads them in loops | Efficiency | Low–Medium |
| A12 | Mutable collections are exposed: `PlanRegistry.Plans`, and `ShellActions`' public delegate fields | Encapsulation | Low–Medium |
| A13 | Repeated shell boilerplate: four `Fill` clamps, about ten styled-label factories, 265 inline theme overrides | Copy-paste | Low |
| A14 | `EventCode` mirrors `PostponeReason`, and the event log maps it back by hand | Parallel enum | Low |
| A15 | Smaller items: `DefaultVessel` sits in Core, `DirectoryContentFileSystem` exists twice, `Set` ignores an unknown storage, a save can never be loaded from the game | Misc | Low |

---

## A1. `SimulationEngine` is a god class

`src/Dimenship.Core/Simulation/SimulationEngine.cs` is 3,695 lines in one `sealed class`. That
is more than the next three Core files together (`WorldSave` 1,480, `PlanDraftEditor` 1,441,
`JsonContentSource` 1,385). Reading its method list, it does at least ten separable jobs:

| Concern | Methods (line where they start) | ≈ lines |
| :--- | :--- | ---: |
| Storage arithmetic | `Available` 129, `Room` 157, `RoomForDelivery` 181, `ReservedVolume`, `OccupiedVolume`, `VolumeOf`, `RoomIn`, `CapacityOf` 3670, `RoomAfterConsuming` 3679, `Deposit` / `Withdraw` / `Set` 959–987 | 250 |
| Command dispatch and validation | `Execute` 324, `Dispatch` 336, `RequireQueueable` 445, `RequireProducible` 504, `RequireHaulable` 545, `RefuseUnboundOperands` 713, `WrongKindOrMissing`, `TargetExists` | 350 |
| The planner's view of the world | `IWorldView.Facilities` 790, `TransportLines` 865, `InHold` 901, `AmendView` 3005 | 160 |
| Production phase | `StepProducer` 1249 … `TryDeposit` 3033, `RunCharge` 2111 | 900 |
| Transport phase | `StepHauler` 1572 … `TryLoad` 1896 | 380 |
| Power | standing draw in `Tick`, `GrantPowerToRunsInProgress` 2137 | 80 |
| Claims | `Free` 2179 … `Reassign` 2345, `ClaimInvariantViolations` 2280 | 200 |
| Plan commands | `Hold` 2375, `Release`, `Cancel` 2422, `Amend` 2461, `Move` 2598, `CutBack` 2898, `FinishCutShort` | 650 |
| Explanations | `Postpone*` 3088–3157, `ClaimantOf`, `HolderOfRoom`, `LargestHolder`, `ReviewWaitingPlans` 1125, `WaitingBehind` | 280 |
| Snapshot projection | `BuildSnapshot` 3243 … `Cargo` 3552 | 330 |

**Why it matters.** Every kernel ticket (K2, K5c, K6a–d, K8) has touched this file, and every
reviewer has to hold all of it in their head, because any private helper might be called from
any phase. Two problems follow. A change to one phase can break another without a type boundary
to show it: `CutBack` clears `line.Current`, which is the transport phase's invariant, from
inside a plan command. And the file is now too big to review in one sitting, which is how the
copies in A2, A5 and A7 got in.

**Refactor:** plan R2. Start with `partial` files, which change nothing at runtime, then pull the
stateless pieces out into collaborators.

## A2. Facilities and lines share no interface

`FacilityInstance` (`State/VesselState.cs:253`), `TransportInstance` (`:342`) and
`ReactorInstance` (`:442`) each declare `Id`, `Archetype`, `NameOverride`, `Built`, `Status`,
`BlockReason`, and all but the reactor also `Queue`, `Current` and `PowerDrawLastTick`. Nothing
ties these together, so every operation that works on "an executor" is written twice:

- `TopReadyPriority(FacilityInstance, …)` at 1402 and `TopReadyPriority(TransportInstance)` at 1783
- `RecordPassedOver(FacilityInstance, …)` at 1429 and `RecordPassedOver(TransportInstance, …)` at 1803. The two loops are the same except for the readiness predicate.
- `Queued(FacilityInstance)` 3601 / `Queued(TransportInstance)` 3613, `CurrentJob` / `CurrentTransfer` 3624 / 3627, and both `Postpone` overloads 3088 / 1950
- `FinishIfCutShort` (2961) branches on `_facilitiesById` versus `_linesById` to do one thing to either: clear `Current`, emit, `Retire`
- the standing-draw loop in `Tick` (1007–1036)
- `WorldSave.Restore` (686–760) repeats the `Status` / `BlockReason` / `Queue` restore for each kind

**Why it matters.** The K2 priority rule has to be kept in step across both copies by hand. The
transport copy already differs in a way that looks accidental: it takes `chosen` as a parameter,
where the facility copy reads `executor.Current`. The next rule added to selection (K8's
setup-preference explanation is the one already on the list) will need writing twice again.

**Refactor:** plan R3. Add a small `IExecutorInstance` interface over the shared members. Keep
the classes sealed and the lists separate (declaration order is the contract), and write the
priority-tier scan once, parameterised by the readiness predicate.

## A3. The save loader breaks its own contract

CLAUDE.md says: *"Every DTO field is nullable, so a missing field is a reported error rather than
a silent default."* `State/Save/WorldSave.cs` does not do that:

- It contains **94** `?? 0` / `?? 1000` / `?? true` / `?? string.Empty` fallbacks. Examples:
  `Built = f.Built ?? true` (695), `WorkRatePermille = f.WorkRatePermille ?? 1000`,
  `LengthTicks = t.LengthTicks ?? 1`, and `new StorageId(s.Id ?? string.Empty)`. A save missing
  `built` loads as built, with no error. A save missing a stock `amount` loads as zero, which
  quietly removes material, the exact outcome the belt-drift rule exists to report.
- It calls **`Enum.Parse` 24 times** (703, 704, 739, …) with no try/catch. An unknown status
  string throws `ArgumentException` out of `Read`, so the caller gets an exception instead of a
  collected `SaveError`. `Enum.Parse` also accepts a numeric string such as `"42"` and returns an
  undefined enum value without complaint.

The content loader already has the right helpers (`Required`, `Flag`, `Positive`, `NonNegative`
at `JsonContentSource.cs:1331–1385`). The save loader, written beside it, does not use them.

It is not urgent only because nothing in the game calls `WorldSave.Read` yet (see A15): the only
callers are tests. It will matter the day the shell gets a Load button.

**Refactor:** plan R1. Add a shared `FieldReader` (path-aware `Required<T>`, `Enum<T>` through
`TryParse` with an `Enum.IsDefined` check, `Positive`, `Permille`), used by both loaders. A
missing field becomes a `SaveError` with its path. An upgrader that really wants a default (the
v3→v4 released flag) sets it explicitly in the DTO, where it is visible and dated.

## A4. The rule for blocking draft issues is written twice

Which `DraftIssueKind`s block approval is listed in
`Planning/Draft/PlanDraft.cs:117` (`IsCommittable`) and again, member for member, in
`Planning/Draft/PlanDraftEditor.cs:102` (`IsStructuralKind`, which decides whether a repeated
issue replaces or accumulates). `Presentation/BuildQuote.cs:88` needs to classify one issue and
cannot call either, so it builds a throwaway draft with a single-issue array to ask
`IsCommittable`. Its comment says it does this "so which kinds block stays decided in one place".
There are already two places.

**Failure it invites:** add a structural kind to one list and not the other. If it goes into
`IsCommittable` only, its quantities accumulate across edits. If it goes into
`IsStructuralKind` only, approval waves it through.

**Refactor:** part of plan R4. Add one `public static bool Blocks(this DraftIssueKind)` (or
`DraftIssue.Blocking`) next to the enum, and call it from all three sites.

## A5. The line-choice rule is copied into `Move`

CLAUDE.md fixes the rule: *least load, then highest throughput, then declaration order.* It is
implemented in `PlanDraftEditor.ChooseTransport` (1158) and again in
`SimulationEngine.Leg` (2799), whose comment says so ("on the line the planner would choose").
`PlanDraftEditor` also has two copies of `RouteExists`, an instance one (1215) and a static one
(310), with identical bodies.

**Failure it invites:** the next tuning of the rule (K5b already moved throughput ahead of
declaration order once) changes the planner, and `MoveWork` goes on placing legs the old way.
No test compares the two.

**Refactor:** part of plan R4. Add one `internal static PlannerTransport? LineChoice.Best(lines,
from, to, extraLoad)` in `Planning/`, called by both. The planner keeps its `_routeLine` cache
and locked-line handling around that call.

## A6. `Commit` doesn't return a plan id

`SimulationEngine.Commit` returns the created `TaskId`s and no `PlanId`. So callers find the plan
by position, assuming the last one in the registry is theirs:

- `SimulationEngine.cs:353`, `:358` and `:640`
- `tools/Dimenship.Replay/Replay.cs:363`, even though the `CommandAccepted` it holds already carries `Plan`
- five test helpers named `Commit(...)` that end `return engine.State.Plans.Plans[^1];`
  (`ClaimTests`, `PlanCommandTests`, `ExplanationTests`, `MoveWorkTests`, `FacilityQueueTests`)

`AmendPlan` (Dispatch, around 386) works out which tasks were appended by counting
`SpawnedTasks` before and after.

**Refactor:** part of plan R4. `Commit` and `Amend` return a small `Committed(PlanId Plan,
IReadOnlyList<TaskId> Tasks)` record. Dispatch, the replay and the tests read it, and the shared
test helper moves into one `TestPlans` fixture.

## A7. `Amend` mutates first, then rolls back

The C0 rule in CLAUDE.md is that *every command validates before it mutates*. `Amend` (2461)
calls `CutBack` first, which rewrites every task's script and can clear a line's `Current`, and
only then plans and validates. It undoes the cut by replaying saved scripts, and that undo block
is written twice, once for a refused approval (2484) and once for a failed `RequireQueueable`
(2501). The undo restores scripts but not `line.Current`, which `CutBack` may already have
cleared (2919). Whether that is ever reachable depends on which transfers are fully loaded at the
moment of the cut. That makes it the kind of edge only a test written for it would find.

The cut-task loop is also a copy: `Move`'s loop (2708–2729) and `CutBack`'s (2905–2925) share the
`TaskAction` rewrite and the `line.Current` clearing word for word.

**Refactor:** part of plan R2.

1. Write a pure `CutPlan` that computes the cut scripts without applying them.
2. Plan against an `AmendView` that reads those scripts.
3. Validate.
4. Apply the cut and the appended tasks together.

The shared `ApplyCut(task, newAction)` then holds the `line.Current` rule once.

## A8. The construction goal for a slot is built three times

"Order one construction unit into the slot's own buffer, targeting the slot" is a domain rule.
Its own comment in the replay explains why: without it *"the unit lands in the hold and the
facility never builds"*. It is assembled separately in:

- `dimenship/scripts/ui/panels/FacilityInspectorPanel.cs:252`: `new ItemAmount(unit, WholeUnit)`, `executor.LocalStorage`, `executor.Id`
- `dimenship/scripts/ui/focus/operations/OperationsFocus.cs:805`: `LaunchPadUnits * UnitsToMilli`, plus `BuildTarget.Destination` / `.Facility`
- `tools/Dimenship.Replay/Replay.cs:339`: looks up `LocalStorage` from `State.Vessel.Facilities`

**Refactor:** part of plan R4. Add `Presentation/ConstructionOrder.For(catalog, snapshot, slot)`,
returning `(ItemAmount Goal, StorageId Destination, ExecutorId Target)`, next to
`ConstructionProgress`. All three callers use it. It also gives the "one whole unit" figure a
home in Core (see A9).

## A9. One unit is defined under four names

| Where | Name |
| :--- | :--- |
| `SimulationEngine.cs:1217` | `WholeConstructionUnit = 1000` |
| `OperationsFocus.cs:46` | `UnitsToMilli = 1000` |
| `OperationsFocus.PlanEditor.cs:20` | `MilliPerUnit = 1000` (the **same partial class** as the line above) |
| `FacilityInspectorPanel.cs:44` | `WholeUnit = 1000` |
| `FacilityInspectorPanel.cs:224` | `Units.Format(1000)`, a bare literal beside the constant |

`Units` already holds `TicksPerMinute` and `TicksPerHour` for exactly this reason (its comment
tells the story of a debug item that advanced a minute under an "hour" label). Milli-units deserve
the same.

**Refactor:** part of plan R4. Add `Units.MilliPerUnit` and `Units.FromWhole(long)` /
`Units.ToWhole(long)`, and delete the four local constants.

## A10. `OperationsFocus` is a god widget

`dimenship/scripts/ui/focus/operations/` is split into three `partial` files (1,533 + 1,069 + 328
lines), but it is still one object with **82 private fields and 109 methods**. Those fields mix
three things:

- **Composer state:** mode, target index, quantity, draft, undo/redo stack, approve lock,
  selected plan, arm-to-cancel.
- **Widget references:** about 50 `Label`, `Button`, `VBoxContainer`, … fields.
- **Snapshot-derived caches:** `_buildTargets`, `_visibleBuildTargets`, `_visibleBuildIds`,
  `_produceTargets`.

The composer's behaviour has been the subject of several fixes (UX-01, the double-APPROVE lock,
target clamping). It is the logic most worth testing, and the only way to exercise it is to boot
Godot. `FacilityInspectorPanel` (975 lines, 19 fields) has the same shape, and repeats its
two-press "arm, then confirm" logic (`_buildArmed`, 51 and 721) alongside Operations'
`_cancelArmed` (PlanControls 55 and 212).

The obvious home for a testable composer model, `Dimenship.Shell`, may not reference Core, and
the model needs `PlanDraft`. That is a missing layer, not a reason to keep the logic in Godot.

**Refactor:** plan R5. Either add a `Dimenship.ShellModel` project (references Core and Shell,
no Godot, has a test project), or put the models in `Core/Presentation` beside `BuildQuote` if
they only read. Then move into it the `ComposerModel` (mode, target, draft, undo/redo, approve
lock, the `OperationsComposerState` round trip) and a two-line `ConfirmGate` (arm, confirm,
disarm on any other input).

## A11. The engine's `IWorldView` lists are rebuilt on every read

`IWorldView.Facilities` (`SimulationEngine.cs:790`) builds a fresh `List<PlannerFacility>` on
every `get`, walking every facility's queue and computing setups. `TransportLines` (865) does the
same for lines. Both look like properties, so `PlanDraftEditor` reads them nine times
(910, 1071, 1087, 1114, 1128, 1141, 1154, 1191, 1217), several of them inside `FindFacility`,
`FindTransport`, `FastestLine`, `ChooseTransport` and `RouteExists`, which run once per step.
The constructor (`Expansion`, 353) already copies a few dictionaries out of the view, which shows
the intent was to read once.

This is not slow today on a vessel of nine facilities. But the inspector recomposes a build
quote on **every snapshot** while an unbuilt slot is selected (`FacilityInspectorPanel.cs:246`),
and the cost grows with queue length times step count.

**Refactor:** part of plan R4. Have the `Expansion` capture `Facilities` and `TransportLines`
once, and index them by id and by `(from, to)`. Turning the interface members into methods
(`GetFacilities()`) would also stop the next caller reading them in a loop by accident.

## A12. Mutable collections are exposed

- `PlanRegistry.Plans` is a public `List<CommittedPlan>` (`State/Ledgers.cs:261`), while the
  `_owners` index beside it is updated only by `Record` / `Append`. Any `Plans.Add` (a test, a
  future save path) leaves `Owning` silently wrong, and `PriorityOf` and `IsHeld` read `Owning`
  on every selection. Expose an `IReadOnlyList` and keep `Record` as the only way in.
- `ShellActions` (`dimenship/scripts/ui/ShellActions.cs`) has 14 **public mutable delegate
  fields** (`public Action? PauseToggled;` …, `public Func<PlanDraft, PlanApproval>?
  PlanApproved;`). Each has one slot, so a second `=` silently replaces the first handler, and
  each is nullable, so every call site writes `?.Invoke` and a wiring mistake becomes a button
  that does nothing. `ShellContext.ComposeDraft` / `AdjustDraft` are nullable `Func`s for the
  same reason. Give them a constructor that requires the handlers, or a narrow interface
  (`IShellCommands`, implemented by `ShellRoot` / `SimulationDriver`) passed in once.

## A13. Repeated shell boilerplate

- The **`Fill(amount, capacity)` clamp** appears in four places: `NodeCard.cs:315`,
  `FacilityInspectorPanel.cs:755`, and twice inline in `EnergyBudgetPanel.cs` (73, 238).
- **About ten private styled-label factories** do one job: `ProgramList.Text`,
  `SlotControl.Word`, `RuleCanvas.Notice`, `BlockView.Note`, `PlanControls.Caption`,
  `PlanEditor.MetaLabel` / `ActionChip`, `RollupGrid.Cell`, `FrameChooser.Micro`, `NodeCard.Row`.
- **265 inline theme overrides** across `dimenship/scripts`: 117
  `AddThemeColorOverride("font_color", …)` and 148 `AddThemeFontSizeOverride("font_size", …)`.
  `ShellTheme` already has a vocabulary for boxes (pane, box, card, chip, meter, divider) and
  none for text. Godot's `ThemeTypeVariation` (`"MicroLabel"`, `"DimLabel"`, `"CaptionLabel"`)
  is the native place for it, and CLAUDE.md already says *a new control type is themed there,
  not at the call site*.
- The two `"REFUSED: …"` formatters (`PlanControls.cs:243`, `FacilityInspectorPanel.cs:734`)
  word a refusal differently: one shows the kernel's reason and the issues, the other only the
  issues. A `Labels.Refusal(CommandRefused)` / `Labels.Refusal(PlanApprovalRefused)` pair would
  match `Labels.Issue`'s "worded once" rule.

**Refactor:** plan R6, mechanical and low-risk, and best done one view at a time.

## A14. `EventCode` mirrors `PostponeReason`

Every `PostponeReason` has a `Postpone*` `EventCode`. `SimulationEngine.CodeFor` (934) maps
reason to code, and `EventLogPanel.cs:173–181` maps code back to reason to get its colour. The
next `PostponeReason` needs an edit in the enum, `CodeFor`, `EventCode`, the event log,
`Conditions.Describe`, `Conditions.Colour` and `UtilizationWindow.CategoryOf`. Only the last
throws if forgotten. The event log's fallback is silent.

Changing the event schema would move journal contents and replay hashes, so the cheap fix is the
right one. Put the inverse (`PostponeReasons.FromEvent(EventCode)`) beside `CodeFor` in
`Simulation/Ids.cs`, and add a test that round-trips every member. Then a forgotten mapping fails
a test rather than a player's log colour.

## A15. Smaller items

- **`DefaultVessel` is test support living in the kernel.** `Simulation/DefaultVessel.cs` holds
  the shipped vessel's ids as constants. Nothing in Core uses it: its callers are 15 test files
  and the Godot programs mock. That makes the kernel assembly name one particular content tree.
  Move it to the test project (and give the mock its own copy, or read the ids from
  `ShellContent.DefaultVessel`).
- **`DirectoryContentFileSystem` exists twice**, in `tools/Dimenship.Replay/` (public) and
  `tests/Dimenship.Core.Tests/Content/ContentTree.cs` (internal), with the same body. It uses
  only `System.IO`, so it can live in Core next to `IContentFileSystem`.
- **`SimulationEngine.Set` (970) silently does nothing for an unknown storage.** `Deposit` and
  `Withdraw` go through it, so a bad storage id destroys or creates material with no error. Every
  other "can't happen" in the engine throws. This one should too.
- **A save can never be loaded from the game.** Outside tests, `WorldSave.Read` has no caller,
  and `WorldSave.Write`'s only caller is the replay's final hash. That is a product gap rather
  than a defect, but it explains why A3 has not been noticed, and it should be closed after R1,
  not before.
- **`Units.Format` divides with `decimal`** (`milliUnits / 1000m`). It passes the no-float guard
  and is presentation-only, but it is the one non-integer arithmetic in Core. Integer
  `quotient.remainder` formatting would make the assembly honestly integer-only. Low priority.
- **Every engine command rebuilds the whole snapshot itself** (14 `Snapshot = BuildSnapshot()`
  sites). Rebuilding once in `Execute` after a successful dispatch removes 12 of them and the risk
  that a new command forgets to. The public command methods the kernel tests call directly would
  keep their own rebuild until those tests move to `Execute`.

---

## Refactoring plans

Ordered by what each one protects. Every plan below must leave all three suites green **and
every replay script's final SHA-256 unchanged**, since those hashes are the closest thing this
repo has to a proof of zero behaviour change. R0 exists to make that checkable.

### R0. A behaviour lock first (prerequisite, about half a day)

**Why:** `ReplayTests` checks that a report *contains* a SHA-256 line, but no golden value is
pinned. A refactor could change the journal or the tick order and every suite would stay green.

1. Add `tests/Dimenship.Replay.Tests/GoldenHashTests.cs`: for each `tools/Dimenship.Replay/scripts/*.json`,
   under each of `Policies.Names`, assert the final SHA-256 against a checked-in table.
2. Add one `DeterminismSurvivesASave`-style test per shipped scenario that advances 500, saves,
   loads and advances 500, compared with advancing 1,000 in one go.
3. Write down the rule in the commit body: these hashes change only in a commit whose subject says
   behaviour changed.

**Done when:** a deliberate one-tick reorder in `Tick` turns the golden test red.

### R1. Make the save loader keep its contract (fixes A3, about a day)

1. Pull `Required` / `Flag` / `Positive` / `NonNegative` out of `JsonContentSource` into an
   `internal static class FieldReader`, generic over the error sink (`ContentError` and
   `SaveError` both reduce to a path and a message).
2. Add `FieldReader.Enum<T>(string?, path, errors)`: `Enum.TryParse` + `Enum.IsDefined`,
   rejecting numeric strings.
3. Replace the 94 fallbacks in `WorldSave.Restore` and the 24 `Enum.Parse` calls. Where a value
   is legitimately optional (`NameOverride`, `Configured`, `Current`), keep it nullable on the
   state type. Do not default it.
4. Where an old save version really lacked a field, the upgrader supplies it explicitly.
5. Tests: one per DTO type that removes a field and expects a `SaveError` at that path, and one
   with `"status": "Bogus"` and one with `"status": "42"`.

**Risk:** a current save that relied on a default stops loading. That is the point, but it means
checking the upgraders cover every field added since version 1.

### R2. Split `SimulationEngine` (fixes A1, A7; staged, about 3–4 days)

**Stage 1 — partial files, zero logic change.** Split the engine into
`SimulationEngine.{Storage,Commands,Validation,WorldView,Production,Transport,Power,Claims,PlanCommands,Explanations,Snapshot}.cs`,
the same way `OperationsFocus` is already split. Moves only, as one commit, so `git log
--follow` and blame stay usable. The diff should be pure moves, and R0's hashes must be
identical.

**Stage 2 — extract the stateless collaborators.** Only these three, because they read state
and the engine's indexes but own no tick-ordering decisions:

- `StorageMath` (Available, Room, RoomForDelivery, volumes, Deposit/Withdraw/Set), constructed
  over the storage index and the catalog. `Set` throws on an unknown storage (A15).
- `SnapshotBuilder`: `BuildSnapshot` and its helpers. It sits naturally beside
  `Presentation/`, and making it a separate type also stops the snapshot reading engine scratch
  state (`_powerGrants`, `_starvedThisTick`) by accident.
- `EngineWorldView`: the `IWorldView` implementation, with `AmendView` as a decorator over it.

**Stage 3 — make `Amend` validate first.** Write `CutPlan` (pure: the cut scripts per task),
plan against a view that applies them virtually, validate, then apply. One `ApplyCut` holds the
`line.Current` rule for both `CutBack` and `Move`. Delete the duplicated undo blocks.

**Deliberately not done:** do not split the tick into separate select/grant/advance passes, and
do not reorder the production and transport phases into strategy objects that iterate on their
own. CLAUDE.md records that the power pass's position is load-bearing, and that a literal
three-pass rewrite reorders the journal. The phases stay methods on the engine and called from
`Tick` in today's order. Only their files change.

**Done when:** no file in `Simulation/` is over about 900 lines, R0 is identical, and each
collaborator has at least one direct unit test.

### R3. An executor interface (fixes A2, about a day, after R2 stage 1)

1. Add `internal interface IExecutorInstance { ExecutorId Id; bool Built; List<TaskId> Queue;
   TaskId? Current; ExecutorStatus Status; PostponeReason? BlockReason; long PowerDrawLastTick; }`,
   implemented by `FacilityInstance` and `TransportInstance`. The classes stay sealed, and
   `VesselState`'s lists stay separate and in declaration order.
2. Write `TopReadyPriority` and `RecordPassedOver` once, taking a readiness delegate and an event
   category. Resolve the `chosen` / `executor.Current` difference first: confirm it is a real
   behaviour, and write it down if so.
3. Collapse `Queued`, `Current*`, both `Postpone` overloads, `FinishIfCutShort`'s two branches,
   and `Tick`'s two standing-draw loops.
4. Optionally give `WorldSave` one `RestoreExecutorCommon` helper over the shared DTO fields.

**Risk:** an interface call in the per-tick loop. That is negligible at this scale, but check with
the replay timings before and after.

### R4. One home for each rule (fixes A4, A5, A6, A8, A9, A11, small PRs)

Each bullet is an independent PR of tens of lines:

- `DraftIssueKind.Blocks()`, used by `PlanDraft.IsCommittable`, `PlanDraftEditor.MergeIssues` and `BuildQuote`.
- `LineChoice.Best(...)`, used by `PlanDraftEditor.ChooseTransport` and `SimulationEngine.Leg`. Delete the second `RouteExists`. Add a test asserting `MoveWork` and a fresh draft choose the same line for the same route and load.
- `Commit` / `Amend` return `Committed(PlanId, Tasks)`. Dispatch, the replay and the tests stop reading `Plans[^1]`. A shared `TestPlans.Commit(engine, goal)` helper replaces the five copies.
- `ConstructionOrder.For(...)` in `Presentation/`, used by the inspector, Operations and the replay.
- `Units.MilliPerUnit` / `FromWhole` / `ToWhole`. Delete `WholeConstructionUnit`, `UnitsToMilli`, `MilliPerUnit`, `WholeUnit` and the bare literal.
- `Expansion` captures `Facilities` / `TransportLines` once, indexed by id and by route.
- `PostponeReasons.FromEvent` beside `CodeFor`, plus a round-trip test (A14).

### R5. A testable shell model layer (fixes A10, about 2–3 days)

1. Decide where it lives. Recommended: a new `src/Dimenship.ShellModel` (references Core and
   Shell, no Godot) with `tests/Dimenship.ShellModel.Tests`. This keeps `Dimenship.Shell`
   Core-free, as CLAUDE.md requires, and stops `Core/Presentation` from collecting
   interaction state, which it has so far avoided (everything there is a read projection thrown
   away after use).
2. Move `ComposerModel` out of `OperationsFocus`: mode, targets, quantity, draft, the
   revision/undo stack, the approve lock, and `ToState()` / `FromState(OperationsComposerState)`.
   `OperationsFocus` keeps the widgets and renders the model.
3. Add `ConfirmGate`, used by inspector BUILD and plan CANCEL.
4. Test the behaviours that have regressed before: UX-01 park/restore, no double APPROVE,
   clamping the target index when a pad commissions and drops out of the list.

**Done when:** `OperationsFocus.cs` has no field that is not a widget or the model, and the
composer tests run without Godot.

### R6. Shell hygiene (fixes A12's `ShellActions` and A13, mechanical)

1. Add `ShellTheme` label variations (`ThemeTypeVariation`) and one `ShellTheme.Text(string,
   variation)` factory. Replace the private label factories and inline overrides one view per
   commit.
2. Add `ShellMath.Fill(amount, capacity)`, replacing the four copies.
3. `Labels.Refusal(...)`, replacing both `"REFUSED:"` sites.
4. `ShellActions`: make the handlers required in the constructor, or move them behind an
   `IShellCommands` interface. Keep the `Handle(InputEvent)` table and `Suspended` exactly as they
   are.

**Risk:** visual regressions only. Check each view by eye in Godot, since no suite covers it.

### R7. Encapsulation and leftovers (A12's `PlanRegistry`, A15)

- `PlanRegistry.Plans` becomes `IReadOnlyList<CommittedPlan>`. The save restore goes through `Record`.
- Move `DefaultVessel` to the test project. Move `DirectoryContentFileSystem` to Core, deleting the copy in tests.
- `Execute` rebuilds the snapshot once after a successful dispatch.
- Integer-only `Units.Format`.
- Then, as its own feature, wire Load / Save into the shell, now that R1 makes loading strict.

### Suggested order

`R0 → R1 → R4 (any order, in parallel) → R2 stage 1 → R3 → R2 stages 2–3 → R5 → R6 → R7`.

R0 gates everything. R1 and R4 are small, independent and remove the drift-prone copies before
the large moves make them harder to see. R2 stage 1 goes before R3 so the interface work happens
in files small enough to review. R5 and R6 touch only the Godot layer and can proceed
alongside the kernel work, once someone can run the game to check them.

## What is good and should be kept

Keep these intact through any of the above:

- the reflection-enforced Core invariants
- the four-tier data model and the `user:` id space
- determinism by declaration order
- the collected-error loader
- the `Presentation/` projections as single sources of wording and state (`ConstructionProgress`, `ExecutorCondition`, `WaitCause`)
- `Labels.Issue` / `Conditions.Describe` as the one place a thing is worded
- `Execute` as the single command door

Most of the duplication above is a place where that same discipline wasn't applied yet. None of
it requires a new idea.
