using NUnit.Framework;

namespace Dimenship.Core.Tests;

public class GameInfoTests
{
    [Test]
    public void Title_IsTheGameName()
    {
        Assert.That(GameInfo.Title, Is.EqualTo("Dimenship"));
    }

    [Test]
    public void DisplayVersion_PrefixesVersionWithV()
    {
        Assert.That(GameInfo.DisplayVersion, Is.EqualTo("v0.1.0"));
    }
}
