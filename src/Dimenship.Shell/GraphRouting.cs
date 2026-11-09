using Rect = (int X, int Y, int W, int H);

namespace Dimenship.Shell;

/// <summary>
/// Routes every edge of a graph at once: orthogonal polylines from one card's side to another's,
/// elbowing in the space between, never through a card, and with as few crossings as a small
/// search can find.
/// <para>
/// All at once, because an edge's place depends on the others. It replaced a per-edge route that
/// took one fan index per card and pushed the whole polyline by it. On the shipped vessel that put
/// Resource Storage's ten routes down its sides 0, 6, 12… 54 pixels below the midline, in content
/// order rather than in the direction each one was going, so they crossed one another right beside
/// the card, and an elbow took the first gap clear of cards, however many lines it then crossed.
/// Factory Alpha's line to Reactor Beta went under the storage across five of them.
/// </para>
/// <para>
/// Three steps. <b>Ports</b>: the endpoints on one side of a card are spread along it in the order
/// of where their far ends lie, so an edge heading up leaves above one heading down, and the
/// spread is centred on any edge whose far card is level with this one, which keeps a same-row
/// route straight. <b>Legs</b>: an elbow's vertical leg goes at the midpoint between the cards or in
/// a gutter between columns, wherever it crosses no card and the fewest other edges, two passes
/// over all edges. Every choice is the same length, since an orthogonal elbow's length does not
/// depend on where it turns. <b>Lanes</b>: legs that land on one x are spread side by side, in
/// whichever order crosses least.
/// </para>
/// <para>
/// Deterministic, integer, and dependent only on the edges in the order given and the cards. It
/// knows nothing about what the edges carry, so it is worked out once per layout, never per tick.
/// </para>
/// </summary>
public static class GraphRouting
{
    /// <summary>Between neighbouring endpoints on one side of a card: clear of an arrowhead's width.</summary>
    public const int PortSpacing = 10;

    /// <summary>Between neighbouring vertical legs that share an x.</summary>
    public const int LaneSpacing = 10;

    /// <summary>How close to a card's corner a port may sit before the spread is compressed.</summary>
    private const int PortMargin = 12;

    /// <summary>How close to a gutter's edge a lane may sit before the spread is compressed.</summary>
    private const int LaneMargin = 8;

    /// <summary>
    /// The largest lane whose every ordering is tried. Six is 720 orderings, which is nothing once
    /// per layout; above it the lane keeps edge order, and the shipped vessel has none so wide.
    /// </summary>
    private const int MaxPermutedLane = 6;

    /// <summary>How many times every elbow is offered its best leg against the others' current ones.</summary>
    private const int LegPasses = 2;

    /// <summary>An overlap runs two lines into one, which is worse than any single crossing.</summary>
    private const int OverlapWeight = 2;

