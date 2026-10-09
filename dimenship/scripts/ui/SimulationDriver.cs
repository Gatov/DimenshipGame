using System;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Owns the kernel and decides how much time it is given. Everything the kernel is forbidden
/// to know about — wall-clock delta, pause, speed multiplier, failure recovery — lives here.
/// </summary>
public sealed partial class SimulationDriver : Node
{
    /// <summary>Index 0 is paused. The remaining entries are the selectable multipliers.</summary>
    public static readonly int[] Speeds = { 0, 1, 5, 30 };

    private readonly SimulationEngine _engine = ShellContent.NewGame();

    private double _accumulator;
    private int _speedIndex = 1;
    private int _resumeIndex = 1;

    /// <summary>
    /// How many events the last <see cref="EventCode.PlanCompleted"/> scan has already accounted for.
    /// <see cref="WorldSnapshot.RecentEvents"/> is a bounded window, not a log, so a scan has to
    /// know how much of it is new since the last look rather than re-reading the whole window
    /// every time and re-pausing on an event that already fired.
    /// </summary>
    private long _lastCheckedEventTotal;

    public WorldSnapshot Snapshot => _engine.Snapshot;

    public int Speed => Speeds[_speedIndex];

    public bool IsPaused => _speedIndex == 0;

    /// <summary>Non-null once the kernel has thrown. The driver stays stopped until restart.</summary>
    public string? FaultMessage { get; private set; }

    public override void _Process(double delta)
    {
        if (FaultMessage is not null || IsPaused)
        {
            return;
        }

        _accumulator += delta * Speed;

        var ticks = (long)_accumulator;
        if (ticks <= 0)
        {
            return;
        }

        _accumulator -= ticks;
        SafeAdvance(ticks);
    }

    public void TogglePause()
    {
        SetIndex(IsPaused ? _resumeIndex : 0);

        // Dropping the fraction avoids a burst of catch-up ticks on resume.
        _accumulator = 0;
    }

    /// <summary>Advances exactly one tick. Only meaningful while paused.</summary>
    public void Step()
    {
        if (IsPaused)
        {
            SafeAdvance(1);
        }
    }

    /// <summary>
    /// Advances a fixed number of ticks regardless of pause state, for debug time-skips. Unlike
    /// <see cref="Step"/> this deliberately ignores the pause gate: a debug affordance that
    /// silently does nothing while the sim happens to be running is worse than useless, because
    /// the failure looks like the simulation being wrong rather than the tool not firing. Still
    /// refuses once the kernel has faulted.
    /// </summary>
    public void AdvanceTicks(long ticks)
    {
        if (FaultMessage is not null || ticks <= 0)
        {
            return;
        }

        SafeAdvance(ticks);
    }

    public void SpeedUp()
    {
        if (_speedIndex < Speeds.Length - 1)
        {
            SetIndex(_speedIndex + 1);
        }
    }

    public void SpeedDown()
    {
        if (_speedIndex > 0)
        {
            SetIndex(_speedIndex - 1);
        }
    }

    public void SetSpeed(int speed)
    {
        var index = Array.IndexOf(Speeds, speed);
        if (index >= 0)
        {
            SetIndex(index);
        }
    }

    /// <summary>
    /// A read-only proposal for how <paramref name="goal"/> could be fulfilled, against the
    /// kernel's live state. Refuses after a fault along with every other call into the engine:
    /// a faulted kernel's collections are not trusted for a read any more than for a write, so an
    /// empty, unplannable-safe plan comes back rather than a call into state that may no longer be
    /// consistent. Unlike <see cref="Commit"/> this never changes anything, so it needs no
    /// try/catch of its own and never rebuilds <see cref="Snapshot"/> — nothing about the world
    /// moved for it to reflect.
    /// </summary>
    public ProductionPlan Plan(ItemAmount goal, StorageId? destination = null) =>
        Draft(goal, destination).Flatten();

    /// <summary>
    /// A live draft proposal against the kernel's current state. Refuses after a fault the same
    /// way <see cref="Plan"/> does — an empty draft rather than a read into inconsistent state.
    /// </summary>
    public PlanDraft Draft(
        ItemAmount goal, StorageId? destination = null, ExecutorId? assemblyTarget = null) =>
        FaultMessage is null
            ? PlanDraftEditor.Create(goal, _engine, destination, assemblyTarget)
            : EmptyDraft(goal, destination);

    /// <summary>
    /// Re-expands one edit against the live world. After a fault, returns an empty draft rather
    /// than touching state that may no longer be consistent.
    /// </summary>
    public PlanDraft Adjust(PlanDraft draft, DraftEdit edit) =>
        FaultMessage is null
            ? PlanDraftEditor.Adjust(draft, _engine, edit)
            : EmptyDraft(draft.Goal, draft.Destination);

