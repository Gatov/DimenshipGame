using System.Globalization;

namespace Dimenship.Core.Simulation;

/// <summary>Display formatting for kernel quantities. Presentation only; no simulation logic.</summary>
public static class Units
{
    public static string Format(long milliUnits) =>
        (milliUnits / 1000m).ToString("0.000", CultureInfo.InvariantCulture);

    public static string FormatSimTime(long ticks)
    {
        var hours = ticks / 3600;
        var minutes = ticks / 60 % 60;
        var seconds = ticks % 60;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"T+{hours:00}:{minutes:00}:{seconds:00}");
    }
}
