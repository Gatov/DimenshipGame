using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.State;

/// <summary>
/// Simulated time, and how fast real time buys it.
/// <para>
/// <see cref="Tick"/> is the only thing the simulation advances on: <b>TimeFlow scales how many
/// ticks a real second buys and nothing else.</b> A tick must cost the same whatever the flow, or
/// 4× becomes a different game rather than a faster one.
/// </para>
/// </summary>
public sealed class OperationalClock
{
    public long Tick { get; set; }

    /// <summary>
    /// Session state, not saved: every load resumes paused. A speed the player happened to leave
    /// running is not a preference they expressed.
    /// </summary>
    public TimeFlow Flow { get; set; } = TimeFlow.Paused;

    /// <summary>Saved, because it is a preference the player set.</summary>
    public bool AutoPauseOnCriticalAlert { get; set; }
}

/// <summary>
/// Per-domain generator state.
/// <para>
/// Seeds are per domain — missions, hazards, salvage — rather than one global generator, so that
/// drawing a mission result cannot shift what a later production tie-break returns. A single stream
/// makes every consumer order-coupled to every other, which is the classic way a deterministic
/// simulation stops being one.
/// </para>
/// <para>
/// A stream value is <b>the generator's advanced state, not the seed it started from</b>. Saving
/// the seed would replay every draw the world has already made the next time it loads.
/// </para>
/// </summary>
public sealed class RandomState
{
    public required ulong[] Streams { get; init; }

    public static RandomState FromSeed(ulong seed)
    {
        var domains = Enum.GetValues<RngDomain>().Length;
        var streams = new ulong[domains];
        for (var i = 0; i < domains; i++)
        {
            // Split-mix the domain index into the seed so two domains of one world do not start
            // life as the same generator.
            streams[i] = seed + (ulong)(i + 1) * 0x9E3779B97F4A7C15UL;
        }

        return new RandomState { Streams = streams };
    }

    /// <summary>
    /// Extends a save made before a domain was appended, rather than failing on it. Domains are
    /// append-only, so a shorter array is an older save and never a re-pointed one.
    /// </summary>
    public RandomState Extended(ulong seed)
    {
        var domains = Enum.GetValues<RngDomain>().Length;
        if (Streams.Length >= domains)
        {
            return this;
        }

        var streams = new ulong[domains];
        Array.Copy(Streams, streams, Streams.Length);
        for (var i = Streams.Length; i < domains; i++)
        {
            streams[i] = seed + (ulong)(i + 1) * 0x9E3779B97F4A7C15UL;
        }

        return new RandomState { Streams = streams };
    }
}

/// <summary>
/// Every task, by id. Executors hold ids; the registry holds bodies — a task is referenced from an
/// executor queue, a plan and the journal, and only one of those can own it.
/// <para>
/// <b>Completed tasks retire.</b> "Every task, by id" cannot mean every task ever. A registry that
/// only grows is a snapshot rebuild and a planner pass that both grow without bound, and a save
/// that grows forever. A finished task leaves the live registry for a bounded retired window, on
/// the same terms as the journal's event bound.
/// </para>
/// </summary>
public sealed class TaskRegistry
{
    /// <summary>
    /// How many finished tasks are remembered. Bounded on the same terms as the journal's events:
    /// the trigger for revisiting one is the trigger for revisiting the other.
    /// </summary>
    public const int RetiredCapacity = 512;

    private readonly Dictionary<TaskId, ProductionTask> _production = new();
    private readonly Dictionary<TaskId, TransportTask> _transport = new();
    private readonly List<ProductionTask> _productionOrder = new();
    private readonly List<TransportTask> _transportOrder = new();
    private readonly List<TaskId> _retired = new();

    public long NextTaskId { get; set; }

    /// <summary>
    /// Production tasks in the order they were queued, live ones and retired ones alike. A retired
    /// task keeps its body until the window rolls past it: a task that vanished the instant it
    /// finished would leave the console and the inspector with nothing to say about work the
    /// player just watched happen.
    /// </summary>
    public IReadOnlyList<ProductionTask> Production => _productionOrder;

    /// <inheritdoc cref="Production"/>
    public IReadOnlyList<TransportTask> Transport => _transportOrder;

    /// <summary>Finished tasks still inside the retired window, oldest first.</summary>
    public IReadOnlyList<TaskId> Retired => _retired;

    public TaskId Mint() => new(++NextTaskId);

    public void Add(ProductionTask task)
    {
        _production[task.Id] = task;
        _productionOrder.Add(task);
    }

    public void Add(TransportTask task)
    {
        _transport[task.Id] = task;
        _transportOrder.Add(task);
    }

    public ProductionTask? Job(TaskId id) => _production.GetValueOrDefault(id);

    public TransportTask? Transfer(TaskId id) => _transport.GetValueOrDefault(id);

    /// <summary>
    /// Moves a finished task out of the live registry. It stays addressable as a retired id until
    /// the window rolls past it, which is what lets a plan whose tasks have all finished still say
    /// so without holding every one of them forever.
    /// </summary>
    public void Retire(TaskId id)
    {
        if (!_production.ContainsKey(id) && !_transport.ContainsKey(id))
        {
            return;
        }

        _retired.Add(id);
        while (_retired.Count > RetiredCapacity)
        {
            Forget(_retired[0]);
            _retired.RemoveAt(0);
        }
    }

