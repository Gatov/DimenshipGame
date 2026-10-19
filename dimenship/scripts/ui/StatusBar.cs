using System;
using System.Linq;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Bottom strip: time state, transport controls, sim clock, vessel health and the attention
/// summary.
/// <para>
/// Time and health are two readings, never one word (UX-04,
/// <c>docs/superpowers/specs/2026-10-19-ux-review-adoption-design.md</c>). The bar used to say
/// NOMINAL whenever the simulation ran without a kernel fault, beside a red count of blocked
/// facilities, and to count only what it derived itself: the kernel's own alert list never
/// reached a screen, so a plan starving behind another for an hour was raised by
/// <c>ReviewWaitingPlans</c> and seen by nobody.
/// </para>
/// </summary>
public sealed partial class StatusBar : HBoxContainer
{
    private readonly SimulationDriver _driver;
    private readonly ShellActions _actions;

    private Label _time = null!;
    private IconSlot _stateIcon = null!;
    private Label _state = null!;
    private Button _playPause = null!;
    private Button _step = null!;
    private Label _clock = null!;
    private Label _tick = null!;
    private IconSlot _alertIcon = null!;
    private Button _attention = null!;
    private readonly System.Collections.Generic.List<Button> _speedButtons = new();

    /// <summary>Which face the transport button is currently wearing. <see cref="Refresh"/> runs
    /// every frame, and swapping a texture it is already showing is work for nothing.</summary>
    private bool? _offeringPlay;

    /// <summary>The snapshot <see cref="_items"/> was read from. Reference inequality is the change
    /// test, as in <see cref="ShellRoot"/>: the summary is recounted per snapshot, not per frame.</summary>
    private WorldSnapshot? _countedSnapshot;

    private System.Collections.Generic.List<AttentionItem> _items = new();

    /// <summary>Which item the next press opens. Pressing again walks the list, so a second
    /// problem is one press away rather than behind a search of the graph.</summary>
    private int _cursor;

    public StatusBar(SimulationDriver driver, ShellActions actions)
    {
        _driver = driver;
        _actions = actions;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 24);
        AddThemeConstantOverride("separation", 12);

