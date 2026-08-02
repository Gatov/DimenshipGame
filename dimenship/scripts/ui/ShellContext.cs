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
}
