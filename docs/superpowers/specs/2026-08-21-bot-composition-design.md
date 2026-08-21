# Bot Composition — Design

Date: 2026-08-21
Status: Draft

## Goal

Fix what a robot **is** — how a frame, its sockets and the things fitted into them combine into a
machine with capabilities — closely enough that the declared-but-undesigned `Robot` in the kernel
can be built out without re-deriving any of it.

The prize is that composition is the player's main preparation verb. Missions are autonomous; the
player does not pilot anything, so *what the bot is carrying when it launches* is one of the two
levers they have, the other being doctrine. A loadout has to be readable at a glance, meaningfully
constrained, and legible in a mission report.

The risk this document exists to prevent is a robot growing its own parallel systems. A field energy
simulation, a per-fitting durability tier, a `LoadoutOrder` state machine and a stats resolver with
its own multiplication order are each individually reasonable and collectively a second engine.
Every decision below is chosen partly for what it refuses to add.

## Source material

- **`docs/Dimenship - Bot Composition Proposal v0.1`** (the project owner's proposal, transcribed
  into the decisions below). Its recommended baseline: frame plus five configurable slots — 1
  Mobility, 2 Equipment, 1 Systems, 1 Utility/Payload — with integrated power and base armour, and
  later frames as sidegrades rather than linear upgrades. **This document adopts its intent and
  changes two things**: what the categories are called, and whether the baseline is a rule.
- **`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`** — already fixed
  the load-bearing rule that *a fitted module is an item in a socket storage*, and left four open
  items this document closes. It is a peer, not a parent: where the two touch, this document says so
  explicitly rather than leaving a reader to notice.
- **`docs/Game Design v0.9.md` §5.10** — authoritative, as always. It states the material model:
  sockets are storages, a machine reads its capability from what its sockets hold, a robot's sockets
  are reachable only while docked. This document adds the **topology and the budgets**; it changes
  none of that.
- **`docs/Game Design v0.9.md` §9** — the MVP scope table, which promises a loadout set predating
  this proposal. See *What this needs from the GDD*.
- **`docs/Dimenship Programming v0.1.md` §4.1** — doctrine targets an individual bot or a group.
  Loadout and doctrine are the two halves of preparation, which is why §9 below exists at all.
- **`docs/Dimenship Mission Visualization Proposal.md` §5, §8** — a deployed robot cannot be
  reconfigured; changes are made to the **template** and reach later missions. §8 asks for "a small
  number of robot frame silhouettes with module variations", which is a presentation constraint on
  how many frames MVP can afford.
- **`src/Dimenship.Core/State/Ledgers.cs`** — `Robot`, "the domain is declared, not designed".

## What is already in the kernel, and what it gets wrong

The robot domain is not a blank page. `Robot` exists, is saved, and has a shape:

```csharp
public sealed class Robot
{
    public required RobotId Id { get; init; }
    public required RobotFrameId Frame { get; init; }
    public string? NameOverride { get; set; }
    public List<ModuleId> Installed { get; } = new();
    public long IntegrityPermille { get; set; } = 1000;
    public RobotGroupId? Group { get; set; }
    public MissionId? OnMission { get; set; }
}
```

Most of that survives this document unchanged, which is the point of having declared it early.
`Frame`, `IntegrityPermille`, `Group` and `OnMission` are all exactly right.

**`Installed` is not.** A `List<ModuleId>` is a bag of fitted things with no socket structure, and
it is already in conflict with the refit spec: if a socket is a storage, then a robot holds a set of
socket storages, not a list of ids. The difference is not cosmetic. A flat list **cannot express an
empty socket** — it can only express a shorter list — and an empty socket is the thing the refit
spec's entire downtime argument rests on. "Between removing the old core and installing the new one,
the socket is genuinely empty, so the machine genuinely runs at its unequipped rating" is
unrepresentable in a `List<ModuleId>`, because there is no hole to look at.