        _time = AddLabel("RUNNING ×1");
        _time.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);

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

        // The health reads twice: once as a glyph, once as the word for it. Colour never travels
        // alone here for the same reason it never does on a card.
        _stateIcon = new IconSlot("status", "active", IconSlot.RowSize, ShellPalette.StateOk);
        AddChild(_stateIcon);
        _state = AddLabel("NOMINAL");

        // Both halves of the attention readout are hidden together while nothing needs it: an
        // alert glyph sitting on the bar with no count beside it would read as a live warning.
        _alertIcon = new IconSlot("status", "alert", IconSlot.RowSize, ShellPalette.StateFault)
        {
            Visible = false,
        };
        AddChild(_alertIcon);

        _attention = new Button { Visible = false, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_attention);
        _attention.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _attention.Pressed += OpenNextItem;
        AddChild(_attention);

        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    public void Refresh()
    {
        var snapshot = _driver.Snapshot;

        _clock.Text = Units.FormatSimTime(snapshot.Tick);
        _tick.Text = $"tick {snapshot.Tick}";
        _time.Text = _driver.IsPaused ? "PAUSED" : $"RUNNING ×{_driver.Speed}";

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

        if (!ReferenceEquals(snapshot, _countedSnapshot))
        {
            _countedSnapshot = snapshot;
            _items = Attention(snapshot);
        }

        var blocked = _items.Count(i => i.Kind == AttentionKind.Blocked);
        var anyFault = _items.Any(i => i.Fault);

        _alertIcon.Visible = _items.Count > 0;
        _attention.Visible = _items.Count > 0;
        if (_items.Count > 0)
        {
            var colour = anyFault ? ShellPalette.StateFault : ShellPalette.StateWarn;
            _attention.Text = Summary(_items);
            _attention.TooltipText = $"OPEN {_items[_cursor % _items.Count].Name}";
            _attention.AddThemeColorOverride("font_color", colour);
            _alertIcon.SetTint(colour);
        }

        if (_driver.FaultMessage is { } message)
        {
            SetHealth(message, "blocked", ShellPalette.StateFault);
        }
        else if (anyFault)
        {
            SetHealth(blocked > 0 ? "BLOCKED" : "CRITICAL", "blocked", ShellPalette.StateFault);
        }
        else if (_items.Count > 0)
        {
            SetHealth("ATTENTION", "alert", ShellPalette.StateWarn);
        }
        else
        {
            SetHealth("NOMINAL", "active", ShellPalette.StateOk);
        }
    }

    private void SetHealth(string text, string icon, Color colour)
    {
        _state.Text = text;
        _state.AddThemeColorOverride("font_color", colour);
        _stateIcon.SetIcon("status", icon);
        _stateIcon.SetTint(colour);
    }

    /// <summary>
    /// What needs the player, in a fixed order: blocked facilities, then blocked lines, then the
    /// kernel's live alerts in the order raised. Derived, never stored.
    /// <para>
    /// Blocked is U3's rule, output that cannot be put down, read through
    /// <see cref="ExecutorCondition"/>; one waiting for input is not here, or every vessel short of
    /// ore would carry a red number. Transport counts as well as production, because a vessel whose
    /// haulage has stalled is exactly as stopped as one whose furnaces have.
    /// </para>
    /// <para>
    /// Each subject is counted once. The derived readings already cover a blocked executor, so an
    /// <see cref="AlertCode.ExecutorBlocked"/> alert (nothing raises one today) is skipped rather
    /// than counted a second time beside it. A waiting plan is a plan, not a facility, and is its
    /// own item even when the plan it waits behind is the one a blocked facility is running: the
    /// two need different controls.
    /// </para>
    /// </summary>
    private static System.Collections.Generic.List<AttentionItem> Attention(WorldSnapshot snapshot)
    {
        var items = new System.Collections.Generic.List<AttentionItem>();

        foreach (var executor in snapshot.Executors)
        {
            if (ExecutorCondition.For(snapshot, executor.Id).Standing == ExecutorStanding.Blocked)
            {
                items.Add(new AttentionItem(
                    executor.Label.ToUpperInvariant(),
                    AttentionKind.Blocked,
                    Fault: true,
                    new GraphSelection(GraphNodeKind.Executor, executor.Id.Value),
                    Plan: null));
            }
        }

        foreach (var line in snapshot.Transports)
        {
            if (line.Status == ExecutorStatus.AllQueuedTasksBlocked)
            {
                items.Add(new AttentionItem(
                    line.Label.ToUpperInvariant(),
                    AttentionKind.Blocked,
                    Fault: true,
                    new GraphSelection(GraphNodeKind.Transport, line.Id.Value),
                    Plan: null));
            }
        }

        foreach (var alert in snapshot.Alerts)
        {
            if (alert.Acknowledged || alert.Code == AlertCode.ExecutorBlocked)
            {
                continue;
            }

            var plan = PlanOf(alert.SubjectId);
            items.Add(new AttentionItem(
                plan is { } id ? $"PLAN #{id}" : "ALERT",
                alert.Code == AlertCode.PlanWaiting ? AttentionKind.PlanWaiting : AttentionKind.Alert,
                Fault: alert.Severity == AlertSeverity.Critical,
                Selection: null,
                plan));
        }

        return items;
    }

    /// <summary>
    /// The plan an alert names, from the kernel's <c>plan:{id}</c> subject. Anything else is an
    /// alert about something that has no view to open yet, and is counted without a destination.
    /// </summary>
    private static PlanId? PlanOf(string subject) =>
        subject.StartsWith("plan:", System.StringComparison.Ordinal)
        && long.TryParse(subject.AsSpan(5), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? new PlanId(id)
            : null;

    /// <summary>
    /// Opens the item under the cursor and moves the cursor on. A plan opens in Operations through
    /// <see cref="ShellActions.OperationsRequested"/>, the inspector's own VIEW PLAN path. A
    /// facility or line is selected, inspected, and the base graph shown again, which remounts it
    /// on the new selection: the graph reads the selection on mount, so a selection made from
    /// here would otherwise leave the old card highlighted.
    /// </summary>
    private void OpenNextItem()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var item = _items[_cursor % _items.Count];
        _cursor = (_cursor + 1) % _items.Count;

        if (item.Plan is { } plan)
        {
            _actions.OperationsRequested?.Invoke(new PendingOperationsTarget(Plan: plan));
        }
        else if (item.Selection is { } selection)
        {
            _actions.SelectionChanged?.Invoke(selection);
            _actions.InspectRequested?.Invoke();
            _actions.FocusRequested?.Invoke(ShellRoot.OverviewId);
        }
    }

    /// <summary>"2 BLOCKED · 1 PLAN WAITING": one count per kind present, in the kind's order.</summary>
    private static string Summary(System.Collections.Generic.IReadOnlyList<AttentionItem> items)
    {
        var parts = new System.Collections.Generic.List<string>(3);
        foreach (var kind in new[] { AttentionKind.Blocked, AttentionKind.PlanWaiting, AttentionKind.Alert })
        {
            var count = items.Count(i => i.Kind == kind);
            if (count == 0)
            {
                continue;
            }

            parts.Add(kind switch
            {
                AttentionKind.Blocked => $"{count} BLOCKED",
                AttentionKind.PlanWaiting => $"{count} PLAN{(count == 1 ? "" : "S")} WAITING",
                _ => $"{count} ALERT{(count == 1 ? "" : "S")}",
            });
        }

        return string.Join(" · ", parts);
    }

    /// <summary>What an attention item is, for the summary's wording. <see cref="AttentionItem.Fault"/>
    /// carries the colour separately, because a kernel alert's severity is its own.</summary>
    private enum AttentionKind
    {
        Blocked,
        PlanWaiting,
        Alert,
    }

    /// <summary>One thing the attention summary can open: a graph selection, a plan, or neither.</summary>
    private readonly record struct AttentionItem(
        string Name, AttentionKind Kind, bool Fault, GraphSelection? Selection, PlanId? Plan);

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
