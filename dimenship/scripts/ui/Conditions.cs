using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The shell's words and colours for why work is not happening. Each surface held its own copy of
/// <see cref="Describe"/> until U3: the node card, the inspector and Operations, three tables that
/// had already fallen behind the enum together. One table means a reason added to the kernel is
/// named once, and named the same on every surface that shows it.
/// </summary>
public static class Conditions
{
    /// <summary>
    /// A reason in words (UX-09). These were the enum names in screaming snake case
    /// (<c>MISSING_INPUT</c>, <c>MATERIAL_CLAIMED</c>), which read as internal identifiers at the
    /// one moment a player is trying to understand why their factory stopped. The words are the
    /// player's: a claim is stock held for another plan, a safety lock is an order on hold.
    /// </summary>
    public static string Describe(PostponeReason? reason) => reason switch
    {
        PostponeReason.InsufficientInputMaterial => "MISSING INPUT",
        PostponeReason.InsufficientSourceMaterial => "NOTHING TO PICK UP AT THE SOURCE",
        PostponeReason.DestinationFull => "DESTINATION FULL",
        PostponeReason.InsufficientEnergy => "NOT ENOUGH POWER",
        PostponeReason.OutputRouteUnavailable => "NO LINE FOR ITS OUTPUT",
        PostponeReason.SafetyLock => "ORDER ON HOLD",
        PostponeReason.Outranked => "HIGHER-PRIORITY WORK FIRST",
        PostponeReason.MaterialClaimed => "INPUT HELD FOR ANOTHER PLAN",
        PostponeReason.PrerequisiteMissing => "PREREQUISITE MISSING",
        PostponeReason.RouteUnsafe => "ROUTE UNSAFE",
        PostponeReason.InsufficientFuel => "NOT ENOUGH FUEL",
        PostponeReason.ComputeDeferred => "WAITING FOR COMPUTE",
        PostponeReason.ConditionNotMet => "START CONDITION NOT MET",
        _ => "UNKNOWN",
    };

    /// <summary>
    /// The colour a postponement with this reason takes wherever it is shown alone, on a task row
    /// or a log line: the colour <see cref="Standing"/> gives a facility in the standing that reason
    /// puts it in, read through <see cref="UtilizationWindow.CategoryOf"/> as
    /// <see cref="ExecutorCondition"/> reads it (UX-06). Every surface once chose its own, and the
    /// log and the inspector's task rows chose red, so a task one tick short of ore read as a fault
    /// beside a card that correctly said waiting. No reason is ordinary text: there is nothing to
    /// colour.
    /// </summary>
    public static Color Colour(PostponeReason? reason) =>
        reason is null
            ? ShellPalette.TextPrimary
            : UtilizationWindow.CategoryOf(ExecutorStatus.AllQueuedTasksBlocked, reason) switch
            {
                UtilizationCategory.WaitingOutput => ShellPalette.StateFault,
                UtilizationCategory.WaitingInput or UtilizationCategory.Throttled => ShellPalette.StateWarn,
                _ => ShellPalette.TextPrimary,
            };

    /// <summary>
    /// A production facility's standing (U3) as a word, a colour and a status glyph. Only
    /// <see cref="ExecutorStanding.Blocked"/> takes the fault colour: a factory waiting for input
    /// it has asked for is not broken, and painting it the colour of one that is teaches the player
    /// to stop reading the colour.
    /// </summary>
    public static (string Text, Color Color, string Glyph) Standing(ExecutorCondition condition) =>
        condition.Standing switch
        {
            ExecutorStanding.Working => ("Production", ShellPalette.StateOk, "active"),
            ExecutorStanding.ChangingOver =>
                ($"Changeover · {condition.ChangeoverTicksRemaining}T", ShellPalette.StateWarn, "time"),
            ExecutorStanding.Waiting =>
                ($"Waiting — {Describe(condition.Reason)}", ShellPalette.StateWarn, "queue"),
            ExecutorStanding.Throttled =>
                ($"Throttled — {Describe(condition.Reason)}", ShellPalette.StateWarn, "energy"),
            ExecutorStanding.Held =>
                ($"Held — {Describe(condition.Reason)}", ShellPalette.TextPrimary, "idle"),
            ExecutorStanding.Blocked =>
                ($"Blocked — {Describe(condition.Reason)}", ShellPalette.StateFault, "blocked"),
            ExecutorStanding.Unbuilt => ("Not built", ShellPalette.TextDim, "idle"),
            _ => ("Idle", ShellPalette.TextDim, "idle"),
        };

    /// <summary>
    /// The setup line: what the facility is set up for, and while a changeover runs, what it is
    /// loading. <c>Configured</c> holds until the changeover completes, so the arrow is what tells
    /// the player the old setup is on its way out.
    /// </summary>
    public static string Setup(ExecutorCondition condition)
    {
        var current = condition.Setup?.Value.ToUpperInvariant() ?? "UNCONFIGURED";
        return condition.SwitchingTo is { } next ? $"{current} → {next.Value.ToUpperInvariant()}" : current;
    }
}
