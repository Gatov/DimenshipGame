# Storage Topology, Direct Routes and the Workpiece Tier — Design

Date: 2026-09-24
Status: Decided (D2)

## Goal

Answer the three questions of the scheduling design's §7.2 before any kernel code or content is
written against them:

1. **Exclusion.** Which intermediates never enter Resource Storage.
2. **Acceptance.** What each local buffer may hold.
3. **Routes.** Which factory↔reactor connections exist on the shipped vessel.

The GDD's central-storage model is the thing this changes, and item ids in this repository come
from the GDD, so the output is this document **and** the GDD amendment in the same change
(`docs/Game Design v0.9.md` §5.8, §5.10, Appendix A and the revision notes). It gates K3
(local-only items), K4 (the revisit chain and its routes) and K5b (the location-aware planner). It
changes no code, no content and no save format.

## Source material

[Issue #46](https://github.com/Gatov/DimenshipGame/issues/46), ticket D2 of
`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`:

> Which intermediates are excluded from Resource Storage? What can each local buffer accept? Which
> factory↔reactor connections exist on the shipped vessel? Today every route passes through
> `resource_storage` except `factory_link_ab` and `factory_link_bc`.
> Keep the vocabulary distinct from the recycling spec's fitted equipment and from the shipped
> `module` commodity. Local-only intermediates are not equipment-tier items.

`docs/production scheduling and automation - gameplay design.md`, §4 *Local Intermediates and
Facility Revisits*:

> A few common components serve several products. Keeping them outside central storage commits
> local space and material before a useful final outcome exists.
>
> `Raw material → Factory: form plate → Reactor: harden plate → Factory: finish assembly`
>
> Transport should deliver between appropriate local buffers over fixed, automatic routes. A
> component restriction cannot work with the current assumption that every intermediate returns to
> central storage. […] Fitted equipment and common components retain distinct storage rules and
> terminology.

The GDD, §5.8: *"One shared Resource Storage sits between every stage. A factory never draws from a
reactor directly except across an authored factory interconnect; docks connect to storage and to
nothing else."* And §5.10: *"Materials and components are ordinary stored goods and are unaffected
by this; it restricts only the things that occupy sockets."*

`docs/superpowers/specs/2026-08-21-bot-composition-design.md` for **fitting** as the equipment
tier's word; `docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md` for
the rule that fitted equipment is never in Resource Storage;
`docs/superpowers/specs/2026-08-20-shared-hold-volume-design.md` for a storage as one shared
volume; `docs/superpowers/specs/2026-09-04-conveyor-belt-design.md` for *blocked means cargo it
cannot put down*; and
`docs/superpowers/specs/2026-09-24-setup-identity-and-interruption-boundary-design.md` (D1), whose
rule that a default must be behaviour-neutral this document follows.

## What the vessel does today

- **Every storage accepts every item.** A storage is one volume every item competes for
  (`SimulationEngine.Room`), and a delivery is further held back by the reservation a facility's
  next run needs (`RoomForDelivery`). Nothing asks *whether* an item belongs somewhere, only
  whether it fits. `StorageArchetype` has no item list and `ItemDefinition` has no placement rule.
- **Every built route touches `resource_storage`.** Each reactor and factory has a feed from the
  hold and a return to it; the extractor and both docks are joined to the hold and to nothing else.
  `factory_link_ab` and `factory_link_bc` are the two buffer-to-buffer routes, and both ship
  unbuilt. **Nothing in the kernel builds a line**, so an unbuilt route is never usable: the
  "hold-star carries this stage until the player builds the interconnect" note on
  `factory_link_ab` describes a path no campaign can take today.
- **There is no factory↔reactor route at all.**
- **Lines have no item filter.** A transport archetype "carries whatever is handed to it". What
  a line may carry is decided by what its task names, and the only refusal at the far end is
  volume.
- **The planner routes every leg through the hold.** `PlanDraftEditor.EmitProduce` moves each
  input `Hold → facility.LocalStorage` and each output `facility.LocalStorage → Hold`. It has no
  way to express buffer-to-buffer delivery.

So the central-storage model is not only the GDD's; the planner assumes it and the content has
nothing that could violate it.

## Decisions

### 1. A new tier, the *workpiece*, and no shipped item joins it

A **workpiece** is an item that exists only between the stages of one production chain. It is
fungible and stored by quantity like any other item, but it is **never held in Resource Storage**,
and it is accepted only by the buffers of facilities that work it (Decision 2).

The word is chosen because every nearby word is taken:

| Word | Already means | Stored in Resource Storage? |
| :--- | :--- | :--- |
| `component` | The shipped bulk item *Component*, output of `press_components`. | Yes |
| `module` | The shipped bulk item *Robot Module*. CLAUDE.md warns it must stay storable. | Yes |
| **fitting** | The equipment tier (bot-composition spec): occupies a socket, grants a capability. The GDD's *Fitted Module*. | **No** — socket, buffer or transit |
| *part* | The loadout mock's retired placeholder for *fitting*. | — |
| *slot* | An authored facility position awaiting construction. | — |
| **workpiece** | *New.* An intermediate between chain stages. | **No** — buffer or transit |

A workpiece and a fitting share one property — neither is ever a line in Resource Storage — and
nothing else. The reasons differ, which is why they are two tiers and not one flag:

- A **fitting** is kept out because equipment must not be stockpiled: no spare-parts inventory, no
  softening upgrade downtime by preparation (GDD §5.10). It will need per-instance identity (wear)
  and lives in a socket, which holds exactly one of it.
- A **workpiece** is kept out because committing it is the decision: material formed for one
  product occupies a specific factory's or reactor's space until that product is finished,
  instead of dissolving back into a common pool (design §4). It is interchangeable within its id,
  has no identity, occupies volume in a buffer like any bulk good, and never occupies a socket.

A change that later needs "never in Resource Storage" for fittings adds the fitting tier's own
rule beside this one. It does not reuse the workpiece flag, because a fitting that could sit in a
buffer indefinitely by quantity would be the spare-parts inventory §5.10 forbids.

**Which intermediates are excluded: only new ones.** Every item on the shipped vessel stays an
ordinary stored good:

| Item | Why it stays storable |
| :--- | :--- |
| Matter Mix, Hydrogen, Basic Metals, Technical Materials | Materials. The GDD's chain begins with them in storage, and the recovery path (hydrogen → Resource Storage → `synthesize_basic`) runs through the hold. |
| `component` | The design proposes common components as local-only; this document declines, for now. `factory_link_ab` is unbuilt and nothing builds a line, so the hold is the *only* path from Factory Alpha to Factory Beta: excluding `component` would make modules, and therefore frames, unmakeable on the shipped vessel. GDD §5.10 names components as ordinary stored goods. And M1–M3 measure the shipped chain; changing its storage rule would move the baseline every later ticket is compared against. The component's scheduling pressure comes instead from demand — Decision 3 makes it an input of a second product. |
| `module` | The shipped bulk commodity, consumed by `assemble_frames`. Excluding it is the exact breakage the recycling spec's vocabulary warning exists to prevent. |
| `robot_frame` | An end product, bound for loadouts and docks by way of the hold. |
| The three construction units | Stand-ins for socket items until sockets exist: they belong to the equipment side, not the chain. Every shipped unit reaches its target through the hold, and nothing but the hold connects a factory to a dock. |

The workpieces are the two new intermediates of the revisit chain (Decision 3). "A few common
intermediates" is honoured by *two*, deliberately: enough to test whether committed local
material creates decisions, and few enough that an E3 no-go withdraws them in one edit.

### 2. Acceptance is derived from the catalog, and only workpieces are restricted

**Ordinary items: unchanged.** Any storage accepts any ordinary item, subject to volume and, for a
delivery, the reservation. A stray ordinary item is always recoverable, because every built
facility buffer on the shipped vessel has a return line to the hold and a line carries anything.
Restricting ordinary items would add a rule to fix a problem that does not exist, and would break
test fixtures and manual transfers that do nothing wrong.

**Workpieces: a single derived rule.**

> A storage accepts a workpiece if and only if it is the local storage of a facility whose type
> has a schematic in the catalog that consumes or produces that workpiece.

Consequences, on the shipped vessel with Decision 3's content:

| Storage | Accepts workpieces? | Because |
| :--- | :--- | :--- |
| `resource_storage` | **Never** | It is no facility's local storage. |
| Factory buffers (all three) | Yes — both | Factory schematics produce `plate_blank` and consume `hardened_blank`. |
| Reactor buffers (both) | Yes — both | `harden_blanks` consumes one and produces the other. |
| `extractor_buffer` | No | The extractor type's only schematic is `extract_hydrogen`. |
| Dock holds | No | A dock has no schematic. This is §5.8's "docks connect to storage and to nothing else", kept true for workpieces by the rule rather than by a second rule. |

The rule is **derived, never authored**. An authored accept-list per storage archetype or per
scenario storage was rejected: it is a second record of what the schematics already say, and it
drifts. Add a schematic that treats a workpiece at a new facility type and that type's buffers
accept it in the same edit, with no list to forget. It reads the **catalog**, not the scenario's
unlocked schematics, so acceptance never changes mid-campaign on an unlock: a workpiece can only
exist because some schematic produced it, and every schematic that could produce or consume it
is already in the catalog.

It reads the facility's **type**, not what the facility is configured for. A buffer that rejected
a workpiece because its reactor is set up for `separate_basic` would bounce the delivery that the
changeover to `harden_blanks` is waiting on.

**Lines stay unfiltered.** What a line may carry is still decided by the destination, not by the
line. `reactor_a_return` (reactor → hold) could be handed a `hardened_blank`; the refusal is that
the hold does not accept it (Decision 4), not that the line is labelled for Basic Metals. A
per-line item filter was rejected as a second rule that could disagree with the first.

**Stranding by explicit command is possible, and is D4's.** Factory Beta may legally run
`form_blanks`, since its type can, but it has no route to a reactor: the blanks would sit in its
buffer with nowhere to go. The planner never plans that (K5b plans only chains with a route). A
player or controller that orders it anyway has made an allocation that recovery (D4: move,
dismantle or discard) exists to undo. Refusing the order would need a reachability check over
built routes at `Enqueue`, and that is reported as an open item rather than decided here.

### 3. One revisit chain, and treatment lines from Factory Alpha to both reactors

**The chain.** The design's illustrative chain, made concrete:

```text
BASIC METALS → FACTORY (form) → MATTER REACTOR (harden) → FACTORY (finish) → STORAGE
                  plate_blank          hardened_blank          bulkhead
```

| Id | Label | Tier | Made by | Consumed by |
| :--- | :--- | :--- | :--- | :--- |
| `plate_blank` | Plate Blank | Workpiece | `form_blanks` (factory): `basic_metals` → `plate_blank` | `harden_blanks` |
| `hardened_blank` | Hardened Blank | Workpiece | `harden_blanks` (matter reactor): `plate_blank` → `hardened_blank` | `assemble_bulkheads` |
| `bulkhead` | Bulkhead | Ordinary | `assemble_bulkheads` (factory): `hardened_blank` + `component` → `bulkhead` | Nothing on the shipped vessel yet; scripted demand (M2) asks for it. |

Why these words: *blank* is the manufacturing term for formed stock awaiting finishing, and none
of *blank*, *workpiece* or *bulkhead* is used anywhere in the content, code or specs. *Plate* on its
own was avoided for the finished product, because the bot-composition spec already uses "an armour
plate" as an example fitting. A bulkhead is structure, not equipment: it is stored, and it
occupies no socket.

`assemble_bulkheads` takes `component` as well. That is what makes the component "common": it
now feeds modules and bulkheads, so its scheduling pressure is two products competing for Factory
Alpha's output, without changing where a component may be stored (Decision 1).

**The routes.** Four new lines, built at start, all on Factory Alpha's buffer:

| Route | From → to | Carries | `lengthTicks` |
| :--- | :--- | :--- | :--- |
| `reactor_a_treat_feed` | `factory_a_buffer` → `reactor_a_buffer` | `plate_blank` | 5 |
| `reactor_a_treat_return` | `reactor_a_buffer` → `factory_a_buffer` | `hardened_blank` | 5 |
| `reactor_b_treat_feed` | `factory_a_buffer` → `reactor_b_buffer` | `plate_blank` | 6 |
| `reactor_b_treat_return` | `reactor_b_buffer` → `factory_a_buffer` | `hardened_blank` | 6 |

Lengths follow the scenario's existing rule, the Manhattan distance in grid cells between the two
cards (Factory Alpha at (4, 1), Reactor Alpha at (0, 2), Reactor Beta at (0, 3)). They are long,
and that is honest: treatment travels across the vessel, and material on those belts is material
the design's metric "material tied up" is meant to count.

Why this topology rather than another:

- **Forming and finishing on one factory** is the design's factory decision in its plainest form
  — "start work, or finish products that release space" — on the machine that already presses
  components. Factory Alpha now has three competing uses, each a different schematic, so every
  choice between them is a changeover (D1).
- **Both reactors treat.** With only Reactor Alpha linked, treatment has one eligible executor and
  "reactor assignment", a column of GDD §5.8's own table, is not a decision for the chain. With
  both, treating on Alpha takes time from the Basic Metals that forming consumes; treating on
  Beta takes it from Technical Materials. Beta ships unbuilt, so the second choice arrives with
  progression. Its lines are built at start, the same way `reactor_b_feed` already is: a built
  line to an unbuilt facility's buffer is legal, and nothing would ever build it later.
- **Factories Beta and Gamma get no reactor link.** They stay the module and frame chain on the
  hold-star, so the shipped chain M3 measures is unchanged apart from the extra competitor for
  Factory Alpha.
- **Every new line is built at start**, because nothing in the kernel builds a line. An unbuilt
  treatment route would be dead content, as `factory_link_ab` is today.
- **No line joins a factory to a reactor for anything but workpieces**, although lines are
  unfiltered. The planner (K5b) uses a direct line when the item may not go through the hold, and
  the hold-star otherwise. Short-cutting Basic Metals from a reactor straight to a factory is a
  separate optimisation that would change the M3 chain, and it is not part of this decision.
- **Direction-switching transport**, which the design lists as a candidate, is not used. Each
  direction is its own line with its own belt, as the conveyor spec requires.

The finished `bulkhead` leaves by `factory_a_return`, the existing hold-star leg, which carries
anything. `basic_metals` and `component` arrive by the existing feeds. No other new line is
needed.

### 4. Enforcement: refuse at the command, never at the belt head

The plan's K3 text says "Resource Storage refusing a deposit with a reported reason". Taken
literally, that refusal would happen when cargo reaches the head of a belt: the head cannot be
emptied, so the belt freezes, and because the destination will *never* accept the item, it freezes
for good. *Blocked means cargo it cannot put down* would become a permanent jam, and the line
would stop carrying everything else too. So K3 is amended: a misplaced workpiece is refused where
the command enters the kernel, and the belt head never meets one.

1. **`SimulationEngine.Enqueue` refuses a transfer of a workpiece to a storage that does not
   accept it**, with a sentence, as it already refuses an unbuilt executor or a non-commandable
   facility. Nothing reaches a queue.
2. **The draft reports it before approval.** Any move step whose destination does not accept its
   workpiece carries a new `DraftIssueKind.WorkpieceNotAccepted`, appended last. So does a goal
   that asks for a workpiece delivered to the hold. It is a structural kind, listed in
   `PlanDraft.IsCommittable` beside `NoSuchRoute`, so approval refuses the draft and the Operations
   composer shows why. A supply kind would let the draft commit and then fail at `Enqueue`.
3. **`Room` and `RoomForDelivery` answer 0** for a workpiece in a storage that does not accept it.
   After (1) this cannot be reached through a command. It keeps the shared-volume rule's "one
   answer" true for every caller, including a future one that forgets to check.
4. **Loading a save reports a misplaced workpiece as content drift**: stock in a storage that does
   not accept it, belt cargo bound for one, or a transfer task naming one, listing every
   reference. It is never moved or dropped. This is what a catalog edit that turns an existing item
   into a workpiece looks like to a campaign already holding it in Resource Storage, and the save
   rules say drift is reported, never absorbed.
5. **The loader** (link phase, errors collected):
   - `workpiece` is a **required** boolean on every item. A default of `false` was rejected: a new
     intermediate whose author forgot the flag would silently become storable, which is the leak
     the rule exists to prevent. `commandable` on facilities is required for the same reason.
   - A workpiece is the output of at least one schematic and an input of at least one. One that
     nothing consumes can never leave a buffer, and one that nothing produces cannot exist.
   - A workpiece is never a facility archetype's `constructionUnit` (the equipment side,
     Decision 1) and never a stratum yield (missions return through docks, which accept none).
   - A scenario's opening stock never places a workpiece in a storage that does not accept it, and
     no `initialTransfers` entry sends one there.

