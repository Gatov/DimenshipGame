using NUnit.Framework;

namespace Dimenship.ShellModel.Tests;

/// <summary>
/// The rule that makes this project worth having, checked the way CoreAssemblyTests checks the
/// kernel's: a model that reached for a Godot type would need an engine to test again, and nothing
/// else would notice until the suite stopped running outside one.
/// </summary>
public class ShellModelAssemblyTests
{
    [Test]
    public void TheShellModel_ReferencesNoGodotAssembly()
    {
        var references = typeof(ComposerModel).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(references, Is.Empty);
    }
}
