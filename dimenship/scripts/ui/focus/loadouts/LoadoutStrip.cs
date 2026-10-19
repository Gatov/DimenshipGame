using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The strip under the stage: POWER BUDGET, STATS, BUILD COST, and a DETAILS toggle. It shows the
/// template as it stands and, while a picker row is previewed, what that candidate would change —
/// hatched on the bar, and as <c>+n</c> / <c>−n</c> beside each stat and cost figure it moves.
/// <para>
/// Every state is said in words as well as colour: an over-budget template reads
/// <c>OVER BUDGET BY 120</c>, a template with no core reads <c>NO POWER SOURCE</c>, and a cost
/// the vessel cannot cover reads <c>SHORT</c>.
/// </para>
/// <para>
/// The per-fitting attribution the old stat table showed is not lost: DETAILS opens it, unchanged,
/// in a drawer over the stage.
/// </para>
/// <para>
/// Its width never depends on what it says. The strip sits under the composer, so a minimum width
/// that grew with a reading — a fourth digit, a preview delta appearing, a cost line added — pushed
/// the split, and every preview hover resized the stage, the library and the inspector beside them.
/// So the power section is a fixed width whose two lines trim rather than grow, each stat keeps a
/// reserved width for its total and its delta whether or not one is shown, and the bill is clipped
/// to the room left rather than asking for more; DETAILS has the whole of it. The stat cells are
/// built once and only relabelled, because cells rebuilt on every refresh sat beside the ones
/// queued for freeing for a frame, doubling the row.
/// </para>
/// </summary>
public sealed partial class LoadoutStrip : PanelContainer
{
    private readonly VesselStock _stock;

    private BudgetBar _bar = null!;
    private Label _reading = null!;
    private Label _preview = null!;
    private readonly List<(Label Total, Label Change)> _stats = new();
    private HBoxContainer _cost = null!;
    private Button _details = null!;

    public LoadoutStrip(VesselStock stock)
    {
        _stock = stock;
    }