Production needs no new check. A facility deposits its output into its own buffer, and its own
type's schematic produced it, so that buffer accepts it by the rule.

**Behaviour-neutral by construction.** With no item flagged, every check above is false and every
answer is today's. K3 therefore lands with the shipped vessel advancing byte-identically, and the
M3 baseline stays valid until K4 changes content.

**The recovery path is untouched.** `extract_hydrogen` → Resource Storage → `synthesize_basic` →
Resource Storage names no workpiece, and the loader rules above stop one entering it. The GDD's
recovery invariant does not depend on the new chain.

## What changes, by ticket

**K3 — Local-only items.** The `workpiece` flag on `ItemDefinition` and in `items.json` (every
shipped item `false`), the loader rules of Decision 4 (5), a derived acceptance index built in the
engine constructor from the catalog and never saved, the `Enqueue` refusal, `Room` /
`RoomForDelivery` answering 0, `DraftIssueKind.WorkpieceNotAccepted`, and the save drift report.
Loader tests break exactly one thing each. Test: the shipped vessel advances byte-identically
with the flag present and every value false.

**K4 — Revisit chain and direct routes.** Content only, in `dimenship/content/`:

- Items `plate_blank` and `hardened_blank` (workpiece), and `bulkhead` (ordinary).
- Schematics `form_blanks`, `harden_blanks` and `assemble_bulkheads`, unlocked in
  `default_vessel.json`.
