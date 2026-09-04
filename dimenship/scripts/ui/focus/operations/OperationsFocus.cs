using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The Operations view: a plan list on the left, and on the right either a plan's detail or the
/// composer that builds a new one.
/// <para>
/// <b>This is not a concept mock.</b> Unlike <see cref="ProgramsFocus"/> and
/// <see cref="LoadoutsFocus"/> it calls into the running kernel: the composer's live preview comes
/// from <see cref="ShellContext.ComposePlan"/> (bound to <see cref="SimulationDriver.Plan"/>), and
/// APPROVE commits it through <see cref="ShellActions.PlanApproved"/> — the first player command
/// this shell has ever issued into <see cref="SimulationEngine.Enqueue"/>. The composed plan is
/// never held onto past that click: what happened lands on the next snapshot's <c>Plans</c> and
/// <c>Tasks</c> lists, the same way every other reading in the shell arrives.
/// </para>
/// <para>
/// The panel identifier stays <c>processes</c> although the view is titled Operations, for the same
/// reason the programming view kept <c>doctrine</c>: the identifier is written into saved layout
/// files, and renaming it would silently reset every player's layout to gain nothing.
/// </para>
/// <para>
/// Only Build Launch Pad 1 and a generic Produce are offered. A composer that could also target
/// Launch Pad 2 is deliberately not built: the second dock is authored unbuilt with nothing pointed
/// at it this step (<c>docs/superpowers/specs/2026-09-03-launch-pad-design.md</c>, "Not built"), and
/// a picker for a target that resolves to nothing would be a control with no correct answer.
/// </para>
/// </summary>
public sealed partial class OperationsFocus : PanelBase
{
    /// <summary>One whole construction unit, in the item's own milli-unit accounting.</summary>
    private const long UnitsToMilli = 1000;

    /// <summary>Commissioning consumes exactly one whole unit — never more, never a fraction.</summary>
    private const long LaunchPadUnits = 1;

    private ShellContext? _context;
    private WorldSnapshot? _lastSnapshot;

    private BuildTarget? _buildTarget;
    private List<ProduceTarget> _produceTargets = new();

    private bool _buildMode = true;
    private int _produceIndex;
    private long _quantityUnits = 1;

    private PlanId? _selectedPlan;
    private ProductionPlan? _currentPlan;

    private VBoxContainer _planListBody = null!;
    private Control _composerRoot = null!;
    private Control _detailRoot = null!;

    private Button _buildToggle = null!;
    private Button _produceToggle = null!;
    private Control _buildTargetRow = null!;
    private Label _buildTargetValue = null!;
    private Control _produceTargetRow = null!;
    private OptionButton _produceTarget = null!;
    private SpinBox _quantity = null!;
    private VBoxContainer _previewBody = null!;
    private Label _estimate = null!;
    private PanelContainer _unplannableSection = null!;
    private VBoxContainer _unplannableBody = null!;
    private Button _approve = null!;

    private Label _detailTitle = null!;
    private VBoxContainer _detailBody = null!;

    public override PanelId Id => ShellRoot.ProcessesId;

    public override string Title => "Operations";

    public override void OnMount(ShellContext context) => _context = context;

    public override void _Ready()
    {
        ResolveTargets();

        var outer = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(outer);

        outer.AddChild(BuildPlanList());

        var right = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        outer.AddChild(right);

        _composerRoot = BuildComposer();
        _composerRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        right.AddChild(_composerRoot);

        _detailRoot = BuildDetail();
        _detailRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        _detailRoot.Visible = false;
        right.AddChild(_detailRoot);

        ApplyModeChrome();
        UpdateTargetVisibility();
        RefreshTargetLabels();
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        RefreshPlanList(snapshot);

        if (_selectedPlan is { } id && snapshot.Plans.Any(p => p.Id == id))
        {
            RefreshDetail(snapshot);
            return;
        }

        if (_selectedPlan is not null)
        {
            // The selected plan is gone — plans are not pruned today, so this is defensive rather
            // than expected, but a detail view for a plan that no longer resolves to anything is
            // worse than falling back to the composer.
            _selectedPlan = null;
        }

        ShowComposerChrome();
        RefreshComposerPreview();
    }

    // ---- Plan list -----------------------------------------------------------------------

