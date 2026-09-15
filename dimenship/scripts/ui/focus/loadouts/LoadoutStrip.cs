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
/// </summary>
public sealed partial class LoadoutStrip : PanelContainer
{
    private readonly ItemStock _stock;

    private BudgetBar _bar = null!;
    private Label _reading = null!;
    private Label _preview = null!;
    private HBoxContainer _stats = null!;
    private HBoxContainer _cost = null!;
    private Button _details = null!;

    public LoadoutStrip(ItemStock stock)
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
        power.CustomMinimumSize = new Vector2(220, 0);

        _bar = new BudgetBar();
        power.AddChild(_bar);

        _reading = new Label();
        _reading.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        power.AddChild(_reading);

        _preview = new Label();
        _preview.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        _preview.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        power.AddChild(_preview);

        row.AddChild(ShellTheme.VerticalDivider());

        _stats = new HBoxContainer();
        _stats.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        Section(row, "Stats").AddChild(_stats);

        row.AddChild(ShellTheme.VerticalDivider());

        var cost = Section(row, "Build Cost");
        cost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _cost = new HBoxContainer();
        _cost.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        cost.AddChild(_cost);

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

        _reading.Text = PowerReading(current);
        _reading.AddThemeColorOverride(
            "font_color", current.Draw > current.Supply ? ShellPalette.StateFault : ShellPalette.TextTitle);
        _preview.Text = preview is null ? string.Empty : PreviewReading(current, preview);

        Clear(_stats);

        foreach (var (header, icon, read) in StatFormat.Compact)
        {
            var value = read(current.Totals);
            long? change = preview is null ? null : read(preview.Totals) - value;

            _stats.AddChild(Cell(
                header,
                icon is null ? null : ("status", icon),
                StatFormat.Plain(value),
                change is { } c && c != 0 ? StatFormat.Signed(c) : null,
                shortfall: false));
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

        return $"{line} · PREVIEW: {verdict}";
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
        foreach (var child in node.GetChildren())
        {
            child.QueueFree();
        }
    }
}
