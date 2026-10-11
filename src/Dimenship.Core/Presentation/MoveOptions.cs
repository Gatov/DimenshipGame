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
