using Dimenship.Core.Programs;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>
    /// One tick of one line: unload the head, advance the belt, load the tail.
    /// <para>
    /// The order is the determinism contract. Unloading first is what lets cargo reaching the
    /// destination this tick be there for the production phase that follows; advancing before
    /// loading is what makes a slot loaded now sit <c>LengthTicks</c> away from being deliverable
    /// rather than one short of it.
    /// </para>
    /// </summary>
    private void StepHauler(TransportInstance hauler)
    {
        hauler.BlockReason = null;

        if (!Unload(hauler))
        {
            // The head could not be emptied, so nothing behind it moves either: a belt is rigid,
            // not an accumulator. The line keeps exactly the fill it had and takes nothing on,
            // however little that fill is. The other direction of a two-way link is a different
            // line with a different belt and is untouched by this.
            return;
        }

        Advance(hauler);
        Load(hauler);
    }

    /// <summary>
    /// Empties the head slot into the destination, as far as it fits, and reports whether the belt
    /// may move. Anything left on the head freezes the line for this tick.
    /// <para>
    /// Conditions are deliberately not consulted here. The spec's carve-out — a run already in
    /// flight is never re-gated — now has the transfer analogue it once lacked, because cargo on a
    /// belt <i>is</i> in flight: a condition that turns false stops the next pickup and never
    /// strands what is already travelling.
    /// </para>
    /// </summary>
    private bool Unload(TransportInstance hauler)
    {
        if (hauler.Belt[0] is not { } head)
        {
            return true;
        }

        // RoomForDelivery, not Room: the reservation a facility's next run needs is held back from
        // transport at the far end of the belt exactly as it was when transport arrived instantly.
        var quantity = Math.Min(head.Quantity, RoomForDelivery(hauler.To, head.Item));
        if (quantity > 0)
        {
            _storage.Deposit(hauler.To, head.Item, quantity);
            head.Quantity -= quantity;
            hauler.DeliveredLastTick += quantity;
            Credit(hauler, head.Task, quantity);
            AllocateArrival(head.Task, hauler.To, head.Item, quantity);
        }

        if (head.Quantity > 0)
        {
            Freeze(hauler);
            return false;
        }

        hauler.Belt[0] = null;
        return true;
    }

    /// <summary>
    /// Credits a delivery to the transfer that loaded it, and finishes that transfer when the last
    /// of it has arrived. The task is found by id rather than taken from the line's current task,
    /// because by the time cargo lands the line has usually moved on to the next haul.
    /// </summary>
    private void Credit(TransportInstance hauler, TaskId id, long quantity)
    {
        if (State.Tasks.Task(id) is not { } task)
        {
            return;
        }

        task.MovedQuantity += quantity;
        Progressed(task);

        if (task.Transfer.Quantity is not { } target || task.MovedQuantity < target)
        {
            return;
        }

        task.State = TaskState.Complete;
        task.CompletedAtTick = State.Clock.Tick;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);

        if (hauler.Current == task.Id)
        {
            hauler.Current = null;
        }

        Emit(EventCategory.Logistics, EventCode.TransferCompleted, hauler.Id.Value,
            new Dictionary<string, long>
            {
                ["task"] = task.Id.Value,
                ["moved"] = task.MovedQuantity,
            });
        Retire(hauler.Queue, task.Id);
    }

    /// <summary>
    /// Reports the whole line blocked on its destination. Every transfer still waiting to be
    /// picked up is postponed for the same reason, because under a frozen belt that is the truth:
    /// none of them can be loaded, whatever their own source looks like.
    /// </summary>
    private void Freeze(TransportInstance hauler)
    {
        // The one place a line reports itself blocked, and the only place its BlockReason is set.
        // A belt can be frozen with nothing left in the queue to postpone, and the line is blocked
        // all the same: the cargo stuck on the head is the fault, not the queue behind it.
        hauler.BlockReason = PostponeReason.DestinationFull;

        var pending = 0;
        foreach (var task in Queued(hauler))
        {
            if (task.IsFinished || FullyLoaded(task))
            {
                continue;
            }

            pending++;
            Postpone(hauler, task, PostponeReason.DestinationFull);
        }

        if (pending > 0 && hauler.Status != ExecutorStatus.AllQueuedTasksBlocked)
        {
            Emit(EventCategory.Logistics, EventCode.AllTasksBlocked, hauler.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        hauler.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    /// <summary>
    /// Moves the belt one slot toward the destination. The head is empty by the time this runs, so
    /// nothing is advanced over, and the tail is freed for this tick's intake.
    /// </summary>
    private static void Advance(TransportInstance hauler)
    {
        hauler.Belt.RemoveAt(0);
        hauler.Belt.Add(null);
    }

    /// <summary>
    /// Picks up for at most one transfer, into the tail slot. Selection is unchanged from when a
    /// line moved material outright: the transfer in hand first, then queue order.
    /// </summary>
    private void Load(TransportInstance hauler)
    {
        // Priority picks which transfer the free tail slot takes (D1, Decision 3, carried over to
        // lines): only the highest priority among transfers that could load now is considered,
        // and within it the transfer in hand, then queue order. Cargo already aboard is never
        // touched, and a transfer gives up the line exactly when it is entirely on the belt.
        if (TopReadyPriority(hauler) is { } tier)
        {
            // Continue the transfer already in hand before looking at anything else, for the same
            // reason a facility prefers its loaded configuration: finishing beats starting.
            TaskId? chosen = CurrentOf(hauler) is { } current && !current.IsFinished
                && PriorityOf(current) == tier && TryLoad(hauler, current)
                    ? current.Id
                    : null;

            if (chosen is null)
            {
                foreach (var task in Queued(hauler))
                {
                    if (!task.IsFinished && PriorityOf(task) == tier && TryLoad(hauler, task))
                    {
                        chosen = task.Id;
                        break;
                    }
                }
            }

            if (chosen is { } winner)
            {
                RecordPassedOver(hauler, tier, winner);
                return;
            }
        }

        var pending = 0;
        foreach (var task in Queued(hauler))
        {
            // A transfer entirely on the belt is neither finished nor blocked — it is travelling.
            // Postponing it would report a stall that is not happening.
            if (task.IsFinished || FullyLoaded(task))
            {
                continue;
            }

            pending++;
            ReadyToLoad(hauler, task, out _, out var reason);
            Postpone(hauler, task, reason);
        }

        // Whether there was anything to pick up or not, a line still holding cargo is working: it
        // is carrying what it has toward a destination that is taking it. Only an empty belt is a
        // line doing nothing.
        if (hauler.CargoQuantity > 0)
        {
            hauler.Status = ExecutorStatus.RunningTask;
            return;
        }

        hauler.Current = null;

        // Queued work that could not be picked up is not this line being blocked. Blocked means
        // cargo aboard that the destination will not take; this belt is empty, so the line is
        // stopping nothing and has nothing stuck on it. Reporting a fault here would light up
        // every line downstream of an empty storage — a shortage the storage itself already
        // reports, and one the line has no part in.
        hauler.Status = pending > 0
            ? ExecutorStatus.NothingToCarry
            : ExecutorStatus.NoTasksQueued;
    }
    /// <summary>
    /// The highest priority among this line's transfers that could load now, if any. A transfer
    /// entirely aboard is travelling, not waiting to load, so it takes no part.
    /// </summary>
    private Priority? TopReadyPriority(TransportInstance hauler) =>
        TopReadyPriority(
            hauler, Loadable,
            (TaskInstance task, out PostponeReason reason) => ReadyToLoad(hauler, task, out _, out reason), null);

    /// <summary>
    /// The line's passed-over transfers (see the shared overload). Every reason a line records,
    /// Outranked included, is a logistics event, as <see cref="Postpone(TransportInstance, TaskInstance, PostponeReason)"/>
    /// files it.
    /// </summary>
    private void RecordPassedOver(TransportInstance hauler, Priority tier, TaskId chosen) =>
        RecordPassedOver(
            hauler, tier, chosen, Loadable,
            (TaskInstance task, out PostponeReason reason) => ReadyToLoad(hauler, task, out _, out reason),
            _ => EventCategory.Logistics);

    /// <summary>A transfer still to load: not finished, and not yet entirely aboard.</summary>
    private static bool Loadable(TaskInstance task) => !task.IsFinished && !FullyLoaded(task);

    /// <summary>True when every unit a transfer asked for is on the belt or past it.</summary>
    private static bool FullyLoaded(TaskInstance task) =>
        task.Transfer.Quantity is { } target && task.LoadedQuantity >= target;

    /// <summary>
    /// Conditions and physical readiness together, the transport counterpart of
    /// <see cref="ReadyToStart"/>, and evaluated on every tick a haul picks up.
    /// <para>
    /// Gating pickup and not delivery is the transfer form of the spec's carve-out for a run in
    /// flight — see <see cref="Unload"/>. Gating only the first tick of a haul instead would leave
    /// every later tick of a long pickup unconditioned, which is the opposite of what a condition
    /// is for.
    /// </para>
    /// </summary>
    private bool ReadyToLoad(
        TransportInstance hauler, TaskInstance task, out long quantity, out PostponeReason reason)
    {
        var conditionsMet = ConditionEvaluator.AllMet(task.Script.Conditions, State);
        var canLoad = CanLoad(hauler, task, out quantity, out var physical);
        var held = IsHeld(task);
        if (conditionsMet && canLoad && !held)
        {
            reason = default;
            return true;
        }

        var reasons = new List<PostponeReason>();
        if (held)
        {
            reasons.Add(PostponeReason.SafetyLock);
        }

        if (!conditionsMet)
        {
            reasons.Add(PostponeReason.ConditionNotMet);
        }

        if (!canLoad)
        {
            reasons.Add(physical);
        }

        quantity = 0;
        reason = PostponeReasons.RootCause(reasons)!.Value;
        return false;
    }

    private bool CanLoad(TransportInstance hauler, TaskInstance task, out long quantity, out PostponeReason reason)
    {
        // A standing order is bounded by what is at the source, and by nothing else. The
        // destination does not appear here at all: room is asked for at the far end of the belt, a
        // whole LengthTicks later, and refusing to pick up now because the destination happens to
        // be full now would leave the belt empty exactly when it should be filling.
        var outstanding = task.Transfer.Quantity is { } requested
            ? requested - task.LoadedQuantity
            : long.MaxValue;
        var atSource = Spendable(task, task.Transfer.From, task.Transfer.Item);

        quantity = Math.Min(Math.Min(Throughput(hauler), outstanding), atSource);

        if (quantity > 0)
        {
            reason = PostponeReason.SafetyLock;
            return true;
        }

        reason = Available(task.Transfer.From, task.Transfer.Item) > 0
            ? PostponeReason.MaterialClaimed
            : PostponeReason.InsufficientSourceMaterial;
        return false;
    }

    private bool TryLoad(TransportInstance hauler, TaskInstance task)
    {
        if (!ReadyToLoad(hauler, task, out var quantity, out _))
        {
            return false;
        }

        _storage.Withdraw(task.Transfer.From, task.Transfer.Item, quantity);
        ConsumeHeld(task, task.Transfer.From, task.Transfer.Item, quantity);
        hauler.Belt[^1] = new BeltSlot
        {
            Task = task.Id,
            Item = task.Transfer.Item,
            Quantity = quantity,
        };

        task.LoadedQuantity += quantity;
        Progressed(task);
        hauler.LoadedLastTick += quantity;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        task.FirstStartedAtTick ??= State.Clock.Tick;
        hauler.Current = task.Id;
        hauler.Status = ExecutorStatus.RunningTask;

        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Started, null))
        {
            var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
            if (task.Transfer.Quantity is { } requested)
            {
                data["quantity"] = requested;
            }

            Emit(EventCategory.Logistics, EventCode.TransferStarted, hauler.Id.Value, data);
        }

        // The line takes the next transfer on the moment this one is entirely aboard. Holding it
        // as current until delivery would park the belt for a whole LengthTicks between two hauls,
        // which is the cost having a belt exists to avoid.
        if (FullyLoaded(task))
        {
            hauler.Current = null;
        }

        return true;
    }

    /// <summary>
    /// Records why one transfer could not be picked up. It deliberately does not touch the line’s
    /// own <see cref="TransportInstance.BlockReason"/>: a postponed transfer is a fact about that
    /// transfer, and only <see cref="Freeze"/> — cargo aboard the destination will not take — is a
    /// fact about the line. Setting it here is what used to report an empty line as blocked.
    /// </summary>
    private void Postpone(TransportInstance hauler, TaskInstance task, PostponeReason reason) =>
        PostponeTask(hauler.Id, task, reason, EventCategory.Logistics);
}