So `Installed` becomes a socket-indexed structure of storage ids, and that is the single largest
state change this document implies. It is recorded here rather than made: nothing in `src/` changes
in this pass.

## A vocabulary decision, made rather than deferred

The recycling/refit spec left one open item marked *"needs settling in the GDD before the first line
of code"*: what the equipment tier is called, given that `module` already names a shipped bulk
commodity. This document settles it, because the proposal cannot be written down without a word for
its subject.

| Word | Meaning, settled |
| :--- | :--- |
| **fitting** | The equipment tier. A weapon, a drill, a sensor, an armour plate — the thing that occupies a socket and grants its capability to the machine holding it. **New word, new concept.** |
| **socket** | The holder. Already the GDD's word since v0.9.1. A robot's loadout is a set of sockets; a facility's upgrade positions are sockets. |
| **module** | Unchanged: the shipped bulk commodity `"id": "module"`, *Robot Module*, `holdCapacity` 120000, produced by `assemble_modules` and consumed by `assemble_frames`. An ordinary stored, fungible ingredient of the factory chain. |
| **slot** | Unchanged: an authored facility node position on the schematic (`FacilityState.BuiltAtStart` false), which is how the fixed layout reveals facilities as they are built. |
| **hardpoint** | One socket **category** — the shared tool/weapon position. Replaces the proposal's "Equipment". |

### Why the new tier took the new word

The alternative was to rename the shipped commodity — `module` → `subassembly` — and free the better
word for equipment. It reads more naturally and it was rejected on cost.

The commodity is real, shipped content: an id in `items.json`, a schematic named
`assemble_modules`, a link in the balanced factory chain, and a line in any save of any world that
has run. Renaming it means a content edit **and** content drift on every existing save, which
`WorldSave` correctly reports rather than absorbs. The fitting tier, by contrast, does not exist
yet. Naming a thing that has never been written down costs nothing at all.

That asymmetry decides it. The failure being prevented is concrete and was flagged in the GDD's own
changelog: a reader who conflates the two senses will make the shipped `module` commodity
unstorable, because the equipment rule — *never a line in Resource Storage* — is the exact opposite
of what the commodity does, and the factory chain would break with no obvious cause.

`ModuleId` in `State/StateIds.cs` therefore becomes `FittingId` when the domain is built. It is a
declared-not-designed id with no content behind it and no persisted value that means anything, so
the rename is free today and will not be free later.

### Why "Equipment" could not stay a category name

The proposal's table names the tool/weapon position "Equipment ×2". Once the whole tier is
equipment, one category called Equipment is the same collision one level down — a reader asking
"which equipment?" is asking a question the vocabulary created.

The fix costs nothing because the proposal already supplies it. Its own prose says "tool or weapon
**hardpoints**" and "2 **hardpoints** by default"; only the table says Equipment. Taking the word
the design rules already use leaves the category set as **Mobility · Hardpoint · Systems · Payload**
and changes no meaning.

## 1. Topology belongs to the frame

> A `FrameArchetype` declares its **own ordered list of sockets**, each with a category. There is no
> engine-level baseline and no engine concept of an exceptional frame.

`1 Mobility / 2 Hardpoint / 1 Systems / 1 Payload` survives as the **authoring convention for the
starting frames** — the shape most frames should have, written in a content note, enforced by taste.

### Why the baseline is a convention and not a rule

The proposal's own example loadouts already argue for this, and it is worth being specific because
the evidence is three lines long:

| Example | Reads as | On baseline? |
| :--- | :--- | :--- |
| **Mining** — Utility Frame, Thrusters, Mining Drill, Defensive Weapon, Geological Scanner, Cargo Module | 1 Mob / 2 HP / 1 Sys / 1 Pay | Yes |
| **Escort** — Heavy Frame, Tracks, Weapon, Weapon, Targeting Array, Shield Generator | 1 Mob / 2 HP / 1 Sys / 1 Pay | Yes |
| **Investigation** — Light Frame, Legs, Manipulator, Forensic Scanner, Evidence Container, Auxiliary Battery | 1 Mob / **1** HP / 1 Sys / **2** Pay | **No** |

