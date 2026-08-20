using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Content;
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
    private IconSlot _icon = null!;
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

        // The header is built like a node card's: the same icon at the same size beside the same
        // title, so the selected thing looks like the thing that was clicked on the graph.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(head);

        _icon = new IconSlot(IconSlot.CardSize, ShellPalette.TextDim);
        head.AddChild(_icon);

        var heading = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeConstantOverride("separation", 0);
        head.AddChild(heading);

        _title = new Label();
        _title.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        _title.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        heading.AddChild(_title);

        _subtitle = new Label();
        _subtitle.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        _subtitle.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        heading.AddChild(_subtitle);

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
            Head("NO SELECTION", string.Empty, null);
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

        // The icon name is the facility kind lowercased, exactly as ExecutorCard derives it: the
        // inspector and the card it was opened from must not name the same kind two ways.
        Head(
            executor.Label,
            $"FACILITY · {executor.Type.ToString().ToUpperInvariant()}",
            new IconRef("facility", executor.Type.ToString().ToLowerInvariant()));

        var (status, color) = Status(executor.Status, executor.BlockReason);
        Row("STATUS", status, color, null, new IconRef("status", Glyph(executor.Status)));
        Row("SCHEMATIC", executor.Configured?.Value.ToUpperInvariant() ?? "NONE");
        Row(
            "POWER",
            $"{Units.Format(executor.PowerDraw)} MW",
            ShellPalette.TextPrimary,
            null,
            new IconRef("status", "energy"));

        if (executor.SwitchOverTicksRemaining > 0)
        {
            Row(
                "SWITCHOVER",
                $"{executor.SwitchOverTicksRemaining} ticks",
                ShellPalette.StateWarn,
                null,
                new IconRef("status", "time"));
        }

        // Zero total means no run in progress, so the bar renders empty rather than dividing.
        var done = executor.RunTicksTotal - executor.RunTicksRemaining;
        Row(
            "RUN",
            executor.RunTicksTotal == 0 ? "—" : $"{done} / {executor.RunTicksTotal} ticks",
            ShellPalette.TextPrimary,
            Fill(done, executor.RunTicksTotal),
            new IconRef("status", "time"));

        Heading("QUEUE", new IconRef("status", "queue"));
        var queued = snapshot.ProductionTasks.Where(t => t.Executor == executor.Id).ToList();
        if (queued.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var task in queued)
        {
            var (state, stateColor) = TaskState(task.State, task.LastReason);

            // A standing order has a running total and no ratio. Rendering it as one would mean
            // inventing a denominator, and a progress bar that never moves.
            var progress = task.RequestedRuns is { } requested
                ? $"{task.CompletedRuns}/{requested}"
                : $"{task.CompletedRuns} · STANDING";

            Row(
                $"{task.Schematic} {progress}",
                state,
                stateColor,
                null,
                new IconRef("status", Glyph(task.State)));
        }

        Heading(
            $"LOCAL STORAGE · {executor.LocalStorage.Value.ToUpperInvariant()}",
            new IconRef("facility", "storage"));
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

        Head(line.Label, "TRANSPORT LINE", new IconRef("control", "chevron_right"));

        var (status, color) = Status(line.Status, line.BlockReason);
        Row("STATUS", status, color, null, new IconRef("status", Glyph(line.Status)));
        Row(
            "ROUTE",
            $"{line.From} → {line.To}",
            ShellPalette.TextPrimary,
            null,
            new IconRef("control", "chevron_right"));

        // An empty line still says CARRYING NOTHING, and gets no item glyph to go with it: an
        // icon there would name a cargo the line is not carrying.
        Row(
            "CARRYING",
            line.CarriedItem?.Value.ToUpperInvariant() ?? "NOTHING",
            ShellPalette.TextPrimary,
            null,
            line.CarriedItem is { } carried ? new IconRef("item", carried.Value) : null);

        Row(
            "POWER",
            $"{Units.Format(line.PowerDraw)} MW",
            ShellPalette.TextPrimary,
            null,
            new IconRef("status", "energy"));
        Row(
            "MOVED",
            $"{Units.Format(line.MovedLastTick)} / {Units.Format(line.ThroughputPerTick)}",
            ShellPalette.TextPrimary,
            Fill(line.MovedLastTick, line.ThroughputPerTick),
            new IconRef("status", "rate"));

        Heading("QUEUE", new IconRef("status", "queue"));
        var queued = snapshot.TransportTasks.Where(t => t.Executor == line.Id).ToList();
        if (queued.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var task in queued)
        {
            var (state, stateColor) = TaskState(task.State, task.LastReason);
            var moved = task.RequestedQuantity is { } requested
                ? $"{Units.Format(task.MovedQuantity)}/{Units.Format(requested)}"
                : $"{Units.Format(task.MovedQuantity)} · STANDING";

            Row($"{task.Item} {moved}", state, stateColor, null, new IconRef("item", task.Item.Value));
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

        Head(storage.Label, "STORAGE", new IconRef("facility", "storage"));
        Row(
            "FILL",
            $"{Units.FormatPermille(storage.FillPermille)} OF ONE HOLD",
            ShellPalette.TextPrimary,
            Fill(storage.FillPermille, StorageArchetype.FullHold),
            new IconRef("status", "capacity"));

        Heading("CONTENTS", new IconRef("status", "queue"));
        Contents(snapshot, storage.Id);
    }

    private void Power(WorldSnapshot snapshot)
    {
        // Deliberately a subset of the Energy Budget panel, which keeps the per-consumer
        // breakdown. Two panels showing the same list would be one panel too many.
        var energy = snapshot.Energy;

        Head("Power", "ENERGY POOL", new IconRef("facility", "power"));
        Row(
            "CAPACITY",
            $"{Units.Format(energy.Capacity)} MW",
            ShellPalette.TextPrimary,
            null,
            new IconRef("status", "capacity"));
        Row(
            "DRAW",
            $"{Units.Format(energy.Draw)} MW",
            ShellPalette.StateWarn,
            Fill(energy.Draw, energy.Capacity),
            new IconRef("status", "energy"));
        Row(
            "RESERVE",
            $"{Units.Format(energy.Reserve)} MW",
            ShellPalette.StateWarn,
            null,
            new IconRef("status", "durability"));
        Row(
            "CAP HITS",
            energy.CapHits.ToString(),
            energy.CapHits > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint,
            null,
            new IconRef("status", "alert"));
        Row(
            "STARVED",
            energy.StarvedTicks.ToString(),
            energy.StarvedTicks > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint,
            null,
            new IconRef("status", "alert"));
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
                Fill(item.Amount, item.Capacity),
                new IconRef("item", item.Id.Value));
        }
    }

    /// <summary>
    /// The selected thing is not in this snapshot. Said plainly rather than falling back to an
    /// empty state, which would read as "nothing is selected" and be a different, wrong answer.
    /// </summary>
    private void Gone(string id)
    {
        Head(id.ToUpperInvariant(), string.Empty, new IconRef("status", "blocked"));
        Row(
            "STATUS",
            "NO LONGER PRESENT",
            ShellPalette.StateFault,
            null,
            new IconRef("status", "blocked"));
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

    /// <summary>
    /// The status domain's name for a facility state. It sits beside <see cref="Status"/> rather
    /// than inside it because the two are read at different rates: the word and its colour change
    /// on the tick, the glyph only when the state itself does.
    /// </summary>
    private static string Glyph(ExecutorStatus status) => status switch
    {
        ExecutorStatus.RunningTask => "active",
        ExecutorStatus.SwitchingOver => "time",
        ExecutorStatus.AllQueuedTasksBlocked => "blocked",
        _ => "idle",
    };

    /// <summary>
    /// The same for a queued task. A finished task takes the notice glyph rather than the running
    /// one: it is on the list to be read, not to be waited on.
    /// </summary>
    private static string Glyph(TaskState state) => state switch
    {
        Core.Simulation.TaskState.Running => "active",
        Core.Simulation.TaskState.Complete => "notice",
        Core.Simulation.TaskState.Postponed => "blocked",
        _ => "idle",
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

    private void Head(string title, string subtitle, IconRef? icon)
    {
        _title.Text = title.ToUpperInvariant();
        _subtitle.Text = subtitle;

        if (icon is { } reference)
        {
            _icon.SetIcon(reference.Domain, reference.Name);
        }
        else
        {
            _icon.Clear();
        }
    }

    private void Heading(string text, IconRef? icon = null) =>
        _lines.Add(
            new Line(text.ToUpperInvariant(), string.Empty, ShellPalette.TextFaint, null, true, icon));

    private void Row(string name, string value) => Row(name, value, ShellPalette.TextPrimary);

    private void Row(string name, string value, Color color, float? fill = null, IconRef? icon = null) =>
        _lines.Add(new Line(name.ToUpperInvariant(), value, color, fill, false, icon));

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
        string Name, string Value, Color Color, float? Fill, bool IsHeading, IconRef? Icon);

    /// <summary>
    /// Which icon a line carries, if any. A pair rather than one <c>domain/name</c> string because
    /// that is what <see cref="IconSlot.SetIcon"/> takes, and splitting one back apart on every
    /// row of every snapshot would be work done purely to undo the joining.
    /// </summary>
    private readonly record struct IconRef(string Domain, string Name);

    /// <summary>
    /// A name, a value, and optionally a bar. One row type rather than three keeps the list sync
    /// above a single loop no matter what the selection is.
    /// </summary>
    private sealed partial class DetailRow : VBoxContainer
    {
        /// <summary>These bars are 4px, which is below the height a rounded fill is legible at.</summary>
        private const int BarHeight = 4;

        private IconSlot _icon = null!;
        private Label _name = null!;
        private Label _value = null!;
        private ProgressBar _bar = null!;
        private Color? _lastColor;
        private bool? _lastHeading;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 0);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            AddChild(row);

            // Always present, empty when the line has no icon: rows are recycled between
            // selections, and a slot that came and went would step every name in the list
            // sideways as the player clicked from one node to the next.
            _icon = new IconSlot(IconSlot.RowSize, ShellPalette.TextDim);
            row.AddChild(_icon);

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
                CustomMinimumSize = new Vector2(0, BarHeight),
            };
            _bar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(BarHeight));
            AddChild(_bar);
        }

        public void Update(Line line)
        {
            _name.Text = line.Name;
            _value.Text = line.Value;

            if (line.Icon is { } reference)
            {
                _icon.SetIcon(reference.Domain, reference.Name);
            }
            else
            {
                _icon.Clear();
            }

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
                _bar.AddThemeStyleboxOverride("fill", ShellTheme.MeterFill(line.Color, BarHeight));

                // The glyph takes the row's own colour, so a blocked line reads as blocked from
                // its icon as well as from the word and the bar beside it.
                _icon.SetTint(line.IsHeading ? ShellPalette.TextFaint : line.Color);
            }
        }
    }
}