- Transport archetypes for the treatment lines, sized to the stage per `transports.json`'s rule,
  and the four routes of Decision 3.
- `matter_reactor`'s `purpose` gains treatment, since the inspector reads it.
- Quantities are K4's to set against the M3 baseline, under two constraints. One run's inputs and
  output fit a facility buffer beside the reservation; the construction-unit note in `items.json`
  records what goes wrong when they do not. And `harden_blanks` draws enough energy that treatment
  is a real cost against separation.
- K4 re-runs M3 on the new content and commits both reports, because four built lines add standing
  draw and a new competitor for Factory Alpha, so the old baseline no longer describes the vessel.
- Test on shipped content: every workpiece an unlocked schematic produces has a built route from a
  producer's buffer to a consumer's buffer.

Until K5b lands, the planner cannot plan the chain: its hold-routed legs raise
`WorkpieceNotAccepted`, which is the honest answer. The M2 harness and kernel tests can still
exercise the chain through direct `Enqueue`.

**K5b — Location-aware planner.** For a workpiece leg, the planner routes buffer to buffer over a
built line, and never through the hold. Where several producers or treaters are reachable, it uses
the existing line choice (least load, then throughput, then declaration order). Ordinary items keep
the hold-star, so an unedited draft for anything outside the new chain still commits
byte-identically.

