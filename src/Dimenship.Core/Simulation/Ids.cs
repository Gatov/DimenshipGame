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
/// <see cref="LaunchBay"/> is the GDD's <i>Mission Dock</i> under the name the vessel's own console
/// uses. The two words mean one facility; the synonym is recorded here rather than left for a
/// reader to discover from a label that does not match the type.
/// </para>
/// </summary>
public enum FacilityType
{
    Extractor,

    /// <summary>Superseded by <see cref="Reactor"/> on this vessel. No facility is one today.</summary>
    Refinery,

    Factory,

    /// <summary>
    /// The refining tier, and — once fuel burn exists — the vessel's power source. Today it refines
    /// and draws power like any other facility: energy is still a fixed pool with no production
    /// behind it, and a reactor that claimed to fill that pool would be claiming a system that has
    /// not been built.
    /// </summary>
    Reactor,

    /// <summary>
    /// Where missions leave from. It has a queue and a status like any facility and no schematic can
    /// run on it, because acquisition does not exist yet: a bay reports idle, which is true.
    /// </summary>
    LaunchBay,
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
}

public enum TaskState
{
    NotStarted,
    Running,

    /// <summary>Selected, then found unable to proceed. Carries a reason and resumes on its own.</summary>
    Postponed,

    Complete,
}

/// <summary>Why an executor could not start or continue a task.</summary>
public enum PostponeReason
{
    InsufficientInputMaterial,
    InsufficientSourceMaterial,
    DestinationFull,
    InsufficientEnergy,
    OutputRouteUnavailable,
    SafetyLock,
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

    /// <summary>A committed plan could not supply something. One event per shortage.</summary>
    PlanShortage,

    PostponeInsufficientInput,
    PostponeInsufficientSource,
    PostponeDestinationFull,
    PostponeInsufficientEnergy,
    PostponeOutputRoute,
    PostponeSafetyLock,

    /// <summary>Vessel-wide: total draw reached capacity, whether or not anything was refused.</summary>
    PowerCapReached,
}
