using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Shell;
using Dimenship.ShellModel;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The Operations view: a plan list on the left, and on the right either a plan's detail or the
/// composer that builds a new one.
/// <para>
/// <b>This is not a concept mock.</b> Unlike <see cref="ProgramsFocus"/> and
/// <see cref="LoadoutsFocus"/> it calls into the running kernel: the composer's live preview comes
/// from <see cref="ShellContext.ComposeDraft"/> (bound to <see cref="SimulationDriver.Draft"/>), and
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
/// Build offers every scenario facility whose archetype names a construction unit, filtered per
/// snapshot to whichever are still unbuilt — Reactor Beta, Factory Beta, Factory Gamma and both
/// Launch Pads on a new campaign. Reactor Alpha and Factory Alpha name a unit too, because the
/// unit belongs to the archetype and not the slot, and they never appear because they open built.
/// It sits behind an <see cref="OptionButton"/> populated the same way Produce's already is.
/// Built-ness is state, so the list shrinks as slots commission rather than being resolved once
/// from content; see
/// <c>docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md</c>, Decision 6.
/// </para>
/// </summary>
public sealed partial class OperationsFocus : PanelBase
{
    private ShellContext? _context;
    private WorldSnapshot? _lastSnapshot;

    /// <summary>
    /// The composer's state and rules (R5). Everything else on this view is a widget or a cache of
    /// what the model and the snapshot already say.
    /// </summary>
    private ComposerModel _model = null!;

    private VBoxContainer _planListBody = null!;
    private Control _composerRoot = null!;
    private Control _detailRoot = null!;

    private Button _buildToggle = null!;
    private Button _produceToggle = null!;
    private Control _buildTargetRow = null!;
    private OptionButton _buildTarget = null!;
    private Control _produceTargetRow = null!;
    private OptionButton _produceTarget = null!;
    private SpinBox _quantity = null!;
    private Label _previewTarget = null!;
    private Control _capabilityRow = null!;
    private Label _capability = null!;
    private VBoxContainer _materialsBody = null!;
    private VBoxContainer _factoriesBody = null!;
    private Label _energyDemand = null!;
    private Control _standingRow = null!;
    private Label _standingDraw = null!;
    private VBoxContainer _deliveriesBody = null!;
    private Label _estimate = null!;
    private VBoxContainer _competitionBody = null!;
    private PanelContainer _unplannableSection = null!;
    private VBoxContainer _unplannableBody = null!;
    private Button _approve = null!;
    private Button _discard = null!;

    private Label _detailTitle = null!;
    private VBoxContainer _detailBody = null!;

    public override PanelId Id => ShellRoot.ProcessesId;

    public override string Title => "Operations";

    /// <summary>
    /// Consumes and clears <see cref="ShellContext.PendingOperationsTarget"/> — the inspector's
    /// construction button parks it there and this is its one reader, so a later return to this
    /// view opens on the composer as before rather than replaying a stale request.
    /// <para>
    /// The direct <see cref="OptionButton.Select"/> call below (rather than only moving the model's
    /// build index and trusting the next refresh) matters because <see cref="Zone"/> calls
    /// <c>OnMount</c> before its own immediate re-delivery of the last snapshot, so
    /// <see cref="_lastSnapshot"/> here may still be null or stale and the model's refresh may not
    /// run before this method returns — without it the visible <see cref="OptionButton"/> would keep
    /// showing whatever it last showed while the model silently disagreed. <see cref="OnDiscardPressed"/> already calls
    /// <c>.Select()</c> directly for the same reason, and it does not raise <c>ItemSelected</c> in
    /// this Godot version, so this does not double-fire <see cref="RefreshComposerPreview"/>.
    /// </para>
    /// </summary>
    public override void OnMount(ShellContext context)
    {
        _context = context;
        _model.Compose = context.ComposeDraft;
        _model.Adjust = context.AdjustDraft;
        _model.Approver = context.Actions.PlanApproved;

        if (context.OperationsComposer is { } kept)
        {
            Restore(kept);
        }

        if (context.PendingOperationsTarget is not { } pending)
        {
            return;
        }

        context.PendingOperationsTarget = null;

        if (pending.Plan is { } planId)
        {
            ShowDetail(planId);
            return;
        }

        if (pending.BuildTarget is { } facilityId)
        {
            var found = _model.OpenBuild(facilityId);
            ApplyModeChrome();
            UpdateTargetVisibility();
            if (found)
            {
                _buildTarget.Select(_model.BuildIndex);
            }

            ShowComposer();
        }
    }

