using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>
    /// How much of an item a storage currently holds. Read from the instance's own stock list
    /// rather than from an index beside it: the state is authoritative, and a cache of it would be
    /// a second answer that a load could disagree with.
    /// </summary>
    public long Available(StorageId storage, ItemId item)
    {
        if (!_storagesById.TryGetValue(storage, out var instance))
        {
            return 0;
        }

        foreach (var stored in instance.Stock)
        {
            if (stored.Item == item)
            {
                return stored.Amount;
            }
        }

        return 0;
    }

    /// <summary>
    /// How much more of an item a storage could accept, which is its whole free volume expressed
    /// in that item's units. Two items asked in turn are each told about the same free volume;
    /// only one of them can take it.
    /// <para>
    /// The item's own ceiling is not a second check, because it cannot be passed: an amount above
    /// <see cref="CapacityOf"/> would occupy more than the entire storage. One rule, so there is
    /// no pair of rules to disagree.
    /// </para>
    /// </summary>
    public long Room(StorageId storage, ItemId item)
    {
        // A workpiece has no room where it is not accepted (K3). Enqueue already refuses a transfer
        // there; answering 0 keeps the one-answer rule true for a caller that forgets to ask.
        if (!_storagesById.TryGetValue(storage, out var instance) || !_items.TryGetValue(item, out var known)
            || !Acceptance.Accepts(storage, item))
        {
            return 0;
        }

        return RoomIn(instance, known, OccupiedVolume(instance));
    }

    /// <summary>
    /// How much of an item may be <i>delivered</i> into a storage, which is its free volume less
    /// the room its own facilities need for the output of the run they are set up for.
    /// <para>
    /// Transport asks this and production does not, because production is what the reservation is
    /// being held for. Without it a standing feed fills a buffer to the brim and the facility it
    /// feeds can never place its output: the shipped vessel deadlocked four operational hours in,
    /// permanently, because Matter Mix is less bulky than the Basic Metals it separates into, so
    /// consuming a run's inputs freed less volume than the run's output needed.
    /// </para>
    /// </summary>
    public long RoomForDelivery(StorageId storage, ItemId item)
    {
        if (!_storagesById.TryGetValue(storage, out var instance) || !_items.TryGetValue(item, out var known)
            || !Acceptance.Accepts(storage, item))
        {
            return 0;
        }

        return RoomIn(instance, known, OccupiedVolume(instance) + ReservedVolume(instance));
    }

    /// <summary>
    /// The volume a storage is holding back for the facilities that work out of it: one run's
    /// output each, at the schematic each is set up for.
    /// <para>
    /// A facility that has never been configured falls back to the schematic of the first task in
    /// its queue, so a fresh campaign reserves from the first tick rather than from whenever the
    /// first run happens to start. A facility with neither reserves nothing, which is right: it
    /// has no output to place.
    /// </para>
    /// <para>
    /// A whole run's output rather than the amount by which it exceeds the run's inputs. The
    /// stronger number is what makes the deadlock impossible instead of merely unlikely: a buffer
    /// filled to the reservation still has room for the output <i>before</i> its inputs are
    /// consumed, so no ordering of deliveries and runs can trap it.
    /// </para>
    /// </summary>
    private long ReservedVolume(StorageInstance storage)
    {
        if (!_facilitiesByStorage.TryGetValue(storage.Id, out var facilities))
        {
            return 0;
        }

        var reserved = 0L;

        foreach (var facility in facilities)
        {
            if (!facility.Built)
            {
                continue;
            }

            if (OutputOf(facility) is not { } output || !_items.TryGetValue(output.Item, out var known))
            {
                continue;
            }

            reserved += VolumeOf(storage, known, output.Quantity);
        }

        return reserved;
    }

    /// <summary>What one run of a facility's loaded schematic would produce, if it has one.</summary>
    private ItemAmount? OutputOf(FacilityInstance facility)
    {
        if (facility.Configured is { } configured)
        {
            return Catalog.Schematics.Get(configured).Output;
        }

        foreach (var task in Queued(facility))
        {
            if (!task.IsFinished)
            {
                return Catalog.Schematics.Get(task.Produce.Schematic).Output;
            }
        }

        return null;
    }

    /// <summary>
    /// How full a storage is, <c>1000</c> being full. The one reading the shell shows and the one
    /// the engine enforces room against, so a bar on a card cannot claim a hold has room the
    /// transport line feeding it disagrees about.
    /// </summary>
    public long FillPermille(StorageId storage) =>
        _storagesById.TryGetValue(storage, out var instance)
            ? OccupiedVolume(instance) * StorageArchetype.FullHold / StorageArchetype.FullVolume
            : 0;

    /// <summary>
    /// How much of its one shared volume a storage is using, in
    /// <see cref="StorageArchetype.FullVolume"/>ths. Summed over what the storage actually holds
    /// rather than over the catalog: an item it has never held contributes nothing, and the stock
    /// list is in first-deposit order, which is stable.
    /// </summary>
    private long OccupiedVolume(StorageInstance storage)
    {
        var occupied = 0L;

        foreach (var stored in storage.Stock)
        {
            if (stored.Amount > 0 && _items.TryGetValue(stored.Item, out var known))
            {
                occupied += VolumeOf(storage, known, stored.Amount);
            }
        }

        return occupied;
    }

    /// <summary>
    /// The volume an amount of one item occupies in one storage. Floored, and
    /// <see cref="RoomIn"/> floors in the same direction, so rounding costs the vessel room rather
    /// than inventing it — which is what keeps a storage from ever coming out over full.
    /// </summary>
    private long VolumeOf(StorageInstance storage, ItemDefinition item, long amount)
    {
        var capacity = CapacityOf(storage, item);
        return capacity <= 0 ? 0 : amount * StorageArchetype.FullVolume / capacity;
    }

    /// <summary>
    /// A free volume, in units of one item. An item the storage has no capacity for gets no room
    /// at all rather than a division: a storage that cannot hold a thing has nowhere to put it.
    /// </summary>
    private long RoomIn(StorageInstance storage, ItemDefinition item, long occupied)
    {
        var capacity = CapacityOf(storage, item);
        if (capacity <= 0)
        {
            return 0;
        }

        var free = StorageArchetype.FullVolume - occupied;
        return free <= 0 ? 0 : free * capacity / StorageArchetype.FullVolume;
    }

    private void Deposit(StorageId storage, ItemId item, long quantity) =>
        Set(storage, item, Available(storage, item) + quantity);

    private void Withdraw(StorageId storage, ItemId item, long quantity) =>
        Set(storage, item, Available(storage, item) - quantity);

    /// <summary>
    /// Writes a storage's holding of one item. Appended in first-deposit order and updated in
    /// place afterwards, so a storage's stock list has a stable order and no entry for an item it
    /// has never held.
    /// </summary>
    private void Set(StorageId storage, ItemId item, long amount)
    {
        if (!_storagesById.TryGetValue(storage, out var instance))
        {
            return;
        }

        for (var i = 0; i < instance.Stock.Count; i++)
        {
            if (instance.Stock[i].Item == item)
            {
                instance.Stock[i] = instance.Stock[i] with { Amount = amount };
                return;
            }
        }

        instance.Stock.Add(new StoredItem(item, amount));
    }

    private long CapacityOf(StorageInstance storage, ItemDefinition item) =>
        item.HoldCapacity * Archetype(storage).CapacityPermille / StorageArchetype.FullHold;

    /// <summary>
    /// Room for a schematic's output in a facility's own buffer, measured as the run itself would
    /// leave it: inputs withdrawn first, output deposited second. The inputs come off the
    /// occupancy rather than out of the storage, so a run that turns out not to fit has changed
    /// nothing.
    /// </summary>
    private long RoomAfterConsuming(StorageInstance storage, SchematicDefinition schematic)
    {
        var occupied = OccupiedVolume(storage);

        foreach (var input in schematic.Inputs)
        {
            if (_items.TryGetValue(input.Item, out var known))
            {
                occupied -= VolumeOf(storage, known, input.Quantity);
            }
        }

        return _items.TryGetValue(schematic.Output.Item, out var output)
            ? RoomIn(storage, output, occupied)
            : 0;
    }
}
