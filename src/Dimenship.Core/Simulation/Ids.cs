namespace Dimenship.Core.Simulation;

/// <summary>
/// Identifier for a kind of item. Stable across saves.
/// <para>
/// Raw materials, refined materials, components and finished items share one namespace: a
/// schematic input may be any of them, and any schematic's output may be another's input.
/// Separate identifier types would only buy conversion code between them.
/// </para>
/// </summary>
public readonly record struct ItemId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a schematic. Stable across saves.</summary>
public readonly record struct SchematicId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a storage location. Stable across saves.</summary>
public readonly record struct StorageId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for an executor instance — a production facility or a transport line.</summary>
public readonly record struct ExecutorId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a runtime task. Assigned by the engine when the task is queued.</summary>
public readonly record struct TaskId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The class of production facility a schematic requires. Matched against an executor's type:
/// a facility can execute only schematics whose <c>RequiredFacilityType</c> is its own.
/// <para>
/// The members are the GDD's own names for the facilities on the production schematic. In
/// particular a <see cref="MatterReactor"/> is not the Power Core — the GDD is explicit that they
/// are different systems, and the Power Core is not a facility at all.
/// </para>
/// </summary>
public enum FacilityType
{
    /// <summary>
    /// The Emergency Hydrogen Extractor. One passive orbital collector, gathering hydrogen at a
    /// deliberately very low rate, so that the vessel always has a path back from a state where
    /// missions have stopped. The GDD keeps it out of the automation graph: no program may
    /// disable it, and nothing here reconfigures it.
    /// </summary>
    Extractor,

    /// <summary>
    /// Separates recovered Matter Mix into standardized resources, in one of several processing
    /// modes. It makes no power — that is the Power Core, which is a state card and not a node.
    /// </summary>
    MatterReactor,

    /// <summary>Builds components, robot frames and modules, equipment and facility upgrades.</summary>
    Factory,

    /// <summary>
    /// Where missions leave from and where their cargo arrives. It has a queue and a status like
    /// any facility and no schematic can run on it, because acquisition does not exist yet: a dock
    /// reports idle, which is true.
    /// </summary>
    MissionDock,
}

/// <summary>
/// What an executor is doing. Deliberately separate from <see cref="TaskState"/>: a transport
/// task never reports "waiting for transport", because whether it can run is the transport
/// executor's determination, and the answer lands on the task as a postponement.
/// </summary>
public enum ExecutorStatus
{
    /// <summary>Nothing is queued that has not already finished.</summary>
    NoTasksQueued,

    RunningTask,

    /// <summary>Reconfiguring for a different schematic. No work is done and none is charged.</summary>
    SwitchingOver,

    /// <summary>Work is queued, and none of it can proceed. Each task carries its own reason.</summary>
    AllQueuedTasksBlocked,

    /// <summary>
    /// Transport only. Work is queued and there was nothing to pick up for any of it, and the belt
    /// is empty, so the line is not carrying anything and is not stopping anything.
    /// <para>
    /// Distinct from <see cref="AllQueuedTasksBlocked"/> on purpose. For a line, blocked means
    /// cargo aboard that the destination will not take — a fault on this line, which the schematic
    /// paints red and the status bar counts. A line with nothing to carry is downstream of someone
    /// else’s shortage: the storage it draws from already reports that, and lighting the line up
    /// as well would put a fault on the one part of the chain that has none.
    /// </para>
    /// <para>
    /// Appended, not inserted: the status is saved by name, and the order of this enum is read by
    /// nothing, but appending is the habit that keeps it that way.
    /// </para>
    /// </summary>
    NothingToCarry,
}

public enum TaskState
{
    NotStarted,
    Running,

    /// <summary>Selected, then found unable to proceed. Carries a reason and resumes on its own.</summary>
    Postponed,

    Complete,
}

/// <summary>
/// Why an executor could not start or continue a task.
/// <para>
/// <b>Declaration order is the root-cause order, highest priority first.</b> Blocked reasons have
/// to be stable, explainable and prioritized consistently, and "root cause" means the highest
/// priority explanation among several true ones — so the order is declared once, here, and every
/// surface reads it through <see cref="PostponeReasons.RootCause"/>. Two panels disagreeing about
/// why a factory is stalled is exactly the small lie the base graph refuses to tell.
/// </para>
/// <para>
/// Members may be reordered only by deciding the priority again. Adding one means placing it.
/// </para>
/// </summary>
public enum PostponeReason
{
    /// <summary>The player, or a program, stopped it. Nothing else is worth reporting over this.</summary>
    SafetyLock,

