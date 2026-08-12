# Programming View — Design

Date: 2026-08-11
Status: Draft

## Goal

Replace the `doctrine` placeholder with the programming view: a rule-card editor in which the
player authors **programs** — reusable automation objects that watch vessel state and issue
bounded commands — plus the deterministic program runtime in `Dimenship.Core` that executes them.

Two things ship together because neither is useful alone:

1. **The program system.** A serializable program model, a bounded condition and action
   vocabulary, a validator, and a deterministic evaluation phase inside the tick. Engine-free of
   Godot, integer-only, unit-tested.
2. **The editor.** The rule canvas, the block palette, the programs list, and the trace dock.

An editor that edits nothing would be a placeholder with more pixels, and a runtime with no
surface would be a system the player cannot reach. The split against the *rest* of the concept is
the other way round: where the concept shows a subsystem that does not exist — drones, missions,
reactor fuel, research, compute cost — the vocabulary simply does not contain it, rather than
containing a word that does nothing.

## Source material

Three documents and one concept image supplied by the project owner:

- **`docs/Dimenship Programming v0.1.md`** — the gameplay design: why programming exists, the
  interface layers, programs as objects, what they help with, conflict as gameplay, progression,
  debugging, and the open design decisions. Authoritative on intent.
- **`docs/Game Design v0.9.md`** — §5 and §8.2 in particular. The SCADA schematic "does not become
  a programming surface for automation logic" (§5, *Explicitly out of scope*), and §7.4 puts
  doctrine management in its own screen. This spec builds that screen.
- **`docs/Dimenship programming.png`** — the operational console showing the programming view: a
  programs list, a rule canvas, a block palette, and a four-box bottom dock.
- **`docs/specs/dimenship-planning-and-task-execution.md`** — binding on what an executor will and
  will not accept, and therefore on what an action is allowed to be.

The concept is direction, not a pixel target. Its label text is generated and frequently garbled
(`ELSE F`, `ECSS`, `Mediers (50)`, `Aduht`, `Pause Non-Coursal Jean`, `Refined Allay`); the
*structure* is read as authoritative and the *strings* are not. Six items were reviewed against
the design documents and the existing kernel before this document was written; their resolutions
are recorded below and bind the rest of it.

### Concept review outcomes

| # | Item | Resolution |
| :--- | :--- | :--- |
| 1 | Two action fill colours (green and purple) | **One action colour.** The same action, `set priority to`, appears green at concept rule 15 and purple at rule 20. The second hue encodes nothing; it is generation noise. Block colour encodes *category* — control, condition, action, branch — and nothing finer. |
| 2 | `SYNTAX CHECK` box | **Renamed to `VALIDATION`.** Every slot is a dropdown or a bounded number field, so a syntactically invalid program cannot be constructed. What the box actually reports is semantic: an unknown target, an out-of-range value, an empty branch, a depth or count over the complexity gate. Calling that "syntax" would teach the player to look for a class of error the editor makes impossible. |
| 3 | `TEST / SIMULATION` box and the `TEST` button | **Not built.** A dry run needs a forkable engine, which needs world-state serialization, which is its own subsystem and does not exist. Per the visual-style rule, a box whose source does not exist is not built, not stubbed. The `SIMULATION LOG` box survives as a **program trace** fed by real telemetry from an activated program. |
| 4 | Two minimaps (`Code MINIMAP` and `MINIMAP`) | **One, and it is optional.** The duplicate is a generation artifact. A minimap earns its place past roughly forty blocks; the complexity gate caps a v1 program well below that. Deferred to open items; the canvas scrolls. |
| 5 | `VARIABLES` and `FUNCTIONS` tabs | **Rendered disabled.** Mutable per-program state and user-defined reusable groups are both real language features with real determinism and validation consequences. Without them, evaluating a program is a pure function of the snapshot plus cooldown state, which is what makes the runtime need no budget or watchdog. |
| 6 | `Estimated CPU Load` metric | **Row omitted.** Compute cost as a balancing resource is in the design document (§3.2, §7.2) and in no system. The metrics box itself is built, because rule, condition, action and depth counts are all real. |

## Relationship to prior specs

`2026-07-28-ui-shell-design.md`, `2026-07-30-production-planning-design.md`,
`2026-08-01-base-graph-design.md`, `2026-08-02-visual-style-system-design.md` and
`2026-08-03-top-focus-navigation-design.md` all stand. Binding here without restatement: the zone
model, the panel contract, the snapshot-and-poll binding, the reference direction between
assemblies, the integer-only rule in `Dimenship.Core`, the box vocabulary, the five-state control
requirement, the glow rule, and the rule that nothing outside `ShellPalette` names a colour and no
state is carried by colour alone.

Two open items from prior specs are touched:

