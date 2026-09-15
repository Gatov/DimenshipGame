using System.Collections.Generic;
using System.Linq;

namespace Dimenship.Ui;

/// <summary>
/// The frames and fittings the mock composes from. Invented content, held in the Godot assembly and
/// shipped in no JSON file: authoring it into <c>dimenship/content/</c> would make it loadable,
/// linkable and saveable, which is a claim a mock for an unspecified subsystem is not entitled to
/// make.
/// <para>
/// The budget is GDD §10's and nothing wider — two frames, and the six socket kinds it names.
/// Every socket kind has two or three fittings: enough that filling a socket is a choice, few enough
/// that no socket is a menu.
/// </para>
/// <para>
/// Item quantities are milli-units of the shipped items, so the build cost can be compared against
/// what the vessel actually holds. Stats are the mock's own units and mean nothing outside it.
/// </para>
/// </summary>
public static class LoadoutCatalog
{
    public const string Surveyor = "surveyor_frame";
    public const string Hauler = "hauler_frame";

    /// <summary>
    /// Declaration order is the display order, here as everywhere: a helper that sorted these would
    /// move the palette under the player between sessions for no reason they could name.
    /// </summary>
    public static readonly IReadOnlyList<FrameDef> Frames = new[]
    {
        new FrameDef(
            Surveyor,
            "Surveyor Frame",
            new[]
            {
                new SocketDef("tool", SocketKind.Tool),
                new SocketDef("sensor", SocketKind.Sensor),
                new SocketDef("power", SocketKind.Power),
                new SocketDef("investigation", SocketKind.Investigation),
            },
            new StatBlock(Mass: 800, Power: 0, Durability: 400, Cargo: 0, Scan: 0, WorkRate: 0),
            new[] { new ItemCost(Items.RobotFrame, 1000), new ItemCost(Items.Component, 4000) },
            "Light, four sockets, no armour. The frame an investigation loadout starts from."),
        new FrameDef(
            Hauler,
            "Hauler Frame",
            new[]
            {
                new SocketDef("tool", SocketKind.Tool),
                new SocketDef("cargo", SocketKind.Cargo),
                new SocketDef("power", SocketKind.Power),
                new SocketDef("defense", SocketKind.Defense),
                new SocketDef("sensor", SocketKind.Sensor),
            },
            new StatBlock(Mass: 1400, Power: 0, Durability: 700, Cargo: 200, Scan: 0, WorkRate: 0),
            new[] { new ItemCost(Items.RobotFrame, 2000), new ItemCost(Items.Component, 6000) },
            "Heavy, five sockets, carries something before anything is fitted."),
    };

