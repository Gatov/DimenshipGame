using System.Text;

namespace Dimenship.Core.Content;

/// <summary>
/// Reads a content tree from a directory on disk. The replay harness reads the tree named on its
/// command line through it, and every test project reads the shipped tree copied beside its
/// binary. The game does not: an exported build has no <c>res://</c> directory to point this at,
/// which is why <c>GodotContentFileSystem</c> exists, and the loader behind both is the same,
/// which is what makes a harness number comparable with the game.
/// <para>
/// It lives beside <see cref="IContentFileSystem"/> because the harness and the kernel tests each
/// carried a copy, and a third would have arrived with the next test project. It reads files and
/// names no engine, so the kernel's rule against a Godot reference is untouched.
/// </para>
/// </summary>
public sealed class DirectoryContentFileSystem(string root) : IContentFileSystem
{
    public string ReadAllText(string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath), Encoding.UTF8);

    public bool Exists(string relativePath) => File.Exists(Path.Combine(root, relativePath));
}
