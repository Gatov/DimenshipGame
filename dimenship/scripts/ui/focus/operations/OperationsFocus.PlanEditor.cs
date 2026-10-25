using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The editable plan composer: row list, revision stack, and adjustment controls. Kept in a
/// partial so the committed-plan list and detail view stay readable beside it.
/// </summary>
public sealed partial class OperationsFocus
{
    /// <summary>How many process frames a REPLANNED flash stays visible after the flag clears.</summary>
    private const ulong ReplannedFlashFrames = 120;

    private readonly Dictionary<long, ulong> _replannedFlashUntil = new();

    private bool _addMoveVisible;
    private bool _applyingDraft;

    private VBoxContainer _stepsBody = null!;
    private ScrollContainer _stepsScroll = null!;
    private Control _addMoveRow = null!;
    private OptionButton _addMoveItem = null!;
    private OptionButton _addMoveFrom = null!;
    private OptionButton _addMoveTo = null!;
    private OptionButton _addMoveLine = null!;
    private SpinBox _addMoveQuantity = null!;
    private Label _coverage = null!;
    private VBoxContainer _issuesBody = null!;
    private PanelContainer _issuesSection = null!;
    private Button _addMoveButton = null!;
    private Button _readjustButton = null!;
    private Button _unlockAllButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;

    private Control BuildPlanEditorChrome()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(BuildEditorToolbar());

        _addMoveRow = BuildAddMoveRow();
        _addMoveRow.Visible = false;
        column.AddChild(_addMoveRow);

