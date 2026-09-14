using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The vessel as a graph: facilities and storages as cards on an authored grid, transport routes
/// as edges coloured by live load. Strictly read-only — it issues no command and mutates nothing.
/// <para>
/// Pan is the canvas's position and zoom is its scale, which means Godot transforms card input
/// coordinates for free and a card's hit area follows the zoom with no extra work. Zoom moves in
/// fixed steps rather than continuously so text stays on whole-pixel sizes.
/// </para>
/// </summary>
public sealed partial class BaseGraphFocus : PanelBase
{
    private static readonly int[] ZoomSteps = { 50, 75, 100, 150, 200 };

    /// <summary>100% zoom, the resting default — <c>internal</c> rather than <c>private</c> so
    /// <see cref="ShellContext.GraphZoom"/>'s own default can reference this one value instead of
    /// repeating the literal <c>2</c>, which is exactly the kind of drift <see cref="ShellContext"/>'s
    /// doc comment on that property already claims does not happen.</summary>
    internal const int RestingZoom = 2;

    /// <summary>How near a click must land to count as hitting an edge, in unzoomed pixels.</summary>
    private const int EdgeHitRadius = 12;

    /// <summary>Below this a press-and-release is a click, not a pan.</summary>
    private const float DragThreshold = 4f;

    // Projected from the campaign's authored slots alone. Every slot is placed, built or not: a
    // dock commissioned mid-run has to stay where it was drawn, so built-ness is the card's
    // business and never the layout's.
    private readonly BaseGraphLayout _placements = BaseGraphLayout.For(ShellContent.DefaultVessel);
    private readonly List<NodeCard> _cards = new();
    private readonly Dictionary<StorageId, (int Column, int Row)> _storageCells = new();

    private ShellContext? _context;
    private ResourceStrip _strip = null!;
    private Control _viewport = null!;
    private GraphCanvas _canvas = null!;
    private GraphLegend _legend = null!;

    private int _zoom = RestingZoom;
    private Vector2 _pan;
    private bool _dragging;
    private bool _panned;
    private Vector2 _pressedAt;
    private GraphSelection? _selection;
    private Vector2 _content;
    private bool _built;

    public override PanelId Id => ShellRoot.OverviewId;

    public override string Title => "Base Graph";

    private float Magnification => ZoomSteps[_zoom] / 100f;

    public override void OnMount(ShellContext context)
    {
        _context = context;
        _selection = context.CurrentSelection;

        // Clamped rather than trusted, for the reason every reading off a shared surface is: the
        // step is an index into ZoomSteps, and a value from outside that range would not misdraw —
        // it would throw on the next read of Magnification.
        _zoom = Mathf.Clamp(context.GraphZoom, 0, ZoomSteps.Length - 1);
        _pan = context.GraphPan;

        // Not optional, and the easiest line in this file to leave out. Zone.Show adds the panel to
        // the tree before it mounts it, so _Ready has already run and already applied a transform —
        // built from the field initialisers, which is the resting camera and not the one the player
        // left. ApplyTransform is the only thing that writes the canvas's Position and Scale, so
        // restoring the fields without calling it again would leave the graph parked at its resting
        // zoom until the player next touched the wheel, with the right numbers sitting unused.
        ApplyTransform();
    }

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(column);

        _strip = new ResourceStrip();
        column.AddChild(_strip);

        _viewport = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _viewport.GuiInput += OnViewportInput;
        column.AddChild(_viewport);

        _canvas = new GraphCanvas();
        _viewport.AddChild(_canvas);

        // Added after the canvas so it draws over the graph, and anchored to the viewport rather
        // than the canvas so it does not scroll away with it.
        _legend = new GraphLegend
        {
            AnchorTop = 1,
            AnchorBottom = 1,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _viewport.AddChild(_legend);
        _legend.Resized += PlaceLegend;

        ApplyTransform();
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        if (!_built)
        {
            Build(snapshot);
            _built = true;
        }

        _strip.Refresh(snapshot);

        foreach (var card in _cards)
        {
            card.Refresh(snapshot);
        }

        _canvas.SetEdges(BuildEdges(snapshot));
        ApplySelection();
    }

