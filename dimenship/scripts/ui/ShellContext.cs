using System;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Shell;

namespace Dimenship.Ui;

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
    /// A live read into the kernel, bound to <see cref="SimulationDriver.Plan"/>. It is the one
    /// exception to "a panel reaches for nothing but its snapshot": a plan being composed is a
    /// hypothesis, not committed state, and no snapshot carries one — the Operations composer has
    /// to ask the planner again on every edit, against however the vessel stands right now, and
    /// there is nowhere else in this tiny surface for a question like that to live. Set once, by
    /// <see cref="ShellRoot"/>, never invoked before it is.
    /// </summary>
    public Func<ItemAmount, StorageId?, ProductionPlan>? ComposePlan { get; set; }
}