    /// <summary>
    /// Every fitting, in socket-kind order. Power is a net budget: the three cores supply it and
    /// everything else spends it, which is what makes a socket a decision rather than a free
    /// upgrade.
    /// </summary>
    public static readonly IReadOnlyList<FittingDef> Fittings = new[]
    {
        Define("mining_head_mk1", "Mining Head Mk1", SocketKind.Tool,
            new StatBlock(180, -220, 0, 0, 0, 400),
            "The blunt option: the most work rate per unit of mass, and it eats the budget.",
            new ItemCost(Items.BasicMetals, 6000), new ItemCost(Items.Component, 2000)),
        Define("salvage_cutter", "Salvage Cutter", SocketKind.Tool,
            new StatBlock(140, -260, 40, 0, 0, 300),
            "Slower than the mining head and hardier, for pulling wrecks apart.",
            new ItemCost(Items.BasicMetals, 4000), new ItemCost(Items.Component, 3000)),
        Define("precision_manipulator", "Precision Manipulator", SocketKind.Tool,
            new StatBlock(90, -120, 0, 0, 40, 180),
            "Little work rate, little draw, and the only tool that sees anything.",
            new ItemCost(Items.BasicMetals, 2000), new ItemCost(Items.Component, 4000),
            new ItemCost(Items.Module, 1000)),

        Define("basic_sensor_array", "Basic Sensor Array", SocketKind.Sensor,
            new StatBlock(60, -90, 0, 0, 250, 0),
            "Cheap sight. Nothing else on the frame notices it is fitted.",
            new ItemCost(Items.TechnicalMaterials, 3000), new ItemCost(Items.Component, 2000)),
        Define("deep_scan_array", "Deep Scan Array", SocketKind.Sensor,
            new StatBlock(110, -210, 0, 0, 520, 0),
            "Twice the range of the basic array and rather more than twice the draw.",
            new ItemCost(Items.TechnicalMaterials, 6000), new ItemCost(Items.Component, 4000),
            new ItemCost(Items.Module, 1000)),
        Define("phase_resonance_probe", "Phase Resonance Probe", SocketKind.Sensor,
            new StatBlock(70, -340, -40, 0, 400, 0),
            "Reads through phase distortion, at the cost of the frame's own integrity.",
            new ItemCost(Items.TechnicalMaterials, 8000), new ItemCost(Items.Component, 3000),
            new ItemCost(Items.Module, 2000)),

        Define("standard_cargo_pod", "Standard Cargo Pod", SocketKind.Cargo,
            new StatBlock(120, 0, 0, 600, 0, 0),
            "A box. Draws nothing, which is the whole of its argument.",
            new ItemCost(Items.BasicMetals, 8000), new ItemCost(Items.Component, 1000)),
        Define("expanded_hold_pod", "Expanded Hold Pod", SocketKind.Cargo,
            new StatBlock(260, -40, 0, 1400, 0, 0),
            "More than twice the hold for more than twice the mass, and a pump to run.",
            new ItemCost(Items.BasicMetals, 14000), new ItemCost(Items.Component, 3000)),

        Define("power_core_mk1", "Power Core Mk1", SocketKind.Power,
            new StatBlock(150, 600, 0, 0, 0, 0),
            "Enough for a tool and a cheap sensor, and not enough for two hungry fittings.",
            new ItemCost(Items.TechnicalMaterials, 4000), new ItemCost(Items.Component, 3000)),
        Define("power_core_mk2", "Power Core Mk2", SocketKind.Power,
            new StatBlock(210, 1100, 0, 0, 0, 0),
            "What an over-budget template usually wants, and the most expensive answer to it.",
            new ItemCost(Items.TechnicalMaterials, 8000), new ItemCost(Items.Component, 5000),
            new ItemCost(Items.Module, 1000)),
        Define("endurance_cell", "Endurance Cell", SocketKind.Power,
            new StatBlock(320, 800, 60, 0, 0, 0),
            "Between the two cores in supply, above both in mass, and it armours what it sits in.",
            new ItemCost(Items.TechnicalMaterials, 6000), new ItemCost(Items.Component, 4000)),

        Define("ablative_plating", "Ablative Plating", SocketKind.Defense,
            new StatBlock(300, 0, 450, 0, 0, 0),
            "Metal. Draws nothing and costs nothing but mass and basic metals.",
            new ItemCost(Items.BasicMetals, 12000)),
        Define("countermeasure_pod", "Countermeasure Pod", SocketKind.Defense,
            new StatBlock(130, -180, 180, 0, 0, 0),
            "Less protection than plating, at a fifth of the mass and a real draw.",
            new ItemCost(Items.TechnicalMaterials, 5000), new ItemCost(Items.Component, 4000),
            new ItemCost(Items.Module, 1000)),

        Define("evidence_collector", "Evidence Collector", SocketKind.Investigation,
            new StatBlock(80, -120, 0, 0, 120, 0),
            "The cheap investigation fitting. Sees a little on its own.",
            new ItemCost(Items.TechnicalMaterials, 3000), new ItemCost(Items.Component, 3000)),
        Define("forensic_sampler", "Forensic Sampler", SocketKind.Investigation,
            new StatBlock(110, -160, 0, 0, 200, 0),
            "Heavier, hungrier, and sees most of what the deep array would.",
            new ItemCost(Items.TechnicalMaterials, 5000), new ItemCost(Items.Component, 4000),
            new ItemCost(Items.Module, 1000)),
        Define("contradiction_logger", "Contradiction Logger", SocketKind.Investigation,
            new StatBlock(50, -90, 0, 0, 80, 0),
            "The lightest fitting on either frame, and the most component-hungry to build.",
            new ItemCost(Items.TechnicalMaterials, 2000), new ItemCost(Items.Component, 5000)),
    };

