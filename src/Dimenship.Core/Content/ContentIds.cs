namespace Dimenship.Core.Content;

/// <summary>
/// Identifier for a class of production facility — what a facility is before one is built and
/// named. Distinct from <see cref="Simulation.ExecutorId"/>, which names the built instance:
/// "Factory Alpha" is an executor, "standard factory" is an archetype, and a scenario that
/// confused the two would let a build sheet redefine a machine's work rate.
/// </summary>
public readonly record struct FacilityArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of transport line.</summary>
public readonly record struct TransportArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of storage. Capacity is a property of the kind of hold.</summary>
public readonly record struct StorageArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a class of reactor. Filled by the fuel-burning power core work.</summary>
public readonly record struct ReactorArchetypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identifier for a mission target stratum.</summary>
public readonly record struct StratumId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Identifier for a power sink. A sink has no per-instance state — it draws what it draws — so a
/// scenario names one rather than describing one.
/// </summary>
public readonly record struct PowerSinkId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Identifier for a class of robot frame — a Utility Frame, a Light Frame — which is what declares
/// a robot's socket topology, its base stats and its power and payload budgets.
/// <para>
/// Distinct from the shipped <see cref="Simulation.ItemId"/> <c>robot_frame</c>, and the distinction
/// is one word wide, so it is worth stating: <i>Robot Frame</i> is a bulk commodity, the terminal
/// product of the factory chain, fungible and stored. A frame archetype is a kind of machine. A
/// reader who merges them ends up needing an archetype that is also an item, or an item that is
/// also an archetype, and neither is what either tier is for.
/// </para>
/// <para>Declared, not designed: no catalog file names a frame yet.</para>
/// </summary>
public readonly record struct RobotFrameId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Identifier for one socket position on a frame — the holder a fitting occupies.
/// <para>
/// Named rather than numbered, because <b>a position would make content order the save format</b>.
/// A frame declares its sockets in an order chosen to read well on a loadout panel, so an author
/// will eventually reorder them; under positional addressing that silently re-points every saved
/// robot's fittings onto different sockets, with nothing failing and nothing reported. It is the
/// failure <see cref="State.RngDomain"/> is emphatic about, arriving somewhere nobody is watching
/// for it. Named, a reorder costs nothing and a rename is content drift, which the save loader
/// reports rather than absorbs.
/// </para>
/// <para>
/// Scoped to the frame that declares it rather than globally unique: every frame may call its
/// mobility socket <c>mobility</c>, which is most of what makes a loadout readable at a glance.
/// Resolving one therefore means knowing the robot's frame first.
/// </para>
/// </summary>
public readonly record struct SocketId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Identifier for a class of fitting — the equipment tier: a weapon, a drill, a sensor, an armour
/// plate, a power core. The thing that occupies a socket and grants its capability to the machine
/// holding it.
/// <para>
/// Deliberately not <c>ModuleId</c>, which is what this was called. <c>module</c> is the shipped
/// bulk commodity <i>Robot Module</i>, an ordinary factory-chain ingredient; the equipment rule
/// that fitted gear is never a line in Resource Storage is the exact opposite of what that
/// commodity does, and a reader who conflates the two makes it unstorable and breaks the chain
/// with no obvious cause. See
/// <c>docs/superpowers/specs/2026-08-21-bot-composition-design.md</c>.
/// </para>
/// <para>
/// Declared, not designed, and currently named by nothing: a socket's contents are ordinary
/// <see cref="Simulation.ItemId"/>s, because a fitting is a fungible item. Whether fittings also
/// need an id space of their own, or whether a fitting archetype is keyed by its item id, is the
/// open question this seat is holding.
/// </para>
/// </summary>
public readonly record struct FittingId(string Value)
{
    public override string ToString() => Value;
}