- The production spec's **Standing orders** open item is *not* closed. `Produce` is the honest
  action today; "keep this recipe configured" is a standing order, and §*Actions* below is shaped
  so it lands as a sixth action rather than a redesign.
- The base-graph spec's **graph commands** decision — "None. Read-only." — is unchanged. Programs
  are not issued from the graph. The graph still mutates nothing.

One statement in the source material is superseded, and one is at risk:

- **`Dimenship Programming v0.1.md` §9, "Technical Notes for C# .NET 10."** The solution targets
  `net8.0` across all five projects. Nothing in this spec needs a later runtime.
- **§10, "Can programs be installed while TimeFlow is running? No for MVP; require pause/0× for
  edits."** Adopted for *activation*, not for editing — see Decisions.

## Current state

- `ShellRoot.cs:93` registers `doctrine` as a `PlaceholderPanel` reading *"Rule editor: nested
  conditions and an ordered action list. No loops."* That sentence is the seed of this spec and
  survives it intact.
- `SimulationEngine` exposes three mutating entry points: `Enqueue`, `EnqueueTransfer` and
  `Commit`. There is no configuration command, no priority, no hold, and no reservation. Executor
  task selection is "continue the current task, else prefer the configured schematic, else switch
  over", with no player- or program-supplied ordering.
- `PostponeReason.SafetyLock` exists, is mapped to `EventCode.PostponeSafetyLock`, and is rendered
  as `SAFETY_LOCK` by both `NodeCard` and `FacilityInspectorPanel` — but nothing ever sets it as a
  real reason. Its only two occurrences, `SimulationEngine.cs:773` and `SimulationEngine.cs:880`,
  assign it to an `out reason` parameter on a **success** path, where the caller ignores it. See
  *Kernel changes*.
- `WorldSnapshot` already carries everything v1 conditions need to read: `Resources`, `Storages`,
  `Energy`, `Executors`, `Transports`, `ProductionTasks`, `TransportTasks`.
- `ShellActions` carries focus, transport, zone and selection commands. `ShellContext` exposes
  `Actions` and `CurrentSelection`.
- The shell persists layout only. There is no world-state save.

## Decisions

| Question | Decision |
| :--- | :--- |
| Which view | The `doctrine` focus view. One editor authoring both vessel controllers and bot doctrines; the target family is a property of the program, not a second editor. |
| Bot doctrines in v1 | The **scope** mechanism ships; the bot scopes do not. No bot, drone or mission exists to name, so the bot vocabulary would be words that command nothing. |
| Processes view | Not built. Programs feed the queues that Processes will display; the seam is recorded below. |
| Editing model | Structural. Every slot is a dropdown or a bounded number field. No free text, no parser. |
| Layout model | A vertical indent ladder, laid out by the container. Dragging reorders and re-parents; it never sets a coordinate. Not a free canvas. |
| Loops | None. Confirmed from the placeholder's own sentence, and load-bearing: no loops plus no variables makes evaluation a pure function with a compile-time depth cap, so the runtime needs no budget, watchdog or step limit. |
| Where programs run | A new phase 0 of the tick, before power sinks. A program reads the vessel as it stood at the end of the previous tick and acts before anything moves. |
| Conflict resolution | Commands are buffered per tick, then resolved per target. Highest priority wins; ties by program order, then rule order. Every loser emits telemetry naming the winner. |
| Action set | Five: `Produce`, `Transfer`, `Hold`, `SetPriority`, `Reserve`. Each maps to a named engine command that really exists or is added here with tests. |
| Activation | Requires TimeFlow at 0×. Editing a draft is allowed at any speed, because a draft does not execute. |
| Persistence | None. Programs are world state, and world state is not saved. `ProgramDefinition` is a plain record so it lands with the persistence subsystem for free. |
| Palette placement | Inside the focus zone, as the editor's third column. Not a shell panel in the Inspector zone. |
| Dry run | Out. See concept review 3. |

## Doctrine and Processes

The programming view authors *intent*. It does not execute it and it does not schedule it.

```
Programming view  →  ProgramDefinition
                          ↓  (phase 0 of the tick)
                     ProgramRuntime  →  bounded commands
                          ↓
                     SimulationEngine  →  executor queues
                          ↓
                     Processes view (placeholder)  →  the queues, in priority order
```

A program's whole effect on the world is the commands it issues into the queues the executors
already own. That is why the action set is small and each action names an engine command: an
action that could not be expressed as something the player could have done by hand would be a
second, hidden simulation.

The `processes` focus view remains a placeholder. Its job — the queue in priority order, with a
drill-down per process — becomes considerably more useful once programs are writing into those
queues, and `SetPriority` is the field it would sort by. It is not built here and this spec does
not shape it beyond the panel contract that already exists.

## Kernel changes

### The safety lock

`PostponeReason.SafetyLock` becomes a real reason, and the two existing assignments become a
hazard that must be fixed in the same change.