**Unchanged by all three.** Resource Storage's handling of ordinary items; the shared-volume
rule; the delivery reservation; the module/frame chain's routes; docks' connections; the save
format (the flag is catalog, and a misplaced workpiece is reported through the existing drift
channel).

## GDD amendment

Made in this change, in `docs/Game Design v0.9.md`:

- §5.8, the facility table: Resource Storage holds everything *except workpieces*; a Matter
  Reactor also treats workpieces.
- §5.8, the storage paragraph: Resource Storage sits between every stage *except a workpiece's*,
  and a factory also draws from a reactor across an authored **treatment line**. Docks are
  unchanged.
- §5.8, a new **Workpieces and treatment** passage: the tier, the acceptance rule, and the chain
  `BASIC METALS → FACTORY (form) → MATTER REACTOR (harden) → FACTORY (finish) → STORAGE` with its
  three item ids.
- §5.10: a workpiece is not a fitted module, and the two rules are separate.
- Appendix A: **Workpiece**, **Treatment Line** and **Treatment**.
- §15: v0.9.2 revision notes, and *Last Updated*.

The amendment says in so many words that it exists for the production-scheduling experiment, and
that an E3 no-go withdraws it with its ids. The design document "does not supersede the GDD", and
this is the smallest GDD change that lets K3 and K4 use ids the GDD names.

