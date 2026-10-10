using System.Security.Cryptography;
using System.Text;
using Dimenship.Core.Content;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;

namespace Dimenship.Replay;

/// <summary>What became of one scripted demand.</summary>
/// <param name="CommittedAtTick">Null when approval refused it.</param>
/// <param name="RefusedIssues">How many issues the refusal named; zero when it committed.</param>
/// <param name="Shortfall">
/// What the committed plan could not supply at all, summed over its unplannable entries. The plan
/// still commits and runs; this much of the goal never had a source.
/// </param>
/// <param name="ReadyAtTick">
/// The tick the plan's last task finished, or null when it had not by the end of the run. A plan
/// with no tasks — the goal was already in stock — is ready the tick it was committed.
/// </param>
public sealed record DemandOutcome(
    ScriptedDemand Demand,
    long? CommittedAtTick,
    int RefusedIssues,
    long Shortfall,
    long? ReadyAtTick)
{
    /// <summary>Ticks from commit to readiness, or null when either has not happened.</summary>
    public long? Readiness => ReadyAtTick - CommittedAtTick;

    /// <summary>
    /// The goal quantity this demand delivered by the end of the run: the part of the goal that had
    /// a source, once its plan is ready, and nothing before then. A plan is all or nothing here
    /// because its tasks are, and a plan half done has delivered nothing it promised.
    /// </summary>
    public long Delivered => ReadyAtTick is null ? 0 : Math.Max(0, Demand.Goal.Quantity - Shortfall);
}

/// <summary>
/// One task of a demand's plan that had not completed when the run ended, as the engine last
/// described it. This is the table that explains a "not ready": which stage the plan stopped at, and
/// whether that task never started, was waiting, or was postponed for a named reason.
/// <paramref name="Behind"/> is who that reason waits behind (K8): another demand, by its id, or a
/// task queued by hand. Null when the reason names no other party.
/// </summary>
public sealed record UnfinishedTask(
    string Demand, TaskId Task, ExecutorId Executor, string Work, TaskState State, PostponeReason? Reason,
    string? Behind);

/// <summary>
/// One waiting-plan alert (K8): the demand whose plan starved, who it waited behind, the reason,
/// and when it was raised and cleared. <paramref name="ClearedAtTick"/> is null when it was still
/// raised at the end of the run.
/// </summary>
public sealed record WaitingAlert(
    string Demand, string Behind, PostponeReason? Reason, long RaisedAtTick, long? ClearedAtTick);

/// <summary>
/// What became of one scripted command. <paramref name="Refusal"/> is null when the kernel
/// accepted it, and otherwise the kernel's sentence, or the harness's when the demand it named had
/// no plan to act on.
/// </summary>
public sealed record CommandOutcome(ScriptedCommand Command, string? Refusal)
{
    public bool Accepted => Refusal is null;
}

/// <summary>Mean and peak of one reading, sampled once per tick.</summary>
public sealed record Reading(string Subject, long Mean, long Peak);

/// <summary>
/// Switch-overs one facility started, the ticks it spent switching, and how many switch-overs it
/// abandoned for a higher priority (K2). A restart counts in both <see cref="Count"/> and
/// <see cref="Abandoned"/>; a cancel only in <see cref="Abandoned"/>.
/// </summary>
public sealed record Changeovers(ExecutorId Facility, long Count, long Ticks, long Abandoned);

/// <summary>
/// One facility's ticks over the whole run, by the same categories as its utilization window
/// (<see cref="UtilizationWindow.CategoryOf"/>), counted only while it was built.
/// </summary>
public sealed record FacilityTime(ExecutorId Facility, IReadOnlyList<long> TicksByCategory);

/// <summary>
/// The commands of one kind a controller issued (E2), counted rather than listed: a replenishment
/// controller can issue hundreds, and the report is for comparing policies, not replaying them.
/// </summary>
public sealed record ControllerCommands(string Kind, int Accepted, int Refused);

/// <summary>How much of one item the vessel's storages hold when the run ends.</summary>
public sealed record EndStock(ItemId Item, long Amount);

