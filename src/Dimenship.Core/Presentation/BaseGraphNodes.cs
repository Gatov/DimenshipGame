using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>
/// Which cell a storage is <i>drawn at</i>, which is not the same question as where it is placed.
/// <para>
/// A facility's buffer has no card of its own. It is drawn inside the card of the facility that
/// works it, so a route ending at the buffer ends at that facility — which is what the base
/// overview concept shows, storage joined straight to each factory, and what keeps a vessel of
/// nine facilities from drawing eighteen cards to say the same thing.
/// </para>
/// <para>
/// The rule lives here, beside the layout, rather than in the view, so that "every route endpoint
/// is drawn somewhere" and "no route begins and ends on the same card" are assertions in
/// <c>Core.Tests</c> rather than things noticed on screen. It names no rendering type.
/// </para>
/// </summary>
public static class BaseGraphNodes
{
    /// <summary>
    /// Every storage the graph can draw, mapped to the placement it is drawn at: its own, if the
    /// layout places it, and otherwise that of the facility whose buffer it is.
    /// <para>
    /// <paramref name="buffers"/> is the world's facilities paired with the storage each works,
    /// in declaration order. A placed storage wins over a facility claiming it, and the first
    /// facility to claim a storage wins over any later one — both are content errors the layout
    /// tests catch, and neither is worth an exception at a call site that is drawing a frame.
    /// </para>
    /// <para>
    /// A storage that is neither placed nor any placed facility's buffer is absent from the result.
    /// The view draws those in its unplaced strip; silently dropping them is exactly the failure
    /// this returns nothing for rather than guessing at.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<StorageId, NodePlacement> DrawnStorages(
        BaseGraphLayout layout, IEnumerable<(ExecutorId Facility, StorageId Buffer)> buffers)
    {
        var drawn = new Dictionary<StorageId, NodePlacement>();

        foreach (var (storage, placement) in layout.Storages)
        {
            drawn[storage] = placement;
        }

        foreach (var (facility, buffer) in buffers)
        {
            if (drawn.ContainsKey(buffer) || !layout.Producers.TryGetValue(facility, out var owner))
            {
                continue;
            }

            drawn[buffer] = owner;
        }

        return drawn;
    }
}
