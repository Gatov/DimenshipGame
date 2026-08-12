using System.Text;
using Dimenship.Core.Content;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// A content tree held in a dictionary. Loader tests build one of these and mutate it, so no
/// loader test needs a file on disk — which is what keeps them fast and keeps a failure about the
/// rule under test rather than about a path.
/// </summary>
internal sealed class MemoryContentFileSystem : IContentFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public MemoryContentFileSystem Write(string path, string text)
    {
        _files[path] = text;
        return this;
    }

    /// <summary>Replaces one JSON fragment, so a test can state the one thing it changed.</summary>
    public MemoryContentFileSystem Edit(string path, string find, string replace)
    {
        var text = _files[path];
        if (!text.Contains(find, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{find}' is not in {path}.", nameof(find));
        }

        _files[path] = text.Replace(find, replace, StringComparison.Ordinal);
        return this;
    }

    public MemoryContentFileSystem Remove(string path)
    {
        _files.Remove(path);
        return this;
    }

    public string ReadAllText(string relativePath) => _files[relativePath];

    public bool Exists(string relativePath) => _files.ContainsKey(relativePath);

    public ContentLoadResult Load() => new JsonContentSource(this).Load();
}

/// <summary>
/// Reads a content tree from a directory. Only the fidelity test uses it, and only to read the
/// tree the game ships, copied beside the test binary by the project file.
/// </summary>
internal sealed class DirectoryContentFileSystem : IContentFileSystem
{
    private readonly string _root;

    public DirectoryContentFileSystem(string root) => _root = root;

    public static DirectoryContentFileSystem Shipped() =>
        new(Path.Combine(AppContext.BaseDirectory, "content"));

    public string ReadAllText(string relativePath) =>
        File.ReadAllText(Path.Combine(_root, relativePath), Encoding.UTF8);

    public bool Exists(string relativePath) => File.Exists(Path.Combine(_root, relativePath));
}

/// <summary>
/// The smallest tree that loads clean: two items, one schematic, one facility archetype, one line,
/// two storages, one sink, and a scenario that uses them. Tests take a copy and break exactly one
/// thing, so what a test asserts is what the rule under test rejects — not a pile of unrelated
/// errors from a fixture that was already broken.
/// </summary>
internal static class ContentTree
{
    public const string Manifest = "manifest.json";
    public const string Items = "catalog/items.json";
    public const string Schematics = "catalog/schematics.json";
    public const string Facilities = "catalog/facilities.json";
    public const string Transports = "catalog/transports.json";
    public const string Storages = "catalog/storages.json";
    public const string Sinks = "catalog/sinks.json";
    public const string Reactors = "catalog/reactors.json";
    public const string Programs = "catalog/programs.json";
    public const string Strata = "catalog/strata.json";
    public const string Scenario = "scenarios/vessel.json";

    public static MemoryContentFileSystem Valid() =>
        new MemoryContentFileSystem()
            .Write(Manifest, """
                {
                  "contentVersion": "test-1",
                  "catalog": [
                    "items.json", "schematics.json", "facilities.json", "transports.json",
                    "storages.json", "sinks.json", "reactors.json", "programs.json", "strata.json"
                  ],
                  "scenarios": [ "vessel.json" ]
                }
                """)
            .Write(Items, """
                {
                  "items": [
                    { "id": "ore", "label": "Ore", "holdCapacity": 1000000 },
                    { "id": "alloy", "label": "Alloy", "holdCapacity": 500000 }
                  ]
                }
                """)
            .Write(Schematics, """
                {
                  "schematics": [
                    {
                      "id": "smelt",
                      "output": { "item": "alloy", "quantity": 100 },
                      "inputs": [ { "item": "ore", "quantity": 400 } ],
                      "effortPerRun": 1600,
                      "energyPerRun": 200,
                      "requiredFacilityType": "matter_reactor"
                    }
                  ]
                }
                """)
            .Write(Facilities, """
                {
                  "facilities": [
                    {
                      "id": "refinery",
                      "label": "Refinery",
                      "type": "matter_reactor",
                      "workRatePerTick": 100,
                      "standingPowerDraw": 150,
                      "switchOverTicks": 30,
                      "bufferPermille": 25,
                      "commandable": true
                    },
                    {
                      "id": "collector",
                      "label": "Passive Collector",
                      "type": "extractor",
                      "workRatePerTick": 100,
                      "standingPowerDraw": 150,
                      "switchOverTicks": 30,
                      "bufferPermille": 25,
                      "commandable": false
                    }
                  ]
                }
                """)
            .Write(Transports, """
                {
                  "transports": [
                    { "id": "feed", "label": "Feed Line", "throughputPerTick": 100, "standingPowerDraw": 200 }
                  ]
                }
                """)
            .Write(Storages, """
                {
                  "storages": [
                    { "id": "global_hold", "label": "Hold", "capacityPermille": 1000 },
                    { "id": "facility_buffer", "label": "Buffer", "capacityPermille": 25 }
                  ]
                }
                """)
            .Write(Sinks, """
                {
                  "sinks": [
                    { "id": "stabilization_field", "label": "Stabilization Array", "powerDraw": 4000 }
                  ]
                }
                """)
            .Write(Reactors, """{ "reactors": [] }""")
            .Write(Programs, """{ "programs": [] }""")
            .Write(Strata, """{ "strata": [] }""")
            .Write(Scenario, """
                {
                  "id": "vessel",
                  "label": "Test Vessel",
                  "energyCapacity": 10000,
                  "hold": "hold",
                  "storages": [
                    {
                      "id": "hold",
                      "archetype": "global_hold",
                      "nameOverride": null,
                      "initial": [ { "item": "ore", "quantity": 1000 } ],
                      "placement": { "column": 1, "row": 0, "badge": "1" }
                    },
                    {
                      "id": "refinery_buffer",
                      "archetype": "facility_buffer",
                      "nameOverride": "Refinery Buffer",
                      "initial": []
                    }
                  ],
                  "facilities": [
                    {
                      "id": "refinery_a",
                      "archetype": "refinery",
                      "nameOverride": "Refinery Alpha",
                      "localStorage": "refinery_buffer",
                      "initialSchematic": "smelt",
                      "builtAtStart": true,
                      "placement": { "column": 0, "row": 0, "badge": "2" }
                    }
                  ],
                  "routes": [
                    {
                      "id": "hold_to_refinery",
                      "archetype": "feed",
                      "nameOverride": "Refinery Feed",
                      "from": "hold",
                      "to": "refinery_buffer",
                      "builtAtStart": true
                    }
                  ],
                  "sinks": [ "stabilization_field" ],
                  "unlockedSchematics": [ "smelt" ],
                  "initialTasks": [
                    { "schematic": "smelt", "executor": "refinery_a" }
                  ],
                  "initialTransfers": [
                    { "item": "ore", "from": "hold", "to": "refinery_buffer", "executor": "hold_to_refinery" }
                  ]
                }
                """);
}