    /// <summary>
    /// Drops a task that has rolled out of the retired window. This is the whole point of the
    /// window: the registry only ever grew before, so a snapshot rebuild projected every task ever
    /// queued and the planner scanned them all on every expansion step — a session that committed
    /// plans for an hour paid for all of it, and a save would have carried all of it too.
    /// </summary>
    private void Forget(TaskId id)
    {
        if (_production.Remove(id, out var job))
        {
            _productionOrder.Remove(job);
        }
        else if (_transport.Remove(id, out var transfer))
        {
            _transportOrder.Remove(transfer);
        }
    }
}

/// <summary>
/// What the campaign knows. Unlocks live here rather than in the catalog because they answer yes to
/// both halves of the question that sorts the tiers: they change during play, and they differ
/// between two players running the same build.
/// </summary>
public sealed class ProgressLedger
{
    public HashSet<Simulation.SchematicId> UnlockedSchematics { get; } = new();

    /// <summary>What the codex may show.</summary>
    public HashSet<ItemId> DiscoveredItems { get; } = new();

    /// <summary>One-shot story and tutorial marks.</summary>
    public HashSet<string> Flags { get; } = new();
}

/// <summary>
/// A goal the player committed, and the work it turned into.
/// <para>
/// This exists because <b>the goal is the only level at which progress is legible.</b> Tasks are
/// per-executor by design — execution order belongs to executors, not to the plan — so nothing in
/// the task list can answer "how far along is <i>produce 4 robot frames</i>" without the plan that
/// grouped them.
/// </para>
/// </summary>
public sealed class CommittedPlan
{
    public required PlanId Id { get; init; }

    /// <summary>The thing the player actually asked for.</summary>
    public required ItemAmount Goal { get; init; }

    public required long CommittedAtTick { get; init; }

    /// <summary>Production and transport alike, in commit order.</summary>
    public required IReadOnlyList<TaskId> SpawnedTasks { get; init; }

    /// <summary>What it could not supply, as of the moment it was committed.</summary>
    public required IReadOnlyList<PlanShortage> Shortages { get; init; }

    /// <summary>
    /// How many of its tasks have finished. Counted as they finish rather than by rescanning
    /// <see cref="SpawnedTasks"/>, because a retired task is no longer in the registry to scan.
    /// </summary>
    public int CompletedTasks { get; set; }

    public PlanState State { get; set; } = PlanState.Active;

    public bool IsFinished => CompletedTasks >= SpawnedTasks.Count;
}

/// <summary>Committed plans, in commit order, and the counter that mints their ids.</summary>
public sealed class PlanRegistry
{
    private readonly Dictionary<TaskId, PlanId> _owners = new();

    public long NextPlanId { get; set; }

    public List<CommittedPlan> Plans { get; } = new();

    public PlanId Mint() => new(++NextPlanId);

    public void Record(CommittedPlan plan)
    {
        Plans.Add(plan);
        foreach (var task in plan.SpawnedTasks)
        {
            _owners[task] = plan.Id;
        }
    }

    /// <summary>The plan a task belongs to, if any. A task queued by hand belongs to none.</summary>
    public CommittedPlan? Owning(TaskId task)
    {
        if (!_owners.TryGetValue(task, out var id))
        {
            return null;
        }

        foreach (var plan in Plans)
        {
            if (plan.Id == id)
            {
                return plan;
            }
        }

        return null;
    }
}

/// <summary>
/// An expedition. Mechanics are deferred; three things about the shape are load-bearing now.
/// <para>
/// <see cref="ForPlan"/> turns "41 raw material missing" into a tracked resolution rather than a
/// warning the player has to remember. <see cref="Dock"/> is an ordinary executor with a queue, so
/// nothing here is built as a parallel system. And <see cref="RngStream"/> is per mission rather
/// than per world, so a mission that draws from its own stream replays identically whether or not
/// another mission ran first.
/// </para>
/// </summary>
public sealed class Mission
{
    public required MissionId Id { get; init; }

    public required Content.StratumId Target { get; init; }

    public required MissionKind Kind { get; init; }

    public MissionPhase Phase { get; set; } = MissionPhase.Preparing;

    public required ExecutorId Dock { get; init; }

    public List<RobotId> Group { get; } = new();

    public required long DepartedAtTick { get; init; }

    public long ArrivesAtTick { get; set; }

    public List<ItemAmount> Manifest { get; } = new();

    public required StorageId Destination { get; init; }

    /// <summary>The shortage this was mounted to fix.</summary>
    public PlanId? ForPlan { get; set; }

    /// <summary>This mission's generator as it now stands, not the seed it started from.</summary>
    public ulong RngStream { get; set; }
}

/// <summary>Missions, and the counter that mints their ids.</summary>
public sealed class MissionLedger
{
    public long NextMissionId { get; set; }

    public List<Mission> Missions { get; } = new();