Two of three sit on the baseline; the third does not. Under *baseline plus exceptions* the
Investigation loadout is a violation to be explained. Under per-frame topology it is simply what a
Light Frame is: **a frame that trades a hardpoint for a payload socket.**

And that trade is exactly the thing the proposal's strongest design rule asks for. "Later frames
should be sidegrades, not linear upgrades" is hard to honour when the only sanctioned axis of
variation is *how many sockets* — more sockets is a linear upgrade almost by definition, which is
why the proposal has to name 4- and 6-socket frames as exceptions and hope authors stay near five.
Per-frame topology gives sidegrades a real axis: same socket count, different **mix**. A Light Frame
with one hardpoint and two payload sockets is not worse than a Utility Frame, it is a different
machine, and no balance discipline is required to keep it that way.

The readability ceiling the proposal wants survives as an authoring constraint, stated in content
notes rather than validated: **4–6 configurable sockets**, and a third hardpoint is a deliberate
frame feature rather than a tier of frame. A loadout that cannot be read at a glance has failed at
the thing loadouts are for.

Rejected on the way:

| Option | Why it was rejected |
| :--- | :--- |
| Fixed 1/2/1/1 baseline, deviating frames flagged as exceptions | The proposal's own examples already break it, and "exception" frames become the interesting ones. A rule whose exceptions are the good content is a rule that erodes. |
| Fixed count of five, categories free per frame | Keeps loadout length constant for mission reports, which is genuinely nice, and still allows sidegrades. Rejected because it makes a genuinely small scout and a genuinely large hauler unexpressible without inventing filler sockets, and a socket that exists to be a placeholder is a decision the player has to make about nothing. |
| Free-form socket count, no guidance | Readable complexity is the proposal's own constraint and a good one. Dropping it invites the eleven-socket frame that is strictly better and impossible to summarise. |

## 2. A socket is a storage of capacity one, filtered by category

The refit spec already made sockets storages. What it did not say is what kind, and the answer
carries the one real engine cost in this document.

A socket storage differs from `global_hold` and `facility_buffer` in two ways:

- it holds **exactly one** fitting, not a volume;
- it accepts **only one category** of fitting — a drill does not go in a Mobility socket.

`StorageArchetype` today is `id`, `label`, `capacityPermille` (`dimenship/content/catalog/storages.json`)
and has **no accept-list at all**. Any storage accepts any item. So a socket needs a new field —
an accepted category — and a capacity expressed as a count rather than as a permille of a hold.

This is stated plainly because it is the price of the whole model and it would be dishonest to imply
sockets fall out of the existing storage tier for free. It is a small price: one nullable field on
the archetype and one branch in the room calculation. But it is not zero, and an implementer
budgeting from "the refit spec says sockets are storages" would be surprised by it.

What it buys is that everything else in this document is already-existing machinery. Fitting a part
is a `TransportTask`. A socket that already holds something postpones the incoming transport on
`DestinationFull`. A robot away on a mission is unreachable by transport, which is how "a deployed
robot cannot be reconfigured" stops being a guard clause somebody can forget to write.

## 3. Fittings carry no per-instance state

> A fitting is a **fungible item**. Two Mining Drill Mk1s are interchangeable. Damage and wear land
> on `Robot.IntegrityPermille` — which already exists — and never on an individual fitting.

This is the decision that keeps a socket an ordinary `ItemId → long` ledger, and it is worth stating
as a decision because the opposite is the more obvious design. "The drill on Scout-2 is at 40%
condition" is a thing players expect from loadout games.

