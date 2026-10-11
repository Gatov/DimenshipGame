# Playable Inspector and the Move Control — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a facility from the Facility Inspector after a preview and a confirming press, show a
built facility's queue with rows that open their plan, and add a MOVE row to Operations' plan
controls.

**Architecture:** Three pure projections in `Dimenship.Core/Presentation`, each unit-tested
without Godot:

- `BuildQuote` reads a construction draft.
- `FacilityQueue` reads a facility's tasks.
- `MoveOptions` reads what a plan's stages can move to.

The Godot inspector and Operations render them, and change the world only through
`ShellContext.ComposeDraft`, `ShellActions.PlanApproved` and `ShellActions.Execute`. Nothing new
reaches the snapshot, the save or the layout file.

**Tech Stack:** Godot 4.7.1 + C# (.NET 8), NUnit 4.5.1, GDScript only for the throwaway headless
smoke script.

**Spec:** `docs/superpowers/specs/2026-10-11-playable-inspector-and-move-control-design.md`
(issue #77)

## Global Constraints

- **Kernel rules.** `Dimenship.Core` references neither Godot nor `Dimenship.Shell`. It has no
  `float` or `double` (`CoreAssemblyTests` enforces both).
- **Colours.** Every colour comes from `ShellPalette`; nothing in the shell hard-codes one.
- **Commands.** Every command goes through `ShellActions`. The shell never decides what the kernel
  refuses, and shows the refusal as worded.
- **Controls.** Controls are built once and refreshed per snapshot, never rebuilt per tick (U1's
  rule).
- **Construction goal.** A construction goal is exactly one whole unit: `1000` milli-units.
- **Doc comments.** XML doc comments explain *why* and name the failure they prevent. Match the
  surrounding file's register.
- **Test names.** They read as sentences with underscores between clauses.
- **Git.** Work on `master` (solo prototype). Commit subjects take a conventional prefix plus a
  lowercase behavioural sentence. Bodies are long prose. Each commit ends with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01SEW1WTtp1GwDbNZgTk7oDc
  ```
- **Godot build.** `dotnet build dimenship/Dimenship.csproj` must succeed.
- **Headless runs.** The game runs headless with
  `/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path . --script <file.gd>`
  from `dimenship/`.

## Review Focus

These are failure modes the spec implies that are easy to miss. Each one's test is in the task
named.

1. **The unit cannot reach its slot.** A construction draft can have no line from the producing
   facility to the slot. The planner reports `NoExecutorOrLine`, a non-structural issue, so the
   draft is still "committable". The inspector must say so (Task 1's `Warnings`, tested), not show
   a clean BUILD.
2. **A plan's stage is split across two facilities.** Each holder must be offered as a target for
   the other's runs. Excluding every holder would offer nothing (Task 3, tested).
3. **A standing order or a hand-queued task in the queue.** It has no plan and no run total. Its
   row must not navigate and must not invent a denominator (Task 2, tested).
4. **The selection changes while BUILD is armed.** The next slot's first press must arm, not commit
   (Task 4, exercised by the smoke).
5. **The game is paused.** BUILD and MOVE must still commit while paused, and the next frame must
   show the result, because a command rebuilds the snapshot. Exercised by the smoke, which never
   unpauses.

---

### Task 1: `BuildQuote` — a construction draft, read for the inspector

**Files:**
- Create: `src/Dimenship.Core/Presentation/BuildQuote.cs`
- Test: `tests/Dimenship.Core.Tests/Presentation/BuildQuoteTests.cs`

**Interfaces:**
- Consumes:
  - `PlanDraft` (`Goal`, `AssemblyTarget`, `Steps`, `Issues`, `EstimatedTicks`, `IsCommittable`);
  - `DraftStep.Work`, which is `DraftProduce(Schematic, Runs)` or
    `DraftMove(Item, Quantity, From, To)`;
  - `DraftIssue(Kind, Item, Quantity, Step)`;
  - `SchematicCatalog.TryGet`.
- Produces:
  ```csharp
  public sealed record BuildQuote(
      ExecutorId Slot, ItemId Unit, ExecutorId? MadeAt,
      IReadOnlyList<ItemAmount> FromHold, IReadOnlyList<ItemAmount> Short,
      IReadOnlyList<DraftIssue> Warnings, IReadOnlyList<DraftIssue> Blocking, long EstimatedTicks)
  {
      public bool Committable { get; }
      public static BuildQuote For(PlanDraft draft, SchematicCatalog schematics, StorageId hold);
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
using Dimenship.Core.Content;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// <see cref="BuildQuote"/>: what the inspector says a construction will take before the player
/// commits it (U4). It reads the composer's own draft, so the inspector and Build mode cannot
/// disagree about a cost or a shortage.
/// </summary>
public class BuildQuoteTests
{
    private static readonly ItemId Unit = new("unit");
    private static readonly ItemId Metal = new("metal");
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId FactoryStorage = new("factory_storage");
    private static readonly StorageId SlotStorage = new("slot_storage");
    private static readonly ExecutorId Factory = new("factory");
    private static readonly ExecutorId Slot = new("slot");
    private static readonly SchematicId Assemble = new("assemble_unit");

    /// <summary>
    /// A factory that makes a slot's unit from metal, which nothing aboard produces. The hold
    /// holds <paramref name="metal"/>; <paramref name="routed"/> false leaves no line from the
    /// factory to the slot.
    /// </summary>
    private static SimulationEngine World(long metal, bool routed = true)
    {
        var builder = new WorldBuilder()
            .Item(Unit)
            .Item(Metal)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Metal, metal))
            .Storage(FactoryStorage)
            .Storage(SlotStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory, effort: 1600,
                inputs: new ItemAmount(Metal, 400))
            .Producer(Factory, FacilityType.Factory, null, storage: FactoryStorage)
            .Producer(Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Transport(new ExecutorId("feed"), Hold, FactoryStorage, throughputPerTick: 50);
        if (routed)
        {
            builder.Transport(new ExecutorId("deliver"), FactoryStorage, SlotStorage, throughputPerTick: 50);
        }

        return builder.Engine();
    }

    private static BuildQuote Quote(SimulationEngine engine, StorageId slotStorage, ExecutorId slot, ItemId unit) =>
        BuildQuote.For(
            PlanDraftEditor.Create(new ItemAmount(unit, 1000), engine, slotStorage, slot),
            engine.Catalog.Schematics,
            engine.State.Vessel.Hold);

    [Test]
    public void FactoryBeta_IsQuotedAtFactoryAlpha_WithItsMetalsFromTheHold_AndAnEstimate()
    {
        var engine = Shipped.Engine();

        var quote = Quote(engine, DefaultVessel.FactoryBBuffer, DefaultVessel.FactoryB,
            DefaultVessel.FactoryConstructionUnit);

        Assert.That(quote.Slot, Is.EqualTo(DefaultVessel.FactoryB));
        Assert.That(quote.Unit, Is.EqualTo(DefaultVessel.FactoryConstructionUnit));
        Assert.That(quote.MadeAt, Is.EqualTo(DefaultVessel.FactoryA));
        Assert.That(quote.FromHold, Is.EqualTo(new[] { new ItemAmount(DefaultVessel.BasicMetals, 400) }));
        Assert.That(quote.Short, Is.Empty);
        Assert.That(quote.Warnings, Is.Empty);
        Assert.That(quote.Blocking, Is.Empty);
        Assert.That(quote.Committable, Is.True);
        Assert.That(quote.EstimatedTicks, Is.GreaterThan(0));
    }

    [Test]
    public void AShortHold_IsQuotedAsAWait_NotABlock()
    {
        var quote = Quote(World(metal: 100), SlotStorage, Slot, Unit);

        Assert.That(quote.Short.Select(s => s.Item), Is.EqualTo(new[] { Metal }));
        Assert.That(quote.Short.Single().Quantity, Is.GreaterThan(0));
        Assert.That(quote.Committable, Is.True, "a shortage waits; it never blocks approval");
        Assert.That(quote.Warnings, Is.Empty, "a shortage is listed once, as a shortage");
    }

    [Test]
    public void AUnitWithNoWayToItsSlot_IsAWarning_SoTheInspectorCanSaySo()
    {
        // NoExecutorOrLine is not structural, so the draft would commit. A clean BUILD over a plan
        // that can never deliver is the failure this pins.
        var quote = Quote(World(metal: 1000, routed: false), SlotStorage, Slot, Unit);

        Assert.That(quote.Warnings.Select(w => w.Kind), Has.Member(DraftIssueKind.NoExecutorOrLine));
    }

    [Test]
    public void ADraftThatBuildsNothing_IsNotAQuote()
    {
        var engine = Shipped.Engine();
        var draft = PlanDraftEditor.Create(new ItemAmount(DefaultVessel.Component, 1000), engine);

        Assert.Throws<ArgumentException>(() =>
            BuildQuote.For(draft, engine.Catalog.Schematics, engine.State.Vessel.Hold));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dimenship.Core.Tests --filter "FullyQualifiedName~BuildQuoteTests"`

Expected: a build error, `The type or namespace name 'BuildQuote' could not be found`.

- [ ] **Step 3: Write the implementation**

```csharp
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>
/// What a construction will take, read off the draft the composer would commit, projected and
/// thrown away (U4; <c>2026-10-11-playable-inspector-and-move-control-design.md</c>). The inspector
/// builds from it without a trip to Operations, and reading the composer's own draft is what keeps
/// the two from quoting one build two ways.
/// <para>
/// Cost is what leaves the hold, not the unit schematic's inputs. A unit whose inputs must first be
/// made costs their raw materials, and those are what the player is short of. A shortage is listed
/// apart from what blocks: <see cref="DraftIssueKind.MaterialShortage"/> never stops approval, and
/// the work waits on its input, so the inspector says "will wait for", not "cannot".
/// </para>
/// <para>
/// <see cref="Warnings"/> are every other issue that does not block approval but means the build
/// may not finish, such as no line from the factory to the slot. Hiding them would put a clean BUILD
/// over a plan that can never deliver.
/// </para>
/// </summary>
public sealed record BuildQuote(
    ExecutorId Slot,
    ItemId Unit,
    ExecutorId? MadeAt,
    IReadOnlyList<ItemAmount> FromHold,
    IReadOnlyList<ItemAmount> Short,
    IReadOnlyList<DraftIssue> Warnings,
    IReadOnlyList<DraftIssue> Blocking,
    long EstimatedTicks)
{
    /// <summary>Whether approval would accept the draft: nothing structural is wrong with it.</summary>
    public bool Committable => Blocking.Count == 0;

    public static BuildQuote For(PlanDraft draft, SchematicCatalog schematics, StorageId hold)
    {
        if (draft.AssemblyTarget is not { } slot)
        {
            throw new ArgumentException(
                "A build quote reads a construction draft, and this draft assembles nothing.", nameof(draft));
        }

        var unit = draft.Goal.Item;
        ExecutorId? madeAt = null;
        var fromHold = new List<ItemAmount>();
        foreach (var step in draft.Steps)
        {
            switch (step.Work)
            {
                case DraftProduce produce when madeAt is null
                    && schematics.TryGet(produce.Schematic, out var schematic) && schematic.Output.Item == unit:
                    madeAt = step.Executor;
                    break;
                case DraftMove move when move.From == hold:
                    Merge(fromHold, move.Item, move.Quantity);
                    break;
            }
        }

        var shortages = new List<ItemAmount>();
        var warnings = new List<DraftIssue>();
        var blocking = new List<DraftIssue>();
        foreach (var issue in draft.Issues)
        {
            // One issue at a time through IsCommittable, so which kinds block stays decided in one
            // place, the draft, rather than copied here where it would drift.
            if (!(draft with { Issues = new[] { issue } }).IsCommittable)
            {
                blocking.Add(issue);
            }
            else if (issue.Kind == DraftIssueKind.MaterialShortage)
            {
                Merge(shortages, issue.Item, issue.Quantity);
            }
            else
            {
                warnings.Add(issue);
            }
        }

        return new BuildQuote(slot, unit, madeAt, fromHold, shortages, warnings, blocking, draft.EstimatedTicks);
    }

    private static void Merge(List<ItemAmount> amounts, ItemId item, long quantity)
    {
        var index = amounts.FindIndex(a => a.Item == item);
        if (index < 0)
        {
            amounts.Add(new ItemAmount(item, quantity));
        }
        else
        {
            amounts[index] = amounts[index] with { Quantity = amounts[index].Quantity + quantity };
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Dimenship.Core.Tests --filter "FullyQualifiedName~BuildQuoteTests"`

Expected: 4 passed.

- **If `FromHold` lists more than the 400 basic metals,** read the draft's steps and correct the
  expectation to what actually leaves the hold. Do not change the projection to fit.
- **If `AUnitWithNoWayToItsSlot` reports a different kind,** assert on the kind the planner
  actually emits, and record it in the spec's Decision 2.

- [ ] **Step 5: Commit**

```bash
git add src/Dimenship.Core/Presentation/BuildQuote.cs tests/Dimenship.Core.Tests/Presentation/BuildQuoteTests.cs
git commit -F- <<'EOF'
feat: a construction draft reads as a quote of where it is made, what it costs and what it waits for

<prose body: why the projection exists, cost as what leaves the hold, shortage vs warning vs blocking>

Refs #77

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SEW1WTtp1GwDbNZgTk7oDc
EOF
```

---

### Task 2: `FacilityQueue` — a facility's work, row by row

**Files:**
- Create: `src/Dimenship.Core/Presentation/FacilityQueue.cs`
- Test: `tests/Dimenship.Core.Tests/Presentation/FacilityQueueTests.cs`

**Interfaces:**
- Consumes:
  - `WorldSnapshot.Executors`, where `ExecutorState.CurrentTask` is the running task or the
    switch-over's target;
  - `WorldSnapshot.Tasks` (`TaskInstanceState`, in registry order, which is queue order);
  - `WorldSnapshot.Plans` (`CommittedPlanState.SpawnedTasks`, `Goal`);
  - `WaitCause.For(snapshot, taskId)`.
- Produces:
  ```csharp
  public sealed record FacilityQueueRow(
      TaskId Task, PlanId? Plan, SchematicId Schematic, int CompletedRuns, int? Runs, bool Current,
      ItemAmount? PlanGoal, Priority Priority, bool Held, PostponeReason? Reason, WaitCause? Waiting);
  public static class FacilityQueue
  {
      public static IReadOnlyList<FacilityQueueRow> For(WorldSnapshot snapshot, ExecutorId facility);
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// <see cref="FacilityQueue"/>: the inspector's QUEUE section (U4). The current task first, then
/// the queue in the order selection reads it, each row naming the plan it is for.
/// </summary>
public class FacilityQueueTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Refinery = new("refinery");

    /// <summary>
    /// One refinery whose buffer already holds ore, so every task can start at once. That is what
    /// lets one plan's priority outrank another's on the first tick.
    /// </summary>
    private static SimulationEngine Vessel() =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 200))
            .Storage(Buffer, StorageArchetype.FullHold, new ItemAmount(Ore, 200))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: 300,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Refinery, FacilityType.MatterReactor, null, storage: Buffer)
            .Transport(new ExecutorId("feed"), Hold, Buffer, throughputPerTick: 10)
            .Transport(new ExecutorId("return"), Buffer, Hold, throughputPerTick: 10)
            .Engine();

    private static CommittedPlan Commit(SimulationEngine engine, long alloy)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, alloy), engine));
        return engine.State.Plans.Plans[^1];
    }

    private static TaskId RunsOf(SimulationEngine engine, CommittedPlan plan) =>
        plan.SpawnedTasks.First(id => engine.State.Tasks.Task(id)!.IsProduce);

    [Test]
    public void TheCurrentTaskComesFirst_ThenQueueOrder_AndFinishedWorkIsLeftOut()
    {
        var engine = Vessel();
        var first = Commit(engine, 3);
        var second = Commit(engine, 3);
        engine.SetPriority(second.Id, Priority.High);
        engine.Advance(1);

        var rows = FacilityQueue.For(engine.Snapshot, Refinery);

        Assert.That(rows.Select(r => r.Task), Is.EqualTo(new[] { RunsOf(engine, second), RunsOf(engine, first) }));
        Assert.That(rows.Select(r => r.Current), Is.EqualTo(new[] { true, false }));

        engine.Advance(200);

        Assert.That(FacilityQueue.For(engine.Snapshot, Refinery), Is.Empty);
    }

    [Test]
    public void ARow_NamesItsPlansGoal_PriorityAndHold_AndWhoItWaitsBehind()
    {
        var engine = Vessel();
        var waiting = Commit(engine, 3);
        var urgent = Commit(engine, 3);
        engine.SetPriority(urgent.Id, Priority.High);
        engine.Hold(waiting.Id);
        engine.Release(waiting.Id);
        engine.Advance(2);

        var row = FacilityQueue.For(engine.Snapshot, Refinery).Single(r => r.Task == RunsOf(engine, waiting));

        Assert.That(row.Plan, Is.EqualTo(waiting.Id));
        Assert.That(row.PlanGoal, Is.EqualTo(new ItemAmount(Alloy, 3)));
        Assert.That(row.Priority, Is.EqualTo(Priority.Normal));
        Assert.That(row.Held, Is.False);
        Assert.That(row.Reason, Is.EqualTo(PostponeReason.Outranked));
        Assert.That(row.Waiting?.Plan, Is.EqualTo(urgent.Id));

        engine.Hold(waiting.Id);
        Assert.That(FacilityQueue.For(engine.Snapshot, Refinery).Single(r => r.Plan == waiting.Id).Held, Is.True);
    }

    [Test]
    public void AStandingOrderQueuedByHand_HasNoPlan_AndNoRunTotal()
    {
        var engine = Vessel();
        var task = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, null)), Refinery);

        var row = FacilityQueue.For(engine.Snapshot, Refinery).Single();

        Assert.That(row.Task, Is.EqualTo(task));
        Assert.That(row.Plan, Is.Null);
        Assert.That(row.PlanGoal, Is.Null);
        Assert.That(row.Runs, Is.Null);
    }

    [Test]
    public void AFacilityTheSnapshotDoesNotHave_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => FacilityQueue.For(Vessel().Snapshot, new ExecutorId("nowhere")));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dimenship.Core.Tests --filter "FullyQualifiedName~FacilityQueueTests"`

Expected: a build error, `The type or namespace name 'FacilityQueue' could not be found`.

- [ ] **Step 3: Write the implementation**

```csharp
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Presentation;

