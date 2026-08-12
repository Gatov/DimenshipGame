namespace Dimenship.Core.Content;

/// <summary>
/// Where content files come from. The Godot layer supplies a <c>res://</c>-backed implementation
/// so exported builds read packed content, and <c>Dimenship.Core</c> still names no Godot type.
/// Tests supply a dictionary — no test needs a file on disk.
/// </summary>
public interface IContentFileSystem
{
    string ReadAllText(string relativePath);

    bool Exists(string relativePath);
}

/// <summary>Somewhere a catalog and its scenarios can be loaded from.</summary>
public interface IContentSource
{
    ContentLoadResult Load();
}

/// <summary>
/// One thing wrong with the content, located well enough to fix without guessing.
/// <see cref="Path"/> is the position within the file — an array index and a field — and is empty
/// when the problem is the file itself.
/// </summary>
public sealed record ContentError(string File, string Path, string Message)
{
    public override string ToString() =>
        Path.Length == 0 ? $"{File}: {Message}" : $"{File} {Path}: {Message}";
}

/// <summary>
/// What a load produced. <see cref="Catalog"/> and <see cref="Scenarios"/> are null and empty when
/// anything failed: a partially linked catalog is worse than none, because everything downstream
/// would treat it as complete.
/// <para>
/// Errors are <b>collected, not thrown on the first one</b>. A content author fixing eleven
/// dangling item ids should see eleven messages, not eleven runs.
/// </para>
/// </summary>
public sealed record ContentLoadResult(
    ContentCatalog? Catalog,
    IReadOnlyList<Scenario> Scenarios,
    IReadOnlyList<ContentError> Errors)
{
    public bool Succeeded => Errors.Count == 0 && Catalog is not null;

    public static ContentLoadResult Failed(IReadOnlyList<ContentError> errors) =>
        new(null, Array.Empty<Scenario>(), errors);
}
