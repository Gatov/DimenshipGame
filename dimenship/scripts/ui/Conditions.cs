using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
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
    public static string Describe(PostponeReason? reason) => reason switch
    {
        PostponeReason.InsufficientInputMaterial => "MISSING_INPUT",
        PostponeReason.InsufficientSourceMaterial => "NO_SOURCE_MATERIAL",
        PostponeReason.DestinationFull => "DESTINATION_FULL",
        PostponeReason.InsufficientEnergy => "INSUFFICIENT_ENERGY",
        PostponeReason.OutputRouteUnavailable => "NO_OUTPUT_ROUTE",
        PostponeReason.SafetyLock => "SAFETY_LOCK",
        PostponeReason.Outranked => "OUTRANKED",
        PostponeReason.MaterialClaimed => "MATERIAL_CLAIMED",
        PostponeReason.PrerequisiteMissing => "PREREQUISITE_MISSING",
        PostponeReason.RouteUnsafe => "ROUTE_UNSAFE",
        PostponeReason.InsufficientFuel => "INSUFFICIENT_FUEL",
        PostponeReason.ComputeDeferred => "COMPUTE_DEFERRED",
        PostponeReason.ConditionNotMet => "CONDITION_NOT_MET",
        _ => "UNKNOWN",
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
