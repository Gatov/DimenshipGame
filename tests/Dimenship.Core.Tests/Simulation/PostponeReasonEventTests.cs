using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// The table the engine journals postponements by, and the event log reads back to colour them.
/// </summary>
public class PostponeReasonEventTests
{
    /// <summary>Reasons that belong to systems not built yet, and are never postponed for.</summary>
    private static readonly PostponeReason[] Unjournalled =
    {
        PostponeReason.PrerequisiteMissing, PostponeReason.RouteUnsafe,
        PostponeReason.InsufficientFuel, PostponeReason.ComputeDeferred,
    };

    [Test]
    public void EveryJournalledReason_RoundTripsThroughItsEvent()
    {
        foreach (var reason in Enum.GetValues<PostponeReason>().Except(Unjournalled))
        {
            Assert.That(PostponeReasons.FromEventCode(PostponeReasons.EventCodeOf(reason)), Is.EqualTo(reason), reason.ToString());
        }
    }

    [Test]
    public void EveryPostponeEvent_NamesAReason_AndNoOtherEventDoes()
    {
        // A Postpone* code added to the enum without a row in the table would log in the default
        // colour; one named otherwise would never be read as a postponement at all.
        foreach (var code in Enum.GetValues<EventCode>())
        {
            var isPostpone = code.ToString().StartsWith("Postpone", StringComparison.Ordinal);
            Assert.That(PostponeReasons.FromEventCode(code).HasValue, Is.EqualTo(isPostpone), code.ToString());
        }
    }

    [Test]
    public void AReasonNothingPostponesFor_HasNoEvent()
    {
        foreach (var reason in Unjournalled)
        {
            Assert.That(() => PostponeReasons.EventCodeOf(reason), Throws.TypeOf<ArgumentOutOfRangeException>(), reason.ToString());
        }
    }
}
