using System;
using System.Collections.Generic;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Single dispatch point for everything the shell can be asked to do. Accelerators and buttons
/// both route through here rather than binding handlers directly, so a command palette can be
/// added later without rewiring every call site.
/// </summary>
public sealed class ShellActions
{
    public Action<PanelId>? FocusRequested;
    public Action? PauseToggled;
    public Action? StepRequested;
    public Action? SpeedUpRequested;
    public Action? SpeedDownRequested;
    public Action? InspectorToggled;
    public Action? ConsoleToggled;
    public Action? FocusReleased;

    /// <summary>
    /// A node or edge was selected in the focus view, or the selection was cleared. Routed
    /// through here rather than the focus view reaching for the inspector zone directly, so that
    /// what a selection does to the layout stays one decision in one place.
    /// </summary>
    public Action<GraphSelection?>? SelectionChanged;

    /// <summary>Show the selected thing's detail, whatever the inspector zone is currently on.</summary>
    public Action? InspectRequested;

    /// <summary>Focus views selectable by Ctrl+1..Ctrl+N, in registration order.</summary>
    public IReadOnlyList<PanelId> FocusOrder { get; set; } = Array.Empty<PanelId>();

    public void Handle(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Space when !key.CtrlPressed:
                PauseToggled?.Invoke();
                break;
            case Key.Period when !key.CtrlPressed:
                StepRequested?.Invoke();
                break;
            case Key.Bracketright when !key.CtrlPressed:
                SpeedUpRequested?.Invoke();
                break;
            case Key.Bracketleft when !key.CtrlPressed:
                SpeedDownRequested?.Invoke();
                break;
            case Key.Escape:
                FocusReleased?.Invoke();
                break;
            case Key.I when key.CtrlPressed:
                InspectorToggled?.Invoke();
                break;
            case Key.Quoteleft when key.CtrlPressed:
                ConsoleToggled?.Invoke();
                break;
            case >= Key.Key1 and <= Key.Key9 when key.CtrlPressed:
                var index = (int)(key.Keycode - Key.Key1);
                if (index < FocusOrder.Count)
                {
                    FocusRequested?.Invoke(FocusOrder[index]);
                }

                break;
        }
    }
}