The refit spec already identified why it is expensive: *"a stockpiled module that accumulates wear or
damage would need per-instance identity, and adding that to the ledger to support a spares box is a
poor trade."* The same argument holds here and is if anything stronger, because per-instance identity
would not stop at the ledger — it would reach the save format, the transport payload, and every
report that names a part.

Robot-level integrity carries the whole of the damage model in MVP and reads correctly: a bot comes
back beaten up, and repair is a robot-level operation rather than a per-part audit.

**What this defers:** fitting-level wear, condition-dependent performance, and any report of the
form "this particular drill is damaged". All of them need per-instance identity, which is a ledger
change and not a content edit.

## 4. Integrated power is a loadout budget, not a field simulation

Adopt the proposal's rule — the frame supplies power, fittings consume it, there is **no mandatory
power-core socket**. A socket that every build fills identically is not a decision, it is a tax with
a UI.

The mechanism, stated so that it cannot quietly grow:

> A frame declares an integer power **output**. Each fitting declares an integer **draw**. A loadout
> is valid when `sum(draw) ≤ frame output`. An auxiliary battery is a Payload fitting whose
> contribution is negative draw — it raises the ceiling, at the cost of the socket it occupies.
>
> **This is an integer comparison performed when a loadout is assembled and when a mission launches.
> It is not a per-tick model of a robot's energy in the field.**

Mass is the second budget and works identically: `sum(fitting mass) + cargo ≤ frame payload`, with
payload declared by the frame. Two integer budgets plus a socket list is the entire composition
constraint, and all three are readable side by side on one panel.

The boundary is the decision. A field energy simulation is the natural next thought — batteries
draining, brownouts on a long mission — and it would be a second `EnergyState` with its own cap
hits, starvation ticks and postponement reasons, running on a machine the player cannot see and
cannot intervene in. The vessel's energy model earns its complexity because the player can act on
it. A robot's would not.

This also makes the proposal's "auxiliary batteries can occupy Utility **when needed**" mean
something exact: needed is when the budget does not close.

**This supersedes the refit spec's worked example.** That document's seven-step refit is literally
"upgrading a robot from Power Core Mk1 to Mk2" through a socket, and under integrated power there is
no power-core socket to refit. The **mechanism** in that document — recall, remove, haul, reverse
run, build, install, release — is untouched and correct. Only its illustration needs re-targeting,
at a hardpoint fitting such as Mining Drill Mk1 → Mk2. Recorded here so the two specs do not quietly
disagree; the edit itself belongs to whoever next opens that file.

## 5. Effective stats are additive permille, summed and applied once

This closes the refit spec's open item: *"When several modules occupy several sockets, how their
modifiers combine is undefined here. It must be order-independent or ordered by declaration."*

> Each fitting contributes integer **permille deltas** to named frame stats. All contributions are
> **summed**, and the sum is applied once to the frame's base value.

Order-independent by arithmetic rather than by discipline. Integer addition commutes, so no
declaration order needs preserving, no helper can hide a bug by reordering, and the determinism
contract is satisfied without anyone having to remember it. This is the same register as the refit
spec's conservation invariant: not a rule enforced by care, a rule that cannot be broken.

Multiplicative stacking is rejected explicitly, because it is the default instinct. It needs a fixed
resolution order to be reproducible at all, it compounds in ways that are hard to read off a panel,
and the first time someone wants a 1.15× that is not expressible in integers, a `double` arrives in
a kernel where `TheKernel_ContainsNoFloatOrDouble` fails the build for it.

Clamping is left to the stat rather than the sum: some stats floor at zero, some do not, and that is
per-stat content rather than a global rule.

## 6. Mobility is environment efficiency, never a mission key-lock

The proposal's rule — "use environment properties and efficiency penalties rather than simple
mission key-locks" — made concrete and integer:

> A stratum or site declares **terrain classes**. A mobility fitting declares an **efficiency
> permille per class**. Mission effort or duration scales by that permille.

