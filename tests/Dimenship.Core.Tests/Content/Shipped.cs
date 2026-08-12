using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// The content the game ships, loaded once for the whole test run — which is also how the game
/// loads it: once per process, immutable, shared by every world opened during that run.
/// <para>
/// A test that wants the default vessel asks here rather than constructing one, because there is
/// no longer anywhere to construct one from. That is the point of the stage: the vessel is content.
/// </para>
/// </summary>
internal static class Shipped
{
    private static readonly ContentLoadResult Result =
        new JsonContentSource(DirectoryContentFileSystem.Shipped()).Load();

    public static ContentCatalog Catalog =>
        Result.Catalog
        ?? throw new InvalidOperationException(
            "the shipped content does not load:\n" + string.Join("\n", Result.Errors));

    public static Scenario DefaultVessel
    {
        get
        {
            _ = Catalog;
            return Result.Scenarios.Single(s => s.Id == "default_vessel");
        }
    }

    /// <summary>A fresh campaign on the shipped vessel.</summary>
    public static SimulationEngine Engine() => SimulationEngine.NewGame(Catalog, DefaultVessel);

    /// <summary>A fresh world, for tests that want the state rather than the engine over it.</summary>
    public static WorldState State() => ScenarioSeeder.Seed(Catalog, DefaultVessel);
}