        _stepsScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 160),
        };
        column.AddChild(_stepsScroll);

        _stepsBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _stepsBody.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        _stepsScroll.AddChild(_stepsBody);

        return column;
    }

    private Control BuildEditorToolbar()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        _addMoveButton = ToolbarButton("ADD MOVE");
        _addMoveButton.Pressed += ToggleAddMoveRow;
        row.AddChild(_addMoveButton);

        _readjustButton = ToolbarButton("RE-ADJUST");
        _readjustButton.Pressed += () =>
        {
            _model.ApplyEdit(new ReAdjust());
            RenderPreview(_model.Draft);
        };
        row.AddChild(_readjustButton);

        _unlockAllButton = ToolbarButton("UNLOCK ALL");
        _unlockAllButton.Pressed += () =>
        {
            _model.ApplyEdit(new UnlockAll());
            RenderPreview(_model.Draft);
        };
        row.AddChild(_unlockAllButton);

        _undoButton = ToolbarButton("UNDO");
        _undoButton.Pressed += UndoEdit;
        row.AddChild(_undoButton);

        _redoButton = ToolbarButton("REDO");
        _redoButton.Pressed += RedoEdit;
        row.AddChild(_redoButton);

        return row;
    }

    private static Button ToolbarButton(string text)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return button;
    }

    private Control BuildAddMoveRow()
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", ShellTheme.Box());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        box.AddChild(column);

        var caption = ShellTheme.Text("NEW MOVE", ShellTheme.Caption);
        column.AddChild(caption);

        var fields = new HBoxContainer();
        fields.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(fields);

        _addMoveItem = AddMoveField(fields, "ITEM");
        _addMoveFrom = AddMoveField(fields, "FROM");
        _addMoveTo = AddMoveField(fields, "TO");
        _addMoveQuantity = new SpinBox
        {
            MinValue = 1,
            MaxValue = 1_000_000,
            Step = 1,
            Value = 1,
            CustomMinimumSize = new Vector2(72, 0),
            UpdateOnTextChanged = false,
        };
        fields.AddChild(WrapAddMoveField("QTY", _addMoveQuantity));

        _addMoveLine = AddMoveField(fields, "LINE");
        _addMoveFrom.ItemSelected += _ => RefreshAddMoveLines();
        _addMoveTo.ItemSelected += _ => RefreshAddMoveLines();

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(actions);

        var confirm = ToolbarButton("ADD");
        confirm.Pressed += ConfirmAddMove;
        actions.AddChild(confirm);

        var cancel = ToolbarButton("CANCEL");
        cancel.Pressed += HideAddMoveRow;
        actions.AddChild(cancel);

        return box;
    }

    private static OptionButton AddMoveField(HBoxContainer row, string label)
    {
        var button = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(WrapAddMoveField(label, button));
        return button;
    }

    private static Control WrapAddMoveField(string label, Control field)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        var caption = ShellTheme.Text(label, ShellTheme.Caption);
        column.AddChild(caption);
        column.AddChild(field);

        return column;
    }

    private void ToggleAddMoveRow()
    {
        _addMoveVisible = !_addMoveVisible;
        if (_addMoveVisible)
        {
            PopulateAddMoveFields();
        }

        _addMoveRow.Visible = _addMoveVisible;
    }

    private void HideAddMoveRow()
    {
        _addMoveVisible = false;
        _addMoveRow.Visible = false;
    }

    private void PopulateAddMoveFields()
    {
        var snapshot = _lastSnapshot;
        if (snapshot is null)
        {
            return;
        }

        var catalog = ShellContent.Catalog;

        _addMoveItem.Clear();
        var itemIndex = 0;
        foreach (var item in catalog.Items.OrderBy(i => i.Label))
        {
            _addMoveItem.AddItem(item.Label.ToUpperInvariant());
            if (itemIndex == 0)
            {
                _addMoveItem.Selected = 0;
            }

            itemIndex++;
        }

        _addMoveFrom.Clear();
        _addMoveTo.Clear();
        foreach (var storage in snapshot.Storages)
        {
            _addMoveFrom.AddItem(storage.Label.ToUpperInvariant());
            _addMoveTo.AddItem(storage.Label.ToUpperInvariant());
        }

        if (snapshot.Storages.Count > 0)
        {
            _addMoveFrom.Selected = 0;
            _addMoveTo.Selected = Math.Min(1, snapshot.Storages.Count - 1);
        }

        RefreshAddMoveLines();
    }

    private void RefreshAddMoveLines()
    {
        var snapshot = _lastSnapshot;
        if (snapshot is null)
        {
            return;
        }

        var from = SelectedStorage(_addMoveFrom);
        var to = SelectedStorage(_addMoveTo);

        _addMoveLine.Clear();
        var index = 0;
        foreach (var line in snapshot.Transports.Where(t => t.Built && t.From == from && t.To == to))
        {
            _addMoveLine.AddItem(line.Label.ToUpperInvariant());
            if (index == 0)
            {
                _addMoveLine.Selected = 0;
            }

            index++;
        }

        if (index == 0)
        {
            _addMoveLine.AddItem("NO DIRECT LINE");
            _addMoveLine.Disabled = true;
        }
        else
        {
            _addMoveLine.Disabled = false;
        }
    }

    private StorageId SelectedStorage(OptionButton button)
    {
        var snapshot = _lastSnapshot;
        if (snapshot is null || button.Selected < 0 || button.Selected >= snapshot.Storages.Count)
        {
            return default;
        }

        return snapshot.Storages[button.Selected].Id;
    }

    private ExecutorId SelectedTransport(OptionButton button)
    {
        var snapshot = _lastSnapshot;
        if (snapshot is null)
        {
            return default;
        }

        var from = SelectedStorage(_addMoveFrom);
        var to = SelectedStorage(_addMoveTo);
        var lines = snapshot.Transports
            .Where(t => t.Built && t.From == from && t.To == to)
            .ToList();
        if (button.Selected < 0 || button.Selected >= lines.Count)
        {
            return default;
        }

        return lines[button.Selected].Id;
    }

    private ItemId SelectedAddMoveItem()
    {
        var catalog = ShellContent.Catalog;
        var ordered = catalog.Items.OrderBy(i => i.Label).ToList();
        if (_addMoveItem.Selected < 0 || _addMoveItem.Selected >= ordered.Count)
        {
            return default;
        }

        return ordered[_addMoveItem.Selected].Id;
    }

    private void ConfirmAddMove()
    {
        if (_addMoveLine.Disabled)
        {
            return;
        }

        var item = SelectedAddMoveItem();
        var from = SelectedStorage(_addMoveFrom);
        var to = SelectedStorage(_addMoveTo);
        var line = SelectedTransport(_addMoveLine);
        var quantity = (long)_addMoveQuantity.Value * Units.MilliPerUnit;

        _model.ApplyEdit(new AddMove(item, from, to, quantity, line));
        HideAddMoveRow();
        RenderPreview(_model.Draft);
    }

    private bool StepListHasFocus()
    {
        var focus = GetViewport()?.GuiGetFocusOwner();
        return focus is not null && GodotObject.IsInstanceValid(_stepsBody) && _stepsBody.IsAncestorOf(focus);
    }

    private void UndoEdit()
    {
        if (_model.Undo())
        {
            RenderPreview(_model.Draft);
        }
        else
        {
            // A refused press may still have committed a typed quantity, which moves both buttons.
            UpdateEditorButtons();
        }
    }

    private void RedoEdit()
    {
        if (_model.Redo())
        {
            RenderPreview(_model.Draft);
        }
        else
        {
            UpdateEditorButtons();
        }
    }

    private void ExtendReplannedFlash(PlanDraft current)
    {
        var deadline = (ulong)Engine.GetProcessFrames() + ReplannedFlashFrames;
        foreach (var step in current.Steps)
        {
            if (step.Replanned)
            {
                _replannedFlashUntil[step.Id.Value] = deadline;
            }
        }
    }

    private bool ShowReplanned(DraftStep step)
    {
        if (step.Replanned)
        {
            return true;
        }

        return _replannedFlashUntil.TryGetValue(step.Id.Value, out var until)
               && (ulong)Engine.GetProcessFrames() < until;
    }

    private void RenderPlanSteps(PlanDraft draft)
    {
        Clear(_stepsBody);

        if (draft.Steps.Count == 0)
        {
            var empty = ShellTheme.Text("NO STEPS", ShellTheme.Note);
            _stepsBody.AddChild(empty);
            return;
        }

        var issuesByStep = draft.Issues
            .Where(i => i.Step is not null)
            .GroupBy(i => i.Step!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var step in draft.Steps)
        {
            issuesByStep.TryGetValue(step.Id, out var stepIssues);
            _stepsBody.AddChild(PlanStepRow(step, stepIssues));
        }

        UpdateEditorButtons();
    }

    private void UpdateEditorButtons()
    {
        _undoButton.Disabled = !_model.CanUndo;
        _redoButton.Disabled = !_model.CanRedo;
        _readjustButton.Disabled = _model.Draft is null;
        _unlockAllButton.Disabled = _model.Draft is null;
        _addMoveButton.Disabled = _model.Draft is null;
    }

    private Control PlanStepRow(DraftStep step, List<DraftIssue>? stepIssues)
    {
        var selected = _model.SelectedStep == step.Id;
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride(
            "panel",
            ShellTheme.Block(ShellPalette.BgGlass, highlighted: selected));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        box.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(header);

        header.AddChild(ShellTheme.Text(StepActionLabel(step), ShellTheme.Tag));

        if (step.Origin == DraftOrigin.Manual)
        {
            header.AddChild(new IconSlot("control", "manual", IconSlot.RowSize, ShellPalette.Accent));
        }

        if (ShowReplanned(step))
        {
            header.AddChild(ReplannedChip());
        }

        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        if (step.Origin == DraftOrigin.Manual)
        {
            var remove = ToolbarButton("✕");
            var stepId = step.Id;
            remove.Pressed += () =>
            {
                _model.ApplyEdit(new RemoveStep(stepId));
                RenderPreview(_model.Draft);
            };
            header.AddChild(remove);
        }

        column.AddChild(StepDetailRow(step, stepIssues));

        return box;
    }

    private static Label ReplannedChip()
    {
        var label = new Label { Text = "REPLANNED" };
        label.AddThemeColorOverride("font_color", ShellPalette.Accent);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return label;
    }

    private Control StepDetailRow(DraftStep step, List<DraftIssue>? stepIssues)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);

        switch (step.Work)
        {
            case DraftProduce produce:
                row.AddChild(QuantityEditor(step, StepQuantityMilli(step, produce)));
                row.AddChild(new Label
                {
                    Text = produce.Schematic.Value.ToUpperInvariant(),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                });
                TintIfLocked(row.GetChild(row.GetChildCount() - 1) as Label, step.QuantityLocked);
                row.AddChild(ProduceExecutorEditor(step, produce));
                break;

            case DraftMove move:
                row.AddChild(QuantityEditor(step, move.Quantity));
                row.AddChild(new Label
                {
                    Text = Labels.Item(move.Item),
                    CustomMinimumSize = new Vector2(80, 0),
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                });
                TintIfLocked(row.GetChild(row.GetChildCount() - 1) as Label, step.QuantityLocked);
                row.AddChild(new Label
                {
                    Text = $"{StorageLabel(move.From)} → {StorageLabel(move.To)}",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                });
                row.AddChild(MoveExecutorEditor(step, move));
                break;

            case DraftAssemble assemble:
                row.AddChild(new Label
                {
                    Text = Labels.Item(assemble.Unit),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                });
                row.AddChild(new Label
                {
                    Text = FactoryLabel(assemble.Target),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                });
                break;
        }

        row.AddChild(MetaLabel($"AVAIL {Units.Format(step.AvailableAtSource)}"));
        row.AddChild(MetaLabel(step.Origin == DraftOrigin.Manual ? "MANUAL" : "AUTO"));

        if (stepIssues is { Count: > 0 })
        {
            row.AddChild(MetaLabel(Labels.Issue(stepIssues[0].Kind), ShellPalette.StateFault));
        }

        return row;
    }

    private static void TintIfLocked(Label? label, bool locked)
    {
        if (label is null)
        {
            return;
        }

        if (locked)
        {
            label.AddThemeColorOverride("font_color", ShellPalette.Accent);
        }
        else
        {
            label.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        }
    }

    private static Label MetaLabel(string text, Color? colour = null)
    {
        var label = new Label
        {
            Text = text,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        label.AddThemeColorOverride("font_color", colour ?? ShellPalette.TextDim);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return label;
    }

    private Control QuantityEditor(DraftStep step, long quantityMilli)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        var units = Math.Max(1, quantityMilli / Units.MilliPerUnit);
        var spin = new SpinBox
        {
            MinValue = 1,
            MaxValue = 1_000_000,
            Step = 1,
            Value = units,
            CustomMinimumSize = new Vector2(72, 0),
            UpdateOnTextChanged = true,
        };
        if (step.QuantityLocked)
        {
            spin.AddThemeColorOverride("font_color", ShellPalette.Accent);
        }

        var stepId = step.Id;
        spin.ValueChanged += value =>
        {
            if (_applyingDraft)
            {
                return;
            }

            _model.LiveQuantityEdit(stepId, (long)value * Units.MilliPerUnit);

            // A typed change is undoable and drops the redo before it is committed, so the buttons
            // follow each keystroke. Only the buttons: re-rendering the rows would take the focus
            // out of the spin box being typed into.
            UpdateEditorButtons();
        };
        spin.GetLineEdit().FocusExited += () =>
        {
            if (_applyingDraft)
            {
                return;
            }

            _model.CommitQuantityEdit();
            RenderPreview(_model.Draft);
        };
        row.AddChild(spin);

        row.AddChild(LockToggle(step, DraftField.Quantity, step.QuantityLocked));
        return row;
    }

    private Control ProduceExecutorEditor(DraftStep step, DraftProduce produce)
    {
        var catalog = ShellContent.Catalog;
        if (!catalog.Schematics.TryGet(produce.Schematic, out var schematic))
        {
            return MetaLabel("—");
        }

        var button = new OptionButton { CustomMinimumSize = new Vector2(120, 0) };
        if (step.ExecutorLocked)
        {
            button.AddThemeColorOverride("font_color", ShellPalette.Accent);
        }

        var facilities = EligibleFacilities(schematic.RequiredFacilityType).ToList();
        var selected = -1;
        for (var i = 0; i < facilities.Count; i++)
        {
            button.AddItem(facilities[i].Label.ToUpperInvariant());
            if (facilities[i].Id == step.Executor)
            {
                selected = i;
            }
        }

        if (step.Executor is { } current && selected < 0)
        {
            button.AddItem(FactoryLabel(current));
            selected = button.ItemCount - 1;
        }

        if (selected >= 0)
        {
            button.Selected = selected;
        }

        var stepId = step.Id;
        button.ItemSelected += index =>
        {
            if (_applyingDraft || index < 0 || index >= facilities.Count)
            {
                return;
            }

            _model.ApplyEdit(new SetExecutor(stepId, facilities[(int)index].Id));
            RenderPreview(_model.Draft);
        };

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        row.AddChild(button);
        row.AddChild(LockToggle(step, DraftField.Executor, step.ExecutorLocked));
        return row;
    }

    private Control MoveExecutorEditor(DraftStep step, DraftMove move)
    {
        var button = new OptionButton { CustomMinimumSize = new Vector2(120, 0) };
        if (step.ExecutorLocked)
        {
            button.AddThemeColorOverride("font_color", ShellPalette.Accent);
        }

        var lines = EligibleTransports(move.From, move.To).ToList();
        var selected = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            button.AddItem(lines[i].Label.ToUpperInvariant());
            if (lines[i].Id == step.Executor)
            {
                selected = i;
            }
        }

        if (step.Executor is { } current && selected < 0)
        {
            button.AddItem(TransportLabel(current));
            selected = button.ItemCount - 1;
        }

        if (selected >= 0)
        {
            button.Selected = selected;
        }

        var stepId = step.Id;
        button.ItemSelected += index =>
        {
            if (_applyingDraft || index < 0 || index >= lines.Count)
            {
                return;
            }

            _model.ApplyEdit(new SetExecutor(stepId, lines[(int)index].Id));
            RenderPreview(_model.Draft);
        };

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        row.AddChild(button);
        row.AddChild(LockToggle(step, DraftField.Executor, step.ExecutorLocked));
        return row;
    }

    private Button LockToggle(DraftStep step, DraftField field, bool locked)
    {
        var button = new Button
        {
            Icon = IconSlot.Load("control", locked ? "lock" : "unlock"),
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(IconSlot.RowSize + ShellPalette.SpaceSm, 0),
            TooltipText = field == DraftField.Quantity
                ? locked
                    ? "Quantity locked — automatic re-planning will not change this amount. Click to unlock."
                    : "Lock quantity — keep this amount when the plan re-adjusts."
                : locked
                    ? "Executor locked — automatic re-planning will not reassign this row. Click to unlock."
                    : "Lock executor — keep this facility or line when the plan re-adjusts.",
        };
        ShellTheme.ApplyGlass(button);
        if (locked)
        {
            button.Modulate = new Color(ShellPalette.Accent, 1f);
        }

        var stepId = step.Id;
        button.Pressed += () =>
        {
            _model.CommitQuantityEdit();
            _model.ApplyEdit(new SetLock(stepId, field, !locked));
            RenderPreview(_model.Draft);
        };
        return button;
    }

    private IEnumerable<ExecutorState> EligibleFacilities(FacilityType type)
    {
        var snapshot = _lastSnapshot;
        if (snapshot is null)
        {
            yield break;
        }

        foreach (var executor in snapshot.Executors)
        {
            if (!executor.Built || executor.Type != type || !IsCommandable(executor.Id))
            {
                continue;
            }

            yield return executor;
        }
    }

    private IEnumerable<TransportExecutorState> EligibleTransports(StorageId from, StorageId to)
    {
        var snapshot = _lastSnapshot;
        if (snapshot is null)
        {
            yield break;
        }

        foreach (var line in snapshot.Transports)
        {
            if (line.Built && line.From == from && line.To == to)
            {
                yield return line;
            }
        }
    }

    private static bool IsCommandable(ExecutorId id)
    {
        // Placement → archetype through every loaded scenario, not only the default vessel.
        foreach (var scenario in ShellContent.Scenarios)
        {
            var placement = scenario.Facilities.FirstOrDefault(f => f.Id == id);
            if (placement is null)
            {
                continue;
            }

            return ShellContent.Catalog.Facility(placement.Archetype)?.Commandable == true;
        }

        return false;
    }

    private static string StepActionLabel(DraftStep step) => step.Work switch
    {
        DraftAssemble => "ASSEMBLE",
        DraftProduce => "PRODUCE",
        DraftMove => "MOVE",
        _ => "?",
    };

    private long StepQuantityMilli(DraftStep step, DraftProduce produce)
    {
        var catalog = ShellContent.Catalog;
        if (!catalog.Schematics.TryGet(produce.Schematic, out var schematic))
        {
            return 0;
        }

        return produce.Runs * schematic.Output.Quantity;
    }

    private void RenderCoverage(PlanDraft draft)
    {
        _coverage.Text =
            $"{Units.Format(draft.Covered)} / {Units.Format(draft.Goal.Quantity)} " +
            $"{Labels.Item(draft.Goal.Item)}";
    }

    private void RenderIssueList(PlanDraft draft)
    {
        Clear(_issuesBody);

        var issues = draft.Issues.ToList();
        _issuesSection.Visible = issues.Count > 0;
        if (issues.Count == 0)
        {
            return;
        }

        foreach (var issue in issues)
        {
            // An issue that blocks approval is a fault; every other kind informs. The kernel's
            // classification, not a copy of it: the copy here once left WorkpieceNotAccepted out
            // and drew a draft approval refuses in the warning colour.
            var colour = issue.Kind.IsStructural() ? ShellPalette.StateFault : ShellPalette.StateWarn;

            var row = new Button
            {
                Text = $"{Labels.Item(issue.Item)} — {Labels.Issue(issue.Kind)}",
                FocusMode = FocusModeEnum.None,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                ClipText = true,
            };
            ShellTheme.ApplyGlass(row);
            row.AddThemeColorOverride("font_color", colour);

            if (issue.Step is { } stepId)
            {
                row.Pressed += () =>
                {
                    _model.SelectedStep = stepId;
                    if (_model.Draft is { } live)
                    {
                        RenderPlanSteps(live);
                    }
                };
            }

            _issuesBody.AddChild(row);
        }
    }

    private string ApproveButtonText(PlanDraft draft)
    {
        if (!draft.IsComplete && draft.IsCommittable)
        {
            var shortfall = draft.Issues.FirstOrDefault(i => i.Kind == DraftIssueKind.GoalShortfall);
            var item = shortfall?.Item ?? draft.Goal.Item;
            return $"APPROVE PARTIAL PLAN — {Labels.Item(item)} SHORT";
        }

        return "APPROVE";
    }
}
