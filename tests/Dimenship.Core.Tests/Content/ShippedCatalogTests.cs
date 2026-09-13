using Dimenship.Core.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// Invariants over the shipped catalog itself, as opposed to the loader's rejection rules — those
/// live in <see cref="ContentLoaderTests"/> against the minimal <see cref="ContentTree"/>. A rule
/// the loader enforces (every archetype has a <c>purpose</c>) is already proven; this is the
/// separate question of whether the vessel the game actually ships answers it with real sentences.
/// </summary>
public class ShippedCatalogTests
{
    [Test]
    public void EveryShippedFacilityArchetype_HasANonEmptyPurpose()
    {
        foreach (var facility in Shipped.Catalog.Facilities)
        {
            Assert.That(
                string.IsNullOrWhiteSpace(facility.Purpose), Is.False,
                $"'{facility.Id}' would show a blank PURPOSE row in the inspector and a blank " +
                "capability-gained line in the construction preview");
        }
    }
}
