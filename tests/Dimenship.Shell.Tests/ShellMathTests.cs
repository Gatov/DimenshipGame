using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class ShellMathTests
{
    [TestCase(0, 10, ExpectedResult = 0f, TestName = "Nothing of something is an empty bar")]
    [TestCase(5, 10, ExpectedResult = 0.5f, TestName = "Half is half")]
    [TestCase(10, 10, ExpectedResult = 1f, TestName = "All of it is a full bar")]
    [TestCase(15, 10, ExpectedResult = 1f, TestName = "More than capacity stops at the end of the track")]
    [TestCase(-3, 10, ExpectedResult = 0f, TestName = "Less than nothing stops at the start")]
    [TestCase(5, 0, ExpectedResult = 0f, TestName = "No capacity is an empty bar rather than a division")]
    [TestCase(5, -1, ExpectedResult = 0f, TestName = "A negative capacity is treated as none")]
    public float Fill_IsTheShareOfTheTrack_Clamped(long amount, long capacity) =>
        ShellMath.Fill(amount, capacity);
}
