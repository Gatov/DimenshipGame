using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning.Draft;

/// <summary>
/// Builds and adjusts a <see cref="PlanDraft"/>. Create keeps the requirement graph the flat
/// planner used to throw away; <see cref="PlanDraft.Flatten"/> is what
/// <see cref="ProductionPlanner.Plan"/> becomes so there is one expansion algorithm.
/// </summary>
public static class PlanDraftEditor
{
    public static PlanDraft Create(
        ItemAmount goal,
        IWorldView world,
        StorageId? destination = null,
        ExecutorId? assemblyTarget = null)
    {
        if (goal.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(goal), goal.Quantity, "A goal must ask for at least one unit.");
        }

        var expansion = new Expansion(world);
        DraftStepId? parent = null;
        if (assemblyTarget is { } target)
        {
            parent = expansion.EmitAssemblyRoot(goal.Item, target);
        }

        var available = expansion.Require(goal.Item, goal.Quantity, 0, new HashSet<SchematicId>(), parent);
        return expansion.Finish(goal, destination, available, assemblyTarget);
    }

    /// <summary>
    /// Re-expands against the live world, refuses a structurally invalid draft, and flattens the
    /// rest into an ordinary <see cref="ProductionPlan"/> with no draft metadata on it.
    /// </summary>
    public static PlanApproval Approve(PlanDraft draft, IWorldView world)
    {
        var refreshed = Adjust(draft, world, new WorldRefresh());
        if (!refreshed.IsCommittable)
        {
            return new PlanApprovalRefused(refreshed.Issues);
        }

        return new PlanApprovalCommitted(refreshed.Flatten());
    }

    /// <summary>
    /// Rebuilds a draft after one edit. Locks, manual facts and valid retained choices survive;
    /// everything else is re-expanded against the live world view.
    /// </summary>
    public static PlanDraft Adjust(PlanDraft draft, IWorldView world, DraftEdit edit)
    {
        var adjustment = AdjustmentState.From(draft, world, edit);
        if (adjustment.Rejected)
        {
            return draft with { Issues = MergeIssues(draft.Issues, adjustment.RejectionIssues) };
        }

        var expansion = new Expansion(world, adjustment);
        DraftStepId? parent = null;
        if (draft.AssemblyTarget is { } target)
        {
            parent = expansion.EmitAssemblyRoot(draft.Goal.Item, target);
        }

        var available = expansion.Require(
            draft.Goal.Item, draft.Goal.Quantity, 0, new HashSet<SchematicId>(), parent);
        expansion.EmitManualSteps();
        return expansion.Finish(draft.Goal, draft.Destination, available, draft.AssemblyTarget);
    }

    private static IReadOnlyList<DraftIssue> MergeIssues(
        IReadOnlyList<DraftIssue> existing, IReadOnlyList<DraftIssue> added)
    {
        var merged = new List<DraftIssue>(existing);
        foreach (var issue in added)
        {
            var index = merged.FindIndex(i =>
                i.Kind == issue.Kind && i.Item == issue.Item && i.Step == issue.Step);
            if (index >= 0)
            {
                // Structural rejections replace; supply kinds still accumulate.
                var prior = merged[index];
                merged[index] = IsStructuralKind(issue.Kind)
                    ? issue
                    : prior with { Quantity = prior.Quantity + issue.Quantity };
            }
            else
            {
                merged.Add(issue);
            }
        }

        return merged;
    }

    private static bool IsStructuralKind(DraftIssueKind kind) => kind is
        DraftIssueKind.IncompatibleExecutor
        or DraftIssueKind.NoSuchRoute
        or DraftIssueKind.UnknownEndpoint
        or DraftIssueKind.NonPositiveQuantity
        or DraftIssueKind.UnbuiltExecutor
        or DraftIssueKind.NotCommandable
        or DraftIssueKind.WorkpieceNotAccepted;

    private sealed class StepConstraint
    {
        public required DraftStepId Id { get; init; }
        public required RequirementKey Key { get; init; }
        public required DraftWork Work { get; init; }
        public long Quantity { get; set; }
        public ExecutorId? Executor { get; set; }
        public bool QuantityLocked { get; set; }
        public bool ExecutorLocked { get; set; }
        public DraftOrigin Origin { get; init; }
        public bool Removed { get; set; }
        public ExecutorId? RetainedExecutor { get; set; }
    }

    private sealed class AdjustmentState
    {
        public bool ReoptimiseUnlocked { get; private set; }
        public bool Rejected { get; private set; }
        public IReadOnlyList<DraftIssue> RejectionIssues { get; private set; } = Array.Empty<DraftIssue>();
        public Dictionary<RequirementKey, StepConstraint> ByKey { get; } = new();
        public Dictionary<DraftStepId, StepConstraint> ById { get; } = new();
        public Dictionary<RequirementKey, DraftStepId> IdForKey { get; } = new();
        public List<StepConstraint> PendingManual { get; } = new();
        public int NextManualOrdinal { get; set; }

        public static AdjustmentState From(PlanDraft draft, IWorldView world, DraftEdit edit)
        {
            var state = new AdjustmentState();
            foreach (var step in draft.Steps)
            {
                var quantity = QuantityOf(step, world);
                var constraint = new StepConstraint
                {
                    Id = step.Id,
                    Key = step.Key,
                    Work = step.Work,
                    Quantity = quantity,
                    Executor = step.Executor,
                    QuantityLocked = step.QuantityLocked,
                    ExecutorLocked = step.ExecutorLocked,
                    Origin = step.Origin,
                    RetainedExecutor = step.Executor,
                };
                state.ByKey[step.Key] = constraint;
                state.ById[step.Id] = constraint;
                state.IdForKey[step.Key] = step.Id;
                if (step.Key.Role == DraftRole.Manual)
                {
                    state.NextManualOrdinal = Math.Max(state.NextManualOrdinal, step.Key.Ordinal + 1);
                }
            }

            state.Apply(draft, world, edit);
            return state;
        }

        private void Apply(PlanDraft draft, IWorldView world, DraftEdit edit)
        {
            switch (edit)
            {
                case SetQuantity(var stepId, var quantity):
                    if (ById.TryGetValue(stepId, out var qtyStep))
                    {
                        // Edited values are locked (issue #40 / covering spec): without this,
                        // RE-ADJUST would silently drop a quantity the player just typed.
                        qtyStep.Quantity = quantity;
                        qtyStep.QuantityLocked = true;
                    }

                    break;

                case SetExecutor(var stepId, var executor):
                    if (ById.TryGetValue(stepId, out var execStep))
                    {
                        execStep.Executor = executor;
                        execStep.RetainedExecutor = executor;
                        execStep.ExecutorLocked = true;
                    }

                    break;

                case SetLock(var stepId, var field, var locked):
                    if (ById.TryGetValue(stepId, out var lockStep))
                    {
                        if (field == DraftField.Quantity)
                        {
                            lockStep.QuantityLocked = locked;
                        }
                        else
                        {
                            lockStep.ExecutorLocked = locked;
                        }
                    }

                    break;

                case AddMove(var item, var from, var to, var quantity, var line):
                    var rejectionKind = ClassifyAddMoveRejection(world, from, to, line);
                    if (rejectionKind is { } kind)
                    {
                        Rejected = true;
                        RejectionIssues = new[]
                        {
                            new DraftIssue(kind, item, quantity, Step: null),
                        };
                        return;
                    }

                    var manualKey = new RequirementKey(null, DraftRole.Manual, NextManualOrdinal++, item);
                    var manual = new StepConstraint
                    {
                        Id = new DraftStepId(0),
                        Key = manualKey,
                        Work = new DraftMove(item, quantity, from, to),
                        Quantity = quantity,
                        Executor = line,
                        QuantityLocked = true,
                        ExecutorLocked = true,
                        Origin = DraftOrigin.Manual,
                        RetainedExecutor = line,
                    };
                    PendingManual.Add(manual);
                    ByKey[manualKey] = manual;
                    break;

                case RemoveStep(var stepId):
                    if (ById.TryGetValue(stepId, out var removed))
                    {
                        removed.Removed = true;
                    }

                    break;

                case ReAdjust:
                    ReoptimiseUnlocked = true;
                    break;

                case UnlockAll:
                    foreach (var constraint in ByKey.Values)
                    {
                        constraint.QuantityLocked = false;
                        constraint.ExecutorLocked = false;
                    }

                    break;

                case WorldRefresh:
                    break;
            }
        }

        public bool TryGet(RequirementKey key, out StepConstraint? constraint) =>
            ByKey.TryGetValue(key, out constraint) && constraint is { Removed: false };

        public long ManualCredit(ItemId item, StorageId to)
        {
            // Pending manuals are also inserted into ByKey on AddMove — count ByKey only so a
            // fresh AddMove does not double the credit and zero the hold-mediated residual.
            var credit = 0L;
            foreach (var constraint in ByKey.Values)
            {
                if (constraint.Removed || constraint.Origin != DraftOrigin.Manual)
                {
                    continue;
                }

                if (constraint.Work is DraftMove move && move.Item == item && move.To == to)
                {
                    credit += constraint.Quantity;
                }
            }

            return credit;
        }

        private static DraftIssueKind? ClassifyAddMoveRejection(
            IWorldView world, StorageId from, StorageId to, ExecutorId line)
        {
            if (!EndpointExists(world, from) || !EndpointExists(world, to))
            {
                return DraftIssueKind.UnknownEndpoint;
            }

            return RouteExists(world, from, to, line) ? null : DraftIssueKind.NoSuchRoute;
        }

        private static bool EndpointExists(IWorldView world, StorageId storage)
        {
            foreach (var transport in world.TransportLines)
            {
                if (transport.From == storage || transport.To == storage)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RouteExists(IWorldView world, StorageId from, StorageId to, ExecutorId line)
        {
            foreach (var transport in world.TransportLines)
            {
                if (transport.Id == line && transport.From == from && transport.To == to)
                {
                    return true;
                }
            }

            return false;
        }

        private static long QuantityOf(DraftStep step, IWorldView world) =>
            step.Work switch
            {
                DraftProduce produce =>
                    produce.Runs * world.Schematics.Get(produce.Schematic).Output.Quantity,
                DraftMove move => move.Quantity,
                _ => 0,
            };
    }

    private sealed class Expansion
    {
        private readonly IWorldView _world;
        private readonly AdjustmentState? _adjustment;
        private readonly Dictionary<ItemId, long> _budget = new();
        private readonly List<DraftStep> _steps = new();
        private readonly List<DraftIssue> _issues = new();
        private readonly Dictionary<(ItemId, DraftIssueKind), int> _issueIndex = new();
        private readonly Dictionary<ExecutorId, long> _facilityTicks = new();
        private readonly Dictionary<ExecutorId, List<SchematicId>> _facilitySetups = new();
        private readonly Dictionary<ExecutorId, long> _transportLoad = new();
        private readonly Dictionary<(StorageId From, StorageId To), ExecutorId> _routeLine = new();
        private readonly HashSet<(ItemId Item, StorageId From, StorageId To)> _movedRoutes = new();
        private readonly HashSet<RequirementKey> _emittedKeys = new();
        private long _nextId = 1;

        private readonly Dictionary<ExecutorId, long> _facilityWorkRate;
        private readonly Dictionary<ExecutorId, long> _transportThroughput;
        private readonly Dictionary<ExecutorId, long> _transportLength;

        public Expansion(IWorldView world, AdjustmentState? adjustment = null)
        {
            _world = world;
            _adjustment = adjustment;
            _facilityWorkRate = world.Facilities.ToDictionary(f => f.Id, f => f.WorkRatePerTick);
            _transportThroughput = world.TransportLines.ToDictionary(l => l.Id, l => l.ThroughputPerTick);
            _transportLength = world.TransportLines.ToDictionary(l => l.Id, l => l.LengthTicks);

            if (adjustment is not null)
            {
                foreach (var id in adjustment.IdForKey.Values)
                {
                    _nextId = Math.Max(_nextId, id.Value + 1);
                }
            }
        }

        /// <param name="deliverTo">
        /// Where a workpiece must end up: the buffer of the facility that consumes it (K5b-w). Null
        /// for anything the hold accepts, whose output goes home to the hold as it always has.
        /// </param>
        public long Require(
            ItemId item,
            long quantity,
            int depth,
            HashSet<SchematicId> visiting,
            DraftStepId? parent,
            StorageId? deliverTo = null)
        {
            var outputKey = new RequirementKey(parent, DraftRole.Output, 0, item);
            var available = Spend(item, quantity);

            if (_adjustment is not null
                && _adjustment.TryGet(outputKey, out var preservedProduce)
                && preservedProduce is not null
                && preservedProduce.Work is DraftProduce preservedWork
                && !preservedProduce.Removed
                && (preservedProduce.QuantityLocked || preservedProduce.ExecutorLocked
                    || !_adjustment.ReoptimiseUnlocked))
            {
                return EmitPreservedProduce(
                    preservedProduce, preservedWork, item, quantity, available, depth, visiting, parent, deliverTo);
            }

            var deficit = quantity - available;
            if (deficit <= 0)
            {
                return available;
            }

            if (depth >= ProductionPlanner.MaxDepth)
            {
                MarkIssue(item, deficit, DraftIssueKind.CyclicSchematic);
                return available;
            }

            var producers = _world.Schematics.ForOutput(item);
            var candidates = new List<SchematicDefinition>(producers.Count);
            foreach (var producer in producers)
            {
                if (_world.IsUnlocked(producer.Id))
                {
                    candidates.Add(producer);
                }
            }

            if (candidates.Count == 0)
            {
                // A raw material nothing aboard makes. The work that needs it is still planned in
                // full: the player acquires the material while that work runs, and the stage
                // short of it waits on its input until it arrives. The shortage is reported so
                // the player knows what to fetch, as a supply note that never blocks approval.
                MarkIssue(
                    item,
                    deficit,
                    producers.Count > 0 ? DraftIssueKind.LockedSchematic : DraftIssueKind.MaterialShortage);

                return available;
            }

            var schematic = candidates[0];

            if (!visiting.Add(schematic.Id))
            {
                MarkIssue(item, deficit, DraftIssueKind.CyclicSchematic);
                return available;
            }

            var runs = (deficit + schematic.Output.Quantity - 1) / schematic.Output.Quantity;
            var facility = ChooseFacility(schematic, runs, outputKey, deliverTo);
            if (facility is null)
            {
                MarkIssue(item, deficit, DraftIssueKind.NoExecutorOrLine);
                visiting.Remove(schematic.Id);
                return available;
            }

            return EmitProduce(
                schematic, facility, runs, item, quantity, available, deficit, depth, visiting, parent,
                deliverTo: deliverTo);
        }

        private long EmitPreservedProduce(
            StepConstraint preserved,
            DraftProduce preservedWork,
            ItemId item,
            long quantity,
            long available,
            int depth,
            HashSet<SchematicId> visiting,
            DraftStepId? parent,
            StorageId? deliverTo)
        {
            var schematic = _world.Schematics.Get(preservedWork.Schematic);
            var outputKey = preserved.Key;

            if (!visiting.Add(schematic.Id))
            {
                MarkIssue(item, quantity - available, DraftIssueKind.CyclicSchematic);
                return available;
            }

            long runs;
            if (preserved.QuantityLocked)
            {
                if (preserved.Quantity <= 0)
                {
                    MarkStepIssue(
                        preserved.Id, item, quantity, DraftIssueKind.NonPositiveQuantity);
                    visiting.Remove(schematic.Id);
                    return available;
                }

                runs = (preserved.Quantity + schematic.Output.Quantity - 1)
                    / schematic.Output.Quantity;
            }
            else if (!(_adjustment?.ReoptimiseUnlocked ?? false)
                     && !preserved.Removed)
            {
                runs = (preserved.Quantity + schematic.Output.Quantity - 1)
                    / schematic.Output.Quantity;
            }
            else
            {
                var deficit = quantity - available;
                runs = (deficit + schematic.Output.Quantity - 1) / schematic.Output.Quantity;
            }

            var facility = ResolveFacility(schematic, runs, outputKey, preserved, deliverTo);
            if (facility is null)
            {
                MarkIssue(item, quantity - available, DraftIssueKind.NoExecutorOrLine);
                visiting.Remove(schematic.Id);
                return available;
            }

            var needed = quantity - available;
            var preservedRuns = (preserved.Quantity + schematic.Output.Quantity - 1)
                / schematic.Output.Quantity;

            return EmitProduce(
                schematic, facility, runs, item, quantity, available, needed, depth, visiting, parent,
                preserved,
                replanned: preservedRuns != runs || facility.Id != preserved.RetainedExecutor,
                deliverTo: deliverTo);
        }

        private long EmitProduce(
            SchematicDefinition schematic,
            PlannerFacility facility,
            long runs,
            ItemId item,
            long quantity,
            long available,
            long deficit,
            int depth,
            HashSet<SchematicId> visiting,
            DraftStepId? parent,
            StepConstraint? preserved = null,
            bool replanned = false,
            StorageId? deliverTo = null)
        {
            _facilityTicks[facility.Id] = _facilityTicks.GetValueOrDefault(facility.Id)
                + runs * RunTicks(facility, schematic);
            if (!_facilitySetups.TryGetValue(facility.Id, out var planned))
            {
                _facilitySetups[facility.Id] = planned = new List<SchematicId>();
            }

            planned.Add(schematic.Id);

            var produceKey = new RequirementKey(parent, DraftRole.Output, 0, item);
            var produceId = preserved is null ? Mint(produceKey) : ReuseId(preserved);

            for (var i = 0; i < schematic.Inputs.Count; i++)
            {
                var input = schematic.Inputs[i];
                var need = input.Quantity * runs;
                var inputKey = new RequirementKey(produceId, DraftRole.Input, i, input.Item);

                // A workpiece never visits the hold, so its producer delivers straight here and
                // there is no hold leg to emit (K5b-w).
                var direct = IsWorkpiece(input.Item);
                var inputAvailable = Require(
                    input.Item, need, depth + 1, visiting, produceId, direct ? facility.LocalStorage : null);
                var manualCredit = _adjustment?.ManualCredit(input.Item, facility.LocalStorage) ?? 0;
                EmitManualsFeeding(input.Item, facility.LocalStorage);
                if (direct)
                {
                    continue;
                }

                var adjustedNeed = Math.Max(0, need - manualCredit);
                Move(
                    input.Item, adjustedNeed, _world.Hold, facility.LocalStorage, inputAvailable,
                    produceId, DraftRole.Input, i, preservedInputKey: inputKey);
            }

            var stepAvailable = available;
            var wasPreserved = preserved is not null;
            var quantityLocked = preserved?.QuantityLocked ?? false;
            var executorLocked = preserved?.ExecutorLocked ?? false;
            var origin = preserved?.Origin ?? DraftOrigin.Automatic;

            _steps.Add(new DraftStep(
                produceId,
                produceKey,
                new DraftProduce(schematic.Id, runs),
                facility.Id,
                stepAvailable,
                quantityLocked,
                executorLocked,
                origin,
                replanned));

            _emittedKeys.Add(produceKey);

            var produced = runs * schematic.Output.Quantity;
            Move(
                schematic.Output.Item, produced, facility.LocalStorage, deliverTo ?? _world.Hold, produced,
                produceId, DraftRole.Output, 0, preservedInputKey: new RequirementKey(produceId, DraftRole.Output, 0, schematic.Output.Item));

            // A surplus delivered to a buffer is not in the hold, and the hold is the only supply
            // the planner counts, so it is never offered to a later requirement.
            if (deliverTo is null)
            {
                _budget[item] = _budget.GetValueOrDefault(item) + produced - deficit;
            }

            visiting.Remove(schematic.Id);
            return available;
        }

        public void EmitManualSteps()
        {
            if (_adjustment is null)
            {
                return;
            }

            foreach (var manual in _adjustment.PendingManual)
            {
                if (manual.Removed)
                {
                    continue;
                }

                if (_emittedKeys.Contains(manual.Key))
                {
                    continue;
                }

                EmitManual(manual);
            }

            foreach (var constraint in _adjustment.ByKey.Values)
            {
                if (constraint.Removed || constraint.Origin != DraftOrigin.Manual)
                {
                    continue;
                }

                if (_emittedKeys.Contains(constraint.Key))
                {
                    continue;
                }

                EmitManual(constraint);
            }
        }

        private void EmitManualsFeeding(ItemId item, StorageId to)
        {
            if (_adjustment is null)
            {
                return;
            }

            foreach (var manual in _adjustment.PendingManual)
            {
                if (manual.Removed || _emittedKeys.Contains(manual.Key))
                {
                    continue;
                }

                if (manual.Work is DraftMove move && move.Item == item && move.To == to)
                {
                    EmitManual(manual);
                }
            }

            foreach (var constraint in _adjustment.ByKey.Values)
            {
                if (constraint.Removed || constraint.Origin != DraftOrigin.Manual)
                {
                    continue;
                }

                if (_emittedKeys.Contains(constraint.Key))
                {
                    continue;
                }

                if (constraint.Work is DraftMove move && move.Item == item && move.To == to)
                {
                    EmitManual(constraint);
                }
            }
        }

        private void EmitManual(StepConstraint manual)
        {
            if (manual.Work is not DraftMove move)
            {
                return;
            }

            ValidateMoveExecutor(manual.Id, move, manual.Executor, manual.ExecutorLocked);

            var id = ReuseId(manual);
            _steps.Add(new DraftStep(
                id,
                manual.Key,
                move with { Quantity = manual.Quantity },
                manual.Executor,
                AvailableAtSource: 0,
                manual.QuantityLocked,
                manual.ExecutorLocked,
                DraftOrigin.Manual,
                Replanned: false));
            _emittedKeys.Add(manual.Key);
        }

        public DraftStepId EmitAssemblyRoot(ItemId unit, ExecutorId target)
        {
            var assemblyKey = new RequirementKey(null, DraftRole.Assembly, 0, unit);
            StepConstraint? preserved = null;
            _adjustment?.TryGet(assemblyKey, out preserved);

            var assemblyId = preserved is null ? Mint(assemblyKey) : ReuseId(preserved);
            _steps.Add(new DraftStep(
                assemblyId,
                assemblyKey,
                new DraftAssemble(unit, target),
                target,
                AvailableAtSource: 0,
                preserved?.QuantityLocked ?? false,
                preserved?.ExecutorLocked ?? false,
                preserved?.Origin ?? DraftOrigin.Automatic,
                Replanned: false));
            _emittedKeys.Add(assemblyKey);
            return assemblyId;
        }

        public PlanDraft Finish(
            ItemAmount goal, StorageId? destination, long available, ExecutorId? assemblyTarget)
        {
            if (destination is { } to)
            {
                MoveFinal(goal.Item, goal.Quantity, _world.Hold, to, available);
            }

            var covered = CoveredToward(goal);
            EmitGoalShortfall(goal, covered);
            MarkUnacceptedWorkpieces(goal, destination);
            var estimate = EstimateTicks();
            IReadOnlyList<DraftIssue> issues = _issues;
            if (_adjustment?.RejectionIssues.Count > 0)
            {
                issues = _adjustment.RejectionIssues.Concat(_issues).ToList();
            }

            return new PlanDraft(
                goal, destination, assemblyTarget, _steps, issues, covered, estimate);
        }

        /// <summary>
        /// Flags every move that would put a workpiece where it is never accepted, and a goal that
        /// would leave one in the hold (K3). One pass over the finished steps rather than a check
        /// at each place a move is made: manual, preserved and automatic moves all end up here, and
        /// a check at each would be one more place to forget. With no workpiece in the catalog it
        /// marks nothing, so an ordinary draft is unchanged.
        /// </summary>
        private void MarkUnacceptedWorkpieces(ItemAmount goal, StorageId? destination)
        {
            foreach (var step in _steps)
            {
                if (step.Work is DraftMove move && !_world.Accepts(move.To, move.Item))
                {
                    MarkStepIssue(step.Id, move.Item, move.Quantity, DraftIssueKind.WorkpieceNotAccepted);
                }
            }

            if (!_world.Accepts(destination ?? _world.Hold, goal.Item))
            {
                MarkIssue(goal.Item, goal.Quantity, DraftIssueKind.WorkpieceNotAccepted);
            }
        }

        private void EmitGoalShortfall(ItemAmount goal, long covered)
        {
            if (covered < goal.Quantity)
            {
                MarkIssue(goal.Item, goal.Quantity - covered, DraftIssueKind.GoalShortfall);
            }
        }

        private long CoveredToward(ItemAmount goal)
        {
            var fromStock = _world.InHold(goal.Item);
            var fromProduction = 0L;
            foreach (var step in _steps)
            {
                // Construction drafts parent the goal produce under DraftAssemble, so Parent is
                // not null — count every Output whose schematic makes the goal item.
                if (step.Key.Role != DraftRole.Output || step.Work is not DraftProduce produce)
                {
                    continue;
                }

                var schematic = _world.Schematics.Get(produce.Schematic);
                if (schematic.Output.Item != goal.Item)
                {
                    continue;
                }

                fromProduction += produce.Runs * schematic.Output.Quantity;
            }

            var covered = Math.Min(goal.Quantity, fromStock + fromProduction);

            foreach (var issue in _issues)
            {
                if (issue.Item == goal.Item
                    && issue.Kind is DraftIssueKind.LockedSchematic
                        or DraftIssueKind.NoExecutorOrLine
                        or DraftIssueKind.CyclicSchematic)
                {
                    covered = Math.Max(0, covered - issue.Quantity);
                }
            }

            return covered;
        }

        private long Spend(ItemId item, long quantity)
        {
            if (!_budget.TryGetValue(item, out var available))
            {
                available = _world.InHold(item);
            }

            var used = Math.Min(available, quantity);
            _budget[item] = available - used;
            return used;
        }

        private PlannerFacility? ChooseFacility(
            SchematicDefinition schematic, long runs, RequirementKey outputKey, StorageId? deliverTo)
        {
            StepConstraint? preserved = null;
            _adjustment?.TryGet(outputKey, out preserved);
            return ResolveFacility(schematic, runs, outputKey, preserved, deliverTo);
        }

        private PlannerFacility? ResolveFacility(
            SchematicDefinition schematic,
            long runs,
            RequirementKey outputKey,
            StepConstraint? preserved,
            StorageId? deliverTo)
        {
            var type = schematic.RequiredFacilityType;
            if (preserved?.ExecutorLocked == true && preserved.Executor is { } lockedId)
            {
                var locked = FindFacility(lockedId);
                if (locked is null)
                {
                    if (FindTransport(lockedId) is not null)
                    {
                        MarkStepIssue(preserved.Id, outputKey.Item, 0, DraftIssueKind.IncompatibleExecutor);
                    }
                    else
                    {
                        MarkStepIssue(preserved.Id, outputKey.Item, 0, DraftIssueKind.UnbuiltExecutor);
                    }

                    return StandInFacility(lockedId, type);
                }

                if (!locked.Commandable)
                {
                    MarkStepIssue(preserved.Id, outputKey.Item, 0, DraftIssueKind.NotCommandable);
                }

                if (locked.Type != type)
                {
                    MarkStepIssue(preserved.Id, outputKey.Item, 0, DraftIssueKind.IncompatibleExecutor);
                }

                return locked;
            }

            if (preserved is not null && !(_adjustment?.ReoptimiseUnlocked ?? false))
            {
                var chosenId = preserved.Executor ?? preserved.RetainedExecutor;
                if (chosenId is { } assignedId)
                {
                    var assigned = FindFacility(assignedId);
                    if (assigned is not null)
                    {
                        if (!assigned.Commandable)
                        {
                            MarkStepIssue(preserved.Id, outputKey.Item, 0, DraftIssueKind.NotCommandable);
                        }
                        else if (assigned.Type != type)
                        {
                            MarkStepIssue(
                                preserved.Id, outputKey.Item, 0, DraftIssueKind.IncompatibleExecutor);
                        }

                        return assigned;
                    }

                    if (FindTransport(assignedId) is not null)
                    {
                        MarkStepIssue(
                            preserved.Id, outputKey.Item, 0, DraftIssueKind.IncompatibleExecutor);
                        return StandInFacility(assignedId, type);
                    }
                }
            }

            PlannerFacility? best = null;
            var bestOccupied = true;
            var bestFinish = long.MaxValue;

            foreach (var facility in _world.Facilities)
            {
                if (facility.Type != type || !ReachesWorkpieces(facility, schematic, deliverTo))
                {
                    continue;
                }

                var finish = EstimatedFinish(facility, schematic, runs, deliverTo);
                if (best is null
                    || (bestOccupied && !facility.Occupied)
                    || (bestOccupied == facility.Occupied && finish < bestFinish))
                {
                    bestOccupied = facility.Occupied;
                    bestFinish = finish;
                    best = facility;
                }
            }

            return best;
        }

        /// <summary>
        /// When a stage would be done at one facility (K5b): the work already queued there, then
        /// the longer of the stage's own work and the slowest hold line it must cross, then each
        /// belt's length once. A belt carries while the facility works, so the slower of the two
        /// paces the stage rather than their sum.
        /// <para>
        /// It replaced the least-loaded factory by run count, which sent situation B's pressing to
        /// Factory Gamma: free, and two thousand components away from the hold down a line that
        /// carries four a tick. Only routes count here, never stock: the planner's supply is still
        /// the main hold's free stock, by the project owner's decision, so every ordinary input leg
        /// starts at the hold and every ordinary output leg ends there. A facility the hold cannot
        /// reach both ways is ranked last, and among such facilities declaration order still
        /// decides.
        /// </para>
        /// </summary>
        /// <para>
        /// A workpiece leg is timed on the line it will actually take (K5b-w): in on the fastest
        /// line from a buffer that makes it, out on the line to the buffer that consumes it, and
        /// no line at all where the workpiece stays in the facility's own buffer.
        /// </para>
        private long EstimatedFinish(
            PlannerFacility facility, SchematicDefinition schematic, long runs, StorageId? deliverTo)
        {
            var paced = runs * RunTicks(facility, schematic);
            var inbound = 0L;
            foreach (var input in schematic.Inputs)
            {
                var direct = IsWorkpiece(input.Item);
                var feed = direct
                    ? WorkpieceFeed(input.Item, facility.LocalStorage)
                    : FastestLine(_world.Hold, facility.LocalStorage);
                if (feed is null)
                {
                    if (direct)
                    {
                        continue;
                    }

                    return long.MaxValue;
                }

                paced = Math.Max(paced, Carry(input.Quantity * runs, feed));
                inbound = Math.Max(inbound, feed.LengthTicks);
            }

            var outbound = 0L;
            var to = deliverTo ?? _world.Hold;
            if (to != facility.LocalStorage)
            {
                if (FastestLine(facility.LocalStorage, to) is not { } home)
                {
                    return long.MaxValue;
                }

                paced = Math.Max(paced, Carry(schematic.Output.Quantity * runs, home));
                outbound = home.LengthTicks;
            }

            return facility.QueuedTicks + _facilityTicks.GetValueOrDefault(facility.Id)
                + SwitchIn(facility, schematic) + paced + inbound + outbound;
        }

        /// <summary>
        /// The changeover a stage adds at one facility (K5c): none when the facility is, or will
        /// be, set up for the schematic, whether by the world or by a stage this draft has already
        /// placed there; none when it has never been set up; one switch-over otherwise. Only the
        /// switch in is charged. The switch back belongs to whatever work comes next, and the
        /// estimate cannot see work nobody has ordered yet.
        /// </summary>
        private long SwitchIn(PlannerFacility facility, SchematicDefinition schematic)
        {
            if (facility.SwitchOverTicks <= 0)
            {
                return 0;
            }

            var world = facility.Setups ?? Array.Empty<SchematicId>();
            var planned = _facilitySetups.GetValueOrDefault(facility.Id);
            if (world.Count == 0 && (planned is null || planned.Count == 0))
            {
                return 0;
            }

            return world.Contains(schematic.Id) || planned?.Contains(schematic.Id) == true
                ? 0
                : facility.SwitchOverTicks;
        }

        /// <summary>
        /// Whether the hold refuses an item, which is what a workpiece is to the planner. It asks
        /// the world rather than reading the catalog's flag, because "the hold will not take it" is
        /// exactly the reason to route around the hold.
        /// </summary>
        private bool IsWorkpiece(ItemId item) => !_world.Accepts(_world.Hold, item);

        /// <summary>
        /// Whether a facility can take part in a workpiece chain (K5b-w). It needs a line from its
        /// buffer to the buffer its workpiece output must reach. For each workpiece it consumes, it
        /// needs a facility that makes it in its own buffer, or a line in from one. A facility that
        /// fails is skipped rather than ranked last, because choosing it would plan material into a
        /// buffer it can never leave. An ordinary stage passes trivially.
        /// </summary>
        private bool ReachesWorkpieces(
            PlannerFacility facility, SchematicDefinition schematic, StorageId? deliverTo)
        {
            if (deliverTo is { } to && to != facility.LocalStorage
                && FastestLine(facility.LocalStorage, to) is null)
            {
                return false;
            }

            foreach (var input in schematic.Inputs)
            {
                if (IsWorkpiece(input.Item)
                    && !MakersOf(input.Item).Contains(facility.LocalStorage)
                    && WorkpieceFeed(input.Item, facility.LocalStorage) is null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The buffers of every facility aboard that could make an item.</summary>
        private HashSet<StorageId> MakersOf(ItemId item)
        {
            var types = new HashSet<FacilityType>();
            foreach (var producer in _world.Schematics.ForOutput(item))
            {
                if (_world.IsUnlocked(producer.Id))
                {
                    types.Add(producer.RequiredFacilityType);
                }
            }

            var buffers = new HashSet<StorageId>();
            foreach (var facility in _world.Facilities)
            {
                if (types.Contains(facility.Type))
                {
                    buffers.Add(facility.LocalStorage);
                }
            }

            return buffers;
        }

        /// <summary>The fastest line into a buffer from one where a workpiece could be made.</summary>
        private PlannerTransport? WorkpieceFeed(ItemId item, StorageId to)
        {
            var makers = MakersOf(item);
            PlannerTransport? fastest = null;
            foreach (var line in _world.TransportLines)
            {
                if (line.To == to && makers.Contains(line.From)
                    && (fastest is null || line.ThroughputPerTick > fastest.ThroughputPerTick))
                {
                    fastest = line;
                }
            }

            return fastest;
        }

        private static long RunTicks(PlannerFacility facility, SchematicDefinition schematic)
        {
            var rate = Math.Max(1, facility.WorkRatePerTick);
            return (schematic.EffortPerRun.Value + rate - 1) / rate;
        }

        private static long Carry(long quantity, PlannerTransport line)
        {
            var rate = Math.Max(1, line.ThroughputPerTick);
            return (quantity + rate - 1) / rate;
        }

        private PlannerTransport? FastestLine(StorageId from, StorageId to)
        {
            PlannerTransport? fastest = null;
            foreach (var line in _world.TransportLines)
            {
                if (line.From == from && line.To == to
                    && (fastest is null || line.ThroughputPerTick > fastest.ThroughputPerTick))
                {
                    fastest = line;
                }
            }

            return fastest;
        }

        private PlannerFacility? FindFacility(ExecutorId id)
        {
            foreach (var facility in _world.Facilities)
            {
                if (facility.Id == id)
                {
                    return facility;
                }
            }

            return null;
        }

        private PlannerTransport? FindTransport(ExecutorId id)
        {
            foreach (var line in _world.TransportLines)
            {
                if (line.Id == id)
                {
                    return line;
                }
            }

            return null;
        }

        private PlannerFacility StandInFacility(ExecutorId id, FacilityType type)
        {
            var storage = _world.Facilities.FirstOrDefault()?.LocalStorage ?? _world.Hold;
            return new PlannerFacility(id, type, storage, 0, false, 1, false);
        }

        private ExecutorId? ChooseTransport(
            StorageId from, StorageId to, RequirementKey moveKey, StepConstraint? preserved)
        {
            if (preserved?.ExecutorLocked == true && preserved.Executor is { } lockedLine)
            {
                if (!RouteExists(from, to, lockedLine))
                {
                    MarkStepIssue(preserved.Id, moveKey.Item, preserved.Quantity, DraftIssueKind.NoSuchRoute);
                }

                _routeLine[(from, to)] = lockedLine;
                return lockedLine;
            }

            if (preserved is not null
                && !(_adjustment?.ReoptimiseUnlocked ?? false)
                && preserved.RetainedExecutor is { } retainedLine
                && !preserved.ExecutorLocked
                && RouteExists(from, to, retainedLine))
            {
                _routeLine[(from, to)] = retainedLine;
                return retainedLine;
            }

            if (_routeLine.TryGetValue((from, to), out var cached))
            {
                return cached;
            }

            ExecutorId? best = null;
            var bestLoad = long.MaxValue;
            var bestThroughput = long.MinValue;

            foreach (var line in _world.TransportLines)
            {
                if (line.From != from || line.To != to)
                {
                    continue;
                }

                var load = line.QueuedTransfers + _transportLoad.GetValueOrDefault(line.Id);
                if (load < bestLoad || (load == bestLoad && line.ThroughputPerTick > bestThroughput))
                {
                    bestLoad = load;
                    bestThroughput = line.ThroughputPerTick;
                    best = line.Id;
                }
            }

            if (best is { } chosen)
            {
                _routeLine[(from, to)] = chosen;
            }

            return best;
        }

        private bool RouteExists(StorageId from, StorageId to, ExecutorId line)
        {
            foreach (var transport in _world.TransportLines)
            {
                if (transport.Id == line && transport.From == from && transport.To == to)
                {
                    return true;
                }
            }

            return false;
        }

        private void Move(
            ItemId item,
            long quantity,
            StorageId from,
            StorageId to,
            long availableAtSource,
            DraftStepId parent,
            DraftRole role,
            int ordinal,
            RequirementKey? preservedInputKey = null)
        {
            if (quantity <= 0 || from == to)
            {
                return;
            }

            var moveKey = preservedInputKey ?? new RequirementKey(parent, role, ordinal, item);
            StepConstraint? preserved = null;
            _adjustment?.TryGet(moveKey, out preserved);

            if (preserved is not null && preserved.QuantityLocked)
            {
                quantity = preserved.Quantity;
                if (quantity <= 0)
                {
                    MarkStepIssue(preserved.Id, item, 0, DraftIssueKind.NonPositiveQuantity);
                    return;
                }
            }

            var line = ChooseTransport(from, to, moveKey, preserved);
            if (line is null)
            {
                if (preserved?.ExecutorLocked == true)
                {
                    MarkStepIssue(preserved.Id, item, quantity, DraftIssueKind.NoSuchRoute);
                    return;
                }

                if (role == DraftRole.Output
                    && _adjustment?.ById.TryGetValue(parent, out var parentStep) == true
                    && parentStep.ExecutorLocked)
                {
                    MarkStepIssue(parent, item, quantity, DraftIssueKind.NoSuchRoute);
                    return;
                }

                MarkIssue(item, quantity, DraftIssueKind.NoExecutorOrLine);
                return;
            }

            if (preserved?.ExecutorLocked == true && !RouteExists(from, to, line.Value))
            {
                MarkStepIssue(preserved.Id, item, quantity, DraftIssueKind.NoSuchRoute);
            }

            if (_movedRoutes.Add((item, from, to)))
            {
                _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;
            }

            var stepId = preserved is null ? Mint(moveKey) : ReuseId(preserved);
            var quantityLocked = preserved?.QuantityLocked ?? false;
            var executorLocked = preserved?.ExecutorLocked ?? false;
            var origin = preserved?.Origin ?? DraftOrigin.Automatic;
            var replanned = preserved is not null
                && (quantity != preserved.Quantity || line != preserved.RetainedExecutor);

            _steps.Add(new DraftStep(
                stepId,
                moveKey,
                new DraftMove(item, quantity, from, to),
                line.Value,
                availableAtSource,
                quantityLocked,
                executorLocked,
                origin,
                replanned));
            _emittedKeys.Add(moveKey);
        }

        private void MoveFinal(ItemId item, long quantity, StorageId from, StorageId to, long availableAtSource)
        {
            if (quantity <= 0 || from == to)
            {
                return;
            }

            var deliveryKey = new RequirementKey(null, DraftRole.Delivery, 0, item);
            StepConstraint? preserved = null;
            _adjustment?.TryGet(deliveryKey, out preserved);

            if (preserved is not null && preserved.QuantityLocked)
            {
                quantity = preserved.Quantity;
            }

            var line = ChooseTransport(from, to, deliveryKey, preserved);
            if (line is null)
            {
                MarkIssue(item, quantity, DraftIssueKind.NoExecutorOrLine);
                return;
            }

            _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;

            var stepId = preserved is null ? Mint(deliveryKey) : ReuseId(preserved);
            _steps.Add(new DraftStep(
                stepId,
                deliveryKey,
                new DraftMove(item, quantity, from, to),
                line.Value,
                availableAtSource,
                preserved?.QuantityLocked ?? false,
                preserved?.ExecutorLocked ?? false,
                preserved?.Origin ?? DraftOrigin.Automatic,
                preserved is not null && (quantity != preserved.Quantity || line != preserved.RetainedExecutor)));
            _emittedKeys.Add(deliveryKey);
        }

        private void ValidateMoveExecutor(
            DraftStepId stepId, DraftMove move, ExecutorId? executor, bool locked)
        {
            if (executor is not { } line)
            {
                return;
            }

            if (!RouteExists(move.From, move.To, line))
            {
                MarkStepIssue(stepId, move.Item, move.Quantity, DraftIssueKind.NoSuchRoute);
            }
            else if (locked)
            {
                _routeLine[(move.From, move.To)] = line;
            }
        }

        private long EstimateTicks()
        {
            var plan = new PlanDraft(
                new ItemAmount(new ItemId("estimate"), 1),
                null, null, _steps, Array.Empty<DraftIssue>(), 0, 0).Flatten();

            var perExecutor = new Dictionary<ExecutorId, long>();

            foreach (var task in plan.Tasks)
            {
                if (task.Script.Action is Transfer transfer)
                {
                    var quantity = transfer.Quantity!.Value;
                    var rate = _transportThroughput.GetValueOrDefault(task.Executor, 1);
                    var length = _transportLength.GetValueOrDefault(task.Executor, 1);
                    perExecutor[task.Executor] =
                        perExecutor.GetValueOrDefault(task.Executor)
                        + (quantity + rate - 1) / rate + length;
                }
                else if (task.Script.Action is Produce produce)
                {
                    var schematic = _world.Schematics.Get(produce.Schematic);
                    var rate = _facilityWorkRate.GetValueOrDefault(task.Executor, 1);
                    var ticksPerRun = (schematic.EffortPerRun.Value + rate - 1) / rate;
                    perExecutor[task.Executor] =
                        perExecutor.GetValueOrDefault(task.Executor)
                        + ticksPerRun * produce.Runs!.Value;
                }
            }

            return perExecutor.Values.DefaultIfEmpty(0L).Max();
        }

        private void MarkIssue(ItemId item, long missing, DraftIssueKind kind)
        {
            var key = (item, kind);
            if (_issueIndex.TryGetValue(key, out var index))
            {
                _issues[index] = _issues[index] with
                {
                    Quantity = _issues[index].Quantity + missing,
                };
                return;
            }

            _issueIndex[key] = _issues.Count;
            _issues.Add(new DraftIssue(kind, item, missing, Step: null));
        }

        private void MarkStepIssue(DraftStepId stepId, ItemId item, long quantity, DraftIssueKind kind)
        {
            _issues.Add(new DraftIssue(kind, item, quantity, stepId));
        }

        private DraftStepId Mint(RequirementKey? outputKey = null)
        {
            if (outputKey is { } key
                && _adjustment?.IdForKey.TryGetValue(key, out var reused) == true)
            {
                return reused;
            }

            return new DraftStepId(_nextId++);
        }

        private DraftStepId ReuseId(StepConstraint preserved)
        {
            if (_adjustment?.IdForKey.TryGetValue(preserved.Key, out var id) == true)
            {
                return id;
            }

            return preserved.Id.Value == 0 ? new DraftStepId(_nextId++) : preserved.Id;
        }
    }
}
