using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>Why part of a goal could not be planned.</summary>
public enum ShortageKind
{
    /// <summary>
    /// Nothing aboard produces it. It must be acquired — this is what an expedition is for, and
    /// the only shortage kind hauling can fix.
    /// </summary>
    RawResource,

    /// <summary>A schematic produces it, but the player has not unlocked one. No branch is built.</summary>
    LockedSchematic,

    /// <summary>The schematic chain re-entered itself. A content error, not a supply problem.</summary>
    CyclicSchematic,

    /// <summary>The vessel has no facility of the required type, or no transport line to route it.</summary>
    NoCompatibleExecutor,
}

/// <summary>A number of executions of one schematic, proposed for one facility.</summary>
public sealed record PlannedRun(SchematicId Schematic, ExecutorId Executor, int Runs);

/// <summary>Material a plan needs moved, and the line proposed to move it.</summary>
public sealed record PlannedTransfer(
    ItemId Item, long Quantity, StorageId From, StorageId To, ExecutorId Executor);

/// <summary>Something the plan could not supply, and why.</summary>
public sealed record PlanShortage(ItemId Item, long Missing, ShortageKind Kind);

/// <summary>
/// How a goal may be fulfilled. Proposals, not tasks: nothing here has an execution state, and
/// nothing exists in the world until <see cref="Simulation.SimulationEngine.Commit"/> injects it
/// into executor queues.
/// <para>
/// A plan carrying shortages is still committable. The available portion may begin immediately
/// while the player decides whether to mount an expedition for the rest.
/// </para>
/// </summary>
public sealed record ProductionPlan(
    ItemAmount Goal,
    IReadOnlyList<PlannedRun> Runs,
    IReadOnlyList<PlannedTransfer> Transfers,
    IReadOnlyList<PlanShortage> Shortages)
{
    /// <summary>True when the plan can be carried out in full with what the vessel has.</summary>
    public bool IsComplete => Shortages.Count == 0;

    /// <summary>
    /// True when the only thing standing in the way is material that must be acquired — the case
    /// where suggesting an expedition is the right thing to tell the player.
    /// </summary>
    public bool NeedsAcquisition
    {
        get
        {
            foreach (var shortage in Shortages)
            {
                if (shortage.Kind != ShortageKind.RawResource)
                {
                    return false;
                }
            }

            return Shortages.Count > 0;
        }
    }
}
