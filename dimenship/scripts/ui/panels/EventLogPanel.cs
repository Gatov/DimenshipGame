using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Structured telemetry, filterable by category, honest about what it dropped.</summary>
public sealed partial class EventLogPanel : PanelBase
{
    private static readonly (string Label, EventCategory? Category)[] Filters =
    {
        ("all", null),
        ("production", EventCategory.Production),
        ("power", EventCategory.Power),
        ("fault", EventCategory.Fault),
    };

    private readonly List<Button> _filterButtons = new();

    private EventCategory? _filter;
    private long _lastTotal = -1;
    private EventCategory? _lastAppliedFilter;
    private RichTextLabel _output = null!;

    public override PanelId Id => ShellRoot.EventLogId;

    public override string Title => "Event Log";

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(column);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(filterRow);

        foreach (var (label, category) in Filters)
        {
            var captured = category;
            var button = new Button { Text = label };
            button.Pressed += () =>
            {
                _filter = captured;
                UpdateFilterButtons();
            };
            _filterButtons.Add(button);
            filterRow.AddChild(button);
        }

        _output = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FitContent = false,
        };
        _output.AddThemeFontSizeOverride("normal_font_size", ShellPalette.FontMicro);
        column.AddChild(_output);

        UpdateFilterButtons();
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        // Rebuilding only when the event total or the filter changed keeps this off the
        // per-frame path: most snapshots emit nothing new.
        if (snapshot.TotalEventsEmitted == _lastTotal && _filter == _lastAppliedFilter)
        {
            return;
        }

        _lastTotal = snapshot.TotalEventsEmitted;
        _lastAppliedFilter = _filter;

        var dropped = snapshot.TotalEventsEmitted - snapshot.RecentEvents.Count;
        var text = new System.Text.StringBuilder();

        if (dropped > 0)
        {
            text.AppendLine($"[color=#{ShellPalette.TextFaint.ToHtml(false)}]─ {dropped} earlier events dropped ─[/color]");
        }

        foreach (var e in snapshot.RecentEvents.Where(e => _filter is null || e.Category == _filter))
        {
            text.AppendLine(Render(e));
        }

        _output.Text = text.ToString();
    }

    private static string Render(SimEvent e)
    {
        var faint = ShellPalette.TextFaint.ToHtml(false);
        var dim = ShellPalette.TextDim.ToHtml(false);
        var (code, color) = e.Code switch
        {
            EventCode.Run => ("RUN  ", ShellPalette.StateOk),
            EventCode.BlockMissingInput => ("BLOCK", ShellPalette.StateFault),
            EventCode.BlockPowerCap => ("BLOCK", ShellPalette.StateFault),
            EventCode.PowerCapReached => ("WARN ", ShellPalette.StateWarn),
            EventCode.StockFull => ("WARN ", ShellPalette.StateWarn),
            _ => ("EVENT", ShellPalette.TextPrimary),
        };

        var data = e.Data.Count == 0
            ? string.Empty
            : " " + string.Join(" ", e.Data.Select(kv => $"{kv.Key}={Units.Format(kv.Value)}"));

        return $"[color=#{faint}]{Units.FormatSimTime(e.Tick)}[/color] " +
               $"[color=#{color.ToHtml(false)}]{code}[/color] " +
               $"{e.Subject.ToUpperInvariant()}[color=#{dim}]{data}[/color]";
    }

    private void UpdateFilterButtons()
    {
        for (var i = 0; i < _filterButtons.Count; i++)
        {
            _filterButtons[i].Disabled = Filters[i].Category == _filter;
        }
    }
}