## Acceptance criteria

- Resource Storage never holds a workpiece, through any command, content file or save.
- A workpiece is accepted exactly where Decision 2's derived rule says, and nowhere else.
- No belt ever freezes on a workpiece its destination refuses: the refusal happens at `Enqueue`,
  and in the draft before that.
- With every item's `workpiece` false, the shipped vessel advances byte-identically to the code
  before K3.
- Every shipped item keeps its current storage behaviour.
- Every new id in K4 is named in the GDD before K4 lands.

## Not built

- **An equipment-tier restriction.** Fittings get their own rule when sockets exist (Decision 1).
- **Building a line.** Nothing builds `factory_link_ab`, `factory_link_bc` or anything else. This
  document avoids needing it by shipping every new line built. The factory interconnects remain
  dead content until line construction is a system, and that is recorded here rather than fixed.
- **A consumer for `bulkhead`.** Repair, dock loadouts and upgrades are candidates. Scripted demand
  stands in until one is designed.
- **Reactor-to-factory delivery of ordinary materials.** Decision 3 reserves the treatment lines
  for workpieces in the planner's hands.
- **Recovery of stranded workpieces.** That is D4.

## Open items

- Whether `Enqueue` should also refuse *producing* a workpiece at a facility with no built route to
  any consumer (Factory Beta's `form_blanks`), or whether D4's recovery is the whole answer.
- Placement: the treatment routes span the hub column from column 4 to column 0. K4 checks that
  `GraphGeometry`'s elbow routing draws them clear of Resource Storage's card, and moves a
  placement (content) if it does not.
- What a save written before K4 does with routes the scenario gained after it. K4 decides and
  tests this, either treating the new routes as drift or giving them to the loaded world, and it
  never does so silently. `contentVersion` moves with K4 either way.
- The standing draw of four new built lines against the 10,000 energy capacity. K4 may give the
  treatment archetype a lower draw than 200 if M3 shows the vessel's budget was sized without room
  for them.
