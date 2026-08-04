using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// The default world's placements are content, and content errors are found by looking at the
/// screen unless something asserts on them. This is that something.
/// </summary>
public class BaseGraphLayoutTests
{
    private static readonly WorldDefinition World = WorldDefinition.CreateDefault();
    private static readonly BaseGraphLayout Layout = BaseGraphLayout.ForDefaultWorld();

    /// <summary>Every placed node, producers and storages together — the graph as drawn.</summary>
    private static IReadOnlyList<NodePlacement> Placements =>
        Layout.Producers.Values.Concat(Layout.Storages.Values).ToList();

    [Test]
    public void EveryProductionExecutor_IsPlaced()
    {
        foreach (var producer in World.Producers)
        {
            Assert.That(
                Layout.Producers.ContainsKey(producer.Id), Is.True,
                $"'{producer.Id}' would be drawn in the unplaced strip instead of the graph");
        }
    }

    [Test]
    public void EveryStorage_IsPlaced()
    {
        foreach (var storage in World.Storages)
        {
            Assert.That(
                Layout.Storages.ContainsKey(storage.Id), Is.True,
                $"'{storage.Id}' would be drawn in the unplaced strip instead of the graph");
        }
    }

    [Test]
    public void NoTransportLine_IsPlaced()
    {
        foreach (var transport in World.Transports)
        {
            Assert.That(
                Layout.Producers.ContainsKey(transport.Id), Is.False,
                $"'{transport.Id}' is an edge; a card for it would draw the same line twice");
        }
    }

    [Test]
    public void NoTwoNodes_ShareACell()
    {
        // On the cell, not on the whole placement: a placement carries a badge as well, and two
        // nodes in one cell with different badges are unequal records and identical positions.
        var cells = Placements.Select(p => (p.Column, p.Row)).ToList();

        Assert.That(
            cells.Distinct().Count(), Is.EqualTo(cells.Count),
            "two nodes in one cell would overprint, and only one of them would be readable");
    }

    [Test]
    public void EveryNode_HasABadge()
    {
        foreach (var placement in Placements)
        {
            Assert.That(
                string.IsNullOrWhiteSpace(placement.Badge), Is.False,
                "a card with no badge draws an empty chip over its own border");
        }
    }

    [Test]
    public void NoTwoNodes_ShareABadge()
    {
        var badges = Placements.Select(p => p.Badge).ToList();

        Assert.That(
            badges.Distinct().Count(), Is.EqualTo(badges.Count),
            "a badge is how a player names a node out loud; two nodes answering to one is a lie");
    }

    [Test]
    public void EveryRouteEndpoint_IsAPlacedStorage()
    {
        foreach (var transport in World.Transports)
        {
            Assert.That(
                Layout.Storages.ContainsKey(transport.From), Is.True,
                $"'{transport.Id}' would leave from nowhere");
            Assert.That(
                Layout.Storages.ContainsKey(transport.To), Is.True,
                $"'{transport.Id}' would arrive nowhere");
        }
    }

    [Test]
    public void NothingIsPlaced_ThatTheWorldDoesNotContain()
    {
        // The reverse of the two assertions above. A placement for a node that was renamed or
        // removed is dead content, and dead content is how a layout drifts out of date.
        foreach (var placed in Layout.Producers.Keys)
        {
            Assert.That(World.Producers.Any(p => p.Id == placed), Is.True, $"'{placed}' is a ghost");
        }

        foreach (var placed in Layout.Storages.Keys)
        {
            Assert.That(World.Storages.Any(s => s.Id == placed), Is.True, $"'{placed}' is a ghost");
        }
    }
}
