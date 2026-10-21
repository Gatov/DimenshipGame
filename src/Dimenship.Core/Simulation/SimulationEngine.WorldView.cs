using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    SchematicCatalog IWorldView.Schematics => Catalog.Schematics;

    bool IWorldView.Accepts(StorageId storage, ItemId item) => Acceptance.Accepts(storage, item);

    StorageId IWorldView.Hold => State.Vessel.Hold;

    /// <summary>
    /// The seam the planner asks through. The unlock set is the world's, not the catalog's: it
    /// changes during play and differs between two players running the same build, which is the
    /// question that sorts campaign progress from rulebook.
    /// </summary>
    public bool IsUnlocked(SchematicId schematic) =>
        State.Progress.UnlockedSchematics.Contains(schematic);

    IReadOnlyList<PlannerFacility> IWorldView.Facilities
    {
        get
        {
            var facilities = new List<PlannerFacility>(State.Vessel.Facilities.Count);
            foreach (var executor in State.Vessel.Facilities)
            {
                if (!executor.Built)
                {
                    continue;
                }

                // A passive source is listed, flagged, rather than left out: the planner never
                // chooses one, and a draft that names one is told it is not commandable instead of
                // being told it is unbuilt, which was true of nothing (#65 item 7).
                var archetype = Archetype(executor);

                var queued = 0L;
                var occupied = false;
                var setups = new List<SchematicId>();
                if (executor.Configured is { } configured)
                {
                    setups.Add(configured);
                }

                if (executor.SwitchTarget is { } target && State.Tasks.Task(target) is { } loading
                    && !setups.Contains(loading.Produce.Schematic))
                {
                    setups.Add(loading.Produce.Schematic);
                }

                foreach (var task in Queued(executor))
                {
                    if (task.IsFinished)
                    {
                        continue;
                    }

                    if (!setups.Contains(task.Produce.Schematic))
                    {
                        setups.Add(task.Produce.Schematic);
                    }

                    // A standing order has no remaining run count to add up. Expressing it as one
                    // would mean choosing a large number, which is the placeholder this replaced.
                    if (task.Produce.Runs is { } requested)
                    {
                        var effort = Catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
                        var rate = WorkRate(executor);
                        queued += (requested - task.CompletedRuns) * IntMath.CeilDiv(effort, rate);
                    }
                    else
                    {
                        occupied = true;
                    }
                }

                facilities.Add(new PlannerFacility(
                    executor.Id,
                    archetype.Type,
                    executor.LocalStorage,
                    queued,
                    occupied,
                    WorkRate(executor),
                    archetype.Commandable,
                    setups,
                    SwitchOverTicks(executor)));
            }

            return facilities;
        }
    }

    IReadOnlyList<PlannerTransport> IWorldView.TransportLines
    {
        get
        {
            var lines = new List<PlannerTransport>(State.Vessel.Transports.Count);
            foreach (var hauler in State.Vessel.Transports)
            {
                if (!hauler.Built)
                {
                    continue;
                }

                var queued = 0L;
                foreach (var task in Queued(hauler))
                {
                    if (!task.IsFinished)
                    {
                        queued++;
                    }
                }

                lines.Add(new PlannerTransport(
                    hauler.Id,
                    hauler.From,
                    hauler.To,
                    queued,
                    Throughput(hauler),
                    hauler.LengthTicks));
            }

            return lines;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Free stock: what is in the hold less what is held there for committed plans (K6b). Stock
    /// held for another plan is stock this plan cannot take, so counting it would plan a haul that
    /// waits on <see cref="PostponeReason.MaterialClaimed"/> for good, which is the race the claims
    /// exist to end.
    /// </remarks>
    public long InHold(ItemId item) => Free(State.Vessel.Hold, item);

    /// <summary>
    /// The live world as an amend plans against it: the engine's view, except that the stock the
    /// amended plan holds in the main hold counts as supply. That stock is the plan's own, and a
    /// replan that ignored it would order production for material it is about to keep.
    /// </summary>
    private sealed class AmendView(SimulationEngine engine, PlanId plan) : IWorldView
    {
        private IWorldView Engine => engine;

        public SchematicCatalog Schematics => Engine.Schematics;

        public StorageId Hold => Engine.Hold;

        public IReadOnlyList<PlannerFacility> Facilities => Engine.Facilities;

        public IReadOnlyList<PlannerTransport> TransportLines => Engine.TransportLines;

        public long InHold(ItemId item) => Engine.InHold(item) + engine.State.Claims.Held(plan, Engine.Hold, item);

        public bool IsUnlocked(SchematicId schematic) => Engine.IsUnlocked(schematic);

        public bool Accepts(StorageId storage, ItemId item) => Engine.Accepts(storage, item);
    }
}
