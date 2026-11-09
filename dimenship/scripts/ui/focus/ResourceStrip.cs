using System.Collections.Generic;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The materials the vessel plans with, as standing tiles above the graph, and beside them the
/// made goods Resource Storage holds right now (<see cref="ItemTiers"/>).
/// <para>
/// It exists because the graph's storage nodes carry amounts but not rates, and the overview this
/// view replaced was the only surface showing <see cref="ResourceStock.NetRatePerTick"/>. Whether
/// the vessel is gaining or losing ore is a different question from where the ore is, and
/// replacing a view must not quietly drop the answer to it.
/// </para>
/// <para>
/// It once drew a tile for every catalog item: thirteen tiles of three readings each, most of them
/// zero, before the player had ordered anything. Only the base materials are decided about, so
/// only they keep a tile. A made good is the consequence of an order, and is shown as a small chip
/// only while the hold has some, which is when it is stock a new order could use. A workpiece is
/// never in the hold and is read on the buffer or belt where it is; a construction unit is read on
/// the slot it builds. The inspector still answers for any of them.
/// </para>
/// <para>
/// A tile's width is fixed and the tiles do not share the line's spare width, so a chip appearing
/// or leaving moves no tile, and the strip's height does not change mid-run and push the graph
/// down. A chip counts what the hold physically holds, held for a plan or not, as the storage
/// card does.
/// </para>
/// </summary>
public sealed partial class ResourceStrip : HBoxContainer
{
    /// <summary>
    /// Wide enough for the widest base reading at <see cref="ShellPalette.FontNumeric"/>, and the
    /// same for every tile so the row reads as one instrument.
    /// </summary>
    private const int TileWidth = 176;

    private readonly Dictionary<ItemId, ResourceTile> _tiles = new();
    private readonly Dictionary<ItemId, StockChip> _chips = new();
    private Control _made = null!;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        MouseFilter = MouseFilterEnum.Ignore;

        var catalog = ShellContent.Catalog;
        var labels = new Dictionary<ItemId, string>();
        foreach (var item in catalog.Items)
        {
            labels[item.Id] = item.Label;
        }

        var tiles = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tiles.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        foreach (var item in ItemTiers.Base(catalog))
        {
            var tile = new ResourceTile(item.Value, labels[item], labels[item])
            {
                CustomMinimumSize = new Vector2(TileWidth, 0),
            };
            _tiles[item] = tile;
            tiles.AddChild(tile);
        }

        AddChild(Group("MATERIALS", tiles, expand: false));

        // A flow container, so a full set of chips on a narrow window takes a second line inside
        // the height the tiles already give the strip rather than running off its edge.
        var chips = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        chips.AddThemeConstantOverride("h_separation", ShellPalette.SpaceSm);
        chips.AddThemeConstantOverride("v_separation", ShellPalette.SpaceXs);
        foreach (var item in ItemTiers.MadeGoods(catalog))
        {
            var chip = new StockChip(item.Value, labels[item]) { Visible = false };
            _chips[item] = chip;
            chips.AddChild(chip);
        }

        _made = Group("IN STORAGE", chips, expand: true);
        _made.Visible = false;
        AddChild(_made);
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

        var hold = ShellContent.DefaultVessel.Hold;
        var any = false;
        foreach (var storage in snapshot.Storages)
        {
            if (storage.Id != hold)
            {
                continue;
            }

            foreach (var stock in storage.Items)
            {
                if (_chips.TryGetValue(stock.Id, out var chip))
                {
                    chip.Update(stock.Amount);
                    any |= stock.Amount > 0;
                }
            }
        }

        _made.Visible = any;
    }

    /// <summary>A caption over a row of readings.</summary>
    private static Control Group(string caption, Control content, bool expand)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        column.AddChild(ShellTheme.Text(caption, ShellTheme.Note));
        column.AddChild(content);
        return column;
    }

    /// <summary>
    /// A made good the hold has some of: its icon, name and amount on one line, and nothing else.
    /// No bar and no rate, because nobody manages this stock to a level; it is here to say that it
    /// exists. Hidden at zero.
    /// </summary>
    private sealed partial class StockChip : PanelContainer
    {
        private readonly string _item;
        private readonly string _label;
        private Label _value = null!;
        private long _amount = -1;

        public StockChip(string item, string label)
        {
            _item = item;
            _label = label;
            MouseFilter = MouseFilterEnum.Pass;
            TooltipText = label;
        }

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Chip(false));

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            AddChild(row);

            row.AddChild(new IconSlot("item", _item, IconSlot.RowSize, ShellPalette.TextDim));
            row.AddChild(ShellTheme.Text(_label.ToUpperInvariant(), ShellTheme.Caption));

            _value = new Label { ThemeTypeVariation = ShellTheme.Value };
            _value.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
            row.AddChild(_value);
        }

        public void Update(long amount)
        {
            if (amount == _amount)
            {
                return;
            }

            _amount = amount;
            Visible = amount > 0;
            _value.Text = Units.FormatCompact(amount);
        }
    }

    /// <summary>
    /// One big number, a fill bar with the capacity beside it, and a rate line.
    /// <para>
    /// Quiet at rest. A stock of nothing draws its number in <see cref="ShellPalette.TextFaint"/>,
    /// and a rate of nothing reads <c>steady</c> in the same colour, because a row of <c>0.000 / 240.000 · net +0.000/s</c> buries the
    /// tiles that hold anything. Quantities drop trailing zeros (<see cref="Units.FormatCompact"/>); a
    /// fraction keeps every digit it has.
    /// </para>
    /// </summary>
    private sealed partial class ResourceTile : PanelContainer
    {
        /// <summary>A pane-scale meter, tall enough to carry the small radius.</summary>
        private const int BarHeight = 6;

        /// <summary>The item's own identifier, which is the icon's filename.</summary>
        private readonly string _item;

        /// <summary>What the caption says, upper-cased.</summary>
        private readonly string _caption;

        private Label _value = null!;
        private Label _capacity = null!;
        private Label _rate = null!;
        private ProgressBar _bar = null!;
        private Color? _lastValueColor;
        private Color? _lastRateColor;
        private long? _lastCapacity;

        public ResourceTile(string item, string caption, string label)
        {
            _item = item;
            _caption = caption;

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

            // The caption is the one line allowed to shorten: it is a name, and its tooltip carries
            // it whole, whereas a number cut short is a lie.
            var caption = new HBoxContainer();
            caption.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            column.AddChild(caption);

            caption.AddChild(new IconSlot("item", _item, IconSlot.RowSize, ShellPalette.TextDim));

            var name = new Label
            {
                Text = _caption.ToUpperInvariant(),
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
