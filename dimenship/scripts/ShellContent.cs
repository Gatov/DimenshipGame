using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Content;
using Dimenship.Core.Simulation;

namespace Dimenship;

/// <summary>
/// The content the shell runs on, loaded once per process out of <c>res://content</c>.
/// <para>
/// Once, and shared: the catalog is the rulebook, it is immutable, and every world opened during
/// this run references it by id rather than copying it. Loading it per world would be the weak
/// form of static — a separate object that could quietly acquire a per-save field.
/// </para>
/// <para>
/// A load failure is fatal and says so. Content that does not link is not something to limp along
/// with: the alternative is a vessel missing a facility nobody notices until a route ends nowhere.
/// </para>
/// </summary>
public static class ShellContent
{
    private static ContentLoadResult? _result;

    public static ContentCatalog Catalog => Load().Catalog!;

    public static Scenario DefaultVessel =>
        Load().Scenarios.FirstOrDefault(s => s.Id == "default_vessel")
        ?? Load().Scenarios[0];

    public static SimulationEngine NewGame() => SimulationEngine.NewGame(Catalog, DefaultVessel);

    private static ContentLoadResult Load()
    {
        if (_result is { } loaded)
        {
            return loaded;
        }

        var result = new JsonContentSource(new GodotContentFileSystem()).Load();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "content failed to load:\n" + string.Join("\n", result.Errors));
        }

        _result = result;
        return result;
    }
}
