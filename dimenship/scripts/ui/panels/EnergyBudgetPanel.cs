using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Where the vessel's power goes, and how often it runs out.</summary>
public sealed partial class EnergyBudgetPanel : PanelBase
{
    private VBoxContainer _column = null!;
    private Label _capacity = null!;
    private Label _draw = null!;
    private ProgressBar _drawBar = null!;
    private VBoxContainer _consumers = null!;
    private Label _reserve = null!;
    private Label _capHits = null!;

    public override PanelId Id => ShellRoot.EnergyBudgetId;

    public override string Title => "Energy Budget";

    public override void _Ready()
    {
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(_column);

        _capacity = AddRow("CAPACITY", ShellPalette.TextPrimary);
        _draw = AddRow("DRAW", ShellPalette.StateWarn);

        _drawBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
        };
        _drawBar.AddThemeStyleboxOverride("fill", ShellTheme.Surface(ShellPalette.StateWarn));
        _drawBar.AddThemeStyleboxOverride("background", ShellTheme.Surface(ShellPalette.BgBase));
        _column.AddChild(_drawBar);

        var heading = new Label { Text = "BY CONSUMER" };
        heading.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        heading.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _column.AddChild(heading);

        _consumers = new VBoxContainer();
        _consumers.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        _column.AddChild(_consumers);

        _reserve = AddRow("RESERVE", ShellPalette.StateWarn);
        _capHits = AddRow("CAP HITS", ShellPalette.StateFault);
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        var energy = snapshot.Energy;

        _capacity.Text = $"{Units.Format(energy.Capacity)} MW";
        _draw.Text = $"{Units.Format(energy.Draw)} MW";
        _drawBar.Value = energy.Capacity == 0 ? 0 : Mathf.Clamp((float)((double)energy.Draw / energy.Capacity), 0f, 1f);
        _reserve.Text = $"{Units.Format(energy.Reserve)} MW";
        _capHits.Text = energy.CapHits.ToString();

        SyncConsumers(snapshot.Facilities, energy.Capacity);
    }

    private void SyncConsumers(IReadOnlyList<FacilityState> facilities, long capacity)
    {
        while (_consumers.GetChildCount() < facilities.Count)
        {
            _consumers.AddChild(new ConsumerRow());
        }

        while (_consumers.GetChildCount() > facilities.Count)
        {
            var extra = _consumers.GetChild(_consumers.GetChildCount() - 1);
            _consumers.RemoveChild(extra);
            extra.QueueFree();
        }

        for (var i = 0; i < facilities.Count; i++)
        {
            ((ConsumerRow)_consumers.GetChild(i)).Update(facilities[i], capacity);
        }
    }

    private Label AddRow(string name, Color valueColor)
    {
        var row = new HBoxContainer();
        _column.AddChild(row);

        var label = new Label { Text = name };
        label.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(label);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var value = new Label();
        value.AddThemeColorOverride("font_color", valueColor);
        row.AddChild(value);

        return value;
    }

    private sealed partial class ConsumerRow : VBoxContainer
    {
        private Label _name = null!;
        private Label _value = null!;
        private ProgressBar _bar = null!;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 0);

            var row = new HBoxContainer();
            AddChild(row);

            _name = new Label();
            _name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            row.AddChild(_name);

            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            _value = new Label();
            _value.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            row.AddChild(_value);

            _bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 4),
            };
            _bar.AddThemeStyleboxOverride("background", ShellTheme.Surface(ShellPalette.BgBase));
            AddChild(_bar);
        }

        public void Update(FacilityState facility, long capacity)
        {
            var drawing = facility.PowerDraw > 0;

            _name.Text = facility.Id.Value;
            _value.Text = Units.Format(facility.PowerDraw);
            _value.AddThemeColorOverride(
                "font_color", drawing ? ShellPalette.TextPrimary : ShellPalette.TextFaint);
            _bar.Value = capacity == 0 ? 0 : Mathf.Clamp((float)((double)facility.PowerDraw / capacity), 0f, 1f);
            _bar.AddThemeStyleboxOverride(
                "fill", ShellTheme.Surface(drawing ? ShellPalette.StateOk : ShellPalette.TextFaint));
        }
    }
}
