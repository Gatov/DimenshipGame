using NUnit.Framework;

namespace Dimenship.Core.Tests;

public class GameInfoTests
{
    [Test]
    public void DisplayVersion_PrefixesVersionWithV()
    {
        Assert.That(GameInfo.DisplayVersion, Is.EqualTo($"v{GameInfo.Version}"));
    }
}
