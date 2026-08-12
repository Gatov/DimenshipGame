using Dimenship.Core.Content;

namespace Dimenship.Core.Simulation;

/// <summary>
/// The ids the shipped vessel uses, as constants.
/// <para>
/// What used to be here was the vessel itself — a record holding the rulebook, this vessel's build
/// sheet, its opening stock and its first two tasks, hand-written in C#. That is now
/// <c>content/scenarios/default_vessel.json</c> against the catalog beside it, and the engine runs
/// on a catalog and a world state.
/// </para>
/// <para>
/// The names survive because tests, helpers and the programming view's dropdowns name them, and a
/// string literal repeated in forty places is a typo waiting for a place to happen. They are the
/// same ids the content files carry, and nothing here defines anything.
/// </para>
/// </summary>
public static class DefaultVessel
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
}
