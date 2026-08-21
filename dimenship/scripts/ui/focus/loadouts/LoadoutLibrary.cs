using System.Collections.Generic;

namespace Dimenship.Ui;

/// <summary>
/// The templates the mock opens with. Between them they put every state the composer can show on
/// screen before the player has touched anything: a complete template, one that draws more than it
/// supplies, and one with an empty socket.
/// <para>
/// None of them are built, queued, or persisted. They exist so the composer has something to be
/// judged against that is not an empty canvas — a composer is easy to like when there is nothing
/// in it.
/// </para>
/// </summary>
public static class LoadoutLibrary
{
    public static List<LoadoutDraft> Create() => new()
    {
        SurveyPattern(),
        DeepProspector(),
        SalvageHauler(),
    };

    /// <summary>
    /// An empty template for <c>+ NEW TEMPLATE</c>: the lighter frame, every socket unfilled. It
    /// opens <c>INCOMPLETE</c> on purpose, so the first thing the player learns about the verdict
    /// chip is what it looks like when there is work left.
    /// </summary>
    public static LoadoutDraft Empty(int ordinal)
    {
        var frame = LoadoutCatalog.Frame(LoadoutCatalog.Surveyor);

        return new LoadoutDraft
        {
            Name = $"Untitled Template {ordinal}",
            Description = "No description yet.",
            FrameId = frame.Id,
            Fitted = Unfilled(frame),
        };
    }

    /// <summary>Every socket filled, and the core supplies more than the fittings spend.</summary>
    private static LoadoutDraft SurveyPattern() => new()
    {
        Name = "Survey Pattern A",
        Description = "The standard prospecting fit: one tool, cheap eyes, room in the budget.",
        FrameId = LoadoutCatalog.Surveyor,
        Fitted = new List<string?>
        {
            "precision_manipulator",
            "basic_sensor_array",
            "power_core_mk1",
            "evidence_collector",
        },
    };

    /// <summary>
    /// Two hungry sensors over the smaller core: −340 and −160 against +600, and the mock's only
    /// invented constraint is what says so.
    /// </summary>
    private static LoadoutDraft DeepProspector() => new()
    {
        Name = "Deep Prospector",
        Description = "Sees everything, on a core that cannot pay for it.",
        FrameId = LoadoutCatalog.Surveyor,
        Fitted = new List<string?>
        {
            "mining_head_mk1",
            "phase_resonance_probe",
            "power_core_mk1",
            "forensic_sampler",
        },
    };

    /// <summary>
    /// Defense left empty, so the rollup has to show a frame baseline for a stat nothing is
    /// contributing to. GDD §5.10: the hauler runs at its unequipped rating, and that is a reading
    /// rather than an error.
    /// </summary>
    private static LoadoutDraft SalvageHauler() => new()
    {
        Name = "Salvage Hauler",
        Description = "Cuts, carries, and goes out unarmoured until the plating is built.",
        FrameId = LoadoutCatalog.Hauler,
        Fitted = new List<string?>
        {
            "salvage_cutter",
            "expanded_hold_pod",
            "endurance_cell",
            null,
            "basic_sensor_array",
        },
    };

    private static List<string?> Unfilled(FrameDef frame)
    {
        var fitted = new List<string?>();

        for (var i = 0; i < frame.Sockets.Count; i++)
        {
            fitted.Add(null);
        }

        return fitted;
    }
}
