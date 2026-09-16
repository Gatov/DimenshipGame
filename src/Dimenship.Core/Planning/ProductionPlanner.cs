using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>
/// Turns a player goal into the tasks that would fulfil it. Pure: it reads a world view and
/// returns a plan, and changes nothing.
/// <para>
/// Expansion now lives in <see cref="PlanDraftEditor"/> so the requirement graph survives for the
/// Operations editor; this entry point is <c>Create(...).Flatten()</c> and must stay behaviour-
/// identical to the former flat lists. See
/// <c>2026-09-16-editable-production-plans-design.md</c> Decision 1.
/// </para>
/// </summary>
public static class ProductionPlanner
{
    /// <summary>
    /// How deep a schematic chain may go. A chain longer than this is a content error rather than
    /// a deep recipe, and a diagnosable shortage beats a stack overflow.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// <paramref name="destination"/> appends one final transfer, hold → destination, for the
    /// goal amount — the delivery that makes a goal actually reach somewhere rather than stopping
    /// at the hold. Null when the plan has no final delivery.
    /// </summary>
    public static ProductionPlan Plan(ItemAmount goal, IWorldView world, StorageId? destination = null) =>
        PlanDraftEditor.Create(goal, world, destination).Flatten();
}
