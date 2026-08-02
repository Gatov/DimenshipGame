using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Root of the shell. Registers panels, builds the zone tree, and pumps snapshots.</summary>
public sealed partial class ShellRoot : Control
{
    /// <summary>
    /// The centre view. Its value is still "overview" although it now shows the base graph:
    /// the string is written into saved layout files, and renaming it would silently reset every
    /// player's layout to gain nothing.
    /// </summary>
    public static readonly PanelId OverviewId = new("overview");

    public static readonly PanelId RoboticsId = new("robotics");
    public static readonly PanelId DoctrineId = new("doctrine");
    public static readonly PanelId ProcessesId = new("processes");
    public static readonly PanelId EnergyBudgetId = new("energy_budget");
    public static readonly PanelId EventLogId = new("event_log");
    public static readonly PanelId FacilityInspectorId = new("facility_inspector");

    private static readonly LayoutState Defaults =
        new(OverviewId, EnergyBudgetId, EventLogId, 900, 320, false, false);

    private readonly PanelRegistry _registry = new();
    private readonly ShellActions _actions = new();

    private SimulationDriver _driver = null!;
    private ShellContext _context = null!;
    private Rail _rail = null!;
    private Zone _centre = null!;
    private Zone _inspector = null!;
    private Zone _console = null!;
    private HSplitContainer _inspectorSplit = null!;
    private VSplitContainer _consoleSplit = null!;

    private LayoutState _layout = Defaults;
    private WorldSnapshot? _lastSnapshot;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = ShellTheme.Build();

        _driver = new SimulationDriver { Name = "SimulationDriver" };
        AddChild(_driver);

        _context = new ShellContext(_actions);

        RegisterPanels();
        WireActions();

        _layout = LayoutStore.Load(_registry.Descriptors, Defaults).State;

