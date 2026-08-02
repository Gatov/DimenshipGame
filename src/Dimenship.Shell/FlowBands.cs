namespace Dimenship.Shell;

/// <summary>How hard a transport line is working, as five readable steps rather than a number.</summary>
public enum FlowBand
{
    /// <summary>Nothing moved this tick.</summary>
    Idle,

    /// <summary>Moving, but well under capacity.</summary>
    Low,

    /// <summary>A healthy share of throughput.</summary>
    Normal,

    /// <summary>At or near saturation. The line is a candidate bottleneck.</summary>
    High,

    /// <summary>Every queued transfer was postponed. Load says nothing while this is true.</summary>
    Blocked,
}

/// <summary>
/// Turns a line's last tick into a band. Integer arithmetic throughout, because a band is
/// compared against fixed boundaries and a float would put the boundaries themselves in doubt.
/// </summary>
public static class FlowBands
{
    private const long LowFloor = 1;
    private const long NormalFloor = 330;
    private const long HighFloor = 800;

    /// <summary>
    /// Classifies a line by what it moved against what it could have moved. <paramref name="blocked"/>
    /// wins over every load reading: a line that moved its full throughput and then blocked is
    /// blocked, and the load behind it is stale. A line that cannot carry anything is
    /// <see cref="FlowBand.Idle"/> rather than a division by zero.
    /// </summary>
    public static FlowBand Classify(long movedLastTick, long throughputPerTick, bool blocked)
    {
        if (blocked)
        {
            return FlowBand.Blocked;
        }

        if (throughputPerTick <= 0 || movedLastTick <= 0)
        {
            return FlowBand.Idle;
        }

        // Saturated or beyond, answered without the multiply so a large reading cannot overflow.
        if (movedLastTick >= throughputPerTick)
        {
            return FlowBand.High;
        }

        var permille = movedLastTick * 1000 / throughputPerTick;

        return permille switch
        {
            >= HighFloor => FlowBand.High,
            >= NormalFloor => FlowBand.Normal,
            >= LowFloor => FlowBand.Low,
            _ => FlowBand.Idle,
        };
    }
}
