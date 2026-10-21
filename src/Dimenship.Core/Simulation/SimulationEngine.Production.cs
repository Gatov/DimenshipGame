using Dimenship.Core.Programs;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>
    /// One whole construction unit in milli-units. Commissioning withdraws exactly this; more in
    /// local storage is left for a later slot or a failed haul, never consumed as change. Public
    /// because an order to build a slot must ask for exactly this much (<c>ConstructionOrder</c>).
    /// </summary>
    public const long WholeConstructionUnit = Units.MilliPerUnit;

    /// <summary>
    /// After transport, before production: unbuilt facilities whose local storage holds a whole
    /// construction unit become Built. Local storage stands in for an upgrade socket until sockets
    /// exist — nothing here builds a line.
    /// </summary>
    private void CommissionFacilities()
    {
        foreach (var facility in State.Vessel.Facilities)
        {
            if (facility.Built)
            {
                continue;
            }

            if (Archetype(facility).ConstructionUnit is not { } unit)
            {
                continue;
            }

            if (Free(facility.LocalStorage, unit) < WholeConstructionUnit)
            {
                continue;
            }

            Withdraw(facility.LocalStorage, unit, WholeConstructionUnit);
            facility.Built = true;
            Emit(EventCategory.Production, EventCode.FacilityBuilt, facility.Id.Value, SimEvent.NoData);
        }
    }

    private void StepProducer(FacilityInstance executor)
    {
        executor.BlockReason = null;

        if (executor.SwitchOverRemaining > 0)
        {
            if (!TryAbandonSwitchOver(executor))
            {
                AdvanceSwitchOver(executor);
            }

            return;
        }

        // A finished run whose output would not fit holds the facility. Neither the work nor the
        // consumed inputs are lost; the run is deposited as soon as room appears.
        if (CurrentOf(executor) is { RunAwaitingDeposit: true } holding)
        {
            if (TryDeposit(executor, holding))
            {
                executor.Status = ExecutorStatus.RunningTask;
            }

            return;
        }

        if (CurrentOf(executor) is { RunActive: true } running)
        {
            AdvanceRun(executor, running);
            return;
        }

        SelectAndStart(executor);
    }

    private void SelectAndStart(FacilityInstance executor)
    {
        // Priority ranks first (D1, Decision 2): only the highest priority among tasks that can
        // start is considered, and within that tier today's three steps keep their order exactly.
        // An unready task never enters a tier, so an urgent order still waiting for its ore does
        // not hold the machine. With every task at the default, the tier is everything ready and
        // the steps choose what they always chose.
        if (TopReadyPriority(executor) is { } tier)
        {
            Select(executor, tier);
            RecordPassedOver(executor, tier);
            return;
        }

        // 4. Nothing can run. Every unfinished task records why, which is what turns "the vessel
        //    stopped" into "the vessel stopped because these three things are missing".
        var pending = 0;
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished)
            {
                continue;
            }

            pending++;
            ReadyToStart(executor, task, out var reason);
            Postpone(executor, task, reason);
        }

        if (pending == 0)
        {
            executor.Status = ExecutorStatus.NoTasksQueued;
            executor.Current = null;
            return;
        }

        if (executor.Status != ExecutorStatus.AllQueuedTasksBlocked)
        {
            Emit(EventCategory.Production, EventCode.AllTasksBlocked, executor.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    /// <summary>
    /// Today's three selection steps, run within one priority tier. The caller has established
    /// that some task in the tier can start, so one of the steps always takes it.
    /// </summary>
    private void Select(FacilityInstance executor, Priority tier)
    {
        // 1. Continue the current task when its next run can start. Preferring the work already
        //    configured is what keeps a facility producing instead of reconfiguring.
        if (CurrentOf(executor) is { } current && !current.IsFinished && PriorityOf(current) == tier
            && ReadyToStart(executor, current, out _))
        {
            StartRun(executor, current);
            return;
        }

        // 2. Any queued task using the configuration already loaded.
        if (executor.Configured is { } configured)
        {
            foreach (var task in Queued(executor))
            {
                if (!task.IsFinished && PriorityOf(task) == tier && task.Produce.Schematic == configured
                    && ReadyToStart(executor, task, out _))
                {
                    executor.Current = task.Id;
                    StartRun(executor, task);
                    return;
                }
            }
        }

        // 3. A runnable task on a different schematic, which costs a reconfiguration. A facility
        //    that has never been configured has nothing to tear down and pays nothing.
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished || PriorityOf(task) != tier || !ReadyToStart(executor, task, out _))
            {
                continue;
            }

            executor.Current = task.Id;

            if (executor.Configured is null || SwitchOverTicks(executor) <= 0)
            {
                executor.Configured = task.Produce.Schematic;
                StartRun(executor, task);
                return;
            }

            BeginSwitchOver(executor, task);
            return;
        }
    }

    private void BeginSwitchOver(FacilityInstance executor, TaskInstance task)
    {
        executor.SwitchOverRemaining = SwitchOverTicks(executor);
        executor.SwitchTarget = task.Id;
        Emit(EventCategory.Production, EventCode.SwitchOverStarted, executor.Id.Value,
            new Dictionary<string, long>
            {
                ["task"] = task.Id.Value,
                ["ticks"] = SwitchOverTicks(executor),
            });

        // The tick that decides to reconfigure is the first tick of the reconfiguration, not
        // a free one spent deciding. Otherwise a switch-over always costs its ticks plus one.
        AdvanceSwitchOver(executor);
    }

    /// <summary>
    /// The highest priority among this facility's tasks that could start now, if any, optionally
    /// only among priorities strictly above <paramref name="above"/>.
    /// </summary>
    private Priority? TopReadyPriority(FacilityInstance executor, Priority? above = null) =>
        TopReadyPriority(
            executor, task => !task.IsFinished,
            (TaskInstance task, out PostponeReason reason) => ReadyToStart(executor, task, out reason), above);

    /// <summary>
    /// The facility's passed-over tasks (see the shared overload). The facility's current task is
    /// the one selection chose, by every path that reaches here: a run started, a switch-over
    /// begun, or one retargeted. Outranked is a production event, and a physical reason files by
    /// its own category (<see cref="CategoryFor"/>), so a task short of power is a power event.
    /// </summary>
    private void RecordPassedOver(FacilityInstance executor, Priority tier) =>
        RecordPassedOver(
            executor, tier, executor.Current, task => !task.IsFinished,
            (TaskInstance task, out PostponeReason reason) => ReadyToStart(executor, task, out reason),
            CategoryFor);

    /// <summary>
    /// D1, Decision 4: a switch-over commits time and no material, so a ready task of strictly
    /// higher priority than its target may redirect it. The new task's schematic decides the cost.
    /// The target's schematic retargets, and the countdown runs on. The setup still loaded
    /// cancels, and the run starts this tick. Anything else restarts the full countdown. Within the
    /// winning tier a task on the loaded setup is preferred, then queue order, as in selection.
    /// Returns false, having changed nothing, when nothing outranks the target.
    /// </summary>
    private bool TryAbandonSwitchOver(FacilityInstance executor)
    {
        if (executor.SwitchTarget is not { } targetId || State.Tasks.Task(targetId) is not { } target)
        {
            return false;
        }

        if (TopReadyPriority(executor, above: PriorityOf(target)) is not { } tier)
        {
            return false;
        }

        TaskInstance? chosen = null;
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished || PriorityOf(task) != tier || !ReadyToStart(executor, task, out _))
            {
                continue;
            }

            if (task.Produce.Schematic == executor.Configured)
            {
                chosen = task;
                break;
            }

            chosen ??= task;
        }

        var replacement = chosen!;
        executor.Current = replacement.Id;

        if (replacement.Produce.Schematic == target.Produce.Schematic)
        {
            executor.SwitchTarget = replacement.Id;
            AdvanceSwitchOver(executor);

            // A cancelled target waited for its switch-over; it no longer has one.
            FinishIfCutShort(target);
            RecordPassedOver(executor, tier);
            return true;
        }

        Emit(EventCategory.Production, EventCode.SwitchOverAbandoned, executor.Id.Value,
            new Dictionary<string, long>
            {
                ["task"] = target.Id.Value,
                ["for"] = replacement.Id.Value,
                ["remaining"] = executor.SwitchOverRemaining,
            });

        if (replacement.Produce.Schematic == executor.Configured)
        {
            // Configured never changes while a switch-over runs, which is what makes cancelling
            // honest: the machine is still set up for this, and only the elapsed ticks are lost.
            executor.SwitchOverRemaining = 0;
            executor.SwitchTarget = null;
            StartRun(executor, replacement);
        }
        else
        {
            BeginSwitchOver(executor, replacement);
        }

        FinishIfCutShort(target);
        RecordPassedOver(executor, tier);
        return true;
    }

    /// <summary>
    /// Conditions and physical readiness together. Both are evaluated so
    /// <see cref="PostponeReasons.RootCause"/> can pick — ConditionNotMet is last, so missing
    /// inputs beat a false gate. An early return on conditions alone would hide the ore.
    /// </summary>
    private bool ReadyToStart(FacilityInstance executor, TaskInstance task, out PostponeReason reason)
    {
        var reasons = new List<PostponeReason>();
        if (IsHeld(task))
        {
            reasons.Add(PostponeReason.SafetyLock);
        }

        if (!ConditionEvaluator.AllMet(task.Script.Conditions, State))
        {
            reasons.Add(PostponeReason.ConditionNotMet);
        }

        if (!CanStart(executor, task, out var physical))
        {
            reasons.Add(physical);
        }

        if (reasons.Count == 0)
        {
            reason = default;
            return true;
        }

        reason = PostponeReasons.RootCause(reasons)!.Value;
        return false;
    }

    private void AdvanceSwitchOver(FacilityInstance executor)
    {
        executor.SwitchOverRemaining--;
        executor.Status = ExecutorStatus.SwitchingOver;

        if (executor.SwitchOverRemaining > 0)
        {
            return;
        }

        var target = State.Tasks.Task(executor.SwitchTarget!.Value)!;
        executor.Configured = target.Produce.Schematic;
        executor.SwitchTarget = null;
        Emit(EventCategory.Production, EventCode.SwitchOverCompleted, executor.Id.Value,
            new Dictionary<string, long> { ["task"] = target.Id.Value });

        // A target cancelled while the facility switched toward it finishes now, with the setup
        // loaded: the switch-over completed, as D1 requires, and there is nothing left to run.
        FinishIfCutShort(target);
    }

    private bool CanStart(FacilityInstance executor, TaskInstance task, out PostponeReason reason)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            if (Spendable(task, storage, input.Item) < input.Quantity)
            {
                reason = Available(storage, input.Item) >= input.Quantity
                    ? PostponeReason.MaterialClaimed
                    : PostponeReason.InsufficientInputMaterial;
                return false;
            }
        }

        // A facility whose buffer is not in the world has nowhere to put anything, which is a
        // content error rather than a shortage; it reports the blockage it actually has.
        if (!_storagesById.TryGetValue(storage, out var instance))
        {
            reason = PostponeReason.DestinationFull;
            return false;
        }

        // Room is checked before anything is consumed. A facility that cannot place its output
        // must not shred its input for nothing.
        //
        // It is measured against the volume the run's own inputs are about to free, not against
        // the volume they still occupy. A buffer full of the metal a run consumes has no room for
        // the components that metal becomes, so checking it before the withdrawal would deadlock
        // every facility whose feed line kept its buffer full. A run whose output is bulkier than
        // its inputs still needs the difference to be free, which this says and the previous
        // wording did not.
        if (RoomAfterConsuming(instance, schematic) < schematic.Output.Quantity)
        {
            reason = PostponeReason.DestinationFull;
            return false;
        }

        reason = PostponeReason.SafetyLock;
        return true;
    }

    private void StartRun(FacilityInstance executor, TaskInstance task)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            Withdraw(storage, input.Item, input.Quantity);
            ConsumeHeld(task, storage, input.Item, input.Quantity);
        }

        executor.Current = task.Id;
        task.RunActive = true;
        task.WorkDoneThisRun = 0;
        task.EnergyChargedThisRun = 0;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        task.FirstStartedAtTick ??= State.Clock.Tick;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Started, null);
        Progressed(task);

        var started = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["run"] = task.CompletedRuns + 1,
        };
        if (task.Produce.Runs is { } requestedRuns)
        {
            started["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunStarted, executor.Id.Value, started);

        AdvanceRun(executor, task);
    }

    private void AdvanceRun(FacilityInstance executor, TaskInstance task)
    {
        var effort = Catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
        var charge = RunCharge(executor, task, out var work, out var targetTotal);

        // A run already in progress at the start of the tick was granted or refused power before
        // any facility stepped, by priority (GrantPowerToRunsInProgress). A run starting this tick
        // draws on what those grants left, in visit order, exactly as every run once did.
        long? reserve = null;
        if (_powerGrants.Remove(task.Id, out var grant))
        {
            if (grant.Granted)
            {
                _reservedPower -= charge;
            }
            else
            {
                reserve = grant.Reserve;
            }
        }
        else if (State.Vessel.Energy.DrawLastTick + _reservedPower + charge > State.Vessel.Energy.Capacity)
        {
            reserve = State.Vessel.Energy.Capacity - State.Vessel.Energy.DrawLastTick - _reservedPower;
        }

        if (reserve is { } left)
        {
            _starvedThisTick = true;
            Postpone(executor, task, PostponeReason.InsufficientEnergy, new Dictionary<string, long>
            {
                ["required"] = charge,
                ["reserve"] = left,
            });
            executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
            return;
        }

        State.Vessel.Energy.DrawLastTick += charge;
        executor.PowerDrawLastTick += charge;
        task.EnergyChargedThisRun = targetTotal;
        task.WorkDoneThisRun += work;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        executor.Status = ExecutorStatus.RunningTask;

        if (task.WorkDoneThisRun >= effort)
        {
            TryDeposit(executor, task);
        }
    }

    /// <summary>
    /// This tick's energy charge for a run, and the work it buys. Charged cumulatively rather than
    /// as a per-tick slice: the final tick's work equals the full effort, so the target lands
    /// exactly on the schematic's energy and the rounding remainder settles itself.
    /// </summary>
    private long RunCharge(FacilityInstance executor, TaskInstance task, out long work, out long targetTotal)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var effort = schematic.EffortPerRun.Value;
        work = Math.Min(WorkRate(executor), effort - task.WorkDoneThisRun);
        targetTotal = schematic.EnergyPerRun.Value * (task.WorkDoneThisRun + work) / effort;
        return targetTotal - task.EnergyChargedThisRun;
    }

    private bool TryDeposit(FacilityInstance executor, TaskInstance task)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        // The plain room, not the deliverable room: the reservation a transport line is kept out
        // of is being held for exactly this deposit.
        if (Room(storage, schematic.Output.Item) < schematic.Output.Quantity)
        {
            task.RunAwaitingDeposit = true;
            Postpone(executor, task, PostponeReason.DestinationFull, new Dictionary<string, long>
            {
                ["room"] = Room(storage, schematic.Output.Item),
                ["need"] = schematic.Output.Quantity,
            });
            executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
            return false;
        }

        Deposit(storage, schematic.Output.Item, schematic.Output.Quantity);
        task.RunActive = false;
        task.RunAwaitingDeposit = false;
        task.WorkDoneThisRun = 0;
        task.EnergyChargedThisRun = 0;
        task.CompletedRuns++;
        Progressed(task);
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.RunCompleted, null);

        var done = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["done"] = task.CompletedRuns,
        };
        if (task.Produce.Runs is { } requestedRuns)
        {
            done["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunCompleted, executor.Id.Value, done);

        if (task.Produce.Runs is { } target && task.CompletedRuns >= target)
        {
            task.State = TaskState.Complete;
            task.CompletedAtTick = State.Clock.Tick;
            task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);
            executor.Current = null;
            Emit(EventCategory.Production, EventCode.TaskCompleted, executor.Id.Value,
                new Dictionary<string, long> { ["task"] = task.Id.Value });
            Retire(executor.Queue, task.Id);
        }

        AllocateArrival(task.Id, storage, schematic.Output.Item, schematic.Output.Quantity);
        return true;
    }
}