Thrusters at 1000‰ in vacuum and 300‰ in dense rubble is a real trade-off; thrusters as a
requirement to enter a vacuum stratum is a lock, and a lock turns preparation into a checklist. The
permille form means a badly-matched loadout **can still go** and simply does worse, which is the
telemetry-rich failure the GDD asks for throughout.

**This decision has a dependency and it is not satisfied.** `dimenship/content/catalog/strata.json`
is currently `"strata": []` — acquisition is not a system, and the two mission docks correctly
report idle. So terrain classes are content that does not exist yet, and this section is a
constraint on the strata work rather than something implementable today. Recorded so that whoever
authors the first stratum knows a field is expected of them.

## 7. A loadout template is authored content; a robot is state

The four-tier model puts these in different tiers and the distinction is easy to lose:

| Thing | Tier | Notes |
| :--- | :--- | :--- |
| `FrameArchetype`, `FittingArchetype` | **Catalog** | The rulebook. Socket topology, base stats, draws, masses, efficiencies. Loaded once per process. |
| Starting robots | **Scenario** | A campaign's authored opening position, like its storages and facilities. |
| `Robot` and its socket storages | **World state** | What is actually fitted right now, and what condition it is in. |
| A named loadout template | **Authored content** | `user:`-prefixed, beside programs. |

A template is a player-authored recipe — "Greedy Miner: Utility Frame, thrusters, drill, defensive
weapon, geological scanner, cargo module" — and it takes the `user:` prefix for the same reason
programs do: the catalog id pattern `^[a-z][a-z0-9_]*$` (`ContentCatalog.IdPattern`) cannot represent
a colon, so the two id spaces are provably disjoint rather than disjoint by convention.

The load-bearing distinction: **a robot does not follow its template; it was built to it.** Editing a
template does not reach out and refit anything. This is already what the Mission Visualization
Proposal assumes when it says changes "are made to the template and affect later missions only after
the robot returns and goes through the required upgrade/refit process" — and it is what makes refit
downtime matter, since a template edit is free and the refit that realises it is not.

## 8. A doctrine that names a capability the loadout lacks blocks the launch

The proposal's synergy rule — "loadout defines what a bot can do; doctrine decides when to use it" —
leaves one question open, and it is the question that decides whether composition has teeth: what
happens when a rule says *scan for anomaly* on a bot with no anomaly sensor fitted?

> The capability check happens at **mission-dock launch readiness**. An unsatisfiable doctrine is a
> blocked launch with a named cause, in the same vocabulary as every other readiness blocker.

The alternatives are both worse in specific ways:

| Option | Why it was rejected |
| :--- | :--- |
| Silent no-op — the rule never fires | The failure is invisible. The player watches a mission underperform for reasons no report explains, which is precisely the "weak version says Factory 70%" failure §5.5 of the GDD exists to prevent. |
| Reject at program install time | A template is edited while its robot is mid-refit, with the sensor removed and in a reactor buffer. Rejecting then makes a valid program un-saveable because of a transient world state, and the player's editor becomes hostage to the vessel's logistics. |

Launch-time is right because it is the moment both halves are known and fixed: this robot, this
loadout, this doctrine, going now. It reuses the readiness-diagnostic vocabulary the GDD already
specifies — "Readiness: Blocked. Blocking systems: …" (§8) — and it matches the programming design's
requirement that "conflicts must be reported clearly", since a conflict that is only explainable is
not clear enough.

It is also what gives composition its teeth. A player who writes an ambitious doctrine and fits the
wrong sensor does not get a quiet disappointment; they get a dock that will not launch and a line
saying why.

## 9. Socket storages stay out of the vessel-wide roll-up

Closes the refit spec's first open item, in the direction it already leaned.

> A robot's socket storages are excluded from `WorldSnapshot.Resources` and from
> `ProductionPlanner` availability.

