using Dimenship.Core.Simulation;

namespace Dimenship.Core.Content;

/// <summary>
/// Which storages accept which workpieces (D2 Decision 2, K3). A storage accepts a workpiece if and
/// only if it is the local storage of a facility whose type has a schematic in the catalog that
/// consumes or produces it. Every ordinary item is accepted everywhere, as before.
/// <para>
/// <b>Derived, never authored.</b> An accept-list per storage would be a second record of what the
/// schematics already say, and it would drift: add a schematic that treats a workpiece at a new
/// facility type, and that type's buffers accept it in the same edit. It reads the whole catalog
/// rather than the unlocked schematics, so acceptance never changes mid-campaign on an unlock. It
/// reads the facility's <i>type</i>, not its configured schematic, so a reactor set up for
/// something else does not bounce the delivery its changeover is waiting on.
/// </para>
/// <para>
/// Built from the catalog and the vessel's facilities, by the content loader to check a scenario
/// and by the engine constructor as an index. It is never saved: the engine rebuilds it from state,
/// like every other index.
/// </para>
/// </summary>
public sealed class WorkpieceAcceptance
{
    private readonly HashSet<ItemId> _workpieces = new();
    private readonly HashSet<(StorageId Storage, ItemId Item)> _accepted = new();

    /// <param name="buffers">Each facility's local storage and the facility's type.</param>
    public WorkpieceAcceptance(ContentCatalog catalog, IEnumerable<(StorageId Buffer, FacilityType Type)> buffers)
    {
        foreach (var item in catalog.Items)
        {
            if (item.Workpiece)
            {
                _workpieces.Add(item.Id);
            }
        }

        if (_workpieces.Count == 0)
        {
            return;
        }

        var worked = new Dictionary<FacilityType, HashSet<ItemId>>();
        foreach (var schematic in catalog.Schematics.All)
        {
            if (!worked.TryGetValue(schematic.RequiredFacilityType, out var items))
            {
                items = new HashSet<ItemId>();
                worked[schematic.RequiredFacilityType] = items;
            }

            if (_workpieces.Contains(schematic.Output.Item))
            {
                items.Add(schematic.Output.Item);
            }

            foreach (var input in schematic.Inputs)
            {
                if (_workpieces.Contains(input.Item))
                {
                    items.Add(input.Item);
                }
            }
        }

        foreach (var (buffer, type) in buffers)
        {
            if (worked.TryGetValue(type, out var items))
            {
                foreach (var item in items)
                {
                    _accepted.Add((buffer, item));
                }
            }
        }
    }

    public bool IsWorkpiece(ItemId item) => _workpieces.Contains(item);

    /// <summary>Whether <paramref name="item"/> may be put down in <paramref name="storage"/>.
    /// Always true for an ordinary item, which is what keeps a vessel with no workpiece
    /// byte-identical to one from before they existed.</summary>
    public bool Accepts(StorageId storage, ItemId item) =>
        !_workpieces.Contains(item) || _accepted.Contains((storage, item));

    /// <summary>The sentence a refusal gives, the same at <c>Enqueue</c>, in the draft and in a
    /// save's drift report.</summary>
    public static string Refusal(ItemId item, StorageId storage) =>
        $"'{item}' is a workpiece, and '{storage}' does not accept it: only the buffer of a facility " +
        "whose type works it does.";
}
