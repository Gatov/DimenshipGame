using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// The engine's stateless collaborators, built over a world the way the engine builds them and
/// with no engine anywhere. That is the point of testing them directly: each answers from the
/// catalog and the state alone, and a test that needed an engine to get one would not show it.
/// </summary>
internal sealed record Collaborators(
    ContentCatalog Catalog, WorldState State, Resolver Resolve, WorkpieceAcceptance Acceptance, StorageMath Storage)
{
    public static Collaborators Over(WorldBuilder world)
    {
        var catalog = world.Catalog();
        var state = ScenarioSeeder.Seed(catalog, world.Scenario());
        var resolve = new Resolver(catalog, state);
        var acceptance = new WorkpieceAcceptance(
            catalog, state.Vessel.Facilities.Select(f => (f.LocalStorage, resolve.Archetype(f).Type)));
        return new Collaborators(catalog, state, resolve, acceptance, new StorageMath(catalog, state, resolve, acceptance));
    }

    public SnapshotBuilder Snapshots() => new(Catalog, State, Resolve, Storage);

    public EngineWorldView View() => new(Catalog, State, Resolve, Storage, Acceptance);
}