    private enum Side
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>
    /// One polyline per edge, in the order given. An edge whose two ends are one card collapses to
    /// a single point drawn twice: a self-route is a definition error, and a stray backwards line
    /// would be a worse answer than nothing.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(int X, int Y)>> Route(
        IReadOnlyList<(Rect From, Rect To)> edges,
        IReadOnlyCollection<Rect>? cards = null)
    {
        var count = edges.Count;
        var ports = Ports(edges);
        var routes = new (int X, int Y)[count][];
        var elbows = new Elbow?[count];

        for (var i = 0; i < count; i++)
        {
            var (from, to) = edges[i];
            var obstacles = (cards ?? Array.Empty<Rect>()).Append(from).Append(to).ToList();

            if (from == to)
            {
                var mid = (from.X + (from.W / 2), from.Y + (from.H / 2));
                routes[i] = new[] { mid, mid };
            }
            else if (from.X == to.X)
            {
                routes[i] = Vertical(from, to, ports[i]);
            }
            else if (Horizontal(from, to, ports[i], obstacles) is { } elbow)
            {
                elbows[i] = elbow;
                routes[i] = elbow.At(elbow.Candidates[0]);
            }
            else
            {
                routes[i] = Straight(from, to, ports[i]);
            }
        }

        ChooseLegs(elbows, routes);
        SpreadLanes(elbows, routes, cards ?? Array.Empty<Rect>());

        return routes;
    }

    /// <summary>
    /// How many times two polylines cross, an overlapping run counting <see cref="OverlapWeight"/>.
    /// A touch counts as a crossing: two lines meeting at a point read as a junction, and nothing on
    /// this graph is one.
    /// </summary>
    public static int Crossings(IReadOnlyList<(int X, int Y)> a, IReadOnlyList<(int X, int Y)> b)
    {
        var total = 0;

        for (var i = 1; i < a.Count; i++)
        {
            for (var j = 1; j < b.Count; j++)
            {
                total += SegmentCrossings(a[i - 1], a[i], b[j - 1], b[j]);
            }
        }

        return total;
    }

    /// <summary>
    /// The offset along its side of each edge's two endpoints, from that side's midpoint, centred
    /// on the endpoints whose far end is level with this card, or on the whole group if none is.
    /// </summary>
    private static (int From, int To)[] Ports(IReadOnlyList<(Rect From, Rect To)> edges)
    {
        var result = new (int From, int To)[edges.Count];
        var groups = new Dictionary<(Rect Card, Side Side), List<(int Edge, bool AtFrom, int Far, int Reach)>>();
        var keys = new List<(Rect Card, Side Side)>();

        void Add(Rect card, Side side, int edge, bool atFrom, Rect far, int reach)
        {
            if (!groups.TryGetValue((card, side), out var members))
            {
                members = new List<(int Edge, bool AtFrom, int Far, int Reach)>();
                groups[(card, side)] = members;
                keys.Add((card, side));
            }

            members.Add((edge, atFrom, Along(side, far), reach));
        }

        for (var i = 0; i < edges.Count; i++)
        {
            var (from, to) = edges[i];
            if (from == to)
            {
                continue;
            }

            // How far apart the two facing sides are: where an elbow can turn at the farthest.
            var reach = from.X == to.X ? 0 : Math.Abs(HorizontalSides(from, to).Arrive - HorizontalSides(from, to).Leave);
            var (leave, arrive) = Sides(from, to);
            Add(from, leave, i, atFrom: true, to, reach);
            Add(to, arrive, i, atFrom: false, from, reach);
        }

        foreach (var key in keys)
        {
            var (card, side) = key;
            var own = Along(side, card);

            // Edges heading towards the side's start (up, or left), then level ones, then those
            // heading towards its end, so a fan never crosses itself at the card. Within a heading
            // they nest: the one reaching farthest across stands farthest from where they turn, so
            // every nearer turn happens inside it rather than across it. That, and not the far
            // end's height alone, is what keeps Factory Alpha's line to the storage, which turns
            // early, from cutting across the two treatment lines that run on past it.
            var sorted = groups[key]
                .OrderBy(member => Math.Sign(member.Far - own))
                .ThenBy(member => member.Far < own ? member.Reach : member.Far > own ? -member.Reach : 0)
                .ThenBy(member => member.Far)
                .ThenBy(member => member.Edge)
                .ToList();

            var level = sorted.FindIndex(member => member.Far == own);
            var centre = level < 0
                ? sorted.Count - 1
                : level + sorted.FindLastIndex(member => member.Far == own);

            var offsets = sorted.Select((_, k) => ((2 * k) - centre) * PortSpacing / 2).ToList();

            // Compressed rather than allowed off the side: a port past the corner would draw its
            // line out of the card's edge where nothing is.
            var room = ((side is Side.Left or Side.Right ? card.H : card.W) / 2) - PortMargin;
            var widest = offsets.Count == 0 ? 0 : offsets.Max(Math.Abs);
            if (widest > room)
            {
                offsets = offsets.Select(offset => offset * room / widest).ToList();
            }

            for (var k = 0; k < sorted.Count; k++)
            {
                var (edge, atFrom, _, _) = sorted[k];
                result[edge] = atFrom ? (offsets[k], result[edge].To) : (result[edge].From, offsets[k]);
            }
        }

        return result;
    }

    /// <summary>
    /// Two cards in one column: leave the side facing the other, and arrive at its facing side. A
    /// jog halfway when the two ports differ, which only a crowded side causes.
    /// </summary>
    private static (int X, int Y)[] Vertical(Rect from, Rect to, (int From, int To) ports)
    {
        var (leaveY, arriveY) = to.Y > from.Y ? (from.Y + from.H, to.Y) : (from.Y, to.Y + to.H);
        var midY = (leaveY + arriveY) / 2;
        var x1 = from.X + (from.W / 2) + ports.From;
        var x2 = to.X + (to.W / 2) + ports.To;

        return x1 == x2
            ? new[] { (x1, leaveY), (x1, midY), (x1, arriveY) }
            : new[] { (x1, leaveY), (x1, midY), (x2, midY), (x2, arriveY) };
    }

    /// <summary>A level route between facing sides, with a midpoint vertex for its label.</summary>
    private static (int X, int Y)[] Straight(Rect from, Rect to, (int From, int To) ports)
    {
        var (leaveX, arriveX) = HorizontalSides(from, to);
        var y = from.Y + (from.H / 2) + ports.From;

        return new[] { (leaveX, y), ((leaveX + arriveX) / 2, y), (arriveX, y) };
    }

    /// <summary>
    /// Two cards in different columns: an elbow, unless the two ports are level, when it is
    /// <see cref="Straight"/> and this answers null. Straight routes are not rerouted, because
    /// there is nowhere else for them to go.
    /// <para>
    /// The candidate legs are the midpoint, then every gutter between the two sides, nearest the
    /// midpoint first and nearer the target on a tie, keeping only those that cross no card. The
    /// two cards an edge joins are obstacles too: its legs end on their sides, which does not count
    /// as crossing, but its elbow must not stand inside either. With nothing clear, the midpoint.
    /// </para>
    /// </summary>
    private static Elbow? Horizontal(Rect from, Rect to, (int From, int To) ports, IReadOnlyList<Rect> obstacles)
    {
        var (leaveX, arriveX) = HorizontalSides(from, to);
        var y1 = from.Y + (from.H / 2) + ports.From;
        var y2 = to.Y + (to.H / 2) + ports.To;

        if (y1 == y2)
        {
            return null;
        }

        var midX = (leaveX + arriveX) / 2;
        var lo = Math.Min(leaveX, arriveX);
        var hi = Math.Max(leaveX, arriveX);
        var stride = GraphGeometry.CellWidth + GraphGeometry.GutterX;

        var gutters = Enumerable.Range(0, (hi / stride) + 1)
            .Select(column => (column * stride) + GraphGeometry.CellWidth + (GraphGeometry.GutterX / 2))
            .Where(x => x > lo && x < hi && x != midX)
            .OrderBy(x => Math.Abs(x - midX))
            .ThenBy(x => Math.Abs(x - arriveX));

        var elbow = new Elbow(leaveX, y1, arriveX, y2, Array.Empty<int>());
        var clear = gutters.Prepend(midX)
            .Where(x => !obstacles.Any(card => GraphGeometry.Crosses(elbow.At(x), card)))
            .ToArray();

        var candidates = clear.Length > 0 ? clear : new[] { midX };
        return elbow with { Candidates = candidates, Leg = candidates[0] };
    }

    /// <summary>
    /// Offers each elbow, in edge order, the candidate leg that crosses the fewest other edges as
    /// they stand, keeping the earlier candidate on a tie. A second pass lets an edge answer the
    /// choices made after it.
    /// </summary>
    private static void ChooseLegs(Elbow?[] elbows, (int X, int Y)[][] routes)
    {
        for (var pass = 0; pass < LegPasses; pass++)
        {
            for (var i = 0; i < elbows.Length; i++)
            {
                if (elbows[i] is not { } elbow)
                {
                    continue;
                }

                var best = elbow.Candidates[0];
                var bestScore = int.MaxValue;

                foreach (var leg in elbow.Candidates)
                {
                    var route = elbow.At(leg);
                    var score = 0;
                    for (var j = 0; j < routes.Length; j++)
                    {
                        // An edge whose leg is on this x will share a lane with this one, and the
                        // lanes are ordered afterwards to cross as little as they can. Scored here,
                        // the two would read as one overlap, and two lines that belong side by side
                        // in one gutter would each be sent to a worse one apart.
                        if (j != i && elbows[j]?.Leg != leg)
                        {
                            score += Crossings(route, routes[j]);
                        }
                    }

                    if (score < bestScore)
                    {
                        (best, bestScore) = (leg, score);
                    }
                }

                elbows[i] = elbow with { Leg = best };
                routes[i] = elbow.At(best);
            }
        }
    }

    /// <summary>
    /// Spreads the legs that share an x into side-by-side lanes, centred on it, trying every order
    /// of a lane up to <see cref="MaxPermutedLane"/> and keeping the one with the fewest crossings
    /// that runs through no card. Lanes are compressed to fit a gutter, since that is where most
    /// shared legs stand.
    /// </summary>
    private static void SpreadLanes(Elbow?[] elbows, (int X, int Y)[][] routes, IReadOnlyCollection<Rect> cards)
    {
        var lanes = Enumerable.Range(0, elbows.Length)
            .Where(i => elbows[i] is not null)
            .GroupBy(i => elbows[i]!.Leg)
            .Where(lane => lane.Count() > 1)
            .OrderBy(lane => lane.Key)
            .Select(lane => lane.ToArray())
            .ToList();

        foreach (var members in lanes)
        {
            var size = members.Length;
            var spacing = Math.Min(LaneSpacing, (GraphGeometry.GutterX - (2 * LaneMargin)) / (size - 1));
            var slots = Enumerable.Range(0, size).Select(k => ((2 * k) - (size - 1)) * spacing / 2).ToArray();
            var inLane = members.ToHashSet();

            (int X, int Y)[][] Place(int[] order) =>
                order.Select((edge, slot) => elbows[edge]!.At(elbows[edge]!.Leg + slots[slot])).ToArray();

            int Score((int X, int Y)[][] placed)
            {
                if (placed.Any(route => cards.Any(card => GraphGeometry.Crosses(route, card))))
                {
                    return int.MaxValue;
                }

                var score = 0;
                for (var a = 0; a < placed.Length; a++)
                {
                    for (var j = 0; j < routes.Length; j++)
                    {
                        if (!inLane.Contains(j))
                        {
                            score += Crossings(placed[a], routes[j]);
                        }
                    }

                    for (var b = a + 1; b < placed.Length; b++)
                    {
                        score += Crossings(placed[a], placed[b]);
                    }
                }

                return score;
            }

            var orders = size <= MaxPermutedLane ? Permutations(members) : new[] { members };
            var best = members;
            var bestScore = int.MaxValue;

            foreach (var order in orders)
            {
                var score = Score(Place(order));
                if (score < bestScore)
                {
                    (best, bestScore) = (order, score);
                }
            }

            // Every order runs through a card: the lane stays unspread, which is no worse than the
            // legs it was given, rather than spread through something.
            if (bestScore == int.MaxValue)
            {
                continue;
            }

            var final = Place(best);
            for (var slot = 0; slot < best.Length; slot++)
            {
                routes[best[slot]] = final[slot];
            }
        }
    }

    /// <summary>Every order of the items, the given order first, then lexicographically by position.</summary>
    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, k) => k != i).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return tail.Prepend(items[i]).ToArray();
            }
        }
    }

    private static int SegmentCrossings((int X, int Y) a1, (int X, int Y) a2, (int X, int Y) b1, (int X, int Y) b2)
    {
        var aLevel = a1.Y == a2.Y;
        var bLevel = b1.Y == b2.Y;

        if (a1 == a2 || b1 == b2)
        {
            return 0;
        }

        if (aLevel != bLevel)
        {
            var (level1, level2, upright1, upright2) = aLevel ? (a1, a2, b1, b2) : (b1, b2, a1, a2);
            var x = upright1.X;
            var y = level1.Y;
            var inLevel = x >= Math.Min(level1.X, level2.X) && x <= Math.Max(level1.X, level2.X);
            var inUpright = y >= Math.Min(upright1.Y, upright2.Y) && y <= Math.Max(upright1.Y, upright2.Y);
            return inLevel && inUpright ? 1 : 0;
        }

        if (aLevel)
        {
            if (a1.Y != b1.Y)
            {
                return 0;
            }

            var overlap = Math.Min(Math.Max(a1.X, a2.X), Math.Max(b1.X, b2.X)) -
                          Math.Max(Math.Min(a1.X, a2.X), Math.Min(b1.X, b2.X));
            return overlap > 0 ? OverlapWeight : 0;
        }

        if (a1.X != b1.X)
        {
            return 0;
        }

        var run = Math.Min(Math.Max(a1.Y, a2.Y), Math.Max(b1.Y, b2.Y)) -
                  Math.Max(Math.Min(a1.Y, a2.Y), Math.Min(b1.Y, b2.Y));
        return run > 0 ? OverlapWeight : 0;
    }

    /// <summary>Which sides two cards present to each other: top and bottom within a column, else left and right.</summary>
    private static (Side Leave, Side Arrive) Sides(Rect from, Rect to) =>
        from.X == to.X
            ? to.Y > from.Y ? (Side.Bottom, Side.Top) : (Side.Top, Side.Bottom)
            : to.X > from.X ? (Side.Right, Side.Left) : (Side.Left, Side.Right);

    /// <summary>A card's midpoint along a side's own axis: y for a left or right side, x for top or bottom.</summary>
    private static int Along(Side side, Rect card) =>
        side is Side.Left or Side.Right ? card.Y + (card.H / 2) : card.X + (card.W / 2);

    private static (int Leave, int Arrive) HorizontalSides(Rect from, Rect to) =>
        to.X > from.X ? (from.X + from.W, to.X) : (from.X, to.X + to.W);

    /// <summary>An elbow between two ports, and the legs it may turn at.</summary>
    private sealed record Elbow(int LeaveX, int Y1, int ArriveX, int Y2, int[] Candidates)
    {
        public int Leg { get; init; }

        public (int X, int Y)[] At(int leg) => new[] { (LeaveX, Y1), (leg, Y1), (leg, Y2), (ArriveX, Y2) };
    }
}
