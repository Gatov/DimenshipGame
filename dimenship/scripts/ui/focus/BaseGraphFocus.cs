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
    private const int RestingZoom = 2;

    /// <summary>How near a click must land to count as hitting an edge, in unzoomed pixels.</summary>
    private const int EdgeHitRadius = 12;

    /// <summary>Below this a press-and-release is a click, not a pan.</summary>
    private const float DragThreshold = 4f;

    private readonly BaseGraphLayout _placements = BaseGraphLayout.ForDefaultWorld();
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
    /// </summary>
    private void Build(WorldSnapshot snapshot)
    {
        var used = new HashSet<(int, int)>();
        var maxColumn = 0;
        var maxRow = 0;

        foreach (var cell in _placements.Producers.Values.Concat(_placements.Storages.Values))
        {
            maxColumn = Mathf.Max(maxColumn, cell.Column);
            maxRow = Mathf.Max(maxRow, cell.Row);
        }

        // The unplaced strip runs along the bottom, one clear row below everything authored.
        var strayColumn = 0;
        var strayRow = maxRow + 2;

        foreach (var executor in snapshot.Executors)
        {
            var (cell, badge) = _placements.Producers.TryGetValue(executor.Id, out var placed)
                ? ((placed.Column, placed.Row), placed.Badge)
                : Stray(executor.Id.Value, ref strayColumn, strayRow);

            Place(new ExecutorCard(executor.Id, executor.Label, executor.Type, badge), cell, used);
        }

        foreach (var storage in snapshot.Storages)
        {
            var (cell, badge) = _placements.Storages.TryGetValue(storage.Id, out var placed)
                ? ((placed.Column, placed.Row), placed.Badge)
                : Stray(storage.Id.Value, ref strayColumn, strayRow);

            _storageCells[storage.Id] = cell;
            Place(new StorageCard(storage.Id, storage.Label, badge), cell, used);
        }

        // Pinned clear of the authored grid, and edgeless: energy is a global pool, so drawing
        // power lines to the facilities that draw from it would be a lie about how it works.
        Place(new PowerCard(), (maxColumn + 1, 0), used);

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
        var parallel = new Dictionary<(StorageId, StorageId), int>();

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

            merged.Add(line.Id);

            var back = snapshot.Transports.FirstOrDefault(
                other => !merged.Contains(other.Id) && other.From == line.To && other.To == line.From);
            if (back is not null)
            {
                merged.Add(back.Id);
            }

            // Keyed on the unordered pair so a merged edge and a second route between the same
            // two storages are pushed apart rather than overprinting.
            var key = Pair(line.From, line.To);
            var index = parallel.GetValueOrDefault(key);
            parallel[key] = index + 1;

            var band = Band(line);
            if (back is not null)
            {
                // Enum order runs Idle, Low, Normal, High, Blocked, so the worse reading wins and
                // a blocked half is never hidden behind a busy one.
                band = (FlowBand)Mathf.Max((int)band, (int)Band(back));
            }

            edges.Add(new GraphCanvas.Edge(
                line.Id.Value,
                back?.Id.Value,
                GraphGeometry.EdgePolyline(
                    GraphGeometry.CellRect(from.Column, from.Row),
                    GraphGeometry.CellRect(to.Column, to.Row),
                    index),
                band,
                GraphCode.Of(band)));
        }

        return edges;
    }

    private static (StorageId, StorageId) Pair(StorageId a, StorageId b) =>
        string.CompareOrdinal(a.Value, b.Value) <= 0 ? (a, b) : (b, a);

    private static FlowBand Band(TransportExecutorState line) =>
        FlowBands.Classify(
            line.MovedLastTick,
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

    private void ApplyTransform()
    {
        _canvas.Position = _pan;
        _canvas.Scale = new Vector2(Magnification, Magnification);
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
