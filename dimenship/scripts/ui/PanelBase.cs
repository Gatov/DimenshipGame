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
