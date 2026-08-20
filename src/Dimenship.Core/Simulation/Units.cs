using System.Globalization;

namespace Dimenship.Core.Simulation;

/// <summary>Display formatting for kernel quantities. Presentation only; no simulation logic.</summary>
public static class Units
{
    /// <summary>One tick is one simulated second. Callers that want to advance a wall-clock
    /// duration must scale by these rather than guessing, which is how a debug menu item once
    /// came to advance a minute under a label that said an hour.</summary>
    public const long TicksPerMinute = 60;

    /// <inheritdoc cref="TicksPerMinute"/>
    public const long TicksPerHour = 60 * TicksPerMinute;

    public static string Format(long milliUnits) =>
        (milliUnits / 1000m).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// A permille as the percentage a player reads. Floored, like every bar in the shell: a hold
    /// that is not full must not read 100%, and 999 permille of a hold is still somewhere to put
    /// something.
    /// </summary>
    public static string FormatPermille(long permille) =>
        (permille / 10).ToString(CultureInfo.InvariantCulture) + "%";

    public static string FormatSimTime(long ticks)
    {
        var hours = ticks / TicksPerHour;
        var minutes = ticks / TicksPerMinute % 60;
        var seconds = ticks % TicksPerMinute;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"T+{hours:00}:{minutes:00}:{seconds:00}");
    }
}
