using System.Collections.Generic;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Vessel-wide totals and their net rate, in a row above the graph, grouped by where each item
/// sits in the chain (<see cref="ItemTiers"/>).
/// <para>
/// It exists because the graph's storage nodes carry amounts but not rates, and the overview this
/// view replaced was the only surface showing <see cref="ResourceStock.NetRatePerTick"/>. Whether
/// the vessel is gaining or losing ore is a different question from where the ore is, and
/// replacing a view must not quietly drop the answer to it.
/// </para>
/// <para>
/// A flow container rather than a row. Thirteen tiles in one row ran off the right edge of a
/// 1920-pixel window with nothing to say they had, and a readout the player cannot see is worse
/// than one that takes a second line. A group wraps whole, so a tier is never split across lines.
/// </para>
/// <para>
/// A tile's width never follows its reading: the number's slot is sized once for the widest value
/// the item's capacity allows. Without that, a stock gaining a fraction would widen its tile, and
/// past the line's width it would carry a whole group onto a second line and push the graph down
/// mid-run.
/// </para>
/// </summary>
public sealed partial class ResourceStrip : HFlowContainer
{
    private readonly Dictionary<ItemId, ResourceTile> _tiles = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("h_separation", ShellPalette.SpaceLg);
        AddThemeConstantOverride("v_separation", ShellPalette.SpaceSm);
        MouseFilter = MouseFilterEnum.Ignore;

        var catalog = ShellContent.Catalog;
        var labels = new Dictionary<ItemId, string>();
        foreach (var item in catalog.Items)
        {
            labels[item.Id] = item.Label;
        }

