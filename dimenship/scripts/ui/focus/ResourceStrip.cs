using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Vessel-wide totals and their net rate, in a row above the graph.
/// <para>
/// It exists because the graph's storage nodes carry amounts but not rates, and the overview this
/// view replaced was the only surface showing <see cref="ResourceStock.NetRatePerTick"/>. Whether
/// the vessel is gaining or losing ore is a different question from where the ore is, and
/// replacing a view must not quietly drop the answer to it.
/// </para>
/// </summary>
public sealed partial class ResourceStrip : HBoxContainer
{
    private readonly Dictionary<ItemId, ResourceTile> _tiles = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        foreach (var stock in snapshot.Resources)
        {
            if (!_tiles.TryGetValue(stock.Id, out var tile))
            {
                tile = new ResourceTile(stock.Id.Value.ToUpperInvariant(), ShellPalette.StateOk);
                _tiles[stock.Id] = tile;
                AddChild(tile);
            }

            tile.Update(
                Units.Format(stock.Amount),
                $"/ {Units.Format(stock.Capacity)}",
                stock.Capacity == 0 ? 0f : (float)((double)stock.Amount / stock.Capacity),
                $"net {(stock.NetRatePerTick >= 0 ? "+" : string.Empty)}{Units.Format(stock.NetRatePerTick)}/s",
                stock.NetRatePerTick > 0 ? ShellPalette.StateOk : ShellPalette.TextDim);
        }
    }

    /// <summary>One big number with a fill bar and a rate line.</summary>
    private sealed partial class ResourceTile : PanelContainer
    {
        /// <summary>A pane-scale meter, tall enough to carry the small radius.</summary>
        private const int BarHeight = 6;

        private readonly string _label;
        private readonly Color _accent;

        private Label _value = null!;
        private Label _capacity = null!;
        private Label _rate = null!;
        private ProgressBar _bar = null!;
        private Color? _lastRateColor;

        public ResourceTile(string label, Color accent)
        {
            _label = label;
            _accent = accent;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        public override void _Ready()
        {
            var column = new VBoxContainer();
            AddChild(column);

            var name = new Label { Text = _label };
            name.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(name);

            var valueRow = new HBoxContainer();
            column.AddChild(valueRow);

            _value = new Label();
            _value.AddThemeColorOverride("font_color", _accent);
            _value.AddThemeFontSizeOverride("font_size", ShellPalette.FontNumeric);
            valueRow.AddChild(_value);

            _capacity = new Label { VerticalAlignment = VerticalAlignment.Bottom };
            _capacity.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            valueRow.AddChild(_capacity);

            _bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, BarHeight),
            };
            _bar.AddThemeStyleboxOverride("fill", ShellTheme.MeterFill(_accent, BarHeight));
            _bar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(BarHeight));
            column.AddChild(_bar);

            _rate = new Label();
            _rate.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(_rate);
        }

        public void Update(string value, string capacity, float fill, string rate, Color rateColor)
        {
            _value.Text = value;
            _capacity.Text = capacity;
            _bar.Value = Mathf.Clamp(fill, 0f, 1f);
            _rate.Text = rate;

            // Text already dedupes internally; the colour override does not.
            if (_lastRateColor != rateColor)
            {
                _lastRateColor = rateColor;
                _rate.AddThemeColorOverride("font_color", rateColor);
            }
        }
    }
}