```csharp
// SimulationEngine.cs:773 and :880, both on the success path of a Try… method:
reason = PostponeReason.SafetyLock;
return true;
```

Both write a reason the caller discards, using `SafetyLock` as an arbitrary non-null filler
precisely because nothing else produced it. Once `Hold` sets a genuine lock, a success path
writing a real block code is a bug waiting for the first caller that stops discarding it. The
`out reason` becomes `out PostponeReason? reason`, `null` on success, and the lock is checked
before either method is reached.

A held executor postpones every queued task with `SafetyLock` and reports
`ExecutorStatus.AllQueuedTasksBlocked`. It keeps drawing standing power — a hold stops work, not
the machine.

### Priority

```csharp
public sealed class ProductionTask
{
    // …
    public int Priority { get; set; }   // higher runs first; default 0
}
```

Task selection gains one step, inserted after "continue the current task" and before "prefer the
configured schematic": among runnable queued tasks, take the highest priority. The existing bias
toward the current configuration survives *within* a priority band, so raising a priority is a
deliberate instruction to accept a switch-over cost, and leaving priorities alone reproduces
today's behaviour exactly. That last property is what makes this testable: every existing task
selection test must stay green unmodified.

### Reservations

```csharp
public sealed record Reservation(StorageId Storage, ItemId Item, long Quantity, ProgramId Owner);
```

A reservation is a floor, not a lock. `Available(storage, item)` — which the planner, production
input consumption and transport sourcing all already call — subtracts the reservations held
against that storage and item. Material under a reservation is therefore invisible to everything
that would consume it, and no new call site has to remember to check.

Consequences, each a test:

- A production run whose inputs are reserved postpones with `InsufficientInputMaterial`, which is
  the true reason: the material is there, and it is not available to that run.
- A transfer sourcing from a reserved storage moves only the surplus.
- Reservations never exceed what is present; a reservation larger than the stock reserves the
  stock and no more, and the shortfall is telemetry rather than a negative number.
- Clearing a program clears its reservations. A reservation with no owner is unreachable.

`Room` is unaffected. A reservation withholds material from consumers; it does not claim space.

### Program identifiers

New `readonly record struct`s alongside the existing ones, in `Ids.cs`: `ProgramId`, `RuleId`.
`RuleId` is stable across an edit so telemetry from before an edit still names something.

## The program model

New folder, `src/Dimenship.Core/Programs/`. Engine-free of Godot, integer-only, no wall clock.

### Definition

```csharp
public sealed record ProgramDefinition
{
    public required ProgramId Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ProgramScope Scope { get; init; }
    public required ComplexityTier Tier { get; init; }
    public required IReadOnlyList<ProgramParameter> Parameters { get; init; }
    public required IReadOnlyList<Rule> Rules { get; init; }
}

public sealed record Rule(RuleId Id, Condition? When, IReadOnlyList<Statement> Then, int Priority, long CooldownTicks);
```

A rule with a null `When` fires every tick it is not on cooldown. That is the honest expression of
an unconditional standing instruction, and it costs nothing: the alternative is a condition that
is always true, which the validator would then have to recognise.

### Statements

The block vocabulary is three shapes and nothing else.

```csharp
public abstract record Statement;

/// <summary>An IF / ELSE IF … / ELSE ladder. Branches are evaluated in order; the first match wins.</summary>
public sealed record Branch(IReadOnlyList<BranchArm> Arms, IReadOnlyList<Statement> Otherwise) : Statement;
public sealed record BranchArm(Condition When, IReadOnlyList<Statement> Then);

/// <summary>A bounded command. The leaf of every path.</summary>
public sealed record Command(ActionKind Kind, IReadOnlyList<Operand> Operands) : Statement;
```

`Branch` carries the whole ladder rather than nesting an `Else` inside an `If`, because the
concept's `IF / ELSE IF / ELSE` is one construct in the player's head and one block group on the
canvas. A nested representation would make "add an else-if" a tree surgery and would let an editor
produce a shape — an `Else` with no `If` — that the language has no meaning for.

There is no loop statement and no assignment statement. Evaluating a `ProgramDefinition` therefore
terminates in a number of steps bounded by its own statement count, which the validator has
already checked against the complexity gate.

### Conditions

```csharp
public sealed record Condition(ConditionKind Kind, IReadOnlyList<Operand> Operands, Comparison Op, Operand Value);
public enum Comparison { LessThan, LessOrEqual, Equal, NotEqual, GreaterOrEqual, GreaterThan }
```

`ConditionKind` is closed, and every member names a field the snapshot already carries:

