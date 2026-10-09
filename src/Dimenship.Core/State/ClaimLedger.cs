using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.State;

/// <summary>
/// Material held for a committed plan (K6b; D3, Decision 5): <c>held(plan, storage, item)</c>, and
/// nothing else.
/// <para>
/// Need, inbound and outstanding are derived from the plan's tasks every time they are asked for
/// (<see cref="ClaimMath"/>), so a claim can never disagree with the work it backs. Storing them as
/// well would be a second answer that drifts the first time a task is retired. Entries with nothing
/// held are not stored, and entries iterate in (plan, storage, item) order, ordinal — the order the
/// save writes them in and the only order anything reads them in.
/// </para>
/// <para>
/// A claim is not a <c>Reservation</c>. The <c>ReservationLedger</c> is material withheld by an
/// installed program, and <c>RoomForDelivery</c>'s reservation is buffer room. A claim is stock,
/// held for the withdrawals one plan has not yet started.
/// </para>
/// </summary>
public sealed class ClaimLedger
{
    private static readonly IComparer<(long Plan, string Storage, string Item)> Order =
        Comparer<(long Plan, string Storage, string Item)>.Create((a, b) =>
        {
            var byPlan = a.Plan.CompareTo(b.Plan);
            if (byPlan != 0)
            {
                return byPlan;
            }

            var byStorage = string.CompareOrdinal(a.Storage, b.Storage);
            return byStorage != 0 ? byStorage : string.CompareOrdinal(a.Item, b.Item);
        });

    private readonly SortedDictionary<(long Plan, string Storage, string Item), long> _held = new(Order);

    /// <summary>Every held entry, in (plan, storage, item) order.</summary>
    public IEnumerable<(PlanId Plan, StorageId Storage, ItemId Item, long Held)> Entries =>
        _held.Select(e => (new PlanId(e.Key.Plan), new StorageId(e.Key.Storage), new ItemId(e.Key.Item), e.Value));

    public long Held(PlanId plan, StorageId storage, ItemId item) =>
        _held.GetValueOrDefault((plan.Value, storage.Value, item.Value));

    /// <summary>Everything held at one storage for one item, by every plan.</summary>
    public long HeldAt(StorageId storage, ItemId item)
    {
        var total = 0L;
        foreach (var entry in _held)
        {
            if (entry.Key.Storage == storage.Value && entry.Key.Item == item.Value)
            {
                total += entry.Value;
            }
        }

        return total;
    }

    /// <summary>Adds to (or, with a negative quantity, takes from) one plan's holding.</summary>
    public void Change(PlanId plan, StorageId storage, ItemId item, long quantity)
    {
        var key = (plan.Value, storage.Value, item.Value);
        var next = _held.GetValueOrDefault(key) + quantity;
        if (next < 0)
        {
            throw new InvalidOperationException(
                $"Plan {plan} would hold {next} of {item} at {storage}; a holding never goes below zero.");
        }

        if (next == 0)
        {
            _held.Remove(key);
        }
        else
        {
            _held[key] = next;
        }
    }

    /// <summary>Drops every holding of one plan, returning what it held, in ledger order.</summary>
    public IReadOnlyList<(StorageId Storage, ItemId Item, long Held)> Release(PlanId plan)
    {
        var released = new List<(StorageId, ItemId, long)>();
        foreach (var entry in _held.Where(e => e.Key.Plan == plan.Value).ToList())
        {
            released.Add((new StorageId(entry.Key.Storage), new ItemId(entry.Key.Item), entry.Value));
            _held.Remove(entry.Key);
        }

        return released;
    }
}

/// <summary>
/// The claim arithmetic of D3, Decision 5, in one place, read by the engine and by the save's
/// validation alike so the two can never disagree about what a plan needs.
/// <code>
/// need(P, S, X)        = P's unstarted runs at a facility whose buffer is S × input X
///                      + P's transfers of X from S: requested − loaded
/// inbound(P, S, X)     = P's runs depositing X into S: runs not yet deposited × output
///                      + P's transfers of X to S: requested − delivered
/// outstanding(P, S, X) = max(0, need − held − inbound)
/// </code>
/// A standing order has no finite need and claims nothing; a task with no plan claims nothing.
/// Counting the plan's own inbound work is what stops a plan holding a second copy of material its
/// own haul is already bringing.
/// </summary>
public static class ClaimMath
{
    public static long Need(ContentCatalog catalog, WorldState state, CommittedPlan plan, StorageId storage, ItemId item)
    {
        var need = 0L;
        foreach (var task in LiveTasks(state, plan))
        {
            switch (task.Script.Action)
            {
                case Produce { Runs: { } runs } produce when BufferOf(state, task) == storage:
                    var unstarted = runs - task.CompletedRuns - (task.RunActive ? 1 : 0);
                    foreach (var input in catalog.Schematics.Get(produce.Schematic).Inputs)
                    {
                        if (input.Item == item)
                        {
                            need += input.Quantity * Math.Max(0, unstarted);
                        }
                    }

                    break;

                case Transfer { Quantity: { } requested } transfer
                    when transfer.From == storage && transfer.Item == item:
                    need += Math.Max(0, requested - task.LoadedQuantity);
                    break;
            }
        }

        return need;
    }

    public static long Inbound(ContentCatalog catalog, WorldState state, CommittedPlan plan, StorageId storage, ItemId item)
    {
        var inbound = 0L;
        foreach (var task in LiveTasks(state, plan))
        {
            switch (task.Script.Action)
            {
                case Produce { Runs: { } runs } produce when BufferOf(state, task) == storage:
                    var output = catalog.Schematics.Get(produce.Schematic).Output;
                    if (output.Item == item)
                    {
                        inbound += output.Quantity * Math.Max(0, runs - task.CompletedRuns);
                    }

                    break;

                case Transfer { Quantity: { } requested } transfer
                    when transfer.To == storage && transfer.Item == item:
                    inbound += Math.Max(0, requested - task.MovedQuantity);
                    break;
            }
        }

        return inbound;
    }

    /// <summary>
    /// Every (storage, item) a plan withdraws from, in the order its tasks name them. These are the
    /// only places it can ever claim.
    /// </summary>
    public static IReadOnlyList<(StorageId Storage, ItemId Item)> Withdrawals(
        ContentCatalog catalog, WorldState state, CommittedPlan plan)
    {
        var keys = new List<(StorageId, ItemId)>();
        foreach (var task in LiveTasks(state, plan))
        {
            switch (task.Script.Action)
            {
                case Produce { Runs: not null } produce when BufferOf(state, task) is { } buffer:
                    foreach (var input in catalog.Schematics.Get(produce.Schematic).Inputs)
                    {
                        if (!keys.Contains((buffer, input.Item)))
                        {
                            keys.Add((buffer, input.Item));
                        }
                    }

                    break;

                case Transfer { Quantity: not null } transfer:
                    if (!keys.Contains((transfer.From, transfer.Item)))
                    {
                        keys.Add((transfer.From, transfer.Item));
                    }

                    break;
            }
        }

        return keys;
    }

    private static IEnumerable<TaskInstance> LiveTasks(WorldState state, CommittedPlan plan)
    {
        foreach (var id in plan.SpawnedTasks)
        {
            if (state.Tasks.Task(id) is { IsFinished: false } task)
            {
                yield return task;
            }
        }
    }

    private static StorageId? BufferOf(WorldState state, TaskInstance task)
    {
        foreach (var facility in state.Vessel.Facilities)
        {
            if (facility.Id == task.ExecutorId)
            {
                return facility.LocalStorage;
            }
        }

        return null;
    }
}
