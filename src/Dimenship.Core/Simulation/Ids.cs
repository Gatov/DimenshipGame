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

/// <summary>Identifier for a runtime task. Assigned by the engine when a task is queued.</summary>
public readonly record struct TaskId(long Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Identifier for a facility instance. Stable across saves.</summary>
public readonly record struct FacilityId(string Value)
{
    public override string ToString() => Value;
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

public enum FacilityKind
{
    Extractor,
    Smelter,
    StabilizationField,
}

public enum FacilityStatus
{
    /// <summary>Has not run yet this session. Only seen before the first tick.</summary>
    Idle,
    Running,
    Blocked,
}

public enum EventCategory
{
    Production,
    Power,
    Fault,
}

public enum EventCode
{
    Run,

    /// <summary>Per-facility: this facility lacked its input this tick.</summary>
    BlockMissingInput,

    /// <summary>Per-facility: granting this facility's draw would have exceeded vessel capacity.</summary>
    BlockPowerCap,

    /// <summary>Vessel-wide: total draw reached capacity, whether or not anything was blocked.</summary>
    PowerCapReached,

    /// <summary>Output was discarded because the destination stock was full.</summary>
    StockFull,
}