| Kind | Reads | Operands |
| :--- | :--- | :--- |
| `StorageItemAmount` | `StorageState.Items[item].Amount` | storage, item |
| `StorageFillPercent` | `TotalAmount * 100 / TotalCapacity` | storage |
| `VesselItemAmount` | `ResourceStock.Amount` | item |
| `VesselItemTrend` | `ResourceStock.NetRatePerTick` | item |
| `ExecutorStatusIs` | `ExecutorState.Status` | executor |
| `ExecutorBlockedBy` | `ExecutorState.BlockReason` | executor |
| `ExecutorQueueLength` | count of that executor's queued tasks | executor |
| `EnergyReservePercent` | `Energy.Reserve * 100 / Energy.Capacity` | — |
| `TicksSinceRuleFired` | runtime state | rule |

The concept's `Resource : Status → Decreasing` is `VesselItemTrend < 0`, and `Factory : Status` is
`ExecutorStatusIs`. `Factory : Job Progress` is deliberately absent: `RunTicksRemaining` against
`RunTicksTotal` describes the run in flight, not the queue, and a program that reacts to a run
being 40% done is reacting to something it cannot usefully change. It is an open item, not an
omission.

**Percentages are integer percent, floored, computed as `x * 100 / y`.** A zero denominator makes
the condition evaluate **false** and emits nothing. A storage with no capacity is not 0% full and
is not 100% full; it is a storage no statement about fullness is true of, and false is the only
answer that does not invent one.

### Operands

```csharp
public abstract record Operand;
public sealed record Literal(long Value) : Operand;
public sealed record ParameterRef(string Name) : Operand;
public sealed record TargetRef(TargetKind Kind, string Id) : Operand;   // executor, storage, item, schematic
public sealed record EnumRef(string Kind, int Value) : Operand;         // ExecutorStatus, PostponeReason, Comparison
```

`ParameterRef` is the entire preset-tuning layer from the design document's §2 — a preset program
is a `ProgramDefinition` whose rules reference parameters, and "adjust a few parameters" is
editing `ProgramParameter.Current` without touching a rule. Both interface layers are one model,
which is what the document's one-way simplicity rule asks for.

```csharp
public sealed record ProgramParameter(string Name, string Label, long Min, long Max, long Default, long Current);
```

Bounds are on the parameter, so a tuned preset cannot leave the range its author validated.

### Actions

`ActionKind` is closed. Every member maps to one engine command.

| Kind | Operands | Engine command | Status |
| :--- | :--- | :--- | :--- |
| `Produce` | schematic, runs, executor | `Enqueue` | exists |
| `Transfer` | item, quantity, from, to | `EnqueueTransfer` | exists |
| `SetPriority` | executor, priority | task priority on that executor's queue | added above |
| `Hold` | executor, on/off | safety lock | added above |
| `Reserve` | storage, item, quantity | reservation | added above |

Five actions is the whole language. The design document's Example D — *Refinery Shortage
Recovery* — is expressible: `Produce` covers "set recipe to Refined Alloy" as an honest quantity
of runs, `SetPriority` covers "set priority to High", and `Reserve` covers "reserve Refined Alloy
for Factory Beta". Its "restore balanced_refining" is `Hold` released plus `SetPriority` back to
its default.

What is *not* expressible is a standing recipe configuration — "keep this refinery on Refined
Alloy indefinitely" — because the engine has no standing order. That is the production spec's
existing open item, and when it closes, `KeepConfigured` is a sixth `ActionKind` with a sixth row
in this table and no change to the model, the validator, the editor, or the conflict rule.

`Produce` and `Transfer` are **idempotent against their own outstanding work**: a rule firing every
tick must not queue a thousand identical tasks. Each carries the issuing rule's `RuleId`, and the
engine treats a command from a rule whose previous task is still incomplete as a no-op that emits
`ProgramCommandRedundant`. Without this the first program a player writes floods a queue, and the
telemetry that explains why is the thing this system exists to produce.

### Validation

```csharp
public sealed record ValidationIssue(RuleId? Rule, IssueSeverity Severity, IssueCode Code, string Detail);

public static class ProgramValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(
        ProgramDefinition program, WorldDefinition world, ComplexityGate gate);
}
```

`IssueCode`: `UnknownTarget`, `TargetOutOfScope`, `ValueOutOfRange`, `EmptyBranch`,
`UnreachableBranch`, `TooManyRules`, `TooManyConditions`, `DepthExceeded`, `NoRules`,
`DuplicateParameterName`, `SelfDefeatingPair`.

Two of those are worth naming for what they prevent:

- **`UnreachableBranch`** — an `ELSE IF` whose condition is subsumed by an earlier arm. The concept
  image shows exactly this bug: its rule 1 tests `Refined Alloy < 300`, and its `ELSE IF` tests
  `Refined Alloy < 300` again, which can never be reached. A validator that does not catch the
  error the concept art itself contains is not carrying its weight.
