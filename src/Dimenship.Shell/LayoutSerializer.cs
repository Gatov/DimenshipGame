using System.Text.Json;

namespace Dimenship.Shell;

/// <summary>Outcome of loading a layout. Always carries a usable state, however bad the input was.</summary>
public sealed record LayoutLoadResult(LayoutState State, IReadOnlyList<string> Warnings, bool UsedDefault);

/// <summary>
/// Reads and writes <see cref="LayoutState"/> as JSON. Every degraded input produces a valid
/// state plus warnings rather than an exception: a corrupt layout file must never stop the
/// shell from opening.
/// </summary>
public static class LayoutSerializer
{
    public const int MinSplitOffset = -2000;
    public const int MaxSplitOffset = 2000;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record Dto(
        string? ActiveFocus,
        string? InspectorPanel,
        string? ConsolePanel,
        int InspectorSplitOffset,
        int ConsoleSplitOffset,
        bool InspectorCollapsed,
        bool ConsoleCollapsed);

    public static string ToJson(LayoutState state) =>
        JsonSerializer.Serialize(
            new Dto(
                state.ActiveFocus.Value,
                state.InspectorPanel.Value,
                state.ConsolePanel.Value,
                state.InspectorSplitOffset,
                state.ConsoleSplitOffset,
                state.InspectorCollapsed,
                state.ConsoleCollapsed),
            Options);

    public static LayoutLoadResult Load(
        string? json,
        IReadOnlyDictionary<PanelId, PanelDescriptor> known,
        LayoutState defaults)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LayoutLoadResult(defaults, Array.Empty<string>(), UsedDefault: true);
        }

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json);
        }
        catch (JsonException e)
        {
            return new LayoutLoadResult(defaults, new[] { $"layout file is not valid JSON: {e.Message}" }, true);
        }

        if (dto is null)
        {
            return new LayoutLoadResult(defaults, new[] { "layout file deserialized to null" }, true);
        }

        var warnings = new List<string>();

        return new LayoutLoadResult(
            new LayoutState(
                Resolve(dto.ActiveFocus, ZoneKind.Focus, defaults.ActiveFocus, known, warnings),
                Resolve(dto.InspectorPanel, ZoneKind.Panel, defaults.InspectorPanel, known, warnings),
                Resolve(dto.ConsolePanel, ZoneKind.Panel, defaults.ConsolePanel, known, warnings),
                Clamp(dto.InspectorSplitOffset, "InspectorSplitOffset", warnings),
                Clamp(dto.ConsoleSplitOffset, "ConsoleSplitOffset", warnings),
                dto.InspectorCollapsed,
                dto.ConsoleCollapsed),
            warnings,
            UsedDefault: false);
    }

    private static PanelId Resolve(
        string? value,
        ZoneKind required,
        PanelId fallback,
        IReadOnlyDictionary<PanelId, PanelDescriptor> known,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            warnings.Add($"layout named no {required} panel; using '{fallback}'");
            return fallback;
        }

        var id = new PanelId(value);

        if (!known.TryGetValue(id, out var descriptor))
        {
            warnings.Add($"layout names unknown panel '{value}'; using '{fallback}'");
            return fallback;
        }

        if (descriptor.Zone != required)
        {
            warnings.Add($"panel '{value}' cannot occupy a {required} zone; using '{fallback}'");
            return fallback;
        }

        return id;
    }

    private static int Clamp(int value, string name, List<string> warnings)
    {
        if (value >= MinSplitOffset && value <= MaxSplitOffset)
        {
            return value;
        }

        var clamped = Math.Clamp(value, MinSplitOffset, MaxSplitOffset);
        warnings.Add($"{name} {value} is out of range; clamped to {clamped}");
        return clamped;
    }
}