    /// <summary>
    /// Validates and flattens a draft for commit. Wrapped like <see cref="Commit"/> because
    /// approval is a player command that can throw on stale or inconsistent input.
    /// </summary>
    public PlanApproval Approve(PlanDraft draft)
    {
        if (FaultMessage is not null)
        {
            return new PlanApprovalRefused(Array.Empty<DraftIssue>());
        }

        try
        {
            return PlanDraftEditor.Approve(draft, _engine);
        }
        catch (Exception e)
        {
            Fault(e);
            return new PlanApprovalRefused(Array.Empty<DraftIssue>());
        }
    }

    private static PlanDraft EmptyDraft(ItemAmount goal, StorageId? destination) =>
        new(goal, destination, null, Array.Empty<DraftStep>(), Array.Empty<DraftIssue>(), 0, 0);

    /// <summary>
    /// The shell's one door for changing the world other than by time: every player command goes
    /// through <see cref="SimulationEngine.Execute"/> here, the same door a controller will use, so
    /// the player never has a command a program cannot have (C0;
    /// <c>docs/superpowers/specs/2026-10-09-kernel-command-surface-design.md</c>).
    /// <para>
    /// A command the kernel refuses (a stale plan id, a plan that finished a tick ago) comes back
    /// as <see cref="CommandRefused"/> and the game runs on: it is an answer, not a fault. Anything
    /// else the kernel throws is a broken invariant and faults the driver, as a tick that throws
    /// does. Refuses once the kernel has already faulted, like every other entry point.
    /// </para>
    /// </summary>
    public CommandResult Execute(Command command)
    {
        if (FaultMessage is not null)
        {
            return new CommandRefused(command, FaultMessage, Array.Empty<DraftIssue>());
        }

        CommandResult result;
        try
        {
            result = _engine.Execute(command);
        }
        catch (Exception e)
        {
            Fault(e);
            return new CommandRefused(command, FaultMessage!, Array.Empty<DraftIssue>());
        }

        // A plan whose every task was already satisfied, or one a cancel just finished, can
        // complete inside the command itself, so this is checked here too rather than only after
        // Advance.
        CheckPlanCompleted();
        return result;
    }

    /// <summary>
    /// Single path for every _speedIndex mutation. Keeps _resumeIndex pointed at the most
    /// recent non-zero speed, so TogglePause always has a meaningful multiplier to return to.
    /// </summary>
    private void SetIndex(int index)
    {
        _speedIndex = index;
        if (index != 0)
        {
            _resumeIndex = index;
        }
    }

    private void SafeAdvance(long ticks)
    {
        try
        {
            _engine.Advance(ticks);
        }
        catch (Exception e)
        {
            Fault(e);
            return;
        }

        CheckPlanCompleted();
    }

    private void Fault(Exception e)
    {
        _speedIndex = 0;
        FaultMessage = $"Simulation fault at tick {_engine.Snapshot.Tick}: {e.GetType().Name}: {e.Message}";
        GD.PushError(FaultMessage);
    }

    /// <summary>
    /// Watches <see cref="WorldSnapshot.RecentEvents"/> for a <see cref="EventCode.PlanCompleted"/>
    /// the player has not seen yet, and pauses on the first one found: the plan's completion is the
    /// player's next turn, not something to keep running past. Only the events emitted since the
    /// last look are scanned — <see cref="WorldSnapshot.TotalEventsEmitted"/> against the window's
    /// own length says how many of the trailing entries are new — so a completion already reacted
    /// to is never paused on twice, and a big multi-tick <see cref="Advance"/> that emitted more
    /// events than the window holds still catches the completion as long as it is still in the
    /// window when this runs.
    /// </summary>
    private void CheckPlanCompleted()
    {
        var snapshot = _engine.Snapshot;
        var newCount = snapshot.TotalEventsEmitted - _lastCheckedEventTotal;
        _lastCheckedEventTotal = snapshot.TotalEventsEmitted;

        if (newCount <= 0)
        {
            return;
        }

        var events = snapshot.RecentEvents;
        var start = Math.Max(0, events.Count - (int)Math.Min(newCount, events.Count));

        for (var i = start; i < events.Count; i++)
        {
            if (events[i].Code == EventCode.PlanCompleted)
            {
                PauseForCompletion();
                return;
            }
        }
    }

    /// <summary>
    /// Sets the speed index directly rather than through <see cref="TogglePause"/>, which would
    /// resume the sim instead if it happened to already be paused — the driver is not asking
    /// whichever state is not current, it is asking for paused specifically.
    /// </summary>
    private void PauseForCompletion()
    {
        if (!IsPaused)
        {
            SetIndex(0);
            _accumulator = 0;
        }
    }
}
