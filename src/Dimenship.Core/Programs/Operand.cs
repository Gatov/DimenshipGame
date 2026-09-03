namespace Dimenship.Core.Programs;

/// <summary>
/// A value a condition names. Hierarchy rather than a tagged union of optionals, so a literal
/// cannot silently carry a target id and a parameter cannot pretend it has a binding.
/// </summary>
public abstract record Operand;

/// <summary>A fixed integer. Amounts, counts, and enum ordinals compared as numbers.</summary>
public sealed record Literal(long Value) : Operand;

/// <summary>
/// A program parameter by name. Declared so the program runtime can bind it; <b>refused at
/// enqueue</b> outside a program, because there is no binding to resolve against.
/// </summary>
public sealed record ParameterRef(string Name) : Operand;

/// <summary>
/// A world object by kind and id. Unknown targets are refused at enqueue so a typo becomes a
/// planning mistake rather than a task that never starts for a reason nobody can see.
/// </summary>
public sealed record TargetRef(TargetKind Kind, string Id) : Operand;

/// <summary>
/// A named enum member. In memory it carries <c>(Kind, Value)</c>; on the wire it is saved by
/// name so a reordered enum does not silently re-point every saved condition.
/// </summary>
public sealed record EnumRef(string Kind, int Value) : Operand;

/// <summary>What a <see cref="TargetRef"/> points at.</summary>
public enum TargetKind
{
    Executor,
    Storage,
    Item,
    Schematic,
}
