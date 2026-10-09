using System;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// A committed plan's controls (U1): priority, hold and release, cancel, and amending the goal.
/// Every one goes through <see cref="ShellActions.Execute"/>, the door the replay harness and any
/// future controller use, so the player never has a command a program cannot have (C0).
/// <para>
/// The controls are built once and refreshed per snapshot, never rebuilt. The detail rows below
/// them are cleared every snapshot, and a control rebuilt every tick would lose a quantity being
/// typed and close a priority list the player had open.
/// </para>
/// <para>
/// Nothing here decides whether a command is allowed. A finished plan disables the row because
/// there is nothing to act on; everything else is the kernel's to refuse, and its reason is shown
/// as it gave it. A second copy of those rules in the shell would drift from the first.
/// </para>
/// </summary>
public sealed partial class OperationsFocus
{
    private static readonly Priority[] Priorities = Enum.GetValues<Priority>();

    private Control _controlsRoot = null!;
    private OptionButton _priority = null!;
    private Button _hold = null!;
    private Button _cancel = null!;
    private SpinBox _amendQuantity = null!;
    private Button _amend = null!;
    private Label _commandFeedback = null!;

    /// <summary>The plan the controls were last set up for. The amend quantity is reset to its goal
    /// only when this changes, so a value being typed survives the next snapshot.</summary>
    private PlanId? _controlsPlan;

    /// <summary>Cancel throws away a plan's unstarted work and cannot be undone, so it takes a second
    /// press. Disarmed by any other control or by choosing another plan.</summary>
    private bool _cancelArmed;

    private Control BuildPlanControls()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);

        var first = new HBoxContainer();
        first.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(first);

        first.AddChild(Caption("PRIORITY:"));

        _priority = new OptionButton { FocusMode = FocusModeEnum.None };
        _priority.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        foreach (var priority in Priorities)
        {
            _priority.AddItem(priority.ToString().ToUpperInvariant());
        }

        _priority.ItemSelected += index =>
        {
            if (_selectedPlan is { } plan)
            {
                Issue(new SetPlanPriority(plan, Priorities[(int)index]));
            }
        };
        first.AddChild(_priority);

        _hold = ControlButton("HOLD");
        _hold.Pressed += OnHoldPressed;
        first.AddChild(_hold);

        _cancel = ControlButton("CANCEL");
        _cancel.Pressed += OnCancelPressed;
        first.AddChild(_cancel);

        var second = new HBoxContainer();
        second.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(second);

        second.AddChild(Caption("GOAL:"));

        _amendQuantity = new SpinBox
        {
            MinValue = 1,
            MaxValue = 1_000_000,
            Step = 1,
            CustomMinimumSize = new Vector2(90, 0),
            UpdateOnTextChanged = false,
        };
        _amendQuantity.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _amendQuantity.GetLineEdit().AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        second.AddChild(_amendQuantity);

        _amend = ControlButton("AMEND");
        _amend.Pressed += OnAmendPressed;
        second.AddChild(_amend);

        _commandFeedback = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _commandFeedback.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(_commandFeedback);

        _controlsRoot = column;
        return column;
    }

    /// <summary>
    /// Brings the controls in line with the selected plan. <see cref="OptionButton.Select"/> does
    /// not raise <c>ItemSelected</c>, so showing the plan's priority never issues a command.
    /// </summary>
    private void RefreshPlanControls(CommittedPlanState plan)
    {
        if (_controlsPlan != plan.Id)
        {
            _controlsPlan = plan.Id;
            _cancelArmed = false;
            _commandFeedback.Text = string.Empty;
            _amendQuantity.Value = Math.Max(1, plan.Goal.Quantity / MilliPerUnit);
        }

        var active = plan.State == PlanState.Active;
        _priority.Select(Array.IndexOf(Priorities, plan.Priority));
        _priority.Disabled = !active;
        _hold.Text = plan.Held ? "RELEASE" : "HOLD";
        _hold.Disabled = !active;
        _cancel.Text = _cancelArmed ? "CONFIRM CANCEL" : "CANCEL";
        _cancel.Disabled = !active;
        _amend.Disabled = !active;
        _amendQuantity.Editable = active;
    }

    private void OnHoldPressed()
    {
        if (_selectedPlan is not { } id || _lastSnapshot?.Plans.FirstOrDefault(p => p.Id == id) is not { } plan)
        {
            return;
        }

        Issue(plan.Held ? new ReleasePlan(id) : new HoldPlan(id));
    }

    private void OnCancelPressed()
    {
        if (_selectedPlan is not { } id)
        {
            return;
        }

        if (!_cancelArmed)
        {
            _cancelArmed = true;
            _cancel.Text = "CONFIRM CANCEL";
            return;
        }

        Issue(new CancelPlan(id));
    }

    private void OnAmendPressed()
    {
        if (_selectedPlan is { } id)
        {
            Issue(new AmendPlan(id, (long)_amendQuantity.Value * MilliPerUnit));
        }
    }

    /// <summary>
    /// Sends one command and says what became of it. Acceptance needs no words: the engine rebuilds
    /// its snapshot inside the command, so the next frame shows the plan as it now is, paused or
    /// not. A refusal is shown as the kernel worded it, with the planner's issues when an amend
    /// could not be planned.
    /// </summary>
    private void Issue(Command command)
    {
        _cancelArmed = false;
        _cancel.Text = "CANCEL";

        var result = _context?.Actions.Execute?.Invoke(command);
        if (result is CommandRefused refused)
        {
            var issues = string.Join(", ", refused.Issues.Select(i => DescribeIssue(i.Kind)).Distinct());
            _commandFeedback.Text = issues.Length > 0 ? $"REFUSED: {refused.Reason} ({issues})" : $"REFUSED: {refused.Reason}";
            _commandFeedback.AddThemeColorOverride("font_color", ShellPalette.StateFault);
        }
        else
        {
            _commandFeedback.Text = string.Empty;
        }
    }

    private static Label Caption(string text)
    {
        var caption = new Label { Text = text };
        caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return caption;
    }

    private static Button ControlButton(string text)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        return button;
    }
}
