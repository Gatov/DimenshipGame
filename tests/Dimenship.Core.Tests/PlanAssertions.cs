using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Tests;

/// <summary>
/// Test-only projections of <see cref="ProductionPlan.Tasks"/> back into the run/transfer shape
/// planner tests were written against before Stage 5 merged runs and transfers into one ordered
/// list. Keeps assertions that only ever cared about "what got planned" from restating the same
/// action cast every time.
/// </summary>
internal static class PlanAssertions
{
    public sealed record PlannedRunView(
        SchematicId Schematic, ExecutorId Executor, int Runs, long AvailableAtSource);

    public sealed record PlannedTransferView(
        ItemId Item, long Quantity, StorageId From, StorageId To, ExecutorId Executor,
        long AvailableAtSource);

    public static IReadOnlyList<PlannedRunView> Runs(this ProductionPlan plan) =>
        plan.Tasks
            .Where(t => t.Script.Action is Produce)
            .Select(t =>
            {
                var produce = (Produce)t.Script.Action;
                return new PlannedRunView(produce.Schematic, t.Executor, produce.Runs!.Value, t.AvailableAtSource);
            })
            .ToList();

    public static IReadOnlyList<PlannedTransferView> Transfers(this ProductionPlan plan) =>
        plan.Tasks
            .Where(t => t.Script.Action is Transfer)
            .Select(t =>
            {
                var transfer = (Transfer)t.Script.Action;
                return new PlannedTransferView(
                    transfer.Item, transfer.Quantity!.Value, transfer.From, transfer.To, t.Executor,
                    t.AvailableAtSource);
            })
            .ToList();
}
