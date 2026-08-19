using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>One storage: how full it is, and the first few things in it.</summary>
public sealed partial class StorageCard : NodeCard
{
    /// <summary>A card is a glance, not a manifest. The inspector lists every row.</summary>
    private const int VisibleItems = 3;

    private readonly StorageId _id;
    private readonly List<IconRow> _items = new(VisibleItems);

    private CardMeter _fill = null!;

    public StorageCard(StorageId id, string label, string badge)
        : base(new GraphSelection(GraphNodeKind.Storage, id.Value), label, badge, "storage") =>
        _id = id;

    protected override void BuildBody(VBoxContainer column)
    {
        _fill = Meter(column, ShellPalette.Accent);

        for (var i = 0; i < VisibleItems; i++)
        {
            // The icon is the item's, so the row is readable as a glance before the name is read
            // at all — which is the only thing three rows on a card can offer over the inspector.
            _items.Add(Row(column, "item", ShellPalette.TextDim));
        }
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var storage = snapshot.Storages.FirstOrDefault(s => s.Id == _id);
        if (storage is null)
        {
            Status("STATUS", "ABSENT", ShellPalette.StateFault);
            return;
        }

        Status(
            "STORED",
            $"{Units.Format(storage.TotalAmount)} / {Units.Format(storage.TotalCapacity)}",
            storage.TotalAmount > 0 ? ShellPalette.TextTitle : ShellPalette.TextDim);

        // A storage with no capacity at all renders empty rather than dividing by zero.
        _fill.Set(Fill(storage.TotalAmount, storage.TotalCapacity));

        // Items come in world item order and that order is stable, so row i is always the same
        // item and a row never changes what it is describing between snapshots.
        for (var i = 0; i < _items.Count; i++)
        {
            if (i >= storage.Items.Count)
            {
                _items[i].Set(string.Empty, string.Empty);
                continue;
            }

            var item = storage.Items[i];
            _items[i].Set(
                item.Id.Value,
                $"{item.Id.Value.ToUpperInvariant()}  {Units.Format(item.Amount)}");
        }
    }
}