        BuildTree();
        ApplyLayout();
    }

    public override void _UnhandledInput(InputEvent @event) => _actions.Handle(@event);

    public override void _Process(double delta)
    {
        // Snapshots are replaced wholesale rather than mutated, so reference inequality is an
        // exact change test — no dirty flags, no per-field comparison.
        var snapshot = _driver.Snapshot;
        if (ReferenceEquals(snapshot, _lastSnapshot))
        {
            return;
        }

        _lastSnapshot = snapshot;
        _centre.Deliver(snapshot);
        _inspector.Deliver(snapshot);
        _console.Deliver(snapshot);
    }

    private void RegisterPanels()
    {
        // The base graph is the centre view the vessel is read from, and it replaced the tile-and-
        // list overview rather than sitting beside it: two answers to "what is this vessel doing"
        // is one too many. Its identifier stays "overview" so a saved layout keeps working. The
        // remaining three focus views are still placeholders pending their own specs.
        _registry.Register(
            new PanelDescriptor(OverviewId, "Base Graph", ZoneKind.Focus),
            () => new BaseGraphFocus());

        Placeholder(RoboticsId, "Robotics", ZoneKind.Focus,
            "Robot construction: frame plus subsystem slots, with a live stat rollup.");
        Placeholder(DoctrineId, "Doctrine", ZoneKind.Focus,
            "Rule editor: nested conditions and an ordered action list. No loops.");
        Placeholder(ProcessesId, "Processes", ZoneKind.Focus,
            "Scheduled processes in priority order, with a Gantt drill-down per process.");

        // Panels.
        _registry.Register(
            new PanelDescriptor(EnergyBudgetId, "Energy Budget", ZoneKind.Panel),
            () => new EnergyBudgetPanel());
        _registry.Register(
            new PanelDescriptor(EventLogId, "Event Log", ZoneKind.Panel),
            () => new EventLogPanel());
        _registry.Register(
            new PanelDescriptor(FacilityInspectorId, "Facility Inspector", ZoneKind.Panel),
            () => new FacilityInspectorPanel());

        _actions.FocusOrder = _registry.OfKind(ZoneKind.Focus)
            .OrderBy(d => d.Title)
            .Select(d => d.Id)
            .ToList();
    }

    private void Placeholder(PanelId id, string title, ZoneKind zone, string body) =>
        _registry.Register(
            new PanelDescriptor(id, title, zone),
            () => new PlaceholderPanel(id, title, body, zone));

    private void WireActions()
    {
        _actions.FocusRequested = id =>
        {
            _layout = _layout with { ActiveFocus = id };
            _centre.Show(id);
            _rail.SetActive(id);
            Persist();
        };
        _actions.PauseToggled = _driver.TogglePause;
        _actions.StepRequested = _driver.Step;
        // Buttons stay focusable so Tab traversal works, which means a focused Button consumes
        // Space before _UnhandledInput sees it. Escape drops focus and hands the accelerators back.
        _actions.FocusReleased = () => GetViewport().GuiReleaseFocus();
        _actions.SpeedUpRequested = _driver.SpeedUp;
        _actions.SpeedDownRequested = _driver.SpeedDown;
        _actions.InspectorToggled = () =>
        {
            _layout = _layout with { InspectorCollapsed = !_layout.InspectorCollapsed };
            ApplyZoneVisibility();
            Persist();
        };
        _actions.ConsoleToggled = () =>
        {
            _layout = _layout with { ConsoleCollapsed = !_layout.ConsoleCollapsed };
            ApplyZoneVisibility();
            Persist();
        };

        // The context holds the selection so a panel mounted after the click still renders it.
        // Redelivering the snapshot is what makes the mounted inspector redraw immediately: the
        // selection changed, but the snapshot has not, so _Process would not push one until the
        // next tick and the panel would lag a click behind.
        _actions.SelectionChanged = selection =>
        {
            _context.CurrentSelection = selection;
            if (_lastSnapshot is not null)
            {
                _inspector.Deliver(_lastSnapshot);
            }
        };

        // Unconditionally, on every selection. The player clicked a node to see its detail, and a
        // rule that only sometimes showed it would be worse than one that always does.
        _actions.InspectRequested = () =>
        {
            if (_layout.InspectorPanel != FacilityInspectorId)
            {
                _layout = _layout with { InspectorPanel = FacilityInspectorId };
                _inspector.Show(FacilityInspectorId);
            }

            if (_layout.InspectorCollapsed)
            {
                _layout = _layout with { InspectorCollapsed = false };
                ApplyZoneVisibility();
            }

            Persist();
        };
    }

    private void BuildTree()
    {
        AddChild(BuildBackdrop());

        var column = new VBoxContainer();
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);

        column.AddChild(BuildMenuBar());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        column.AddChild(body);

        _rail = new Rail(_registry, _actions) { Name = "Rail" };
        body.AddChild(_rail);

        _inspectorSplit = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(_inspectorSplit);

        // Horizontal ExpandFill here is load-bearing: within _inspectorSplit, whichever child
        // carries the expand flag is the one that absorbs surplus window width. This is the
        // first child, so it (centre + console, stacked inside it) grows and the inspector does
        // not — see the SizeFlagsHorizontal reset below.
        _consoleSplit = new VSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _inspectorSplit.AddChild(_consoleSplit);

        _centre = new Zone(ZoneKind.Focus, _registry, _context, showPicker: false) { Name = "CentreZone" };
        _consoleSplit.AddChild(_centre);

        _console = new Zone(ZoneKind.Panel, _registry, _context) { Name = "ConsoleZone" };
        _console.CustomMinimumSize = new Vector2(0, 120);
        _consoleSplit.AddChild(_console);

        _inspector = new Zone(ZoneKind.Panel, _registry, _context) { Name = "InspectorZone" };
        _inspector.CustomMinimumSize = new Vector2(240, 0);
        _inspectorSplit.AddChild(_inspector);
        // Zone._Ready runs synchronously during AddChild and unconditionally claims ExpandFill on
        // both axes. That is correct for the centre and console zones but wrong for the inspector,
        // which must keep a fixed minimum instead of competing for surplus width, so it is reset
        // here, after the child has entered the tree and _Ready has already run.
        _inspector.SizeFlagsHorizontal = SizeFlags.Fill;

        column.AddChild(new StatusBar(_driver) { Name = "StatusBar" });

        _inspector.PanelChosen += id =>
        {
            _layout = _layout with { InspectorPanel = id };
            _inspector.Show(id);
            Persist();
        };
        _console.PanelChosen += id =>
        {
            _layout = _layout with { ConsolePanel = id };
            _console.Show(id);
            Persist();
        };
        // Dragged fires per mouse-motion frame for the whole duration of a drag; persisting on
        // every one of those would mean a disk write per frame. Update the in-memory layout live
        // so the split tracks the pointer, and defer the write to DragEnded, which fires once.
        _inspectorSplit.Dragged += offset =>
        {
            _layout = _layout with { InspectorSplitOffset = (int)offset };
        };
        _inspectorSplit.DragEnded += Persist;
        _consoleSplit.Dragged += offset =>
        {
            _layout = _layout with { ConsoleSplitOffset = (int)offset };
        };
        _consoleSplit.DragEnded += Persist;
    }

    /// <summary>
    /// The backdrop stack: a flat floor, the nebula over it, a scrim over that. The floor stays
    /// underneath so a missing texture degrades to the old flat fill instead of to bare viewport,
    /// and so the scrim has something opaque to darken towards.
    /// </summary>
    private static Control BuildBackdrop()
    {
        var backdrop = new Control { Name = "Backdrop", MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);

        var floor = new ColorRect { Color = ShellPalette.BgBase, MouseFilter = MouseFilterEnum.Ignore };
        floor.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.AddChild(floor);

        if (ShellBackdrop.Texture is not { } texture)
        {
            return backdrop;
        }

        // KeepAspectCovered crops instead of letterboxing, so the image fills every window aspect.
        // IgnoreSize stops the 2516x1664 source from claiming a minimum size larger than the window.
        // FrostPane's shader reproduces this same fit, so the two must change together.
        var image = new TextureRect
        {
            Texture = texture,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        image.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.AddChild(image);

        var scrim = new ColorRect { Color = ShellPalette.BgScrim, MouseFilter = MouseFilterEnum.Ignore };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        backdrop.AddChild(scrim);

        return backdrop;
    }

    private Control BuildMenuBar()
    {
        var bar = new HBoxContainer();
        bar.CustomMinimumSize = new Vector2(0, 26);
        bar.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);

        var brand = new Label { Text = "\u25ae DIMENSHIP", VerticalAlignment = VerticalAlignment.Center };
        brand.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
        brand.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        bar.AddChild(brand);

        var vessel = new MenuButton { Text = "Vessel" };
        vessel.GetPopup().AddItem("Return to start screen", 0);
        vessel.GetPopup().AddItem("Quit", 1);
        vessel.GetPopup().IdPressed += id =>
        {
            if (id == 0)
            {
                GetTree().ChangeSceneToFile("res://scenes/StartScreen.tscn");
            }
            else
            {
                GetTree().Quit();
            }
        };
        bar.AddChild(vessel);

        var view = new MenuButton { Text = "View" };
        view.GetPopup().AddItem("Toggle inspector", 0);
        view.GetPopup().AddItem("Toggle console", 1);
        view.GetPopup().AddItem("Reset layout", 2);
        view.GetPopup().IdPressed += id =>
        {
            switch (id)
            {
                case 0:
                    _actions.InspectorToggled?.Invoke();
                    break;
                case 1:
                    _actions.ConsoleToggled?.Invoke();
                    break;
                case 2:
                    _layout = Defaults;
                    ApplyLayout();
                    Persist();
                    break;
            }
        };
        bar.AddChild(view);

        if (OS.IsDebugBuild())
        {
            var debug = new MenuButton { Text = "Debug" };
            debug.GetPopup().AddItem("Advance 1 hour", 0);

            // One tick is one simulated second, so an hour is Units.TicksPerHour — not 60, which
            // is a minute, and not a Step() loop, which no-ops unless the sim is paused.
            debug.GetPopup().IdPressed += _ => _driver.AdvanceTicks(Units.TicksPerHour);
            bar.AddChild(debug);
        }

        return bar;
    }

    private void ApplyLayout()
    {
        _centre.Show(_layout.ActiveFocus);
        _inspector.Show(_layout.InspectorPanel);
        _console.Show(_layout.ConsolePanel);
        _rail.SetActive(_layout.ActiveFocus);
        // SplitContainer.SplitOffset is obsolete in Godot 4.7; SplitOffsets is the replacement.
        // Both splits here have exactly two children, so a single-element array is equivalent.
        _inspectorSplit.SplitOffsets = new[] { _layout.InspectorSplitOffset };
        _consoleSplit.SplitOffsets = new[] { _layout.ConsoleSplitOffset };
        ApplyZoneVisibility();
    }

    private void ApplyZoneVisibility()
    {
        _inspector.Visible = !_layout.InspectorCollapsed;
        _console.Visible = !_layout.ConsoleCollapsed;
        _rail.SetZoneState(_inspector.Visible, _console.Visible);
    }

    private void Persist() => LayoutStore.Save(_layout);
}
