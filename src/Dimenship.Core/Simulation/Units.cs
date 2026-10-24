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

    /// <summary>
    /// One whole unit of an item, in the milli-units every quantity is counted in. Named for the
    /// reason the tick constants are: the shell once spelled it as four private constants under
    /// four names, two of them in the same partial class, beside a bare <c>1000</c>.
    /// </summary>
    public const long MilliPerUnit = 1000;

    /// <summary>
    /// A milli-unit quantity as whole units with three decimals, worked out as a quotient and a
    /// remainder. It once divided as a <c>decimal</c>, which passed the no-float guard and was the
    /// one non-integer arithmetic left in the kernel (R7). The sign is written once, from the
    /// quantity, because both parts of a negative one are negative and a quotient of zero has no
    /// sign of its own. Negating the parts, never the quantity, is what keeps
    /// <see cref="long.MinValue"/> from overflowing.
    /// </summary>
    public static string Format(long milliUnits)
    {
        var whole = milliUnits / MilliPerUnit;
        var fraction = milliUnits % MilliPerUnit;
        return milliUnits < 0
            ? string.Create(CultureInfo.InvariantCulture, $"-{-whole}.{-fraction:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{whole}.{fraction:000}");
    }

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
