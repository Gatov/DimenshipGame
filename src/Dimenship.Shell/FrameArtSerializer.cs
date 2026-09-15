using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dimenship.Shell;

/// <summary>
/// One socket's placement on a frame's artwork: where its connector is drawn, and where its box
/// is centred. Both in the artwork's own coordinates, so the drawing, the boxes and the leader
/// lines scale together — see <see cref="StageGeometry"/>.
/// </summary>
public sealed record FrameArtSocket(string Id, (int X, int Y) Anchor, (int X, int Y) Box);

/// <summary>
/// A frame's illustration and where each of its sockets sits on it. Placement only: which fitting
/// fits which socket is the loadout catalog's, so no edit to this file can change what is legal.
/// </summary>
/// <param name="Canvas">The artwork's <c>viewBox</c> size. The frame is drawn in its middle and the
/// margins are where the boxes go.</param>
public sealed record FrameArt(
    string Frame,
    string Artwork,
    (int W, int H) Canvas,
    IReadOnlyList<FrameArtSocket> Sockets);

/// <summary>Outcome of reading one placement file. <see cref="Art"/> is null exactly when there are warnings.</summary>
public sealed record FrameArtLoadResult(FrameArt? Art, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a frame art placement file (<c>assets/loadouts/frames/{frame}.json</c>) on the settings
/// file's contract: every degraded input produces warnings rather than an exception, because a
/// broken placement is a presentation problem and the composer must keep working without its art.
/// <para>
/// Unlike <see cref="SettingsSerializer"/>, a problem here yields <b>no</b> result rather than a
/// defaulted one. A half-placed frame reads as a layout, and the player would take it for the
/// intended one; the view falls back to a plain column for the whole frame instead
/// (<c>2026-09-14-glass-console-loadout-editor-design.md</c>, Decision 6).
/// </para>
/// <para>
/// Every DTO field is nullable so a missing one is reported rather than read as zero, unknown
/// fields are rejected so a misspelt <c>ancor</c> is not silently ignored, and coordinates are
/// integers so a fractional one fails parsing rather than being rounded somewhere nobody sees.
/// </para>
/// <para>
/// This assembly cannot see the loadout catalog, so validation is split along that line:
/// <see cref="Load"/> checks what a file can get wrong on its own, and <see cref="Check"/> compares
/// it with a frame's socket ids, which the Godot layer supplies.
/// </para>
/// </summary>
public static class FrameArtSerializer
{
    /// <summary>The catalog's id pattern, so a socket id could be a content id if sockets ever become content.</summary>
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record PointDto(int? X, int? Y);

    private sealed record SizeDto(int? Width, int? Height);

    private sealed record SocketDto(string? Id, PointDto? Anchor, PointDto? Box);

    private sealed record Dto(
        string? Notes,
        string? Frame,
        string? Artwork,
        SizeDto? Canvas,
        List<SocketDto?>? Sockets);

    public static FrameArtLoadResult Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failed("placement file is empty");
        }

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, Options);
        }
        catch (JsonException e)
        {
            return Failed($"placement file is not valid: {e.Message}");
        }

        if (dto is null)
        {
            return Failed("placement file deserialized to null");
        }

        var warnings = new List<string>();
        var frame = Required(dto.Frame, "frame", warnings);
        var artwork = Required(dto.Artwork, "artwork", warnings);
        var canvas = ReadCanvas(dto.Canvas, warnings);
        var sockets = ReadSockets(dto.Sockets, canvas, warnings);

        return warnings.Count > 0 || frame is null || artwork is null || canvas is null
            ? new FrameArtLoadResult(null, warnings)
            : new FrameArtLoadResult(new FrameArt(frame, artwork, canvas.Value, sockets), warnings);
    }

    /// <summary>
    /// Compares a parsed placement with the frame it is meant for. The socket ids must match
    /// exactly — none missing, none extra — because a socket with no placement has nowhere to draw
    /// its box, and a placement for a socket the frame lacks is a file written for another frame.
    /// </summary>
    public static IReadOnlyList<string> Check(FrameArt art, string frameId, IReadOnlyList<string> socketIds)
    {
        var warnings = new List<string>();

        if (art.Frame != frameId)
        {
            warnings.Add($"placement is for frame '{art.Frame}', not '{frameId}'");
        }

        var placed = art.Sockets.Select(socket => socket.Id).ToHashSet();

        foreach (var id in socketIds)
        {
            if (!placed.Contains(id))
            {
                warnings.Add($"socket '{id}' has no placement");
            }
        }

        var known = socketIds.ToHashSet();

        foreach (var socket in art.Sockets)
        {
            if (!known.Contains(socket.Id))
            {
                warnings.Add($"socket '{socket.Id}' is not on frame '{frameId}'");
            }
        }

        return warnings;
    }

    private static FrameArtLoadResult Failed(string warning) => new(null, new[] { warning });

    private static string? Required(string? value, string name, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        warnings.Add($"placement names no {name}");
        return null;
    }

    private static (int W, int H)? ReadCanvas(SizeDto? dto, List<string> warnings)
    {
        if (dto is null)
        {
            warnings.Add("placement names no canvas");
            return null;
        }

        var width = Positive(dto.Width, "width", warnings);
        var height = Positive(dto.Height, "height", warnings);

        return width is { } w && height is { } h ? (w, h) : null;
    }

    private static int? Positive(int? value, string name, List<string> warnings)
    {
        if (value is not { } number)
        {
            warnings.Add($"canvas names no {name}");
            return null;
        }

        if (number > 0)
        {
            return number;
        }

        warnings.Add($"canvas {name} must be positive, not {number}");
        return null;
    }

    private static IReadOnlyList<FrameArtSocket> ReadSockets(
        List<SocketDto?>? dtos, (int W, int H)? canvas, List<string> warnings)
    {
        var sockets = new List<FrameArtSocket>();

        if (dtos is null || dtos.Count == 0)
        {
            warnings.Add("placement names no sockets");
            return sockets;
        }

        var seen = new HashSet<string>();

        for (var i = 0; i < dtos.Count; i++)
        {
            if (dtos[i] is not { } dto)
            {
                warnings.Add($"socket {i + 1} is null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                warnings.Add($"socket {i + 1} names no id");
                continue;
            }

            if (!IdPattern.IsMatch(dto.Id))
            {
                warnings.Add($"socket id '{dto.Id}' is not a valid id");
                continue;
            }

            if (!seen.Add(dto.Id))
            {
                warnings.Add($"socket id '{dto.Id}' is placed twice");
                continue;
            }

            var anchor = Point(dto.Anchor, $"socket '{dto.Id}' anchor", warnings);
            var box = Point(dto.Box, $"socket '{dto.Id}' box", warnings);

            if (anchor is not { } a || box is not { } b)
            {
                continue;
            }

            if (canvas is { } c && (a.X < 0 || a.Y < 0 || a.X > c.W || a.Y > c.H))
            {
                warnings.Add($"socket '{dto.Id}' anchor ({a.X}, {a.Y}) lies outside the {c.W}x{c.H} canvas");
                continue;
            }

            sockets.Add(new FrameArtSocket(dto.Id, a, b));
        }

        return sockets;
    }

    private static (int X, int Y)? Point(PointDto? dto, string what, List<string> warnings)
    {
        if (dto is null)
        {
            warnings.Add($"{what} is missing");
            return null;
        }

        if (dto.X is not { } x)
        {
            warnings.Add($"{what} names no x");
            return null;
        }

        if (dto.Y is not { } y)
        {
            warnings.Add($"{what} names no y");
            return null;
        }

        return (x, y);
    }
}
