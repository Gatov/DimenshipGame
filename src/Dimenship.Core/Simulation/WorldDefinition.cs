using Dimenship.Core.Production;

namespace Dimenship.Core.Simulation;

/// <summary>
/// A kind of item. <paramref name="HoldCapacity"/> is how much of it a full-sized storage holds;
/// a smaller storage holds a fraction of that (see <see cref="StorageDefinition"/>).
/// </summary>
public sealed record ItemDefinition(ItemId Id, string Label, long HoldCapacity);

/// <summary>
/// A place items sit. Capacity is expressed as a permille of each item's
/// <see cref="ItemDefinition.HoldCapacity"/> rather than as a per-item table: a facility's local
/// buffer is "one percent of a hold" for every item at once, which is one number instead of a
/// table that has to be revisited every time an item is added.
/// <para>
/// Per-item overrides are a deliberate omission. Nothing in the production model needs them, and
/// adding them later changes no call site.
/// </para>
/// </summary>
public sealed record StorageDefinition(
    StorageId Id,
    string Label,
    long CapacityPermille,
    IReadOnlyList<ItemAmount> Initial)
{
    public const long FullHold = 1000;
}

/// <summary>
/// A production facility: it owns a queue, a local storage it draws from and deposits into, and
/// a configuration it keeps between runs.
/// <para>
/// <paramref name="StandingPowerDraw"/> is a flat per-tick cost, the same whether the facility is
/// idle, switching over or running. A schematic's energy is charged on top of it, in proportion
/// to the work actually done — so reconfiguration, which does no work, costs only this.
/// </para>
/// <paramref name="SwitchOverTicks"/> is a per-facility constant: any change of schematic is a
/// full standard reconfiguration, not a cost that varies by recipe.
/// </summary>
public sealed record ProductionExecutorDefinition(
    ExecutorId Id,
    string Label,
    FacilityType Type,
    StorageId LocalStorage,
    long WorkRatePerTick,
    long StandingPowerDraw,
    long SwitchOverTicks,
    SchematicId? InitialSchematic);

/// <summary>
/// A transport line. It owns a queue and moves up to <paramref name="ThroughputPerTick"/> units
/// of one item per tick along a fixed route, from <paramref name="From"/> to
/// <paramref name="To"/>.
/// <para>
/// The route is a property of the line, not of the transfer riding it: a line is built between
/// two places and carries whatever is handed to it, but only between those two places. Without
/// that, any line could serve any transfer, which leaves no topology to draw and no constraint to
/// plan against. A two-way link is two definitions.
/// </para>
/// <para>
/// It has no configuration and so no reconfiguration cost. Its energy is the standing draw alone —
/// a per-unit haulage charge is a refinement the specifications do not ask for.
/// </para>
/// </summary>
public sealed record TransportExecutorDefinition(
    ExecutorId Id,
    string Label,
    StorageId From,
    StorageId To,
    long ThroughputPerTick,
    long StandingPowerDraw);

/// <summary>
/// Something that draws power and does nothing else, such as the stabilization field. It owns no
/// queue and executes no schematic, so forcing it through <see cref="FacilityType"/> would be a
/// fiction.
/// </summary>
public sealed record PowerSinkDefinition(string Id, string Label, long PowerDraw);

/// <summary>A task the world starts with, queued on the named executor before the first tick.</summary>
public sealed record InitialTask(SchematicId Schematic, int Runs, ExecutorId Executor);

/// <summary>A transfer the world starts with, queued on the named transport line.</summary>
public sealed record InitialTransfer(
    ItemId Item, long Quantity, StorageId From, StorageId To, ExecutorId Executor);