- **`SelfDefeatingPair`** — two rules in one program whose commands are exact inverses on the same
  target with no separating condition, which is the oscillation a player writes once and then
  spends an hour finding.

`Severity` is `Error` or `Warning`. A program with any `Error` cannot be activated; warnings
activate and stay visible. `UnreachableBranch` and `SelfDefeatingPair` are warnings — they are
usually mistakes and are occasionally a player mid-thought, and refusing to run a program because
the author left a dead branch in it would be the editor overruling them.

### Runtime and the tick

```csharp
public sealed class ProgramRuntime
{
    public void Install(ProgramDefinition program);
    public void Remove(ProgramId id);
    public IReadOnlyList<ProgramCommand> Evaluate(WorldSnapshot snapshot, long tick);
}
```

`Evaluate` is pure: same snapshot, same tick, same installed programs, same cooldown state → same
commands, in the same order. It reads no engine internals, only the snapshot the shell also sees,
which is what makes a program's behaviour reproducible from a saved tick and explainable to the
player from a screen they were looking at.

The tick order becomes:

0. **Programs.** Evaluate in installation order, rules in definition order. Collect commands.
   Resolve conflicts. Apply.
1. Power sinks.
2. Transport executors.
3. Production executors.
4. Power reconciliation.

Phase 0 reads the snapshot published at the end of tick *n−1*. Programs therefore never see a
half-updated world and never race each other for a value one of them just changed — the only state
any of them reads is the state the player would have seen if they had paused. Two programs
disagreeing is resolved by the conflict rule, not by evaluation order, which is what stops
"whichever ran first" from becoming a hidden mechanic.

### Conflict resolution

Commands are buffered, then keyed by `(ActionKind, primary target)`. Within a key:

1. Highest command priority wins. A command's priority is its rule's.
2. Ties break by installation order, then rule order.

Every superseded command emits `ProgramCommandSuperseded` carrying the losing rule, the winning
rule, and the target. `Produce` and `Transfer` do **not** participate: two programs asking for
different production on different executors are not in conflict, and two asking for the same are
handled by the redundancy rule above. `Hold`, `SetPriority` and `Reserve` are the exclusive ones,
because each sets a single value on a single target.

The design document's conflict report (§6.2) is then a rendering of these events over an
operational window, not a separate system.

### Telemetry

`SimEvent` keeps its shape. `EventCategory` gains `Automation`. New `EventCode`s, one per
observable program decision, following the existing one-code-per-reason rule so the console can
filter them:

`ProgramInstalled`, `ProgramRemoved`, `RuleFired`, `RuleSkippedCooldown`, `RuleSkippedCondition`,
`ProgramCommandIssued`, `ProgramCommandSuperseded`, `ProgramCommandRedundant`,
`ProgramCommandRejected`, `ReservationPlaced`, `ReservationCleared`, `ExecutorHeld`,
`ExecutorReleased`.

`Data` carries the numbers: a `RuleSkippedCondition` records the value read and the value
compared, which is what turns the design document's "Rule 1 triggered (Refined Alloy = 250)" into
something the console renders rather than a string the runtime formats.

`RuleSkippedCondition` fires for every rule that evaluated false, every tick, for every installed
program. That is a lot of events against a 512-entry buffer. It is therefore **gated by the
program's telemetry level** — `Minimal` emits fired and rejected only, `Normal` adds superseded
and redundant, `Verbose` adds skips. `Normal` is the default and `Verbose` is what the design
document's *Verbose Doctrine Trace* reward unlocks per program.

## Shell assembly additions

`Dimenship.Shell`, engine-free and Godot-free — the editor's arithmetic, so that almost none of it
needs the editor to verify. The program model itself stays in `Core` for the same reason
`BaseGraphLayout` does: `Shell` cannot name `ExecutorId`, and that reference direction is
load-bearing.

```csharp
/// <summary>A position in the statement tree: child indices from the rule root.</summary>
public readonly record struct BlockPath(IReadOnlyList<int> Indices);

public enum BlockKind { Control, Condition, Action, Branch }

public static class BlockLayout
{
    public const int RowHeight = 28, IndentWidth = 20, RowGap = 4, ArmGap = 8;

    public static IReadOnlyList<BlockBox> Measure(IReadOnlyList<int> shape);
    public static (int W, int H) ContentSize(IReadOnlyList<BlockBox> boxes);
}

public readonly record struct BlockBox(BlockPath Path, BlockKind Kind, int X, int Y, int W, int H);

public static class DropTarget
{
    /// <summary>Null when the pointer is over no legal target for the dragged kind.</summary>
    public static (BlockPath Parent, int Index)? Resolve(
        IReadOnlyList<BlockBox> boxes, int pointerY, BlockKind dragged, int maxDepth);
}
```