    /// <summary>
    /// Parks the composer on <see cref="ShellContext.OperationsComposer"/> for the next mount
    /// (UX-01). An open quantity edit is committed first, as leaving the step list commits it, so
    /// the number the player typed is the number that comes back.
    /// </summary>
    public override void OnUnmount()
    {
        if (_context is null)
        {
            return;
        }

        _context.OperationsComposer = _model.ToState();
    }

    /// <summary>
    /// Puts back what <see cref="OnUnmount"/> kept. Runs from <see cref="OnMount"/>, after
    /// <see cref="_Ready"/> has built the controls and before the zone re-delivers its snapshot.
    /// The model is restored first, then every control is set without its change signal
    /// (<see cref="OptionButton.Select"/> raises none, and the spin box goes through
    /// <c>SetValueNoSignal</c>), because each of those handlers recomposes from scratch and would
    /// throw away the very draft being restored.
    /// </summary>
    private void Restore(OperationsComposerState kept)
    {
        _model.FromState(kept);
        ApplyModeChrome();
        UpdateTargetVisibility();

        if (_model.VisibleBuildTargets.Count > 0)
        {
            _buildTarget.Select(_model.BuildIndex);
        }

        if (_model.ProduceTargets.Count > 0)
        {
            _produceTarget.Select(_model.ProduceIndex);
        }

        _quantity.SetValueNoSignal(_model.QuantityUnits);

        if (_model.SelectedPlan is { } plan)
        {
            ShowDetail(plan);
        }
    }

