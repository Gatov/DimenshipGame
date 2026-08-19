using System.Linq;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>Bottom strip: vessel state, transport controls, sim clock, alert count.</summary>
public sealed partial class StatusBar : HBoxContainer
{
    private readonly SimulationDriver _driver;

    private IconSlot _stateIcon = null!;
    private Label _state = null!;
    private Button _playPause = null!;
    private Button _step = null!;
    private Label _clock = null!;
    private Label _tick = null!;
    private IconSlot _alertIcon = null!;
    private Label _alerts = null!;
    private readonly System.Collections.Generic.List<Button> _speedButtons = new();

    /// <summary>Which face the transport button is currently wearing. <see cref="Refresh"/> runs
    /// every frame, and swapping a texture it is already showing is work for nothing.</summary>
    private bool? _offeringPlay;

    public StatusBar(SimulationDriver driver)
    {
        _driver = driver;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 24);
        AddThemeConstantOverride("separation", 12);

        // The state reads twice: once as a glyph, once as the word for it. Colour never travels
        // alone here for the same reason it never does on a card.
        _stateIcon = new IconSlot("status", "active", IconSlot.RowSize, ShellPalette.StateOk);
        AddChild(_stateIcon);
        _state = AddLabel("NOMINAL");

        _playPause = AddIconButton("control", "pause", _driver.TogglePause);
        _step = AddIconButton("control", "step", _driver.Step);

        foreach (var speed in SimulationDriver.Speeds.Skip(1))
        {
            var captured = speed;
            _speedButtons.Add(AddButton($"×{speed}", () => _driver.SetSpeed(captured)));
        }

        AddChild(new IconSlot("status", "time", IconSlot.RowSize, ShellPalette.TextDim));
        _clock = AddLabel(Units.FormatSimTime(0));
        _tick = AddLabel("tick 0");
        AddLabel("STRATUM N-2 (fixed)");

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(spacer);

        // Both halves of the alert readout are hidden together while nothing is blocked: an alert
        // glyph sitting on the bar with no count beside it would read as a live warning.
        _alertIcon = new IconSlot("status", "alert", IconSlot.RowSize, ShellPalette.StateFault)
        {
            Visible = false,
        };
        AddChild(_alertIcon);
        _alerts = AddLabel(string.Empty);

        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    public void Refresh()
    {
        var snapshot = _driver.Snapshot;

        _clock.Text = Units.FormatSimTime(snapshot.Tick);
        _tick.Text = $"tick {snapshot.Tick}";

        // The button shows what pressing it does, so a running sim offers PAUSE and a paused one
        // offers PLAY.
        if (_offeringPlay != _driver.IsPaused)
        {
            _offeringPlay = _driver.IsPaused;
            _playPause.Icon = IconSlot.Load("control", _driver.IsPaused ? "play" : "pause");
            _playPause.TooltipText = _driver.IsPaused ? "PLAY" : "PAUSE";
        }

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
        _alertIcon.Visible = blocked > 0;
        _alerts.Text = blocked == 0 ? string.Empty : $"{blocked} alert{(blocked == 1 ? "" : "s")}";
        _alerts.AddThemeColorOverride("font_color", ShellPalette.StateFault);

        if (_driver.FaultMessage is { } fault)
        {
            _state.Text = fault;
            _state.AddThemeColorOverride("font_color", ShellPalette.StateFault);
            _stateIcon.SetIcon("status", "blocked");
            _stateIcon.SetTint(ShellPalette.StateFault);
        }
        else
        {
            _state.Text = _driver.IsPaused ? "PAUSED" : "NOMINAL";
            _state.AddThemeColorOverride(
                "font_color", _driver.IsPaused ? ShellPalette.StateWarn : ShellPalette.StateOk);
            _stateIcon.SetIcon("status", _driver.IsPaused ? "idle" : "active");
            _stateIcon.SetTint(_driver.IsPaused ? ShellPalette.StateWarn : ShellPalette.StateOk);
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

    /// <summary>
    /// A transport control: the glyph alone, with the word for it as the tooltip. These three sit
    /// together in a fixed order and are the only icon-only controls in the shell, which is what
    /// makes them readable without labels; anything else on the bar keeps its text.
    /// </summary>
    private Button AddIconButton(string domain, string name, System.Action onPressed)
    {
        var button = new Button
        {
            Icon = IconSlot.Load(domain, name),
            TooltipText = name.ToUpperInvariant(),
        };
        button.Pressed += onPressed;
        AddChild(button);
        return button;
    }
}
