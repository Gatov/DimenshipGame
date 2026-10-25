using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

/// <summary>
/// What an instance's numbers are under the catalog, and which tasks it holds: archetypes, rates
/// after upgrades, queues and effective priorities. Every one of these reads the archetype or the
/// registry at the point of use rather than copying a number onto an instance, which is what lets
/// an upgrade move a permille without touching a schematic or a run in flight.
/// <para>
/// A type of its own because the engine, <see cref="StorageMath"/>, <see cref="SnapshotBuilder"/>
/// and <see cref="EngineWorldView"/> all read these, and the last three must not reach the engine
/// to do it: a collaborator handed the engine can read its per-tick scratch, which is the accident
/// splitting them out exists to rule out. It holds the catalog and the state and nothing else, so
/// it cannot answer from anything a save does not carry.
/// </para>
/// </summary>
internal sealed class Resolver(ContentCatalog catalog, WorldState state)
{
    public FacilityArchetype Archetype(FacilityInstance facility) =>
        catalog.Facility(facility.Archetype)
        ?? throw new KeyNotFoundException($"No facility archetype '{facility.Archetype}'.");

    public TransportArchetype Archetype(TransportInstance line) =>
        catalog.Transport(line.Archetype)
        ?? throw new KeyNotFoundException($"No transport archetype '{line.Archetype}'.");

    public StorageArchetype Archetype(StorageInstance storage) =>
        catalog.Storage(storage.Archetype)
        ?? throw new KeyNotFoundException($"No storage archetype '{storage.Archetype}'.");

    /// <summary>Work per tick after upgrades, floored at one: a facility that does no work per
    /// tick would never finish a run, and a permille of zero is a content error, not a rate.</summary>
    public long WorkRate(FacilityInstance facility) =>
        Math.Max(1, Archetype(facility).WorkRatePerTick * facility.WorkRatePermille / 1000);

    public long SwitchOverTicks(FacilityInstance facility) => Archetype(facility).SwitchOverTicks;

    /// <inheritdoc cref="WorkRate"/>
    public long Throughput(TransportInstance line) =>
        Math.Max(1, Archetype(line).ThroughputPerTick * line.ThroughputPermille / 1000);

    /// <summary>
    /// How much a line can hold: one tick of intake per slot, and one slot per tick of length.
    /// Derived rather than authored, so a capacity can never drift from the throughput and the
    /// distance it is made of.
    /// </summary>
    public long Capacity(TransportInstance line) => Throughput(line) * line.LengthTicks;

    /// <summary>
    /// The tasks queued on a facility or line, in queue order. Instances hold ids and the registry
    /// holds bodies, because a task is referenced from an executor queue, a plan and the journal,
    /// and only one of those can own it.
    /// </summary>
    public IEnumerable<TaskInstance> Queued(IExecutorInstance executor)
    {
        foreach (var id in executor.Queue)
        {
            if (state.Tasks.Task(id) is { } task)
            {
                yield return task;
            }
        }
    }

    /// <summary>The task a facility or line has in hand: a facility's run, a line's transfer.</summary>
    public TaskInstance? CurrentOf(IExecutorInstance executor) =>
        executor.Current is { } id ? state.Tasks.Task(id) : null;

    /// <summary>
    /// A task's effective priority: its plan's when it has one, read live, and its own only when
    /// it was queued by hand (D3, Decision 1).
    /// </summary>
    public Priority PriorityOf(TaskInstance task) => state.Plans.Owning(task.Id)?.Priority ?? task.Priority;

    /// <summary>Whether a task's unstarted work is held: its plan's flag, or its own when it has none.</summary>
    public bool IsHeld(TaskInstance task) => state.Plans.Owning(task.Id)?.Held ?? task.Held;

    /// <summary>The vessel's power sinks, in the vessel's order, resolved against the catalog.</summary>
    public IEnumerable<PowerSinkDefinition> Sinks()
    {
        foreach (var id in state.Vessel.Sinks)
        {
            foreach (var sink in catalog.Sinks)
            {
                if (sink.Id == id)
                {
                    yield return sink;
                    break;
                }
            }
        }
    }
}
