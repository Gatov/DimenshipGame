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
/// <para>
/// <paramref name="WorkRatePerTick"/> is the effective rate after upgrades — the same number
/// <c>SimulationEngine.WorkRate</c> charges a run against — so <c>EstimatedTicks</c> divides by
/// what a run will actually cost rather than by the archetype's unmodified figure.
/// </para>
/// <para>
/// <paramref name="Commandable"/> is false for a passive source. The view that builds this list
/// already skips those, the way it skips unbuilt ones; the flag is still here so a draft that
/// somehow names one can emit a precise <c>NotCommandable</c> issue rather than inventing a second
/// lookup. See <c>2026-09-16-editable-production-plans-design.md</c> Decision 11.
/// </para>
/// </summary>
public sealed record PlannerFacility(
    ExecutorId Id,
    FacilityType Type,
    StorageId LocalStorage,
    long QueuedRuns,
    bool Occupied,
    long WorkRatePerTick,
    bool Commandable);

/// <summary>
/// A transport line, as the planner needs to see it. The route is here because a line can only
/// serve the leg it was built for: choosing by load alone would pick a line that cannot make the
/// journey. <paramref name="ThroughputPerTick"/> is the effective, post-upgrade rate, for the same
/// reason <see cref="PlannerFacility.WorkRatePerTick"/> is.
/// <para>
/// <paramref name="LengthTicks"/> is here because it is time a plan spends and cannot spend
/// faster: a leg takes as long as the material needs to be picked up, plus the whole of the belt
/// once. An estimate that left it out would promise every plan a delivery it cannot make.
/// </para>
/// </summary>
public sealed record PlannerTransport(
    ExecutorId Id,
    StorageId From,
    StorageId To,
    long QueuedTransfers,
    long ThroughputPerTick,
    long LengthTicks);

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
    /// How much of an item sits in <see cref="Hold"/> right now. It is the planner's only supply.
    /// <para>
    /// Nothing else counts: not a facility buffer, not a Launch Pad hold, not cargo on a belt, and
    /// not the output of work already queued, even work bound for the hold. The planner can only
    /// route material out of the hold, so stock anywhere else is stock it cannot spend. Counting
    /// another plan's output was worse still: a second order read the first order's unit as its own,
    /// planned only a final haul, and stalled for good. The scheduling baseline
    /// (<c>docs/reviews/2026-10-09-scheduling-baseline.md</c>) records that defect, and this rule
    /// replaced the old vessel-wide <c>Uncommitted</c> arithmetic to remove it.
    /// </para>
    /// <para>
    /// The consequence is deliberate. Two plans ordered back to back each order their own
    /// production, and two plans that both read the same hold stock may both count on it.
    /// Sequencing orders, avoiding over-production and spending stock that is not yet in the hold
    /// is the optimization the game hands the player, not one the planner makes for them.
    /// </para>
    /// </summary>
    long InHold(ItemId item);

    /// <summary>
    /// Whether the player may build from a schematic. The planner asks the world rather than the
    /// catalog: what is unlocked is a property of campaign progress, and the catalog holding that
    /// set today is where it lives, not where it belongs.
    /// </summary>
    bool IsUnlocked(SchematicId schematic);
}
