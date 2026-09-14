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
    /// <summary>One whole construction unit, in the item's own milli-unit accounting.</summary>
    private const long UnitsToMilli = 1000;

    /// <summary>Commissioning consumes exactly one whole unit — never more, never a fraction.</summary>
    private const long LaunchPadUnits = 1;

    private ShellContext? _context;
    private WorldSnapshot? _lastSnapshot;

    /// <summary>Every scenario facility whose archetype names a construction unit, in scenario
    /// declaration order — resolved once, because content doesn't change mid-game.</summary>
    private List<BuildTarget> _buildTargets = new();

    /// <summary>The still-unbuilt subset of <see cref="_buildTargets"/>, recomputed from the latest
    /// snapshot and rebuilt into <see cref="_buildTarget"/>'s items only when it actually changes.</summary>
    private List<BuildTarget> _visibleBuildTargets = new();

    /// <summary>The <see cref="ExecutorId"/>s behind <see cref="_visibleBuildTargets"/>, kept only
    /// to detect whether the visible set changed since the last snapshot.</summary>
    private List<ExecutorId> _visibleBuildIds = new();

    private List<ProduceTarget> _produceTargets = new();

    private bool _buildMode = true;
    private int _buildIndex;
    private int _produceIndex;
    private long _quantityUnits = 1;

    /// <summary>Set the moment APPROVE commits a plan, and cleared only by an actual composer
    /// change — never by the periodic snapshot-driven preview refresh. This is what stops a second
    /// press of an already-disabled APPROVE from committing the same plan again: <see cref="RenderPreview"/>
    /// force-disables the button while this is true regardless of whether the freshly recomposed
    /// preview plan has tasks.</summary>
    private bool _approveLocked;

    private PlanId? _selectedPlan;
    private ProductionPlan? _currentPlan;

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
    /// The direct <see cref="OptionButton.Select"/> call below (rather than only setting
    /// <see cref="_buildIndex"/> and trusting the next refresh) matters because <see cref="Zone"/>
    /// calls <c>OnMount</c> before its own immediate re-delivery of the last snapshot, so
    /// <see cref="_lastSnapshot"/> here may still be null or stale and
    /// <see cref="RefreshVisibleBuildTargets"/> may not run before this method returns — without it
    /// the visible <see cref="OptionButton"/> would keep showing whatever it last showed while
    /// <see cref="_buildIndex"/> silently disagreed. <see cref="OnDiscardPressed"/> already calls
    /// <c>.Select()</c> directly for the same reason, and it does not raise <c>ItemSelected</c> in
    /// this Godot version, so this does not double-fire <see cref="RefreshComposerPreview"/>.
    /// </para>
    /// </summary>
    public override void OnMount(ShellContext context)
    {
        _context = context;

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
            _buildMode = true;
            ApplyModeChrome();
            UpdateTargetVisibility();

            var index = _visibleBuildTargets.FindIndex(t => t.Facility == facilityId);
            if (index >= 0)
            {
                _buildIndex = index;
                _buildTarget.Select(index);
            }

            _approveLocked = false;
            ShowComposer();
        }
    }

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

        // Everything below is read-only reporting on the draft plan, and it is the part that grew
        // from "one estimate line" to nine readings — scrolled so a long materials or deliveries
        // list never pushes MODE/TARGET or APPROVE/DISCARD off the panel.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);

        var sections = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sections.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        scroll.AddChild(sections);

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
        if (_buildMode == build)
        {
            return;
        }

        _buildMode = build;
        _approveLocked = false;
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

        // Capability gained and the standing power cost only mean anything for a facility being
        // built — Produce mode has no target archetype to read either from.
        _capabilityRow.Visible = _buildMode;
        _standingRow.Visible = _buildMode;
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
            _buildIndex = (int)index;
            _approveLocked = false;
            RefreshComposerPreview();
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
            _approveLocked = false;
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
            _approveLocked = false;
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
    /// Commits the previewed plan exactly once. The <c>_approveLocked</c> guard is defense in depth
    /// beside <see cref="RenderPreview"/> forcing <c>_approve.Disabled</c> true: a disabled Godot
    /// button does not deliver a real click, but this handler no longer trusts that alone — a second
    /// invocation for any reason returns immediately instead of re-reading <c>_currentPlan</c>, which
    /// <see cref="RefreshComposerPreview"/> repopulates with a fresh (uncommitted) candidate plan on
    /// every subsequent snapshot tick regardless of whether the last one was just approved.
    /// </summary>
    private void OnApprovePressed()
    {
        if (_approveLocked)
        {
            return;
        }

        if (_currentPlan is { Tasks.Count: > 0 } plan)
        {
            _context?.Actions.PlanApproved?.Invoke(plan);
            _currentPlan = null;
            _approveLocked = true;
            _approve.Disabled = true;
        }
    }

    /// <summary>Clears the draft and resets the composer to its declared defaults — both modes at
    /// once, since discard resets "the composer," not just whichever mode is active. Unlike
    /// <see cref="OnApprovePressed"/> this does not set <c>_approveLocked</c>: discarding starts a
    /// fresh composition, it does not lock a committed one.</summary>
    private void OnDiscardPressed()
    {
        _currentPlan = null;
        _approveLocked = false;

        _buildIndex = 0;
        if (_visibleBuildTargets.Count > 0)
        {
            _buildTarget.Select(0);
        }

        _produceIndex = 0;
        if (_produceTargets.Count > 0)
        {
            _produceTarget.Select(0);
        }

        _quantityUnits = 1;
        _quantity.Value = _quantityUnits;

        RefreshComposerPreview();
    }

    private void RefreshComposerPreview()
    {
        // Recomputed here, not in OnSnapshot directly, so it also runs from SetMode, ShowComposer
        // and OnDiscardPressed — every path that needs a fresh preview needs a fresh build list too.
        if (_lastSnapshot is { } snapshot)
        {
            RefreshVisibleBuildTargets(snapshot);
        }

        var plan = _context?.ComposePlan is { } compose && ComposerGoal() is { } goal
            ? compose(goal, _buildMode ? SelectedBuildTarget()?.Destination : null)
            : null;

        _currentPlan = plan;
        RenderPreview(plan);
    }

    private ItemAmount? ComposerGoal()
    {
        if (_buildMode)
        {
            return SelectedBuildTarget() is { } target
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

    /// <summary>The currently-selected build target, clamped against <see cref="_visibleBuildTargets"/>
    /// the same way the produce branch of <see cref="ComposerGoal"/> clamps <c>_produceIndex</c> — so
    /// an index left over from a larger list (a pad just commissioned and dropped out) can't fault.</summary>
    private BuildTarget? SelectedBuildTarget()
    {
        if (_visibleBuildTargets.Count == 0)
        {
            return null;
        }

        var index = Mathf.Clamp(_buildIndex, 0, _visibleBuildTargets.Count - 1);
        return _visibleBuildTargets[index];
    }

    /// <summary>
    /// Renders the nine named readings Decision 1 of the vessel construction interface spec maps
    /// out: target, capability gained, materials, factories, energy, deliveries, the estimate,
    /// competition and unplannable. Every number here is read once off <paramref name="plan"/> or
    /// the last snapshot and shown — nothing is re-derived, and in particular
    /// <see cref="ProductionPlan.EstimatedTicks"/> is never re-summed.
    /// </summary>
    private void RenderPreview(ProductionPlan? plan)
    {
        Clear(_materialsBody);
        Clear(_factoriesBody);
        Clear(_deliveriesBody);
        Clear(_competitionBody);
        Clear(_unplannableBody);

        if (plan is null)
        {
            _previewTarget.Text = "—";
            _capability.Text = "—";
            _energyDemand.Text = "—";
            _standingDraw.Text = "—";
            _estimate.Text = "—";

            var reason = new Label { Text = "NO TARGET TO PLAN" };
            reason.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            _materialsBody.AddChild(reason);

            _unplannableSection.Visible = false;
            _approve.Disabled = true;
            return;
        }

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

        _unplannableSection.Visible = plan.Unplannable.Count > 0;
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
                    $"{Units.Format(entry.Quantity)} — {Describe(entry.Reason)}",
                    ShellPalette.StateWarn));
            }
        }

        // _approveLocked forces this regardless of Tasks.Count: RefreshComposerPreview recomposes a
        // fresh candidate plan on every subsequent snapshot tick, and without this the button would
        // re-enable itself the instant that candidate has tasks again — the exact double-approve bug
        // this lock exists to close. See OnApprovePressed.
        _approve.Disabled = _approveLocked || plan.Tasks.Count == 0;
    }

    /// <summary>Reading 1: the composer's own selection, restated — <see cref="SelectedBuildTarget"/>
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
        _capability.Text = _buildMode
            ? (SelectedBuildTarget()?.Purpose ?? "—")
            : "—";
    }

    private string SelectedTargetLabel()
    {
        if (_buildMode)
        {
            return SelectedBuildTarget()?.Label ?? "—";
        }

        if (_produceTargets.Count == 0)
        {
            return "—";
        }

        var index = Mathf.Clamp(_produceIndex, 0, _produceTargets.Count - 1);
        return _produceTargets[index].Label;
    }

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

        _standingDraw.Text = _buildMode && SelectedBuildTarget() is { } target
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

        _detailBody = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _detailBody.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(_detailBody);

        return column;
    }

    private void ShowComposer()
    {
        _selectedPlan = null;
        _approveLocked = false;
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

        _detailTitle.Text = $"PLAN #{plan.Id} · {Labels.Item(plan.Goal.Item)}".ToUpperInvariant();

        _detailBody.AddChild(BoxSection.Row(
            "GOAL", $"{Units.Format(plan.Goal.Quantity)} {Labels.Item(plan.Goal.Item)}"));

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

            var (taskText, taskColor) = TaskStateReading(task.State, task.LastReason);
            _detailBody.AddChild(BoxSection.Row(Instruction(task.Action), taskText, taskColor));
        }
    }

    /// <summary>The plan detail's own root-cause reading for a stuck task — the same
    /// <c>"WORD — REASON"</c> shape <see cref="NodeCard"/>'s <c>Describe</c> and
    /// <see cref="FacilityInspectorPanel"/>'s own task rows already use, so the schematic, the
    /// inspector and this detail cannot name one condition three ways.</summary>
    private static (string Text, Color Color) TaskStateReading(TaskState state, PostponeReason? reason) =>
        state switch
        {
            TaskState.Running => ("RUNNING", ShellPalette.StateOk),
            TaskState.Complete => ("DONE", ShellPalette.TextDim),
            TaskState.Postponed => ($"POSTPONED — {Describe(reason)}", ShellPalette.StateWarn),
            _ => ("QUEUED", ShellPalette.TextFaint),
        };

    /// <summary>Identical to <see cref="NodeCard"/>'s protected <c>Describe</c> and
    /// <see cref="FacilityInspectorPanel"/>'s own private copy — this file is neither a
    /// <see cref="NodeCard"/> subclass nor that panel, so it carries its own rather than reusing
    /// either.</summary>
    private static string Describe(PostponeReason? reason) => reason switch
    {
        PostponeReason.InsufficientInputMaterial => "MISSING_INPUT",
        PostponeReason.InsufficientSourceMaterial => "NO_SOURCE_MATERIAL",
        PostponeReason.DestinationFull => "DESTINATION_FULL",
        PostponeReason.InsufficientEnergy => "INSUFFICIENT_ENERGY",
        PostponeReason.OutputRouteUnavailable => "NO_OUTPUT_ROUTE",
        PostponeReason.SafetyLock => "SAFETY_LOCK",
        _ => "UNKNOWN",
    };

    // ---- Targets, resolved once from content ----------------------------------------------

    /// <summary>
    /// Every scenario facility whose archetype names a construction unit, in scenario declaration
    /// order — read generically off each archetype rather than hard-coding an item or storage, so a
    /// content edit to a construction unit or a dock's hold reaches this composer for free. This is
    /// the composer's full, static candidate list: it never changes at runtime, because content
    /// doesn't change mid-game. What changes is which of these are still unbuilt, which is state and
    /// is read per snapshot by <see cref="RefreshVisibleBuildTargets"/>, not resolved here.
    /// </summary>
    private static List<BuildTarget> ResolveBuildTargets()
    {
        var scenario = ShellContent.DefaultVessel;
        var catalog = ShellContent.Catalog;
        var targets = new List<BuildTarget>();

        foreach (var facility in scenario.Facilities)
        {
            var archetype = catalog.Facility(facility.Archetype);
            if (archetype?.ConstructionUnit is not { } unit)
            {
                continue;
            }

            targets.Add(new BuildTarget(
                facility.Id, facility.NameOverride ?? archetype.Label, unit, facility.LocalStorage,
                archetype.Purpose, archetype.StandingPowerDraw));
        }

        return targets;
    }

    /// <summary>
    /// Recomputes which of <see cref="_buildTargets"/> are still unbuilt, and rebuilds the build
    /// target <see cref="OptionButton"/>'s items only when that visible set actually changed —
    /// comparing every tick unconditionally and rebuilding regardless would reset the player's open
    /// dropdown or selection while they are simply looking at it, which is not what "populated per
    /// snapshot" means. When the set does change, the previously-selected target stays selected if
    /// it is still present; otherwise selection falls back to index 0 (or the empty state, handled
    /// by <see cref="PopulateBuildOptions"/>, if the visible list is now empty).
    /// </summary>
    private void RefreshVisibleBuildTargets(WorldSnapshot snapshot)
    {
        var visible = _buildTargets.Where(t => StillUnbuilt(t.Facility, snapshot)).ToList();
        var ids = visible.Select(t => t.Facility).ToList();

        if (ids.SequenceEqual(_visibleBuildIds))
        {
            return;
        }

        var selected = SelectedBuildTarget()?.Facility;

        _visibleBuildTargets = visible;
        _visibleBuildIds = ids;
        _buildIndex = selected is { } id ? Math.Max(0, visible.FindIndex(t => t.Facility == id)) : 0;

        // The visible set changing is not the same event as the *selection* changing — a reorder
        // that leaves the same facility selected must leave the lock alone. It does change when the
        // previously-selected facility dropped out of the list and the index above fell back to
        // whatever is now at 0 (or to nothing, if the list is now empty): that is a real target
        // change happening entirely inside this method, never through the OptionButton's own
        // ItemSelected handler, which is the only other place this lock is cleared. Left uncleared,
        // a plan approved for a facility that has since commissioned would leave the player staring
        // at a fully-composed preview for whatever slot took its place with APPROVE disabled and
        // nothing on screen explaining why.
        //
        // Gated on _buildMode because this method runs every snapshot regardless of which mode is
        // active (a Produce-mode session still needs a fresh build list ready for when the player
        // switches back) — an unrelated facility commissioning while the player is in Produce mode
        // must not clear a lock that a Produce-mode approval set. Without this guard, approving a
        // Produce plan, then having any build target elsewhere on the vessel commission while still
        // on this screen, silently re-enables APPROVE for the already-committed Produce plan — the
        // exact double-commit this lock exists to prevent, reopened through Build mode's own fix.
        var nowSelected = _visibleBuildTargets.Count > 0
            ? _visibleBuildTargets[Mathf.Clamp(_buildIndex, 0, _visibleBuildTargets.Count - 1)].Facility
            : (ExecutorId?)null;
        if (_buildMode && nowSelected != selected)
        {
            _approveLocked = false;
        }

        PopulateBuildOptions();
    }

    /// <summary>An executor not yet reporting <c>Built</c> — or one the snapshot has no row for at
    /// all, which defensively counts as not yet built rather than silently dropping out.</summary>
    private static bool StillUnbuilt(ExecutorId facility, WorldSnapshot snapshot) =>
        snapshot.Executors.FirstOrDefault(e => e.Id == facility) is not { Built: true };

    /// <summary>Rebuilds <see cref="_buildTarget"/>'s items from <see cref="_visibleBuildTargets"/> —
    /// called once from <see cref="BuildTargetRow"/> and again only when
    /// <see cref="RefreshVisibleBuildTargets"/> finds the visible set changed, never on every
    /// tick. Mirrors <see cref="ProduceTargetRow"/>'s own empty-state handling.</summary>
    private void PopulateBuildOptions()
    {
        _buildTarget.Clear();

        foreach (var target in _visibleBuildTargets)
        {
            _buildTarget.AddItem(target.Label.ToUpperInvariant());
        }

        if (_visibleBuildTargets.Count == 0)
        {
            _buildTarget.AddItem("NOTHING LEFT TO BUILD");
            _buildTarget.Disabled = true;
            return;
        }

        _buildTarget.Disabled = false;
        _buildTarget.Select(Mathf.Clamp(_buildIndex, 0, _visibleBuildTargets.Count - 1));
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

            targets.Add(new ProduceTarget(schematic.Output.Item, Labels.Item(schematic.Output.Item)));
        }

        return targets;
    }

    private void ResolveTargets()
    {
        _buildTargets = ResolveBuildTargets();

        // Optimistic initial state before the first snapshot arrives: nothing is known to be built
        // yet, so every candidate is provisionally visible. RefreshVisibleBuildTargets corrects this
        // (rebuilding the OptionButton only if it turns out to differ) the moment a real snapshot's
        // Built readings are available, which in practice is within the first tick or two.
        _visibleBuildTargets = new List<BuildTarget>(_buildTargets);
        _visibleBuildIds = _buildTargets.Select(t => t.Facility).ToList();

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

    private readonly record struct BuildTarget(
        ExecutorId Facility,
        string Label,
        ItemId ConstructionUnit,
        StorageId Destination,
        string Purpose,
        long StandingPowerDraw);

    private readonly record struct ProduceTarget(ItemId Item, string Label);
}
