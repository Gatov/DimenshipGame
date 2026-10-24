namespace Dimenship.Shell;

/// <summary>
/// Small numbers the shell draws from, worked out once. Before this, each meter worked out its own
/// share of a track: the base graph's cards, the inspector and the energy budget held four copies
/// of one clamp, two of which tested the denominator as <c>== 0</c> and two as <c>&lt;= 0</c>.
/// </summary>
public static class ShellMath
{
    /// <summary>
    /// How much of a bar <paramref name="amount"/> fills out of <paramref name="capacity"/>, from 0
    /// to 1. An empty bar rather than a division whenever there is no denominator. Clamped rather
    /// than trusted: a scenario may seed a storage over its own capacity, and a bar drawn past its
    /// track would be a rendering fault on screen. A storage's fill, already a permille of one
    /// shared volume, is passed with the full-hold permille as its capacity.
    /// </summary>
    public static float Fill(long amount, long capacity) =>
        capacity <= 0 ? 0f : Math.Clamp((float)((double)amount / capacity), 0f, 1f);
}
