using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Everything about whatever is selected in the focus view. It reads the selection from the shell
/// context and resolves the identifier against the current snapshot every time, so it holds no
/// reference to the graph and cannot go stale with it.
/// </summary>
public sealed partial class FacilityInspectorPanel : PanelBase
{
    private readonly List<Line> _lines = new();

    private ShellContext? _context;
    private Label _title = null!;
    private Label _subtitle = null!;
    private VBoxContainer _rows = null!;

    public override PanelId Id => ShellRoot.FacilityInspectorId;

    public override string Title => "Facility Inspector";

    public override void OnMount(ShellContext context) => _context = context;

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(column);

        _title = new Label();
        _title.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        _title.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        column.AddChild(_title);

        _subtitle = new Label();
        _subtitle.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        _subtitle.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(_subtitle);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        column.AddChild(_rows);
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        _lines.Clear();

        var selection = _context?.CurrentSelection;
        if (selection is not { } selected)
        {
            Head("NO SELECTION", string.Empty);
            Sync();
            return;
        }

        switch (selected.Kind)
        {
            case GraphNodeKind.Executor:
                Executor(snapshot, selected.Id);
                break;
            case GraphNodeKind.Transport:
                Transport(snapshot, selected.Id);
                break;
            case GraphNodeKind.Storage:
                Storage(snapshot, selected.Id);
                break;
            case GraphNodeKind.Power:
                Power(snapshot);
                break;
        }