    public MissionId Mint() => new(++NextMissionId);
}

/// <summary>
/// A live condition, which is not a journal event and must not be conflated with one. An event is a
/// historical fact, already emitted and immutable. An alert persists until its cause clears,
/// carries a severity and a root cause, and carries player state — acknowledged, pinned — which is
/// the reason alerts are saved at all.
/// </summary>
public sealed class Alert
{
    public required AlertId Id { get; init; }

    public required AlertSeverity Severity { get; init; }

    public required AlertCode Code { get; init; }

    /// <summary>The node, line or quest it is about.</summary>
    public required string SubjectId { get; init; }

    public required long RaisedAtTick { get; init; }

    public PostponeReason? RootCause { get; set; }

    public bool Acknowledged { get; set; }

    public bool Pinned { get; set; }
}

/// <summary>Live alerts, and the counter that mints their ids. Nothing raises one yet.</summary>
public sealed class AlertLedger
{
    public long NextAlertId { get; set; }

    public List<Alert> Alerts { get; } = new();

    public AlertId Mint() => new(++NextAlertId);
}

/// <summary>
/// The bounded event ring the engine keeps, plus the total ever emitted. Saved, because a console
/// that goes blank on load is a bug report.
/// </summary>
public sealed class JournalLedger
{
    /// <summary>
    /// How many events are remembered. The console shows the recent past, not the whole run, and an
    /// unbounded buffer is a memory leak with a scrollbar.
    /// </summary>
    public const int Capacity = 512;

    public Queue<SimEvent> Events { get; } = new();

    public long TotalEmitted { get; set; }

    public void Emit(SimEvent entry)
    {
        Events.Enqueue(entry);
        TotalEmitted++;
        while (Events.Count > Capacity)
        {
            Events.Dequeue();
        }
    }
}

/// <summary>
/// Installed programs, and the counter that mints their ids. Declared, not filled: the program
/// language and the authored-content tier are separate work, and this is the seat they take.
/// </summary>
public sealed class ProgramLedger
{
    public long NextInstanceId { get; set; }

    public ProgramInstanceId Mint() => new(++NextInstanceId);
}

/// <summary>
/// One socket on a robot, and the storage that holds whatever is fitted into it.
/// <para>
/// The pair rather than the storage alone, because the socket is what content names and the
/// storage is only where its contents live: a frame declares the socket, and the socket's category
/// is what accepts a drill and refuses a thruster.
/// </para>
/// </summary>
public sealed record RobotSocket(Content.SocketId Socket, StorageId Storage);

/// <summary>
/// A robot: a frame, the sockets that frame declares, and what condition it is in. The domain is
/// declared, not designed — no catalog names a frame or a fitting yet — but the shape of the
/// loadout is fixed here, because it is the one part that is expensive to change later.
/// <para>
/// <b>Sockets, rather than a list of what is installed.</b> A flat list of fitted ids cannot
/// express an empty socket; it can only express a shorter list, and a robot mid-refit would come
/// back off a save reading as a smaller machine. The empty socket is the whole of the refit
/// downtime argument — between removing the old core and installing the new one the socket is
/// genuinely empty, so the machine genuinely runs at its unequipped rating, and nobody has to
/// model "60% upgraded". There is one entry per socket the frame declares, from the moment the
/// robot is built, whether or not anything is in it.
/// </para>
/// <para>
/// What is fitted is not here at all: a socket is a storage of capacity one, so the fitting is an
/// item in that storage. That is what makes installing and removing ordinary
/// <see cref="TransportTask"/>s rather than a refit state machine, and it is why a robot away on a
/// mission cannot be reconfigured — its sockets are simply unreachable by transport.
/// </para>
/// </summary>
public sealed class Robot
{
    public required RobotId Id { get; init; }

    public required Content.RobotFrameId Frame { get; init; }

    public string? NameOverride { get; set; }

    /// <summary>
    /// The frame's sockets and the storage behind each, in the frame's declaration order. Every
    /// socket the frame declares appears; an empty one is an entry whose storage holds nothing.
    /// </summary>
    public List<RobotSocket> Sockets { get; } = new();

    public long IntegrityPermille { get; set; } = 1000;

    public RobotGroupId? Group { get; set; }

    public MissionId? OnMission { get; set; }
}

/// <inheritdoc cref="Robot"/>
public sealed class RobotLedger
{
    public long NextRobotId { get; set; }

    public List<Robot> Robots { get; } = new();

    public RobotId Mint() => new(++NextRobotId);
}

/// <summary>
/// The case graph's seat in the save. Declared and empty: the domain is deferred by the spec, and
/// two things about it are worth recording before anything fills it.
/// <para>
/// The Case Board is explicitly a <b>different graph</b> from the vessel schematic, and must not
/// reuse <c>BaseGraphLayout</c>, <c>NodePlacement</c> or the base graph's projection — sharing them
/// would be the exact confusion the GDD warns against. And readiness is <b>derived</b> from the
/// case graph, so it is snapshot-only and never state: a readiness evaluator that stored its
/// conclusions would be a second source of truth.
/// </para>
/// </summary>
public sealed class CaseLedger;