A fitted drill is aboard the vessel in some sense, but it is not stock. Counting it would let the
planner allocate a part currently bolted into a working robot, and the symptom would be a plan that
looks satisfiable and is not — the planner's shortage reporting is only worth having if the
availability it reads is real.

The same argument covers facility upgrade sockets, and for the same reason.

## What this deliberately does not add

Recorded so a later reader does not read an oversight:

- **No `LoadoutOrder`, `EquipTask` or `RefitId`.** Fitting and removing are `TransportTask`s, per the
  refit spec. Nothing tracks a loadout change as a unit.
- **No per-tick field energy model.** Power is a budget checked twice, not a resource simulated
  continuously. See §4.
- **No fitting durability, condition or per-instance identity.** See §3.
- **No tactical statistics.** No range, accuracy, cooldown or facing. The Mission Monitor is
  watch-only by its own proposal §5, and the simulation resolves missions rather than fights.
- **No second storage model on the robot side.** A socket is a storage archetype with two new
  properties, not a new kind of container.
- **No engine-level baseline topology.** See §1. The five-socket shape is an authoring convention.

## What this needs from the GDD

The GDD is the tiebreaker on vocabulary, so two of its passages now trail this document and should
be reconciled deliberately rather than as a side effect of some later change. **Neither is edited
here.**

- **§9, the MVP scope table**, currently promises "Two robot frames plus tool, sensor, storage,
  power/defense, basic investigation module, basic weapon/armor package." Under this document there
  is no power fitting — power is frame-integrated — and the category set is Mobility, Hardpoint,
  Systems, Payload. The row needs rewriting against the new categories, and "two robot frames" needs
  checking against §1's sidegrade argument, which wants at least two frames with *different
  topologies* rather than two of the same shape.
- **§5.10 and the glossary entry for *Fitted Module***, which the *fitting* decision supersedes.
  v0.9.1's changelog already flagged that this name needed separating; this document supplies the
  separation, and the glossary should carry it.

Until then, the GDD wins on any point where a reader thinks the two disagree, exactly as everywhere
else in this repository.

## Open items

- **Does a Payload socket accept a stack?** Cargo is a quantity of ordinary goods, and a Cargo
  Module that grants capacity is a fitting — but an Evidence Container that *holds* things is a
  fitting and a storage at once. This is the one place the capacity-of-one rule may not hold, and it
  is unresolved.
- **Recovery fraction `p` for fittings.** The refit spec puts `p` on the build schematic and leaves
  the values to a balance pass read from worked yields rather than percentages. Fittings inherit
  that and add nothing, but nobody has chosen numbers.
- **How many frames does MVP actually ship?** §1 wants topological variety; the Mission
  Visualization Proposal §8 wants "a small number of robot frame silhouettes" for asset reasons;
  GDD §9 says two. Two frames with genuinely different topologies may be enough, and this has not
  been tested against the three example loadouts, which appear to want three.
- **Is composition ever a group-level property?** The programming design targets both an individual
  bot and a robot group. Whether a group has composition rules of its own — a required role mix — or
  whether groups only ever aggregate individually-composed robots is undecided.
- **Where does the player edit a loadout?** `Robotics` is a `PlaceholderPanel` in the shell
  (`ShellRoot.cs`) and the GDD's Robotics screen is where "loadout editing and doctrine management
  remain". That panel's design is its own piece of work and is not started.
- **Do fittings appear on the base graph at all?** Fittings move by transport between factory
  buffers and sockets, and a socket is not a graph node. Whether a refit in progress is visible on
  the schematic, or only in the Robotics screen, is unexamined.
- **Armour as a Payload fitting versus frame integrity.** The proposal puts "extra armour" in
  Utility/Payload while base armour is frame-integrated. Under §3 damage lands on
  `IntegrityPermille`, so extra armour is presumably a permille delta to how much damage that
  absorbs — but the damage model itself does not exist, so this cannot be stated properly yet.
