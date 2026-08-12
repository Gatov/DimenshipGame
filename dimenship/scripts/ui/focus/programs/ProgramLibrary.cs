using System;
using System.Collections.Generic;
using Dimenship.Core.Simulation;

namespace Dimenship.Ui;

/// <summary>
/// The programs the concept mock opens with. Every one of them commands only what
/// <see cref="WorldDefinition.CreateDefault"/> contains, and between them they put every shape the
/// ladder can draw on screen at once: a three-level nest, an <c>ELSE IF</c>, an <c>ELSE</c>, a
/// two-block rule, and a rule with no condition at all.
/// <para>
/// None of them run. They exist so the editor has something to be judged against that is not an
/// empty canvas — an editor is easy to like when there is nothing in it.
/// </para>
/// </summary>
public static class ProgramLibrary
{
    public static List<ProgramDraft> Create() => new()
    {
        MetalsRecovery(),
        ReactorBufferGuard(),
        ExtractorIdleWatch(),
    };

    /// <summary>An empty program, for <c>+ NEW PROGRAM</c>. One rule, so the canvas is never blank.</summary>
    public static ProgramDraft Empty(int ordinal) => new()
    {
        Name = $"Untitled Program {ordinal}",
        Description = "No description yet.",
        Scope = "Vessel",
        Tier = "Basic",
        Reliability = "Experimental",
        Version = "v0.1",
        RuleLimit = 10,
        Rules = { new RuleDraft() },
    };

    /// <summary>
    /// The concept image's own program, rewritten against the vessel that exists: Basic Metals
    /// instead of refined alloy, Matter Reactor Alpha instead of Refinery Alpha. Its first rule nests
    /// three levels deep,
    /// which is what the indent rules and the nested drop targets have to be judged on.
    /// </summary>
    private static ProgramDraft MetalsRecovery()
    {
        var shortage = new RuleDraft(
            Condition(
                ConditionKind.VesselItemAmount,
                WorldDefinition.BasicMetals.Value,
                nameof(Comparison.LessThan),
                100_000L))
        {
            Priority = 80,
            CooldownTicks = 30,
        };

        var ladder = new BranchDraft(
            Condition(
                ConditionKind.StorageItemAmount,
                WorldDefinition.ReactorABuffer.Value,
                WorldDefinition.MatterMix.Value,
                nameof(Comparison.LessThan),
                20_000L));

        // Level three: the reactor's feed line is the only thing that can fix a starved buffer, so
        // releasing its hold is nested under the shortage that makes it matter.
        var release = new BranchDraft(
            Condition(
                ConditionKind.ExecutorStatusIs,
                WorldDefinition.ReactorAFeed.Value,
                nameof(ExecutorStatus.AllQueuedTasksBlocked)));
        release.Arms[0].Then.Add(
            Command(ActionKind.Hold, WorldDefinition.ReactorAFeed.Value, "off"));

        ladder.Arms[0].Then.Add(
            Command(
                ActionKind.Produce,
                WorldDefinition.ExtractHydrogen.Value,
                WorldDefinition.Extractor01.Value,
                20L));
        ladder.Arms[0].Then.Add(release);

        ladder.Arms.Add(new BranchArmDraft(
            Condition(
                ConditionKind.ExecutorStatusIs,
                WorldDefinition.ReactorA.Value,
                nameof(ExecutorStatus.NoTasksQueued))));
        ladder.Arms[1].Then.Add(
            Command(
                ActionKind.Produce,
                WorldDefinition.SeparateBasic.Value,
                WorldDefinition.ReactorA.Value,
                5L));

        ladder.Otherwise = new List<StatementDraft>
        {
            Command(ActionKind.SetPriority, WorldDefinition.ReactorA.Value, 80L),
        };

        shortage.Then.Add(ladder);

        var surplus = new RuleDraft(
            Condition(
                ConditionKind.VesselItemAmount,
                WorldDefinition.BasicMetals.Value,
                nameof(Comparison.GreaterThan),
                400_000L))
        {
            Priority = 40,
        };
        surplus.Then.Add(
            Command(ActionKind.SetPriority, WorldDefinition.ReactorA.Value, 20L));
        surplus.Then.Add(
            Command(
                ActionKind.Reserve,
                25_000L,
                WorldDefinition.BasicMetals.Value,
                WorldDefinition.ResourceStorage.Value));

        return new ProgramDraft
        {
            Name = "Basic Metals Recovery",
            Description =
                "Keeps Basic Metals available for production by pushing Matter Mix to the reactor " +
                "buffer during a shortage and standing the reactor back down once the hold is full.",
            Scope = "Matter Reactor Alpha, Reactor Alpha Feed",
            Tier = "Industrial",
            Reliability = "Verified",
            Version = "v1.3",
            RuleLimit = 10,
            Rules = { shortage, surplus },
        };
    }

