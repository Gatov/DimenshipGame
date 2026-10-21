using Dimenship.Core.Content;
using Dimenship.Core.Programs;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>
    /// Every check <see cref="Enqueue"/> makes, and nothing it changes. Split from the queueing so
    /// <see cref="Commit"/> can check a whole plan before queueing any of it: a refused command must
    /// leave the world as it found it (C0, Decision 2), and a plan whose third task was invalid
    /// used to leave the first two queued with no plan.
    /// </summary>
    private void RequireQueueable(TaskScript script, ExecutorId executor)
    {
        RefuseUnboundOperands(script.Conditions);

        switch (script.Action)
        {
            case Produce produce:
                RequireProducible(script, produce, executor);
                break;
            case Transfer transfer:
                RequireHaulable(script, transfer, executor);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown task action '{script.Action.GetType().Name}'.", nameof(script));
        }
    }

    private void RequireProducible(TaskScript script, Produce produce, ExecutorId executor)
    {
        if (produce.Runs is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(script), produce.Runs, "A task must request at least one run.");
        }

        if (!_facilitiesById.TryGetValue(executor, out var target))
        {
            throw new ArgumentException(WrongKindOrMissing(executor, wantedFacility: true), nameof(executor));
        }

        if (!target.Built)
        {
            throw new ArgumentException(
                $"Executor '{executor}' is unbuilt and cannot be queued on.", nameof(executor));
        }

        var archetype = Archetype(target);
        if (!archetype.Commandable)
        {
            // Same sentence the content loader uses for a scenario task on a passive source —
            // one wording, two seams, so a picker that somehow offers one fails the same way.
            throw new ArgumentException(NotCommandable(executor, archetype), nameof(executor));
        }

        var definition = Catalog.Schematics.Get(produce.Schematic);
        if (!IsUnlocked(produce.Schematic))
        {
            throw new ArgumentException(
                $"Schematic '{produce.Schematic}' is not unlocked.", nameof(script));
        }

        RequireCompatible(definition, target);
    }

    private static string NotCommandable(ExecutorId executor, FacilityArchetype archetype) =>
        $"'{executor}' is a {archetype.Id}, which is not commandable. A passive " +
        "facility runs what it is configured with and is scheduled by nobody.";

    private void RequireHaulable(TaskScript script, Transfer transfer, ExecutorId executor)
    {
        if (transfer.Quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(script), transfer.Quantity, "A transfer must move at least one unit.");
        }

        if (!_linesById.TryGetValue(executor, out var line))
        {
            throw new ArgumentException(WrongKindOrMissing(executor, wantedFacility: false), nameof(executor));
        }

        if (!line.Built)
        {
            throw new ArgumentException(
                $"Transport '{executor}' is unbuilt and cannot be queued on.", nameof(executor));
        }

        RequireKnownItem(transfer.Item);

        if (_storage.Find(transfer.From) is null)
        {
            throw new ArgumentException($"No storage '{transfer.From}'.", nameof(script));
        }

        if (_storage.Find(transfer.To) is null)
        {
            throw new ArgumentException($"No storage '{transfer.To}'.", nameof(script));
        }

        if (transfer.From == transfer.To)
        {
            throw new ArgumentException(
                $"A transfer from '{transfer.From}' to itself would move nothing.", nameof(script));
        }

        // Refused here and never at the belt head (D2 Decision 4): a head whose destination will
        // never accept its cargo freezes the belt for good, and everything else on it with it.
        if (!Acceptance.Accepts(transfer.To, transfer.Item))
        {
            throw new ArgumentException(WorkpieceAcceptance.Refusal(transfer.Item, transfer.To), nameof(script));
        }

        // A line runs a fixed route. Queueing a transfer it could never make would leave a task
        // sitting in a queue that no line aboard can serve, which reads as a stalled vessel rather
        // than as the planning mistake it is.
        if (line.From != transfer.From || line.To != transfer.To)
        {
            throw new ArgumentException(
                $"Transport '{executor}' runs '{line.From}' to '{line.To}', " +
                $"not '{transfer.From}' to '{transfer.To}'.",
                nameof(executor));
        }
    }

    /// <summary>
    /// Refuses a script whose conditions name a parameter or an unknown target. A parameter has
    /// no binding outside a program; an unknown target would postpone forever for a reason nobody
    /// can see. Both are planning mistakes, not runtime stalls.
    /// </summary>
    private void RefuseUnboundOperands(IReadOnlyList<Condition> conditions)
    {
        foreach (var condition in conditions)
        {
            foreach (var operand in condition.Operands.Append(condition.Value))
            {
                switch (operand)
                {
                    case ParameterRef parameter:
                        throw new ArgumentException(
                            $"Condition parameter '{parameter.Name}' has no binding outside a program.",
                            nameof(conditions));
                    case TargetRef target when !TargetExists(target):
                        throw new ArgumentException(
                            $"Unknown {target.Kind.ToString().ToLowerInvariant()} '{target.Id}'.",
                            nameof(conditions));
                }
            }
        }
    }

    /// <summary>
    /// Why an executor could not take a task: it does not exist, or it exists and is the other kind.
    /// <para>
    /// Facilities and lines live in separate indexes, so each entry point misses the other's
    /// executors as if they were not aboard. "No executor 'factory_a_feed'" sends the author looking
    /// for a missing line when what they have is a produce task addressed at one — the action and
    /// the executor disagree, and only the message can say so.
    /// </para>
    /// </summary>
    private string WrongKindOrMissing(ExecutorId executor, bool wantedFacility)
    {
        var otherIndexHasIt = wantedFacility
            ? _linesById.ContainsKey(executor)
            : _facilitiesById.ContainsKey(executor);

        if (!otherIndexHasIt)
        {
            return wantedFacility
                ? $"No executor '{executor}'."
                : $"No transport executor '{executor}'.";
        }

        return wantedFacility
            ? $"Executor '{executor}' is a transport line; a produce task needs a facility."
            : $"Executor '{executor}' is a facility; a transfer needs a transport line.";
    }

    private bool TargetExists(TargetRef target) =>
        target.Kind switch
        {
            TargetKind.Storage => _storage.Find(new StorageId(target.Id)) is not null,
            TargetKind.Item => _storage.Knows(new ItemId(target.Id)),
            TargetKind.Schematic => Catalog.Schematics.TryGet(new SchematicId(target.Id), out _),
            TargetKind.Executor =>
                _facilitiesById.ContainsKey(new ExecutorId(target.Id))
                || _linesById.ContainsKey(new ExecutorId(target.Id)),
            _ => false,
        };

    private void RequireCompatible(SchematicDefinition schematic, FacilityInstance executor)
    {
        var type = Archetype(executor).Type;
        if (schematic.RequiredFacilityType != type)
        {
            throw new ArgumentException(
                $"Schematic '{schematic.Id}' needs a {schematic.RequiredFacilityType}, " +
                $"but '{executor.Id}' is a {type}.");
        }
    }

    private void RequireKnownItem(ItemId item)
    {
        if (!_storage.Knows(item))
        {
            throw new ArgumentException($"Unknown item '{item}'.", "definition");
        }
    }
}
