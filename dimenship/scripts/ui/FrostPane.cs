using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Draws the frosted fill for one or more host controls.
///
/// A Node2D rather than a Control on purpose: containers lay out every Control child — a panel's
/// children inset by the stylebox content margins, a rail's stacked into the column — whereas a
/// Node2D is left out of that pass entirely and can cover its hosts exactly. Draw order is tree
/// order, so the pane must sit ahead of whatever it frosts: child zero of a panel, ahead of the
/// buttons in a rail.
/// </summary>
public sealed partial class FrostPane : Node2D
{
    private readonly Control[] _hosts;

    public FrostPane(params Control[] hosts) => _hosts = hosts;

    public override void _Ready()
    {
        // ItemRectChanged, not Resized: a rail button that keeps its size but slides up the column
        // still needs the pane redrawn under its new position.
        foreach (var host in _hosts)
        {
            host.ItemRectChanged += QueueRedraw;
        }
    }

    public override void _Draw()
    {
        // Hosts are not necessarily in this node's coordinate space — a panel's pane is its own
        // child, a rail's pane is a sibling of the buttons — so go through global space.
        var toLocal = GetGlobalTransform().AffineInverse();

        foreach (var host in _hosts)
        {
            // Grow(-1) insets by the stylebox's 1px border so the hairline survives. Panels have
            // no other stake in the pane's position: the shader picks its sample from SCREEN_UV,
            // so a pane that moves re-samples on its own.
            var rect = host.GetGlobalRect().Grow(-1);
            if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            {
                continue;
            }

            DrawRect(new Rect2(toLocal * rect.Position, rect.Size), Colors.White);
        }
    }
}
