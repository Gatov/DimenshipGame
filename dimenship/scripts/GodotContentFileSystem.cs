using Dimenship.Core.Content;
using Godot;

namespace Dimenship;

/// <summary>
/// Reads the content tree out of <c>res://content</c>.
/// <para>
/// It exists here rather than in <c>Dimenship.Core</c> because the kernel names no Godot type, and
/// this is the one place that has to: an exported build has no filesystem to walk — its content is
/// packed into the archive, and <see cref="FileAccess"/> is what reads out of one. A loader that
/// used <c>System.IO</c> would work in the editor and fail only in an export, which is the worst
/// place to find out.
/// </para>
/// <para>
/// The tree lives under the Godot project directory for the same reason: <c>res://</c> is that
/// directory, and content outside it is content the export never sees.
/// </para>
/// </summary>
public sealed class GodotContentFileSystem : IContentFileSystem
{
    public const string Root = "res://content";

    public string ReadAllText(string relativePath)
    {
        var path = Path(relativePath);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new System.IO.IOException($"Cannot read '{path}': {FileAccess.GetOpenError()}.");
        }

        return file.GetAsText();
    }

    public bool Exists(string relativePath) => FileAccess.FileExists(Path(relativePath));

    private static string Path(string relativePath) => $"{Root}/{relativePath}";
}
