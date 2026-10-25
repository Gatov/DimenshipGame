using NUnit.Framework;

namespace Dimenship.ShellModel.Tests;

public class ConfirmGateTests
{
    [Test]
    public void TheFirstPressArms_AndOnlyTheSecondConfirms()
    {
        var gate = new ConfirmGate();

        Assert.That(gate.Press(), Is.False);
        Assert.That(gate.Armed, Is.True);
        Assert.That(gate.Press(), Is.True);
        Assert.That(gate.Armed, Is.False, "a confirmed gate stayed armed for a third press");
    }

    [Test]
    public void AnythingInBetween_Disarms_SoTheNextPressArmsAgain()
    {
        var gate = new ConfirmGate();
        gate.Press();

        gate.Disarm();

        Assert.That(gate.Press(), Is.False);
    }
}
