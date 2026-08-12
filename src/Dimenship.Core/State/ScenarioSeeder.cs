using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.State;

/// <summary>
/// Turns a scenario into the world it describes. The only thing that ever reads a
/// <see cref="Scenario"/>.
/// <para>
/// The scenario itself is retained rather than consumed: it authors every node slot the campaign
/// will ever show, including the ones nothing is built in yet, and the graph is projected from the
/// scenario and the state together. What the seeder produces is only the half that changes.
/// </para>
/// </summary>
public static class ScenarioSeeder
{
    /// <summary>
    /// The seed every new game starts from until something chooses one. Fixed rather than drawn
    /// from a clock: the engine never calls a clock, a Guid, or an unseeded generator, and a new
    /// game that replays identically is worth more than one that surprises.
    /// </summary>
    public const ulong DefaultSeed = 0xD1_4E_45_41_1D_1E_51_00UL;

    public static WorldState Seed(ContentCatalog catalog, Scenario scenario, ulong seed = DefaultSeed)
    {
        var vessel = new VesselState
        {
            Hold = scenario.Hold,
            Energy = new EnergyLedger { Capacity = scenario.EnergyCapacity },
            Compute = new ComputeLedger(),
            Reservations = new ReservationLedger(),
        };

        foreach (var authored in scenario.Storages)
        {
            var storage = new StorageInstance
            {
                Id = authored.Id,
                Archetype = authored.Archetype,
                NameOverride = authored.NameOverride,
            };

            // Opening stock is a campaign's starting position, so it is copied into the instance
            // once and never read from the scenario again. Editing it in content changes what a
            // new game starts with and leaves an existing one alone, which is the point.
            foreach (var amount in authored.Initial)
            {
                storage.Stock.Add(new StoredItem(amount.Item, amount.Quantity));
            }

            vessel.Storages.Add(storage);
        }

        foreach (var authored in scenario.Facilities)
        {
            vessel.Facilities.Add(new FacilityInstance
            {
                Id = authored.Id,
                Archetype = authored.Archetype,
                NameOverride = authored.NameOverride,
                LocalStorage = authored.LocalStorage,
                Built = authored.BuiltAtStart,
                Configured = authored.InitialSchematic,
                Utilization = UtilizationWindow.Empty(),
            });
        }

        foreach (var authored in scenario.Routes)
        {
            vessel.Transports.Add(new TransportInstance
            {
                Id = authored.Id,
                Archetype = authored.Archetype,
                NameOverride = authored.NameOverride,
                From = authored.From,
                To = authored.To,
                Built = authored.BuiltAtStart,
            });
        }

        foreach (var sink in scenario.Sinks)
        {
            vessel.Sinks.Add(sink);
        }

        var state = new WorldState
        {
            ScenarioId = scenario.Id,
            Clock = new OperationalClock(),
            Random = RandomState.FromSeed(seed),
            Vessel = vessel,
            Tasks = new TaskRegistry(),
            Progress = new ProgressLedger(),
            Plans = new PlanRegistry(),
            Missions = new MissionLedger(),
            Alerts = new AlertLedger(),
            Journal = new JournalLedger(),
            Programs = new ProgramLedger(),
            Robots = new RobotLedger(),
            Case = new CaseLedger(),
        };

        foreach (var unlocked in scenario.UnlockedSchematics)
        {
            state.Progress.UnlockedSchematics.Add(unlocked);
        }

        // A passive facility is scheduled by nobody, so it carries no authored task — and it still
        // has to work. What it is configured with is its standing order, which is the read-only
        // source the GDD describes rather than a job nobody ordered.
        //
        // These are seeded before anything a scenario wrote, in facility declaration order,
        // because that is what they are: a standing order is what an executor does absent
        // instruction, so it exists before any instruction does.
        foreach (var facility in state.Vessel.Facilities)
        {
            var archetype = catalog.Facility(facility.Archetype);
            if (archetype is null || archetype.Commandable || !facility.Built)
            {
                continue;
            }

            if (facility.Configured is { } configured)
            {
                Queue(state, configured, null, facility.Id);
            }
        }

        foreach (var authored in scenario.InitialTasks)
        {
            Queue(state, authored.Schematic, authored.Runs, authored.Executor);
        }

        foreach (var authored in scenario.InitialTransfers)
        {
            var task = new TransportTask
            {
                Id = state.Tasks.Mint(),
                Item = authored.Item,
                RequestedQuantity = authored.Quantity,
                Source = authored.From,
                Destination = authored.To,
                ExecutorId = authored.Executor,
            };

            state.Tasks.Add(task);
            Line(state, authored.Executor)?.Queue.Add(task.Id);
        }

        return state;
    }

    private static void Queue(
        WorldState state, SchematicId schematic, int? runs, ExecutorId executor)
    {
        var task = new ProductionTask
        {
            Id = state.Tasks.Mint(),
            SchematicId = schematic,
            RequestedRuns = runs,
            ExecutorId = executor,
        };

        state.Tasks.Add(task);
        Facility(state, executor)?.Queue.Add(task.Id);
    }

    private static FacilityInstance? Facility(WorldState state, ExecutorId id)
    {
        foreach (var facility in state.Vessel.Facilities)
        {
            if (facility.Id == id)
            {
                return facility;
            }
        }

        return null;
    }

    private static TransportInstance? Line(WorldState state, ExecutorId id)
    {
        foreach (var line in state.Vessel.Transports)
        {
            if (line.Id == id)
            {
                return line;
            }
        }

        return null;
    }
}