    public override void _Ready()
    {
        _model = new ComposerModel(
            ComposerTargets.Build(ShellContent.Catalog, ShellContent.DefaultVessel),
            ComposerTargets.Produce(ShellContent.Catalog, ShellContent.DefaultVessel, Labels.Item));
        _model.Adjusted += ExtendReplannedFlash;
        _model.RevisionsCleared += () =>
        {
            _replannedFlashUntil.Clear();
            HideAddMoveRow();
        };

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
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        RefreshPlanList(snapshot);

        if (_model.SelectedPlan is { } id && snapshot.Plans.Any(p => p.Id == id))
        {
            RefreshDetail(snapshot);
            return;
        }

        if (_model.SelectedPlan is not null)
        {
            // The selected plan is gone — plans are not pruned today, so this is defensive rather
            // than expected, but a detail view for a plan that no longer resolves to anything is
            // worse than falling back to the composer.
            _model.SelectedPlan = null;
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

        // Priority and the held flag only while they still act: a finished plan's priority is
        // history, and Normal is the default nobody chose.
        if (plan.State == PlanState.Active)
        {
            if (plan.Priority != Priority.Normal)
            {
                stateText += $" · {plan.Priority.ToString().ToUpperInvariant()}";
            }

            if (plan.Held)
            {
                stateText += " · HELD";
                stateColor = ShellPalette.StateWarn;
            }
        }

        var button = new Button
        {
            Text = $"#{plan.Id} {Labels.Item(plan.Goal.Item)} — {stateText} " +
                   $"({plan.CompletedTasks}/{plan.SpawnedTasks.Count})",
            FocusMode = FocusModeEnum.None,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        button.AddThemeColorOverride("font_color", stateColor);
        button.AddThemeStyleboxOverride(
            "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: plan.Id == _model.SelectedPlan));

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

        // Target and capability read every tick, same as ESTIMATE below — this is the composer's
        // own selection restated as the first line of its own preview, not a second control.
        var (targetRow, targetValue) = LiveRow("TARGET");
        _previewTarget = targetValue;
        column.AddChild(targetRow);

        // A Purpose sentence runs long (see FacilityArchetype.Purpose) — a wrapping value, not a
        // LiveRow's single-line ellipsis, is what keeps it readable rather than clipped.
        var (capabilityRow, capabilityValue) = WrappedRow("CAPABILITY GAINED");
        _capabilityRow = capabilityRow;
        _capability = capabilityValue;
        column.AddChild(capabilityRow);

        column.AddChild(ShellTheme.Divider());

        var stepsBox = BoxSection.Create("PLAN STEPS", out var stepsSectionBody);
        stepsSectionBody.AddChild(BuildPlanEditorChrome());
        column.AddChild(stepsBox);

        // Summary readings sit below the editable row list — scrolled so a long materials list
        // never pushes MODE/TARGET or APPROVE/DISCARD off the panel.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);

        var sections = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sections.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        scroll.AddChild(sections);

        var (coverageRow, coverageValue) = LiveRow("GOAL COVERAGE");
        _coverage = coverageValue;
        sections.AddChild(coverageRow);

        _issuesSection = BoxSection.Create("ISSUES", out _issuesBody);
        sections.AddChild(_issuesSection);

        var materialsBox = BoxSection.Create("MATERIALS", out var materialsBody);
        _materialsBody = materialsBody;
        sections.AddChild(materialsBox);

        var factoriesBox = BoxSection.Create("FACTORIES", out var factoriesBody);
        _factoriesBody = factoriesBody;
        sections.AddChild(factoriesBox);

        var (energyRow, energyValue) = LiveRow("ENERGY DEMAND");
        _energyDemand = energyValue;
        sections.AddChild(energyRow);

        var (standingRow, standingValue) = LiveRow("STANDING DRAW AFTER BUILT");
        _standingRow = standingRow;
        _standingDraw = standingValue;
        sections.AddChild(standingRow);

        var deliveriesBox = BoxSection.Create("DELIVERIES", out var deliveriesBody);
        _deliveriesBody = deliveriesBody;
        sections.AddChild(deliveriesBox);

        var (estimateRow, estimateValue) = LiveRow("ESTIMATE (MINIMUM)");
        _estimate = estimateValue;
        sections.AddChild(estimateRow);

        var competitionBox = BoxSection.Create("COMPETITION", out var competitionBody);
        _competitionBody = competitionBody;
        sections.AddChild(competitionBox);

        _unplannableSection = BoxSection.Create("UNPLANNABLE", out var unplannableBody);
        _unplannableBody = unplannableBody;
        sections.AddChild(_unplannableSection);

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
        if (!_model.SetMode(build))
        {
            return;
        }

        ApplyModeChrome();
        UpdateTargetVisibility();
        RefreshComposerPreview(freshCompose: true);
    }

    /// <summary>Selection here is a border colour change, the same rule every tab and frame button
    /// in the shell follows: growth would shift the row's neighbours.</summary>
    private void ApplyModeChrome()
    {
        _buildToggle.AddThemeStyleboxOverride(
            "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: _model.BuildMode));
        _buildToggle.AddThemeColorOverride(
            "font_color", _model.BuildMode ? ShellPalette.TextTitle : ShellPalette.TextDim);

        _produceToggle.AddThemeStyleboxOverride(
            "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: !_model.BuildMode));
        _produceToggle.AddThemeColorOverride(
            "font_color", !_model.BuildMode ? ShellPalette.TextTitle : ShellPalette.TextDim);
    }

    private void UpdateTargetVisibility()
    {
        _buildTargetRow.Visible = _model.BuildMode;
        _produceTargetRow.Visible = !_model.BuildMode;

        // Capability gained and the standing power cost only mean anything for a facility being
        // built — Produce mode has no target archetype to read either from.
        _capabilityRow.Visible = _model.BuildMode;
        _standingRow.Visible = _model.BuildMode;
    }

    private Control BuildTargetRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var caption = new Label { Text = "TARGET:" };
        caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(caption);

        _buildTarget = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _buildTarget.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        PopulateBuildOptions();

        _buildTarget.ItemSelected += index =>
        {
            _model.SelectBuildTarget((int)index);
            RefreshComposerPreview(freshCompose: true);
        };
        row.AddChild(_buildTarget);

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
        foreach (var target in _model.ProduceTargets)
        {
            _produceTarget.AddItem(target.Label.ToUpperInvariant());
        }

        if (_model.ProduceTargets.Count == 0)
        {
            _produceTarget.AddItem("NOTHING UNLOCKED");
            _produceTarget.Disabled = true;
        }

        _produceTarget.ItemSelected += index =>
        {
            _model.SelectProduceTarget((int)index);
            RefreshComposerPreview(freshCompose: true);
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
            Value = _model.QuantityUnits,
            CustomMinimumSize = new Vector2(90, 0),

            // Committed on focus loss as well as on Enter, matching the programming view's numeric
            // slot: a quantity the player typed and clicked away from has been decided.
            UpdateOnTextChanged = false,
        };
        _quantity.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _quantity.GetLineEdit().AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _quantity.ValueChanged += amount =>
        {
            _model.SetQuantity((long)amount);
            RefreshComposerPreview(freshCompose: true);
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
            Text = "APPROVING STARTS THIS ORDER — THE VESSEL QUEUES THE WORK AND DOES IT OVER TIME",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        hint.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        hint.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(hint);

        _discard = new Button { Text = "DISCARD", FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_discard);
        _discard.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _discard.Pressed += OnDiscardPressed;
        row.AddChild(_discard);

        _approve = new Button { Text = "APPROVE", Disabled = true };
        _approve.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _approve.Pressed += OnApprovePressed;
        row.AddChild(_approve);

        return row;
    }

    /// <summary>
    /// Commits the previewed plan exactly once, through <see cref="ComposerModel.Approve"/>, which
    /// refuses while the approve lock is set. That lock is defense in depth beside
    /// <see cref="RenderPreview"/> disabling the button: a disabled Godot button does not deliver a
    /// real click, but this handler no longer trusts that alone, because the snapshot-driven
    /// refresh recomposes a fresh candidate on every tick after a commit.
    /// </summary>
    private void OnApprovePressed()
    {
        var result = _model.Approve();
        if (result is PlanApprovalCommitted)
        {
            _approve.Disabled = true;
        }
        else if (result is PlanApprovalRefused)
        {
            RenderPreview(_model.Draft);
        }
    }

    /// <summary>Resets the composer (<see cref="ComposerModel.Discard"/>), puts the pickers back
    /// to their first entries, and recomposes. Setting the quantity's value fires its change
    /// handler, as it always has; the composition that follows is the same either way.</summary>
    private void OnDiscardPressed()
    {
        _model.Discard();

        if (_model.VisibleBuildTargets.Count > 0)
        {
            _buildTarget.Select(0);
        }

        if (_model.ProduceTargets.Count > 0)
        {
            _produceTarget.Select(0);
        }

        _quantity.Value = _model.QuantityUnits;

        RefreshComposerPreview();
    }

    /// <summary>
    /// Brings the model up to date with the last snapshot and renders it. Every path that needs a
    /// fresh preview (a snapshot, a mode, a target, a discard) comes through here, so every one of
    /// them gets a fresh build list too; the picker is rebuilt only when that list changed.
    /// </summary>
    private void RefreshComposerPreview(bool freshCompose = false)
    {
        if (_model.Refresh(_lastSnapshot, freshCompose))
        {
            PopulateBuildOptions();
        }

        RenderPreview(_model.Draft);
    }

    /// <summary>
    /// Renders the nine named readings Decision 1 of the vessel construction interface spec maps
    /// out: target, capability gained, materials, factories, energy, deliveries, the estimate,
    /// competition and unplannable. Every number here is read once off <paramref name="plan"/> or
    /// the last snapshot and shown — nothing is re-derived, and in particular
    /// <see cref="ProductionPlan.EstimatedTicks"/> is never re-summed.
    /// </summary>
    private void RenderPreview(PlanDraft? draft)
    {
        Clear(_materialsBody);
        Clear(_factoriesBody);
        Clear(_deliveriesBody);
        Clear(_competitionBody);
        Clear(_unplannableBody);

        if (draft is null)
        {
            _model.CommitQuantityEdit();
            _previewTarget.Text = "—";
            _capability.Text = "—";
            _coverage.Text = "—";
            _energyDemand.Text = "—";
            _standingDraw.Text = "—";
            _estimate.Text = "—";

            Clear(_stepsBody);
            Clear(_issuesBody);
            _issuesSection.Visible = false;

            var reason = new Label { Text = "NO TARGET TO PLAN" };
            reason.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _materialsBody.AddChild(reason);

            _unplannableSection.Visible = false;
            _approve.Text = "APPROVE";
            _approve.Disabled = true;
            UpdateEditorButtons();
            return;
        }

        // While the player is mid-edit in a quantity spin, keep the step rows — clearing them
        // would fire FocusExited under _applyingDraft and drop the typed value on the next tick.
        var preserveSteps = _model.QuantityEditInProgress && StepListHasFocus();
        if (!preserveSteps)
        {
            _model.CommitQuantityEdit();
            draft = _model.Draft ?? draft;

            _applyingDraft = true;
            RenderPlanSteps(draft);
            _applyingDraft = false;
        }

        _applyingDraft = true;
        RenderCoverage(draft);
        RenderIssueList(draft);
        _applyingDraft = false;

        var plan = draft.Flatten();

        RenderTargetLine();
        RenderEnergy(plan);

        if (plan.Tasks.Count == 0)
        {
            var nothing = new Label { Text = "NOTHING TO PLAN" };
            nothing.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            nothing.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _materialsBody.AddChild(nothing);
        }
        else
        {
            RenderMaterialsAndDeliveries(plan);
            RenderFactories(plan);
            RenderCompetition(plan);
        }

        // A documented lower bound, not a promise: ProductionPlan.EstimatedTicks ignores
        // switch-over, queueing behind existing work, and energy contention. Read verbatim — the
        // row label carries the "minimum" framing, not a second sum computed here.
        _estimate.Text = $"{plan.EstimatedTicks} ticks (~{Units.FormatSimTime(plan.EstimatedTicks)})";

        var structuralIssues = draft.Issues.Where(i => !draft.IsCommittable && i.Step is not null).ToList();
        _unplannableSection.Visible = plan.Unplannable.Count > 0 || structuralIssues.Count > 0;
        if (plan.Unplannable.Count > 0)
        {
            var note = new Label
            {
                Text = "THE REST OF THIS PLAN STILL PROCEEDS WHILE THIS IS MISSING.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            note.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            note.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _unplannableBody.AddChild(note);

            foreach (var entry in plan.Unplannable)
            {
                _unplannableBody.AddChild(BoxSection.Row(
                    Labels.Item(entry.Item),
                    $"{Units.Format(entry.Quantity)} — {Labels.Unplannable(entry.Reason)}",
                    ShellPalette.StateWarn));
            }
        }

        foreach (var issue in structuralIssues)
        {
            _unplannableBody.AddChild(BoxSection.Row(
                Labels.Item(issue.Item),
                Labels.Issue(issue.Kind),
                ShellPalette.StateFault));
        }

        _approve.Text = ApproveButtonText(draft);

        // The approve lock forces this regardless of Tasks.Count: RefreshComposerPreview recomposes a
        // fresh candidate plan on every subsequent snapshot tick, and without this the button would
        // re-enable itself the instant that candidate has tasks again — the exact double-approve bug
        // this lock exists to close. See OnApprovePressed.
        _approve.Disabled = _model.ApproveLocked || plan.Tasks.Count == 0 || !draft.IsCommittable;
        UpdateEditorButtons();
    }

    /// <summary>Reading 1: the composer's own selection, restated — <see cref="ComposerModel.SelectedBuildTarget"/>
    /// in Build mode (an <see cref="ExecutorState.Label"/> already projected through
    /// <see cref="Dimenship.Core.State.WorldState.NameOf"/> by the time it reaches
    /// <see cref="BuildTarget.Label"/>), or the chosen produced item in Produce mode.</summary>
    private void RenderTargetLine()
    {
        _previewTarget.Text = SelectedTargetLabel().ToUpperInvariant();

        // Reading 2, Build mode only: the target archetype's Purpose, landed in Task 1 for this
        // exact use. The row itself is hidden in Produce mode by UpdateTargetVisibility; the text
        // is still kept tidy rather than left stale from whatever Build target was last selected.
        // Shown in its authored sentence case, not upper-cased like every short label around it —
        // Purpose is the one piece of actual prose in this view, and a full sentence in all caps
        // reads as shouting rather than as a label.
        _capability.Text = _model.BuildMode
            ? (_model.SelectedBuildTarget?.Purpose ?? "—")
            : "—";
    }

    private string SelectedTargetLabel() =>
        (_model.BuildMode ? _model.SelectedBuildTarget?.Label : _model.SelectedProduceTarget?.Label) ?? "—";

    /// <summary>Reading 5: energy demand is <c>EnergyPerRun × runs</c> summed over every
    /// <see cref="Produce"/> task the plan proposes — never a number the planner itself carries,
    /// since <see cref="ProductionPlan"/> has no energy field of its own. Shown on its own, not
    /// against <see cref="EnergyState.Capacity"/> or <see cref="EnergyState.Reserve"/>: those are
    /// documented on <see cref="EnergyState"/> itself as "the vessel's power position for one
    /// tick", a rate, while this sum is the whole plan's one-time total — the two units do not
    /// reconcile, and comparing them read a single Build run of <c>assemble_dock_unit</c>
    /// (<c>energyPerRun: 4800</c>) against the vessel's <c>energyCapacity: 10000</c> as
    /// "4.800 OF 10.000 CAPACITY", implying the run costs 48% of the vessel's power when it does
    /// not. The standing draw half below (Build mode only) is the target archetype's
    /// <see cref="Dimenship.Core.Content.FacilityArchetype.StandingPowerDraw"/> — an ongoing rate,
    /// what the vessel pays every tick after commissioning — which is the number that actually
    /// belongs beside a per-tick capacity, and stays compared against it exactly as before.</summary>
    private void RenderEnergy(ProductionPlan plan)
    {
        var catalog = ShellContent.Catalog;
        long demand = 0;
        long runCount = 0;

        foreach (var task in plan.Tasks)
        {
            if (task.Script.Action is Produce { Runs: { } runs } produce &&
                catalog.Schematics.TryGet(produce.Schematic, out var schematic))
            {
                demand += schematic.EnergyPerRun.Value * runs;
                runCount += runs;
            }
        }

        _energyDemand.Text = $"{Units.Format(demand)} MW TOTAL ACROSS {runCount} RUN(S)";

        _standingDraw.Text = _model.BuildMode && _model.SelectedBuildTarget is { } target
            ? $"{Units.Format(target.StandingPowerDraw)} MW ONGOING, AFTER IT IS BUILT"
            : "—";
    }

    /// <summary>
    /// Readings 3 and 6 together, one pass over the plan's own <see cref="Transfer"/> tasks in
    /// their plan order: every leg becomes a Deliveries row, and every leg whose item is not the
    /// goal item also becomes a Materials row of required / available / to-produce —
    /// <see cref="Transfer.Quantity"/>, <see cref="PlannedTask.AvailableAtSource"/>, and their
    /// difference floored at zero. Nothing here is re-derived from a schematic; both numbers come
    /// off the task the planner already built.
    /// <para>
    /// A <see cref="Transfer"/> whose item equals <see cref="ProductionPlan.Goal"/>'s is excluded
    /// from Materials outright, by item alone — <b>not</b> by matching it to one identified "final"
    /// leg. <c>ProductionPlanner.Require</c> emits a return-to-hold <c>Move</c> (ProductionPlanner.cs:155)
    /// every time the goal item is itself produced, before the plan's own <c>MoveFinal</c> leg to
    /// <see cref="ProductionPlan.Destination"/> ever runs — so the ordinary Build shape carries
    /// <i>two</i> transfers of the goal item, not one, and Produce mode (which never sets a
    /// <c>Destination</c> at all) carries the return-to-hold leg with no final leg to compare it
    /// against. Matching by destination alone caught only the second and left the first sitting in
    /// Materials, reporting the thing being built as one of its own inputs. Excluding by item can
    /// never wrongly hide a genuine input: a schematic that consumed the goal item as one of its
    /// own recursive inputs would be a cycle, and <c>ProductionPlanner</c>'s <c>visiting</c> set and
    /// <c>MaxDepth</c> catch that as <see cref="Unplannable"/> before any such cross-item
    /// <see cref="Transfer"/> could exist.
    /// </para>
    /// </summary>
    private void RenderMaterialsAndDeliveries(ProductionPlan plan)
    {
        foreach (var task in plan.Tasks)
        {
            if (task.Script.Action is not Transfer transfer)
            {
                continue;
            }

            var quantity = transfer.Quantity ?? 0;

            _deliveriesBody.AddChild(BoxSection.Row(
                Labels.Item(transfer.Item),
                $"{Units.Format(quantity)} · {StorageLabel(transfer.From)} → " +
                $"{StorageLabel(transfer.To)} · {TransportLabel(task.Executor)}"));

            if (transfer.Item == plan.Goal.Item)
            {
                // The goal item in transit toward wherever it's going — a return-to-hold leg or
                // the plan's own final delivery, either way the thing being built, not a material
                // the plan still needs. Deliveries above is where this belongs, not Materials.
                continue;
            }

            var available = task.AvailableAtSource;
            var toProduce = Math.Max(0, quantity - available);
            _materialsBody.AddChild(BoxSection.Row(
                Labels.Item(transfer.Item),
                $"{Units.Format(quantity)} REQUIRED · {Units.Format(available)} AVAILABLE · " +
                $"{Units.Format(toProduce)} TO PRODUCE"));
        }
    }

    /// <summary>Reading 4: one row per <see cref="Produce"/> task, the factory running it and the
    /// schematic and run count exactly as the planner set them on <see cref="PlannedTask"/>.</summary>
    private void RenderFactories(ProductionPlan plan)
    {
        foreach (var task in plan.Tasks)
        {
            if (task.Script.Action is not Produce produce)
            {
                continue;
            }

            var runsText = produce.Runs is { } runs ? $"×{runs}" : "(STANDING)";
            _factoriesBody.AddChild(BoxSection.Row(
                FactoryLabel(task.Executor),
                $"{produce.Schematic.Value.ToUpperInvariant()} {runsText}"));
        }
    }

    /// <summary>
    /// Reading 8: every distinct executor the plan itself names — a <see cref="Produce"/>'s
    /// factory or a <see cref="Transfer"/>'s line, de-duplicated in plan order — against how many
    /// entries already reference that same id in <see cref="WorldSnapshot.Tasks"/>. This is what is
    /// already queued on the executor this draft plan would also use, independent of the draft
    /// itself: the draft is never committed, so it never appears in <c>snapshot.Tasks</c> to double-count.
    /// </summary>
    private void RenderCompetition(ProductionPlan plan)
    {
        var snapshot = _lastSnapshot;
        var seen = new HashSet<ExecutorId>();

        foreach (var task in plan.Tasks)
        {
            if (!seen.Add(task.Executor))
            {
                continue;
            }

            // A Produce task's executor is always a facility; a Transfer task's executor is
            // always a line — the two lists are never both searched for the same id.
            var label = task.Script.Action is Produce
                ? FactoryLabel(task.Executor)
                : TransportLabel(task.Executor);
            var queued = snapshot?.Tasks.Count(t => t.Executor == task.Executor) ?? 0;

            _competitionBody.AddChild(BoxSection.Row(label, $"{queued} ALREADY QUEUED"));
        }
    }

    private string StorageLabel(StorageId id) =>
        (_lastSnapshot?.Storages.FirstOrDefault(s => s.Id == id)?.Label ?? id.Value).ToUpperInvariant();

    private string FactoryLabel(ExecutorId id) =>
        (_lastSnapshot?.Executors.FirstOrDefault(e => e.Id == id)?.Label ?? id.Value).ToUpperInvariant();

    private string TransportLabel(ExecutorId id) =>
        (_lastSnapshot?.Transports.FirstOrDefault(t => t.Id == id)?.Label ?? id.Value).ToUpperInvariant();

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

        column.AddChild(BuildPlanControls());
        column.AddChild(ShellTheme.Divider());

        _detailBody = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _detailBody.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(_detailBody);

        return column;
    }

    /// <summary>
    /// Opens the composer on whatever draft it holds, edits and undo included (UX-01): BACK and
    /// "+ NEW" both used to recompose from scratch, so reading a plan's detail cost the order being
    /// written beside it. DISCARD is now the one control that throws a draft away. With no draft
    /// there is nothing to resume, and this begins a fresh composition and reopens APPROVE, as it
    /// always did. A goal that changed meanwhile still recomposes, through
    /// <see cref="RefreshComposerPreview"/>'s own check.
    /// </summary>
    private void ShowComposer()
    {
        _model.ShowComposer();

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
        _model.SelectedPlan = id;
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

        var plan = snapshot.Plans.FirstOrDefault(p => p.Id == _model.SelectedPlan);
        _controlsRoot.Visible = plan is not null;
        if (plan is null)
        {
            _detailTitle.Text = "PLAN";
            _detailBody.AddChild(BoxSection.Row("STATUS", "NO LONGER PRESENT", ShellPalette.StateFault));
            return;
        }

        _detailTitle.Text = $"PLAN #{plan.Id} · {Labels.Item(plan.Goal.Item)}".ToUpperInvariant();
        RefreshPlanControls(plan, snapshot);

        _detailBody.AddChild(BoxSection.Row(
            "GOAL", $"{Units.Format(plan.Goal.Quantity)} {Labels.Item(plan.Goal.Item)}"));

        if (plan.Destination is { } destination)
        {
            var label = snapshot.Storages.FirstOrDefault(s => s.Id == destination)?.Label
                        ?? destination.Value;
            _detailBody.AddChild(BoxSection.Row("DESTINATION", label.ToUpperInvariant()));
        }

        var (stateText, stateColor) = PlanStateReading(plan.State);
        _detailBody.AddChild(BoxSection.Row(
            "STATE", plan.Held && plan.State == PlanState.Active ? $"{stateText} · HELD" : stateText,
            plan.Held && plan.State == PlanState.Active ? ShellPalette.StateWarn : stateColor));
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

            var (taskText, taskColor) = TaskStateReading(task.State, task.LastReason);
            _detailBody.AddChild(BoxSection.Row(Instruction(task.Action), taskText, taskColor));

            // Who it waits behind, when its reason has another party (K8): the plan holding its
            // stock or its room, or the task that outranked it. Read from the kernel's projection,
            // so this detail and the replay report cannot word one cause two ways.
            if (task.State == TaskState.Postponed && WaitCause.For(snapshot, task.Id) is { } cause)
            {
                var because = new Label
                {
                    Text = cause.Text,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                because.AddThemeColorOverride("font_color", ShellPalette.TextDim);
                because.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
                _detailBody.AddChild(because);
            }
        }
    }

    /// <summary>The plan detail's own root-cause reading for a stuck task — the same
    /// <c>"WORD — REASON"</c> shape, through <see cref="Conditions.Describe"/>, that the node card
    /// and the inspector's task rows use, so the schematic, the inspector and this detail cannot
    /// name one condition three ways.</summary>
    private static (string Text, Color Color) TaskStateReading(TaskState state, PostponeReason? reason) =>
        state switch
        {
            TaskState.Running => ("RUNNING", ShellPalette.StateOk),
            TaskState.Complete => ("DONE", ShellPalette.TextDim),
            TaskState.Postponed => ($"POSTPONED — {Conditions.Describe(reason)}", Conditions.Colour(reason)),
            _ => ("QUEUED", ShellPalette.TextFaint),
        };

    // ---- The build picker ---------------------------------------------------------------------

    /// <summary>Rebuilds <see cref="_buildTarget"/>'s items from the model's visible build targets:
    /// once from <see cref="BuildTargetRow"/>, and again only when a refresh reports the visible
    /// set changed, never on every tick, which would reset a dropdown the player has open. Mirrors
    /// <see cref="ProduceTargetRow"/>'s own empty-state handling.</summary>
    private void PopulateBuildOptions()
    {
        _buildTarget.Clear();

        foreach (var target in _model.VisibleBuildTargets)
        {
            _buildTarget.AddItem(target.Label.ToUpperInvariant());
        }

        if (_model.VisibleBuildTargets.Count == 0)
        {
            _buildTarget.AddItem("NOTHING LEFT TO BUILD");
            _buildTarget.Disabled = true;
            return;
        }

        _buildTarget.Disabled = false;
        _buildTarget.Select(Math.Clamp(_model.BuildIndex, 0, _model.VisibleBuildTargets.Count - 1));
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

    /// <summary>A dim uppercase caption over a wrapping value line, for a reading that is a full
    /// sentence (a facility's <see cref="Dimenship.Core.Content.FacilityArchetype.Purpose"/>) rather than a short
    /// number — <see cref="LiveRow"/>'s single-line, ellipsis-trimmed value would clip it instead
    /// of showing it.</summary>
    private static (Control Row, Label Value) WrappedRow(string label)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        var name = new Label { Text = label.ToUpperInvariant() };
        name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(name);

        var value = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        value.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        value.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        column.AddChild(value);

        return (column, value);
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
