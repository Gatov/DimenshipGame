using System.Collections.Generic;
using System.Linq;

namespace Dimenship.Ui;

// The model the loadout composer mock edits. It mirrors
// docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md, with the same two deliberate
// differences ProgramModel carries, and for the same reasons:
//
//   1. LoadoutDraft is a mutable class, not an immutable record. An editor over immutable records
//      needs a rewrite path on every edit, which is real work with no usability payoff at the stage
//      this mock exists to answer. LoadoutDraft.Clone is what undo uses instead.
//
//   2. It lives in the Godot assembly rather than in Dimenship.Core, so the spike cannot break the
//      tested kernel and reverts in one commit. When the real system ships, the model moves to Core
//      and becomes records.
//
// Nothing here is simulated. No robot exists, no socket is a storage, no part is ever built, and
// the vessel is unaffected by every edit the player makes. The one thing the view reads for real is
// the vessel's material stock, and that comes from WorldSnapshot rather than from anything here.

/// <summary>
/// What a socket accepts, and therefore what a part is. Exactly the GDD §10 MVP list — "tool,
/// sensor, storage, power/defense, basic investigation module, basic weapon/armor package" — split
/// into the six kinds a composer needs to keep separate, and no wider. A seventh kind would be
/// content the design budget has not authorised.
/// </summary>
public enum SocketKind
{
    Tool,
    Sensor,
    Cargo,
    Power,
    Defense,
    Investigation,
}

/// <summary>Where a template stands. Shown as a word, never as a colour alone.</summary>
public enum Verdict
{
    /// <summary>Every socket filled and the power budget balances.</summary>
    Complete,

    /// <summary>
    /// A socket is empty. Legal, not an error: GDD §5.10 says a machine missing a part runs at its
    /// unequipped rating, so an empty socket is a consequence to read rather than a state to
    /// forbid.
    /// </summary>
    Incomplete,

    /// <summary>The fitted parts draw more power than the fitted cores supply.</summary>
    OverBudget,
}

/// <summary>
/// The six numbers a frame or a part contributes. Integers, and milli-nothing: these are the mock's
/// own invented units, and inventing a precision for them would be pretending the balance work has
/// happened. Item quantities are milli-units, because those are real and shipped.
/// <para>
/// <see cref="Power"/> is a <b>net budget</b>, not a draw: a core supplies positive and everything
/// else consumes negative, so a template can be over-budget. It is the mock's only invented
/// constraint, and without a constraint composing is shopping.
/// </para>
/// </summary>
public readonly record struct StatBlock(
    long Mass,
    long Power,
    long Durability,
    long Cargo,
    long Scan,
    long WorkRate)
{
    public static StatBlock operator +(StatBlock a, StatBlock b) => new(
        a.Mass + b.Mass,
        a.Power + b.Power,
        a.Durability + b.Durability,
        a.Cargo + b.Cargo,
        a.Scan + b.Scan,
        a.WorkRate + b.WorkRate);
}

/// <summary>
/// One line of a build cost: an amount of a shipped item, in milli-units. The item id is a bare
/// string rather than an <c>ItemId</c> because the composer never hands it to the kernel — it only
/// looks it up in the snapshot's resources to ask what the vessel holds.
/// </summary>
public sealed record ItemCost(string ItemId, long Amount);

/// <summary>
/// A chassis: the sockets it offers, the rating it runs at with every one of them empty, and what
/// building the bare frame costs.
/// <para>
/// <see cref="Baseline"/> is GDD §5.10's <i>unequipped rating</i>, and it is what makes an empty
/// socket legible instead of a hole: the rollup has a number to show for a stat nothing is
/// contributing to.
/// </para>
/// </summary>
public sealed record FrameDef(
    string Id,
    string Label,
    IReadOnlyList<SocketKind> Sockets,
    StatBlock Baseline,
    IReadOnlyList<ItemCost> Cost,
    string Note);

/// <summary>
/// A thing that occupies one socket. Called a <b>part</b> and not a module on purpose: <c>module</c>
/// is a shipped bulk commodity (<c>"id": "module"</c>, <i>Robot Module</i>), and the GDD glossary
/// has the collision between that and its <i>Fitted Module</i> open. The mock takes a third word
/// rather than closing a vocabulary question the foundation document is still holding.
/// </summary>
public sealed record PartDef(
    string Id,
    string Label,
    SocketKind Kind,
    StatBlock Delta,
    IReadOnlyList<ItemCost> Cost,
    string Note);

/// <summary>
/// What the player authors: a name, a frame, and one fitted part per socket. Not a robot — there
/// are no robots — and not a saved object: a template is forgotten when the game closes, because a
/// template that survived would be a save format for a subsystem that does not exist.
/// </summary>
public sealed class LoadoutDraft
{
    public required string Name { get; set; }

    public required string Description { get; set; }

    public required string FrameId { get; set; }

    /// <summary>
    /// Parallel to the frame's socket list: <c>Fitted[i]</c> is what sits in socket <c>i</c>, or
    /// null for an empty one. Positional rather than keyed by <see cref="SocketKind"/>, because a
    /// frame may offer two sockets of the same kind and a dictionary would silently make them one.
    /// </summary>
    public required List<string?> Fitted { get; init; }

    public LoadoutDraft Clone() => new()
    {
        Name = Name,
        Description = Description,
        FrameId = FrameId,
        Fitted = new List<string?>(Fitted),
    };

    /// <summary>
    /// Swaps the frame, keeping every fitted part whose socket kind is unchanged at its index and
    /// dropping the rest. The alternative — clearing everything — punishes the player for trying a
    /// frame, and the alternative to that — hunting for a compatible socket elsewhere on the new
    /// frame — moves parts the player did not ask to move.
    /// </summary>
    public void Refit(FrameDef from, FrameDef to)
    {
        var kept = new List<string?>();

        for (var i = 0; i < to.Sockets.Count; i++)
        {
            var carried = i < from.Sockets.Count
                && i < Fitted.Count
                && from.Sockets[i] == to.Sockets[i];

            kept.Add(carried ? Fitted[i] : null);
        }

        FrameId = to.Id;
        Fitted.Clear();
        Fitted.AddRange(kept);
    }

    public int FilledSockets => Fitted.Count(part => part is not null);
}
