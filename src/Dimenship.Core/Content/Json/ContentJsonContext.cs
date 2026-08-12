using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dimenship.Core.Content.Json;

/// <summary>
/// Source-generated serialization for every content file. Generated rather than reflected because
/// the shell exports through Godot, and a reflection-based serializer is the first thing trimming
/// and AOT take away — silently, and only in the exported build.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false)]
[JsonSerializable(typeof(ManifestFile))]
[JsonSerializable(typeof(ItemsFile))]
[JsonSerializable(typeof(SchematicsFile))]
[JsonSerializable(typeof(FacilitiesFile))]
[JsonSerializable(typeof(TransportsFile))]
[JsonSerializable(typeof(StoragesFile))]
[JsonSerializable(typeof(SinksFile))]
[JsonSerializable(typeof(ReactorsFile))]
[JsonSerializable(typeof(ProgramsFile))]
[JsonSerializable(typeof(StrataFile))]
[JsonSerializable(typeof(ScenarioFile))]
internal sealed partial class ContentJsonContext : JsonSerializerContext;
