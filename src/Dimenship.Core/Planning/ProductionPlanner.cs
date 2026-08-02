using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>
/// Turns a player goal into the production, transport and acquisition requirements that would
/// fulfil it. Pure: it reads a world view and returns a plan, and changes nothing.
/// </summary>
public static class ProductionPlanner
{
    /// <summary>
    /// How deep a schematic chain may go. A chain longer than this is a content error rather than
    /// a deep recipe, and a diagnosable shortage beats a stack overflow.
    /// </summary>
    public const int MaxDepth = 32;

    public static ProductionPlan Plan(ItemAmount goal, IWorldView world)
    {
        if (goal.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(goal), goal.Quantity, "A goal must ask for at least one unit.");
        }

        var state = new Expansion(world);
        state.Require(goal.Item, goal.Quantity, 0, new HashSet<SchematicId>());
        return state.Build(goal);
    }

    /// <summary>
    /// One recursive expansion. Holds the running budget of what is still unspent, so that two
    /// branches needing the same material cannot both claim it.
    /// </summary>
    private sealed class Expansion(IWorldView world)
    {
        private readonly Dictionary<ItemId, long> _budget = new();
        private readonly List<PlannedRun> _runs = new();
        private readonly List<PlannedTransfer> _transfers = new();
        private readonly Dictionary<(ItemId Item, StorageId From, StorageId To), int> _transferIndex = new();
        private readonly List<PlanShortage> _shortages = new();
        private readonly Dictionary<(ItemId, ShortageKind), int> _shortageIndex = new();
        private readonly Dictionary<ExecutorId, long> _facilityLoad = new();
        private readonly Dictionary<ExecutorId, long> _transportLoad = new();

        public void Require(ItemId item, long quantity, int depth, HashSet<SchematicId> visiting)
        {
            var deficit = quantity - Spend(item, quantity);
            if (deficit <= 0)
            {
                return;
            }

            if (depth >= MaxDepth)
            {
                Short(item, deficit, ShortageKind.CyclicSchematic);
                return;
            }

            var candidates = world.Schematics.UnlockedForOutput(item);
            if (candidates.Count == 0)
            {
                // A locked schematic and an unproducible raw material are not the same problem.
                // One is fixed by a mission, the other by hauling, and only one of them should
                // ever suggest an expedition.
                Short(
                    item,
                    deficit,
                    world.Schematics.ForOutput(item).Count > 0
                        ? ShortageKind.LockedSchematic
                        : ShortageKind.RawResource);
                return;
            }

            // The player may select among candidates, and an unlocked assistant may later choose
            // for them. Until either exists, the first in declaration order is the choice.
            var schematic = candidates[0];

            if (!visiting.Add(schematic.Id))
            {
                Short(item, deficit, ShortageKind.CyclicSchematic);
                return;
            }

            var facility = ChooseFacility(schematic.RequiredFacilityType);
            if (facility is null)
            {
                Short(item, deficit, ShortageKind.NoCompatibleExecutor);
                visiting.Remove(schematic.Id);
                return;
            }

            var runs = (deficit + schematic.Output.Quantity - 1) / schematic.Output.Quantity;

            // Reserved before recursing, not after. A branch expanded further down needing the
            // same facility type must see this work already claimed, or every branch in the plan
            // picks the same idle facility and the load-spreading does nothing.
            _facilityLoad[facility.Id] = _facilityLoad.GetValueOrDefault(facility.Id) + runs;

            foreach (var input in schematic.Inputs)
            {
                Require(input.Item, input.Quantity * runs, depth + 1, visiting);
                Move(input.Item, input.Quantity * runs, world.Hold, facility.LocalStorage);
            }

            // Recorded after its inputs, so the plan reads in the order the work has to happen:
            // the deepest branch first, the goal's own run last.
            _runs.Add(new PlannedRun(schematic.Id, facility.Id, (int)runs));

            var produced = runs * schematic.Output.Quantity;
            Move(schematic.Output.Item, produced, facility.LocalStorage, world.Hold);

            // A schematic that produces five at a time overshoots a deficit of three. The surplus
            // is real and stays available to any later branch that wants it.
            _budget[item] = _budget.GetValueOrDefault(item) + produced - deficit;

            visiting.Remove(schematic.Id);
        }

        public ProductionPlan Build(ItemAmount goal) =>
            new(goal, _runs, _transfers, _shortages);

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
            var bestLoad = long.MaxValue;

            // Definition order, and a strict comparison, so a tie always goes to the earlier
            // facility rather than to whichever the dictionary happened to hand back first.
            foreach (var facility in world.Facilities)
            {
                if (facility.Type != type)
                {
                    continue;
                }

                var load = facility.QueuedRuns + _facilityLoad.GetValueOrDefault(facility.Id);
                if (load < bestLoad)
                {
                    bestLoad = load;
                    best = facility;
                }
            }

            return best;
        }

        /// <summary>
        /// The least loaded line that actually runs this leg. Route first, load second: a line
        /// with an empty queue is no use for a journey it cannot make.
        /// </summary>
        private ExecutorId? ChooseTransport(StorageId from, StorageId to)
        {
            ExecutorId? best = null;
            var bestLoad = long.MaxValue;

            foreach (var line in world.TransportLines)
            {
                if (line.From != from || line.To != to)
                {
                    continue;
                }

                var load = line.QueuedTransfers + _transportLoad.GetValueOrDefault(line.Id);
                if (load < bestLoad)
                {
                    bestLoad = load;
                    best = line.Id;
                }
            }

            return best;
        }

        /// <summary>
        /// Adds to an existing leg of the same route rather than appending another line for it.
        /// Four runs of one schematic are one haul of sixty, not four hauls of fifteen.
        /// </summary>
        private void Move(ItemId item, long quantity, StorageId from, StorageId to)
        {
            if (quantity <= 0 || from == to)
            {
                return;
            }

            var key = (item, from, to);
            if (_transferIndex.TryGetValue(key, out var index))
            {
                var existing = _transfers[index];
                _transfers[index] = existing with { Quantity = existing.Quantity + quantity };
                return;
            }

            var line = ChooseTransport(from, to);
            if (line is null)
            {
                Short(item, quantity, ShortageKind.NoCompatibleExecutor);
                return;
            }

            _transferIndex[key] = _transfers.Count;
            _transfers.Add(new PlannedTransfer(item, quantity, from, to, line.Value));
            _transportLoad[line.Value] = _transportLoad.GetValueOrDefault(line.Value) + 1;
        }

        private void Short(ItemId item, long missing, ShortageKind kind)
        {
            var key = (item, kind);
            if (_shortageIndex.TryGetValue(key, out var index))
            {
                _shortages[index] = _shortages[index] with
                {
                    Missing = _shortages[index].Missing + missing,
                };
                return;
            }

            _shortageIndex[key] = _shortages.Count;
            _shortages.Add(new PlanShortage(item, missing, kind));
        }
    }
}
