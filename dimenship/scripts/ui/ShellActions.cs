using System;
using System.Collections.Generic;
using Dimenship.Core.Planning;
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

    /// <summary>Open the settings menu. Routed like everything else, so the Vessel menu, a future
    /// accelerator and a command palette all reach one implementation.</summary>
    public Action? SettingsRequested;

    /// <summary>
    /// A node or edge was selected in the focus view, or the selection was cleared. Routed
    /// through here rather than the focus view reaching for the inspector zone directly, so that
    /// what a selection does to the layout stays one decision in one place.
    /// </summary>
    public Action<GraphSelection?>? SelectionChanged;

    /// <summary>Show the selected thing's detail, whatever the inspector zone is currently on.</summary>
    public Action? InspectRequested;

    /// <summary>
    /// The composer's plan was approved. Routed like a command rather than the Operations view
    /// reaching for <see cref="SimulationDriver.Commit"/> directly, the same reason every other
    /// verb goes through here: a command palette that later wants to approve a plan gets there
    /// with no second wiring path to keep in sync with this one. The composed plan is not kept
    /// anywhere past this call — it is discarded the moment it is committed, and what comes back
    /// afterwards is whatever the next snapshot's <c>Plans</c>/<c>Tasks</c> lists say happened.
    /// </summary>
    public Action<ProductionPlan>? PlanApproved;

    /// <summary>
    /// The inspector's construction action was pressed: open Operations with either a build target
    /// prefilled (an unbuilt slot with no plan yet) or an existing plan selected (one already targets
    /// it). One command rather than two, because the button that fires it already decided which case
    /// applies — see FacilityInspectorPanel.
    /// </summary>
    public Action<PendingOperationsTarget>? OperationsRequested;

    /// <summary>Focus views selectable by Ctrl+1..Ctrl+N, in registration order.</summary>
    public IReadOnlyList<PanelId> FocusOrder { get; set; } = Array.Empty<PanelId>();

    /// <summary>
    /// Set while a modal surface owns the keyboard. Suspending here rather than in the shell's
    /// input override is what keeps the accelerator table in one place: a modal that had to know
    /// which keys to swallow would be a second copy of that table, and the two would drift.
    /// </summary>
    public bool Suspended { get; set; }

    public void Handle(InputEvent @event)
    {
        if (Suspended)
        {
            return;
        }

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
