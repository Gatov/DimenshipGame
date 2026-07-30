using System.Linq;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>Bottom strip: vessel state, transport controls, sim clock, alert count.</summary>
public sealed partial class StatusBar : HBoxContainer
{
    private readonly SimulationDriver _driver;

    private Label _state = null!;
    private Button _playPause = null!;
    private Button _step = null!;
    private Label _clock = null!;
    private Label _tick = null!;
    private Label _alerts = null!;
    private readonly System.Collections.Generic.List<Button> _speedButtons = new();

    public StatusBar(SimulationDriver driver)
    {
        _driver = driver;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 24);
        AddThemeConstantOverride("separation", 12);

        _state = AddLabel("\u25c9 NOMINAL");

        _playPause = AddButton("\u23f8", _driver.TogglePause);
        _step = AddButton("\u23ed", _driver.Step);

        foreach (var speed in SimulationDriver.Speeds.Skip(1))
        {
            var captured = speed;
            _speedButtons.Add(AddButton($"\u00d7{speed}", () => _driver.SetSpeed(captured)));
        }

        _clock = AddLabel(Units.FormatSimTime(0));
        _tick = AddLabel("tick 0");
        AddLabel("STRATUM N-2 (fixed)");

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(spacer);

        _alerts = AddLabel(string.Empty);

        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    public void Refresh()
    {
        var snapshot = _driver.Snapshot;

        _clock.Text = Units.FormatSimTime(snapshot.Tick);
        _tick.Text = $"tick {snapshot.Tick}";

        _playPause.Text = _driver.IsPaused ? "\u25b6" : "\u23f8";
        _step.Disabled = !_driver.IsPaused;

        for (var i = 0; i < _speedButtons.Count; i++)
        {
            // Speeds[0] is pause, so the button at index i maps to Speeds[i + 1].
            _speedButtons[i].Disabled = _driver.Speed == SimulationDriver.Speeds[i + 1];
        }

        // Derived, never stored, and counting transport as well as production: a vessel whose
        // haulage has stalled is exactly as stopped as one whose furnaces have.
        var blocked =
            snapshot.Executors.Count(e => e.Status == ExecutorStatus.AllQueuedTasksBlocked)
            + snapshot.Transports.Count(t => t.Status == ExecutorStatus.AllQueuedTasksBlocked);
        _alerts.Text = blocked == 0 ? string.Empty : $"\u26a0 {blocked} alert{(blocked == 1 ? "" : "s")}";
        _alerts.AddThemeColorOverride("font_color", ShellPalette.StateFault);

        if (_driver.FaultMessage is { } fault)
        {
            _state.Text = fault;
            _state.AddThemeColorOverride("font_color", ShellPalette.StateFault);
        }
        else
        {
            _state.Text = _driver.IsPaused ? "\u25c9 PAUSED" : "\u25c9 NOMINAL";
            _state.AddThemeColorOverride(
                "font_color", _driver.IsPaused ? ShellPalette.StateWarn : ShellPalette.StateOk);
        }
    }

    private Label AddLabel(string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        AddChild(label);
        return label;
    }

    private Button AddButton(string text, System.Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += onPressed;
        AddChild(button);
        return button;
    }
}