    /// <summary>
    /// Lays out one card per node, once. Nothing in the world definition is built or removed
    /// while the shell runs, so a rebuild would only ever produce the same tree again.
    /// <para>
    /// A facility's buffer gets no card of its own: it is drawn inside the facility that works it,
    /// and a route ending at that buffer ends at that facility's card. Only storages the layout
    /// places — the vessel's central hold — stand on the grid by themselves.
    /// </para>
    /// </summary>
    private void Build(WorldSnapshot snapshot)
    {
        var used = new HashSet<(int, int)>();
        var maxRow = _placements.Power.Row;

        foreach (var cell in _placements.Producers.Values.Concat(_placements.Storages.Values))
        {
            maxRow = Mathf.Max(maxRow, cell.Row);
        }

        // The unplaced strip runs along the bottom, one clear row below everything authored.
        var strayColumn = 0;
        var strayRow = maxRow + 2;

        var drawn = BaseGraphNodes.DrawnStorages(
            _placements, snapshot.Executors.Select(e => (e.Id, e.LocalStorage)));

        foreach (var executor in snapshot.Executors)
        {
            (int Column, int Row) cell;
            string badge;

            if (_placements.Producers.TryGetValue(executor.Id, out var placed))
            {
                cell = (placed.Column, placed.Row);
                badge = placed.Badge;
            }
            else
            {
                (cell, badge) = Stray(executor.Id.Value, ref strayColumn, strayRow);
            }

            Place(
                new ExecutorCard(executor.Id, executor.Label, executor.Type, badge, executor.LocalStorage),
                cell,
                used);
        }

        foreach (var storage in snapshot.Storages)
        {
            if (_placements.Storages.TryGetValue(storage.Id, out var placed))
            {
                _storageCells[storage.Id] = (placed.Column, placed.Row);
                Place(
                    new StorageCard(storage.Id, storage.Label, placed.Badge),
                    (placed.Column, placed.Row),
                    used);
                continue;
            }

            // Drawn inside the facility that works it. Its contents are that card's to report, and
            // an edge to it lands on that card.
            if (drawn.TryGetValue(storage.Id, out var owner))
            {
                _storageCells[storage.Id] = (owner.Column, owner.Row);
                continue;
            }

            var (cell, badge) = Stray(storage.Id.Value, ref strayColumn, strayRow);
            _storageCells[storage.Id] = cell;
            Place(new StorageCard(storage.Id, storage.Label, badge), cell, used);
        }

        // Authored like every other card, and edgeless: energy is a global pool, so drawing power
        // lines to the facilities that draw from it would be a lie about how it works.
        Place(
            new PowerCard(_placements.Power.Badge),
            (_placements.Power.Column, _placements.Power.Row),
            used);

        _content = Content(used);
        _canvas.CustomMinimumSize = _content;
        _canvas.Size = _content;
    }

    /// <summary>
    /// A node the layout does not place. It is drawn in the strip along the bottom and badged with
    /// a question mark, because a card carrying no badge at all would look like a design choice
    /// rather than the content error it is.
    /// </summary>
    private ((int Column, int Row) Cell, string Badge) Stray(string id, ref int column, int row)
    {
        // Never silently hidden: an unplaced node is drawn where it can be seen and said out loud.
        GD.PushWarning(
            $"Base graph: '{id}' has no authored placement and is drawn in the unplaced strip.");

        return ((column++, row), "?");
    }

    private void Place(NodeCard card, (int Column, int Row) cell, HashSet<(int, int)> used)
    {
        var rect = GraphGeometry.CellRect(cell.Column, cell.Row);
        var offset = Vector2.Zero;

        if (!used.Add(cell))
        {
            // Both drawn, one nudged, and the collision reported. Hiding either would leave a
            // node missing from the graph with nothing to say why.
            GD.PushWarning(
                $"Base graph: cell ({cell.Column}, {cell.Row}) holds more than one node; " +
                "the later one is offset by half a cell.");
            offset = new Vector2(GraphGeometry.CellWidth / 2f, GraphGeometry.CellHeight / 2f);
        }

        card.Position = new Vector2(rect.X, rect.Y) + offset;
        card.Size = new Vector2(rect.W, rect.H);
        card.Chosen += chosen => Select(chosen);

        _cards.Add(card);
        _canvas.AddChild(card);
    }

