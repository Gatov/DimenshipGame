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
/// </summary>
public sealed record UnfinishedTask(
    string Demand, TaskId Task, ExecutorId Executor, string Work, TaskState State, PostponeReason? Reason);

/// <summary>Mean and peak of one reading, sampled once per tick.</summary>
public sealed record Reading(string Subject, long Mean, long Peak);

/// <summary>Switch-overs one facility started, and the ticks it spent switching.</summary>
public sealed record Changeovers(ExecutorId Facility, long Count, long Ticks);

/// <summary>
/// One facility's ticks over the whole run, by the same categories as its utilization window
/// (<see cref="UtilizationWindow.CategoryOf"/>), counted only while it was built.
/// </summary>
public sealed record FacilityTime(ExecutorId Facility, IReadOnlyList<long> TicksByCategory);

/// <summary>
/// Everything a replay measured. Integers only, ratios in permille, and every list in the
/// declaration order of the content or script it came from.
/// </summary>
public sealed record ReplayResult(
    string Scenario,
    string ContentVersion,
    long EndTick,
    int Interventions,
    IReadOnlyList<DemandOutcome> Demands,
    IReadOnlyList<Reading> MaterialTiedUp,
    IReadOnlyList<Reading> SpaceTiedUp,
    IReadOnlyList<Changeovers> Changeovers,
    IReadOnlyList<FacilityTime> FacilityTime,
    IReadOnlyList<UnfinishedTask> Unfinished,
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
/// No policy hook yet. Controllers need the kernel command surface (C0), and queue order is the
/// only policy until that exists.
/// </para>
/// </summary>
public static class Replay
{
    public static ReplayResult Run(ContentCatalog catalog, Scenario scenario, ReplayScript script)
    {
        if (scenario.Id != script.Scenario)
        {
            throw new ArgumentException(
                $"The script runs '{script.Scenario}', not '{scenario.Id}'.", nameof(scenario));
        }

        var engine = SimulationEngine.NewGame(catalog, scenario);
        var run = new Accumulators(engine);

        for (var now = 0L; ; now++)
        {
            foreach (var demand in script.Demands)
            {
                if (demand.Tick == now)
                {
                    run.Apply(demand);
                }
            }

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
            script.EndTick,
            run.Interventions,
            script.Demands.Select(run.Outcome).ToList(),
            run.Material(catalog),
            run.Space(),
            run.ChangeoverReadings(),
            run.Time(),
            run.Unfinished(script),
            hash);
    }

    /// <summary>The accumulators for one run. Dictionaries are lookups only; every list written out
    /// is rebuilt in declaration order from the catalog or the snapshot.</summary>
    private sealed class Accumulators
    {
        private readonly SimulationEngine _engine;
        private readonly Dictionary<string, DemandState> _demands = new(StringComparer.Ordinal);
        private readonly Dictionary<PlanId, DemandState> _byPlan = new();
        private readonly Dictionary<ItemId, (long Sum, long Peak)> _material = new();
        private readonly Dictionary<StorageId, (long Sum, long Peak)> _space = new();
        private readonly Dictionary<ExecutorId, long> _switchesStarted = new();
        private readonly Dictionary<ExecutorId, long> _switchingTicks = new();
        private readonly Dictionary<ExecutorId, long[]> _time = new();
        private readonly int _categories = Enum.GetValues<UtilizationCategory>().Length;
        private long _seen;
        private long _samples;

        public Accumulators(SimulationEngine engine)
        {
            _engine = engine;
            _seen = engine.Snapshot.TotalEventsEmitted;
        }

        public int Interventions { get; private set; }

        public void Apply(ScriptedDemand demand)
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

            var draft = PlanDraftEditor.Create(demand.Goal, _engine, destination, demand.Assemble);
            switch (PlanDraftEditor.Approve(draft, _engine))
            {
                case PlanApprovalRefused refused:
                    state.RefusedIssues = refused.Issues.Count;
                    return;

                case PlanApprovalCommitted committed:
                    _engine.Commit(committed.Plan);
                    Interventions++;

                    var plan = _engine.State.Plans.Plans[^1];
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

                    return;
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
                    e.Id, _switchesStarted.GetValueOrDefault(e.Id), _switchingTicks.GetValueOrDefault(e.Id)))
                .ToList();

        public IReadOnlyList<FacilityTime> Time() =>
            _engine.Snapshot.Executors
                .Select(e => new FacilityTime(
                    e.Id, _time.TryGetValue(e.Id, out var ticks) ? ticks : new long[_categories]))
                .ToList();

        /// <summary>
        /// Every task of a not-ready demand's plan still open at the end, in demand order, then in
        /// the plan's commit order. A task missing from the snapshot has retired, which only a
        /// finished task does.
        /// </summary>
        public IReadOnlyList<UnfinishedTask> Unfinished(ReplayScript script)
        {
            var tasks = _engine.Snapshot.Tasks.ToDictionary(t => t.Id);
            var open = new List<UnfinishedTask>();
            foreach (var demand in script.Demands)
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
                        open.Add(new UnfinishedTask(
                            demand.Id, task.Id, task.Executor, Describe(task), task.State, task.LastReason));
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
