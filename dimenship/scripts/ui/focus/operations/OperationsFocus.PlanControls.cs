using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.ShellModel;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// A committed plan's controls (U1): priority, hold and release, cancel, amending the goal, and
/// moving a stage to another facility (U4, K6d's <see cref="MoveWork"/>).
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
    private OptionButton _moveSchematic = null!;
    private OptionButton _moveTo = null!;
    private Button _move = null!;
    private IReadOnlyList<MoveOption> _moveOptions = Array.Empty<MoveOption>();

    /// <summary>
    /// What the move lists were last filled from. They are refilled only when it changes, so a list
    /// the player has open survives the next snapshot, as the priority list does.
    /// </summary>
    private string? _moveSignature;

    /// <summary>The plan the controls were last set up for. The amend quantity is reset to its goal
    /// only when this changes, so a value being typed survives the next snapshot.</summary>
    private PlanId? _controlsPlan;

    /// <summary>Cancel throws away a plan's unstarted work and cannot be undone, so it takes a second
    /// press. Disarmed by any other control or by choosing another plan.</summary>
    private readonly ConfirmGate _cancelGate = new();

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
            if (_model.SelectedPlan is { } plan)
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

        var third = new HBoxContainer();
        third.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(third);

        third.AddChild(Caption("MOVE:"));
        _moveSchematic = new OptionButton { FocusMode = FocusModeEnum.None };
        _moveSchematic.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _moveSchematic.ItemSelected += _ => FillMoveTargets();
        third.AddChild(_moveSchematic);

        third.AddChild(Caption("TO"));
        _moveTo = new OptionButton { FocusMode = FocusModeEnum.None };
        _moveTo.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        third.AddChild(_moveTo);

        _move = ControlButton("MOVE");
        _move.Pressed += OnMovePressed;
        third.AddChild(_move);

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
    private void RefreshPlanControls(CommittedPlanState plan, WorldSnapshot snapshot)
    {
        if (_controlsPlan != plan.Id)
        {
            _controlsPlan = plan.Id;
            _cancelGate.Disarm();
            _commandFeedback.Text = string.Empty;
            _amendQuantity.Value = Math.Max(1, plan.Goal.Quantity / Units.MilliPerUnit);
            _moveSignature = null;
        }

        var active = plan.State == PlanState.Active;
        _priority.Select(Array.IndexOf(Priorities, plan.Priority));
        _priority.Disabled = !active;
        _hold.Text = plan.Held ? "RELEASE" : "HOLD";
        _hold.Disabled = !active;
        _cancel.Text = _cancelGate.Armed ? "CONFIRM CANCEL" : "CANCEL";
        _cancel.Disabled = !active;
        _amend.Disabled = !active;
        _amendQuantity.Editable = active;

        var options = MoveOptions.For(snapshot, ShellContent.Catalog.Schematics, plan.Id);
        var signature = string.Join(";", options.Select(o => $"{o.Schematic}>{string.Join(",", o.To)}"));
        if (signature != _moveSignature)
        {
            _moveSignature = signature;
            var kept = _moveOptions.ElementAtOrDefault(_moveSchematic.Selected)?.Schematic;
            _moveOptions = options;
            _moveSchematic.Clear();
            foreach (var option in options)
            {
                _moveSchematic.AddItem(option.Schematic.Value.ToUpperInvariant());
            }

            var index = options.ToList().FindIndex(o => o.Schematic == kept);
            if (options.Count > 0)
            {
                _moveSchematic.Select(Math.Max(0, index));
            }

            FillMoveTargets();
        }

        _moveSchematic.Disabled = !active || _moveOptions.Count == 0;
        _moveTo.Disabled = !active || _moveTo.ItemCount == 0;
        _move.Disabled = !active || _moveTo.ItemCount == 0;
    }

    private void OnHoldPressed()
    {
        if (_model.SelectedPlan is not { } id || _lastSnapshot?.Plans.FirstOrDefault(p => p.Id == id) is not { } plan)
        {
            return;
        }

        Issue(plan.Held ? new ReleasePlan(id) : new HoldPlan(id));
    }

    private void OnCancelPressed()
    {
        if (_model.SelectedPlan is not { } id)
        {
            return;
        }

        if (!_cancelGate.Press())
        {
            _cancel.Text = "CONFIRM CANCEL";
            return;
        }

        Issue(new CancelPlan(id));
    }

    private void OnAmendPressed()
    {
        if (_model.SelectedPlan is { } id)
        {
            Issue(new AmendPlan(id, (long)_amendQuantity.Value * Units.MilliPerUnit));
        }
    }

    /// <summary>
    /// Sends one command and says what became of it. Acceptance needs no words: the engine rebuilds
    /// its snapshot inside the command, so the next frame shows the plan as it now is, paused or
    /// not. A refusal is shown as the kernel worded it, with the planner's issues when an amend
    /// could not be planned.
    /// </summary>
    private CommandResult? Issue(Command command)
    {
        _cancelGate.Disarm();
        _cancel.Text = "CANCEL";

        var result = _context?.Actions.Execute?.Invoke(command);
        if (result is CommandRefused refused)
        {
            _commandFeedback.Text = Labels.Refusal(refused);
            _commandFeedback.AddThemeColorOverride("font_color", ShellPalette.StateFault);
        }
        else
        {
            _commandFeedback.Text = string.Empty;
        }

        return result;
    }

    /// <summary>
    /// The facilities the selected stage can go to, labelled as the graph labels them. The choice
    /// is kept across a refill when it is still offered.
    /// </summary>
    private void FillMoveTargets()
    {
        var kept = _moveTo.Selected >= 0 ? _moveTo.GetItemMetadata(_moveTo.Selected).AsString() : null;
        _moveTo.Clear();
        if (_moveOptions.ElementAtOrDefault(_moveSchematic.Selected) is not { } option)
        {
            return;
        }

        for (var i = 0; i < option.To.Count; i++)
        {
            var id = option.To[i];
            var label = _lastSnapshot?.Executors.FirstOrDefault(e => e.Id == id)?.Label ?? id.Value;
            _moveTo.AddItem(label.ToUpperInvariant());
            _moveTo.SetItemMetadata(i, id.Value);
            if (id.Value == kept)
            {
                _moveTo.Select(i);
            }
        }

        if (_moveTo.Selected < 0 && _moveTo.ItemCount > 0)
        {
            _moveTo.Select(0);
        }

        // Here as well as in RefreshPlanControls: choosing another stage refills the targets at
        // once, and while paused no snapshot comes to re-enable MOVE for a stage that has some.
        var active = _lastSnapshot?.Plans.FirstOrDefault(p => p.Id == _model.SelectedPlan)?.State == PlanState.Active;
        _moveTo.Disabled = !active || _moveTo.ItemCount == 0;
        _move.Disabled = !active || _moveTo.ItemCount == 0;
    }

    /// <summary>
    /// Sends <see cref="MoveWork"/> (K6d). Whether any run can move is the kernel's to decide: a
    /// refusal, such as material already sent or no line, is shown as it worded it.
    /// </summary>
    private void OnMovePressed()
    {
        if (_model.SelectedPlan is not { } plan
            || _moveOptions.ElementAtOrDefault(_moveSchematic.Selected) is not { } option
            || _moveTo.Selected < 0)
        {
            return;
        }

        var to = new ExecutorId(_moveTo.GetItemMetadata(_moveTo.Selected).AsString());
        if (Issue(new MoveWork(plan, option.Schematic, to)) is CommandAccepted)
        {
            _commandFeedback.Text =
                $"MOVED {option.Schematic.Value.ToUpperInvariant()} TO {_moveTo.GetItemText(_moveTo.Selected)}";
            _commandFeedback.AddThemeColorOverride("font_color", ShellPalette.StateOk);
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
