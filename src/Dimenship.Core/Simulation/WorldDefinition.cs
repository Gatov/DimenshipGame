using Dimenship.Core.Content;
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
public sealed record PowerSinkDefinition(PowerSinkId Id, string Label, long PowerDraw);

/// <summary>
/// A task the world starts with, queued on the named executor before the first tick.
/// <para>
/// <c>Runs</c> is null for a standing order: run this schematic for as long as its inputs keep
/// arriving. That is a different thing from a very large run count, and the difference is
/// arithmetic — see <see cref="SimulationEngine.Uncommitted"/>.
/// </para>
/// </summary>
public sealed record InitialTask(SchematicId Schematic, int? Runs, ExecutorId Executor);

/// <summary>
/// A transfer the world starts with, queued on the named transport line. <c>Quantity</c> is null
/// for a standing order: haul this item down this line for as long as there is any to haul.
/// </summary>
public sealed record InitialTransfer(
    ItemId Item, long? Quantity, StorageId From, StorageId To, ExecutorId Executor);

/// <summary>
/// The starting configuration of a world. Every list's order is significant: it is the order
/// executors claim power and are stepped in, and the order every projection on the snapshot is
/// built in. That ordering is what makes the simulation deterministic.
/// </summary>
public sealed record WorldDefinition(
    long EnergyCapacity,
    SchematicCatalog Schematics,

    /// <summary>
    /// Which schematics the player may build from. Campaign progress, not rulebook: it changes
    /// during play, so it sits on the world rather than inside the catalog it indexes.
    /// </summary>
    IReadOnlyList<SchematicId> UnlockedSchematics,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<StorageDefinition> Storages,
    IReadOnlyList<ProductionExecutorDefinition> Producers,
    IReadOnlyList<TransportExecutorDefinition> Transports,
    IReadOnlyList<PowerSinkDefinition> Sinks,
    IReadOnlyList<InitialTask> InitialTasks,
    IReadOnlyList<InitialTransfer> InitialTransfers)
{
    /// <summary>
    /// What missions recover: one bulk material with a composition profile, rather than a dozen
    /// ores. GDD §5.8 — it is what makes a reactor's processing mode a decision instead of a
    /// formality.
    /// </summary>
    public static readonly ItemId MatterMix = new("matter_mix");

    /// <summary>What the emergency extractor gathers, and the only material the vessel can make
    /// without a mission.</summary>
    public static readonly ItemId Hydrogen = new("hydrogen");

    // The reactors' standardized outputs. Rare Metals, Chemical Feedstock and Phase Materials are
    // named by the GDD and absent here: no schematic on this vessel consumes them, and an item
    // nothing produces or consumes is a row of zeroes on every storage panel.
    public static readonly ItemId BasicMetals = new("basic_metals");
    public static readonly ItemId TechnicalMaterials = new("technical_materials");

    // What the factories build.
    public static readonly ItemId Component = new("component");
    public static readonly ItemId Module = new("module");
    public static readonly ItemId RobotFrame = new("robot_frame");

    /// <summary>The one global buffer. GDD §5.8: every route that is not a factory interconnect
    /// ends here.</summary>
    public static readonly StorageId ResourceStorage = new("resource_storage");

    // One buffer per facility. They are the storages a facility works out of, and they are drawn
    // inside its card rather than beside it — see BaseGraphNodes.
    public static readonly StorageId ExtractorBuffer = new("extractor_buffer");
    public static readonly StorageId ReactorABuffer = new("reactor_a_buffer");
    public static readonly StorageId ReactorBBuffer = new("reactor_b_buffer");
    public static readonly StorageId FactoryABuffer = new("factory_a_buffer");
    public static readonly StorageId FactoryBBuffer = new("factory_b_buffer");
    public static readonly StorageId FactoryCBuffer = new("factory_c_buffer");
    public static readonly StorageId DockAHold = new("dock_a_hold");
    public static readonly StorageId DockBHold = new("dock_b_hold");

    /// <summary>The vessel's permanent energy sink. It draws and produces nothing.</summary>
    public static readonly PowerSinkId StabilizationField = new("stabilization_field");

    public static readonly SchematicId ExtractHydrogen = new("extract_hydrogen");

    // A reactor's processing modes: the same Matter Mix separated to favour one output or another,
    // plus the emergency synthesis that runs off hydrogen when there is no Matter Mix left.
    public static readonly SchematicId SeparateBasic = new("separate_basic");
    public static readonly SchematicId SeparateTechnical = new("separate_technical");
    public static readonly SchematicId SynthesizeBasic = new("synthesize_basic");

    public static readonly SchematicId PressComponents = new("press_components");
    public static readonly SchematicId AssembleModules = new("assemble_modules");
    public static readonly SchematicId AssembleFrames = new("assemble_frames");

    public static readonly ExecutorId Extractor01 = new("extractor_01");
    public static readonly ExecutorId ReactorA = new("reactor_a");
    public static readonly ExecutorId ReactorB = new("reactor_b");
    public static readonly ExecutorId FactoryA = new("factory_a");
    public static readonly ExecutorId FactoryB = new("factory_b");
    public static readonly ExecutorId FactoryC = new("factory_c");
    public static readonly ExecutorId DockA = new("dock_a");
    public static readonly ExecutorId DockB = new("dock_b");

    public static readonly ExecutorId ExtractorOut = new("extractor_out");
    public static readonly ExecutorId ReactorAFeed = new("reactor_a_feed");
    public static readonly ExecutorId ReactorAReturn = new("reactor_a_return");
    public static readonly ExecutorId ReactorBFeed = new("reactor_b_feed");
    public static readonly ExecutorId ReactorBReturn = new("reactor_b_return");
    public static readonly ExecutorId FactoryAFeed = new("factory_a_feed");
    public static readonly ExecutorId FactoryBFeed = new("factory_b_feed");
    public static readonly ExecutorId FactoryLinkAb = new("factory_link_ab");
    public static readonly ExecutorId FactoryLinkBc = new("factory_link_bc");
    public static readonly ExecutorId FactoryCReturn = new("factory_c_return");
    public static readonly ExecutorId DockASupply = new("dock_a_supply");
    public static readonly ExecutorId DockAReturn = new("dock_a_return");
    public static readonly ExecutorId DockBSupply = new("dock_b_supply");
    public static readonly ExecutorId DockBReturn = new("dock_b_return");

    /// <summary>
    /// The vessel the shell starts on, built to the GDD's production layer: one global Resource
    /// Storage in the middle, an interconnected array of factories, Matter Reactors as the
    /// separating tier, and Mission Docks joined to storage and to nothing else.
    /// <para>
    /// The flow the GDD names is <c>missions → docks → storage → matter reactors → storage →
    /// factories → storage</c>. Missions do not exist, so the vessel starts stocked with the Matter
    /// Mix they would have recovered: the reactors separate it into Basic Metals and Technical
    /// Materials, and the factory array passes work along its interconnects — components, then
    /// modules, then robot frames — before returning the finished frames to storage. Every facility
    /// works its own buffer rather than the hold, so nothing reaches a machine except by a route,
    /// which is what gives the graph edges worth colouring.
    /// </para>
    /// <para>
    /// Two things here are deliberately inert, because the systems behind them do not exist. A
    /// Matter Reactor separates and draws power; it does not make any, and
    /// <see cref="EnergyCapacity"/> is still a constant. A Mission Dock has no schematic at all, so
    /// it reports idle and its link to storage sits in the idle band — which is how the missing
    /// acquisition loop stays visible on the graph instead of being quietly drawn as if it worked.
    /// </para>
    /// </summary>
    public static WorldDefinition CreateDefault()
    {
        // Every reactor and factory takes 16 ticks a run at a work rate of 100, so a card's progress
        // bar is readable rather than flicking between empty and full, and a postponement is visible
        // for long enough to read the reason off the card.
        var effort = new WorkAmount(1_600);

        var schematics = new SchematicCatalog(
            new[]
            {
                new SchematicDefinition
                {
                    Id = ExtractHydrogen,
                    Output = new ItemAmount(Hydrogen, 240),
                    Inputs = Array.Empty<ItemAmount>(),

                    // Sixty ticks a run — four hydrogen a tick. The GDD calls the rate very low on
                    // purpose: this is the recovery floor, not a supply line, and a vessel that
                    // could live off it would have no reason to run missions.
                    EffortPerRun = new WorkAmount(6_000),
                    EnergyPerRun = new EnergyAmount(3_650),
                    RequiredFacilityType = FacilityType.Extractor,
                },

                // The reactor's two ordinary processing modes. Same input, same effort, different
                // output: choosing between them is the optimization the GDD asks a reactor to
                // offer, and it is why the two reactors are configured differently below.
                new SchematicDefinition
                {
                    Id = SeparateBasic,
                    Output = new ItemAmount(BasicMetals, 800),
                    Inputs = new[] { new ItemAmount(MatterMix, 4_000) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.MatterReactor,
                },
                new SchematicDefinition
                {
                    Id = SeparateTechnical,
                    Output = new ItemAmount(TechnicalMaterials, 400),
                    Inputs = new[] { new ItemAmount(MatterMix, 4_000) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.MatterReactor,
                },

                // Emergency synthesis: thirty times the input for half the output, at four times the
                // energy and twice the time. Defined and unconfigured — nothing selects a schematic
                // yet — so it sits in the catalog as the path back the GDD's recovery invariant
                // requires, and the hydrogen accumulating in storage is what would pay for it.
                new SchematicDefinition
                {
                    Id = SynthesizeBasic,
                    Output = new ItemAmount(BasicMetals, 400),
                    Inputs = new[] { new ItemAmount(Hydrogen, 12_000) },
                    EffortPerRun = new WorkAmount(3_200),
                    EnergyPerRun = new EnergyAmount(19_200),
                    RequiredFacilityType = FacilityType.MatterReactor,
                },

                // The factory chain is balanced link for link: each stage consumes exactly what the
                // one before it produces, so a stall shows up as a blocked card downstream rather
                // than as a buffer that quietly fills forever.
                new SchematicDefinition
                {
                    Id = PressComponents,
                    Output = new ItemAmount(Component, 200),
                    Inputs = new[] { new ItemAmount(BasicMetals, 400) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Factory,
                },

                // The one stage that draws on both reactor modes, which is what makes the second
                // mode worth running and the array an array rather than a queue.
                new SchematicDefinition
                {
                    Id = AssembleModules,
                    Output = new ItemAmount(Module, 100),
                    Inputs = new[]
                    {
                        new ItemAmount(Component, 200),
                        new ItemAmount(TechnicalMaterials, 100),
                    },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Factory,
                },
                new SchematicDefinition
                {
                    Id = AssembleFrames,
                    Output = new ItemAmount(RobotFrame, 50),
                    Inputs = new[] { new ItemAmount(Module, 100) },
                    EffortPerRun = effort,
                    EnergyPerRun = new EnergyAmount(4_800),
                    RequiredFacilityType = FacilityType.Factory,
                },
            });

        return new WorldDefinition(
            // Standing draw is 7,900 — a 4,000 sink, 1,100 across eight facilities, and 200 apiece
            // for fourteen lines — and full production adds about 1,560 on top of it: 61 extracting,
            // and 300 for each of the five facilities on the chain, since a schematic's energy is
            // charged in proportion to the work done in the tick. The vessel therefore peaks around
            // 9,460 and nothing is ever refused. The reserve above that is deliberate: it is the
            // room a fuel-burning Power Core will need when capacity stops being a constant, and
            // the room the emergency synthesis needs on the day it is the only thing running.
            EnergyCapacity: 10_000,
            Schematics: schematics,
            UnlockedSchematics: new[]
            {
                ExtractHydrogen, SeparateBasic, SeparateTechnical, SynthesizeBasic,
                PressComponents, AssembleModules, AssembleFrames,
            },
            Items: new[]
            {
                new ItemDefinition(MatterMix, "Matter Mix", HoldCapacity: 5_000_000),
                new ItemDefinition(Hydrogen, "Hydrogen", HoldCapacity: 2_000_000),
                new ItemDefinition(BasicMetals, "Basic Metals", HoldCapacity: 500_000),
                new ItemDefinition(TechnicalMaterials, "Technical Materials", HoldCapacity: 300_000),
                new ItemDefinition(Component, "Component", HoldCapacity: 200_000),
                new ItemDefinition(Module, "Robot Module", HoldCapacity: 120_000),
                new ItemDefinition(RobotFrame, "Robot Frame", HoldCapacity: 60_000),
            },
            Storages: new[]
            {
                // The vessel starts stocked rather than empty: the chain has to be running when the
                // player first looks at it, and with no missions there is nowhere else the opening
                // Matter Mix could come from. 3,600,000 against the 500 a tick the two reactors
                // separate is two operational hours before the shortage bites.
                new StorageDefinition(
                    ResourceStorage, "Resource Storage", StorageDefinition.FullHold,
                    new[]
                    {
                        new ItemAmount(MatterMix, 3_600_000),
                        new ItemAmount(BasicMetals, 40_000),
                    }),

                // 25 permille of a full hold, for every facility alike: 125,000 Matter Mix, which is
                // thirty reactor runs, down to 1,500 robot frames, which is thirty of them.
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
                    DockAHold, "Mission Dock Alpha Hold", 25, Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    DockBHold, "Mission Dock Beta Hold", 25, Array.Empty<ItemAmount>()),
            },
            Producers: new[]
            {
                // Passive by the GDD's rule — no player program may disable it — and modelled as an
                // ordinary facility because nothing yet can command any facility at all. What keeps
                // it honest is that it is configured once, here, and never reconfigured.
                new ProductionExecutorDefinition(
                    Extractor01, "Emergency Hydrogen Extractor", FacilityType.Extractor,
                    ExtractorBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: ExtractHydrogen),

                // Two reactors, two modes. The array's output mix is a consequence of how they are
                // configured, which is the decision the GDD wants a reactor to present.
                new ProductionExecutorDefinition(
                    ReactorA, "Matter Reactor Alpha", FacilityType.MatterReactor, ReactorABuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: SeparateBasic),
                new ProductionExecutorDefinition(
                    ReactorB, "Matter Reactor Beta", FacilityType.MatterReactor, ReactorBBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: SeparateTechnical),

                new ProductionExecutorDefinition(
                    FactoryA, "Factory Alpha", FacilityType.Factory, FactoryABuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: PressComponents),
                new ProductionExecutorDefinition(
                    FactoryB, "Factory Beta", FacilityType.Factory, FactoryBBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: AssembleModules),
                new ProductionExecutorDefinition(
                    FactoryC, "Factory Gamma", FacilityType.Factory, FactoryCBuffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 150, SwitchOverTicks: 30,
                    InitialSchematic: AssembleFrames),

                // No schematic exists for a mission dock, and none is configured. A dock reports
                // idle until missions are a system, and it draws the keep-the-lights-on 100
                // meanwhile.
                new ProductionExecutorDefinition(
                    DockA, "Mission Dock Alpha", FacilityType.MissionDock, DockAHold,
                    WorkRatePerTick: 100, StandingPowerDraw: 100, SwitchOverTicks: 30,
                    InitialSchematic: null),
                new ProductionExecutorDefinition(
                    DockB, "Mission Dock Beta", FacilityType.MissionDock, DockBHold,
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
                    ExtractorOut, "Extractor Output", From: ExtractorBuffer, To: ResourceStorage,
                    ThroughputPerTick: 5, StandingPowerDraw: 200),

                // 260 against the 250 a tick a reactor consumes, and the same margin down the
                // factory array, so haulage is never the binding constraint but never idles on a
                // full buffer either.
                new TransportExecutorDefinition(
                    ReactorAFeed, "Reactor Alpha Feed", From: ResourceStorage, To: ReactorABuffer,
                    ThroughputPerTick: 260, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReactorAReturn, "Reactor Alpha Return", From: ReactorABuffer, To: ResourceStorage,
                    ThroughputPerTick: 52, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReactorBFeed, "Reactor Beta Feed", From: ResourceStorage, To: ReactorBBuffer,
                    ThroughputPerTick: 260, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    ReactorBReturn, "Reactor Beta Return", From: ReactorBBuffer, To: ResourceStorage,
                    ThroughputPerTick: 26, StandingPowerDraw: 200),

                // The factory array takes Basic Metals in at one end and hands robot frames back at
                // the other, with Technical Materials joining it at the middle stage. Intermediates
                // never visit storage: that is what "interconnected" buys, and it is also why a
                // component has only one place to go and cannot be raced for by two lines.
                new TransportExecutorDefinition(
                    FactoryAFeed, "Factory Alpha Feed", From: ResourceStorage, To: FactoryABuffer,
                    ThroughputPerTick: 26, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryBFeed, "Factory Beta Feed", From: ResourceStorage, To: FactoryBBuffer,
                    ThroughputPerTick: 7, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryLinkAb, "Factory Link A-B", From: FactoryABuffer, To: FactoryBBuffer,
                    ThroughputPerTick: 13, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryLinkBc, "Factory Link B-C", From: FactoryBBuffer, To: FactoryCBuffer,
                    ThroughputPerTick: 7, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    FactoryCReturn, "Factory Gamma Return", From: FactoryCBuffer, To: ResourceStorage,
                    ThroughputPerTick: 4, StandingPowerDraw: 200),

                // Docks are joined to storage and to nothing else, per the GDD. Nothing is queued on
                // these four lines, so they sit idle — the honest reading of a dock that cannot
                // launch anything yet.
                new TransportExecutorDefinition(
                    DockASupply, "Dock Alpha Supply", From: ResourceStorage, To: DockAHold,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    DockAReturn, "Dock Alpha Return", From: DockAHold, To: ResourceStorage,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    DockBSupply, "Dock Beta Supply", From: ResourceStorage, To: DockBHold,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
                new TransportExecutorDefinition(
                    DockBReturn, "Dock Beta Return", From: DockBHold, To: ResourceStorage,
                    ThroughputPerTick: 500, StandingPowerDraw: 200),
            },
            Sinks: new[]
            {
                // Draws power unconditionally and produces nothing. GDD: the stabilization
                // array is the vessel's permanent energy sink.
                new PowerSinkDefinition(StabilizationField, "Stabilization Array", 4_000),
            },
            InitialTasks: new[]
            {
                // Standing orders, stated as such. Null is not "a very large number of runs": an
                // indefinite task claims nothing beyond the run it is executing, which is what
                // makes the vessel's opening stock spendable by anything else.
                new InitialTask(ExtractHydrogen, null, Extractor01),
                new InitialTask(SeparateBasic, null, ReactorA),
                new InitialTask(SeparateTechnical, null, ReactorB),
                new InitialTask(PressComponents, null, FactoryA),
                new InitialTask(AssembleModules, null, FactoryB),
                new InitialTask(AssembleFrames, null, FactoryC),
            },
            InitialTransfers: new[]
            {
                new InitialTransfer(
                    Hydrogen, null, ExtractorBuffer, ResourceStorage, ExtractorOut),

                new InitialTransfer(
                    MatterMix, null, ResourceStorage, ReactorABuffer, ReactorAFeed),
                new InitialTransfer(
                    BasicMetals, null, ReactorABuffer, ResourceStorage, ReactorAReturn),
                new InitialTransfer(
                    MatterMix, null, ResourceStorage, ReactorBBuffer, ReactorBFeed),
                new InitialTransfer(
                    TechnicalMaterials, null, ReactorBBuffer, ResourceStorage,
                    ReactorBReturn),

                new InitialTransfer(
                    BasicMetals, null, ResourceStorage, FactoryABuffer, FactoryAFeed),
                new InitialTransfer(
                    TechnicalMaterials, null, ResourceStorage, FactoryBBuffer, FactoryBFeed),
                new InitialTransfer(
                    Component, null, FactoryABuffer, FactoryBBuffer, FactoryLinkAb),
                new InitialTransfer(
                    Module, null, FactoryBBuffer, FactoryCBuffer, FactoryLinkBc),
                new InitialTransfer(
                    RobotFrame, null, FactoryCBuffer, ResourceStorage, FactoryCReturn),
            });
    }
}