/// <summary>One unfinished production task on one facility, as the inspector's QUEUE lists it (U4).</summary>
/// <param name="Plan">The plan the task is for; null for a task queued by hand, which no row can open.</param>
/// <param name="Runs">Null for a standing order, which has no total to show progress against.</param>
/// <param name="Current">The run in progress, or the task a switch-over is loading.</param>
/// <param name="Priority">The effective priority, the plan's when there is one, as selection reads it.</param>
/// <param name="Reason">Why its last attempt was postponed, if it was.</param>
/// <param name="Waiting">Who it waits behind (K8), when its reason has another party.</param>
public sealed record FacilityQueueRow(
    TaskId Task,
    PlanId? Plan,
    SchematicId Schematic,
    int CompletedRuns,
    int? Runs,
    bool Current,
    ItemAmount? PlanGoal,
    Priority Priority,
    bool Held,
    PostponeReason? Reason,
    WaitCause? Waiting);

/// <summary>
/// A facility's work, current task first, then the rest in queue order, projected from a snapshot
/// and thrown away (U4; <c>2026-10-11-playable-inspector-and-move-control-design.md</c>). Before
/// it the inspector said a facility was busy and not with what, or for which order. The order is
/// the one selection reads, so the list does not promise a sequence the engine will not follow.
/// Finished tasks are left out: the queue is what is still to come.
/// </summary>
public static class FacilityQueue
{
    public static IReadOnlyList<FacilityQueueRow> For(WorldSnapshot snapshot, ExecutorId facility)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id == facility)
            ?? throw new ArgumentException($"No facility '{facility}' in this snapshot.", nameof(facility));

        var owners = new Dictionary<TaskId, CommittedPlanState>();
        foreach (var plan in snapshot.Plans)
        {
            foreach (var id in plan.SpawnedTasks)
            {
                owners.TryAdd(id, plan);
            }
        }

        var rows = new List<FacilityQueueRow>();
        foreach (var task in snapshot.Tasks)
        {
            if (task.Executor != facility || task.State == TaskState.Complete || task.Action is not Produce produce)
            {
                continue;
            }

            var owner = owners.GetValueOrDefault(task.Id);
            var row = new FacilityQueueRow(
                task.Id,
                owner?.Id,
                produce.Schematic,
                task.CompletedRuns,
                produce.Runs,
                task.Id == executor.CurrentTask,
                owner?.Goal,
                task.Priority,
                task.Held,
                task.LastReason,
                WaitCause.For(snapshot, task.Id));

            if (row.Current)
            {
                rows.Insert(0, row);
            }
            else
            {
                rows.Add(row);
            }
        }

        return rows;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Dimenship.Core.Tests --filter "FullyQualifiedName~FacilityQueueTests"`

Expected: 4 passed. If `ARow_NamesItsPlansGoal...` finds no `Outranked` after `Advance(2)`, raise
it to `Advance(3)`. The waiting task must have been attempted while the High one ran.

- [ ] **Step 5: Commit** as in Task 1, with subject
  `feat: a facility's queue reads as rows naming the plan each task is for and what it waits behind`
  and `Refs #77`.

