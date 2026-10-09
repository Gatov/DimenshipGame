using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

/// <summary>
/// One change to the world other than the passage of time, as a value. The shell, the replay
/// harness and every future controller change the world by handing one of these to
/// <see cref="SimulationEngine.Execute"/> and by nothing else, which is what keeps a player from
/// having a command a program can never have (C0;
/// <c>docs/superpowers/specs/2026-10-09-kernel-command-surface-design.md</c>).
/// <para>
/// A record rather than a method on an interface, because a record can be put in a script, logged
/// and compared. A string verb with an argument bag was rejected: it defers every type error to run
/// time, and the parser belongs to whichever of the console or the program language lands first.
/// </para>
/// <para>
/// Recovery (K7) and protecting a reserve are deliberately absent until the tickets that build
/// them; so is pre-emptive setup, which D1 makes a command but nothing yet asks for.
/// </para>
/// </summary>
public abstract record Command;

/// <summary>
/// Plans a goal against the live world through the draft path and commits it. A controller's way
/// to add demand: it has no draft to edit.
/// </summary>
public sealed record OrderGoal(ItemAmount Goal, StorageId? Destination = null, ExecutorId? AssemblyTarget = null)
    : Command;

/// <summary>
/// Commits a plan already approved. The composer's way to add demand, because a player's draft can
/// carry edits that planning the goal again would lose.
/// </summary>
public sealed record CommitPlan(ProductionPlan Plan) : Command;

/// <summary>Queues one task by hand, with no plan.</summary>
public sealed record QueueTask(TaskScript Script, ExecutorId Executor) : Command;

/// <summary>Sets a plan's priority, which every task it spawned reads live (K6a).</summary>
public sealed record SetPlanPriority(PlanId Plan, Priority Priority) : Command;

/// <summary>Sets a hand-queued task's priority. A plan's task is refused, naming the plan.</summary>
public sealed record SetTaskPriority(TaskId Task, Priority Priority) : Command;

/// <summary>Holds a plan: its unstarted work stops, and what is physically committed finishes (K6c).</summary>
public sealed record HoldPlan(PlanId Plan) : Command;

/// <summary>Releases a held plan, which is offered free stock at once.</summary>
public sealed record ReleasePlan(PlanId Plan) : Command;

/// <summary>Cancels a plan by cutting its work back to what has started; its holdings go back to allocation.</summary>
public sealed record CancelPlan(PlanId Plan) : Command;

/// <summary>Replans a plan for a new goal quantity under the same id, priority and held flag.</summary>
public sealed record AmendPlan(PlanId Plan, long Quantity) : Command;

/// <summary>Holds a task queued by hand.</summary>
public sealed record HoldTask(TaskId Task) : Command;

/// <summary>Releases a held task queued by hand.</summary>
public sealed record ReleaseTask(TaskId Task) : Command;

/// <summary>Cancels a task queued by hand, cutting it back to what has started.</summary>
public sealed record CancelTask(TaskId Task) : Command;

/// <summary>Gives up some of a plan's holding, which goes back through the allocation order (K6b).</summary>
public sealed record RelinquishStock(PlanId Plan, StorageId Storage, ItemId Item, long Quantity) : Command;

/// <summary>Moves held stock between plans, up to what the receiver is short (K6b).</summary>
public sealed record ReassignStock(PlanId From, PlanId To, StorageId Storage, ItemId Item, long Quantity) : Command;

/// <summary>What became of a command.</summary>
public abstract record CommandResult(Command Command)
{
    public bool Accepted => this is CommandAccepted;
}

/// <summary>
/// The command was applied. <paramref name="Plan"/> is the plan it committed or acted on, and
/// <paramref name="Tasks"/> the tasks it queued, empty when it queued none.
/// </summary>
public sealed record CommandAccepted(Command Command, PlanId? Plan, IReadOnlyList<TaskId> Tasks)
    : CommandResult(Command);

/// <summary>
/// The command made no sense against this world, and changed nothing. <paramref name="Reason"/> is
/// the kernel's sentence; <paramref name="Issues"/> are a refused draft's, empty otherwise. A
/// refusal is an answer, not a fault: a controller acting on a world one tick stale is ordinary.
/// </summary>
public sealed record CommandRefused(Command Command, string Reason, IReadOnlyList<DraftIssue> Issues)
    : CommandResult(Command);
