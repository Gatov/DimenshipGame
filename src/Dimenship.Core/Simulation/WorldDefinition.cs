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
    public static readonly ItemId Plate = new("plate");
    public static readonly ItemId Actuator = new("actuator");
    public static readonly ItemId Frame = new("frame");

    /// <summary>The one global hold. GDD Appendix 1: every route that is not a factory
    /// interconnect ends here.</summary>
    public static readonly StorageId CentralStorage = new("central_storage");

    // One buffer per facility. They are the storages a facility works out of, and they are drawn
    // inside its card rather than beside it — see BaseGraphNodes.
    public static readonly StorageId ExtractorBuffer = new("extractor_buffer");
    public static readonly StorageId ReactorABuffer = new("reactor_a_buffer");
    public static readonly StorageId ReactorBBuffer = new("reactor_b_buffer");
    public static readonly StorageId FactoryABuffer = new("factory_a_buffer");
    public static readonly StorageId FactoryBBuffer = new("factory_b_buffer");
    public static readonly StorageId FactoryCBuffer = new("factory_c_buffer");
    public static readonly StorageId BayAHold = new("bay_a_hold");
    public static readonly StorageId BayBHold = new("bay_b_hold");

    public static readonly SchematicId ExtractMatter = new("extract_matter");
    public static readonly SchematicId RefineAlloy = new("refine_alloy");
    public static readonly SchematicId PressPlate = new("press_plate");
    public static readonly SchematicId BuildActuator = new("build_actuator");
    public static readonly SchematicId AssembleFrame = new("assemble_frame");

    public static readonly ExecutorId Extractor01 = new("extractor_01");
    public static readonly ExecutorId ReactorA = new("reactor_a");
    public static readonly ExecutorId ReactorB = new("reactor_b");
    public static readonly ExecutorId FactoryA = new("factory_a");
    public static readonly ExecutorId FactoryB = new("factory_b");
    public static readonly ExecutorId FactoryC = new("factory_c");
    public static readonly ExecutorId BayA = new("bay_a");
    public static readonly ExecutorId BayB = new("bay_b");

    public static readonly ExecutorId ExtractorOut = new("extractor_out");
    public static readonly ExecutorId ReactorAFeed = new("reactor_a_feed");
    public static readonly ExecutorId ReactorAReturn = new("reactor_a_return");
    public static readonly ExecutorId ReactorBFeed = new("reactor_b_feed");
    public static readonly ExecutorId ReactorBReturn = new("reactor_b_return");
    public static readonly ExecutorId FactoryAFeed = new("factory_a_feed");
    public static readonly ExecutorId FactoryLinkAb = new("factory_link_ab");
    public static readonly ExecutorId FactoryLinkBc = new("factory_link_bc");
    public static readonly ExecutorId FactoryCReturn = new("factory_c_return");
    public static readonly ExecutorId BayASupply = new("bay_a_supply");
    public static readonly ExecutorId BayAReturn = new("bay_a_return");
    public static readonly ExecutorId BayBSupply = new("bay_b_supply");
    public static readonly ExecutorId BayBReturn = new("bay_b_return");

    /// <summary>
    /// The vessel the shell starts on, built to GDD Appendix 1: one global storage in the middle,
    /// an interconnected array of factories, a refining tier — here the two reactors — and launch
    /// bays joined to storage and to nothing else.
    /// <para>
    /// The chain runs extract, refine, fabricate: the extractor gathers matter from space into its
    /// own buffer, a line hauls it to central storage, the reactors draw it back out and return
    /// alloy, and the factories pass work along their interconnects — alloy to plate to actuator to
    /// drone frame — before the last of them returns finished frames to storage. Every facility
    /// works its own buffer rather than the hold, so nothing reaches a machine except by a route,
    /// which is what gives the graph edges worth colouring.
    /// </para>
    /// <para>
    /// Two things here are deliberately inert, because the systems behind them do not exist. A
    /// reactor refines and draws power; it does not make any, and <see cref="EnergyCapacity"/> is
    /// still a constant. A launch bay has no schematic at all, so it reports idle and its link to
    /// storage sits in the idle band — which is how the missing acquisition loop stays visible on
    /// the graph instead of being quietly drawn as if it worked.
    /// </para>
    /// </summary>
    public static WorldDefinition CreateDefault()
    {
        // Every facility takes 16 ticks a run at a work rate of 100, so a card's progress bar is
        // readable rather than flicking between empty and full, and a postponement is visible for
        // long enough to read the reason off the card.
        var effort = new WorkAmount(1_600);

        var schematics = new SchematicCatalog(
            new[]
            {
                new SchematicDefinition
                {
                    Id = ExtractMatter,
                    Output = new ItemAmount(Ore, 2_400),
                    Inputs = Array.Empty<ItemAmount>(),

                    // Six ticks a run, so 400 raw matter a tick against the 500 the two reactors
                    // want. The shortfall is the point: the extractor is the emergency source, and
                    // closing the gap is what the launch bays are for once missions exist.
                    EffortPerRun = new WorkAmount(600),
                    EnergyPerRun = new EnergyAmount(3_650),
                    RequiredFacilityType = FacilityType.Extractor,
                },
                new SchematicDefinition
                {
                    Id = RefineAlloy,
                    Output = new ItemAmount(Alloy, 800),
                    Inputs = new[] { new ItemAmount(Ore, 4_000) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Reactor,
                },

                // The factory chain is balanced link for link: each stage consumes exactly what the
                // one before it produces, so a stall anywhere shows up as a blocked card downstream
                // rather than as a buffer that quietly fills forever.
                new SchematicDefinition
                {
                    Id = PressPlate,
                    Output = new ItemAmount(Plate, 200),
                    Inputs = new[] { new ItemAmount(Alloy, 400) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Factory,
                },
                new SchematicDefinition
                {
                    Id = BuildActuator,
                    Output = new ItemAmount(Actuator, 100),
                    Inputs = new[] { new ItemAmount(Plate, 200) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Factory,
                },
                new SchematicDefinition
                {
                    Id = AssembleFrame,
                    Output = new ItemAmount(Frame, 50),
                    Inputs = new[] { new ItemAmount(Actuator, 100) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Factory,
                },
            },
            new[] { ExtractMatter, RefineAlloy, PressPlate, BuildActuator, AssembleFrame });

        return new WorldDefinition(
            // Standing draw is 7,700 — a 4,000 sink, 1,100 across eight facilities, and 200 apiece
            // for thirteen lines — and full production adds 2,108 on top of it: 608 extracting and
            // 300 for each of the five other facilities, since a schematic's energy is charged in
            // proportion to the work done in the tick. The vessel therefore peaks at 9,808 and
            // nothing is ever refused. The remaining reserve is deliberate: it is the room a
            // fuel-burning power core will need when capacity stops being a constant.
            EnergyCapacity: 10_000,
            Schematics: schematics,
            Items: new[]
            {
                new ItemDefinition(Ore, "Raw Matter", HoldCapacity: 2_000_000),
                new ItemDefinition(Alloy, "Refined Alloy", HoldCapacity: 500_000),
                new ItemDefinition(Plate, "Hull Plate", HoldCapacity: 200_000),
                new ItemDefinition(Actuator, "Micro Actuator", HoldCapacity: 120_000),
                new ItemDefinition(Frame, "Drone Frame", HoldCapacity: 60_000),
            },
            Storages: new[]
            {
                // The vessel starts stocked rather than empty: the chain has to be running when
                // the player first looks at it, and with no acquisition system there is nowhere
                // else the opening raw matter could come from. 800,000 against a net drain of 100
                // a tick is a little over two operational hours before the shortage bites.
                new StorageDefinition(
                    CentralStorage, "Central Storage", StorageDefinition.FullHold,
                    new[] { new ItemAmount(Ore, 800_000), new ItemAmount(Alloy, 40_000) }),

                // 25 permille of a full hold, for every facility alike: 50,000 raw matter, which is
                // a dozen reactor runs, down to 1,500 drone frames, which is thirty of them.
                new StorageDefinition(
                    ExtractorBuffer, "Extractor Buffer", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    ReactorABuffer, "Reactor Alpha Buffer", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    ReactorBBuffer, "Reactor Beta Buffer", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    FactoryABuffer, "Factory Alpha Buffer", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    FactoryBBuffer, "Factory Beta Buffer", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    FactoryCBuffer, "Factory Gamma Buffer", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    BayAHold, "Launch Bay Alpha Hold", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    BayBHold, "Launch Bay Beta Hold", 25, Array.Empty<ItemAmount>()),
            },
            Producers: new[]
            {
                new ProductionExecutorDefinition(
                    Extractor01, "Extractor 01", FacilityType.Extractor, ExtractorBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: ExtractMatter),

                new ProductionExecutorDefinition(
                    ReactorA, "Reactor Alpha", FacilityType.Reactor, ReactorABuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: RefineAlloy),
                new ProductionExecutorDefinition(
                    ReactorB, "Reactor Beta", FacilityType.Reactor, ReactorBBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: RefineAlloy),

                new ProductionExecutorDefinition(
                    FactoryA, "Factory Alpha", FacilityType.Factory, FactoryABuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: PressPlate),
                new ProductionExecutorDefinition(
                    FactoryB, "Factory Beta", FacilityType.Factory, FactoryBBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: BuildActuator),
                new ProductionExecutorDefinition(
                    FactoryC, "Factory Gamma", FacilityType.Factory, FactoryCBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: AssembleFrame),

                // No schematic exists for a launch bay, and none is configured. A bay reports idle
                // until missions are a system, and it draws the keep-the-lights-on 100 meanwhile.
                new ProductionExecutorDefinition(
                    BayA, "Launch Bay Alpha", FacilityType.LaunchBay, BayAHold,
                    WorkRatePerTick: 100, StandingPowerDraw: 100, SwitchOverTicks: 30,
                    InitialSchematic: null),
                new ProductionExecutorDefinition(
                    BayB, "Launch Bay Beta", FacilityType.LaunchBay, BayBHold,
                    WorkRatePerTick: 100, StandingPowerDraw: 100, SwitchOverTicks: 30,
                    InitialSchematic: null),
            },
            Transports: new[]
            {
                // Throughput is sized just above the stage each line serves — a few percent, not
                // a few multiples. The surplus is what lets a line catch up after a stall, and
                // keeping it small is what keeps a healthy line from spending its ticks parked on
                // a buffer it has already filled: the engine calls a line with nowhere to put its
                // load blocked, and the view draws a blocked line red. Sized close, a line moves on
                // nearly every tick and its colour reports load, which is what the edge is for.
                new TransportExecutorDefinition(
                    ExtractorOut, "Extractor Output", From: ExtractorBuffer, To: CentralStorage,
                    ThroughputPerTick: 420, StandingPowerDraw: 200),

                // 260 against the 250 a tick a reactor consumes, and the same margin down the
                // factory array, so haulage is never the binding constraint but never idles on a
                // full buffer either.
                new TransportExecutorDefinition(
                    ReactorAFeed, "Reactor Alpha Feed", From: CentralStorage, To: ReactorABuffer,
                    ThroughputPerTick: 260, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReactorAReturn, "Reactor Alpha Return", From: ReactorABuffer, To: CentralStorage,
                    ThroughputPerTick: 52, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReactorBFeed, "Reactor Beta Feed", From: CentralStorage, To: ReactorBBuffer,
                    ThroughputPerTick: 260, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReactorBReturn, "Reactor Beta Return", From: ReactorBBuffer, To: CentralStorage,
                    ThroughputPerTick: 52, StandingPowerDraw: 200),

                // The factory array takes alloy in at one end and hands frames back at the other.
                // Intermediates never visit storage: that is what "interconnected" buys, and it is
                // also why a plate has only one place to go and cannot be raced for by two lines.
                new TransportExecutorDefinition(
                    FactoryAFeed, "Factory Alpha Feed", From: CentralStorage, To: FactoryABuffer,
                    ThroughputPerTick: 26, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryLinkAb, "Factory Link A-B", From: FactoryABuffer, To: FactoryBBuffer,
                    ThroughputPerTick: 13, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryLinkBc, "Factory Link B-C", From: FactoryBBuffer, To: FactoryCBuffer,
                    ThroughputPerTick: 7, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryCReturn, "Factory Gamma Return", From: FactoryCBuffer, To: CentralStorage,
                    ThroughputPerTick: 4, StandingPowerDraw: 200),

                // Bays are joined to storage and to nothing else, per Appendix 1. Nothing is queued
                // on these four lines, so they sit idle — the honest reading of a dock that cannot
                // launch anything yet.
                new TransportExecutorDefinition(
                    BayASupply, "Bay Alpha Supply", From: CentralStorage, To: BayAHold,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    BayAReturn, "Bay Alpha Return", From: BayAHold, To: CentralStorage,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    BayBSupply, "Bay Beta Supply", From: CentralStorage, To: BayBHold,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    BayBReturn, "Bay Beta Return", From: BayBHold, To: CentralStorage,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
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
                new InitialTask(ExtractMatter, 1_000_000, Extractor01),
                new InitialTask(RefineAlloy, 1_000_000, ReactorA),
                new InitialTask(RefineAlloy, 1_000_000, ReactorB),
                new InitialTask(PressPlate, 1_000_000, FactoryA),
                new InitialTask(BuildActuator, 1_000_000, FactoryB),
                new InitialTask(AssembleFrame, 1_000_000, FactoryC),
            },
            InitialTransfers: new[]
            {
                new InitialTransfer(
                    Ore, 1_000_000_000, ExtractorBuffer, CentralStorage, ExtractorOut),

                new InitialTransfer(
                    Ore, 1_000_000_000, CentralStorage, ReactorABuffer, ReactorAFeed),
                new InitialTransfer(
                    Alloy, 1_000_000_000, ReactorABuffer, CentralStorage, ReactorAReturn),
                new InitialTransfer(
                    Ore, 1_000_000_000, CentralStorage, ReactorBBuffer, ReactorBFeed),
                new InitialTransfer(
                    Alloy, 1_000_000_000, ReactorBBuffer, CentralStorage, ReactorBReturn),

                new InitialTransfer(
                    Alloy, 1_000_000_000, CentralStorage, FactoryABuffer, FactoryAFeed),
                new InitialTransfer(
                    Plate, 1_000_000_000, FactoryABuffer, FactoryBBuffer, FactoryLinkAb),
                new InitialTransfer(
                    Actuator, 1_000_000_000, FactoryBBuffer, FactoryCBuffer, FactoryLinkBc),
                new InitialTransfer(
                    Frame, 1_000_000_000, FactoryCBuffer, CentralStorage, FactoryCReturn),
            });
    }
}
