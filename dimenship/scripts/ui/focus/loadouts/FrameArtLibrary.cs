using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One frame's art as loaded: its placement and SVG source when both are good, or the warnings
/// saying why not. <see cref="Art"/> and <see cref="Svg"/> are null exactly when there are warnings.
/// </summary>
public sealed record FrameArtEntry(FrameArt? Art, string? Svg, IReadOnlyList<string> Warnings);

/// <summary>
/// Loads each frame's placement sidecar and SVG from <c>res://assets/loadouts/frames/</c>, named by
/// frame id, and rasterises the SVG at the size the stage draws it.
/// <para>
/// This is the Godot half of the sidecar's validation: <see cref="FrameArtSerializer"/> checks what
/// a file can get wrong on its own, and this adds what needs the catalog or the file system — the
/// socket ids against <see cref="FrameDef"/>, and whether the named artwork exists. Any warning
/// means no art for that frame; the stage falls back to a plain column and says why in words
/// (<c>2026-09-14-glass-console-loadout-editor-design.md</c>, Decision 6). Warnings are pushed once
/// per frame per process, because the result is cached and a broken file does not mend itself.
/// </para>
/// <para>
/// The SVG is rasterised at runtime rather than imported at a fixed scale, rounded up to a quarter
/// step and cached per step. A fixed import either blurs thin lines when the zone is enlarged or
/// spends memory at every size, and thin lines are the whole of this art.
/// </para>
/// </summary>
public static class FrameArtLibrary
{
    private const string Root = "res://assets/loadouts/frames";

    /// <summary>Rasterise in quarter-scale steps, so a window drag does not re-render every pixel it passes.</summary>
    private const int StepPermille = 250;

    private static readonly Dictionary<string, FrameArtEntry> Entries = new();
    private static readonly Dictionary<(string Frame, int Step), ImageTexture?> Textures = new();

    public static FrameArtEntry For(FrameDef frame)
    {
        if (!Entries.TryGetValue(frame.Id, out var entry))
        {
            entry = Load(frame);
            Entries[frame.Id] = entry;
        }

        return entry;
    }

    /// <summary>
    /// The frame's drawing rendered at least as large as <paramref name="scalePermille"/> of its
    /// canvas. Null when the frame has no good art, when the scale is zero, or when the SVG will not
    /// rasterise — which is warned once and then remembered.
    /// </summary>
    public static ImageTexture? Rasterise(FrameDef frame, int scalePermille)
    {
        var entry = For(frame);

        if (entry.Svg is null || scalePermille <= 0)
        {
            return null;
        }

        var step = (scalePermille + StepPermille - 1) / StepPermille;

        if (Textures.TryGetValue((frame.Id, step), out var cached))
        {
            return cached;
        }

        var image = new Image();
        var error = image.LoadSvgFromString(entry.Svg, step * StepPermille / 1000f);
        ImageTexture? texture = null;

        if (error == Error.Ok)
        {
            texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            GD.PushWarning($"Frame art '{frame.Id}' could not be rasterised: {error}.");
        }

        Textures[(frame.Id, step)] = texture;
        return texture;
    }

    private static FrameArtEntry Load(FrameDef frame)
    {
        var file = $"{frame.Id}.json";
        var path = $"{Root}/{file}";
        var warnings = new List<string>();
        FrameArt? art = null;
        string? svg = null;

        if (!FileAccess.FileExists(path))
        {
            warnings.Add("no placement file");
        }
        else
        {
            var result = FrameArtSerializer.Load(FileAccess.GetFileAsString(path));
            warnings.AddRange(result.Warnings);

            if (result.Art is { } parsed)
            {
                warnings.AddRange(FrameArtSerializer.Check(
                    parsed, frame.Id, frame.Sockets.Select(socket => socket.Id).ToList()));

                var artwork = $"{Root}/{parsed.Artwork}";

                if (FileAccess.FileExists(artwork))
                {
                    svg = FileAccess.GetFileAsString(artwork);
                }
                else
                {
                    warnings.Add($"artwork '{parsed.Artwork}' is missing");
                }

                art = parsed;
            }
        }

        if (warnings.Count == 0)
        {
            return new FrameArtEntry(art, svg, warnings);
        }

        var named = warnings.Select(warning => $"{file}: {warning}").ToList();

        foreach (var warning in named)
        {
            GD.PushWarning($"Frame art: {warning}");
        }

        return new FrameArtEntry(null, null, named);
    }
}
