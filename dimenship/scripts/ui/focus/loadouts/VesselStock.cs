using System.Collections.Generic;
using Dimenship.Core.Simulation;

namespace Dimenship.Ui;

/// <summary>
/// The vessel's material stock, taken off each snapshot: the one live reading in the loadout mock.
/// Held once and shared by the strip's build cost and the details drawer's cost box, so the two
/// cannot disagree about what the vessel holds — two private copies of this dictionary is how they
/// would.
/// <para>
/// Only amounts are kept. Capacity and rate belong to the resource strip; a cost line asking "can
/// this be paid for now" has no use for either. Nothing is reserved or spent: the vessel does not
/// know any template exists.
/// </para>
/// </summary>
public sealed class VesselStock
{
    private readonly Dictionary<string, long> _held = new();

    /// <summary>Takes the snapshot's amounts. True when any changed, so the caller redraws only then.</summary>
    public bool Update(WorldSnapshot snapshot)
    {
        var changed = false;

        foreach (var stock in snapshot.Resources)
        {
            if (!_held.TryGetValue(stock.Id.Value, out var amount) || amount != stock.Amount)
            {
                _held[stock.Id.Value] = stock.Amount;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Milli-units held of an item. Zero for an item no snapshot has mentioned yet.</summary>
    public long Held(string itemId) => _held.TryGetValue(itemId, out var amount) ? amount : 0L;
}
