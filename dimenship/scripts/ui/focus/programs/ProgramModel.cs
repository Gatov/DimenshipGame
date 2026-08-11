using System.Collections.Generic;
using System.Linq;

namespace Dimenship.Ui;

// The program model the concept mock edits. It mirrors the shapes in
// docs/superpowers/specs/2026-08-11-programming-view-design.md one for one, with two deliberate
// differences that belong to the mock and must not survive into the real build:
//
//   1. These are mutable classes, not immutable records. An editor over immutable records needs a
//      tree-rewrite path on every edit, which is real work with no usability payoff at the stage
//      this mock exists to answer. ProgramDraft.Clone is what undo uses instead.
//
//   2. They live in the Godot assembly rather than Dimenship.Core/Programs, so the spike cannot
//      break the tested kernel and reverts in one commit. When the real system ships they move to
//      Core and become records; nothing else about them changes.
//
// Nothing here executes. No runtime reads a ProgramDraft, no command reaches the engine, and the
// vessel is unaffected by every edit the player makes.

/// <summary>What a block is, which is the only thing its fill colour encodes.</summary>
public enum BlockKind
{
    /// <summary>A rule's own header: its number, priority and cooldown.</summary>
    Control,

    /// <summary>A predicate. Never a statement — a condition is always something's gate.</summary>
    Condition,

    /// <summary>A bounded command. The leaf of every path.</summary>
    Action,

    /// <summary>An <c>IF</c> / <c>ELSE IF</c> / <c>ELSE</c> arm's header.</summary>
    Branch,
}

/// <summary>
/// Every member names a field <see cref="Core.Simulation.WorldSnapshot"/> already carries. The
/// spec's <c>TicksSinceRuleFired</c> and <c>ExecutorBlockedBy</c> are omitted here only because
/// neither reads differently in an editor that never runs.
/// </summary>
public enum ConditionKind
{
    StorageItemAmount,
    StorageFillPercent,
    VesselItemAmount,
    VesselItemTrend,
    ExecutorStatusIs,
    ExecutorQueueLength,
    EnergyReservePercent,
}

/// <summary>
/// The whole language. Each member maps to one engine command in the real build; the mock issues
/// none of them.
/// </summary>
public enum ActionKind
{
    Produce,
    Transfer,
    SetPriority,
    Hold,
    Reserve,
}

public enum Comparison
{
    LessThan,
    LessOrEqual,
    Equal,
    NotEqual,
    GreaterOrEqual,
    GreaterThan,
}

/// <summary>What a slot offers the player. Every slot is one of these — there is no free text.</summary>
public enum SlotKind
{
    Item,
    Storage,
    Executor,
    Schematic,
    Comparison,
    ExecutorStatus,
    Trend,
    Toggle,
    Number,
}

/// <summary>
/// One editable value in a block. A choice slot carries an <see cref="Id"/> out of the vocabulary's
/// option list; a number slot carries a <see cref="Number"/> its <see cref="SlotSpec"/> bounds.
/// Both fields exist on every slot rather than being split into two types, because the editor
/// treats slots uniformly and a two-type split would buy a cast at every site.
/// </summary>
public sealed class SlotValue
{
    public SlotValue(string id) => Id = id;

    public SlotValue(long number) => Number = number;

    public string Id { get; set; } = string.Empty;

    public long Number { get; set; }

    public SlotValue Clone() => new(Id) { Number = Number };
}

/// <summary>A predicate: a kind, and the slots that kind declares.</summary>
public sealed class ConditionDraft
{
    public ConditionDraft(ConditionKind kind, params SlotValue[] slots)
    {
        Kind = kind;
        Slots = slots.ToList();
    }

    public ConditionKind Kind { get; set; }

    public List<SlotValue> Slots { get; }

    public ConditionDraft Clone()
    {
        var copy = new ConditionDraft(Kind);
        copy.Slots.AddRange(Slots.Select(slot => slot.Clone()));
        return copy;
    }
}

/// <summary>A statement: either a <see cref="BranchDraft"/> or a <see cref="CommandDraft"/>.</summary>
public abstract class StatementDraft
{
    public abstract StatementDraft Clone();

    /// <summary>
    /// Every statement list inside this statement, itself included in the walk. Used to refuse a
    /// drag that would drop a branch inside its own body, which would detach the subtree from the
    /// rule and leave it unreachable.
    /// </summary>
    public abstract IEnumerable<List<StatementDraft>> Bodies();
}