`Resolve` is where the language's shape is enforced at the pointer: an action drops only into a
branch body or a rule body, a condition drops only into a condition slot, and nothing drops deeper
than `maxDepth`. Returning null rather than a nearest-legal guess is deliberate — a drop that
silently lands somewhere other than where the player aimed is worse than one that does not land.

## The focus view

`dimenship/scripts/ui/focus/programs/` — new folder.

| File | Role |
| :--- | :--- |
| `ProgramsFocus.cs` | `PanelBase` for `doctrine`. Owns the three columns, the selected program and the edit state. |
| `ProgramList.cs` | Left column: the library, and the selected program's info box. |
| `RuleCanvas.cs` | Centre: the indent ladder, drag and drop, the gutter. |
| `BlockView.cs` | One block: its keyword, its slots, its fill, its hit area. |
| `SlotControl.cs` | One slot: dropdown, number field, or enum picker. |
| `BlockPalette.cs` | Right column: tabs, search, domain filter, template list. |
| `ValidationBox.cs`, `TraceBox.cs`, `MetricsBox.cs` | The dock boxes. |

`PanelId` stays `"doctrine"`. It is written into saved layout files, and renaming it would reset
every player's layout to gain nothing — the same reasoning `ShellRoot` records for `"overview"`.

The descriptor **title** changes from `Doctrine` to `Programs`, matching the concept's tab. One
consequence, which is the whole reason it is written down: `ShellRoot` builds `FocusOrder` by
`OrderBy(d => d.Title)`, so the accelerators shift from `Base Graph, Doctrine, Processes, Robotics`
to `Base Graph, Processes, Programs, Robotics` — this view moves from `Ctrl+2` to `Ctrl+3` and
`Processes` takes `Ctrl+2`.

### Composition

Three columns inside the focus zone, in an `HSplitContainer` pair so the player can size them:

| Column | Width | Content |
| :--- | :--- | :--- |
| Library | 260 min | `PROGRAMS LIST` box, `+ NEW PROGRAM` button, `SELECTED PROGRAM INFO` box. |
| Editor | expand | Header row, tab strip, rule canvas, `+ ADD NEW RULE` footer. |
| Palette | 280 min | `CONDITIONS` / `ACTIONS` / `FUNCTIONS` tabs, search, domain filter, templates. |

The palette lives here rather than in the Inspector zone because the editor is unusable without
it: a palette the player can replace with the Energy Budget panel is a palette that will be
missing exactly when it is needed. The Inspector zone keeps serving `facility_inspector`, and the
player may collapse it for width.

Column split offsets are session-local. `LayoutState` describes zones, not the interior of a focus
view, and widening it to carry one view's private geometry is how a layout format stops being
about layout.

### Library

`PROGRAMS LIST` is a `Box` of selectable `Row`s. Each row: a 16×16 domain icon, the program name
at `FontBody` `TextTitle`, its scope beneath at `FontMicro` `TextDim`, and a right-aligned version
chip. The active row takes the `Row` hover fill permanently plus a 1px `Accent` border.

`SELECTED PROGRAM INFO` is a `Box` of `Row`s: description at `FontMicro` `TextPrimary`, then type,
scope, complexity, reliability and last edit. Complexity renders as a filled count out of the
tier's maximum — `4/10`, with the numerals present, because a row of stars is a value encoded in
shape alone. Reliability renders as its word in a state colour, never the colour alone.

### Editor header

A `Row` above the tab strip: `PROGRAM:` at `FontMicro` `TextDim`, the name at `FontHeading`
`TextTitle` with an inline rename affordance, then right-aligned, a version chip and three
buttons — `SAVE` (Default), `VALIDATE` (Default), `ACTIVATE` (Primary).

`ACTIVATE` is disabled with a `TextDim` reason label when TimeFlow is not 0×, or when validation
holds an `Error`. The reason is stated in words next to it; a disabled button with no explanation
is the design document's "readable" requirement failing at the first control the player meets.

Tab strip: `RULES` · `PARAMETERS` · `NOTES` — and `VARIABLES` · `PERMISSIONS` · `FUNCTIONS`
rendered disabled at `TextFaint`, per the tab rule that positions stay stable as subsystems ship.

### Block anatomy

Every block is one row of a fixed height, laid out left to right:

1. **Keyword cap** — `IF`, `ELSE IF`, `ELSE`, `THEN`, or the action's target name. `FontMicro`,
   uppercase, tracked, `TextTitle`, on the block's category fill at `RadiusMd`.
2. **Slots** — each a `SlotControl` at `RadiusSm`, separated by `SpaceSm`.
3. **Trailing unit label**, where the value has one — `units`, `%`, `ticks` — at `FontMicro`
   `TextDim`.

A `Branch` draws its arms as a connected group: each arm's keyword cap, then its body indented by
`IndentWidth` with a 2px left rule in the branch's fill colour running the body's full height.
That rule is the only thing carrying nesting depth visually, so it is drawn at full opacity and
never dimmed.