    /// <summary>A prerequisite the world does not have yet: an unlock, a built facility, a robot.</summary>
    PrerequisiteMissing,

    /// <summary>The route exists and cannot be used. A stronger statement than having no route.</summary>
    RouteUnsafe,

    OutputRouteUnavailable,

    /// <summary>The power core has no fuel. Reported over a refusal, because it explains one.</summary>
    InsufficientFuel,

    InsufficientEnergy,

    ComputeDeferred,

    /// <summary>A run finished and its output would not fit. The work is held, not lost.</summary>
    DestinationFull,

    InsufficientInputMaterial,

    /// <summary>Nothing at the source to haul. The most ordinary reason, and the least specific.</summary>
    InsufficientSourceMaterial,

    /// <summary>
    /// A task script's start conditions are false. Appended last on purpose: declaration order is
    /// root-cause priority, and a false condition must lose to every physical readiness reason so
    /// a factory short of ore reports the ore rather than the gate. Deliberate exception to the
    /// recycling/refit "no new PostponeReason" rule — that rule still holds for construction and
    /// refit; this reason belongs to the task-script / program layer.
    /// </summary>
    ConditionNotMet,

    /// <summary>
    /// Ready to run, and passed over for a task of strictly higher priority on the same executor
    /// (K2). Appended after <see cref="ConditionNotMet"/>: it is never a physical cause, so every
    /// physical reason outranks it as a root cause, and it is the one reason that means the task
    /// could have run. Only a strictly higher priority records it. At equal priority, waiting
    /// behind setup preference is explained by K8 instead, so that a world where nobody touched a
    /// priority runs exactly as it did before K2.
    /// </summary>
    Outranked,

    /// <summary>
    /// The stock is physically there and held for another plan (K6b; D3, Decision 5). Appended
    /// after <see cref="Outranked"/>: a physical shortage, reported as such, outranks it as a root
    /// cause, and this reason means precisely that the shortage is an allocation, not the vessel.
    /// </summary>
    MaterialClaimed,
}

/// <summary>
/// How urgent a task is. Higher outranks lower; declaration order is the rank, so the members are
/// never reordered and none is inserted. Saved by name.
/// <para>
/// Four named tiers rather than an open integer (D3's vocabulary, the programming doc's names):
/// bounded, so two controllers cannot escalate past each other one point at a time, and every task
/// starts at <see cref="Normal"/>, which is what keeps an untouched world byte-identical to one
/// from before priority existed.
/// </para>
/// </summary>
public enum Priority
{
    Low,
    Normal,
    High,
    Critical,
}

/// <summary>The one comparer every surface uses to pick a root cause.</summary>
public static class PostponeReasons
{
    /// <summary>
    /// The highest-priority reason among several true ones, or null when there are none. Priority
    /// is declaration order, which is where it is decided.
    /// </summary>
    public static PostponeReason? RootCause(IEnumerable<PostponeReason> reasons)
    {
        PostponeReason? best = null;
        foreach (var reason in reasons)
        {
            if (best is null || reason < best)
            {
                best = reason;
            }
        }

        return best;
    }

    /// <summary>Where a reason sits in the root-cause order. Lower wins.</summary>
    public static int Priority(PostponeReason reason) => (int)reason;
}

public enum TaskAttemptOutcome
{
    Started,
    Postponed,
    RunCompleted,
    Completed,
}

public enum EventCategory
{
    Production,

    /// <summary>Material moving between storages.</summary>
    Logistics,

    /// <summary>A plan being committed, and what it could not supply.</summary>
    Planning,

    Power,
    Fault,
}

/// <summary>
/// What happened. Postponement reasons are codes rather than a field inside
/// <see cref="SimEvent.Data"/> because the console's filter and severity mapping switch over
/// codes — a reason buried in a dictionary could not be filtered on.
/// </summary>
public enum EventCode
{
    /// <summary>A task was added to an executor's queue.</summary>
    TaskQueued,

