using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class FrameArtSerializerTests
{
    /// <summary>
    /// A minimal valid sidecar. Every test but the first copies it and breaks exactly one thing,
    /// so a failure is about the rule under test and not about the fixture.
    /// </summary>
    private const string Valid = """
        {
          "notes": "fixture",
          "frame": "test_frame",
          "artwork": "test_frame.svg",
          "canvas": { "width": 1200, "height": 720 },
          "sockets": [
            { "id": "tool", "anchor": { "x": 330, "y": 160 }, "box": { "x": 170, "y": 160 } },
            { "id": "sensor", "anchor": { "x": 648, "y": 222 }, "box": { "x": 1030, "y": 160 } }
          ]
        }
        """;

    private static FrameArt ValidArt() => FrameArtSerializer.Load(Valid).Art!;

    [Test]
    public void AValidSidecar_Parses_WithNoWarnings()
    {
        var result = FrameArtSerializer.Load(Valid);

        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.Art, Is.Not.Null);
        Assert.That(result.Art!.Frame, Is.EqualTo("test_frame"));
        Assert.That(result.Art.Artwork, Is.EqualTo("test_frame.svg"));
        Assert.That(result.Art.Canvas, Is.EqualTo((1200, 720)));
        Assert.That(result.Art.Sockets, Is.EqualTo(new[]
        {
            new FrameArtSocket("tool", (330, 160), (170, 160)),
            new FrameArtSocket("sensor", (648, 222), (1030, 160)),
        }));
    }

    [Test]
    public void TheSocketOrder_IsTheFilesOrder_NeverSorted()
    {
        var art = ValidArt();

        Assert.That(art.Sockets.Select(socket => socket.Id), Is.EqualTo(new[] { "tool", "sensor" }));
    }

    [Test]
    public void NotesAreOptional_BecauseTheyAreDocumentation_NotPlacement()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"notes\": \"fixture\",", string.Empty));

        Assert.That(result.Warnings, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AnEmptyFile_IsReported_AndYieldsNoArt(string json)
    {
        var result = FrameArtSerializer.Load(json);

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement file is empty" }));
    }

    [Test]
    public void NoFileContentAtAll_IsReportedAsEmpty()
    {
        var result = FrameArtSerializer.Load(null);

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement file is empty" }));
    }

    [Test]
    public void MalformedJson_IsReported_WithoutThrowing()
    {
        var result = FrameArtSerializer.Load("{ \"frame\": ");

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.StartWith("placement file is not valid"));
    }

    [Test]
    public void AMissingField_IsNamed()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"artwork\": \"test_frame.svg\",", string.Empty));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement names no artwork" }));
    }

    [Test]
    public void AMissingSocketCoordinate_IsNamed_WithItsSocket()
    {
        var result = FrameArtSerializer.Load(
            Valid.Replace("\"anchor\": { \"x\": 330, \"y\": 160 }", "\"anchor\": { \"x\": 330 }"));

        Assert.That(result.Warnings, Is.EqualTo(new[] { "socket 'tool' anchor names no y" }));
    }

    [Test]
    public void AnUnknownField_IsRejected_RatherThanIgnored()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"notes\": \"fixture\",", "\"colour\": \"cyan\","));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("colour"));
    }

    [Test]
    public void AFractionalCoordinate_IsReported_NotRounded()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"x\": 330,", "\"x\": 330.5,"));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("$.sockets[0].anchor.x"));
    }

    [Test]
    public void ADuplicateSocketId_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"id\": \"sensor\"", "\"id\": \"tool\""));

        Assert.That(result.Warnings, Is.EqualTo(new[] { "socket id 'tool' is placed twice" }));
    }

    [Test]
    public void ASocketIdOutsideTheCatalogPattern_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"id\": \"sensor\"", "\"id\": \"Sensor-1\""));

        Assert.That(result.Warnings, Is.EqualTo(new[] { "socket id 'Sensor-1' is not a valid id" }));
    }

    [Test]
    public void AnAnchorOutsideTheCanvas_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"x\": 648,", "\"x\": 1300,"));

        Assert.That(result.Warnings, Is.EqualTo(new[]
        {
            "socket 'sensor' anchor (1300, 222) lies outside the 1200x720 canvas",
        }));
    }

    [Test]
    public void ANonPositiveCanvas_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"width\": 1200", "\"width\": 0"));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Does.Contain("canvas width must be positive, not 0"));
    }

    [Test]
    public void NoSockets_IsReported()
    {
        var json = Valid[..Valid.IndexOf("\"sockets\"", StringComparison.Ordinal)] + "\"sockets\": [] }";
        var result = FrameArtSerializer.Load(json);

        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement names no sockets" }));
    }

    [Test]
    public void Check_AMatchingFrame_HasNoWarnings()
    {
        Assert.That(FrameArtSerializer.Check(ValidArt(), "test_frame", new[] { "tool", "sensor" }), Is.Empty);
    }

    [Test]
    public void Check_ASocketTheFrameHasButTheFileDoesNot_IsNamed()
    {
        var warnings = FrameArtSerializer.Check(ValidArt(), "test_frame", new[] { "tool", "sensor", "power" });

        Assert.That(warnings, Is.EqualTo(new[] { "socket 'power' has no placement" }));
    }

    [Test]
    public void Check_ASocketTheFilePlacesButTheFrameLacks_IsNamed()
    {
        var warnings = FrameArtSerializer.Check(ValidArt(), "test_frame", new[] { "tool" });

        Assert.That(warnings, Is.EqualTo(new[] { "socket 'sensor' is not on frame 'test_frame'" }));
    }

    [Test]
    public void Check_APlacementForAnotherFrame_IsNamed()
    {
        var warnings = FrameArtSerializer.Check(ValidArt(), "hauler_frame", new[] { "tool", "sensor" });

        Assert.That(warnings, Is.EqualTo(new[] { "placement is for frame 'test_frame', not 'hauler_frame'" }));
    }
}
