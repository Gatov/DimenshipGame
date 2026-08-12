using System.Reflection;
using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.State;

/// <summary>
/// The rules that keep state from becoming a second rulebook, asserted rather than remembered.
/// Each of them fails quietly if left to discipline, and each is expensive to undo once a save
/// format has shipped carrying the mistake.
/// </summary>
public class WorldStateTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Refinery = new("refinery");

    [Test]
    public void NoStateType_HoldsAContentRecord()
    {
        // State stores ids and deltas, never definitions. A label or a work rate copied onto an
        // instance means a change in content never reaches an existing save — and the copy is
        // authoritative from that moment on, which is the whole failure.
        //
        // Ids are the exception, and the only one: they are how state refers to content at all.
        var content = typeof(ContentCatalog).Assembly.GetTypes()
            .Where(t => t.Namespace == "Dimenship.Core.Content" && !IsId(t))
            .ToHashSet();

        var offenders = new List<string>();

        foreach (var type in typeof(WorldState).Assembly.GetTypes()
            .Where(t => t.Namespace == "Dimenship.Core.State"))
        {
            foreach (var property in type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var named in Unwrap(property.PropertyType))
                {
                    if (content.Contains(named))
                    {
                        offenders.Add($"{type.Name}.{property.Name} is a {named.Name}");
                    }
                }
            }
        }

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ARenamedArchetype_ChangesWhatTheSnapshotShows()
    {
        // The catalog is the rulebook and the state is this world, so a label lives in exactly one
        // of them. Rebalancing or retitling a machine in content has to reach a campaign already
        // in progress.
        var builder = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor)
            .Producer(Refinery, FacilityType.MatterReactor, Smelt);

        var state = builder.State();
        var original = builder.Catalog();
        var renamed = original with
        {
            Facilities = original.Facilities
                .Select(f => f with { Label = "Refinery Mk. II" })
                .ToList(),
        };

        Assert.That(
            new SimulationEngine(original, state).Snapshot.Executors.Single().Label,
            Is.EqualTo("refinery"));
        Assert.That(
            new SimulationEngine(renamed, state).Snapshot.Executors.Single().Label,
            Is.EqualTo("Refinery Mk. II"));
    }

    [Test]
    public void AFacilityThePlayerRenamed_KeepsItsOwnName()
    {
        // The other half of the same rule: an override is a delta the player made, and content
        // moving underneath it must not overwrite what they chose.
        var builder = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor)
            .Producer(Refinery, FacilityType.MatterReactor, Smelt);

        var state = builder.State();
        state.Vessel.Facilities.Single().NameOverride = "Old Faithful";

        Assert.That(
            new SimulationEngine(builder.Catalog(), state).Snapshot.Executors.Single().Label,
            Is.EqualTo("Old Faithful"));
    }

    [Test]
    public void AnUpgradedWorkRate_ChangesExecutionWithoutTouchingTheSchematic()
    {
        // The point of the permille. Effort stays the schematic's and the task in flight is never
        // rewritten; only what the facility gets through in a tick moves.
        var builder = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: 400, inputs: new ItemAmount(Ore, 1))
            .Producer(Refinery, FacilityType.MatterReactor, Smelt, workRate: 100)
            .Task(Smelt, 10, Refinery);

        var plain = builder.Engine();
        plain.Advance(8);

        var upgraded = builder.State();
        upgraded.Vessel.Facilities.Single().WorkRatePermille = 2000;
        var faster = new SimulationEngine(builder.Catalog(), upgraded);
        faster.Advance(8);

        Assert.That(plain.Available(Hold, Alloy), Is.EqualTo(2), "four ticks a run at 100");
        Assert.That(faster.Available(Hold, Alloy), Is.EqualTo(4), "twice the rate, twice the runs");
        Assert.That(
            faster.Catalog.Schematics.Get(Smelt).EffortPerRun.Value,
            Is.EqualTo(400),
            "the schematic was rewritten to express an upgrade");
    }

    [Test]
    public void ACommittedPlan_ReportsItsOwnProgress()
    {
        // Tasks are per-executor by design, so the plan is the only level at which "how far along
        // is four alloy" has an answer at all.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 1))
            .Producer(Refinery, FacilityType.MatterReactor, Smelt)
            .Engine();

        var created = engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, 4), engine));
        var plan = engine.State.Plans.Plans.Single();

        Assert.That(plan.Goal, Is.EqualTo(new ItemAmount(Alloy, 4)));
        Assert.That(plan.SpawnedTasks, Is.EqualTo(created));
        Assert.That(plan.State, Is.EqualTo(PlanState.Active));
        Assert.That(plan.CompletedTasks, Is.Zero);

        engine.Advance(100);

        Assert.That(plan.State, Is.EqualTo(PlanState.Complete));
        Assert.That(plan.CompletedTasks, Is.EqualTo(plan.SpawnedTasks.Count));
    }

    [Test]
    public void AFinishedTask_LeavesItsExecutorsQueue()
    {
        // The registry keeps its body inside the retired window so the console still has something
        // to say about it, but the queue is what the executor evaluates every tick and a finished
        // task has no business in it.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 1))
            .Producer(Refinery, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, 2, Refinery)
            .Engine();

        engine.Advance(100);

        var facility = engine.State.Vessel.Facilities.Single();

        Assert.That(facility.Queue, Is.Empty);
        Assert.That(engine.State.Tasks.Retired, Has.Count.EqualTo(1));
        Assert.That(
            engine.State.Tasks.Production.Single().State,
            Is.EqualTo(TaskState.Complete),
            "the body is still readable inside the window");
    }

    [Test]
    public void TheRootCauseOrder_IsDeclaredOnce_AndPicksTheStrongestReason()
    {
        // "Root cause" is the highest-priority explanation among several true ones, and two panels
        // disagreeing about why a factory is stalled is the small lie the base graph refuses.
        var reasons = new[]
        {
            PostponeReason.InsufficientSourceMaterial,
            PostponeReason.SafetyLock,
            PostponeReason.InsufficientEnergy,
        };

        Assert.That(PostponeReasons.RootCause(reasons), Is.EqualTo(PostponeReason.SafetyLock));
        Assert.That(PostponeReasons.RootCause(Array.Empty<PostponeReason>()), Is.Null);
        Assert.That(
            PostponeReasons.Priority(PostponeReason.SafetyLock),
            Is.LessThan(PostponeReasons.Priority(PostponeReason.InsufficientSourceMaterial)));
    }

    private static bool IsId(Type type) => type.IsValueType && type.Name.EndsWith("Id");

    /// <summary>Every named type a property's type mentions, including the ones inside a list.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var inner in Unwrap(argument))
                {
                    yield return inner;
                }
            }
        }
    }
}
