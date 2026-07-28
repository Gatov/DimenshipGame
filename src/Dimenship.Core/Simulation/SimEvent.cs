namespace Dimenship.Core.Simulation;

/// <summary>
/// A structured telemetry event. Deliberately not a formatted string — the console panel
/// formats it, and a category filter is only possible because the fields stay separate.
/// </summary>
public sealed record SimEvent(
    long Tick,
    EventCategory Category,
    EventCode Code,
    string Subject,
    IReadOnlyDictionary<string, long> Data)
{
    public static readonly IReadOnlyDictionary<string, long> NoData = new Dictionary<string, long>();
}