/// <summary>
/// Everything a replay measured. Integers only, ratios in permille, and every list in the
/// declaration order of the content or script it came from.
/// </summary>
public sealed record ReplayResult(
    string Scenario,
    string ContentVersion,
    string Policy,
    long EndTick,
    int Interventions,
    int Assignments,
    IReadOnlyList<DemandOutcome> Demands,
    IReadOnlyList<CommandOutcome> Commands,
    IReadOnlyList<DemandOutcome> ControllerOrders,
    IReadOnlyList<ControllerCommands> ControllerCommands,
    IReadOnlyList<EndStock> EndStock,
    IReadOnlyList<Reading> MaterialTiedUp,
    IReadOnlyList<Reading> SpaceTiedUp,
    IReadOnlyList<Changeovers> Changeovers,
    IReadOnlyList<FacilityTime> FacilityTime,
    IReadOnlyList<UnfinishedTask> Unfinished,
    IReadOnlyList<WaitingAlert> WaitingAlerts,
    string FinalStateSha256);

/// <summary>
/// Runs a script against a fresh campaign and integrates the scheduling design's §7 metrics.
/// <para>
/// Demands go through the same path the Operations composer's APPROVE takes:
/// <see cref="PlanDraftEditor.Create"/>, then <see cref="PlanDraftEditor.Approve"/>, then
/// <see cref="SimulationEngine.Commit"/>. <c>SimulationDriver.Draft</c> and <c>Approve</c> are thin
/// wrappers over the first two, so a harness number and a played session cannot diverge on how a
/// plan was built.
/// </para>
/// <para>
/// The engine is stepped one tick at a time, because completions have to be caught as they happen.
/// The task registry and the journal both retire into bounded windows, so reading them at the end
/// of a long run would find most of the run gone. Events are drained after every tick against
/// <see cref="WorldSnapshot.TotalEventsEmitted"/>. A tick that emitted more than the journal holds
/// throws rather than reporting from a partial record.
/// </para>
/// <para>
/// No wall clock is read and nothing is random, so one script on one content tree gives one
/// report. The report ends with a hash of the final save, which catches a divergence in state that
/// none of the metrics shows.
/// </para>
/// <para>
/// Every change goes through <see cref="SimulationEngine.Execute"/>, the command surface (C0): a
/// demand's commit and its priority, every scripted command, and everything a controller does.
/// </para>
/// <para>
/// The controller (E2) decides once per tick, after that tick's scripted demands and commands and
/// before the engine advances, so it sees what the script just did. With none given, the run is
/// <see cref="QueueOrder"/>, which decides nothing: every report before E2 is that policy's.
/// </para>
/// </summary>
public static class Replay
{
    public static ReplayResult Run(
        ContentCatalog catalog, Scenario scenario, ReplayScript script, IController? controller = null)
    {
        controller ??= new QueueOrder();
        if (scenario.Id != script.Scenario)
        {
            throw new ArgumentException(
                $"The script runs '{script.Scenario}', not '{scenario.Id}'.", nameof(scenario));
        }

        var engine = SimulationEngine.NewGame(catalog, scenario);
        var run = new Accumulators(engine);
        var context = new ControllerContext(run);

        for (var now = 0L; ; now++)
        {
            foreach (var demand in script.Demands)
            {
                if (demand.Tick == now)
                {
                    run.Apply(demand);
                }
            }

            foreach (var command in script.Commands)
            {
                if (command.Tick == now)
                {
                    run.Apply(command);
                }
            }

            controller.Decide(context);
            run.Drain();

            if (now == script.EndTick)
            {
                break;
            }

            engine.Advance(1);
            run.Drain();
            run.Sample();
        }

        var save = WorldSave.Write(catalog, engine.State);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(save))).ToLowerInvariant();

        return new ReplayResult(
            scenario.Id,
            catalog.ContentVersion,
            controller.Name,
            script.EndTick,
            run.Interventions,
            run.Assignments,
            script.Demands.Select(run.Outcome).ToList(),
            run.CommandOutcomes,
            run.Orders.Select(run.Outcome).ToList(),
            run.ControllerCommandCounts(),
            run.Stock(catalog),
            run.Material(catalog),
            run.Space(),
            run.ChangeoverReadings(),
            run.Time(),
            run.Unfinished(script.Demands.Concat(run.Orders)),
            run.WaitingAlerts,
            hash);
    }

    /// <summary>The accumulators for one run. Dictionaries are lookups only; every list written out
    /// is rebuilt in declaration order from the catalog or the snapshot, or kept in the order it
    /// happened.</summary>
    internal sealed class Accumulators
    {
        private readonly SimulationEngine _engine;
        private readonly Dictionary<string, DemandState> _demands = new(StringComparer.Ordinal);
        private readonly Dictionary<PlanId, DemandState> _byPlan = new();
        private readonly Dictionary<ItemId, (long Sum, long Peak)> _material = new();
        private readonly Dictionary<StorageId, (long Sum, long Peak)> _space = new();
        private readonly Dictionary<ExecutorId, long> _switchesStarted = new();
        private readonly Dictionary<ExecutorId, long> _switchesAbandoned = new();
        private readonly Dictionary<ExecutorId, long> _switchingTicks = new();
        private readonly Dictionary<ExecutorId, long[]> _time = new();
        private readonly int _categories = Enum.GetValues<UtilizationCategory>().Length;
        private readonly List<KnownPlan> _known = new();
        private readonly List<(string Kind, int Accepted, int Refused)> _controllerCommands = new();
        private long _seen;
        private long _samples;

        public Accumulators(SimulationEngine engine)
        {
            _engine = engine;
            _seen = engine.Snapshot.TotalEventsEmitted;
        }

        public int Interventions { get; private set; }

        /// <summary>Facility choices a scripted demand made in its draft (E3). Each is also an
        /// intervention, because a player picking a facility has made a decision.</summary>
        public int Assignments { get; private set; }

        public SimulationEngine Engine => _engine;

        /// <summary>Every plan a demand or an order committed, in commit order.</summary>
        public IReadOnlyList<KnownPlan> Known => _known;

        /// <summary>The controller's orders, in the order it placed them, committed or refused.</summary>
        public List<ScriptedDemand> Orders { get; } = new();

        public List<CommandOutcome> CommandOutcomes { get; } = new();

        public List<WaitingAlert> WaitingAlerts { get; } = new();

        /// <summary>
        /// A plan or task the engine names, as the report names it: the demand that committed the
        /// plan, or the plan or task id when no demand did.
        /// </summary>
        private string Name(string subject)
        {
            var parts = subject.Split(':');
            if (parts is ["plan", var id] && long.TryParse(id, out var plan)
                && _byPlan.TryGetValue(new PlanId(plan), out var state))
            {
                return _demands.Single(d => ReferenceEquals(d.Value, state)).Key;
            }

            return subject.Replace(':', ' ');
        }

        public void Apply(ScriptedDemand demand) => Commit(demand, byController: false);

        /// <summary>
        /// A controller's order, through the same path as a scripted demand, named
        /// <c>{item}#{n}</c> in the order placed. Null when approval refused it.
        /// </summary>
        public PlanId? Order(ItemAmount goal, StorageId? destination)
        {
            var order = new ScriptedDemand(
                $"{goal.Item}#{Orders.Count + 1}", _engine.Snapshot.Tick, goal, destination, Assemble: null);
            Orders.Add(order);
            return Commit(order, byController: true);
        }

        /// <summary>
        /// A controller's command. Accepted ones are interventions, as a scripted command's are; a
        /// refused one changed nothing and counts for nothing.
        /// </summary>
        public CommandResult Execute(Command command)
        {
            var result = _engine.Execute(command);
            var kind = command.GetType().Name;
            var at = _controllerCommands.FindIndex(c => c.Kind == kind);
            if (at < 0)
            {
                _controllerCommands.Add((kind, 0, 0));
                at = _controllerCommands.Count - 1;
            }

            var (_, accepted, refused) = _controllerCommands[at];
            if (result is CommandRefused)
            {
                _controllerCommands[at] = (kind, accepted, refused + 1);
            }
            else
            {
                Interventions++;
                _controllerCommands[at] = (kind, accepted + 1, refused);
            }

            return result;
        }

        public IReadOnlyList<ControllerCommands> ControllerCommandCounts() =>
            _controllerCommands.Select(c => new ControllerCommands(c.Kind, c.Accepted, c.Refused)).ToList();

        private PlanId? Commit(ScriptedDemand demand, bool byController)
        {
            var state = new DemandState();
            _demands[demand.Id] = state;

            // Build mode in the composer delivers the unit into the slot's own buffer, which is
            // where commissioning looks for it. A construction demand with no destination of its
            // own does the same, or the unit lands in the hold and the facility never builds.
            var destination = demand.Destination;
            if (destination is null && demand.Assemble is { } slot)
            {
                destination = _engine.State.Vessel.Facilities.Single(f => f.Id == slot).LocalStorage;
            }

            var draft = Assign(
                PlanDraftEditor.Create(demand.Goal, _engine, destination, demand.Assemble), demand, out var edits);
            switch (PlanDraftEditor.Approve(draft, _engine))
            {
                case PlanApprovalRefused refused:
                    state.RefusedIssues = refused.Issues.Count;
                    return null;

                case PlanApprovalCommitted committed:
                    var result = _engine.Execute(new CommitPlan(committed.Plan));
                    if (result is CommandRefused commitRefused)
                    {
                        // Approved against this very world a moment ago, so a refusal here is the
                        // harness's bug, not the script's, and reporting it as a refusal would hide it.
                        throw new InvalidOperationException(
                            $"Demand '{demand.Id}' was approved and then refused: {commitRefused.Reason}");
                    }

                    Interventions += 1 + edits;
                    Assignments += edits;

                    var plan = _engine.State.Plans.Plans[^1];
                    if (demand.Priority is { } priority)
                    {
                        _engine.Execute(new SetPlanPriority(plan.Id, priority));
                    }

                    state.Plan = plan.Id;
                    state.CommittedAtTick = plan.CommittedAtTick;
                    state.Shortfall = committed.Plan.Unplannable.Sum(u => u.Quantity);
                    _byPlan[plan.Id] = state;

                    // A plan that spawned nothing never retires a task, so no PlanCompleted is
                    // coming for it: the goal was already where it was asked for.
                    if (plan.SpawnedTasks.Count == 0)
                    {
                        state.ReadyAtTick = plan.CommittedAtTick;
                    }

                    _known.Add(new KnownPlan(
                        demand.Id, demand.Goal, destination, demand.Assemble, plan.Id, byController));
                    return plan.Id;
            }

            return null;
        }

        /// <summary>
        /// Moves every production step running an assigned schematic to its assigned facility, one
        /// <see cref="SetExecutor"/> edit at a time, which is the composer's facility picker. The
        /// steps are found again after each edit, because moving a stage replans the legs beneath
        /// it; a step is edited once at most, so an edit the draft does not take cannot loop.
        /// An assignment the draft cannot honour is left for approval to refuse, as the composer
        /// would. The edits count only once the plan commits: a refused draft changed nothing.
        /// </summary>
        private PlanDraft Assign(PlanDraft draft, ScriptedDemand demand, out int edits)
        {
            edits = 0;
            if (demand.Assign is not { } assignments)
            {
                return draft;
            }

            var edited = new HashSet<DraftStepId>();
            foreach (var assignment in assignments)
            {
                while (draft.Steps.FirstOrDefault(s =>
                           s.Work is DraftProduce produce && produce.Schematic == assignment.Schematic
                           && s.Executor != assignment.Facility && !edited.Contains(s.Id)) is { } step)
                {
                    edited.Add(step.Id);
                    draft = PlanDraftEditor.Adjust(draft, _engine, new SetExecutor(step.Id, assignment.Facility));
                    edits++;
                }
            }

            return draft;
        }

        /// <summary>
        /// Applies one scripted command to the plan its demand committed. Accepted commands are
        /// interventions; a refused one is reported with the kernel's reason and counts for nothing,
        /// because it changed nothing.
        /// </summary>
        public void Apply(ScriptedCommand scripted)
        {
            if (_demands[scripted.Demand].Plan is not { } plan)
            {
                CommandOutcomes.Add(new CommandOutcome(scripted, $"demand '{scripted.Demand}' has no plan"));
                return;
            }

            PlanId? to = null;
            if (scripted.To is { } receiver)
            {
                if (_demands[receiver].Plan is not { } receiving)
                {
                    CommandOutcomes.Add(new CommandOutcome(scripted, $"demand '{receiver}' has no plan"));
                    return;
                }

                to = receiving;
            }

            Command command = scripted.Kind switch
            {
                ScriptedCommandKind.Priority => new SetPlanPriority(plan, scripted.Priority!.Value),
                ScriptedCommandKind.Hold => new HoldPlan(plan),
                ScriptedCommandKind.Release => new ReleasePlan(plan),
                ScriptedCommandKind.Cancel => new CancelPlan(plan),
                ScriptedCommandKind.Amend => new AmendPlan(plan, scripted.Quantity!.Value),
                ScriptedCommandKind.Relinquish => new RelinquishStock(
                    plan, scripted.Storage!.Value, scripted.Item!.Value, scripted.Quantity!.Value),
                ScriptedCommandKind.Reassign => new ReassignStock(
                    plan, to!.Value, scripted.Storage!.Value, scripted.Item!.Value, scripted.Quantity!.Value),
                ScriptedCommandKind.Move => new MoveWork(plan, scripted.Schematic!.Value, scripted.Facility!.Value),
                _ => throw new InvalidOperationException($"Unknown command kind {scripted.Kind}."),
            };

            switch (_engine.Execute(command))
            {
                case CommandRefused refused:
                    CommandOutcomes.Add(new CommandOutcome(scripted, refused.Reason));
                    break;
                default:
                    Interventions++;
                    CommandOutcomes.Add(new CommandOutcome(scripted, null));
                    break;
            }
        }

        public void Drain()
        {
            var snapshot = _engine.Snapshot;
            var fresh = snapshot.TotalEventsEmitted - _seen;
            if (fresh > snapshot.RecentEvents.Count)
            {
                throw new InvalidOperationException(
                    $"Tick {snapshot.Tick} emitted {fresh} events and the journal holds " +
                    $"{snapshot.RecentEvents.Count}; the replay would be reporting from a partial record.");
            }

            for (var i = snapshot.RecentEvents.Count - (int)fresh; i < snapshot.RecentEvents.Count; i++)
            {
                var e = snapshot.RecentEvents[i];
                switch (e.Code)
                {
                    case EventCode.PlanCompleted when _byPlan.TryGetValue(new PlanId(e.Data["plan"]), out var demand):
                        demand.ReadyAtTick ??= e.Tick;
                        break;

                    case EventCode.SwitchOverStarted:
                        var facility = new ExecutorId(e.Subject);
                        _switchesStarted[facility] = _switchesStarted.GetValueOrDefault(facility) + 1;
                        break;

                    case EventCode.AlertRaised when _byPlan.ContainsKey(new PlanId(e.Data["plan"])):
                        var raised = snapshot.Alerts.Single(a => a.SubjectId == e.Subject);
                        WaitingAlerts.Add(new WaitingAlert(
                            Name(e.Subject), Name(raised.RelatedSubjectId ?? string.Empty), raised.RootCause, e.Tick, null));
                        break;

                    case EventCode.AlertCleared:
                        var open = WaitingAlerts.FindLastIndex(a => a.Demand == Name(e.Subject) && a.ClearedAtTick is null);
                        if (open >= 0)
                        {
                            WaitingAlerts[open] = WaitingAlerts[open] with { ClearedAtTick = e.Tick };
                        }

                        break;

                    case EventCode.SwitchOverAbandoned:
                        var abandoning = new ExecutorId(e.Subject);
                        _switchesAbandoned[abandoning] = _switchesAbandoned.GetValueOrDefault(abandoning) + 1;
                        break;
                }
            }

            _seen = snapshot.TotalEventsEmitted;
        }

        public void Sample()
        {
            var snapshot = _engine.Snapshot;
            _samples++;

            foreach (var item in snapshot.InProcess)
            {
                Accumulate(_material, item.Id, item.InRuns + item.OnBelts);
            }

            foreach (var storage in snapshot.Storages)
            {
                Accumulate(_space, storage.Id, storage.FillPermille);
            }

            foreach (var executor in snapshot.Executors)
            {
                if (!executor.Built)
                {
                    continue;
                }

                if (executor.Status == ExecutorStatus.SwitchingOver)
                {
                    _switchingTicks[executor.Id] = _switchingTicks.GetValueOrDefault(executor.Id) + 1;
                }

                if (!_time.TryGetValue(executor.Id, out var ticks))
                {
                    ticks = new long[_categories];
                    _time[executor.Id] = ticks;
                }

                ticks[(int)UtilizationWindow.CategoryOf(executor.Status, executor.BlockReason)]++;
            }
        }

        public DemandOutcome Outcome(ScriptedDemand demand)
        {
            var state = _demands[demand.Id];
            return new DemandOutcome(
                demand, state.CommittedAtTick, state.RefusedIssues, state.Shortfall, state.ReadyAtTick);
        }

        /// <summary>
        /// Every item a demand or an order asked for, in catalog order, and what the vessel's
        /// storages hold of it at the end. Over-production is what this shows.
        /// </summary>
        public IReadOnlyList<EndStock> Stock(ContentCatalog catalog)
        {
            var asked = _known.Select(k => k.Goal.Item).ToHashSet();
            var resources = _engine.Snapshot.Resources.ToDictionary(r => r.Id, r => r.Amount);
            return catalog.Items
                .Where(item => asked.Contains(item.Id))
                .Select(item => new EndStock(item.Id, resources.GetValueOrDefault(item.Id)))
                .ToList();
        }

        /// <summary>Per item, in catalog order; an item never in process is left out.</summary>
        public IReadOnlyList<Reading> Material(ContentCatalog catalog) =>
            catalog.Items
                .Where(item => _material.TryGetValue(item.Id, out var m) && m.Peak > 0)
                .Select(item => Mean(item.Id.Value, _material[item.Id]))
                .ToList();

        public IReadOnlyList<Reading> Space() =>
            _engine.Snapshot.Storages
                .Select(s => Mean(s.Id.Value, _space.GetValueOrDefault(s.Id)))
                .ToList();

        public IReadOnlyList<Changeovers> ChangeoverReadings() =>
            _engine.Snapshot.Executors
                .Select(e => new Changeovers(
                    e.Id,
                    _switchesStarted.GetValueOrDefault(e.Id),
                    _switchingTicks.GetValueOrDefault(e.Id),
                    _switchesAbandoned.GetValueOrDefault(e.Id)))
                .ToList();

        public IReadOnlyList<FacilityTime> Time() =>
            _engine.Snapshot.Executors
                .Select(e => new FacilityTime(
                    e.Id, _time.TryGetValue(e.Id, out var ticks) ? ticks : new long[_categories]))
                .ToList();

        /// <summary>
        /// Every task of a not-ready demand's plan still open at the end, in demand order (the
        /// script's, then the controller's orders), then in the plan's commit order. A task missing from the snapshot has retired, which only a
        /// finished task does.
        /// </summary>
        public IReadOnlyList<UnfinishedTask> Unfinished(IEnumerable<ScriptedDemand> demands)
        {
            var tasks = _engine.Snapshot.Tasks.ToDictionary(t => t.Id);
            var open = new List<UnfinishedTask>();
            foreach (var demand in demands)
            {
                var state = _demands[demand.Id];
                if (state.ReadyAtTick is not null || state.Plan is not { } id)
                {
                    continue;
                }

                var plan = _engine.State.Plans.Plans.Single(p => p.Id == id);
                foreach (var taskId in plan.SpawnedTasks)
                {
                    if (tasks.TryGetValue(taskId, out var task) && task.State != TaskState.Complete)
                    {
                        string? behind = task.WaitingOnPlan is { } holder ? Name($"plan:{holder}")
                            : task.WaitingOnTask is { } winner
                                ? _engine.State.Plans.Owning(winner) is { } owner ? Name($"plan:{owner.Id}") : $"task {winner}"
                                : null;
                        open.Add(new UnfinishedTask(
                            demand.Id, task.Id, task.Executor, Describe(task), task.State, task.LastReason, behind));
                    }
                }
            }

            return open;
        }

        private static string Describe(TaskInstanceState task) => task.Action switch
        {
            Produce p => $"{p.Schematic} {task.CompletedRuns}/{p.Runs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "standing"} runs",
            Transfer x => $"{x.Item} {x.From} → {x.To} {task.MovedQuantity}/{x.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "standing"}",
            _ => task.Action.GetType().Name,
        };

        private Reading Mean(string subject, (long Sum, long Peak) sampled) =>
            new(subject, _samples == 0 ? 0 : sampled.Sum / _samples, sampled.Peak);

        private static void Accumulate<TKey>(Dictionary<TKey, (long Sum, long Peak)> into, TKey key, long value)
            where TKey : notnull
        {
            var (sum, peak) = into.GetValueOrDefault(key);
            into[key] = (sum + value, Math.Max(peak, value));
        }
    }

    private sealed class DemandState
    {
        public PlanId? Plan { get; set; }

        public long? CommittedAtTick { get; set; }

        public int RefusedIssues { get; set; }

        public long Shortfall { get; set; }

        public long? ReadyAtTick { get; set; }
    }
}
