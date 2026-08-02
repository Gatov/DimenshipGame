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
    private readonly List<Label> _items = new(VisibleItems);

    private ProgressBar _fill = null!;

    public StorageCard(StorageId id, string label)
        : base(new GraphSelection(GraphNodeKind.Storage, id.Value), label) =>
        _id = id;

    protected override void BuildBody(VBoxContainer column)
    {
        _fill = Bar(column, ShellPalette.StateOk);

        for (var i = 0; i < VisibleItems; i++)
        {
            _items.Add(Row(column, ShellPalette.TextDim));
        }
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var storage = snapshot.Storages.FirstOrDefault(s => s.Id == _id);
        if (storage is null)
        {
            Status("ABSENT", ShellPalette.StateFault);
            return;
        }

        Status(
            $"{Units.Format(storage.TotalAmount)} / {Units.Format(storage.TotalCapacity)}",
            storage.TotalAmount > 0 ? ShellPalette.TextPrimary : ShellPalette.TextDim);

        // A storage with no capacity at all renders empty rather than dividing by zero.
        _fill.Value = Fill(storage.TotalAmount, storage.TotalCapacity);

        // Items come in world item order and that order is stable, so row i is always the same
        // item and a row never changes what it is describing between snapshots.
        for (var i = 0; i < _items.Count; i++)
        {
            if (i >= storage.Items.Count)
            {
                _items[i].Text = string.Empty;
                continue;
            }

            var item = storage.Items[i];
            _items[i].Text = $"{item.Id.Value.ToUpperInvariant()}  {Units.Format(item.Amount)}";
        }
    }
}
