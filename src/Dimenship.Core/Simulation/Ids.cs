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
/// </summary>
public enum FacilityType
{
    Extractor,
    Refinery,
    Factory,
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

    /// <summary>Per-executor: work is queued and none of it can proceed.</summary>
    AllTasksBlocked,

    PostponeInsufficientInput,
    PostponeInsufficientSource,
    PostponeDestinationFull,
    PostponeInsufficientEnergy,
    PostponeOutputRoute,
    PostponeSafetyLock,

    /// <summary>Vessel-wide: total draw reached capacity, whether or not anything was refused.</summary>
    PowerCapReached,
}
