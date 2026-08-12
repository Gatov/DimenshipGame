namespace Dimenship.Core.Content;

/// <summary>
/// Identifier for a class of production facility — what a facility is before one is built and
/// named. Distinct from <see cref="Simulation.ExecutorId"/>, which names the built instance:
/// "Factory Alpha" is an executor, "standard factory" is an archetype, and a scenario that
/// confused the two would let a build sheet redefine a machine's work rate.
/// </summary>
public readonly record struct FacilityArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of transport line.</summary>
public readonly record struct TransportArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of storage. Capacity is a property of the kind of hold.</summary>
public readonly record struct StorageArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of reactor. Filled by the fuel-burning power core work.</summary>
public readonly record struct ReactorArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a mission target stratum.</summary>
public readonly record struct StratumId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Identifier for a power sink. A sink has no per-instance state — it draws what it draws — so a
/// scenario names one rather than describing one.
/// </summary>
public readonly record struct PowerSinkId(string Value)
{
    public override string ToString() => Value;
}
