using Godot;

namespace Dimenship.Ui;

/// <summary>
/// What is being dragged: either a fitting pulled off the palette, or the fitting already sitting in
/// a socket being moved out of it.
/// <para>
/// A <see cref="RefCounted"/> for the same reason <see cref="BlockDragData"/> is one: Godot's drag
/// payload is a <c>Variant</c>, and a Variant carries a <see cref="GodotObject"/> but not a plain
/// C# object. Ref-counted rather than a bare object so an abandoned drag frees itself.
/// </para>
/// </summary>
public sealed partial class LoadoutDragData : RefCounted
{
    /// <summary>The fitting being dragged. Always set — there is nothing else to drag here.</summary>
    public required FittingDef Fitting { get; init; }

    /// <summary>
    /// The socket this came out of, or null when it came off the palette. What tells a drop
    /// whether it is fitting a new fitting or moving one, and therefore whether the source socket
    /// ends up empty or holding whatever was displaced.
    /// </summary>
    public int? FromSocket { get; init; }

    /// <summary>True when this may be fitted into a socket of the given kind.</summary>
    public bool Fits(SocketKind kind) => Fitting.Kind == kind;
}
