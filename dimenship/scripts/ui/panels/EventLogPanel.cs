using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Structured telemetry, filterable by category, honest about what it dropped.</summary>
public sealed partial class EventLogPanel : PanelBase
{
    /// <summary>
    /// Caps the expensive part of a rebuild (formatting, colour lookup, BBCode reshape) to a
    /// number of lines a person can actually read, regardless of how many events are sitting in
    /// the kernel's up-to-512-entry ring buffer.
    /// </summary>
    private const int MaxRenderedEvents = 200;

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
    private WorldSnapshot? _lastSnapshot;
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

                // OnSnapshot only fires on a new snapshot reference, and the driver stops
                // producing those while paused — so a filter click while paused would otherwise
                // leave the previous filter's contents on screen indefinitely. Rebuild straight
                // from whatever we last saw instead of waiting for a snapshot that may not come
                // for a while (see Zone.Show, which solves the same problem the same way).
                if (_lastSnapshot is not null)
                {
                    Rebuild(_lastSnapshot);
                }
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
        _lastSnapshot = snapshot;

        // In the shipped world every tick emits at least one event per facility, so this guard
        // does not skip normal play — TotalEventsEmitted changes on nearly every delivery. What
        // keeps this cheap is that Rebuild's own cost is bounded by MaxRenderedEvents rather than
        // by how many events the kernel is holding. The guard still earns its keep for deliveries
        // that carry nothing new: a redundant re-delivery of the same totals, or a filter that
        // was already applied.
        if (snapshot.TotalEventsEmitted == _lastTotal && _filter == _lastAppliedFilter)
        {
            return;
        }

        Rebuild(snapshot);
    }

    private void Rebuild(WorldSnapshot snapshot)
    {
        _lastTotal = snapshot.TotalEventsEmitted;
        _lastAppliedFilter = _filter;

        var dropped = snapshot.TotalEventsEmitted - snapshot.RecentEvents.Count;
        var matched = snapshot.RecentEvents.Where(e => _filter is null || e.Category == _filter).ToList();
        var hiddenByCap = System.Math.Max(0, matched.Count - MaxRenderedEvents);
        var shown = hiddenByCap > 0 ? matched.Skip(hiddenByCap) : matched;

        var text = new System.Text.StringBuilder();

        if (dropped > 0)
        {
            text.AppendLine($"[color=#{ShellPalette.TextFaint.ToHtml(false)}]─ {dropped} earlier events dropped ─[/color]");
        }

        if (hiddenByCap > 0)
        {
            text.AppendLine($"[color=#{ShellPalette.TextFaint.ToHtml(false)}]─ showing last {MaxRenderedEvents} of {matched.Count} matching events ─[/color]");
        }

        foreach (var e in shown)
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