---

### Task 3: `MoveOptions` — where a plan's stages can go

**Files:**
- Create: `src/Dimenship.Core/Presentation/MoveOptions.cs`
- Test: `tests/Dimenship.Core.Tests/Presentation/MoveOptionsTests.cs`

**Interfaces:**
- Consumes:
  - `WorldSnapshot.Plans`, `Tasks` and `Executors` (`Built`, `Type`);
  - `SchematicCatalog.TryGet`, whose `RequiredFacilityType` it reads;
  - K6d's `MoveWork` semantics. It moves runs from every facility other than the target.
- Produces:
  ```csharp
  public sealed record MoveOption(SchematicId Schematic, IReadOnlyList<ExecutorId> To);
  public static class MoveOptions
  {
      public static IReadOnlyList<MoveOption> For(WorldSnapshot snapshot, SchematicCatalog schematics, PlanId plan);
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// <see cref="MoveOptions"/>: what Operations' MOVE row offers (U4). It offers where a stage could
/// go and nothing more. Whether its material has already been sent is the kernel's to refuse.
/// </summary>
public class MoveOptionsTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Alpha = new("alpha");
    private static readonly ExecutorId Beta = new("beta");
    private static readonly ExecutorId Press = new("press");

    /// <summary>Two refineries on their own buffers, and a press of another type that never qualifies.</summary>
    private static SimulationEngine World(bool betaBuilt = true) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 400))
            .Storage(BufferA)
            .Storage(BufferB)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: 300,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Alpha, FacilityType.MatterReactor, null, storage: BufferA)
            .Producer(Beta, FacilityType.MatterReactor, null, storage: BufferB, builtAtStart: betaBuilt)
            .Producer(Press, FacilityType.Factory, null)
            .Transport(new ExecutorId("feed_a"), Hold, BufferA, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10)
            .Transport(new ExecutorId("feed_b"), Hold, BufferB, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("return_b"), BufferB, Hold, throughputPerTick: 10)
            .Engine();

    private static PlanId Commit(SimulationEngine engine, long alloy)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, alloy), engine));
        return engine.State.Plans.Plans[^1].Id;
    }

    private static IReadOnlyList<MoveOption> Options(SimulationEngine engine, PlanId plan) =>
        MoveOptions.For(engine.Snapshot, engine.Catalog.Schematics, plan);

    [Test]
    public void AStage_CanMoveToEveryBuiltFacilityOfItsType_ButTheOneHoldingIt()
    {
        var engine = World();
        var plan = Commit(engine, 10);

        Assert.That(Options(engine, plan), Has.Count.EqualTo(1));
        Assert.That(Options(engine, plan)[0].Schematic, Is.EqualTo(Smelt));
        Assert.That(Options(engine, plan)[0].To, Is.EqualTo(new[] { Beta }));
    }

    [Test]
    public void AStageSplitAcrossTwoFacilities_OffersEachToTheOther()
    {
        var engine = World();
        var plan = Commit(engine, 10);
        engine.Move(plan, Smelt, Beta);
        Assume.That(engine.Snapshot.Tasks.Count(t => t.Action is Dimenship.Core.Production.Produce
            && t.State != TaskState.Complete), Is.EqualTo(2), "fixture: the move left runs on both");

        Assert.That(Options(engine, plan).Single().To, Is.EqualTo(new[] { Alpha, Beta }));
    }

    [Test]
    public void AnUnbuiltFacility_IsNotOffered()
    {
        var engine = World(betaBuilt: false);
        var plan = Commit(engine, 10);

        Assert.That(Options(engine, plan).Single().To, Is.Empty);
    }

    [Test]
    public void AFinishedPlan_AndAnUnknownOne_OfferNothing()
    {
        var engine = World();
        var plan = Commit(engine, 2);
        engine.Advance(300);
        Assume.That(engine.Snapshot.Plans.Single(p => p.Id == plan).State, Is.EqualTo(PlanState.Complete));

        Assert.That(Options(engine, plan), Is.Empty);
        Assert.That(Options(engine, new PlanId(99)), Is.Empty);
    }
}
```