        foreach (var group in ItemTiers.Groups(catalog))
        {
            AddChild(Group(group, labels));
        }
    }

    public void Refresh(WorldSnapshot snapshot)
    {
        foreach (var stock in snapshot.Resources)
        {
            if (_tiles.TryGetValue(stock.Id, out var tile))
            {
                tile.Update(stock);
            }
        }
    }

    /// <summary>
    /// A tier's caption over its tiles. It expands in proportion to how many tiles it holds, so the
    /// width a line has to spare is shared per tile rather than per group, and a two-item group's
    /// tiles are not twice as wide as a six-item group's.
    /// </summary>
    private Control Group(ItemTierGroup group, IReadOnlyDictionary<ItemId, string> labels)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = group.Items.Count,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        column.AddChild(ShellTheme.Text(Caption(group.Tier), ShellTheme.Note));

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(row);

        foreach (var item in group.Items)
        {
            var tile = new ResourceTile(item.Value, labels[item]);
            _tiles[item] = tile;
            row.AddChild(tile);
        }

        return column;
    }

    private static string Caption(ItemTier tier) => tier switch
    {
        ItemTier.Raw => "RAW",
        ItemTier.Processed => "PROCESSED",
        ItemTier.Workpiece => "WORKPIECES",
        ItemTier.ConstructionUnit => "CONSTRUCTION UNITS",
        _ => tier.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// One big number, a fill bar with the capacity beside it, and a rate line.
    /// <para>
    /// Quiet at rest. A stock of nothing draws its number in <see cref="ShellPalette.TextFaint"/>,
    /// and a rate of nothing reads <c>steady</c> in the same colour, because a new campaign shows
    /// eleven empty items and a row of thirteen <c>0.000 / 240.000 · net +0.000/s</c> buried the two
    /// that hold anything. Quantities drop trailing zeros (<see cref="Units.FormatCompact"/>); a
    /// fraction keeps every digit it has.
    /// </para>
    /// </summary>
    private sealed partial class ResourceTile : PanelContainer
    {
        /// <summary>A pane-scale meter, tall enough to carry the small radius.</summary>
        private const int BarHeight = 6;

        /// <summary>The item's own identifier, which is the icon's filename.</summary>
        private readonly string _item;

        /// <summary>The catalog's label, upper-cased for the caption and whole in the tooltip.</summary>
        private readonly string _label;

        private Label _value = null!;
        private Label _capacity = null!;
        private Label _rate = null!;
        private ProgressBar _bar = null!;
        private Color? _lastValueColor;
        private Color? _lastRateColor;
        private long? _lastCapacity;

        public ResourceTile(string item, string label)
        {
            _item = item;
            _label = label;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // Pass rather than Ignore, for the tooltip: a caption ellipsised to fit the tile still
            // has to be readable whole somewhere.
            MouseFilter = MouseFilterEnum.Pass;
            TooltipText = label;
        }

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Tile());

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            AddChild(column);

            // The caption is the one line allowed to shorten: "Mission Dock Construction Unit" is
            // a name, and its tooltip carries it whole, whereas a number cut short is a lie.
            var caption = new HBoxContainer();
            caption.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            column.AddChild(caption);

            caption.AddChild(new IconSlot("item", _item, IconSlot.RowSize, ShellPalette.TextDim));

            var name = new Label
            {
                Text = _label.ToUpperInvariant(),
                VerticalAlignment = VerticalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ThemeTypeVariation = ShellTheme.Caption,
            };
            caption.AddChild(name);

            // The number is a quantity, not a state, so it takes the title tier rather than the
            // accent: the accent belongs to the bar, which is the part that reads as a level.
            _value = new Label { ThemeTypeVariation = ShellTheme.Value };
            _value.AddThemeFontSizeOverride("font_size", ShellPalette.FontNumeric);
            column.AddChild(_value);

            // The capacity sits beside the bar it is the far end of, rather than beside the number,
            // which is what lets a tile be as narrow as its number.
            var level = new HBoxContainer();
            level.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            column.AddChild(level);

            _bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, BarHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            _bar.AddThemeStyleboxOverride("fill", ShellTheme.MeterFill(ShellPalette.Accent, BarHeight));
            _bar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(BarHeight));
            level.AddChild(_bar);

            _capacity = new Label { ThemeTypeVariation = ShellTheme.Value };
            _capacity.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            _capacity.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            level.AddChild(_capacity);

            _rate = new Label { ThemeTypeVariation = ShellTheme.Value };
            _rate.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(_rate);
        }

        public void Update(ResourceStock stock)
        {
            if (_lastCapacity != stock.Capacity)
            {
                _lastCapacity = stock.Capacity;
                _capacity.Text = $"/ {Units.FormatCompact(stock.Capacity)}";
                ReserveValueWidth(stock.Capacity);
            }

            _value.Text = Units.FormatCompact(stock.Amount);
            _bar.Value = stock.Capacity == 0
                ? 0
                : Mathf.Clamp((double)stock.Amount / stock.Capacity, 0, 1);

            Recolour(_value, ref _lastValueColor,
                stock.Amount == 0 ? ShellPalette.TextFaint : ShellPalette.TextTitle);

            var rate = stock.NetRatePerTick;
            _rate.Text = rate == 0
                ? "steady"
                : $"net {(rate > 0 ? "+" : string.Empty)}{Units.FormatCompact(rate)}/s";
            Recolour(_rate, ref _lastRateColor, rate switch
            {
                > 0 => ShellPalette.StateOk,
                < 0 => ShellPalette.TextPrimary,
                _ => ShellPalette.TextFaint,
            });
        }

        /// <summary>
        /// Sizes the number's slot for the widest reading this capacity allows, every decimal
        /// included, so a tile is as wide on the tick its stock gains a fraction as on the tick
        /// before. See <see cref="ResourceStrip"/> for what an unreserved slot did to the layout.
        /// </summary>
        private void ReserveValueWidth(long capacity)
        {
            var font = _value.GetThemeFont("font");
            var widest = font.GetStringSize(
                Units.Format(capacity), HorizontalAlignment.Left, -1, ShellPalette.FontNumeric);
            _value.CustomMinimumSize = new Vector2(Mathf.Ceil(widest.X), 0);
        }

        /// <summary>Text dedupes internally; a colour override does not.</summary>
        private static void Recolour(Label label, ref Color? last, Color colour)
        {
            if (last == colour)
            {
                return;
            }

            last = colour;
            label.AddThemeColorOverride("font_color", colour);
        }
    }
}