/// <summary>A bounded command. The leaf of every path.</summary>
public sealed class CommandDraft : StatementDraft
{
    public CommandDraft(ActionKind kind, params SlotValue[] slots)
    {
        Kind = kind;
        Slots = slots.ToList();
    }

    public ActionKind Kind { get; set; }

    public List<SlotValue> Slots { get; }

    public override StatementDraft Clone()
    {
        var copy = new CommandDraft(Kind);
        copy.Slots.AddRange(Slots.Select(slot => slot.Clone()));
        return copy;
    }

    public override IEnumerable<List<StatementDraft>> Bodies() =>
        System.Array.Empty<List<StatementDraft>>();
}

/// <summary>One arm of a branch ladder: a condition, and what runs when it is the first to match.</summary>
public sealed class BranchArmDraft
{
    public BranchArmDraft(ConditionDraft when) => When = when;

    public ConditionDraft When { get; set; }

    public List<StatementDraft> Then { get; } = new();

    public BranchArmDraft Clone()
    {
        var copy = new BranchArmDraft(When.Clone());
        copy.Then.AddRange(Then.Select(statement => statement.Clone()));
        return copy;
    }
}

/// <summary>
/// A whole <c>IF</c> / <c>ELSE IF</c> … / <c>ELSE</c> ladder, evaluated in order, first match wins.
/// It is one statement rather than an <c>Else</c> nested inside an <c>If</c> because that is how
/// the player reads it, and because a nested shape lets an editor produce an <c>ELSE</c> with no
/// <c>IF</c> — a thing the language has no meaning for.
/// </summary>
public sealed class BranchDraft : StatementDraft
{
    public BranchDraft(ConditionDraft when) => Arms.Add(new BranchArmDraft(when));

    private BranchDraft()
    {
    }

    public List<BranchArmDraft> Arms { get; } = new();

    /// <summary>The <c>ELSE</c> body. Null when the ladder has no final arm at all.</summary>
    public List<StatementDraft>? Otherwise { get; set; }

    public override StatementDraft Clone()
    {
        var copy = new BranchDraft();
        copy.Arms.AddRange(Arms.Select(arm => arm.Clone()));

        if (Otherwise is not null)
        {
            copy.Otherwise = Otherwise.Select(statement => statement.Clone()).ToList();
        }

        return copy;
    }

    public override IEnumerable<List<StatementDraft>> Bodies()
    {
        foreach (var arm in Arms)
        {
            yield return arm.Then;

            foreach (var nested in arm.Then.SelectMany(statement => statement.Bodies()))
            {
                yield return nested;
            }
        }

        if (Otherwise is null)
        {
            yield break;
        }

        yield return Otherwise;

        foreach (var nested in Otherwise.SelectMany(statement => statement.Bodies()))
        {
            yield return nested;
        }
    }
}

/// <summary>
/// One rule. A null <see cref="When"/> is an unconditional standing instruction that fires every
/// tick it is not on cooldown — the honest expression of that, rather than a condition that is
/// always true which the validator would then have to recognise.
/// </summary>
public sealed class RuleDraft
{
    public RuleDraft(ConditionDraft? when = null) => When = when;

    public ConditionDraft? When { get; set; }

    public List<StatementDraft> Then { get; } = new();

    public int Priority { get; set; }

    public long CooldownTicks { get; set; }

    public RuleDraft Clone()
    {
        var copy = new RuleDraft(When?.Clone()) { Priority = Priority, CooldownTicks = CooldownTicks };
        copy.Then.AddRange(Then.Select(statement => statement.Clone()));
        return copy;
    }
}

/// <summary>
/// A program as the library lists it and the editor edits it. The descriptive fields —
/// <see cref="Scope"/>, <see cref="Tier"/>, <see cref="Reliability"/> — are the design document's
/// program properties, carried as text because in the mock they describe and do not gate.
/// </summary>
public sealed class ProgramDraft
{
    public required string Name { get; set; }

    public required string Description { get; set; }

    public required string Scope { get; set; }

    public required string Tier { get; set; }

    public required string Reliability { get; set; }

    public required string Version { get; set; }

    /// <summary>The tier's rule maximum, rendered beside the count. Balance, so a plain number.</summary>
    public required int RuleLimit { get; set; }

    public List<RuleDraft> Rules { get; } = new();

    public ProgramDraft Clone()
    {
        var copy = new ProgramDraft
        {
            Name = Name,
            Description = Description,
            Scope = Scope,
            Tier = Tier,
            Reliability = Reliability,
            Version = Version,
            RuleLimit = RuleLimit,
        };

        copy.Rules.AddRange(Rules.Select(rule => rule.Clone()));
        return copy;
    }
}