`AStageSplitAcrossTwoFacilities` calls `engine.Move` right after commit. Nothing has loaded yet,
so every run moves to Beta and Alpha keeps none. Make sure some ore is aboard before the move:
insert `engine.Advance(3);` before `engine.Move(...)`. That leaves Alpha with the loaded runs and
Beta with the rest.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dimenship.Core.Tests --filter "FullyQualifiedName~MoveOptionsTests"`

Expected: a build error, `The type or namespace name 'MoveOptions' could not be found`.

- [ ] **Step 3: Write the implementation**

```csharp
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Presentation;

/// <summary>One stage a plan could move, and every facility it could move to.</summary>
public sealed record MoveOption(SchematicId Schematic, IReadOnlyList<ExecutorId> To);

/// <summary>
/// What Operations' MOVE row offers for a plan (U4), projected from a snapshot and thrown away.
/// <para>
/// A stage is offered while any of the plan's tasks has unstarted runs of it. A facility is offered
/// when it is built and of the schematic's type, and some other facility holds such runs, because
/// <c>SimulationEngine.Move</c> takes runs from every facility but the target. Excluding every
/// holder instead would offer nothing for a stage already split across two.
/// </para>
/// <para>
/// It does not check whether the material has been sent, or whether the target is commandable.
/// Both are the kernel's to refuse, and the shell shows the refusal as given (U1's rule). A second
/// copy of those checks here would drift from the first. The only passive facility type, the
/// extractor, runs no schematic a plan can contain.
/// </para>
/// </summary>
public static class MoveOptions
{
    public static IReadOnlyList<MoveOption> For(WorldSnapshot snapshot, SchematicCatalog schematics, PlanId plan)
    {
        var committed = snapshot.Plans.FirstOrDefault(p => p.Id == plan);
        if (committed is null || committed.State != PlanState.Active)
        {
            return Array.Empty<MoveOption>();
        }

        var spawned = committed.SpawnedTasks.ToHashSet();
        var holders = new List<(SchematicId Schematic, List<ExecutorId> At)>();
        foreach (var task in snapshot.Tasks)
        {
            if (!spawned.Contains(task.Id) || task.State == TaskState.Complete
                || task.Action is not Produce { Runs: { } runs } produce)
            {
                continue;
            }

            var unstarted = runs - task.CompletedRuns - (task.State == TaskState.Running ? 1 : 0);
            if (unstarted <= 0)
            {
                continue;
            }

            var index = holders.FindIndex(h => h.Schematic == produce.Schematic);
            if (index < 0)
            {
                holders.Add((produce.Schematic, new List<ExecutorId> { task.Executor }));
            }
            else if (!holders[index].At.Contains(task.Executor))
            {
                holders[index].At.Add(task.Executor);
            }
        }

        var options = new List<MoveOption>();
        foreach (var (schematic, at) in holders)
        {
            if (!schematics.TryGet(schematic, out var definition))
            {
                continue;
            }

            var to = snapshot.Executors
                .Where(e => e.Built && e.Type == definition.RequiredFacilityType && at.Any(h => h != e.Id))
                .Select(e => e.Id)
                .ToList();
            options.Add(new MoveOption(schematic, to));
        }

        return options;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Dimenship.Core.Tests --filter "FullyQualifiedName~MoveOptionsTests"`

Expected: 4 passed.

- [ ] **Step 5: Commit** with subject
  `feat: a plan's movable stages read as options naming every facility each could move to` and
  `Refs #77`.

---

### Task 4: The inspector builds an unbuilt slot, after a preview and a confirming press

**Files:**
- Modify: `dimenship/scripts/ui/Labels.cs`, adding `Issue(DraftIssueKind)`.
- Modify: `dimenship/scripts/ui/focus/operations/OperationsFocus.cs`:
  - delete `DescribeIssue` (around line 876);
  - replace its two call sites with `Labels.Issue`.
- Modify: `dimenship/scripts/ui/focus/operations/OperationsFocus.PlanEditor.cs`, at around
  line 702, where `DescribeIssue` becomes `Labels.Issue`.
- Modify: `dimenship/scripts/ui/focus/operations/OperationsFocus.PlanControls.cs`, at around
  line 183, the same replacement.
- Modify: `dimenship/scripts/ui/panels/FacilityInspectorPanel.cs`:
  - the fields and `_Ready` (lines 26 to 91);
  - `OnSnapshot` (93 onward);
  - the unbuilt branch of `Executor` (lines 196 to 219).

**Interfaces:**
- Consumes:
  - `BuildQuote.For(draft, ShellContent.Catalog.Schematics, ShellContent.DefaultVessel.Hold)`
    from Task 1;
  - `ShellContext.ComposeDraft`, typed `Func<ItemAmount, StorageId?, ExecutorId?, PlanDraft>?`;
  - `ShellActions.PlanApproved`, typed `Func<PlanDraft, PlanApproval>?`;
  - `ConstructionProgress.For`.
- Produces: `Labels.Issue(DraftIssueKind kind) : string`, used by Task 6.

- [ ] **Step 1: Move the issue wording to `Labels`**

In `Labels.cs`, add `using Dimenship.Core.Planning.Draft;` and:

```csharp
    /// <summary>
    /// A draft issue in the shell's words. One copy, shared by the composer, the plan controls and
    /// the inspector's build quote (U4), so one issue is never named two ways.
    /// </summary>
    public static string Issue(DraftIssueKind kind) => kind switch
    {
        DraftIssueKind.IncompatibleExecutor => "INCOMPATIBLE EXECUTOR",
        DraftIssueKind.NoSuchRoute => "NO ROUTE",
        DraftIssueKind.UnknownEndpoint => "UNKNOWN ENDPOINT",
        DraftIssueKind.NonPositiveQuantity => "NON-POSITIVE QUANTITY",
        DraftIssueKind.UnbuiltExecutor => "UNBUILT EXECUTOR",
        DraftIssueKind.NotCommandable => "NOT COMMANDABLE",
        DraftIssueKind.MaterialShortage => "NOT IN HOLD — ACQUIRE",
        DraftIssueKind.WorkpieceNotAccepted => "WORKPIECE NOT ACCEPTED THERE",
        _ => kind.ToString().ToUpperInvariant(),
    };
```

Delete `OperationsFocus.DescribeIssue`, then run:

```bash
sed -i 's/DescribeIssue(/Labels.Issue(/g' dimenship/scripts/ui/focus/operations/OperationsFocus*.cs
```

- [ ] **Step 2: Add the build state to the inspector**

These fields go beside `_pendingActionCommand`:

```csharp
    /// <summary>A construction goal is one whole unit: commissioning withdraws exactly this much.</summary>
    private const long WholeUnit = 1000;

    /// <summary>
    /// BUILD commits a plan that takes material from the hold, so it takes a second press, as the
    /// plan controls' cancel does (U1). It is disarmed by any other selection, by the slot being
    /// ordered, and by its draft stopping being committable.
    /// </summary>
    private bool _buildArmed;

    /// <summary>The selection the armed state and the refusal belong to; a different one resets both.</summary>
    private string? _buildFor;

    /// <summary>The planner's refusal of the last confirmed BUILD, shown until the selection changes.</summary>
    private string? _buildRefusal;

    private Label _actionFeedback = null!;
```

In `_Ready`, after `actionArea.AddChild(_actionButton);`:

```csharp
        _actionFeedback = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _actionFeedback.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _actionFeedback.AddThemeColorOverride("font_color", ShellPalette.StateFault);
        column.AddChild(_actionFeedback);
```

In `OnSnapshot`, after `_pendingActionCommand = null;`:

```csharp
        _actionButton.Disabled = false;

        var selectedId = _context?.CurrentSelection?.Id;
        if (selectedId != _buildFor)
        {
            _buildFor = selectedId;
            _buildArmed = false;
            _buildRefusal = null;
        }

        _actionFeedback.Text = _buildRefusal ?? string.Empty;
        _actionFeedback.Visible = _buildRefusal is not null;
```

- [ ] **Step 3: Replace the unbuilt branch's action with the quote and BUILD**

Replace from `var progress = ConstructionProgress.For(snapshot, executor.Id);` through the end of
the `else { ... PLAN CONSTRUCTION… ... }` block with:

```csharp
            var progress = ConstructionProgress.For(snapshot, executor.Id);
            if (progress.Plan is { } planId)
            {
                _buildArmed = false;
                _buildRefusal = null;
                _actionFeedback.Visible = false;
                _actionButton.Text = "VIEW PLAN";
                _actionButton.Visible = true;
                _pendingActionCommand = () =>
                    _context?.Actions.OperationsRequested?.Invoke(new PendingOperationsTarget(Plan: planId));
                return;
            }

            // The composer's own door, so the quote is of the draft BUILD would commit (U4). It is
            // recomposed per snapshot while the slot is selected and unordered: one small planner
            // pass, and a quote that never shows the world as it was a minute ago.
            if (_context?.ComposeDraft is not { } compose)
            {
                return;
            }

            var goal = new ItemAmount(unit, WholeUnit);
            var quote = BuildQuote.For(
                compose(goal, executor.LocalStorage, executor.Id),
                ShellContent.Catalog.Schematics,
                ShellContent.DefaultVessel.Hold);
            Quote(snapshot, quote);

            if (!quote.Committable)
            {
                _buildArmed = false;
            }

            _actionButton.Text = _buildArmed ? "CONFIRM BUILD" : "BUILD";
            _actionButton.Disabled = !quote.Committable;
            _actionButton.Visible = true;
            var slot = executor.Id;
            var buffer = executor.LocalStorage;
            _pendingActionCommand = () => OnBuildPressed(goal, buffer, slot);
```

Add these two methods beside `Gone`:

```csharp
    /// <summary>
    /// The quote's rows. Cost is what leaves the hold. A shortage reads as a wait, because it never
    /// blocks approval, and a warning as a note, because the draft would commit and might never
    /// deliver. What blocks is named in the fault colour.
    /// </summary>
    private void Quote(WorldSnapshot snapshot, BuildQuote quote)
    {
        if (quote.MadeAt is { } at)
        {
            var label = snapshot.Executors.FirstOrDefault(e => e.Id == at)?.Label ?? at.Value;
            Row("MADE AT", label.ToUpperInvariant(), ShellPalette.TextPrimary, null, new IconRef("status", "active"));
        }

        for (var i = 0; i < quote.FromHold.Count; i++)
        {
            var cost = quote.FromHold[i];
            Row(i == 0 ? "COST" : string.Empty, $"{Units.Format(cost.Quantity)} {Labels.Item(cost.Item)}",
                ShellPalette.TextPrimary, null, new IconRef("item", cost.Item.Value));
        }

        foreach (var shortage in quote.Short)
        {
            Row("SHORT", $"WILL WAIT FOR {Units.Format(shortage.Quantity)} {Labels.Item(shortage.Item)}",
                ShellPalette.StateWarn, null, new IconRef("item", shortage.Item.Value));
        }

        foreach (var warning in quote.Warnings)
        {
            Row("NOTE", $"{Labels.Issue(warning.Kind)} · {Labels.Item(warning.Item)}", ShellPalette.StateWarn);
        }

        foreach (var blocking in quote.Blocking)
        {
            Row("BLOCKED", $"{Labels.Issue(blocking.Kind)} · {Labels.Item(blocking.Item)}",
                ShellPalette.StateFault, null, new IconRef("status", "blocked"));
        }

        Row("EST", Units.FormatSimTime(quote.EstimatedTicks), ShellPalette.TextPrimary, null,
            new IconRef("status", "time"));
    }

    /// <summary>
    /// The first press arms; the second composes a fresh draft and approves it, so anything
    /// committed or built since the preview is accounted for. A refusal is shown as the planner's
    /// issues and disarms. Acceptance needs no words: the engine rebuilt its snapshot inside the
    /// commit, so the next frame shows the slot QUEUED and the button VIEW PLAN, paused or not.
    /// </summary>
    private void OnBuildPressed(ItemAmount goal, StorageId buffer, ExecutorId slot)
    {
        if (!_buildArmed)
        {
            _buildArmed = true;
            _actionButton.Text = "CONFIRM BUILD";
            return;
        }

        _buildArmed = false;
        _actionButton.Text = "BUILD";
        if (_context?.ComposeDraft is not { } compose || _context.Actions.PlanApproved is not { } approve)
        {
            return;
        }

        _buildRefusal = approve(compose(goal, buffer, slot)) is PlanApprovalRefused refused
            ? "REFUSED: " + string.Join(", ", refused.Issues.Select(i => Labels.Issue(i.Kind)).Distinct())
            : null;
        _actionFeedback.Text = _buildRefusal ?? string.Empty;
        _actionFeedback.Visible = _buildRefusal is not null;
    }
```

Add `using Dimenship.Core.Planning.Draft;` to the inspector. Remove the comment above the unbuilt
branch that says composing "stays Operations' job". Replace it with one sentence: the inspector now
quotes and builds through the composer's door (U4), which reverses construction Decision 3.

- [ ] **Step 4: Build the Godot assembly**

Run: `dotnet build dimenship/Dimenship.csproj -v q`

Expected: `Build succeeded.` with 0 errors. Fix any missing `using` the compiler names.

- [ ] **Step 5: Commit** with subject
  `feat: an unbuilt slot is quoted and built from the inspector, on a confirming second press` and
  `Refs #77`.

---

### Task 5: The inspector's queue lists plans, and a planned row opens its plan

**Files:**
- Modify: `dimenship/scripts/ui/panels/FacilityInspectorPanel.cs`:
  - the built branch's QUEUE block (around lines 264 to 288);
  - `Row` and `Heading` (around 665 to 672);
  - `Sync`, `Line` and `DetailRow`.

**Interfaces:**
- Consumes: `FacilityQueue.For(snapshot, executor.Id)` (Task 2), `FacilityQueueRow`,
  `PendingOperationsTarget(Plan: id)` and `Conditions.Describe`.
- Produces: `Line` gains a last field, `PlanId? Opens`. `DetailRow` gains
  `Action<PlanId>? Open { get; init; }`.

- [ ] **Step 1: Let a recycled row open a plan**

Change the `Line` record and the two writers:

```csharp
    private readonly record struct Line(
        string Name, string Value, Color Color, float? Fill, bool IsHeading, IconRef? Icon, PlanId? Opens);

    private void Heading(string text, IconRef? icon = null) =>
        _lines.Add(
            new Line(text.ToUpperInvariant(), string.Empty, ShellPalette.TextFaint, null, true, icon, null));

    private void Row(
        string name, string value, Color color, float? fill = null, IconRef? icon = null, PlanId? opens = null) =>
        _lines.Add(new Line(name.ToUpperInvariant(), value, color, fill, false, icon, opens));

    /// <summary>A queue row's press: Operations opened on the plan, the same door VIEW PLAN uses.</summary>
    private void OpenPlan(PlanId plan) =>
        _context?.Actions.OperationsRequested?.Invoke(new PendingOperationsTarget(Plan: plan));
```

In `Sync`, change `_rows.AddChild(new DetailRow());` to
`_rows.AddChild(new DetailRow { Open = OpenPlan });`.

In `DetailRow`, add:

```csharp
        /// <summary>
        /// What a press on a row naming a plan does. A row is recycled between lines, so the plan
        /// is read from the line it holds now, never captured when the row was made. U4 made the
        /// queue's rows open their plan, and a separate pool of buttons beside these rows would
        /// have split the panel's order in two.
        /// </summary>
        public Action<PlanId>? Open { get; init; }

        private PlanId? _opens;

        public override void _GuiInput(InputEvent @event)
        {
            if (_opens is { } plan && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Open?.Invoke(plan);
                AcceptEvent();
            }
        }
```

At the top of `Update(Line line)`, add:

```csharp
            _opens = line.Opens;
            MouseFilter = line.Opens is null ? MouseFilterEnum.Pass : MouseFilterEnum.Stop;
            MouseDefaultCursorShape = line.Opens is null ? CursorShape.Arrow : CursorShape.PointingHand;
```

- [ ] **Step 2: Render the queue from `FacilityQueue`**

Replace the block from `Heading("QUEUE", ...)` through the end of its `foreach (var task in queued)`
loop with:

```csharp
        Heading("QUEUE", new IconRef("status", "queue"));
        var queue = FacilityQueue.For(snapshot, executor.Id);
        if (queue.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var row in queue.Take(MaxQueueRows))
        {
            QueueRow(row);
        }

        // A side panel, not a campaign's ledger: past this many, Operations is where to read on.
        if (queue.Count > MaxQueueRows)
        {
            Row($"+{queue.Count - MaxQueueRows} MORE", "SEE OPERATIONS", ShellPalette.TextFaint);
        }
```

Add `private const int MaxQueueRows = 8;` and:

```csharp
    /// <summary>
    /// One task in up to three lines: what it runs and how far along; which plan it is for, at what
    /// priority (that line opens the plan); and who it waits behind, when K8 names someone. A task
    /// queued by hand says so and opens nothing. A standing order shows a running total rather than
    /// a ratio with an invented denominator.
    /// </summary>
    private void QueueRow(FacilityQueueRow row)
    {
        var progress = row.Runs is { } requested
            ? $"{row.CompletedRuns}/{requested}"
            : $"{row.CompletedRuns} · STANDING";
        var (state, color, glyph) = row.Reason is { } reason
            ? ($"WAITING — {Conditions.Describe(reason)}", ShellPalette.StateWarn, "blocked")
            : row.Current
                ? ("RUNNING", ShellPalette.StateOk, "active")
                : ("QUEUED", ShellPalette.TextFaint, "idle");
        Row($"{(row.Current ? "▶ " : string.Empty)}{row.Schematic.Value} {progress}", state, color, null,
            new IconRef("status", glyph), row.Plan);

        var standing = row.Held
            ? " · HELD"
            : row.Priority != Priority.Normal ? $" · {row.Priority.ToString().ToUpperInvariant()}" : string.Empty;
        if (row.Plan is { } plan && row.PlanGoal is { } goal)
        {
            Row($"  FOR PLAN {plan}", $"→ {Units.Format(goal.Quantity)} {Labels.Item(goal.Item)}{standing}",
                ShellPalette.TextDim, null, null, plan);
        }
        else
        {
            Row("  BY HAND", standing.Length == 0 ? "NORMAL" : standing.TrimStart(' ', '·'), ShellPalette.TextFaint);
        }

        if (row.Waiting is { } cause)
        {
            Row("  BEHIND", cause.Text, ShellPalette.StateWarn, null, null, row.Plan);
        }
    }
```

Delete the now-unused `Glyph(TaskState)` and `TaskState(...)` helpers only if no other call site
remains. Check with `grep -n "TaskState(\|Glyph(task" dimenship/scripts/ui/panels/FacilityInspectorPanel.cs`.

- [ ] **Step 3: Build the Godot assembly**

Run: `dotnet build dimenship/Dimenship.csproj -v q`

Expected: `Build succeeded.`

- [ ] **Step 4: Commit** with subject
  `feat: the inspector lists a facility's queue by plan, and a planned row opens that plan` and
  `Refs #77`.

---

### Task 6: Operations' plan controls gain MOVE

**Files:**
- Modify: `dimenship/scripts/ui/focus/operations/OperationsFocus.PlanControls.cs`:
  - the fields;
  - `BuildPlanControls`;
  - `RefreshPlanControls`;
  - `Issue`, which now returns `CommandResult?`;
  - new methods `FillMoveTargets` and `OnMovePressed`.
- Modify: `dimenship/scripts/ui/focus/operations/OperationsFocus.cs:1153`. The call becomes
  `RefreshPlanControls(plan, snapshot);`.

**Interfaces:**
- Consumes:
  - `MoveOptions.For(snapshot, ShellContent.Catalog.Schematics, plan.Id)` (Task 3);
  - `MoveWork(PlanId, SchematicId, ExecutorId)` (K6d);
  - `ShellActions.Execute`, typed `Func<Command, CommandResult>?`.
- Produces: none that later tasks use.

- [ ] **Step 1: Add the fields and the row**

Add these fields:

```csharp
    private OptionButton _moveSchematic = null!;
    private OptionButton _moveTo = null!;
    private Button _move = null!;
    private IReadOnlyList<MoveOption> _moveOptions = Array.Empty<MoveOption>();

    /// <summary>
    /// What the move lists were last filled from. They are refilled only when it changes, so a list
    /// the player has open survives the next snapshot, as the priority list does.
    /// </summary>
    private string? _moveSignature;
```

In `BuildPlanControls`, insert this before
`_commandFeedback = new Label { ... };`:

```csharp
        var third = new HBoxContainer();
        third.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(third);

        third.AddChild(Caption("MOVE:"));
        _moveSchematic = new OptionButton { FocusMode = FocusModeEnum.None };
        _moveSchematic.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _moveSchematic.ItemSelected += _ => FillMoveTargets();
        third.AddChild(_moveSchematic);

        third.AddChild(Caption("TO"));
        _moveTo = new OptionButton { FocusMode = FocusModeEnum.None };
        _moveTo.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        third.AddChild(_moveTo);

        _move = ControlButton("MOVE");
        _move.Pressed += OnMovePressed;
        third.AddChild(_move);
```

- [ ] **Step 2: Refresh it per snapshot**

Change the signature to `private void RefreshPlanControls(CommittedPlanState plan, WorldSnapshot snapshot)`.
Inside the `if (_controlsPlan != plan.Id)` block, add `_moveSignature = null;`. At the end of the
method, add:

```csharp
        var options = MoveOptions.For(snapshot, ShellContent.Catalog.Schematics, plan.Id);
        var signature = string.Join(";", options.Select(o => $"{o.Schematic}>{string.Join(",", o.To)}"));
        if (signature != _moveSignature)
        {
            _moveSignature = signature;
            var kept = _moveOptions.ElementAtOrDefault(_moveSchematic.Selected)?.Schematic;
            _moveOptions = options;
            _moveSchematic.Clear();
            foreach (var option in options)
            {
                _moveSchematic.AddItem(option.Schematic.Value.ToUpperInvariant());
            }

            var index = options.ToList().FindIndex(o => o.Schematic == kept);
            if (options.Count > 0)
            {
                _moveSchematic.Select(Math.Max(0, index));
            }

            FillMoveTargets();
        }

        _moveSchematic.Disabled = !active || _moveOptions.Count == 0;
        _moveTo.Disabled = !active || _moveTo.ItemCount == 0;
        _move.Disabled = !active || _moveTo.ItemCount == 0;
```

Update the call in `OperationsFocus.cs` (around line 1153) to `RefreshPlanControls(plan, snapshot);`.

- [ ] **Step 3: Fill targets and press**

```csharp
    /// <summary>
    /// The facilities the selected stage can go to, labelled as the graph labels them. The choice
    /// is kept across a refill when it is still offered.
    /// </summary>
    private void FillMoveTargets()
    {
        var kept = _moveTo.Selected >= 0 ? _moveTo.GetItemMetadata(_moveTo.Selected).AsString() : null;
        _moveTo.Clear();
        if (_moveOptions.ElementAtOrDefault(_moveSchematic.Selected) is not { } option)
        {
            return;
        }

        for (var i = 0; i < option.To.Count; i++)
        {
            var id = option.To[i];
            var label = _lastSnapshot?.Executors.FirstOrDefault(e => e.Id == id)?.Label ?? id.Value;
            _moveTo.AddItem(label.ToUpperInvariant());
            _moveTo.SetItemMetadata(i, id.Value);
            if (id.Value == kept)
            {
                _moveTo.Select(i);
            }
        }

        if (_moveTo.Selected < 0 && _moveTo.ItemCount > 0)
        {
            _moveTo.Select(0);
        }
    }

    /// <summary>
    /// Sends <see cref="MoveWork"/> (K6d). Whether any run can move is the kernel's to decide: a
    /// refusal, such as material already sent or no line, is shown as it worded it.
    /// </summary>
    private void OnMovePressed()
    {
        if (_selectedPlan is not { } plan
            || _moveOptions.ElementAtOrDefault(_moveSchematic.Selected) is not { } option
            || _moveTo.Selected < 0)
        {
            return;
        }

        var to = new ExecutorId(_moveTo.GetItemMetadata(_moveTo.Selected).AsString());
        if (Issue(new MoveWork(plan, option.Schematic, to)) is CommandAccepted)
        {
            _commandFeedback.Text =
                $"MOVED {option.Schematic.Value.ToUpperInvariant()} TO {_moveTo.GetItemText(_moveTo.Selected)}";
            _commandFeedback.AddThemeColorOverride("font_color", ShellPalette.StateOk);
        }
    }
```

Change `Issue` to return its result. The signature becomes
`private CommandResult? Issue(Command command)`, and `return result;` goes at the end. The existing
callers discard it unchanged. Add `using Dimenship.Core.Presentation;` and
`using System.Collections.Generic;`. Confirm that `_lastSnapshot` is the field name in
`OperationsFocus.cs` with `grep -n "_lastSnapshot" dimenship/scripts/ui/focus/operations/OperationsFocus.cs | head -3`.

- [ ] **Step 4: Build the Godot assembly**

Run: `dotnet build dimenship/Dimenship.csproj -v q`

Expected: `Build succeeded.`

- [ ] **Step 5: Commit** with subject
  `feat: a committed plan's stage can be moved to another facility from Operations` and
  `Refs #77`.

---

### Task 7: Headless smoke through the real shell, then the docs and the closing commit

**Files:**
- Create (throwaway, not committed): `$SCRATCH/u4_smoke.gd`, where `$SCRATCH` is the session
  scratchpad.
- Modify: `docs/superpowers/specs/2026-10-11-playable-inspector-and-move-control-design.md`.
  - Decision 2 gains `Warnings` and `Committable`, and loses `Blocking`-only wording.
  - Decision 4: the queue is rendered as clickable recycled rows, with no button pool.
  - Decision 5: eligibility is "some other facility holds runs". Commandability is left to the
    kernel. Accept feedback reads "MOVED {stage} TO {facility}".
  - The status changes to `Built`.
- Create: `docs/reviews/2026-10-11-u4-playable-inspector.md`.
- Modify: `docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`, adding a
  `**U4 Built**` paragraph after U3.
- Modify: `CLAUDE.md`.
  - In *The Godot shell*, replace "`OperationsRequested` is the inspector's construction button"
    wording where it says the inspector only routes to Operations. It now quotes and builds itself,
    and VIEW PLAN and queue rows route.
  - Add `BuildQuote`, `FacilityQueue` and `MoveOptions` to the `Presentation/` row of the namespace
    table.

- [ ] **Step 1: Write the smoke script**

```gdscript
extends SceneTree

var shell

func _initialize():
	shell = load("res://scenes/Shell.tscn").instantiate()
	root.add_child(shell)
	call_deferred("_run")

func frames(n):
	for i in n:
		await process_frame

func all_nodes(node, out):
	out.append(node)
	for c in node.get_children():
		all_nodes(c, out)
	return out

func script_named(n, file):
	var s = n.get_script()
	return s != null and s.resource_path.ends_with(file)

func visible_labels(prefix):
	var out = []
	for n in all_nodes(root, []):
		if n is Label and n.is_visible_in_tree() and n.text.begins_with(prefix):
			out.append(n)
	return out

func button(text):
	for n in all_nodes(root, []):
		if n is Button and n.is_visible_in_tree() and n.text == text:
			return n
	return null

func click(node):
	var ev = InputEventMouseButton.new()
	ev.button_index = MOUSE_BUTTON_LEFT
	ev.pressed = true
	node._gui_input(ev)
	await frames(5)

func card(title):
	for n in all_nodes(root, []):
		if script_named(n, "ExecutorCard.cs"):
			for l in all_nodes(n, []):
				if l is Label and l.text.to_upper() == title:
					return n
	return null

func row_value(name):
	for l in visible_labels(name):
		var row = l.get_parent()
		for c in row.get_children():
			if c is Label and c != l and c.text != "":
				return c.text
	return ""

func _run():
	await frames(20)
	# 1. Build Factory Beta from the inspector, paused, without leaving the graph.
	await click(card("FACTORY BETA"))
	print("SMOKE made at: ", row_value("MADE AT"), " cost: ", row_value("COST"), " est: ", row_value("EST"))
	var b = button("BUILD")
	print("SMOKE build button: ", b != null and not b.disabled)
	b.emit_signal("pressed")
	await frames(3)
	print("SMOKE armed: ", button("CONFIRM BUILD") != null)
	# Review focus 4: another selection disarms.
	await click(card("REACTOR BETA"))
	print("SMOKE disarmed on reselect: ", button("CONFIRM BUILD") == null and button("BUILD") != null)
	await click(card("FACTORY BETA"))
	button("BUILD").emit_signal("pressed")
	await frames(3)
	button("CONFIRM BUILD").emit_signal("pressed")
	await frames(8)
	print("SMOKE ordered: ", button("VIEW PLAN") != null)
	# 2. Factory Alpha's queue names the plan; its row opens Operations on it.
	await click(card("FACTORY ALPHA"))
	var plan_rows = visible_labels("  FOR PLAN")
	print("SMOKE queue rows: ", plan_rows.size(), " ", row_value("  FOR PLAN"))
	if plan_rows.is_empty():
		quit(1)
		return
	await click(plan_rows[0].get_parent().get_parent())
	await frames(10)
	print("SMOKE operations open: ", button("+ NEW") != null)
	# 3. The MOVE row: Factory Alpha is the only factory built, so nothing to move to.
	var move = button("MOVE")
	print("SMOKE move row: ", move != null, " disabled=", move.disabled if move else "n/a")
	quit(0)
```

- [ ] **Step 2: Run the smoke**

Run:

```bash
cd dimenship && dotnet build Dimenship.csproj -v q && \
  /c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path . --script "$SCRATCH/u4_smoke.gd" 2>&1 | grep SMOKE
```

Expected lines:
- `SMOKE made at: FACTORY ALPHA cost: 0.4 BASIC METALS est: …`, with the exact number format
  `Units.Format` gives;
- `SMOKE build button: true`;
- `SMOKE armed: true`;
- `SMOKE disarmed on reselect: true`;
- `SMOKE ordered: true`;
- `SMOKE queue rows: 1 → 1 FACTORY CONSTRUCTION UNIT`, or similar;
- `SMOKE operations open: true`;
- `SMOKE move row: true disabled=true`.

Any `false` is a defect in Tasks 4 to 6. Fix it there and rerun. Do not adjust the smoke to pass.
If a card's title is not the upper-cased label (for example a different `REACTOR BETA` text), print
every `ExecutorCard` label once and use the real text.

- [ ] **Step 3: Run every suite and the replay baseline**

```bash
dotnet test tests/Dimenship.Core.Tests && dotnet test tests/Dimenship.Shell.Tests && dotnet test tests/Dimenship.Replay.Tests
```

Expected:
- Core is 447: 435 plus 12 new.
- Shell is 108.
- Replay is 54.
- All pass. No replay changes, because nothing in the kernel's behaviour changed.

- [ ] **Step 4: Write the docs**

- **The spec:** apply the three corrections listed under **Files**, and set `Status: Built
  (2026-10-11, #77)`.
- **The review (`docs/reviews/2026-10-11-u4-playable-inspector.md`):**
  - Goal; what was built, one bullet per projection and surface;
  - the deviations from the spec and why;
  - the smoke's printed lines verbatim;
  - Not done (upgrades, plan commands in the inspector, more than 8 queue rows, a MOVE accept in
    the smoke, since the opening vessel has one factory);
  - the test counts.
- **The plan's `**U4 Built**` paragraph:** the house style of U1 to U3, five to eight lines,
  linking the review.
- **CLAUDE.md:** the two edits listed under **Files**.

- [ ] **Step 5: Commit and push**

```bash
git add -A
git commit -F- <<'EOF'
docs: the inspector's build, queue and Operations' move are recorded as built

<prose body: smoke result, deviations from the spec, what is not done>

Closes #77

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SEW1WTtp1GwDbNZgTk7oDc
EOF
git push origin master
```