    private static readonly Dictionary<string, FrameDef> FrameIndex =
        Frames.ToDictionary(frame => frame.Id);

    private static readonly Dictionary<string, FittingDef> FittingIndex =
        Fittings.ToDictionary(fitting => fitting.Id);

    /// <summary>The frame a template names, falling back to the first rather than throwing.</summary>
    public static FrameDef Frame(string id) =>
        FrameIndex.TryGetValue(id, out var frame) ? frame : Frames[0];

    /// <summary>Null for an empty socket, which is a state the composer expects rather than a fault.</summary>
    public static FittingDef? Fitting(string? id) =>
        id is not null && FittingIndex.TryGetValue(id, out var fitting) ? fitting : null;

    public static IEnumerable<FittingDef> OfKind(SocketKind kind) =>
        Fittings.Where(fitting => fitting.Kind == kind);

    /// <summary>
    /// Where a fitting's standalone image lives. By convention from the id rather than a field on
    /// <see cref="FittingDef"/>: the id is already unique, and a path field would restate it in a
    /// second place that could drift. One image per fitting, reused by the box, the picker and
    /// anything else that shows one.
    /// </summary>
    public static string FittingArtPath(string fittingId) =>
        $"res://assets/loadouts/fittings/{fittingId}.svg";

    /// <summary>
    /// The word a socket wears in the composer: a box's kind line and a picker's title. Held here
    /// rather than taken from <c>ToString()</c> so a renamed enum member cannot silently retitle
    /// the interface.
    /// </summary>
    public static string Label(SocketKind kind) => kind switch
    {
        SocketKind.Tool => "Tool",
        SocketKind.Sensor => "Sensor",
        SocketKind.Cargo => "Cargo",
        SocketKind.Power => "Power",
        SocketKind.Defense => "Defense",
        SocketKind.Investigation => "Investigation",
        _ => "Socket",
    };

    /// <summary>
    /// Which existing <c>status</c> glyph stands in for a socket kind. Borrowed rather than drawn:
    /// a <c>fitting</c> icon domain is a follow-up, and <see cref="IconSlot"/> reserves its space
    /// whether or not a file is there.
    /// </summary>
    public static string Icon(SocketKind kind) => kind switch
    {
        SocketKind.Tool => "rate",
        SocketKind.Sensor => "stability",
        SocketKind.Cargo => "capacity",
        SocketKind.Power => "energy",
        SocketKind.Defense => "durability",
        SocketKind.Investigation => "crew_ai",
        _ => "info",
    };

    private static FittingDef Define(
        string id, string label, SocketKind kind, StatBlock delta, string note,
        params ItemCost[] cost) =>
        new(id, label, kind, delta, cost, note);

    /// <summary>
    /// The shipped item ids a build cost may name. Only these five: a cost naming an item the
    /// catalog does not have would produce a row the vessel can never fill, and the affordability
    /// readout would be reporting on a shortage that cannot be resolved.
    /// </summary>
    public static class Items
    {
        public const string BasicMetals = "basic_metals";
        public const string TechnicalMaterials = "technical_materials";
        public const string Component = "component";

        /// <summary>
        /// The <b>bulk commodity</b> — <i>Robot Module</i>, produced by <c>assemble_modules</c> —
        /// spent to build a fitting. Not the fitting itself; see <see cref="FittingDef"/> for the
        /// word the mock uses for the fitted thing.
        /// </summary>
        public const string Module = "module";

        public const string RobotFrame = "robot_frame";
    }
}
