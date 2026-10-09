using System.Globalization;
using System.Text;
using Dimenship.Core.State;

namespace Dimenship.Replay;

/// <summary>
/// Renders a <see cref="ReplayResult"/> as Markdown tables, so a baseline can be committed under
/// <c>docs/reviews/</c> as it stands and a before/after pair diffs line by line.
/// <para>
/// Byte-identical for one result on any machine: every number goes through the invariant culture,
/// lines end in <c>\n</c> whatever the platform's newline is, and nothing from the environment —
/// no path, no clock, no machine name — is written. A report that named its content directory
/// could not be compared with the same run from another checkout.
/// </para>
/// </summary>
public static class ReplayReport
{
    private static readonly string[] CategoryHeadings =
    {
        "Working", "Idle", "Waiting input", "Waiting output", "Throttled", "Switching", "Held",
    };

    public static string Format(ReplayResult result)
    {
        var text = new StringBuilder();

        Line(text, "# Replay report");
        Line(text);
        Line(text, $"- Scenario: `{result.Scenario}`");
        Line(text, $"- Content version: `{result.ContentVersion}`");
        Line(text, $"- Ticks run: {N(result.EndTick)}");
        Line(text, $"- Interventions (commands applied): {N(result.Interventions)}");
        Line(text);

        Line(text, "## Demands");
        Line(text);
        Line(text, "Quantities in milli-units. Readiness is ticks from commit to the plan's last completion.");
        Line(text);
        Line(text, "| Demand | Item | Goal | At | Committed | Ready | Readiness | Delivered | Shortfall | Priority |");
        Line(text, "| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |");
        foreach (var d in result.Demands)
        {
            var committed = d.CommittedAtTick is { } c ? N(c) : $"refused ({N(d.RefusedIssues)} issues)";
            var ready = d.ReadyAtTick is { } r ? N(r) : d.CommittedAtTick is null ? "—" : "not ready";
            var readiness = d.Readiness is { } t ? N(t) : "—";
            Line(text,
                $"| {d.Demand.Id} | {d.Demand.Goal.Item} | {N(d.Demand.Goal.Quantity)} | {N(d.Demand.Tick)} " +
                $"| {committed} | {ready} | {readiness} | {N(d.Delivered)} | {N(d.Shortfall)} " +
                $"| {d.Demand.Priority?.ToString() ?? "Normal"} |");
        }

        if (result.Commands.Count > 0)
        {
            Line(text);
            Line(text, "## Commands");
            Line(text);
            Line(text, "Scripted commands, in script order. A refused command changed nothing and is not an intervention.");
            Line(text);
            Line(text, "| At | Command | Demand | Detail | Outcome |");
            Line(text, "| ---: | :--- | :--- | :--- | :--- |");
            foreach (var c in result.Commands)
            {
                var s = c.Command;
                var detail = new List<string>();
                if (s.Priority is { } priority)
                {
                    detail.Add(priority.ToString());
                }

                if (s.Quantity is { } quantity)
                {
                    detail.Add(N(quantity));
                }

                if (s.Item is { } item)
                {
                    detail.Add($"{item} at {s.Storage}");
                }

                if (s.To is { } to)
                {
                    detail.Add($"to {to}");
                }

                var outcome = c.Refusal is { } reason ? $"refused: {reason}" : "accepted";
                Line(text,
                    $"| {N(s.Tick)} | {s.Kind.ToString().ToLowerInvariant()} | {s.Demand} " +
                    $"| {(detail.Count == 0 ? "—" : string.Join(", ", detail))} | {outcome} |");
            }
        }

        if (result.Unfinished.Count > 0)
        {
            Line(text);
            Line(text, "## Unfinished work");
            Line(text);
            Line(text, "Every task of a not-ready demand still open at the end, as the engine last described it.");
            Line(text);
            Line(text, "| Demand | Task | Executor | Work | State | Reason |");
            Line(text, "| :--- | ---: | :--- | :--- | :--- | :--- |");
            foreach (var u in result.Unfinished)
            {
                Line(text,
                    $"| {u.Demand} | {u.Task} | {u.Executor} | {u.Work} | {u.State} | {u.Reason?.ToString() ?? "—"} |");
            }
        }

        Line(text);
        Line(text, "## Material tied up");
        Line(text);
        Line(text, "Inputs held by unfinished runs plus cargo on belts, per item, in milli-units, sampled every tick.");
        Line(text);
        if (result.MaterialTiedUp.Count == 0)
        {
            Line(text, "Nothing was ever in process.");
        }
        else
        {
            Line(text, "| Item | Mean | Peak |");
            Line(text, "| :--- | ---: | ---: |");
            foreach (var m in result.MaterialTiedUp)
            {
                Line(text, $"| {m.Subject} | {N(m.Mean)} | {N(m.Peak)} |");
            }
        }

        Line(text);
        Line(text, "## Space tied up");
        Line(text);
        Line(text, "Storage fill in permille of its shared volume, sampled every tick.");
        Line(text);
        Line(text, "| Storage | Mean | Peak |");
        Line(text, "| :--- | ---: | ---: |");
        foreach (var s in result.SpaceTiedUp)
        {
            Line(text, $"| {s.Subject} | {N(s.Mean)} | {N(s.Peak)} |");
        }

        Line(text);
        Line(text, "## Changeovers");
        Line(text);
        Line(text, "| Facility | Count | Ticks | Abandoned |");
        Line(text, "| :--- | ---: | ---: | ---: |");
        foreach (var c in result.Changeovers)
        {
            Line(text, $"| {c.Facility} | {N(c.Count)} | {N(c.Ticks)} | {N(c.Abandoned)} |");
        }

        Line(text);
        Line(text, "## Facility time");
        Line(text);
        Line(text, "Ticks under each utilization category over the whole run, counted while built.");
        Line(text);
        Line(text, "| Facility | " + string.Join(" | ", CategoryHeadings) + " |");
        Line(text, "| :--- |" + string.Concat(Enumerable.Repeat(" ---: |", CategoryHeadings.Length)));
        foreach (var f in result.FacilityTime)
        {
            Line(text, $"| {f.Facility} | " + string.Join(" | ", f.TicksByCategory.Select(N)) + " |");
        }

        Line(text);
        Line(text, $"Final state SHA-256: `{result.FinalStateSha256}`");

        return text.ToString();
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder text, string line = "") => text.Append(line).Append('\n');

    static ReplayReport()
    {
        // The headings are the window's categories in declaration order; a category appended there
        // without a heading here would shift every column after it.
        if (CategoryHeadings.Length != Enum.GetValues<UtilizationCategory>().Length)
        {
            throw new InvalidOperationException("ReplayReport's headings no longer match UtilizationCategory.");
        }
    }
}
