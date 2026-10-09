using System.Text;
using Dimenship.Core.Content;

namespace Dimenship.Replay;

/// <summary>
/// Reads a content tree from a directory given on the command line. The game reads through
/// <c>GodotContentFileSystem</c> and the tests through their own copy of this; the loader behind
/// all three is the same, which is what makes a harness number comparable with the game.
/// </summary>
public sealed class DirectoryContentFileSystem : IContentFileSystem
{
    private readonly string _root;

    public DirectoryContentFileSystem(string root) => _root = root;

    public string ReadAllText(string relativePath) =>
        File.ReadAllText(Path.Combine(_root, relativePath), Encoding.UTF8);

    public bool Exists(string relativePath) => File.Exists(Path.Combine(_root, relativePath));
}