        Sync();
    }

    private void Executor(WorldSnapshot snapshot, string id)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id.Value == id);
        if (executor is null)
        {
            Gone(id);
            return;
        }

        Head(executor.Label, $"FACILITY · {executor.Type.ToString().ToUpperInvariant()}");

        var (status, color) = Status(executor.Status, executor.BlockReason);
        Row("STATUS", status, color);
        Row("SCHEMATIC", executor.Configured?.Value.ToUpperInvariant() ?? "NONE");
        Row("POWER", $"{Units.Format(executor.PowerDraw)} MW");

        if (executor.SwitchOverTicksRemaining > 0)
        {
            Row("SWITCHOVER", $"{executor.SwitchOverTicksRemaining} ticks", ShellPalette.StateWarn);
        }

        // Zero total means no run in progress, so the bar renders empty rather than dividing.
        var done = executor.RunTicksTotal - executor.RunTicksRemaining;
        Row(
            "RUN",
            executor.RunTicksTotal == 0 ? "—" : $"{done} / {executor.RunTicksTotal} ticks",
            ShellPalette.TextPrimary,
            Fill(done, executor.RunTicksTotal));

        Heading("QUEUE");
        var queued = snapshot.ProductionTasks.Where(t => t.Executor == executor.Id).ToList();
        if (queued.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var task in queued)
        {
            var (state, stateColor) = TaskState(task.State, task.LastReason);
            Row($"{task.Schematic} {task.CompletedRuns}/{task.RequestedRuns}", state, stateColor);
        }

        Heading($"LOCAL STORAGE · {executor.LocalStorage.Value.ToUpperInvariant()}");
        Contents(snapshot, executor.LocalStorage);
    }

    private void Transport(WorldSnapshot snapshot, string id)
    {
        var line = snapshot.Transports.FirstOrDefault(t => t.Id.Value == id);
        if (line is null)
        {
            Gone(id);
            return;
        }

        Head(line.Label, "TRANSPORT LINE");

        var (status, color) = Status(line.Status, line.BlockReason);
        Row("STATUS", status, color);
        Row("ROUTE", $"{line.From} → {line.To}");
        Row("CARRYING", line.CarriedItem?.Value.ToUpperInvariant() ?? "NOTHING");
        Row("POWER", $"{Units.Format(line.PowerDraw)} MW");
        Row(
            "MOVED",
            $"{Units.Format(line.MovedLastTick)} / {Units.Format(line.ThroughputPerTick)}",
            ShellPalette.TextPrimary,
            Fill(line.MovedLastTick, line.ThroughputPerTick));

        Heading("QUEUE");
        var queued = snapshot.TransportTasks.Where(t => t.Executor == line.Id).ToList();
        if (queued.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var task in queued)
        {
            var (state, stateColor) = TaskState(task.State, task.LastReason);
            Row(
                $"{task.Item} {Units.Format(task.MovedQuantity)}/{Units.Format(task.RequestedQuantity)}",
                state,
                stateColor);
        }
    }

    private void Storage(WorldSnapshot snapshot, string id)
    {
        var storage = snapshot.Storages.FirstOrDefault(s => s.Id.Value == id);
        if (storage is null)
        {
            Gone(id);
            return;
        }

        Head(storage.Label, "STORAGE");
        Row(
            "FILL",
            $"{Units.Format(storage.TotalAmount)} / {Units.Format(storage.TotalCapacity)}",
            ShellPalette.TextPrimary,
            Fill(storage.TotalAmount, storage.TotalCapacity));

        Heading("CONTENTS");
        Contents(snapshot, storage.Id);
    }

    private void Power(WorldSnapshot snapshot)
    {
        // Deliberately a subset of the Energy Budget panel, which keeps the per-consumer
        // breakdown. Two panels showing the same list would be one panel too many.
        var energy = snapshot.Energy;

        Head("Power", "ENERGY POOL");
        Row("CAPACITY", $"{Units.Format(energy.Capacity)} MW");
        Row(
            "DRAW",
            $"{Units.Format(energy.Draw)} MW",
            ShellPalette.StateWarn,
            Fill(energy.Draw, energy.Capacity));
        Row("RESERVE", $"{Units.Format(energy.Reserve)} MW", ShellPalette.StateWarn);
        Row(
            "CAP HITS",
            energy.CapHits.ToString(),
            energy.CapHits > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint);
        Row(
            "STARVED",
            energy.StarvedTicks.ToString(),
            energy.StarvedTicks > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint);
    }

    private void Contents(WorldSnapshot snapshot, StorageId id)
    {
        var storage = snapshot.Storages.FirstOrDefault(s => s.Id == id);
        if (storage is null)
        {
            Row("—", "NO SUCH STORAGE", ShellPalette.StateFault);
            return;
        }

        foreach (var item in storage.Items)
        {
            Row(
                item.Id.Value.ToUpperInvariant(),
                $"{Units.Format(item.Amount)} / {Units.Format(item.Capacity)}",
                item.Amount > 0 ? ShellPalette.TextPrimary : ShellPalette.TextFaint,
                Fill(item.Amount, item.Capacity));
        }
    }

    /// <summary>
    /// The selected thing is not in this snapshot. Said plainly rather than falling back to an
    /// empty state, which would read as "nothing is selected" and be a different, wrong answer.
    /// </summary>
    private void Gone(string id)
    {
        Head(id.ToUpperInvariant(), string.Empty);
        Row("STATUS", "NO LONGER PRESENT", ShellPalette.StateFault);
    }

    private static float Fill(long amount, long capacity) =>
        capacity <= 0 ? 0f : Mathf.Clamp((float)((double)amount / capacity), 0f, 1f);

    private static (string Text, Color Color) Status(ExecutorStatus status, PostponeReason? reason) =>
        status switch
        {
            ExecutorStatus.RunningTask => ("RUNNING", ShellPalette.StateOk),
            ExecutorStatus.SwitchingOver => ("SWITCHING", ShellPalette.StateWarn),
            ExecutorStatus.AllQueuedTasksBlocked =>
                ($"BLOCKED — {Describe(reason)}", ShellPalette.StateFault),
            _ => ("IDLE", ShellPalette.TextDim),
        };

    private static (string Text, Color Color) TaskState(TaskState state, PostponeReason? reason) =>
        state switch
        {
            Core.Simulation.TaskState.Running => ("RUNNING", ShellPalette.StateOk),
            Core.Simulation.TaskState.Complete => ("COMPLETE", ShellPalette.TextDim),
            Core.Simulation.TaskState.Postponed =>
                ($"POSTPONED — {Describe(reason)}", ShellPalette.StateFault),
            _ => ("NOT STARTED", ShellPalette.TextFaint),
        };

    private static string Describe(PostponeReason? reason) => reason switch
    {
        PostponeReason.InsufficientInputMaterial => "MISSING_INPUT",
        PostponeReason.InsufficientSourceMaterial => "NO_SOURCE_MATERIAL",
        PostponeReason.DestinationFull => "DESTINATION_FULL",
        PostponeReason.InsufficientEnergy => "INSUFFICIENT_ENERGY",
        PostponeReason.OutputRouteUnavailable => "NO_OUTPUT_ROUTE",
        PostponeReason.SafetyLock => "SAFETY_LOCK",
        _ => "UNKNOWN",
    };

    private void Head(string title, string subtitle)
    {
        _title.Text = title.ToUpperInvariant();
        _subtitle.Text = subtitle;
    }

    private void Heading(string text) =>
        _lines.Add(new Line(text.ToUpperInvariant(), string.Empty, ShellPalette.TextFaint, null, true));

    private void Row(string name, string value) => Row(name, value, ShellPalette.TextPrimary);

    private void Row(string name, string value, Color color, float? fill = null) =>
        _lines.Add(new Line(name.ToUpperInvariant(), value, color, fill, false));

    private void Sync()
    {
        while (_rows.GetChildCount() < _lines.Count)
        {
            _rows.AddChild(new DetailRow());
        }

        while (_rows.GetChildCount() > _lines.Count)
        {
            var extra = _rows.GetChild(_rows.GetChildCount() - 1);
            _rows.RemoveChild(extra);
            extra.QueueFree();
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            ((DetailRow)_rows.GetChild(i)).Update(_lines[i]);
        }
    }

    /// <summary>One rendered line, flattened from whichever kind of thing produced it.</summary>
    private readonly record struct Line(
        string Name, string Value, Color Color, float? Fill, bool IsHeading);

    /// <summary>
    /// A name, a value, and optionally a bar. One row type rather than three keeps the list sync
    /// above a single loop no matter what the selection is.
    /// </summary>
    private sealed partial class DetailRow : VBoxContainer
    {
        private Label _name = null!;
        private Label _value = null!;
        private ProgressBar _bar = null!;
        private Color? _lastColor;
        private bool? _lastHeading;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 0);

            var row = new HBoxContainer();
            AddChild(row);

            _name = new Label();
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

        public void Update(Line line)
        {
            _name.Text = line.Name;
            _value.Text = line.Value;

            _bar.Visible = line.Fill.HasValue;
            if (line.Fill is { } fill)
            {
                _bar.Value = fill;
            }

            // Text dedupes internally; the theme overrides do not, so they are skipped whenever
            // nothing about the row's colour or kind has actually changed.
            if (_lastColor != line.Color || _lastHeading != line.IsHeading)
            {
                _lastColor = line.Color;
                _lastHeading = line.IsHeading;
                _name.AddThemeColorOverride(
                    "font_color", line.IsHeading ? ShellPalette.TextFaint : ShellPalette.TextDim);
                _value.AddThemeColorOverride("font_color", line.Color);
                _bar.AddThemeStyleboxOverride("fill", ShellTheme.Surface(line.Color));
            }
        }
    }
}