    private static Vector2 Content(IEnumerable<(int Column, int Row)> cells)
    {
        var (width, height) = GraphGeometry.ContentSize(cells);
        return new Vector2(width, height);
    }

    /// <summary>
    /// One drawn edge per route, except that an opposing pair between the same two storages is
    /// merged into a single double-headed edge: two lines a few pixels apart would be two things
    /// on screen where the player sees one link.
    /// </summary>
    private List<GraphCanvas.Edge> BuildEdges(WorldSnapshot snapshot)
    {
        var edges = new List<GraphCanvas.Edge>(snapshot.Transports.Count);
        var merged = new HashSet<ExecutorId>();
        var fan = new Dictionary<(int Column, int Row), int>();

        foreach (var line in snapshot.Transports)
        {
            if (merged.Contains(line.Id))
            {
                continue;
            }

            if (!_storageCells.TryGetValue(line.From, out var from) ||
                !_storageCells.TryGetValue(line.To, out var to))
            {
                continue;
            }

            // Both ends fold into one card — a line between two buffers of one facility, or from a
            // facility to its own buffer. There is no line to draw between a card and itself, and
            // drawing a stub that went nowhere would read as a fault in the vessel rather than in
            // its content. The layout tests assert this never happens.
            if (from == to)
            {
                GD.PushWarning(
                    $"Base graph: '{line.Id}' begins and ends on the same card; it is not drawn.");
                continue;
            }

            merged.Add(line.Id);

            var back = snapshot.Transports.FirstOrDefault(
                other => !merged.Contains(other.Id) && other.From == line.To && other.To == line.From);
            if (back is not null)
            {
                merged.Add(back.Id);
            }

            // Counted per card rather than per pair of cards. Every edge leaves and arrives at the
            // centre of the side facing the other end, so with the central storage joined to seven
            // things, edges approaching it from the same side would overprint however far apart
            // their far ends are. Giving each edge that touches a card the next offset spreads the
            // whole fan, and it subsumes the parallel-pair case: two routes between the same two
            // cards touch both of them and so cannot share an offset either.
            var index = Mathf.Max(fan.GetValueOrDefault(from), fan.GetValueOrDefault(to));
            fan[from] = index + 1;
            fan[to] = index + 1;

            var forwardBand = Band(line);
            FlowBand? backBand = back is null ? null : Band(back);

            // A merged pair is one drawn edge for two routes, and the same "worse reading wins"
            // rule applies to the shared stroke and to whether it is built: a route commissioned
            // in one direction only is not fully usable yet, and drawing it as built because its
            // opposite leg happens to be would hide exactly the state a player approving a plan
            // needs to see. Arrowheads keep their own bands so an idle reverse tip stays idle.
            var band = backBand is { } other
                ? (FlowBand)Mathf.Max((int)forwardBand, (int)other)
                : forwardBand;
            var built = line.Built && (back?.Built ?? true);

            edges.Add(new GraphCanvas.Edge(
                line.Id.Value,
                back?.Id.Value,
                GraphGeometry.EdgePolyline(
                    GraphGeometry.CellRect(from.Column, from.Row),
                    GraphGeometry.CellRect(to.Column, to.Row),
                    index),
                band,
                forwardBand,
                backBand,
                line.CargoFillPermille,
                back?.CargoFillPermille ?? 0,
                GraphCode.Of(band),
                built));
        }

        return edges;
    }

    /// <summary>
    /// A line's load band, read off what it took on rather than what it put down. Intake is what
    /// working at a fraction of throughput means for a conveyor, and it is the reading that is
    /// right on the first tick of a haul, before anything has crossed the belt yet.
    /// </summary>
    private static FlowBand Band(TransportExecutorState line) =>
        FlowBands.Classify(
            line.LoadedLastTick,
            line.ThroughputPerTick,
            line.Status == ExecutorStatus.AllQueuedTasksBlocked);