    private Control BuildPlanList()
    {
        var column = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(240, 0),
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(header);

        var title = new Label { Text = "PLANS", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        header.AddChild(title);

        var newPlan = new Button { Text = "+ NEW", FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(newPlan);
        newPlan.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        newPlan.Pressed += ShowComposer;
        header.AddChild(newPlan);

        column.AddChild(ShellTheme.Divider());

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);

        _planListBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _planListBody.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        scroll.AddChild(_planListBody);

        return column;
    }

    private void RefreshPlanList(WorldSnapshot snapshot)
    {
        Clear(_planListBody);

        if (snapshot.Plans.Count == 0)
        {
            var empty = new Label { Text = "NO PLANS YET" };
            empty.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            empty.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _planListBody.AddChild(empty);
            return;
        }

        // Newest first: the plan a player just approved is the one they want to watch.
        for (var i = snapshot.Plans.Count - 1; i >= 0; i--)
        {
            _planListBody.AddChild(PlanRow(snapshot.Plans[i]));
        }
    }

    private Control PlanRow(CommittedPlanState plan)
    {
        var (stateText, stateColor) = PlanStateReading(plan.State);
        var button = new Button
        {
            Text = $"#{plan.Id} {ItemLabel(plan.Goal.Item)} — {stateText} " +
                   $"({plan.CompletedTasks}/{plan.SpawnedTasks.Count})",
            FocusMode = FocusModeEnum.None,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        button.AddThemeColorOverride("font_color", stateColor);
        button.AddThemeStyleboxOverride(
            "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: plan.Id == _selectedPlan));

        var id = plan.Id;
        button.Pressed += () => ShowDetail(id);

        return button;
    }

    private static (string Text, Color Color) PlanStateReading(PlanState state) => state switch
    {
        PlanState.Active => ("ACTIVE", ShellPalette.StateOk),
        PlanState.Complete => ("DONE", ShellPalette.TextDim),
        PlanState.Abandoned => ("ABANDONED", ShellPalette.StateFault),
        _ => ("UNKNOWN", ShellPalette.TextFaint),
    };

    // ---- Composer --------------------------------------------------------------------------

    private Control BuildComposer()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        column.AddChild(ModeToggle());

        var target = new VBoxContainer();
        target.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        _buildTargetRow = BuildTargetRow();
        target.AddChild(_buildTargetRow);
        _produceTargetRow = ProduceTargetRow();
        target.AddChild(_produceTargetRow);
        column.AddChild(target);

        column.AddChild(ShellTheme.Divider());

        var previewBox = BoxSection.Create("PREVIEW — TASKS IN ORDER", out var previewBody);
        previewBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        _previewBody = previewBody;
        column.AddChild(previewBox);

        var (estimateRow, estimateValue) = LiveRow("ESTIMATE");
        _estimate = estimateValue;
        column.AddChild(estimateRow);

        _unplannableSection = BoxSection.Create("UNPLANNABLE", out var unplannableBody);
        _unplannableBody = unplannableBody;
        column.AddChild(_unplannableSection);

        column.AddChild(ApproveRow());

        return column;
    }

    private Control ModeToggle()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        var caption = new Label { Text = "MODE:" };
        caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(caption);

        _buildToggle = ModeButton("BUILD");
        _buildToggle.Pressed += () => SetMode(true);
        row.AddChild(_buildToggle);

        _produceToggle = ModeButton("PRODUCE");
        _produceToggle.Pressed += () => SetMode(false);
        row.AddChild(_produceToggle);

        return row;
    }

    private static Button ModeButton(string text)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        return button;
    }

    private void SetMode(bool build)
    {
        if (_buildMode == build)
        {
            return;
        }

        _buildMode = build;
        ApplyModeChrome();
        UpdateTargetVisibility();
        RefreshComposerPreview();
    }

