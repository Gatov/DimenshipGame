using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The default centre view: resource tiles and a facility list. Deliberately built from labels
/// and bars — the expensive graph view is a separate spec.
/// </summary>
public sealed partial class OverviewFocus : PanelBase
{
    private readonly Dictionary<ItemId, ResourceTile> _tiles = new();

    private HBoxContainer _tileRow = null!;
    private VBoxContainer _facilityList = null!;
    private ResourceTile _energyTile = null!;

    public override PanelId Id => ShellRoot.OverviewId;

    public override string Title => "Overview";

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXl);
        AddChild(column);

        _tileRow = new HBoxContainer();
        _tileRow.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        column.AddChild(_tileRow);

        _energyTile = new ResourceTile("ENERGY", ShellPalette.StateWarn);
        _tileRow.AddChild(_energyTile);

        var heading = new Label { Text = "FACILITIES" };
        heading.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        heading.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(heading);

        _facilityList = new VBoxContainer();
        _facilityList.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(_facilityList);
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        foreach (var stock in snapshot.Resources)
        {
            if (!_tiles.TryGetValue(stock.Id, out var tile))
            {
                tile = new ResourceTile(stock.Id.Value.ToUpperInvariant(), ShellPalette.StateOk);
                _tiles[stock.Id] = tile;
                // Inserted before the energy tile so energy always reads last.
                _tileRow.AddChild(tile);
                _tileRow.MoveChild(tile, _tileRow.GetChildCount() - 2);
            }

            tile.Update(
                Units.Format(stock.Amount),
                $"/ {Units.Format(stock.Capacity)}",
                stock.Capacity == 0 ? 0f : (float)((double)stock.Amount / stock.Capacity),
                $"net {(stock.NetRatePerTick >= 0 ? "+" : "")}{Units.Format(stock.NetRatePerTick)}/s",
                stock.NetRatePerTick > 0 ? ShellPalette.StateOk : ShellPalette.TextDim);
        }

        var energy = snapshot.Energy;
        _energyTile.Update(
            Units.Format(energy.Draw),
            $"/ {Units.Format(energy.Capacity)} MW",
            energy.Capacity == 0 ? 0f : (float)((double)energy.Draw / energy.Capacity),
            $"reserve {Units.Format(energy.Reserve)} MW",
            energy.Reserve == 0 ? ShellPalette.StateFault : ShellPalette.StateWarn);

        SyncFacilities(snapshot.Facilities);
    }

    private void SyncFacilities(IReadOnlyList<FacilityState> facilities)
    {
        while (_facilityList.GetChildCount() < facilities.Count)
        {
            _facilityList.AddChild(new FacilityRow());
        }

        while (_facilityList.GetChildCount() > facilities.Count)
        {
            var extra = _facilityList.GetChild(_facilityList.GetChildCount() - 1);
            _facilityList.RemoveChild(extra);
            extra.QueueFree();
        }

        for (var i = 0; i < facilities.Count; i++)
        {
            ((FacilityRow)_facilityList.GetChild(i)).Update(facilities[i]);
        }
    }

    /// <summary>One big number with a fill bar and a rate line.</summary>
    private sealed partial class ResourceTile : PanelContainer
    {
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
                CustomMinimumSize = new Vector2(0, 6),
            };
            _bar.AddThemeStyleboxOverride("fill", ShellTheme.Surface(_accent));
            _bar.AddThemeStyleboxOverride("background", ShellTheme.Surface(ShellPalette.BgBase));
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

            // Text already dedupes internally; the colour override does not, so skip it when
            // the colour has not actually changed rather than re-applying it every snapshot.
            if (_lastRateColor != rateColor)
            {
                _lastRateColor = rateColor;
                _rate.AddThemeColorOverride("font_color", rateColor);
            }
        }
    }

    /// <summary>One facility, its power draw, and why it is or is not running.</summary>
    private sealed partial class FacilityRow : PanelContainer
    {
        private Label _name = null!;
        private Label _power = null!;
        private Label _status = null!;
        private Color? _lastStatusColor;

        public override void _Ready()
        {
            var row = new HBoxContainer();
            AddChild(row);

            _name = new Label();
            _name.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            row.AddChild(_name);

            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            _power = new Label();
            _power.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            row.AddChild(_power);

            _status = new Label();
            row.AddChild(_status);
        }

        public void Update(FacilityState facility)
        {
            _name.Text = facility.Id.Value.ToUpperInvariant();
            _power.Text = $"{Units.Format(facility.PowerDraw)} MW";

            // Colour is never the only carrier: the code is spelled out beside it.
            (_status.Text, var color) = facility.Status switch
            {
                FacilityStatus.Running => ("\u25c9 RUNNING", ShellPalette.StateOk),
                FacilityStatus.Blocked => ($"\u25c9 BLOCKED — {Describe(facility.BlockReason)}", ShellPalette.StateFault),
                _ => ("\u25c9 IDLE", ShellPalette.TextDim),
            };

            // Skip the override entirely when the status colour has not changed since the last
            // snapshot: Text already dedupes internally, this is the write that does not.
            if (_lastStatusColor != color)
            {
                _lastStatusColor = color;
                _status.AddThemeColorOverride("font_color", color);
            }
        }

        private static string Describe(EventCode? code) => code switch
        {
            EventCode.BlockMissingInput => "MISSING_INPUT",
            EventCode.BlockPowerCap => "POWER_CAP",
            EventCode.StockFull => "OUTPUT_FULL",
            _ => "UNKNOWN",
        };
    }
}
