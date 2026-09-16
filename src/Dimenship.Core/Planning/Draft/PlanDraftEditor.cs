using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning.Draft;

/// <summary>
/// Builds and (later) adjusts a <see cref="PlanDraft"/>. Create keeps the requirement graph the
/// flat planner used to throw away; <see cref="PlanDraft.Flatten"/> is what
/// <see cref="ProductionPlanner.Plan"/> becomes so there is one expansion algorithm.
/// </summary>
public static class PlanDraftEditor
{
    public static PlanDraft Create(ItemAmount goal, IWorldView world, StorageId? destination = null)
    {
        if (goal.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(goal), goal.Quantity, "A goal must ask for at least one unit.");
        }

        var expansion = new Expansion(world);
        var available = expansion.Require(goal.Item, goal.Quantity, 0, new HashSet<SchematicId>(), parent: null);
        return expansion.Finish(goal, destination, available);
    }

    private sealed class Expansion(IWorldView world)
    {
        private readonly Dictionary<ItemId, long> _budget = new();
        private readonly List<DraftStep> _steps = new();
        private readonly List<DraftIssue> _issues = new();
        private readonly Dictionary<(ItemId, DraftIssueKind), int> _issueIndex = new();
        private readonly Dictionary<ExecutorId, long> _facilityLoad = new();
        private readonly Dictionary<ExecutorId, long> _transportLoad = new();
        private readonly Dictionary<(StorageId From, StorageId To), ExecutorId> _routeLine = new();
        private readonly HashSet<(ItemId Item, StorageId From, StorageId To)> _movedRoutes = new();
        private long _nextId = 1;

        private readonly Dictionary<ExecutorId, long> _facilityWorkRate =
            world.Facilities.ToDictionary(f => f.Id, f => f.WorkRatePerTick);

        private readonly Dictionary<ExecutorId, long> _transportThroughput =
            world.TransportLines.ToDictionary(l => l.Id, l => l.ThroughputPerTick);

        private readonly Dictionary<ExecutorId, long> _transportLength =
            world.TransportLines.ToDictionary(l => l.Id, l => l.LengthTicks);

        public long Require(
            ItemId item, long quantity, int depth, HashSet<SchematicId> visiting, DraftStepId? parent)
        {
            var available = Spend(item, quantity);
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

            var producers = world.Schematics.ForOutput(item);
            var candidates = new List<SchematicDefinition>(producers.Count);
            foreach (var producer in producers)
            {
                if (world.IsUnlocked(producer.Id))
                {
                    candidates.Add(producer);
                }
            }

            if (candidates.Count == 0)
            {
                if (producers.Count > 0)
                {
                    MarkIssue(item, deficit, DraftIssueKind.LockedSchematic);
                }

                return available;
            }

            var schematic = candidates[0];

            if (!visiting.Add(schematic.Id))
            {
                MarkIssue(item, deficit, DraftIssueKind.CyclicSchematic);
                return available;
            }

            var facility = ChooseFacility(schematic.RequiredFacilityType);
            if (facility is null)
            {
                MarkIssue(item, deficit, DraftIssueKind.NoExecutorOrLine);
                visiting.Remove(schematic.Id);
                return available;
            }

            var runs = (deficit + schematic.Output.Quantity - 1) / schematic.Output.Quantity;
            _facilityLoad[facility.Id] = _facilityLoad.GetValueOrDefault(facility.Id) + runs;

            var produceId = Mint();
            var produceKey = new RequirementKey(parent, DraftRole.Output, 0, item);

            for (var i = 0; i < schematic.Inputs.Count; i++)
            {
                var input = schematic.Inputs[i];
                var need = input.Quantity * runs;
                var inputAvailable = Require(input.Item, need, depth + 1, visiting, produceId);
                Move(
                    input.Item, need, world.Hold, facility.LocalStorage, inputAvailable,
                    produceId, DraftRole.Input, i);
            }

            _steps.Add(new DraftStep(
                produceId,
                produceKey,
                new DraftProduce(schematic.Id, runs),
                facility.Id,
                available,
                QuantityLocked: false,
                ExecutorLocked: false,
                DraftOrigin.Automatic,
                Replanned: false));

            var produced = runs * schematic.Output.Quantity;
            Move(
                schematic.Output.Item, produced, facility.LocalStorage, world.Hold, produced,
                produceId, DraftRole.Output, 0);

            _budget[item] = _budget.GetValueOrDefault(item) + produced - deficit;

            visiting.Remove(schematic.Id);
            return available;
        }

        public PlanDraft Finish(ItemAmount goal, StorageId? destination, long available)
        {
            if (destination is { } to)
            {
                MoveFinal(goal.Item, goal.Quantity, world.Hold, to, available);
            }

            var covered = CoveredToward(goal);
            var estimate = EstimateTicks();
            return new PlanDraft(
                goal, destination, AssemblyTarget: null, _steps, _issues, covered, estimate);
        }

        private long CoveredToward(ItemAmount goal)
        {
            var missing = 0L;
            foreach (var issue in _issues)
            {
                if (issue.Item == goal.Item
                    && issue.Kind is DraftIssueKind.LockedSchematic
                        or DraftIssueKind.NoExecutorOrLine
                        or DraftIssueKind.CyclicSchematic)
                {
                    missing += issue.Quantity;
                }
            }

            return Math.Max(0L, goal.Quantity - missing);
        }

        private long Spend(ItemId item, long quantity)
        {
            if (!_budget.TryGetValue(item, out var available))
            {
                available = Math.Max(0, world.Uncommitted(item));
            }

            var used = Math.Min(available, quantity);
            _budget[item] = available - used;
            return used;
        }

        private PlannerFacility? ChooseFacility(FacilityType type)
        {
            PlannerFacility? best = null;
            var bestOccupied = true;
            var bestLoad = long.MaxValue;

            foreach (var facility in world.Facilities)
            {
                if (facility.Type != type)
                {
                    continue;
                }

                var load = facility.QueuedRuns + _facilityLoad.GetValueOrDefault(facility.Id);
                if (best is null
                    || (bestOccupied && !facility.Occupied)
                    || (bestOccupied == facility.Occupied && load < bestLoad))
                {
                    bestOccupied = facility.Occupied;
                    bestLoad = load;
                    best = facility;
                }
            }

            return best;
        }

        private ExecutorId? ChooseTransport(StorageId from, StorageId to)
        {
            if (_routeLine.TryGetValue((from, to), out var cached))
            {
                return cached;
            }

            ExecutorId? best = null;
            var bestLoad = long.MaxValue;
            var bestThroughput = long.MinValue;

            foreach (var line in world.TransportLines)
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

        private void Move(
            ItemId item,
            long quantity,
            StorageId from,
            StorageId to,
            long availableAtSource,
            DraftStepId parent,
            DraftRole role,
            int ordinal)
        {
            if (quantity <= 0 || from == to)
            {
                return;
            }

            var line = ChooseTransport(from, to);
            if (line is null)
            {
                MarkIssue(item, quantity, DraftIssueKind.NoExecutorOrLine);
                return;
            }

            // Same load accounting the flat Move used: one +1 the first time a given
            // (item, from, to) appears, even when several requirement rows share the line.
            if (_movedRoutes.Add((item, from, to)))
            {
                _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;
            }

            _steps.Add(new DraftStep(
                Mint(),
                new RequirementKey(parent, role, ordinal, item),
                new DraftMove(item, quantity, from, to),
                line.Value,
                availableAtSource,
                QuantityLocked: false,
                ExecutorLocked: false,
                DraftOrigin.Automatic,
                Replanned: false));
        }

        private void MoveFinal(ItemId item, long quantity, StorageId from, StorageId to, long availableAtSource)
        {
            if (quantity <= 0 || from == to)
            {
                return;
            }

            var line = ChooseTransport(from, to);
            if (line is null)
            {
                MarkIssue(item, quantity, DraftIssueKind.NoExecutorOrLine);
                return;
            }

            // MoveFinal never merged with earlier legs; it always paid the line once more.
            _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;

            _steps.Add(new DraftStep(
                Mint(),
                new RequirementKey(null, DraftRole.Delivery, 0, item),
                new DraftMove(item, quantity, from, to),
                line.Value,
                availableAtSource,
                QuantityLocked: false,
                ExecutorLocked: false,
                DraftOrigin.Automatic,
                Replanned: false));
        }

        private long EstimateTicks()
        {
            // Flatten once for the same arithmetic ProductionPlanner used on its two lists.
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
                    var schematic = world.Schematics.Get(produce.Schematic);
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

        private DraftStepId Mint() => new(_nextId++);
    }
}