    /// <summary>Two blocks per rule. The simplest thing the ladder can be asked to read.</summary>
    private static ProgramDraft ReactorBufferGuard()
    {
        var full = new RuleDraft(
            Condition(
                ConditionKind.StorageFillPercent,
                WorldDefinition.ReactorABuffer.Value,
                nameof(Comparison.GreaterThan),
                90L))
        {
            Priority = 60,
        };
        full.Then.Add(Command(ActionKind.Hold, WorldDefinition.ReactorAFeed.Value, "on"));

        var drained = new RuleDraft(
            Condition(
                ConditionKind.StorageFillPercent,
                WorldDefinition.ReactorABuffer.Value,
                nameof(Comparison.LessThan),
                60L))
        {
            Priority = 60,
        };
        drained.Then.Add(Command(ActionKind.Hold, WorldDefinition.ReactorAFeed.Value, "off"));

        return new ProgramDraft
        {
            Name = "Reactor Buffer Guard",
            Description =
                "Stops the feed line before the reactor buffer overflows, and releases it once " +
                "the reactor has drawn the buffer back down.",
            Scope = "Reactor Alpha Feed",
            Tier = "Basic",
            Reliability = "Verified",
            Version = "v1.0",
            RuleLimit = 6,
            Rules = { full, drained },
        };
    }

    /// <summary>
    /// Opens with an unconditional rule — a null condition, which the spec makes the honest
    /// expression of a standing instruction rather than a condition that is always true.
    /// </summary>
    private static ProgramDraft ExtractorIdleWatch()
    {
        var standing = new RuleDraft { Priority = 10 };
        standing.Then.Add(
            Command(ActionKind.SetPriority, WorldDefinition.Extractor01.Value, 50L));

        var idle = new RuleDraft(
            Condition(
                ConditionKind.ExecutorStatusIs,
                WorldDefinition.Extractor01.Value,
                nameof(ExecutorStatus.NoTasksQueued)))
        {
            Priority = 70,
            CooldownTicks = 60,
        };
        idle.Then.Add(
            Command(
                ActionKind.Produce,
                WorldDefinition.ExtractHydrogen.Value,
                WorldDefinition.Extractor01.Value,
                50L));

        return new ProgramDraft
        {
            Name = "Extractor Idle Watch",
            Description = "Refills the extractor's queue rather than letting it run dry.",
            Scope = "Extractor 01",
            Tier = "Basic",
            Reliability = "Experimental",
            Version = "v0.4",
            RuleLimit = 6,
            Rules = { standing, idle },
        };
    }

    /// <summary>
    /// Builds from the vocabulary's own defaults, then overwrites slot by slot. Going through
    /// <see cref="ProgramVocabulary.NewCondition"/> is what guarantees a seeded block has exactly
    /// the slots its kind declares — a hand-built list would drift the first time a kind gains one.
    /// </summary>
    private static ConditionDraft Condition(ConditionKind kind, params object[] values)
    {
        var condition = ProgramVocabulary.NewCondition(kind);
        Fill(condition.Slots, values, kind.ToString());
        return condition;
    }

    private static CommandDraft Command(ActionKind kind, params object[] values)
    {
        var command = ProgramVocabulary.NewCommand(kind);
        Fill(command.Slots, values, kind.ToString());
        return command;
    }

    private static void Fill(List<SlotValue> slots, IReadOnlyList<object> values, string what)
    {
        if (slots.Count != values.Count)
        {
            throw new ArgumentException(
                $"'{what}' declares {slots.Count} slots and the library supplied {values.Count}.",
                nameof(values));
        }

        for (var i = 0; i < slots.Count; i++)
        {
            switch (values[i])
            {
                case string id:
                    slots[i].Id = id;
                    break;

                case long number:
                    slots[i].Number = number;
                    break;

                default:
                    throw new ArgumentException(
                        $"'{what}' slot {i} takes a string id or a long, not " +
                        $"{values[i].GetType().Name}.",
                        nameof(values));
            }
        }
    }
}