    /// <summary>One execution of a schematic began. Inputs were consumed this tick.</summary>
    RunStarted,

    /// <summary>One execution finished and its output was deposited.</summary>
    RunCompleted,

    /// <summary>Every requested run of a task is done.</summary>
    TaskCompleted,

    SwitchOverStarted,
    SwitchOverCompleted,

    /// <summary>A transfer moved material for the first time.</summary>
    TransferStarted,

    /// <summary>A transfer has moved everything it was asked to move.</summary>
    TransferCompleted,

    /// <summary>Per-executor: work is queued and none of it can proceed.</summary>
    AllTasksBlocked,

    /// <summary>A plan's tasks were injected into executor queues.</summary>
    PlanCommitted,

    /// <summary>A committed plan could not supply something at all. One event per entry.</summary>
    PlanUnplannable,

    /// <summary>Every task a plan spawned has retired. The player's next turn.</summary>
    PlanCompleted,

    PostponeInsufficientInput,
    PostponeInsufficientSource,
    PostponeDestinationFull,
    PostponeInsufficientEnergy,
    PostponeOutputRoute,
    PostponeSafetyLock,
    PostponeConditionNotMet,

    /// <summary>Vessel-wide: total draw reached capacity, whether or not anything was refused.</summary>
    PowerCapReached,

    /// <summary>
    /// An unbuilt facility consumed its construction unit and became built. Category
    /// <see cref="EventCategory.Production"/>.
    /// </summary>
    FacilityBuilt,

    /// <summary>A ready task was passed over for a strictly higher-priority one.</summary>
    PostponeOutranked,

    /// <summary>
    /// A switch-over gave way to a strictly higher-priority task, by cancelling back to the setup
    /// still loaded or by restarting toward a third schematic. Carries the abandoned target
    /// (<c>task</c>) and its replacement (<c>for</c>); a restart's <see cref="SwitchOverStarted"/>
    /// follows it. A retarget to the same schematic continues the countdown and emits nothing.
    /// </summary>
    SwitchOverAbandoned,

    /// <summary>A command set the priority of a task, or of every task a plan spawned.</summary>
    PriorityChanged,

    /// <summary>
    /// Free stock went to a plan's outstanding claim (K6b): when the plan was committed, on an
    /// arrival, or by a reassign. Carries <c>plan</c> and <c>quantity</c>; the subject is the
    /// storage and the item. A plan's own delivery, held for it on arrival, emits nothing: that is
    /// the cargo keeping its owner, not a decision.
    /// </summary>
    ClaimAllocated,

    /// <summary>A task could not take stock that is present and held for another plan.</summary>
    PostponeMaterialClaimed,

    /// <summary>
    /// A command held a plan (<c>plan</c>) or a task queued by hand (<c>task</c>): its unstarted
    /// work stops at the next boundary, and what is physically committed finishes (K6c).
    /// </summary>
    Held,

    /// <summary>A held plan or task was released, and its work is selectable again.</summary>
    Released,

    /// <summary>
    /// A plan or a task queued by hand was cancelled: its work was cut back to what had physically
    /// started, and a plan's holdings went back to allocation. Carries <c>plan</c> or <c>task</c>.
    /// </summary>
    Cancelled,

    /// <summary>
    /// A plan was replanned under its own id for a new goal quantity. Carries <c>plan</c>,
    /// <c>goal</c> and the number of <c>tasks</c> appended.
    /// </summary>
    PlanAmended,

    /// <summary>
    /// An alert was raised (K8). The subject is the alert's subject; a waiting plan's carries
    /// <c>plan</c> and the <c>reason</c> it waits for, as the reason's ordinal.
    /// </summary>
    AlertRaised,

    /// <summary>An alert's condition cleared, and the alert with it.</summary>
    AlertCleared,

    /// <summary>
    /// A command moved a plan's unstarted runs of one schematic to another facility (K6d). The
    /// subject is that facility. Carries <c>plan</c>, the <c>runs</c> moved, and the number of
    /// <c>tasks</c> appended.
    /// </summary>
    WorkMoved,
}
