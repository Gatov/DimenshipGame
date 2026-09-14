using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>
/// Turns a player goal into the tasks that would fulfil it. Pure: it reads a world view and
/// returns a plan, and changes nothing.
/// </summary>
public static class ProductionPlanner
{
    /// <summary>
    /// How deep a schematic chain may go. A chain longer than this is a content error rather than
    /// a deep recipe, and a diagnosable shortage beats a stack overflow.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// <paramref name="destination"/> appends one final transfer, hold → destination, for the
    /// goal amount — the delivery that makes a goal actually reach somewhere rather than stopping
    /// at the hold. Null when the plan has no final delivery.
    /// </summary>
    public static ProductionPlan Plan(ItemAmount goal, IWorldView world, StorageId? destination = null)
    {
        if (goal.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(goal), goal.Quantity, "A goal must ask for at least one unit.");
        }

        var state = new Expansion(world);
        var available = state.Require(goal.Item, goal.Quantity, 0, new HashSet<SchematicId>());
        return state.Build(goal, destination, available);
    }

    /// <summary>
    /// One recursive expansion. Holds the running budget of what is still unspent, so that two
    /// branches needing the same material cannot both claim it.
    /// </summary>
    private sealed class Expansion(IWorldView world)
    {
        private readonly Dictionary<ItemId, long> _budget = new();
        private readonly List<PlannedTask> _runTasks = new();
        private readonly List<PlannedTask> _transferTasks = new();
        private readonly Dictionary<(ItemId Item, StorageId From, StorageId To), int> _transferIndex = new();
        private readonly List<Unplannable> _unplannable = new();
        private readonly Dictionary<(ItemId, UnplannableReason), int> _unplannableIndex = new();
        private readonly Dictionary<ExecutorId, long> _facilityLoad = new();
        private readonly Dictionary<ExecutorId, long> _transportLoad = new();

        private readonly Dictionary<ExecutorId, long> _facilityWorkRate =
            world.Facilities.ToDictionary(f => f.Id, f => f.WorkRatePerTick);

        private readonly Dictionary<ExecutorId, long> _transportThroughput =
            world.TransportLines.ToDictionary(l => l.Id, l => l.ThroughputPerTick);

        private readonly Dictionary<ExecutorId, long> _transportLength =
            world.TransportLines.ToDictionary(l => l.Id, l => l.LengthTicks);

        /// <summary>
        /// Expands one item's requirement, recursing into its schematic chain as needed. Returns
        /// how much of <paramref name="quantity"/> was already available — aboard and
        /// uncommitted, or credited from an earlier branch's surplus — before this call did
        /// anything about the rest. That number becomes <see cref="PlannedTask.AvailableAtSource"/>
        /// on whatever task this requirement produces, so a plan preview can show "the vessel has
        /// some of this already" without a separate shortage list.
        /// </summary>
        public long Require(ItemId item, long quantity, int depth, HashSet<SchematicId> visiting)
        {
            var available = Spend(item, quantity);
            var deficit = quantity - available;
            if (deficit <= 0)
            {
                return available;
            }

            if (depth >= MaxDepth)
            {
                MarkUnplannable(item, deficit, UnplannableReason.CyclicSchematic);
                return available;
            }

            // Declaration order, filtered rather than re-queried: the producers of an item and the
            // unlocked subset of them are one question asked twice, and the branch below needs
            // both answers.
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
                // A locked schematic is a real planning problem — a mission or a tech unlock
                // fixes it, and the branch is not built at all. An item nothing produces is not a
                // shortage: the caller's Move still emits the transfer for the full amount
                // regardless of what Require returns, and the engine's transport phase postpones
                // it on InsufficientSourceMaterial until the material actually arrives. Reporting
                // it here would send the player looking for a shortage that hauling already
                // resolves on its own, eventually.
                if (producers.Count > 0)
                {
                    MarkUnplannable(item, deficit, UnplannableReason.LockedSchematic);
                }

                return available;
            }

            // The player may select among candidates, and an unlocked assistant may later choose
            // for them. Until either exists, the first in declaration order is the choice.
            var schematic = candidates[0];

            if (!visiting.Add(schematic.Id))
            {
                MarkUnplannable(item, deficit, UnplannableReason.CyclicSchematic);
                return available;
            }

            var facility = ChooseFacility(schematic.RequiredFacilityType);
            if (facility is null)
            {
                MarkUnplannable(item, deficit, UnplannableReason.NoExecutorOrLine);
                visiting.Remove(schematic.Id);
                return available;
            }

            var runs = (deficit + schematic.Output.Quantity - 1) / schematic.Output.Quantity;

            // Reserved before recursing, not after. A branch expanded further down needing the
            // same facility type must see this work already claimed, or every branch in the plan
            // picks the same idle facility and the load-spreading does nothing.
            _facilityLoad[facility.Id] = _facilityLoad.GetValueOrDefault(facility.Id) + runs;

            foreach (var input in schematic.Inputs)
            {
                var inputAvailable = Require(input.Item, input.Quantity * runs, depth + 1, visiting);
                Move(input.Item, input.Quantity * runs, world.Hold, facility.LocalStorage, inputAvailable);
            }

            // Recorded after its inputs, so the plan reads in the order the work has to happen:
            // the deepest branch first, the goal's own run last.
            _runTasks.Add(new PlannedTask(
                new TaskScript(Array.Empty<Condition>(), new Produce(schematic.Id, (int)runs)),
                facility.Id,
                available));

            var produced = runs * schematic.Output.Quantity;
            // A run's output is freshly made, not something waiting to be produced: the whole
            // amount is available the moment the run completes.
            Move(schematic.Output.Item, produced, facility.LocalStorage, world.Hold, produced);

            // A schematic that produces five at a time overshoots a deficit of three. The surplus
            // is real and stays available to any later branch that wants it.
            _budget[item] = _budget.GetValueOrDefault(item) + produced - deficit;

            visiting.Remove(schematic.Id);
            return available;
        }

        public ProductionPlan Build(ItemAmount goal, StorageId? destination, long available)
        {
            var finalLeg = destination is { } to
                ? MoveFinal(goal.Item, goal.Quantity, world.Hold, to, available)
                : null;

            var tasks = new List<PlannedTask>(
                _transferTasks.Count + _runTasks.Count + (finalLeg is null ? 0 : 1));
            tasks.AddRange(_transferTasks);
            tasks.AddRange(_runTasks);
            if (finalLeg is not null)
            {
                tasks.Add(finalLeg);
            }

            var transfersForEstimate = finalLeg is null
                ? _transferTasks
                : _transferTasks.Append(finalLeg);

            return new ProductionPlan(
                goal, destination, tasks, _unplannable, EstimateTicks(transfersForEstimate, _runTasks));
        }

        /// <summary>Takes what it can from the running budget and reports how much it got.</summary>
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

            // Free before occupied, then least loaded. Definition order, and strict comparisons,
            // so a tie always goes to the earlier facility rather than to whichever the dictionary
            // happened to hand back first — which is what every facility on the default vessel is,
            // since all of them run a standing order.
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

        /// <summary>
        /// The least loaded line that actually runs this leg. Route first, load second: a line
        /// with an empty queue is no use for a journey it cannot make.
        /// <para>
        /// Throughput third, and declaration order only after that. Two lines can run one leg at
        /// very different rates — the shipped vessel has a 7-a-tick Technical Materials feed and a
        /// 50-a-tick hold-star feed both running storage to Factory Beta — and when both are idle,
        /// declaration order alone handed a whole construction unit to the slow one: Factory
        /// Beta's Build plan estimated 145 ticks against 23 for every other slot, on a leg that
        /// had a line seven times faster sitting beside it doing nothing.
        /// </para>
        /// </summary>
        private ExecutorId? ChooseTransport(StorageId from, StorageId to)
        {
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

            return best;
        }

        /// <summary>
        /// Adds to an existing leg of the same route rather than appending another line for it.
        /// Four runs of one schematic are one haul of sixty, not four hauls of fifteen.
        /// </summary>
        private void Move(ItemId item, long quantity, StorageId from, StorageId to, long availableAtSource)
        {
            if (quantity <= 0 || from == to)
            {
                return;
            }

            var key = (item, from, to);
            if (_transferIndex.TryGetValue(key, out var index))
            {
                var existing = _transferTasks[index];
                var transfer = (Transfer)existing.Script.Action;
                _transferTasks[index] = existing with
                {
                    Script = existing.Script with
                    {
                        Action = transfer with { Quantity = transfer.Quantity!.Value + quantity },
                    },
                    AvailableAtSource = existing.AvailableAtSource + availableAtSource,
                };
                return;
            }

            var line = ChooseTransport(from, to);
            if (line is null)
            {
                MarkUnplannable(item, quantity, UnplannableReason.NoExecutorOrLine);
                return;
            }

            _transferIndex[key] = _transferTasks.Count;
            _transferTasks.Add(new PlannedTask(
                new TaskScript(Array.Empty<Condition>(), new Transfer(item, quantity, from, to)),
                line.Value,
                availableAtSource));
            _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;
        }

        /// <summary>
        /// The plan's one delivery to <see cref="ProductionPlan.Destination"/>, kept separate from
        /// <see cref="Move"/>'s merged legs because it is always exactly one transfer of the whole
        /// goal amount, appended after every other task rather than folded into whichever leg
        /// happens to share its route.
        /// </summary>
        private PlannedTask? MoveFinal(ItemId item, long quantity, StorageId from, StorageId to, long availableAtSource)
        {
            if (quantity <= 0 || from == to)
            {
                return null;
            }

            var line = ChooseTransport(from, to);
            if (line is null)
            {
                MarkUnplannable(item, quantity, UnplannableReason.NoExecutorOrLine);
                return null;
            }

            _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;
            return new PlannedTask(
                new TaskScript(Array.Empty<Condition>(), new Transfer(item, quantity, from, to)),
                line.Value,
                availableAtSource);
        }

        /// <summary>
        /// The busiest executor's total, a documented lower bound: it ignores switch-over,
        /// queueing behind existing work, and energy contention, none of which the planner can
        /// see. A run's ticks divide the schematic's effort by the facility's own work rate; a
        /// transfer's ticks divide its quantity by the line's throughput — the same arithmetic
        /// <c>SimulationEngine</c> charges at runtime, read here through the world view instead.
        /// </summary>
        private long EstimateTicks(IEnumerable<PlannedTask> transfers, IEnumerable<PlannedTask> runs)
        {
            var perExecutor = new Dictionary<ExecutorId, long>();

            foreach (var task in transfers)
            {
                var quantity = ((Transfer)task.Script.Action).Quantity!.Value;
                var rate = _transportThroughput.GetValueOrDefault(task.Executor, 1);

                // Ticks to pick it all up, plus the belt once. The belt is paid once per leg and
                // not once per tick of loading, because the line keeps loading while the earlier
                // cargo travels — only the last slot still has the whole journey ahead of it.
                var length = _transportLength.GetValueOrDefault(task.Executor, 1);
                perExecutor[task.Executor] =
                    perExecutor.GetValueOrDefault(task.Executor) + (quantity + rate - 1) / rate + length;
            }

            foreach (var task in runs)
            {
                var produce = (Produce)task.Script.Action;
                var schematic = world.Schematics.Get(produce.Schematic);
                var rate = _facilityWorkRate.GetValueOrDefault(task.Executor, 1);
                var ticksPerRun = (schematic.EffortPerRun.Value + rate - 1) / rate;
                perExecutor[task.Executor] =
                    perExecutor.GetValueOrDefault(task.Executor) + ticksPerRun * produce.Runs!.Value;
            }

            return perExecutor.Values.DefaultIfEmpty(0L).Max();
        }

        private void MarkUnplannable(ItemId item, long missing, UnplannableReason reason)
        {
            var key = (item, reason);
            if (_unplannableIndex.TryGetValue(key, out var index))
            {
                _unplannable[index] = _unplannable[index] with
                {
                    Quantity = _unplannable[index].Quantity + missing,
                };
                return;
            }

            _unplannableIndex[key] = _unplannable.Count;
            _unplannable.Add(new Unplannable(item, missing, reason));
        }
    }
}
