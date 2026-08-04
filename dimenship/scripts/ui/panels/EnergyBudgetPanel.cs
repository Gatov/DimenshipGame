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
    private Label _starved = null!;
    private int _lastStarvedTicks = -1;

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
        _drawBar.AddThemeStyleboxOverride("fill", ShellTheme.MeterFill(ShellPalette.StateWarn, 6));
        _drawBar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(6));
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

        // Sits next to CAP HITS because the two answer different questions and the operator needs
        // both: CAP HITS is "ran flat out", STARVED is "something was refused". Neither implies
        // the other, and RESERVE reads healthy during starvation.
        _starved = AddRow("STARVED", ShellPalette.StateFault);
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        var energy = snapshot.Energy;

        _capacity.Text = $"{Units.Format(energy.Capacity)} MW";
        _draw.Text = $"{Units.Format(energy.Draw)} MW";
        _drawBar.Value = energy.Capacity == 0 ? 0 : Mathf.Clamp((float)((double)energy.Draw / energy.Capacity), 0f, 1f);
        _reserve.Text = $"{Units.Format(energy.Reserve)} MW";
        _capHits.Text = energy.CapHits.ToString();

        // Guarded the way Task 6's rows are: Label.Text dedupes internally, the colour override
        // does not. Faint while nothing has starved so a zero does not read as a live alarm.
        if (_lastStarvedTicks != energy.StarvedTicks)
        {
            _lastStarvedTicks = energy.StarvedTicks;
            _starved.Text = energy.StarvedTicks.ToString();
            _starved.AddThemeColorOverride(
                "font_color",
                energy.StarvedTicks > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint);
        }

        SyncConsumers(Consumers(snapshot), energy.Capacity);
    }

    /// <summary>
    /// Everything that draws power, in the order the engine grants it: sinks, then facilities,
    /// then transport lines. A budget that listed only some of its consumers would not add up to
    /// the draw shown above it, which is the one thing this panel exists to make true.
    /// </summary>
    private static List<Consumer> Consumers(WorldSnapshot snapshot)
    {
        var consumers = new List<Consumer>(
            snapshot.Sinks.Count + snapshot.Executors.Count + snapshot.Transports.Count);

        foreach (var sink in snapshot.Sinks)
        {
            consumers.Add(new Consumer(sink.Label, sink.PowerDraw));
        }

        foreach (var executor in snapshot.Executors)
        {
            consumers.Add(new Consumer(executor.Label, executor.PowerDraw));
        }

        foreach (var transport in snapshot.Transports)
        {
            consumers.Add(new Consumer(transport.Label, transport.PowerDraw));
        }

        return consumers;
    }

    private void SyncConsumers(IReadOnlyList<Consumer> facilities, long capacity)
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

    /// <summary>One power consumer, flattened from whichever kind of thing it is.</summary>
    private readonly record struct Consumer(string Name, long Draw);

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
        /// <summary>These bars are 4px, which is below the height a rounded fill is legible at.</summary>
        private const int BarHeight = 4;

        // Only two fill styleboxes are ever needed (drawing or idle). Hoisted to statics created
        // once rather than allocated fresh by ShellTheme.MeterFill on every Update call.
        private static readonly StyleBoxFlat DrawingFill =
            ShellTheme.MeterFill(ShellPalette.StateOk, BarHeight);

        private static readonly StyleBoxFlat IdleFill =
            ShellTheme.MeterFill(ShellPalette.TextFaint, BarHeight);

        private Label _name = null!;
        private Label _value = null!;
        private ProgressBar _bar = null!;
        private bool? _lastDrawing;

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
                CustomMinimumSize = new Vector2(0, BarHeight),
            };
            _bar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(BarHeight));
            AddChild(_bar);
        }

        public void Update(Consumer facility, long capacity)
        {
            var drawing = facility.Draw > 0;

            _name.Text = facility.Name.ToUpperInvariant();
            _value.Text = $"{Units.Format(facility.Draw)} MW";
            _bar.Value = capacity == 0 ? 0 : Mathf.Clamp((float)((double)facility.Draw / capacity), 0f, 1f);

            // The colour and stylebox both derive from the same boolean, so one comparison
            // guards both writes and skips the theme-changed churn on every unchanged snapshot.
            if (_lastDrawing != drawing)
            {
                _lastDrawing = drawing;
                _value.AddThemeColorOverride(
                    "font_color", drawing ? ShellPalette.TextPrimary : ShellPalette.TextFaint);
                _bar.AddThemeStyleboxOverride("fill", drawing ? DrawingFill : IdleFill);
            }
        }
    }
}
