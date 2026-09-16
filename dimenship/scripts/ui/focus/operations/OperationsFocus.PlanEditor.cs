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
    private const int UndoLimit = 64;

    /// <summary>One whole unit in the item's milli-unit accounting.</summary>
    private const long MilliPerUnit = 1000;

    /// <summary>How many process frames a REPLANNED flash stays visible after the flag clears.</summary>
    private const ulong ReplannedFlashFrames = 120;

    private readonly List<PlanDraft> _undo = new();
    private readonly List<PlanDraft> _redo = new();
    private readonly Dictionary<long, ulong> _replannedFlashUntil = new();

    private ItemAmount? _trackedGoal;
    private StorageId? _trackedDestination;
    private ExecutorId? _trackedAssemblyTarget;

    private DraftStepId? _selectedStepId;
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
            ApplyPlayerEdit(new ReAdjust());
            RenderPreview(_currentDraft);
        };
        row.AddChild(_readjustButton);

        _unlockAllButton = ToolbarButton("UNLOCK ALL");
        _unlockAllButton.Pressed += () =>
        {
            ApplyPlayerEdit(new UnlockAll());
            RenderPreview(_currentDraft);
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

        var caption = new Label { Text = "NEW MOVE" };
        caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
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
        _addMoveQuantity.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
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
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(WrapAddMoveField(label, button));
        return button;
    }

    private static Control WrapAddMoveField(string label, Control field)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        var caption = new Label { Text = label };
        caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
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
        var quantity = (long)_addMoveQuantity.Value * MilliPerUnit;

        ApplyPlayerEdit(new AddMove(item, from, to, quantity, line));
        HideAddMoveRow();
        RenderPreview(_currentDraft);
    }

    private void ClearRevisionStack()
    {
        _undo.Clear();
        _redo.Clear();
        _replannedFlashUntil.Clear();
        _selectedStepId = null;
    }

    private bool GoalContextChanged()
    {
        var goal = ComposerGoal();
        var destination = _buildMode ? SelectedBuildTarget()?.Destination : null;
        var assembly = _buildMode ? SelectedBuildTarget()?.Facility : null;

        return goal != _trackedGoal
               || destination != _trackedDestination
               || assembly != _trackedAssemblyTarget;
    }

    private void TrackGoalContext()
    {
        _trackedGoal = ComposerGoal();
        _trackedDestination = _buildMode ? SelectedBuildTarget()?.Destination : null;
        _trackedAssemblyTarget = _buildMode ? SelectedBuildTarget()?.Facility : null;
    }

    private void ApplyPlayerEdit(DraftEdit edit)
    {
        if (_currentDraft is null || _context?.AdjustDraft is not { } adjust)
        {
            return;
        }

        var previous = _currentDraft;
        _undo.Add(previous);
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _currentDraft = adjust(previous, edit);
        _approveLocked = false;
        ExtendReplannedFlash(previous, _currentDraft);
    }

    private void RefreshDraftFromWorld()
    {
        if (_currentDraft is null || _context?.AdjustDraft is not { } adjust)
        {
            return;
        }

        var previous = _currentDraft;
        _currentDraft = adjust(previous, new WorldRefresh());
        ExtendReplannedFlash(previous, _currentDraft);
    }

    private void UndoEdit()
    {
        if (_undo.Count == 0 || _currentDraft is null)
        {
            return;
        }

        _redo.Add(_currentDraft);
        _currentDraft = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _approveLocked = false;
        RenderPreview(_currentDraft);
    }

    private void RedoEdit()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        if (_currentDraft is not null)
        {
            _undo.Add(_currentDraft);
        }

        _currentDraft = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _approveLocked = false;
        RenderPreview(_currentDraft);
    }

    private void ExtendReplannedFlash(PlanDraft previous, PlanDraft current)
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
            var empty = new Label { Text = "NO STEPS" };
            empty.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            empty.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
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
        _undoButton.Disabled = _undo.Count == 0;
        _redoButton.Disabled = _redo.Count == 0;
        _readjustButton.Disabled = _currentDraft is null;
        _unlockAllButton.Disabled = _currentDraft is null;
        _addMoveButton.Disabled = _currentDraft is null;
    }

    private Control PlanStepRow(DraftStep step, List<DraftIssue>? stepIssues)
    {
        var selected = _selectedStepId == step.Id;
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

        header.AddChild(ActionChip(StepActionLabel(step)));

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
                ApplyPlayerEdit(new RemoveStep(stepId));
                RenderPreview(_currentDraft);
            };
            header.AddChild(remove);
        }

        column.AddChild(StepDetailRow(step, stepIssues));

        return box;
    }

    private static Label ActionChip(string action)
    {
        var label = new Label { Text = action };
        label.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return label;
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
            row.AddChild(MetaLabel(DescribeIssue(stepIssues[0].Kind), ShellPalette.StateFault));
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

        var units = Math.Max(1, quantityMilli / MilliPerUnit);
        var spin = new SpinBox
        {
            MinValue = 1,
            MaxValue = 1_000_000,
            Step = 1,
            Value = units,
            CustomMinimumSize = new Vector2(72, 0),
            UpdateOnTextChanged = false,
        };
        spin.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        if (step.QuantityLocked)
        {
            spin.AddThemeColorOverride("font_color", ShellPalette.Accent);
        }

        var stepId = step.Id;
        spin.GetLineEdit().FocusExited += () =>
        {
            if (_applyingDraft)
            {
                return;
            }

            ApplyPlayerEdit(new SetQuantity(stepId, (long)spin.Value * MilliPerUnit));
            RenderPreview(_currentDraft);
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
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
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

            ApplyPlayerEdit(new SetExecutor(stepId, facilities[(int)index].Id));
            RenderPreview(_currentDraft);
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
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
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

            ApplyPlayerEdit(new SetExecutor(stepId, lines[(int)index].Id));
            RenderPreview(_currentDraft);
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
        };
        ShellTheme.ApplyGlass(button);
        if (locked)
        {
            button.Modulate = new Color(ShellPalette.Accent, 1f);
        }

        var stepId = step.Id;
        button.Pressed += () =>
        {
            ApplyPlayerEdit(new SetLock(stepId, field, !locked));
            RenderPreview(_currentDraft);
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
        var scenario = ShellContent.DefaultVessel;
        var placement = scenario.Facilities.FirstOrDefault(f => f.Id == id);
        if (placement is null)
        {
            return false;
        }

        return ShellContent.Catalog.Facility(placement.Archetype)?.Commandable == true;
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
            var colour = draft.IsCommittable && IsSupplyIssue(issue.Kind)
                ? ShellPalette.StateWarn
                : IsStructuralIssue(issue.Kind)
                    ? ShellPalette.StateFault
                    : ShellPalette.StateWarn;

            var row = new Button
            {
                Text = $"{Labels.Item(issue.Item)} — {DescribeIssue(issue.Kind)}",
                FocusMode = FocusModeEnum.None,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                ClipText = true,
            };
            ShellTheme.ApplyGlass(row);
            row.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            row.AddThemeColorOverride("font_color", colour);

            if (issue.Step is { } stepId)
            {
                row.Pressed += () =>
                {
                    _selectedStepId = stepId;
                    RenderPlanSteps(draft);
                };
            }

            _issuesBody.AddChild(row);
        }
    }

    private static bool IsStructuralIssue(DraftIssueKind kind) => kind is
        DraftIssueKind.IncompatibleExecutor
        or DraftIssueKind.NoSuchRoute
        or DraftIssueKind.UnknownEndpoint
        or DraftIssueKind.NonPositiveQuantity
        or DraftIssueKind.UnbuiltExecutor
        or DraftIssueKind.NotCommandable;

    private static bool IsSupplyIssue(DraftIssueKind kind) => kind is
        DraftIssueKind.MaterialShortage
        or DraftIssueKind.GoalShortfall;

    private string ApproveButtonText(PlanDraft draft)
    {
        if (!draft.IsComplete && draft.IsCommittable)
        {
            var shortfall = draft.Issues.FirstOrDefault(i => i.Kind == DraftIssueKind.GoalShortfall)
                            ?? draft.Issues.FirstOrDefault(i => i.Kind == DraftIssueKind.MaterialShortage);
            var item = shortfall?.Item ?? draft.Goal.Item;
            return $"APPROVE PARTIAL PLAN — {Labels.Item(item)} SHORT";
        }

        return "APPROVE";
    }
}