    /// <summary>Selection here is a border colour change, the same rule every tab and frame button
    /// in the shell follows: growth would shift the row's neighbours.</summary>
    private void ApplyModeChrome()
    {
        _buildToggle.AddThemeStyleboxOverride(
            "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: _buildMode));
        _buildToggle.AddThemeColorOverride(
            "font_color", _buildMode ? ShellPalette.TextTitle : ShellPalette.TextDim);

        _produceToggle.AddThemeStyleboxOverride(
            "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: !_buildMode));
        _produceToggle.AddThemeColorOverride(
            "font_color", !_buildMode ? ShellPalette.TextTitle : ShellPalette.TextDim);
    }

    private void UpdateTargetVisibility()
    {
        _buildTargetRow.Visible = _buildMode;
        _produceTargetRow.Visible = !_buildMode;
    }

    private Control BuildTargetRow()
    {
        var (row, value) = LiveRow("TARGET");
        _buildTargetValue = value;
        return row;
    }

    private Control ProduceTargetRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var targetCaption = new Label { Text = "TARGET:" };
        targetCaption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        targetCaption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(targetCaption);

        _produceTarget = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _produceTarget.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        foreach (var target in _produceTargets)
        {
            _produceTarget.AddItem(target.Label.ToUpperInvariant());
        }

        if (_produceTargets.Count == 0)
        {
            _produceTarget.AddItem("NOTHING UNLOCKED");
            _produceTarget.Disabled = true;
        }

        _produceTarget.ItemSelected += index =>
        {
            _produceIndex = (int)index;
            RefreshComposerPreview();
        };
        row.AddChild(_produceTarget);

        var quantityCaption = new Label { Text = "QTY:" };
        quantityCaption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        quantityCaption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(quantityCaption);

        _quantity = new SpinBox
        {
            MinValue = 1,
            MaxValue = 1_000_000,
            Step = 1,
            Value = _quantityUnits,
            CustomMinimumSize = new Vector2(90, 0),

            // Committed on focus loss as well as on Enter, matching the programming view's numeric
            // slot: a quantity the player typed and clicked away from has been decided.
            UpdateOnTextChanged = false,
        };
        _quantity.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _quantity.GetLineEdit().AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _quantity.ValueChanged += amount =>
        {
            _quantityUnits = Math.Max(1, (long)amount);
            RefreshComposerPreview();
        };
        row.AddChild(_quantity);

        return row;
    }

    private Control ApproveRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var hint = new Label
        {
            Text = "APPROVING INJECTS THESE TASKS INTO THE VESSEL'S QUEUES",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        hint.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        hint.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(hint);

        _approve = new Button { Text = "APPROVE", Disabled = true };
        _approve.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _approve.Pressed += OnApprovePressed;
        row.AddChild(_approve);

        return row;
    }

    private void OnApprovePressed()
    {
        if (_currentPlan is { Tasks.Count: > 0 } plan)
        {
            _context?.Actions.PlanApproved?.Invoke(plan);
        }
    }

    private void RefreshTargetLabels()
    {
        _buildTargetValue.Text = _buildTarget is { } target
            ? $"{target.Label.ToUpperInvariant()} · {Units.Format(LaunchPadUnits * UnitsToMilli)} " +
              $"{ItemLabel(target.ConstructionUnit)} → {target.Destination.Value.ToUpperInvariant()}"
            : "NO BUILDABLE TARGET IN CONTENT";
        _buildTargetValue.AddThemeColorOverride(
            "font_color", _buildTarget is null ? ShellPalette.StateFault : ShellPalette.TextTitle);
    }

    private void RefreshComposerPreview()
    {
        var plan = _context?.ComposePlan is { } compose && ComposerGoal() is { } goal
            ? compose(goal, _buildMode ? _buildTarget?.Destination : null)
            : null;

        _currentPlan = plan;
        RenderPreview(plan);
    }

    private ItemAmount? ComposerGoal()
    {
        if (_buildMode)
        {
            return _buildTarget is { } target
                ? new ItemAmount(target.ConstructionUnit, LaunchPadUnits * UnitsToMilli)
                : null;
        }

        if (_produceTargets.Count == 0)
        {
            return null;
        }

        var index = Mathf.Clamp(_produceIndex, 0, _produceTargets.Count - 1);
        return new ItemAmount(_produceTargets[index].Item, _quantityUnits * UnitsToMilli);
    }

    private void RenderPreview(ProductionPlan? plan)
    {
        Clear(_previewBody);
        Clear(_unplannableBody);

        if (plan is null)
        {
            var reason = new Label { Text = "NO TARGET TO PLAN" };
            reason.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _previewBody.AddChild(reason);

            _estimate.Text = "—";
            _unplannableSection.Visible = false;
            _approve.Disabled = true;
            return;
        }

        if (plan.Tasks.Count == 0)
        {
            var nothing = new Label { Text = "NOTHING TO PLAN" };
            nothing.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            nothing.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _previewBody.AddChild(nothing);
        }

        foreach (var task in plan.Tasks)
        {
            var row = new Label
            {
                Text = Instruction(task.Script.Action),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            row.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            row.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _previewBody.AddChild(row);
        }

        // A documented lower bound, not a promise: ProductionPlan.EstimatedTicks ignores
        // switch-over, queueing behind existing work, and energy contention.
        _estimate.Text = $"{plan.EstimatedTicks} ticks (~{Units.FormatSimTime(plan.EstimatedTicks)})";

        _unplannableSection.Visible = plan.Unplannable.Count > 0;
        foreach (var entry in plan.Unplannable)
        {
            _unplannableBody.AddChild(BoxSection.Row(
                ItemLabel(entry.Item),
                $"{Units.Format(entry.Quantity)} — {Describe(entry.Reason)}",
                ShellPalette.StateWarn));
        }

        _approve.Disabled = plan.Tasks.Count == 0;
    }

    // ---- Detail ----------------------------------------------------------------------------

    private Control BuildDetail()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var back = new Button { Text = "← BACK TO COMPOSER", FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(back);
        back.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        back.Pressed += ShowComposer;
        column.AddChild(back);

        _detailTitle = new Label();
        _detailTitle.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        _detailTitle.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        column.AddChild(_detailTitle);

        _detailBody = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _detailBody.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(_detailBody);

        return column;
    }

    private void ShowComposer()
    {
        _selectedPlan = null;
        ShowComposerChrome();
        RefreshComposerPreview();

        // A row lower in the list needs to lose its highlight the moment the composer is chosen
        // without it — clicking "+ NEW" while a plan is selected, say.
        if (_lastSnapshot is { } snapshot)
        {
            RefreshPlanList(snapshot);
        }
    }

    private void ShowComposerChrome()
    {
        _composerRoot.Visible = true;
        _detailRoot.Visible = false;
    }

    private void ShowDetail(PlanId id)
    {
        _selectedPlan = id;
        _composerRoot.Visible = false;
        _detailRoot.Visible = true;

        if (_lastSnapshot is { } snapshot)
        {
            RefreshPlanList(snapshot);
            RefreshDetail(snapshot);
        }
    }

    private void RefreshDetail(WorldSnapshot snapshot)
    {
        Clear(_detailBody);

        var plan = snapshot.Plans.FirstOrDefault(p => p.Id == _selectedPlan);
        if (plan is null)
        {
            _detailTitle.Text = "PLAN";
            _detailBody.AddChild(BoxSection.Row("STATUS", "NO LONGER PRESENT", ShellPalette.StateFault));
            return;
        }

        _detailTitle.Text = $"PLAN #{plan.Id} · {ItemLabel(plan.Goal.Item)}".ToUpperInvariant();

        _detailBody.AddChild(BoxSection.Row(
            "GOAL", $"{Units.Format(plan.Goal.Quantity)} {ItemLabel(plan.Goal.Item)}"));

        if (plan.Destination is { } destination)
        {
            var label = snapshot.Storages.FirstOrDefault(s => s.Id == destination)?.Label
                        ?? destination.Value;
            _detailBody.AddChild(BoxSection.Row("DESTINATION", label.ToUpperInvariant()));
        }

        var (stateText, stateColor) = PlanStateReading(plan.State);
        _detailBody.AddChild(BoxSection.Row("STATE", stateText, stateColor));
        _detailBody.AddChild(BoxSection.Row("COMMITTED", Units.FormatSimTime(plan.CommittedAtTick)));
        _detailBody.AddChild(BoxSection.Row(
            "TASKS", $"{plan.CompletedTasks} / {plan.SpawnedTasks.Count} COMPLETE"));

        _detailBody.AddChild(ShellTheme.Divider());

        // Resolved against the live task list rather than carried on the plan itself, which owns
        // only the ids: a task's instruction and progress belong to the executor running it, and
        // the plan's job is only to say which ids are its own. A task retired out of the bounded
        // window is skipped — the TASKS row above already accounts for it.
        foreach (var taskId in plan.SpawnedTasks)
        {
            var task = snapshot.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is null)
            {
                continue;
            }

            var (taskText, taskColor) = TaskStateReading(task.State);
            _detailBody.AddChild(BoxSection.Row(Instruction(task.Action), taskText, taskColor));
        }
    }

    private static (string Text, Color Color) TaskStateReading(TaskState state) => state switch
    {
        TaskState.Running => ("RUNNING", ShellPalette.StateOk),
        TaskState.Complete => ("DONE", ShellPalette.TextDim),
        TaskState.Postponed => ("POSTPONED", ShellPalette.StateWarn),
        _ => ("QUEUED", ShellPalette.TextFaint),
    };

    // ---- Targets, resolved once from content ----------------------------------------------

    /// <summary>
    /// The one facility this composer can build: whatever the scenario names "dock_a", read
    /// generically off its archetype rather than hard-coding the item and storage it names, so a
    /// content edit to the construction unit or the dock's hold reaches this composer for free.
    /// The facility id itself is the one hard-coded thing — deliberately: a picker able to target
    /// Launch Pad 2 as well is not wanted this step (see the type's own doc comment).
    /// </summary>
    private static BuildTarget? ResolveBuildTarget()
    {
        var scenario = ShellContent.DefaultVessel;
        var facility = scenario.Facilities.FirstOrDefault(f => f.Id.Value == "dock_a");
        if (facility is null)
        {
            return null;
        }

        var archetype = ShellContent.Catalog.Facility(facility.Archetype);
        if (archetype?.ConstructionUnit is not { } unit)
        {
            return null;
        }

        return new BuildTarget(
            facility.Id, facility.NameOverride ?? archetype.Label, unit, facility.LocalStorage);
    }

    /// <summary>
    /// Every item an unlocked schematic produces, in unlock declaration order and de-duplicated by
    /// output — the same order the planner itself prefers a producer in. Reading the scenario's
    /// unlocked set rather than enumerating every schematic in the catalog is what keeps a Produce
    /// target list matching what the player can actually plan today.
    /// </summary>
    private static List<ProduceTarget> ResolveProduceTargets()
    {
        var scenario = ShellContent.DefaultVessel;
        var catalog = ShellContent.Catalog;
        var seen = new HashSet<ItemId>();
        var targets = new List<ProduceTarget>();

        foreach (var schematicId in scenario.UnlockedSchematics)
        {
            if (!catalog.Schematics.TryGet(schematicId, out var schematic) ||
                !seen.Add(schematic.Output.Item))
            {
                continue;
            }

            targets.Add(new ProduceTarget(schematic.Output.Item, ItemLabel(schematic.Output.Item)));
        }

        return targets;
    }

    private void ResolveTargets()
    {
        _buildTarget = ResolveBuildTarget();
        _produceTargets = ResolveProduceTargets();
    }

    // ---- Shared rendering helpers -----------------------------------------------------------

    private static string Instruction(TaskAction action) => action switch
    {
        Produce p => p.Runs is { } runs
            ? $"RUN {p.Schematic} ×{runs}"
            : $"RUN {p.Schematic} (STANDING)",
        Transfer t => t.Quantity is { } quantity
            ? $"HAUL {t.Item} {t.From} → {t.To} ({Units.Format(quantity)})"
            : $"HAUL {t.Item} {t.From} → {t.To} (STANDING)",
        _ => "?",
    };

    private static string Describe(UnplannableReason reason) => reason switch
    {
        UnplannableReason.LockedSchematic => "LOCKED SCHEMATIC",
        UnplannableReason.NoExecutorOrLine => "NO FACILITY OR LINE",
        UnplannableReason.CyclicSchematic => "CYCLIC SCHEMATIC",
        _ => "UNKNOWN",
    };

    private static string ItemLabel(ItemId id) =>
        (ShellContent.Catalog.Item(id)?.Label ?? id.Value).ToUpperInvariant();

    /// <summary>A dim uppercase label and a bright right-aligned value, kept live rather than
    /// rebuilt: <see cref="BoxSection.Row"/> makes the same shape but hands back a
    /// <see cref="Control"/> with no way to update the value later, which is exactly what an
    /// estimate or a target reading that changes every tick needs.</summary>
    private static (Control Row, Label Value) LiveRow(string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var name = new Label { Text = label.ToUpperInvariant() };
        name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(name);

        var value = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        value.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        value.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(value);

        return (row, value);
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private readonly record struct BuildTarget(
        ExecutorId Facility, string Label, ItemId ConstructionUnit, StorageId Destination);

    private readonly record struct ProduceTarget(ItemId Item, string Label);
}
