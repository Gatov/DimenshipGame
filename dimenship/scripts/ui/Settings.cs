using System;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The player's settings for this process, and the only way to change them.
/// <para>
/// Static rather than an autoload because the start screen and the shell are separate scenes with
/// no shared root: a settings menu reachable from both has to outlive <c>ChangeSceneToFile</c>,
/// and an autoload would be a node in the tree that exists solely to hold nine values. The file is
/// read once, on first use.
/// </para>
/// <para>
/// Applying and persisting are separable because a slider changes continuously. <see
/// cref="Preview"/> applies without writing, so dragging the volume is heard immediately without
/// a disk write per frame, and <see cref="Persist"/> writes once the drag ends. What is applied
/// and what is on disk are tracked separately so that a preview followed by a persist cannot be
/// mistaken for a no-op and silently dropped — which is the bug that produces a menu whose
/// switches revert on restart.
/// </para>
/// </summary>
public static class Settings
{
    private static SettingsState? _applied;
    private static SettingsState? _saved;

    /// <summary>
    /// Raised after a change has been applied. Subscribers must unsubscribe when they leave the
    /// tree: this event outlives every scene, so a node that forgets is a node the event keeps
    /// alive and calls into after Godot has freed it.
    /// </summary>
    public static event Action<SettingsState>? Changed;

    public static SettingsState Current
    {
        get
        {
            if (_applied is null)
            {
                _applied = SettingsStore.Load(SettingsState.Defaults).State;
                _saved = _applied;
            }

            return _applied;
        }
    }

    /// <summary>
    /// Pushes the current settings into the engine without writing anything. Called by whichever
    /// scene loads first, because the engine starts on the values in <c>project.godot</c> and
    /// nothing else will correct them.
    /// </summary>
    public static void ApplyTo(SceneTree tree) => SettingsApplier.Apply(Current, tree);

    /// <summary>Applies and announces a change without writing it. Pair with <see cref="Persist"/>.</summary>
    public static void Preview(SettingsState state, SceneTree tree)
    {
        if (state == Current)
        {
            return;
        }

        _applied = state;
        SettingsApplier.Apply(state, tree);
        Changed?.Invoke(state);
    }

    /// <summary>Applies, announces and writes. The path for anything that changes in one step.</summary>
    public static void Change(SettingsState state, SceneTree tree)
    {
        Preview(state, tree);
        Persist();
    }

    public static void Persist()
    {
        if (_applied is null || _applied == _saved)
        {
            return;
        }

        SettingsStore.Save(_applied);
        _saved = _applied;
    }
}
