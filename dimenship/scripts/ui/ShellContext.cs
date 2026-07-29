namespace Dimenship.Ui;

/// <summary>
/// The whole surface a panel is allowed to touch. Deliberately tiny: a panel that needs more
/// than this is reaching for state it should have been handed in its snapshot.
/// </summary>
public sealed class ShellContext
{
    public ShellContext(ShellActions actions) => Actions = actions;

    public ShellActions Actions { get; }
}
