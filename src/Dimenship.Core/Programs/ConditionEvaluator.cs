using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Programs;

/// <summary>
/// Resolves a condition against live vessel state. Lives beside the records rather than inside
/// the engine so the evaluator can be unit-tested without constructing a full tick loop, and so a
/// later program runtime reuses the same answer without going through selection.
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>
    /// True when every condition holds. An empty list is vacuously true — attempt every tick,
    /// which is what keeps empty-condition tasks byte-identical to today's unconditioned ones.
    /// </summary>
    public static bool AllMet(IReadOnlyList<Condition> conditions, WorldState state) =>
        conditions.All(c => Met(c, state));

    /// <summary>True when one condition holds against live state.</summary>
    public static bool Met(Condition condition, WorldState state)
    {
        var left = Read(condition.Kind, condition.Operands, state);
        var right = Resolve(condition.Value, state);
        if (left is null || right is null)
        {
            // A zero-denominator fullness is the programming-view rule for "no statement is true";
            // a missing target at evaluation time is the same kind of unanswerable question. False,
            // not an exception: selection postpones rather than faulting the vessel.
            return false;
        }

        return Compare(left.Value, condition.Op, right.Value);
    }

    private static long? Read(ConditionKind kind, IReadOnlyList<Operand> operands, WorldState state) =>
        kind switch
        {
            ConditionKind.StorageItemAmount => ReadStorageItemAmount(operands, state),
            ConditionKind.ExecutorStatus => ReadExecutorStatus(operands, state),
            _ => null,
        };

    private static long? ReadStorageItemAmount(IReadOnlyList<Operand> operands, WorldState state)
    {
        if (operands.Count != 2
            || operands[0] is not TargetRef { Kind: TargetKind.Storage } storageRef
            || operands[1] is not TargetRef { Kind: TargetKind.Item } itemRef)
        {
            return null;
        }

        var storage = new StorageId(storageRef.Id);
        var item = new ItemId(itemRef.Id);
        foreach (var candidate in state.Vessel.Storages)
        {
            if (candidate.Id != storage)
            {
                continue;
            }

            foreach (var held in candidate.Stock)
            {
                if (held.Item == item)
                {
                    return held.Amount;
                }
            }

            return 0;
        }

        return null;
    }

    private static long? ReadExecutorStatus(IReadOnlyList<Operand> operands, WorldState state)
    {
        if (operands.Count != 1 || operands[0] is not TargetRef { Kind: TargetKind.Executor } target)
        {
            return null;
        }

        var id = new ExecutorId(target.Id);
        foreach (var facility in state.Vessel.Facilities)
        {
            if (facility.Id == id)
            {
                return (long)facility.Status;
            }
        }

        foreach (var line in state.Vessel.Transports)
        {
            if (line.Id == id)
            {
                return (long)line.Status;
            }
        }

        return null;
    }

    private static long? Resolve(Operand operand, WorldState state) =>
        operand switch
        {
            Literal literal => literal.Value,
            EnumRef enumeration => enumeration.Value,
            // ParameterRef has no binding outside a program; TargetRef is a left-hand shape.
            // Either on the right is unanswerable here.
            _ => null,
        };

    private static bool Compare(long left, Comparison op, long right) =>
        op switch
        {
            Comparison.LessThan => left < right,
            Comparison.LessOrEqual => left <= right,
            Comparison.Equal => left == right,
            Comparison.NotEqual => left != right,
            Comparison.GreaterOrEqual => left >= right,
            Comparison.GreaterThan => left > right,
            _ => false,
        };
}
