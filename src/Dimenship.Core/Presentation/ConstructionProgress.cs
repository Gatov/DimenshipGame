using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Presentation;

/// <summary>
/// Where an unbuilt slot's committed construction plan has got to, in the schematic's and the
/// inspector's own words.
/// <para>
/// <see cref="Unplanned"/> and <see cref="Complete"/> bookend a plan's life; <see cref="Queued"/>,
/// <see cref="ProducingUnit"/> and <see cref="InTransit"/> are progress inside it, in the order the
/// GDD's Build plan actually runs (a run, then a delivery); <see cref="Blocked"/> is not a stage in
/// that sequence but a report over it — see <see cref="ConstructionProgress"/>.
/// </para>
/// </summary>
public enum ConstructionPhase
{
    Unplanned,
    Queued,
    ProducingUnit,
    InTransit,
    Blocked,
    Complete,
}

/// <summary>
/// A pure projection of one slot's construction, over a snapshot and nothing else — no engine
/// call, no world reference kept, thrown away the moment the caller is done with it.
/// <para>
/// This exists so the schematic and the Operations detail read one answer rather than two. Both
/// need "where has this plan got to", and a phase field on <c>ExecutorState</c> would make the
/// kernel own interface vocabulary and would have to be recomputed every tick for every slot
/// whether or not anything is looking; a helper in <c>Dimenship.Shell</c> cannot name a
/// <see cref="CommittedPlanState"/> at all, since that assembly does not reference
/// <c>Dimenship.Core</c> by construction. A projection beside <see cref="BaseGraphLayout"/> —
/// carrying Core ids, no rendering type, no float, and reachable from the kernel test suite — is
/// the shape both callers can share.
/// </para>
/// <para>
/// <b>Attribution.</b> A slot names no plan of its own; <see cref="CommittedPlanState.Destination"/>
/// is the storage a plan ends at, and a slot's <see cref="ExecutorState.LocalStorage"/> is that
/// storage, so the plan building a given slot is found by matching the two. The most recently
/// committed match wins over an older one with the same destination — which in practice never
/// happens twice for one slot, since nothing rebuilds an already-built facility — rather than
/// requiring the match to still be <see cref="PlanState.Active"/>: a plan's own state
/// flips to <see cref="PlanState.Complete"/> the instant its last task retires, which
/// for a real Build plan is the same tick commissioning sets <see cref="ExecutorState.Built"/>, so
/// requiring "active" would make a slot momentarily unattributable at the exact tick it finishes,
/// and would make an artificially long-lived plan (one whose tasks retire before the delivery that
/// depends on them, see <see cref="ConstructionPhase.Blocked"/> below) attribute to nothing at all once the registry
/// forgets its last live task.
/// </para>
/// <para>
/// <b>Blocked is reported over every other phase</b>, per the GDD's diagnostic rule that a serious
/// reading answers what is wrong before it answers how far along — the same rule that put a total
/// order on <see cref="PostponeReason"/> in the first place. So this checks every spawned task for
/// <see cref="TaskState.Postponed"/> before it checks whether anything is running or in transit,
/// and picks the cause with <see cref="PostponeReasons.RootCause"/> rather than the first
/// postponed task found — otherwise the schematic and the Operations detail could name two
/// different reasons for one stall. A consequence worth stating plainly: a Build plan's own
/// delivery leg is queued from the moment the plan commits, and a transport line that finds
/// nothing yet at its source reports <see cref="PostponeReason.InsufficientSourceMaterial"/> on
/// that task every tick until the run deposits something — so a real Build plan reads
/// <see cref="ConstructionPhase.Blocked"/>, not <see cref="ConstructionPhase.ProducingUnit"/>, for the entire time its factory is
/// working. That is not a misreading; it is the literal truth that the delivery is, right now,
/// unable to proceed, and the GDD's rule says that is what the card owes the player first.
/// </para>
/// <para>
/// <b>A retired task resolves as complete.</b> <see cref="WorldSnapshot.Tasks"/> is the
/// engine's bounded retirement window (see <c>TaskRegistry</c>); a <see cref="TaskId"/> named in
/// <see cref="CommittedPlanState.SpawnedTasks"/> that no longer resolves there was retired and has
/// since aged out. Retirement only ever happens to a task that finished, so a missing id can only
/// mean "this step is done" — never "still waiting" and never a fault to crash on. Treating it any
/// other way would read a plan whose oldest steps have simply aged out of memory as stuck at its
/// first tick forever.
/// </para>
/// </summary>
public sealed record ConstructionProgress(ConstructionPhase Phase, PostponeReason? BlockedReason, PlanId? Plan)
{
    /// <summary>
    /// Projects one slot's construction phase from a snapshot. Pure: calling it twice on the same
    /// snapshot and slot always answers the same way, and nothing about the snapshot or the world
    /// it came from is touched.
    /// </summary>
    public static ConstructionProgress For(WorldSnapshot snapshot, ExecutorId slot)
    {
        ExecutorState? executor = null;
        foreach (var candidate in snapshot.Executors)
        {
            if (candidate.Id == slot)
            {
                executor = candidate;
                break;
            }
        }

        if (executor is null)
        {
            throw new KeyNotFoundException($"No executor '{slot}' in this snapshot.");
        }

        var plan = FindPlan(snapshot, executor.LocalStorage);

        if (executor.Built)
        {
            return new ConstructionProgress(ConstructionPhase.Complete, null, plan?.Id);
        }

        if (plan is null)
        {
            return new ConstructionProgress(ConstructionPhase.Unplanned, null, null);
        }

        // snapshot.Tasks is the engine's bounded retirement window (up to 512 entries), and the
        // only ids ever looked up here are the handful named by plan.SpawnedTasks — so the lookup
        // table is built by filtering to those ids up front rather than indexing every entry in the
        // snapshot, which would guarantee the dictionary rehashes past a capacity hint sized for
        // what it actually ends up holding.
        var spawnedIds = new HashSet<TaskId>(plan.SpawnedTasks);
        var tasksById = new Dictionary<TaskId, TaskInstanceState>(plan.SpawnedTasks.Count);
        foreach (var task in snapshot.Tasks)
        {
            if (spawnedIds.Contains(task.Id))
            {
                tasksById[task.Id] = task;
            }
        }

        var postponedReasons = new List<PostponeReason>();
        var anyTaskStarted = false;
        TaskInstanceState? produceTask = null;
        TaskInstanceState? lastTransfer = null;

        foreach (var taskId in plan.SpawnedTasks)
        {
            if (!tasksById.TryGetValue(taskId, out var task))
            {
                // Retired past the window: this step is done, not still waiting.
                anyTaskStarted = true;
                continue;
            }

            if (task.State != TaskState.NotStarted)
            {
                anyTaskStarted = true;
            }

            if (task.State == TaskState.Postponed && task.LastReason is { } reason)
            {
                postponedReasons.Add(reason);
            }

            switch (task.Action)
            {
                case Produce:
                    produceTask = task;
                    break;
                case Transfer:
                    // Overwritten on every match, so what is left after the loop is the last
                    // Transfer in spawned order — the destination-most delivery, which is the one
                    // "in transit" means.
                    lastTransfer = task;
                    break;
            }
        }

        if (postponedReasons.Count > 0)
        {
            return new ConstructionProgress(
                ConstructionPhase.Blocked, PostponeReasons.RootCause(postponedReasons), plan.Id);
        }

        if (!anyTaskStarted)
        {
            return new ConstructionProgress(ConstructionPhase.Queued, null, plan.Id);
        }

        if (produceTask is { State: TaskState.Running })
        {
            return new ConstructionProgress(ConstructionPhase.ProducingUnit, null, plan.Id);
        }

        if (lastTransfer is { } transfer && transfer.LoadedQuantity > transfer.MovedQuantity)
        {
            return new ConstructionProgress(ConstructionPhase.InTransit, null, plan.Id);
        }

        // Nothing is stuck, nothing is actively running or in flight, and something has already
        // started: as far as this snapshot can tell the plan's own work is behind it. A real Build
        // plan is here for one tick at most — the one between its last task retiring and
        // commissioning catching up — and a plan whose remaining tasks have all aged out of the
        // registry's window can never report anything else again.
        return new ConstructionProgress(ConstructionPhase.Complete, null, plan.Id);
    }

    /// <summary>
    /// The plan whose delivery ends at this storage, or null when none ever did. See the type's own
    /// remarks for why this does not filter by <see cref="PlanState"/>.
    /// </summary>
    private static CommittedPlanState? FindPlan(WorldSnapshot snapshot, StorageId localStorage)
    {
        CommittedPlanState? found = null;
        foreach (var candidate in snapshot.Plans)
        {
            if (candidate.Destination == localStorage)
            {
                found = candidate;
            }
        }

        return found;
    }
}
