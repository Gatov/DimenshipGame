namespace Dimenship.Core.State;

/// <summary>
/// Identifier for a committed plan. Minted by <see cref="PlanRegistry"/>, which saves its counter:
/// a registry that restarts from zero after a load mints an id already in use, which is not a
/// crash and not visible until two entities that were never the same are indistinguishable.
/// </summary>
public readonly record struct PlanId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <inheritdoc cref="PlanId"/>
public readonly record struct MissionId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <inheritdoc cref="PlanId"/>
public readonly record struct AlertId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Identifier for a program definition — a shipped preset, or one a player authored. Authored ids
/// carry a <c>user:</c> prefix, which the catalog's id pattern cannot represent.
/// </summary>
public readonly record struct ProgramId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Identifier for one installed copy of a program, with its own target and tuned parameters. A
/// definition, a rule within it and an installed copy are three different things to name: two
/// installations of one program on two facilities hold their own reservations, and clearing one
/// must not clear the other.
/// </summary>
public readonly record struct ProgramInstanceId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Identifier for a rule within a program. Stable across an edit, because it is a dictionary key
/// in the save: an edit that re-minted them would silently clear every cooldown it touched.
/// </summary>
public readonly record struct RuleId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a robot. The robot domain is declared, not designed.</summary>
public readonly record struct RobotId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <inheritdoc cref="RobotId"/>
public readonly record struct RobotGroupId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of robot frame. Catalog, when the domain arrives.</summary>
public readonly record struct RobotFrameId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a robot module. Catalog, when the domain arrives.</summary>
public readonly record struct ModuleId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>How fast real time buys ticks. It scales nothing else.</summary>
public enum TimeFlow
{
    Paused,
    X1,
    X2,
    X4,
}

/// <summary>
/// Which generator a draw comes from.
/// <para>
/// <b>Members are append-only and never reordered: the index is the save format.</b> Inserting a
/// domain in the middle re-points every stream in every existing save at a different domain. New
/// domains go on the end, and a save shorter than this enum extends with fresh seeds.
/// </para>
/// </summary>
public enum RngDomain
{
    Mission,
    Hazard,
    Salvage,
    Analysis,
}

public enum PlanState
{
    Active,

    /// <summary>Every task the plan spawned has finished.</summary>
    Complete,

    Abandoned,
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>
/// What an alert is about. Declared minimally: the alert system is populated by the telemetry work,
/// and a code nothing raises is a code nobody can agree on the meaning of.
/// </summary>
public enum AlertCode
{
    ExecutorBlocked,
    EnergyStarved,
    StorageFull,
}

/// <summary>The GDD's MVP set. Mission mechanics are deferred; this is shape only.</summary>
public enum MissionKind
{
    Mining,
    Scavenging,
    Investigation,
}

/// <inheritdoc cref="MissionKind"/>
public enum MissionPhase
{
    Preparing,
    Outbound,
    Working,
    Inbound,
    Delivered,
    Lost,
}