    private void OnViewportInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } up:
                StepZoom(1, up.Position);
                _viewport.AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } down:
                StepZoom(-1, down.Position);
                _viewport.AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _dragging = true;
                    _panned = false;
                    _pressedAt = button.Position;
                }
                else if (_dragging)
                {
                    _dragging = false;

                    // A drag that moved is a pan; one that did not is a click on whatever is
                    // under it, which for the canvas means an edge or nothing.
                    if (!_panned)
                    {
                        SelectAt(button.Position);
                    }
                }

                _viewport.AcceptEvent();
                break;

            case InputEventMouseMotion motion when _dragging:
                if (motion.Position.DistanceTo(_pressedAt) > DragThreshold)
                {
                    _panned = true;
                }

                if (_panned)
                {
                    _pan += motion.Relative;
                    ApplyTransform();
                }

                break;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F } key &&
            !key.CtrlPressed)
        {
            Fit();
            AcceptEvent();
        }
    }

    /// <summary>Zooms about the cursor, so whatever is under it stays under it.</summary>
    private void StepZoom(int steps, Vector2 anchor)
    {
        var before = Magnification;
        _zoom = Mathf.Clamp(_zoom + steps, 0, ZoomSteps.Length - 1);

        var after = Magnification;
        if (Mathf.IsEqualApprox(before, after))
        {
            return;
        }

        _pan = anchor - ((anchor - _pan) * (after / before));
        ApplyTransform();
    }

    /// <summary>
    /// Everything in view, at the largest fixed step that fits. Snapped to a step rather than
    /// scaled exactly, so a fit never lands the text on a fractional pixel size.
    /// </summary>
    private void Fit()
    {
        var available = _viewport.Size;
        if (_content.X <= 0 || _content.Y <= 0 || available.X <= 0 || available.Y <= 0)
        {
            return;
        }

        _zoom = 0;
        for (var i = ZoomSteps.Length - 1; i >= 0; i--)
        {
            var scale = ZoomSteps[i] / 100f;
            if (_content.X * scale <= available.X && _content.Y * scale <= available.Y)
            {
                _zoom = i;
                break;
            }
        }

        _pan = ((available - (_content * Magnification)) / 2f).Floor();
        ApplyTransform();
    }

    /// <summary>
    /// The one place the camera reaches the canvas, and therefore the one place it is handed back to
    /// the shell. <see cref="StepZoom"/>, <see cref="Fit"/> and the pan drag all already end here, so
    /// the write-back costs one guarded assignment rather than three call sites that can each be
    /// forgotten separately — and a fourth mutator added later gets it for free.
    /// </summary>
    private void ApplyTransform()
    {
        _canvas.Position = _pan;
        _canvas.Scale = new Vector2(Magnification, Magnification);

        // Null on the call _Ready makes, which happens before the panel is mounted.
        if (_context is not null)
        {
            _context.GraphZoom = _zoom;
            _context.GraphPan = _pan;
        }
    }

    private void PlaceLegend() =>
        _legend.Position = new Vector2(
            ShellPalette.SpaceMd, -_legend.Size.Y - ShellPalette.SpaceMd);

    private void SelectAt(Vector2 point)
    {
        var local = (point - _pan) / Magnification;
        var x = Mathf.RoundToInt(local.X);
        var y = Mathf.RoundToInt(local.Y);

        GraphCanvas.Edge? nearest = null;
        var nearestDistance = int.MaxValue;

        foreach (var edge in _canvas.Edges)
        {
            var distance = GraphGeometry.HitDistanceSquared(edge.Points, x, y);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = edge;
            }
        }

        if (nearest is null || nearestDistance > EdgeHitRadius * EdgeHitRadius)
        {
            Select(null);
            return;
        }

        // A merged edge stands for two lines. Clicking it again moves to the other one, which is
        // the only way to reach the return leg of a link that is drawn once.
        var id = nearest.BackId is not null &&
                 _selection is { Kind: GraphNodeKind.Transport } current &&
                 current.Id == nearest.Id
            ? nearest.BackId
            : nearest.Id;

        Select(new GraphSelection(GraphNodeKind.Transport, id));
    }

    private void Select(GraphSelection? selection)
    {
        _selection = selection;
        ApplySelection();

        _context?.Actions.SelectionChanged?.Invoke(selection);

        if (selection is not null)
        {
            _context?.Actions.InspectRequested?.Invoke();
        }
    }

    private void ApplySelection()
    {
        foreach (var card in _cards)
        {
            card.SetSelected(_selection == card.Selection);
        }

        _canvas.SetSelected(
            _selection is { Kind: GraphNodeKind.Transport } line ? line.Id : null);
    }
}
