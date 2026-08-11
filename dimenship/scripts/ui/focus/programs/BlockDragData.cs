using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// What is being dragged: either an existing statement being moved out of the list it sits in, or a
/// fresh template being pulled off the palette.
/// <para>
/// A <see cref="RefCounted"/> because Godot's drag payload is a <c>Variant</c>, and a Variant
/// carries a <see cref="GodotObject"/> but not a plain C# object. Ref-counted rather than a bare
/// object so an abandoned drag frees itself.
/// </para>
/// </summary>
public sealed partial class BlockDragData : RefCounted
{
    /// <summary>The statement being moved. Null when the drag came off the palette.</summary>
    public StatementDraft? Statement { get; set; }

    /// <summary>The list <see cref="Statement"/> currently sits in, so a move can remove it.</summary>
    public List<StatementDraft>? Body { get; set; }

    /// <summary>The condition template being dragged, if this came off the palette.</summary>
    public ConditionKind? Condition { get; set; }

    /// <summary>The action template being dragged, if this came off the palette.</summary>
    public ActionKind? Action { get; set; }

    /// <summary>What the drag preview reads. Set for every payload.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>True when this may fill a condition slot — only a bare condition template can.</summary>
    public bool IsCondition => Condition.HasValue;

    /// <summary>
    /// True when this may be dropped into a statement body. A condition qualifies: dropping one
    /// into a body means "gate what follows on this", which is a branch, and building the branch
    /// there is less work for the player than dragging a branch and then a condition into it.
    /// </summary>
    public bool IsStatement => Statement is not null || Action.HasValue || Condition.HasValue;

    /// <summary>
    /// The statement this payload puts into a body: the one being moved, a fresh command, or a
    /// fresh single-armed branch around a fresh condition.
    /// </summary>
    public StatementDraft ToStatement()
    {
        if (Statement is not null)
        {
            return Statement;
        }

        return Action.HasValue
            ? ProgramVocabulary.NewCommand(Action.Value)
            : new BranchDraft(ProgramVocabulary.NewCondition(Condition!.Value));
    }

    /// <summary>The condition this payload puts into a condition slot.</summary>
    public ConditionDraft ToCondition() => ProgramVocabulary.NewCondition(Condition!.Value);

    /// <summary>
    /// True when <paramref name="target"/> lies inside the statement being dragged. Dropping a
    /// branch into its own body would detach the whole subtree from its rule, and the block would
    /// vanish from the canvas with nothing to say where it went.
    /// </summary>
    public bool WouldOrphan(List<StatementDraft> target)
    {
        if (Statement is null)
        {
            return false;
        }

        foreach (var body in Statement.Bodies())
        {
            if (ReferenceEquals(body, target))
            {
                return true;
            }
        }

        return false;
    }
}