The gutter left of the canvas carries a sequential block number at `FontMicro` `TextFaint`,
matching the concept. It is a display index recomputed on every edit, not an identity — telemetry
names a `RuleId`, never a line number, because a line number changes when the player inserts a
block above it and old telemetry would then point at the wrong block.

**Block colour.** Four new `ShellPalette` tokens, one per `BlockKind`:

| Token | Value | Carries |
| :--- | :--- | :--- |
| `BlockControl` | `#1E4A66` | `IF`, `THEN` — the ladder's structure |
| `BlockBranch` | `#6B4A16` | `ELSE IF`, `ELSE` — the alternative path |
| `BlockCondition` | `#1E2A31` (`Border`) | a condition's slot strip |
| `BlockAction` | `#2A4A2E` | every command |

Text on all four is `TextTitle`; slot fills are `BgBase`. The category is also carried by the
keyword cap's word, so the colours are redundant rather than load-bearing, which is what the
no-colour-alone rule requires. `BlockControl`, `BlockBranch` and `BlockAction` are new values read
off the concept and are open items to tune, on the same footing as `Accent`.

### Interaction

| Input | Effect |
| :--- | :--- |
| Click a block | Select it. |
| Drag a block | Reorder or re-parent it. Legal targets highlight; an illegal drop returns it. |
| Drag a palette template | Insert a new block at the drop target. |
| Click a slot | Open its dropdown, or focus its number field. |
| `Delete` | Remove the selected block and its children, with an undo entry. |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo, bounded at 64 entries. |
| `Tab` / `Enter` | Blocks and slots are focusable `Control`s, so the shell's existing traversal reaches them. |
| Wheel | Scroll the canvas. No zoom. |

No zoom, unlike the graph: the canvas is a list of fixed-height rows in a scroll container, and a
scaled text editor buys nothing a scrollbar does not.

Undo is per program and is discarded when the selection changes. A player who edits three programs
and expects `Ctrl+Z` to walk backwards across all three is expecting a document model this editor
does not have; discarding at the boundary is the behaviour that is easy to predict.

### Dock

One console panel, `program_dock`, holding an `HBoxContainer` of `Box`es per the visual-style
bottom-dock pattern:

| Box | Style | Source |
| :--- | :--- | :--- |
| `VALIDATION` | Status list | `ProgramValidator` on the current definition. `NO ERRORS` in `StateOk` when clean; otherwise one row per issue, severity dot, code, and detail. Clicking a row selects the block. |
| `PROGRAM TRACE` | Alert feed | `SimEvent`s in the `Automation` category for the active program, newest first. Timestamp at `TextFaint`, then the message in the code's severity colour. |
| `PROGRAM METRICS` | Status list | Rules, conditions, actions, max depth — all counted from the definition — plus the complexity gate's maxima beside each, and last activation time. |

`TEST / SIMULATION` is not built. See concept review 3.

## Error handling

| Failure | Behaviour |
| :--- | :--- |
| A condition's denominator is zero | The condition is **false**. No division, no event. |
| A program names an executor, storage or item absent from the world | `UnknownTarget` error at validation; the program cannot be activated. An active program whose target disappears is suspended, emits `ProgramCommandRejected` once, and the row shows `TARGET MISSING`. |
| A branch has an empty body | `EmptyBranch` error. It commands nothing and is almost always an unfinished edit. |
| An `ELSE IF` is unreachable | `UnreachableBranch` warning. Activates. |
| Nesting exceeds the gate | `DepthExceeded` error. The editor also refuses the drop that would create it, so this is reachable only by a loaded program from a higher tier. |
| A rule fires while on cooldown | Skipped, `RuleSkippedCooldown` at `Verbose`. |
| A rule re-issues a command whose previous task is incomplete | No-op, `ProgramCommandRedundant`. |
| Two commands contend for one target | Priority, then order. Loser emits `ProgramCommandSuperseded`. |
| A reservation exceeds the stock present | Reserves the stock. The shortfall is telemetry, never a negative available quantity. |
| `ACTIVATE` pressed while TimeFlow is not 0× | Disabled, with the reason in words beside it. |
| A parameter is set outside its bounds | Impossible from the editor — the field clamps. A loaded program out of bounds is `ValueOutOfRange`. |
| Drag lands on an illegal target | The block returns to its origin. No nearest-legal guess. |
| The program library is empty | The canvas renders `NO PROGRAM SELECTED` at `FontBody` `TextDim`, centred, with no chrome. |

## Build order

Tests precede the code they cover.

1. `Dimenship.Shell` — `BlockPath`, `BlockLayout`, `DropTarget`, and their tests. Pure integer
   arithmetic, no engine, no Godot.
