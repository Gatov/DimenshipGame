using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>
/// A production facility, as the planner needs to see it.
/// <para>
/// <paramref name="Occupied"/> is separate from <paramref name="QueuedRuns"/> because a standing
/// order is permanently busy, and a run count cannot say that without picking a large number.
/// A facility with nothing indefinite queued is preferred over one that has, however deep its
/// queue: finite work drains, a standing order does not.
/// </para>
/// </summary>
public sealed record PlannerFacility(
    ExecutorId Id, FacilityType Type, StorageId LocalStorage, long QueuedRuns, bool Occupied);

/// <summary>
/// A transport line, as the planner needs to see it. The route is here because a line can only
/// serve the leg it was built for: choosing by load alone would pick a line that cannot make the
/// journey.
/// </summary>
public sealed record PlannerTransport(
    ExecutorId Id, StorageId From, StorageId To, long QueuedTransfers);

/// <summary>
/// Everything the planner is allowed to know. It takes this rather than the engine, which is what
/// keeps planning pure: a plan is a description of how a goal may be fulfilled, and producing one
/// must not change the world it describes.
/// </summary>
public interface IWorldView
{
    SchematicCatalog Schematics { get; }

    /// <summary>The storage plans route material through.</summary>
    StorageId Hold { get; }

    /// <summary>Production facilities, in world definition order.</summary>
    IReadOnlyList<PlannerFacility> Facilities { get; }

    /// <summary>Transport lines, in world definition order.</summary>
    IReadOnlyList<PlannerTransport> TransportLines { get; }

    /// <summary>
    /// How much of an item the vessel could spend on something new: everything aboard, less what
    /// committed tasks will consume, plus what tasks in flight will produce.
    /// <para>
    /// Netting out commitments is what stops planning the same goal twice from spending the same
    /// stock twice. It may be negative when the vessel is over-committed.
    /// </para>
    /// </summary>
    long Uncommitted(ItemId item);

    /// <summary>
    /// Whether the player may build from a schematic. The planner asks the world rather than the
    /// catalog: what is unlocked is a property of campaign progress, and the catalog holding that
    /// set today is where it lives, not where it belongs.
    /// </summary>
    bool IsUnlocked(SchematicId schematic);
}
