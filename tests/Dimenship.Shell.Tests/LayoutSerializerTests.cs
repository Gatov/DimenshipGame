using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class LayoutSerializerTests
{
    private static readonly PanelId Overview = new("overview");
    private static readonly PanelId BaseGraph = new("base_graph");
    private static readonly PanelId Energy = new("energy_budget");
    private static readonly PanelId EventLog = new("event_log");

    private static readonly Dictionary<PanelId, PanelDescriptor> Known = new()
    {
        [Overview] = new PanelDescriptor(Overview, "Overview", ZoneKind.Focus),
        [BaseGraph] = new PanelDescriptor(BaseGraph, "Base Graph", ZoneKind.Focus),
        [Energy] = new PanelDescriptor(Energy, "Energy Budget", ZoneKind.Panel),
        [EventLog] = new PanelDescriptor(EventLog, "Event Log", ZoneKind.Panel),
    };

    private static readonly LayoutState Defaults =
        new(Overview, Energy, EventLog, 900, 300, false, false);

    [Test]
    public void RoundTrip_PreservesEveryField()
    {
        var original = new LayoutState(BaseGraph, Energy, EventLog, 742, 188, true, false);

        var result = LayoutSerializer.Load(LayoutSerializer.ToJson(original), Known, Defaults);

        Assert.That(result.State, Is.EqualTo(original));
        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.UsedDefault, Is.False);
    }

    [Test]
    public void Load_NullJson_UsesDefaultsWithoutComplaining()
    {
        var result = LayoutSerializer.Load(null, Known, Defaults);

        Assert.That(result.State, Is.EqualTo(Defaults));
        Assert.That(result.UsedDefault, Is.True);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Load_UnparseableJson_UsesDefaultsAndWarns()
    {
        var result = LayoutSerializer.Load("{ this is not json", Known, Defaults);

        Assert.That(result.State, Is.EqualTo(Defaults));
        Assert.That(result.UsedDefault, Is.True);
        Assert.That(result.Warnings, Is.Not.Empty);
    }

    [Test]
    public void Load_UnknownPanel_FallsBackForThatZoneOnly()
    {
        var json = LayoutSerializer.ToJson(
            new LayoutState(BaseGraph, new PanelId("deleted_panel"), EventLog, 500, 200, false, false));

        var result = LayoutSerializer.Load(json, Known, Defaults);

        Assert.That(result.State.InspectorPanel, Is.EqualTo(Defaults.InspectorPanel));
        Assert.That(result.State.ActiveFocus, Is.EqualTo(BaseGraph), "other zones must survive");
        Assert.That(result.State.ConsolePanel, Is.EqualTo(EventLog), "other zones must survive");
        Assert.That(result.State.InspectorSplitOffset, Is.EqualTo(500), "offsets must survive");
        Assert.That(result.Warnings.Count(w => w.Contains("deleted_panel")), Is.EqualTo(1));
        Assert.That(result.UsedDefault, Is.False);
    }

    [Test]
    public void Load_FocusViewInPanelZone_FallsBack()
    {
        var json = LayoutSerializer.ToJson(
            new LayoutState(Overview, BaseGraph, EventLog, 500, 200, false, false));

        var result = LayoutSerializer.Load(json, Known, Defaults);

        Assert.That(result.State.InspectorPanel, Is.EqualTo(Defaults.InspectorPanel));
        Assert.That(result.Warnings.Count(w => w.Contains("base_graph")), Is.EqualTo(1));
    }

    [Test]
    public void Load_PanelInFocusZone_FallsBack()
    {
        var json = LayoutSerializer.ToJson(
            new LayoutState(Energy, Energy, EventLog, 500, 200, false, false));

        var result = LayoutSerializer.Load(json, Known, Defaults);

        Assert.That(result.State.ActiveFocus, Is.EqualTo(Defaults.ActiveFocus));
    }

    [Test]
    public void Load_AbsurdSplitOffset_IsClamped()
    {
        var json = LayoutSerializer.ToJson(
            new LayoutState(Overview, Energy, EventLog, 999_999, -999_999, false, false));

        var result = LayoutSerializer.Load(json, Known, Defaults);

        Assert.That(result.State.InspectorSplitOffset, Is.EqualTo(LayoutSerializer.MaxSplitOffset));
        Assert.That(result.State.ConsoleSplitOffset, Is.EqualTo(LayoutSerializer.MinSplitOffset));
        Assert.That(result.Warnings, Has.Count.EqualTo(2));
    }

    [Test]
    public void Load_MissingPanelField_FallsBackAndWarns()
    {
        const string json = """
            { "InspectorSplitOffset": 400, "ConsoleSplitOffset": 150 }
            """;

        var result = LayoutSerializer.Load(json, Known, Defaults);

        Assert.That(result.State.ActiveFocus, Is.EqualTo(Defaults.ActiveFocus));
        Assert.That(result.State.InspectorPanel, Is.EqualTo(Defaults.InspectorPanel));
        Assert.That(result.State.ConsolePanel, Is.EqualTo(Defaults.ConsolePanel));
        Assert.That(result.State.InspectorSplitOffset, Is.EqualTo(400));
        Assert.That(result.Warnings, Has.Count.EqualTo(3));
    }
}
