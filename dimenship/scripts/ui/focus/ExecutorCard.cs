using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>One production facility: what it is set up for, what it is doing, and how far in.</summary>
public sealed partial class ExecutorCard : NodeCard
{
    private readonly ExecutorId _id;

    private Label _detail = null!;
    private Label _queue = null!;
    private ProgressBar _run = null!;

    public ExecutorCard(ExecutorId id, string label)
        : base(new GraphSelection(GraphNodeKind.Executor, id.Value), label) =>
        _id = id;

    protected override void BuildBody(VBoxContainer column)
    {
        _detail = Row(column, ShellPalette.TextDim);
        _queue = Row(column, ShellPalette.TextFaint);
        _run = Bar(column, ShellPalette.StateOk);
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id == _id);
        if (executor is null)
        {
            Status("ABSENT", ShellPalette.StateFault);
            return;
        }

        var (text, color) = executor.Status switch
        {
            ExecutorStatus.RunningTask => ("◉ RUNNING", ShellPalette.StateOk),
            ExecutorStatus.SwitchingOver => ("◉ SWITCHING", ShellPalette.StateWarn),
            ExecutorStatus.AllQueuedTasksBlocked =>
                ($"◉ BLOCKED — {Describe(executor.BlockReason)}", ShellPalette.StateFault),
            _ => ("◉ IDLE", ShellPalette.TextDim),
        };

        Status(text, color);

        _detail.Text =
            $"{executor.Type.ToString().ToUpperInvariant()} · " +
            $"{executor.Configured?.Value.ToUpperInvariant() ?? "UNCONFIGURED"}";

        var queued = snapshot.ProductionTasks.Count(
            t => t.Executor == _id && t.State != TaskState.Complete);
        _queue.Text = queued == 1 ? "1 TASK QUEUED" : $"{queued} TASKS QUEUED";

        // Zero total is a facility between runs: an empty bar, not a division.
        _run.Value = Fill(executor.RunTicksTotal - executor.RunTicksRemaining, executor.RunTicksTotal);
    }
}
