# Transport Lines As Conveyors — Design

Date: 2026-09-04
Status: Draft

## Goal

Make a transport line a **conveyor of authored length that holds cargo between ticks**. Material
picked up at the source travels for as many ticks as the line is long, and the line reports how much
of its capacity is in flight. When the far end cannot accept a delivery, that line freezes in place
at whatever fill it had — and only that line: the opposing direction is a separate object and keeps
running.

This changes the tick, the transport state, the save format and the scenario schema. It changes no
id, no archetype and no catalog file.

## Source material

[Issue #34](https://github.com/Gatov/DimenshipGame/issues/34):

> The transport line should act as a two-way conveyor. It can take weight every tick. It's blocked
> (one way) only if accepting side can't receive. It shows % of max capacity in-flight (each side).

The GDD already promised the holding half of this, and the code already contradicted it. §5.10:

> A part in transit is never lost. If its destination is not ready — a reactor mid-run, a full
> buffer — the transport holds it and retries until the destination frees, rather than dropping it
> or diverting it somewhere it does not belong.

`SimulationEngine.TryMove` withdrew from the source and deposited into the destination inside one
tick, atomically, and said so in its own doc comment: *"a transfer is not in flight between ticks …
not cargo hanging in a tube."* Nothing held anything, because nothing was ever between two places.
This spec closes that gap.

## Decisions

### 1. A conveyor stays two one-way lines, not one bidirectional object

A two-way link is authored as two `ScenarioRoute`s today — `*_feed` and `*_return` — and the loader
already *requires* both for a commandable facility. The base graph already merges an opposing pair
into one double-headed edge, so the player already sees one conveyor.

Merging them in state was considered and rejected. The two directions of a shipped link do not share
a rate — `reactor_feed` moves 260 a tick and `reactor_return_basic` moves 52 — so a merged archetype
would need two throughput numbers, and merging the eighteen routes into ten objects would halve the
vessel's authored standing power, which a content rule checks.

The decisive point is that the issue's hard requirement is already satisfied by the split: two
objects have two queues, two belts and two `BlockReason`s, and there is no code path by which one
can stop the other. Nothing here has to be built for "blocked one way only" to hold. Merging would
have been a rename with a balance pass attached.

### 2. The belt holds cargo, and cargo is saved state

`TransportInstance` gains a `Belt`: a fixed list of `LengthTicks` slots, index `0` at the destination
end, each holding at most one `BeltSlot` — a task id, an item and a quantity.

One slot holds one item for one task because a line already services at most one task per tick, so a
tick's intake is never more than that, and one slot is exactly one tick of belt. That correspondence
is what makes capacity derivable rather than authored (Decision 4).

The slot carries its **task id** rather than inferring it from the line's current task, because
several hauls are in flight at once (Decision 6) and a delivery has to be credited to the transfer
that picked it up.

### 3. A tick is unload, then advance, then load

The order is the determinism contract.

- **Unload first** is what keeps the existing guarantee that transport runs before production: cargo
  reaching the head this tick is deposited before the production phase, so a facility can use it the
  same tick.
- **Advance before load** is what makes a slot loaded now sit a full `LengthTicks` from being
  deliverable rather than one short of it.

Load-then-unload was rejected: it gives zero latency, which preserves every existing tick count, but
leaves the belt empty whenever flow is healthy — so the in-flight reading the issue asks for would
read `0%` at all times except during a jam, and would be a blockage lamp rather than a load gauge.

### 4. Capacity is throughput times length, and is never authored

A slot holds one tick of intake and there are `LengthTicks` slots, so capacity falls out as
`ThroughputPerTick × LengthTicks`. Authoring a capacity beside those two numbers would create a
third that can drift from them, and the symptom of that drift is a line that either strands material
it should have carried or holds more than a tick of throughput on a slot that is one tick wide.

Capacity is a **quantity** in milli-units, not a volume. Mixed items on one belt are summed by
quantity. This is deliberate and follows the number it is built from: throughput is already a
quantity a tick, not a volume a tick. A belt is not a storage and does not use `OccupiedVolume`.

### 5. A blocked belt freezes in place, it does not accumulate

If the head cannot be emptied, nothing behind it moves and the line takes nothing on. The fill stays
exactly where it was, however partial — a belt three tenths loaded reads `BLOCKED` at three tenths
and stays there.

The head still deposits **as much as fits**. Partial unload keeps the promise of GDD §5.10 precisely:
the remainder stays on the head rather than being dropped or diverted.

An accumulating belt — cargo packing toward the blocked end until capacity is reached — was
considered, and is the model in which capacity does real buffering work. It was rejected because a
rigid belt is what the project owner specified, and because it makes the stranded amount trivially
bounded: a frozen line holds at most what it was holding at the moment it stopped, and the source is
not drawn down another unit while it is stuck.

The price is written down and tested: **a destination-full head stalls the whole line, including the
transfers queued behind it.** Under the old model a blocked transfer was skipped and the next one
ran. That still holds for a *source*-side block — nothing to pick up leaves the belt free — but not
for a destination-side one, because there is nowhere for the queue behind the head to go. The two
halves are `ATransferWithNothingToPickUp_DoesNotStallTheOnesBehindIt` and
`ADestinationBlock_StopsTheWholeLine_IncludingTheTransfersBehindIt`.

### 6. Progress counts deliveries; pickup is counted separately

`TaskInstance` gains `LoadedQuantity` beside `MovedQuantity`. A transfer loads against
`target - LoadedQuantity` and completes on `MovedQuantity >= target`.

Completing at pickup was rejected outright: it would tell a plan its material had arrived a whole
belt early, and `PlanState.Complete` would fire while the goods were still travelling.

`TransportInstance.Current` clears when a transfer is entirely **aboard**, not when it completes.
Holding it until delivery would park the belt for a whole `LengthTicks` between two consecutive
hauls — the exact cost having a belt exists to avoid. This is why several transfers are in flight at
once and why a slot names its task.

### 7. Conditions gate pickup, never delivery

The planning spec carves out that conditions never re-gate a run already in flight, and noted that
transfers had no analogue because nothing was ever in flight. They do now: cargo on a belt is in
flight, so a condition that turns false stops the next pickup and never strands what is already
travelling. The belt always clears.

### 8. Length is authored on the route, not on the archetype

`ScenarioRoute` gains `lengthTicks`; `TransportArchetype` is untouched.

Length is physical distance and belongs to the instance. The four hold-star legs share `factory_feed`
and `factory_return` and are plainly not the same distance, so an archetype-level length would force
them to one number. The archetype keeps holding the numbers that describe a *class* of line — the
rate and the standing draw — which is what its doc comment already says it is for.

The shipped vessel's lengths are the **Manhattan distance in grid cells** between the two cards a
route is drawn between: star legs run 2 or 3, the two factory interconnects run 1. A rule rather
than eighteen judgement calls, and one that makes the schematic honest — a leg that looks longer is
longer to travel.

`lengthTicks` is optional and defaults to `1`, the shortest belt there is. Zero is rejected with
*"a line has to be at least one tick long"*, for the same reason a throughput of zero is: it is a
line that moves nothing.

### 9. Three readings on a line, not one

`MovedLastTick` is replaced by `LoadedLastTick` and `DeliveredLastTick`, and joined by
`CargoFillPermille` against `Capacity`.

A belt makes the two rates differ: a line whose source has run dry is still delivering, and one that
has just started is loading without having arrived. The graph's edge band reads **intake**, because
that is what "working at this fraction of throughput" means for a conveyor and it is the reading that
is right on the first tick of a haul.

The in-flight percentage is drawn **beside each arrowhead**, in that direction's band colour, and
there is deliberately no merged figure. The worse-of-two rule that suits a shared stroke would report
a jam on the side that has none — and per-side is the whole point of the reading. A belt carrying
anything at all reads at least `1%`; rounding a live load down to nothing would say the line is empty
at the moment it starts to back up.

### 10. The save carries the belt sparsely, and the format version stays at 1

`TransportDto` gains `lengthTicks` and a `belt`: a position-ordered list of the occupied slots only.
Sparse rather than an array with a null per empty slot, because most belts are mostly empty and a
diff between two saves should show the cargo that moved rather than the padding around it.

A saved position the belt does not have is **reported**, not clamped or dropped — it means a route
was shortened in content while a save sat mid-haul, and both alternatives are a vessel silently
changing how much material it owns across a load. Belt items are checked against the catalog like
every other id.

`saveVersion` stays at `1`. The record shape changed, but there are still no saves in the wild and an
upgrader for a format nobody wrote would be a fiction — the same reasoning the constant already
carried.

## Where this departs from the transcribed spec

`docs/specs/dimenship-planning-and-task-execution.md` §7 describes partial execution as *"immediately
transfer the 19 units currently available and postpone the remaining 41"*. That still happens; what
changed is that the 19 are picked up immediately and arrive `LengthTicks` later. The transcription is
one of the project owner's handwritten pages and is not edited here — it is a source document, and
this section is the record of the divergence.

## Not built

- **No merged bidirectional entity.** See Decision 1. If the vocabulary ever wants one object, that
  is a content and save migration, not a behaviour change.
- **No accumulating belt.** See Decision 5.
- **No per-slot travel other than one tick per slot.** A belt does not speed up under load or slow
  down when full.
- **No belt volume.** Cargo is summed by quantity and counts toward no storage's fill.
- **Nothing builds or lengthens a line at runtime.** Routes are authored, and `SizeBelt` is called
  once, at seed or at load.
