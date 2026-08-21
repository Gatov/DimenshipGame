using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// What building the template would cost, against what the vessel actually holds.
/// <para>
/// This is the honest half of the mock. The frames, the parts and their prices are invented, but
/// the held column comes from <see cref="WorldSnapshot.Resources"/> and moves as the vessel
/// produces and spends. An affordability readout against a made-up stock would be the one part of
/// a concept mock a reviewer could not trust, and it would cost nothing to be wrong.
/// </para>
/// <para>
/// Nothing is reserved, queued or spent: the vessel does not know this template exists.
/// </para>
/// </summary>
public sealed partial class CostBox : VBoxContainer
{
    private readonly Dictionary<string, long> _held = new();

    private VBoxContainer _rows = null!;
    private IReadOnlyList<ItemCost> _cost = new List<ItemCost>();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(BoxSection.Create("Build Cost", out _rows));
        Rebuild();
    }

    public void Refresh(IReadOnlyList<ItemCost> cost)
    {
        _cost = cost;

        // Told before it is in the tree when the view builds its first template: _Ready does the
        // first draw, and IsNodeReady is what says which of the two happened first.
        if (IsNodeReady())
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Takes the vessel's stock off the snapshot. Only the amounts are kept: capacity and rate
    /// belong to the resource strip, and a cost line answering "can this be paid for now" has no
    /// use for either.
    /// </summary>
    public void OnSnapshot(WorldSnapshot snapshot)
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

        if (changed && IsNodeReady())
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        foreach (var child in _rows.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var line in _cost)
        {
            _rows.AddChild(Row(line));
        }
    }

    private Control Row(ItemCost line)
    {
        var held = _held.TryGetValue(line.ItemId, out var amount) ? amount : 0L;
        var missing = line.Amount - held;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        // The item's own identifier is both the icon's filename and the caption, as the resource
        // strip does it: one value, so the glyph and the word cannot disagree.
        row.AddChild(new IconSlot("item", line.ItemId, IconSlot.RowSize, ShellPalette.TextPrimary));

        var name = new Label
        {
            Text = line.ItemId.ToUpperInvariant(),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(name);

        var required = new Label { Text = Units.Format(line.Amount) };
        required.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        required.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(required);

        var stock = new Label { Text = $"/ {Units.Format(held)} HELD" };
        stock.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        stock.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(stock);

        if (missing <= 0)
        {
            return row;
        }

        var shortfall = new Label { Text = $"SHORT {Units.Format(missing)}" };
        shortfall.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
        shortfall.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(shortfall);

        return row;
    }
}
