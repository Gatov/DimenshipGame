using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>One production facility: what it is set up for, what it is doing, and how far in.</summary>
public sealed partial class ExecutorCard : NodeCard
{
    private readonly ExecutorId _id;
    private readonly StorageId _buffer;

    private Label _detail = null!;
    private Label _queue = null!;
    private CardMeter _run = null!;

    public ExecutorCard(
        ExecutorId id, string label, FacilityType type, string badge, StorageId buffer)
        : base(
            new GraphSelection(GraphNodeKind.Executor, id.Value),
            label,
            badge,
            // The icon name is the facility kind, lowercased: one file per kind, and a kind added
            // to the enum without an icon beside it renders an empty slot rather than the wrong one.
            type.ToString().ToLowerInvariant())
    {
        _id = id;
        _buffer = buffer;
    }

    protected override void BuildBody(VBoxContainer column)
    {
        _detail = Row(column, ShellPalette.TextDim);
        _queue = Row(column, ShellPalette.TextFaint);
        _run = Meter(column, ShellPalette.StateOk);
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id == _id);
        if (executor is null)
        {
            Status("STATUS", "ABSENT", ShellPalette.StateFault);
            return;
        }

        var (text, color) = executor.Status switch
        {
            ExecutorStatus.RunningTask => ("RUNNING", ShellPalette.StateOk),
            ExecutorStatus.SwitchingOver => ("SWITCHING", ShellPalette.StateWarn),
            ExecutorStatus.AllQueuedTasksBlocked =>
                ($"BLOCKED — {Describe(executor.BlockReason)}", ShellPalette.StateFault),
            _ => ("IDLE", ShellPalette.TextDim),
        };

        Status("STATUS", text, color);

        _detail.Text =
            $"{Spaced(executor.Type)} · " +
            $"{executor.Configured?.Value.ToUpperInvariant() ?? "UNCONFIGURED"}";

        var queued = snapshot.ProductionTasks.Count(
            t => t.Executor == _id && t.State != TaskState.Complete);
        var tasks = queued == 1 ? "1 TASK QUEUED" : $"{queued} TASKS QUEUED";

        // The facility's own buffer has no card of its own — it is drawn here — so this line is
        // the only place on the graph that a facility starved of input can be seen filling or
        // emptying. A buffer the snapshot does not carry is reported as missing rather than as
        // empty: the two look identical on a meter and mean entirely different things.
        var buffer = snapshot.Storages.FirstOrDefault(s => s.Id == _buffer);
        _queue.Text = buffer is null
            ? $"{tasks} · BUFFER ABSENT"
            : $"{tasks} · BUF {Percent(buffer.TotalAmount, buffer.TotalCapacity)}";

        // Zero total is a facility between runs: an empty bar, not a division.
        _run.Set(Fill(executor.RunTicksTotal - executor.RunTicksRemaining, executor.RunTicksTotal));
    }

    /// <summary>Floored, like every other meter on a card: a buffer that is not full must not
    /// read 100%. A buffer with no capacity reads 0% rather than dividing.</summary>
    private static string Percent(long amount, long capacity) =>
        $"{Mathf.FloorToInt(Fill(amount, capacity) * 100f)}%";

    /// <summary>
    /// The facility kind as the GDD writes it: <c>MATTER REACTOR</c>, not <c>MATTERREACTOR</c>. The
    /// enum member is the name and the card is where it is read, so the word break belongs here
    /// rather than in a second table of labels that could drift from the enum.
    /// </summary>
    private static string Spaced(FacilityType type)
    {
        var name = type.ToString();
        var text = new System.Text.StringBuilder(name.Length + 4);

        foreach (var character in name)
        {
            if (char.IsUpper(character) && text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(char.ToUpperInvariant(character));
        }

        return text.ToString();
    }
}