/// <summary>
/// The starting configuration of a world. Every list's order is significant: it is the order
/// executors claim power and are stepped in, and the order every projection on the snapshot is
/// built in. That ordering is what makes the simulation deterministic.
/// </summary>
public sealed record WorldDefinition(
    long EnergyCapacity,
    SchematicCatalog Schematics,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<StorageDefinition> Storages,
    IReadOnlyList<ProductionExecutorDefinition> Producers,
    IReadOnlyList<TransportExecutorDefinition> Transports,
    IReadOnlyList<PowerSinkDefinition> Sinks,
    IReadOnlyList<InitialTask> InitialTasks,
    IReadOnlyList<InitialTransfer> InitialTransfers)
{
    public static readonly ItemId Ore = new("ore");
    public static readonly ItemId Alloy = new("alloy");

    public static readonly StorageId MainHold = new("main_hold");
    public static readonly StorageId SmelterBuffer = new("smelter_buffer");

    public static readonly SchematicId ExtractOre = new("extract_ore");
    public static readonly SchematicId SmeltAlloy = new("smelt_alloy");

    public static readonly ExecutorId Extractor01 = new("extractor_01");
    public static readonly ExecutorId SmelterA = new("smelter_a");
    public static readonly ExecutorId FeedLine = new("feed_line");
    public static readonly ExecutorId ReturnLine = new("return_line");

    /// <summary>
    /// The vessel the shell starts on. Effort equals work rate for both schematics, so a run
    /// takes one tick and the world produces at the rates it did before the production model
    /// existed — the point being that the shell's panels keep showing a live, occasionally
    /// blocked vessel rather than an empty one.
    /// <para>
    /// The smelter works its own buffer rather than the hold, so ore only reaches it because a
    /// transport line carries it there and alloy only leaves because another carries it back.
    /// That is the whole chain — produce, haul, consume, haul — running from the first tick.
    /// </para>
    /// </summary>
    public static WorldDefinition CreateDefault()
    {
        var schematics = new SchematicCatalog(
            new[]
            {
                new SchematicDefinition
                {
                    Id = ExtractOre,
                    Output = new ItemAmount(Ore, 2_400),
                    Inputs = Array.Empty<ItemAmount>(),
                    EffortPerRun = new WorkAmount(100),
                    EnergyPerRun = new EnergyAmount(3_650),
                    RequiredFacilityType = FacilityType.Extractor,
                },
                new SchematicDefinition
                {
                    Id = SmeltAlloy,
                    Output = new ItemAmount(Alloy, 8_000),
                    Inputs = new[] { new ItemAmount(Ore, 40_000) },
                    EffortPerRun = new WorkAmount(100),
                    EnergyPerRun = new EnergyAmount(1_650),
                    RequiredFacilityType = FacilityType.Refinery,
                },
            },
            new[] { ExtractOre, SmeltAlloy });

        return new WorldDefinition(
            // 4,000 sink, 150 + 3,650 extracting, 150 + 1,650 smelting, and 200 apiece for the
            // two transport lines comes to exactly 10,000: the vessel reaches its cap on every
            // tick the smelter runs, and never exceeds it.
            EnergyCapacity: 10_000,
            Schematics: schematics,
            Items: new[]
            {
                new ItemDefinition(Ore, "Ore", HoldCapacity: 2_000_000),
                new ItemDefinition(Alloy, "Alloy", HoldCapacity: 500_000),
            },
            Storages: new[]
            {
                new StorageDefinition(
                    MainHold, "Main Hold", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),

                // 25 permille of the hold: 50,000 ore, which is one smelter run and a little
                // over, and 12,500 alloy, which is rather more than one run produces.
                new StorageDefinition(
                    SmelterBuffer, "Smelter Buffer", 25, Array.Empty<ItemAmount>()),
            },
            Producers: new[]
            {
                new ProductionExecutorDefinition(
                    Extractor01, "Extractor 01", FacilityType.Extractor, MainHold,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: ExtractOre),

                // Needs about seventeen ticks of extractor output per run, so it alternates
                // between postponed and running rather than sitting in one state.
                new ProductionExecutorDefinition(
                    SmelterA, "Smelter A", FacilityType.Refinery, SmelterBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: SmeltAlloy),
            },
            Transports: new[]
            {
                new TransportExecutorDefinition(
                    FeedLine, "Feed Line", From: MainHold, To: SmelterBuffer,
                    ThroughputPerTick: 4_000, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReturnLine, "Return Line", From: SmelterBuffer, To: MainHold,
                    ThroughputPerTick: 4_000, StandingPowerDraw: 200),
            },
            Sinks: new[]
            {
                // Draws power unconditionally and produces nothing. GDD: the stabilization
                // field is the vessel's permanent energy sink.
                new PowerSinkDefinition("stabilization_field", "Stabilization Field", 4_000),
            },
            InitialTasks: new[]
            {
                // A stand-in for the standing order the specifications do not yet describe:
                // enough runs that the vessel keeps working far longer than any session.
                new InitialTask(ExtractOre, 1_000_000, Extractor01),
                new InitialTask(SmeltAlloy, 1_000_000, SmelterA),
            },
            InitialTransfers: new[]
            {
                new InitialTransfer(Ore, 1_000_000_000, MainHold, SmelterBuffer, FeedLine),
                new InitialTransfer(Alloy, 1_000_000_000, SmelterBuffer, MainHold, ReturnLine),
            });
    }
}
