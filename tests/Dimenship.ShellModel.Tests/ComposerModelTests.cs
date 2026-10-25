using Dimenship.Core.Planning.Draft;
using NUnit.Framework;

namespace Dimenship.ShellModel.Tests;

/// <summary>
/// The composer's rules, each of which has been broken and fixed before: UX-01's draft lost to a
/// view change, the second APPROVE that committed one order twice, and the build target left
/// pointing past the end of a list that shrank when a slot commissioned. Before R5 these could be
/// exercised only by booting Godot.
/// </summary>
public class ComposerModelTests
{
    [Test]
    public void AnApprovedOrder_IsNotApprovedAgain_WhenTheNextSnapshotRecomposesIt()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.Refresh(vessel.Engine.Snapshot);

        Assert.That(model.Approve(), Is.InstanceOf<PlanApprovalCommitted>());
        Assert.That(vessel.PlanCount, Is.EqualTo(1));

        // The next snapshot composes a fresh candidate for the same slot, with tasks. Without the
        // lock, that candidate was what a second press committed.
        vessel.Tick();
        Assert.That(model.Draft, Is.Not.Null);
        Assert.That(model.Draft!.Flatten().Tasks, Is.Not.Empty);
        Assert.That(model.ApproveLocked, Is.True);

        Assert.That(model.Approve(), Is.Null);
        Assert.That(vessel.PlanCount, Is.EqualTo(1));
    }

    [Test]
    public void AfterACommit_ReAdjustKeepsApproveLocked_ButAChangeOfSubstanceUnlocksIt()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.Refresh(vessel.Engine.Snapshot);
        model.Approve();
        vessel.Tick();

        model.ApplyEdit(new ReAdjust());
        Assert.That(model.ApproveLocked, Is.True, "a validation pass reopened APPROVE");

        model.ApplyEdit(new UnlockAll());
        Assert.That(model.ApproveLocked, Is.False);
    }

    [Test]
    public void AParkedComposer_ComesBackWithItsDraftAndUndo_AndIsRefreshedRatherThanRecomposed()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        vessel.OrderComponents(model, units: 5);
        var produceIndex = model.ProduceIndex;
        var step = model.Draft!.Steps[^1];
        model.LiveQuantityEdit(step.Id, 7 * 1000);
        model.CommitQuantityEdit();
        Assert.That(model.CanUndo, "fixture: the edit made an undo entry");
        var parked = model.ToState();

        var remounted = vessel.Remount();
        remounted.FromState(parked);
        vessel.Tick(remounted);

        Assert.That(remounted.BuildMode, Is.False);
        Assert.That(remounted.ProduceIndex, Is.EqualTo(produceIndex));
        Assert.That(remounted.QuantityUnits, Is.EqualTo(5));
        Assert.That(remounted.CanUndo, Is.True, "the first snapshot recomposed over the restored draft");
        Assert.That(remounted.Draft, Is.Not.Null);
        Assert.That(remounted.Draft!.Goal, Is.EqualTo(parked.Draft!.Goal));
    }

    [Test]
    public void AQuantityBeingTyped_IsKept_WhenTheComposerIsParked()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        vessel.OrderComponents(model);
        var step = model.Draft!.Steps[^1];

        model.LiveQuantityEdit(step.Id, 9 * 1000);
        Assume.That(model.QuantityEditInProgress);
        var parked = model.ToState();

        Assert.That(model.QuantityEditInProgress, Is.False);
        Assert.That(parked.Undo, Has.Count.EqualTo(1), "the typed quantity was not committed as one undo entry");
        Assert.That(parked.Draft, Is.SameAs(model.Draft));
    }

    [Test]
    public void WhenTheSelectedSlotCommissions_TheSelectionFallsBackToTheFirst_AndApproveUnlocks()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.Refresh(vessel.Engine.Snapshot);
        var last = model.VisibleBuildTargets.Count - 1;
        var leaving = model.VisibleBuildTargets[last].Facility;
        model.SelectBuildTarget(last);
        model.Refresh(vessel.Engine.Snapshot, freshCompose: true);
        model.Approve();
        Assume.That(model.ApproveLocked);

        vessel.Commission(leaving);
        var changed = vessel.Tick();

        Assert.That(changed, Is.True, "the view was not told to rebuild its picker");
        Assert.That(model.VisibleBuildTargets.Select(t => t.Facility), Has.No.Member(leaving));
        Assert.That(model.BuildIndex, Is.Zero);
        Assert.That(model.SelectedBuildTarget!.Value.Facility, Is.EqualTo(model.VisibleBuildTargets[0].Facility));
        Assert.That(model.ApproveLocked, Is.False, "APPROVE stayed locked for a slot the player never ordered");
        Assert.That(model.Draft!.AssemblyTarget, Is.EqualTo(model.VisibleBuildTargets[0].Facility),
            "the draft was not recomposed for the slot now selected");
    }

    [Test]
    public void WhenAnotherSlotCommissions_TheSelectionFollowsItsFacility_AndTheLockHolds()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.Refresh(vessel.Engine.Snapshot);
        var last = model.VisibleBuildTargets.Count - 1;
        var kept = model.VisibleBuildTargets[last].Facility;
        model.SelectBuildTarget(last);
        model.Refresh(vessel.Engine.Snapshot, freshCompose: true);
        model.Approve();

        vessel.Commission(model.VisibleBuildTargets[0].Facility);
        vessel.Tick();

        Assert.That(model.BuildIndex, Is.EqualTo(last - 1), "the index was not moved to follow its facility");
        Assert.That(model.SelectedBuildTarget!.Value.Facility, Is.EqualTo(kept));
        Assert.That(model.ApproveLocked, Is.True);
    }

    [Test]
    public void ASlotCommissioning_DoesNotUnlockAProduceOrderJustApproved()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.Refresh(vessel.Engine.Snapshot);
        var first = model.VisibleBuildTargets[0].Facility;
        vessel.OrderComponents(model);
        Assert.That(model.Approve(), Is.InstanceOf<PlanApprovalCommitted>());

        vessel.Commission(first);
        vessel.Tick();

        Assert.That(model.ApproveLocked, Is.True, "a slot elsewhere reopened APPROVE on a committed order");
        Assert.That(model.Approve(), Is.Null);
        Assert.That(vessel.PlanCount, Is.EqualTo(1));
    }

    [Test]
    public void AnIndexPastTheEndOfTheList_NamesTheLastTarget_RatherThanFaulting()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.Refresh(vessel.Engine.Snapshot);

        model.SelectBuildTarget(model.VisibleBuildTargets.Count + 3);

        Assert.That(model.SelectedBuildTarget!.Value.Facility, Is.EqualTo(model.VisibleBuildTargets[^1].Facility));
        Assert.DoesNotThrow(() => model.Refresh(vessel.Engine.Snapshot, freshCompose: true));
    }

    [Test]
    public void Discard_ResetsBothModes_AndUnlocks()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        model.SelectBuildTarget(2);
        model.SelectProduceTarget(1);
        model.SetQuantity(12);
        model.Refresh(vessel.Engine.Snapshot, freshCompose: true);
        model.Approve();
        var cleared = 0;
        model.RevisionsCleared += () => cleared++;

        model.Discard();

        Assert.That((model.BuildIndex, model.ProduceIndex, model.QuantityUnits), Is.EqualTo((0, 0, 1L)));
        Assert.That(model.Draft, Is.Null);
        Assert.That(model.ApproveLocked, Is.False);
        Assert.That(cleared, Is.EqualTo(1), "the view was not told to drop its flash and add-move row");
    }

    [Test]
    public void UndoAndRedo_StepThroughEdits_AndAnEditDropsTheRedo()
    {
        var vessel = new Vessel();
        var model = vessel.Model;
        vessel.OrderComponents(model);
        var original = model.Draft;

        model.ApplyEdit(new UnlockAll());
        var edited = model.Draft;
        Assert.That(model.Undo(), Is.True);
        Assert.That(model.Draft, Is.SameAs(original));
        Assert.That(model.Redo(), Is.True);
        Assert.That(model.Draft, Is.SameAs(edited));

        model.Undo();
        model.ApplyEdit(new ReAdjust());
        Assert.That(model.CanRedo, Is.False);
    }

    [Test]
    public void RedoPressed_WhileAQuantityIsBeingTyped_KeepsTheTypedValue_RatherThanFaulting()
    {
        // REDO takes no focus, so the spin box is still open when it is pressed. Committing the
        // typed value drops the redo stack, which Redo once indexed anyway, and threw.
        var vessel = new Vessel();
        var model = vessel.Model;
        vessel.OrderComponents(model, units: 5);
        model.ApplyEdit(new UnlockAll());
        model.Undo();
        Assert.That(model.CanRedo, Is.True, "fixture");

        model.LiveQuantityEdit(model.Draft!.Steps[^1].Id, 7 * 1000);
        var typed = model.Draft;
        Assert.That(model.CanRedo, Is.False, "a typed change drops the redo once it is committed");

        Assert.That(model.Redo(), Is.False);
        Assert.That(model.Draft, Is.SameAs(typed));
        Assert.That(model.QuantityEditInProgress, Is.False, "the press committed the typed value");
        Assert.That(model.CanUndo, Is.True);
    }

    [Test]
    public void UndoPressed_WhileTheFirstQuantityIsBeingTyped_RevertsToTheComposedDraft()
    {
        // On a fresh draft the stack is empty until the typed value is committed. UNDO once read
        // the stack first, and so stood disabled over a change the player could see.
        var vessel = new Vessel();
        var model = vessel.Model;
        vessel.OrderComponents(model, units: 5);
        var composed = model.Draft;
        Assert.That(model.CanUndo, Is.False, "fixture: nothing to undo yet");

        model.LiveQuantityEdit(composed!.Steps[^1].Id, 7 * 1000);
        Assert.That(model.CanUndo, Is.True);

        Assert.That(model.Undo(), Is.True);
        Assert.That(model.Draft, Is.SameAs(composed));
        Assert.That(model.CanRedo, Is.True);
    }
}
