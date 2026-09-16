using System;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// What the inspector's construction button asked Operations to open with — a build target (an
/// unbuilt slot to prefill the Build composer with) or a plan to select directly. Exactly one of
/// the two is set, never both: the button that creates this decided which case applies before
/// firing <see cref="ShellActions.OperationsRequested"/>.
/// </summary>
public readonly record struct PendingOperationsTarget(ExecutorId? BuildTarget = null, PlanId? Plan = null);

/// <summary>
/// The whole surface a panel is allowed to touch. Deliberately tiny: a panel that needs more
/// than this is reaching for state it should have been handed in its snapshot.
/// </summary>
public sealed class ShellContext
{
    public ShellContext(ShellActions actions) => Actions = actions;

    public ShellActions Actions { get; }

    /// <summary>
    /// What is selected in the focus view. Held here rather than passed with the selection event
    /// so that a panel mounted <em>after</em> the selection was made renders it, instead of
    /// showing an empty state until the player clicks again.
    /// </summary>
    public GraphSelection? CurrentSelection { get; set; }

    /// <summary>
    /// A live read into the kernel, bound to <see cref="SimulationDriver.Draft"/>. It is the one
    /// exception to "a panel reaches for nothing but its snapshot": a draft being composed is a
    /// hypothesis, not committed state, and no snapshot carries one — the Operations composer has
    /// to ask the planner again on every edit, against however the vessel stands right now, and
    /// there is nowhere else in this tiny surface for a question like that to live. Set once, by
    /// <see cref="ShellRoot"/>, never invoked before it is.
    /// </summary>
    public Func<ItemAmount, StorageId?, ExecutorId?, PlanDraft>? ComposeDraft { get; set; }

    /// <summary>
    /// Set by <see cref="ShellRoot"/> when the inspector's construction button fires
    /// <see cref="ShellActions.OperationsRequested"/>, and consumed — read once, then cleared — by
    /// <see cref="OperationsFocus.OnMount"/>. The same reason <see cref="CurrentSelection"/> is parked
    /// here rather than passed with the event: the <c>OperationsFocus</c> that will read this does not
    /// exist yet when the button is pressed.
    /// </summary>
    public PendingOperationsTarget? PendingOperationsTarget { get; set; }

    /// <summary>
    /// The base graph's camera: its zoom step index and its pan offset. Godot state rather than
    /// simulation state, parked here for exactly the reason <see cref="CurrentSelection"/> is —
    /// <see cref="Zone.Show"/> frees and rebuilds the panel on every view change, so a value that has
    /// to survive a trip to Operations and back cannot live in the panel's own fields. The default
    /// is <c>BaseGraphFocus</c>'s own resting step, so a session that never touches the wheel opens
    /// exactly where it always did.
    /// <para>
    /// Session-lifetime and deliberately not written to <c>user://layout.json</c>: that file is a
    /// seven-field record every existing player already has, and growing it would change a file on
    /// disk to promise something further than "coming back from another view keeps your place" —
    /// which is the whole of what was asked. See the design spec's Decision 10.
    /// </para>
    /// </summary>
    public int GraphZoom { get; set; } = BaseGraphFocus.RestingZoom;

    /// <summary>The pan half of the camera. See <see cref="GraphZoom"/>.</summary>
    public Vector2 GraphPan { get; set; }
}
