using System.Linq;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
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
    private CardGauge _hold = null!;

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

        // Accent, the colour a storage card fills its own meter with: the two report the same
        // thing, and a buffer that read in a different colour would look like a different quantity.
        _hold = Gauge(ShellPalette.Accent);
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id == _id);
        if (executor is null)
        {
            Status("STATUS", "ABSENT", ShellPalette.StateFault);

            // Emptied rather than left alone: a gauge still showing the last reading of a facility
            // that is gone is the one failure a card this small cannot afford.
            _hold.Set(0f);
            return;
        }

        // Read every snapshot, not watched for a FacilityBuilt event: the snapshot already carries
        // Built on every delivery, so a plain read here is what re-chromes the card the tick
        // commissioning sets it, with no second path duplicating what the snapshot already says.
        SetBuilt(executor.Built);

        var (text, color) = executor.Status switch
        {
            ExecutorStatus.RunningTask => ("Production", ShellPalette.StateOk),
            ExecutorStatus.SwitchingOver => ("Reconfiguration", ShellPalette.StateWarn),
            ExecutorStatus.AllQueuedTasksBlocked =>
                ($"Blocked — {Describe(executor.BlockReason)}", ShellPalette.StateFault),

            // A transport-only state, and never a fault: work is queued and there was nothing to
            // pick up for it. Dimmed like Idle, because that is what the line is.
            ExecutorStatus.NothingToCarry => ("Nothing to carry", ShellPalette.TextDim),
            _ => ("Idle", ShellPalette.TextDim),
        };

        Status("STATUS", text, color);

        // A slot nothing has commissioned spends this line on where its construction stands instead
        // of on what it is configured for, because it is configured for nothing and an unbuilt dock
        // reading MISSION DOCK · UNCONFIGURED is equally true of a working idle one. The word is
        // what makes the state legible with the colour taken away — the dimming and the dashed
        // outline beside it are a silhouette and a mark, and neither of them is a sentence.
        //
        // Safe unconditionally: ConstructionProgress.For throws only for a slot the snapshot does
        // not carry, and the executor-is-null branch above already returned for that case.
        _detail.Text = executor.Built
            ? $"{Spaced(executor.Type)} · " +
              $"{executor.Configured?.Value.ToUpperInvariant() ?? "UNCONFIGURED"}"
            : Phase(ConstructionProgress.For(snapshot, _id));

        var queued = snapshot.Tasks.Where(t => t.Action is Produce).Count(
            t => t.Executor == _id && t.State != TaskState.Complete);
        var tasks = queued == 1 ? "1 TASK QUEUED" : $"{queued} TASKS QUEUED";

        // The facility's own buffer has no card of its own — it is drawn here — so this line is
        // the only place on the graph that a facility starved of input can be seen filling or
        // emptying. A buffer the snapshot does not carry is reported as missing rather than as
        // empty: the two look identical on a meter and mean entirely different things.
        var buffer = snapshot.Storages.FirstOrDefault(s => s.Id == _buffer);
        _queue.Text = buffer is null
            ? $"{tasks} · BUFFER ABSENT"
            : $"{tasks} · BUF {Units.FormatPermille(buffer.FillPermille)}";

        // The same reading as the text beside it, down the card's right edge, so a buffer filling
        // toward full is visible across a graph of cards without any of them being read.
        _hold.Set(buffer is null ? 0f : Fill(buffer.FillPermille));

        // Zero total is a facility between runs: an empty bar, not a division.
        _run.Set(Fill(executor.RunTicksTotal - executor.RunTicksRemaining, executor.RunTicksTotal));
    }

    /// <summary>
    /// Where an unbuilt slot's construction has got to, in one word. The same vocabulary the
    /// inspector and the Operations detail use for the same projection, and the same
    /// dash-and-root-cause shape every blocked reading on this card already takes — one condition
    /// named three different ways across three surfaces is how a player learns to distrust all of
    /// them.
    /// <para>
    /// <see cref="ConstructionPhase.Complete"/> is asked for here even though the caller only asks
    /// while <c>Built</c> is false, and it is not the same thing as built. The projection reports it
    /// for an unbuilt slot whose plan has no work left to show — the tick between the last task
    /// retiring and commissioning catching up, and permanently for a plan whose tasks have all aged
    /// out of the registry's window with the slot still standing. Folding it into the default would
    /// print <c>UNBUILT</c> over a slot whose unit has already arrived, which is the one thing this
    /// line exists to stop being said.
    /// </para>
    /// </summary>
    private static string Phase(ConstructionProgress progress) => progress.Phase switch
    {
        ConstructionPhase.Queued => "QUEUED",
        ConstructionPhase.ProducingUnit => "PRODUCING",
        ConstructionPhase.InTransit => "IN TRANSIT",
        ConstructionPhase.Blocked => $"BLOCKED — {Describe(progress.BlockedReason)}",
        ConstructionPhase.Complete => "COMMISSIONING",
        _ => "UNBUILT",
    };

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
