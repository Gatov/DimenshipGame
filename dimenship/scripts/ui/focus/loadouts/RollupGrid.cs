using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The stat readout: one row per contributor, one column per stat, and the totals underneath.
/// <para>
/// Attribution rather than a totals column, because the question a composer has to make answerable
/// is <i>which fitting is costing me this</i>. With only totals, the player answers it by pulling
/// fittings out one at a time and watching the numbers move.
/// </para>
/// <para>
/// The frame's own row is always first and is never empty: it carries GDD §5.10's unequipped
/// rating, which is what an empty socket leaves the machine running at.
/// </para>
/// </summary>
public sealed partial class RollupGrid : VBoxContainer
{
    private GridContainer _grid = null!;
    private Label _note = null!;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        AddChild(BoxSection.Create("Stat Rollup", out var body));

        _grid = new GridContainer { Columns = StatFormat.Columns.Count + 1 };
        _grid.AddThemeConstantOverride("h_separation", ShellPalette.SpaceLg);
        _grid.AddThemeConstantOverride("v_separation", ShellPalette.SpaceSm);
        body.AddChild(_grid);

        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _note.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        _note.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        body.AddChild(_note);
    }

    public void Refresh(Rollup rollup)
    {
        if (!IsNodeReady())
        {
            return;
        }

        foreach (var child in _grid.GetChildren())
        {
            child.QueueFree();
        }

        Header();

        foreach (var contribution in rollup.Contributions)
        {
            var source = contribution.SocketIndex is { } socket
                ? $"{socket + 1}. {contribution.Source}"
                : contribution.Source;

            Row(source, contribution.Stats, total: false);
        }

        Row("TOTAL", rollup.Totals, total: true);

        _note.Text = rollup.EmptySockets switch
        {
            0 when rollup.Totals.Power < 0 =>
                "The fittings draw more than the fitted core supplies. Nothing here is built, so "
                + "nothing is stopped by it.",
            0 => string.Empty,
            1 => "One socket is empty; the frame's baseline is what it runs at.",
            _ => $"{rollup.EmptySockets} sockets are empty; the frame's baseline is what they run at.",
        };
    }

    private void Header()
    {
        _grid.AddChild(Cell("SOURCE", ShellPalette.TextDim, ShellPalette.FontMicro, right: false));

        foreach (var column in StatFormat.Columns)
        {
            _grid.AddChild(Cell(column.Header, ShellPalette.TextDim, ShellPalette.FontMicro));
        }
    }

    private void Row(string source, StatBlock stats, bool total)
    {
        var colour = total ? ShellPalette.TextTitle : ShellPalette.TextPrimary;
        var size = total ? ShellPalette.FontBody : ShellPalette.FontMicro;

        _grid.AddChild(Cell(source, colour, size, right: false));

        foreach (var column in StatFormat.Columns)
        {
            var value = column.Read(stats);

            // The only cell that carries a colour is a negative power total, and it carries a
            // minus sign as well: the verdict chip says OVER BUDGET in words, so nothing here is
            // state told by colour alone.
            var cellColour = total && column.Header == "POWER" && value < 0
                ? ShellPalette.StateFault
                : colour;

            _grid.AddChild(Cell(
                total ? StatFormat.Plain(value) : StatFormat.Signed(value), cellColour, size));
        }
    }

    private static Label Cell(string text, Color colour, int size, bool right = true)
    {
        var cell = new Label
        {
            Text = text,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            SizeFlagsHorizontal = right ? SizeFlags.ExpandFill : SizeFlags.Fill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        cell.AddThemeColorOverride("font_color", colour);
        cell.AddThemeFontSizeOverride("font_size", size);

        return cell;
    }
}
