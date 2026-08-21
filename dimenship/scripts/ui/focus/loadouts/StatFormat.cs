using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// How the six stats are named and written, in one place. The rollup grid, the socket rows and the
/// palette all print the same numbers, and three call sites deciding independently what a zero
/// looks like is how a readout stops being one thing.
/// </summary>
public static class StatFormat
{
    /// <summary>
    /// The columns, in the order the composer shows them, each with the accessor that reads it.
    /// Declaration order is the display order — the same rule the content follows.
    /// </summary>
    public static readonly IReadOnlyList<(string Header, Func<StatBlock, long> Read)> Columns =
        new (string, Func<StatBlock, long>)[]
        {
            ("MASS", stats => stats.Mass),
            ("POWER", stats => stats.Power),
            ("DURABILITY", stats => stats.Durability),
            ("CARGO", stats => stats.Cargo),
            ("SCAN", stats => stats.Scan),
            ("WORK", stats => stats.WorkRate),
        };

    /// <summary>
    /// A contribution: signed, and an em dash when it is zero. A column of zeroes reads as noise,
    /// and the question the attribution answers is which parts touch this stat at all.
    /// </summary>
    public static string Signed(long value) => value switch
    {
        0 => "—",
        > 0 => $"+{value}",
        _ => value.ToString(),
    };

    /// <summary>A total. Unsigned, because a total is a reading rather than a change.</summary>
    public static string Plain(long value) => value.ToString();

    /// <summary>
    /// What a fitted part contributes, on one line, naming only the stats it touches. The socket
    /// row has one line to say it in, so a fixed six-column layout would spend most of that line
    /// on dashes.
    /// </summary>
    public static string Summary(StatBlock stats)
    {
        var parts = Columns
            .Select(column => (column.Header, Value: column.Read(stats)))
            .Where(entry => entry.Value != 0)
            .Select(entry => $"{entry.Header} {Signed(entry.Value)}");

        var line = string.Join("   ", parts);

        return line.Length > 0 ? line : "NO EFFECT";
    }
}

/// <summary>
/// How a verdict is written and tinted. One place, because it appears on the header chip, in the
/// library row and in the info box, and three call sites deciding independently what
/// <see cref="Verdict.OverBudget"/> is called is how a status stops being one status.
/// <para>
/// The word carries the state; the colour only agrees with it. Nothing here is told by colour
/// alone.
/// </para>
/// </summary>
public static class VerdictText
{
    public static string Of(Verdict verdict) => verdict switch
    {
        Verdict.Complete => "COMPLETE",
        Verdict.Incomplete => "INCOMPLETE",
        Verdict.OverBudget => "OVER BUDGET",
        _ => "UNKNOWN",
    };

    public static Color Colour(Verdict verdict) => verdict switch
    {
        Verdict.Complete => ShellPalette.StateOk,
        Verdict.Incomplete => ShellPalette.TextDim,
        Verdict.OverBudget => ShellPalette.StateFault,
        _ => ShellPalette.TextDim,
    };
}
