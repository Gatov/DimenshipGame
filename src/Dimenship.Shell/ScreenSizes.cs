namespace Dimenship.Shell;

/// <summary>
/// The window sizes the settings menu offers, as arithmetic rather than as a list read off the
/// display. Filtering lives here, engine-free, so the rule that a player is never offered a window
/// larger than the screen it has to fit on can be tested without a display attached.
/// </summary>
public static class ScreenSizes
{
    /// <summary>
    /// The offered ladder. Every entry is 16:9 except 1366x768, which is 16:9 to within a pixel
    /// and is on more laptop panels than any true 16:9 size below 1080p. The console is laid out
    /// against 1920x1080 and stretches, so this is a convenience list, not a set of supported
    /// modes — anything outside it still works, it is simply not offered.
    /// </summary>
    public static readonly IReadOnlyList<ScreenSize> Ladder = new[]
    {
        new ScreenSize(1280, 720),
        new ScreenSize(1366, 768),
        new ScreenSize(1600, 900),
        new ScreenSize(1920, 1080),
        new ScreenSize(2560, 1440),
        new ScreenSize(3840, 2160),
    };

    /// <summary>
    /// The ladder entries that fit inside <paramref name="screen"/>, ascending, with
    /// <paramref name="current"/> inserted if it is not already among them.
    /// <para>
    /// Current is kept even when it does not fit: a settings file carried from a larger monitor,
    /// or a window the player resized by dragging, must still appear as the selected item.
    /// A dropdown whose current value is missing has no honest thing to display.
    /// </para>
    /// <para>
    /// The smallest ladder entry survives a screen too small for anything, so the list is never
    /// empty and the caller never has to handle a dropdown with no options.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ScreenSize> Offered(ScreenSize screen, ScreenSize current)
    {
        var offered = Ladder
            .Where(size => size.Width <= screen.Width && size.Height <= screen.Height)
            .ToList();

        if (offered.Count == 0)
        {
            offered.Add(Ladder[0]);
        }

        if (!offered.Contains(current))
        {
            offered.Add(current);
        }

        offered.Sort(static (a, b) =>
        {
            var byWidth = a.Width.CompareTo(b.Width);
            return byWidth != 0 ? byWidth : a.Height.CompareTo(b.Height);
        });

        return offered;
    }
}
