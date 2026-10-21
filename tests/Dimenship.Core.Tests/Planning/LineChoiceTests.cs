using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Planning;

/// <summary>
/// The one rule the planner and <c>MoveWork</c> both route legs by: least load, then highest
/// throughput, then declaration order.
/// </summary>
public class LineChoiceTests
{
    private static readonly StorageId Hold = new("hold");
    private static readonly StorageId Buffer = new("buffer");
    private static readonly IReadOnlyDictionary<ExecutorId, long> NothingPlaced = new Dictionary<ExecutorId, long>();

    private static PlannerTransport Line(string id, long queued, long throughput, StorageId? from = null, StorageId? to = null) =>
        new(new ExecutorId(id), from ?? Hold, to ?? Buffer, queued, throughput, 1);

    [Test]
    public void TheLeastLoadedLineWins_WhateverItsThroughput()
    {
        var lines = new[] { Line("fast_busy", 2, 50), Line("slow_idle", 0, 7) };

        Assert.That(LineChoice.Best(lines, Hold, Buffer, NothingPlaced)!.Id.Value, Is.EqualTo("slow_idle"));
    }

    [Test]
    public void AtEqualLoad_TheFasterLineWins_EvenDeclaredSecond()
    {
        // The Factory Beta case: two feeds at 7 and 50 a tick, and declaration order alone sent a
        // whole construction unit down the slow one.
        var lines = new[] { Line("slow", 0, 7), Line("fast", 0, 50) };

        Assert.That(LineChoice.Best(lines, Hold, Buffer, NothingPlaced)!.Id.Value, Is.EqualTo("fast"));
    }

    [Test]
    public void AtEqualLoadAndThroughput_TheFirstDeclaredWins()
    {
        var lines = new[] { Line("first", 0, 10), Line("second", 0, 10) };

        Assert.That(LineChoice.Best(lines, Hold, Buffer, NothingPlaced)!.Id.Value, Is.EqualTo("first"));
    }

    [Test]
    public void WhatTheCallerAlreadyPlaced_CountsAsLoad()
    {
        var lines = new[] { Line("a", 0, 10), Line("b", 0, 10) };
        var placed = new Dictionary<ExecutorId, long> { [new ExecutorId("a")] = 1 };

        Assert.That(LineChoice.Best(lines, Hold, Buffer, placed)!.Id.Value, Is.EqualTo("b"));
    }

    [Test]
    public void OnlyALineOnTheRouteIsChosen_AndNoneIsNull()
    {
        var lines = new[] { Line("back", 0, 99, from: Buffer, to: Hold) };

        Assert.That(LineChoice.Best(lines, Hold, Buffer, NothingPlaced), Is.Null);
        Assert.That(LineChoice.Runs(lines, new ExecutorId("back"), Buffer, Hold), Is.True);
        Assert.That(LineChoice.Runs(lines, new ExecutorId("back"), Hold, Buffer), Is.False);
    }
}
