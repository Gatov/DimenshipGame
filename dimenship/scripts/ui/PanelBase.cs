using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Every mountable surface in the shell, focus views included. A panel never reads the engine;
/// the shell hands it a snapshot. That symmetry is what lets a focus view be swapped into the
/// centre zone with no special-casing.
/// </summary>
public abstract partial class PanelBase : PanelContainer
{
    protected PanelBase()
    {
        // Constructed here, not in _Ready: every subclass overrides _Ready to build its contents
        // and none call base, and the pane has to be child zero so it draws beneath everything
        // they add. Null material means the backdrop asset is missing — the panel then just shows
        // the theme's flat fill.
        if (ShellBackdrop.CreateFrostMaterial() is { } frost)
        {
            AddChild(new FrostPane(this) { Name = "FrostPane", Material = frost });
        }
    }

    public abstract PanelId Id { get; }

    public abstract string Title { get; }

    /// <summary>Called once, after the node enters the tree and before the first snapshot.</summary>
    public virtual void OnMount(ShellContext context)
    {
    }

    /// <summary>Called only when the snapshot has actually changed.</summary>
    public abstract void OnSnapshot(WorldSnapshot snapshot);

    public virtual void OnUnmount()
    {
    }
}