    public Action<bool>? DetailsToggled { get; set; }

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Box());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        AddChild(row);

        var power = Section(row, "Power Budget");
        power.CustomMinimumSize = new Vector2(PowerWidth, 0);

        _bar = new BudgetBar();
        power.AddChild(_bar);

        _reading = Trimmed(ShellPalette.FontBody);
        power.AddChild(_reading);

        _preview = Trimmed(ShellPalette.FontMicro);
        _preview.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        power.AddChild(_preview);

        row.AddChild(ShellTheme.VerticalDivider());

        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        Section(row, "Stats").AddChild(stats);

        var totalWidth = Width(TotalReserve, ShellPalette.FontBody);
        var changeWidth = Width(ChangeReserve, ShellPalette.FontMicro);

        foreach (var (header, icon, _) in StatFormat.Compact)
        {
            stats.AddChild(StatCell(header, icon, totalWidth, changeWidth, out var total, out var change));
            _stats.Add((total, change));
        }

        row.AddChild(ShellTheme.VerticalDivider());

        var cost = Section(row, "Build Cost");
        cost.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Never shown and never scrolled: a clip that takes the bill's height and none of its width.
        var clip = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        cost.AddChild(clip);

        _cost = new HBoxContainer();
        _cost.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        clip.AddChild(_cost);

        _details = new Button { Text = "DETAILS", ToggleMode = true, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_details);
        _details.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _details.Toggled += open => DetailsToggled?.Invoke(open);
        row.AddChild(_details);
    }

    /// <summary>Sets the toggle without raising <see cref="DetailsToggled"/>, for a view restoring its session state.</summary>
    public void SetDetails(bool open) => _details.SetPressedNoSignal(open);

    public void Refresh(Rollup current, Rollup? preview)
    {
        if (!IsNodeReady())
        {
            return;
        }

        _bar.Show(current.Supply, current.Draw, preview?.Supply, preview?.Draw);

        SetTrimmed(_reading, PowerReading(current));
        _reading.AddThemeColorOverride(
            "font_color", current.Draw > current.Supply ? ShellPalette.StateFault : ShellPalette.TextTitle);
        SetTrimmed(_preview, preview is null ? string.Empty : PreviewReading(current, preview));

        for (var i = 0; i < _stats.Count; i++)
        {
            var read = StatFormat.Compact[i].Read;
            var value = read(current.Totals);
            var change = preview is null ? 0 : read(preview.Totals) - value;

            _stats[i].Total.Text = StatFormat.Plain(value);
            _stats[i].Change.Text = change != 0 ? StatFormat.Signed(change) : string.Empty;
        }

        Clear(_cost);

        foreach (var (item, amount, change) in CostLines(current, preview))
        {
            _cost.AddChild(Cell(
                string.Empty,
                ("item", item),
                Units.Format(amount),
                change != 0 ? (change > 0 ? "+" : string.Empty) + Units.Format(change) : null,
                shortfall: amount > _stock.Held(item)));
        }
    }

    /// <summary>The power section's width, which its readings trim to rather than widen.</summary>
    private const int PowerWidth = 240;

    /// <summary>The widest total and change a stat cell keeps room for; the mock's totals run to four digits.</summary>
    private const string TotalReserve = "00000";
    private const string ChangeReserve = "+00000";

    private static Label Trimmed(int fontSize)
    {
        var label = new Label
        {
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            // A label ignores the mouse by default, and the tooltip is where a trimmed reading is whole.
            MouseFilter = MouseFilterEnum.Pass,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static void SetTrimmed(Label label, string text)
    {
        label.Text = text;
        label.TooltipText = text;
    }

    private float Width(string sample, int fontSize) =>
        Mathf.Ceil(GetThemeFont("font", "Label").GetStringSize(sample, HorizontalAlignment.Left, -1, fontSize).X);

    /// <summary>
    /// One stat, with its total and change labels held open at a reserved width, so a preview's
    /// delta appearing or a total gaining a digit relabels the cell without resizing it.
    /// </summary>
    private static Control StatCell(string header, string? icon, float totalWidth, float changeWidth, out Label total, out Label change)
    {
        var cell = new HBoxContainer();
        cell.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        var name = new Label { Text = header };
        name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        cell.AddChild(name);

        cell.AddChild(icon is null
            ? new IconSlot(IconSlot.RowSize, ShellPalette.TextPrimary)
            : new IconSlot("status", icon, IconSlot.RowSize, ShellPalette.TextPrimary));

        total = new Label
        {
            CustomMinimumSize = new Vector2(totalWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        total.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        total.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        cell.AddChild(total);

        change = new Label { CustomMinimumSize = new Vector2(changeWidth, 0) };
        change.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        change.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        cell.AddChild(change);

        return cell;
    }

    private static string PowerReading(Rollup rollup) =>
        rollup.Supply <= 0 ? $"NO POWER SOURCE — {rollup.Draw} / 0"
        : rollup.Draw > rollup.Supply ? $"{rollup.Draw} / {rollup.Supply} — OVER BUDGET BY {rollup.Draw - rollup.Supply}"
        : $"{rollup.Draw} / {rollup.Supply}";

    /// <summary>
    /// The preview's own reading, and — only when the candidate would change the verdict — what it
    /// would change it to, in words.
    /// </summary>
    private static string PreviewReading(Rollup current, Rollup preview)
    {
        var line = $"PREVIEW {preview.Draw} / {preview.Supply}";

        if (preview.Verdict == current.Verdict)
        {
            return line;
        }

        var verdict = preview.Verdict == Verdict.OverBudget
            ? $"OVER BUDGET BY {preview.Draw - preview.Supply}"
            : VerdictText.Of(preview.Verdict);

        // The line already opens on PREVIEW; saying it twice spent a fixed-width line on the repeat.
        return $"{line} · {verdict}";
    }

    /// <summary>
    /// The current bill in its own order, then anything only the preview would add, each with the
    /// change the preview makes to it. Zero-amount lines are kept while previewed so a fitting that
    /// would remove an item's last line shows it going to zero rather than vanishing.
    /// </summary>
    private static IEnumerable<(string Item, long Amount, long Change)> CostLines(Rollup current, Rollup? preview)
    {
        var previewed = preview?.Cost.ToDictionary(line => line.ItemId, line => line.Amount)
            ?? new Dictionary<string, long>();
        var seen = new HashSet<string>();

        foreach (var line in current.Cost)
        {
            seen.Add(line.ItemId);
            var after = preview is null ? line.Amount : previewed.GetValueOrDefault(line.ItemId);
            yield return (line.ItemId, line.Amount, after - line.Amount);
        }

        if (preview is null)
        {
            yield break;
        }

        foreach (var line in preview.Cost)
        {
            if (!seen.Contains(line.ItemId))
            {
                yield return (line.ItemId, 0, line.Amount);
            }
        }
    }

    private static VBoxContainer Section(Container row, string caption)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        row.AddChild(column);

        var title = new Label { Text = caption.ToUpperInvariant() };
        title.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(title);

        return column;
    }

    private static Control Cell(string header, (string Domain, string Name)? icon, string value, string? change, bool shortfall)
    {
        var cell = new HBoxContainer();
        cell.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        if (header.Length > 0)
        {
            var name = new Label { Text = header };
            name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cell.AddChild(name);
        }

        cell.AddChild(icon is { } glyph
            ? new IconSlot(glyph.Domain, glyph.Name, IconSlot.RowSize, ShellPalette.TextPrimary)
            : new IconSlot(IconSlot.RowSize, ShellPalette.TextPrimary));

        var reading = new Label { Text = value };
        reading.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        reading.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        cell.AddChild(reading);

        if (change is not null)
        {
            var delta = new Label { Text = change };
            delta.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            delta.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cell.AddChild(delta);
        }

        if (shortfall)
        {
            var warning = new Label { Text = "SHORT" };
            warning.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
            warning.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cell.AddChild(warning);
        }

        return cell;
    }

    private static void Clear(Node node)
    {
        // Removed now, freed later: a child only queued for freeing still lays out until the end of
        // the frame, beside its replacement.
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
