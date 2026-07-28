namespace Dimenship.Core.Simulation;

/// <summary>Identifier for a resource kind. Stable across saves.</summary>
public readonly record struct ResourceId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a facility instance. Stable across saves.</summary>
public readonly record struct FacilityId(string Value)
{
    public override string ToString() => Value;
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
