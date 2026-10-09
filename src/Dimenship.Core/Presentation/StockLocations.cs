using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>The three kinds of place an item can be aboard.</summary>
public enum StockPlace
{
    /// <summary>In a storage: Resource Storage, a facility buffer or a Launch Pad hold.</summary>
    Storage,

    /// <summary>Cargo on a transport line's belt.</summary>
    Belt,

    /// <summary>The expected output of a run in progress, not yet deposited.</summary>
    Run,
}

/// <summary>
/// One place an item is, and how much of it. <paramref name="Storage"/> is set for a storage,
/// <paramref name="Executor"/> for a belt or a run. <paramref name="Held"/> is the part held for
/// committed plans (K6b), and is only ever non-zero in a storage: claims are on stock that is put
/// down.
/// </summary>
public sealed record StockAt(StockPlace Place, StorageId? Storage, ExecutorId? Executor, long Amount, long Held);

/// <summary>
/// What is where for one item (K5a): every storage holding it, every belt carrying it and every
/// run in progress that will make it, in that order and each in world order. Places holding none
/// are left out.
/// <para>
/// A projection over the snapshot, thrown away, like <see cref="WaitCause"/> and
/// <see cref="ConstructionProgress"/>. The scheduling plan put this on <c>IWorldView</c>, and it is
/// deliberately not there: the planner's supply is the main hold's free stock and nothing else, by
/// the project owner's decision, and a view offering it every buffer and belt would be an invitation
/// to net against stock it must not count. The reader this is for is the inspector (U2).
/// </para>
/// </summary>
public static class StockLocations
{
    public static IReadOnlyList<StockAt> For(WorldSnapshot snapshot, ItemId item)
    {
        var places = new List<StockAt>();

        foreach (var storage in snapshot.Storages)
        {
            foreach (var stock in storage.Items)
            {
                if (stock.Id == item && stock.Amount > 0)
                {
                    places.Add(new StockAt(StockPlace.Storage, storage.Id, null, stock.Amount, stock.Held));
                }
            }
        }

        foreach (var line in snapshot.Transports)
        {
            foreach (var cargo in line.Cargo)
            {
                if (cargo.Id == item && cargo.Amount > 0)
                {
                    places.Add(new StockAt(StockPlace.Belt, null, line.Id, cargo.Amount, 0));
                }
            }
        }

        foreach (var executor in snapshot.Executors)
        {
            if (executor.RunOutput is { } output && output.Item == item)
            {
                places.Add(new StockAt(StockPlace.Run, null, executor.Id, output.Quantity, 0));
            }
        }

        return places;
    }
}