2. `Dimenship.Core` kernel changes — the safety lock and its `out reason` fix, task priority,
   reservations, and their tests. Every existing task-selection test must stay green unmodified.
3. `Dimenship.Core/Programs` — the model, `ProgramValidator`, `ProgramRuntime`, the tick phase,
   the new event codes, and their tests.
4. The starting library — two authored programs against the default world, as `Core` content with
   a test that each validates clean.
5. `ProgramsFocus` with the three columns, replacing the placeholder registration.
6. `RuleCanvas`, `BlockView`, `SlotControl`, `BlockPalette`.
7. `program_dock` and its three boxes.
8. `ShellPalette` block tokens.

## Verification

Automated, runnable here — `dotnet build DimenshipGame.sln` clean, `dotnet test` green.

`Dimenship.Shell.Tests`: `Measure` places a flat rule at increasing `Y` with constant `X`; a
nested branch indents its body by exactly `IndentWidth` per level; `ContentSize` bounds an
arbitrary tree; `Resolve` returns null for an action over a condition slot, null past `maxDepth`,
and the correct `(parent, index)` at the boundary between two sibling blocks; a `BlockPath` round
-trips through the tree it indexes.

`Dimenship.Core.Tests`: a held executor postpones every queued task with `SafetyLock` and still
draws standing power; releasing it resumes the same task rather than restarting it; no `Try…`
method writes a reason on a success path; a higher-priority task is selected ahead of one matching
the current configuration, and with equal priorities selection is byte-identical to today;
reserved material is invisible to `Available`, to production input consumption, and to transport
sourcing; a reservation larger than the stock reserves the stock; removing a program clears its
reservations; `Evaluate` is pure over identical inputs; a rule on cooldown does not fire; a zero
-denominator percentage condition is false; an unreachable `ELSE IF` is a warning and an empty
branch is an error; two commands on one target resolve by priority then order and the loser emits
`ProgramCommandSuperseded`; a rule firing every tick queues exactly one task; the concept's
*Refined Alloy Recovery* — rewritten against the default world — moves the vessel from a shortage
to a supplied state within a bounded tick count.

Manual, the user's step, in the editor:

- The programming view opens with the starting library listed and the first program selected.
- Dragging a condition from the palette into a rule inserts it; dragging an action into a
  condition slot refuses.
- Nesting draws its indent rule at every level and the gutter renumbers on insert.
- `VALIDATION` reports the seeded unreachable branch as a warning and clears when it is fixed.
- `ACTIVATE` is disabled while running and enabled at 0×.
- With the program active, the trace fills, the base graph's edges change, and the executor the
  program held reads `SAFETY_LOCK` on its card.
- Tab, hover, press, disabled and focus states are visible on blocks, slots and palette rows.
- Nothing glows.

The Godot editor is not on `PATH` here. No visual item is reported as verified before the user
confirms it.

## Out of scope

Bots, drones, robot groups, missions, mission docks and every doctrine that would command them.
Reactor fuel, research, analysis queues, compute cost and the case board. Found, corrupted, locked
and forbidden programs, and every progression gate that would deliver them. Program persistence,
sharing and versioned migration. The dry run and the `TEST` button. Variables, functions,
permissions and the Python-like expert editor. Standing recipe orders. A minimap. The Processes
view. Program-authored expeditions. Animation of any kind. The mobile profile.

## Open items

1. **Persistence.** A program does not survive a restart, because no world state does. This is the
   most player-visible cost in the document and it resolves with the persistence subsystem, not
   here. `ProgramDefinition` is a plain record precisely so that lands cheaply.
2. **Dry run.** Blocked on a forkable engine. `ProgramRuntime.Evaluate` is already pure over a
   snapshot, so the missing piece is a second engine instance, not a second runtime.
3. **Standing orders.** Carried forward from the production spec. `KeepConfigured` is the sixth
   action when it closes.
4. **Job progress conditions.** `RunTicksRemaining` against `RunTicksTotal` is available and
   deliberately unexposed. Revisit if a real use appears that is not better served by a queue
   -length or status condition.
5. **New colour values.** `BlockControl`, `BlockBranch` and `BlockAction` are read off the concept
   with nothing to anchor them. Expect to tune against the real backdrop, as with `Accent`.
6. **Telemetry volume.** `Verbose` on several programs at 30× will evict the 512-entry buffer
   within seconds. The gap marker keeps it honest, but a per-category buffer or a larger bound is
   the real answer if verbose tracing becomes routine.
7. **Conflict across programs versus within one.** The rule is the same for both, which is simple
   but means a program can supersede its own earlier rule without that reading as a conflict.
   Watch whether players find that surprising.
8. **Complexity gates.** Rule, condition and depth maxima per tier are named as a mechanism and
   not given numbers here; they are balance, and belong with the progression subsystem that
   unlocks tiers.
