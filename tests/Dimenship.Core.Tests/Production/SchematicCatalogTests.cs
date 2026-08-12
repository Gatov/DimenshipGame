using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

public class SchematicCatalogTests
{
    private static readonly ItemId Alloy = new("alloy");
    private static readonly ItemId Ore = new("ore");
    private static readonly ItemId Scrap = new("scrap");

    private static SchematicDefinition Producing(string id, ItemId output, ItemId input) =>
        new()
        {
            Id = new SchematicId(id),
            Output = new ItemAmount(output, 1),
            Inputs = new[] { new ItemAmount(input, 1) },
            EffortPerRun = new WorkAmount(100),
            EnergyPerRun = new EnergyAmount(10),
            RequiredFacilityType = FacilityType.MatterReactor,
        };

    [Test]
    public void Get_ReturnsTheSchematic()
    {
        var smelt = Producing("smelt", Alloy, Ore);
        var catalog = new SchematicCatalog(new[] { smelt });

        Assert.That(catalog.Get(new SchematicId("smelt")), Is.SameAs(smelt));
    }

    [Test]
    public void Get_UnknownId_ThrowsNamingTheId()
    {
        var catalog = new SchematicCatalog(Array.Empty<SchematicDefinition>());

        var thrown = Assert.Throws<KeyNotFoundException>(() => catalog.Get(new SchematicId("nope")));

        Assert.That(thrown!.Message, Does.Contain("nope"), "a lookup failure has to say what was looked up");
    }

    [Test]
    public void TryGet_UnknownId_IsFalse()
    {
        var catalog = new SchematicCatalog(Array.Empty<SchematicDefinition>());

        Assert.That(catalog.TryGet(new SchematicId("nope"), out _), Is.False);
    }

    [Test]
    public void ForOutput_ReturnsEveryProducerInDeclarationOrder()
    {
        // Ids are deliberately not alphabetical relative to declaration order: an implementation
        // that enumerated a dictionary, or sorted by id, would return them the other way round
        // and make the planner's choice between two recipes depend on hashing.
        var catalog = new SchematicCatalog(
            new[] { Producing("zulu", Alloy, Ore), Producing("alpha", Alloy, Scrap) });

        Assert.That(
            catalog.ForOutput(Alloy).Select(s => s.Id.Value).ToList(),
            Is.EqualTo(new List<string> { "zulu", "alpha" }));
    }

    [Test]
    public void ForOutput_UnproducedItem_IsEmpty()
    {
        var catalog = new SchematicCatalog(
            new[] { Producing("smelt", Alloy, Ore) });

        Assert.That(catalog.ForOutput(Scrap), Is.Empty);
    }

    [Test]
    public void DuplicateSchematicId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SchematicCatalog(
            new[] { Producing("smelt", Alloy, Ore), Producing("smelt", Alloy, Scrap) }));
    }
}
