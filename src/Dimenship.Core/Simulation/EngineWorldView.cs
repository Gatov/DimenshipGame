using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

/// <summary>
/// The live world as the planner sees it: built facilities and lines with their load, the free
/// stock in the main hold, the unlock set and workpiece acceptance. Split out of the engine (R2.2),
/// which still implements <see cref="IWorldView"/> by forwarding here, so every caller that drafts
/// against an engine is unchanged.
/// <para>
/// It reads the state, the catalog and the engine's stateless collaborators and nothing else. The
/// planner is pure over this seam; a view that could reach the engine's per-tick scratch could make
/// a draft depend on when in a tick it was asked for.
/// </para>
/// </summary>
internal sealed class EngineWorldView(
    ContentCatalog catalog, WorldState state, Resolver resolve, StorageMath storages, WorkpieceAcceptance acceptance)
    : IWorldView
{
    public SchematicCatalog Schematics => catalog.Schematics;

    public bool Accepts(StorageId storage, ItemId item) => acceptance.Accepts(storage, item);

    public StorageId Hold => state.Vessel.Hold;

    /// <summary>
    /// The unlock set is the world's, not the catalog's: it changes during play and differs
    /// between two players running the same build, which is the question that sorts campaign
    /// progress from rulebook.
    /// </summary>
    public bool IsUnlocked(SchematicId schematic) =>
        state.Progress.UnlockedSchematics.Contains(schematic);

    public IReadOnlyList<PlannerFacility> Facilities
    {
        get
        {
            var facilities = new List<PlannerFacility>(state.Vessel.Facilities.Count);
            foreach (var executor in state.Vessel.Facilities)
            {
                if (!executor.Built)
                {
                    continue;
                }

                // A passive source is listed, flagged, rather than left out: the planner never
                // chooses one, and a draft that names one is told it is not commandable instead of
                // being told it is unbuilt, which was true of nothing (#65 item 7).
                var archetype = resolve.Archetype(executor);

                var queued = 0L;
                var occupied = false;
                var setups = new List<SchematicId>();
                if (executor.Configured is { } configured)
                {
                    setups.Add(configured);
                }

                if (executor.SwitchTarget is { } target && state.Tasks.Task(target) is { } loading
                    && !setups.Contains(loading.Produce.Schematic))
                {
                    setups.Add(loading.Produce.Schematic);
                }

                foreach (var task in resolve.Queued(executor))
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
                        var effort = catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
                        var rate = resolve.WorkRate(executor);
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
                    resolve.WorkRate(executor),
                    archetype.Commandable,
                    setups,
                    resolve.SwitchOverTicks(executor)));
            }

            return facilities;
        }
    }

    public IReadOnlyList<PlannerTransport> TransportLines
    {
        get
        {
            var lines = new List<PlannerTransport>(state.Vessel.Transports.Count);
            foreach (var hauler in state.Vessel.Transports)
            {
                if (!hauler.Built)
                {
                    continue;
                }

                var queued = 0L;
                foreach (var task in resolve.Queued(hauler))
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
                    resolve.Throughput(hauler),
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
    public long InHold(ItemId item) => storages.Free(state.Vessel.Hold, item);
}

/// <summary>
/// A world as an amend plans against it: the inner view, except that the stock the amended plan
/// holds in the main hold counts as supply. That stock is the plan's own, and a replan that ignored
/// it would order production for material it is about to keep. A decorator over any view rather
/// than a second copy of the engine's, so the two cannot drift apart on anything but supply.
/// </summary>
internal sealed class AmendView(IWorldView inner, ClaimLedger claims, PlanId plan) : IWorldView
{
    public SchematicCatalog Schematics => inner.Schematics;

    public StorageId Hold => inner.Hold;

    public IReadOnlyList<PlannerFacility> Facilities => inner.Facilities;

    public IReadOnlyList<PlannerTransport> TransportLines => inner.TransportLines;

    public long InHold(ItemId item) => inner.InHold(item) + claims.Held(plan, inner.Hold, item);

    public bool IsUnlocked(SchematicId schematic) => inner.IsUnlocked(schematic);

    public bool Accepts(StorageId storage, ItemId item) => inner.Accepts(storage, item);
}
