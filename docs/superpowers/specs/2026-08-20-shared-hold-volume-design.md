# Shared Hold Volume — Design

Date: 2026-08-20
Status: Draft

## Goal

Make a storage's capacity a **single shared volume** that every item competes for, rather than one
independent silo per item. A hold one third full of Basic Metals and one half full of Technical
Materials is **five sixths full**, and has one sixth of its volume left for anything at all.

This changes what `Room` means, and therefore when a transport line reports `DESTINATION_FULL` and
when a facility postpones a run. It changes no content, no save format and no id.

## Source material

`docs/superpowers/specs/2026-08-10-static-content-and-world-state-design.md` fixed the shape of the
capacity data: an item declares `holdCapacity`, a storage archetype declares `capacityPermille`,
and a storage's capacity for an item is the product over `FullHold`. That data is unchanged here.
Only its interpretation moves.

The reading it had until now was per item and independent, in `SimulationEngine.Room`:

> `CapacityOf(instance, known) - Available(storage, item)`

Nothing bounded the sum. On the shipped vessel that is not a theoretical gap: at tick 7,200 the
global Resource Storage holds 123.6% of a hold — 34.6% of a hold in Matter Mix, 25.0% in Basic
Metals, 21.4% in Technical Materials, 18.3% in Robot Frames and the rest in Hydrogen — because each
of those was measured against its own ceiling and none against the others.

The same gap is what made a facility buffer look permanently empty on the base graph. The card
divided `TotalAmount` by `TotalCapacity`, and `TotalCapacity` was the sum of every catalog item's
capacity — 204,500 for a facility buffer that can only ever receive two of the seven items. Factory
Alpha holding 29% of its Basic Metals ceiling rendered as 1%.

## Decisions

### 1. `holdCapacity` is a density, and the hold is measured in volume

An item's capacity in a storage answers "how much of this item, and nothing else, would fill this
storage". So one milli-unit of an item occupies `1 / CapacityOf(storage, item)` of that storage,
and a storage's occupancy is the sum of those fractions.

This is a reinterpretation, not a new field. It is also the reading the shipped content was
authored under: Matter Mix fills a hold at 5,000,000 and Robot Frames at 60,000, which only says
something about the vessel if a frame takes roughly eighty-three times the room a unit of mix does.

### 2. Occupancy is integer, in billionths of a hold

`StorageArchetype.FullVolume` is `1_000_000_000`. An item's occupancy is
`amount * FullVolume / capacity`, floored, and a storage's is the sum over the items it holds.

Floored, and the conversion back out is floored too, so every rounding error costs room rather than
inventing it: a storage can be a few milli-units short of full and never a milli-unit over. The
scale is a billion rather than the `FullHold` thousand so that the per-item error stays below one
part in a billion of a hold — at permille, a seven-item hold could hide most of a percent, and a
percent of the global hold is 50,000 milli-units of Matter Mix.

No `decimal`, no rational, no accumulating remainder. The largest intermediate is
`amount * FullVolume`, which for the largest shipped capacity is 5 × 10¹⁵ and has three orders of
magnitude of headroom in a `long`.

### 3. `Room` answers in units of the item asked about

`Room(storage, item) = (FullVolume - Occupied(storage)) * CapacityOf(storage, item) / FullVolume`.

The per-item ceiling is not checked separately, because it cannot be exceeded: an amount above
`CapacityOf` would occupy more than the whole volume. One rule, not two that could disagree.

### 4. A run's output is measured against the volume its inputs free

`CanStart` checked that the output fitted **before** the inputs were consumed. Under independent
silos that was merely cautious. Under a shared volume it is wrong, and deadlocks: a buffer nearly
full of Basic Metals has no room for Components until the metals that become them are withdrawn.

So the check becomes: remove the run's inputs from the occupancy, then ask whether the output fits.
The order of operations inside one run is input, then work, then output, and the check now models
that. The reason it is a check rather than an attempt is unchanged and stated in the code: a
facility that cannot place its output must not shred its input for nothing.

This does not make a facility immune to a full buffer. A schematic whose output is bulkier than its
input — Basic Metals at 500,000 to a hold becoming Components at 200,000 — still needs the
difference to be free. It removes only the deadlock that consuming nothing would have caused.

### 5. The snapshot reports a fraction, not two sums

`StorageState.TotalAmount` and `TotalCapacity` are removed and replaced by `FillPermille`.

Summing the amounts of unlike items was already meaningless — 3,600,000 milli-units of Matter Mix
plus 650 Robot Frames is not a quantity of anything — and summing the capacities produced the
denominator that made every buffer read empty. Both were derivable, which was their stated
justification; what they derived was not the number any surface wanted.

`FillPermille` is computed once, in the kernel, beside the rule that enforces it, so a storage card
and an inspector panel cannot disagree about how full a storage is. Per-item `Amount` and
`Capacity` stay on `ItemStock`: a panel listing contents wants them, and they are the honest
answer for one item.

### 6. A facility buffer reserves the room its own output needs

`RoomForDelivery` is what transport asks. It is the free volume **less one run's output** for every
facility working out of that storage, at the schematic each is set up for. `Room` — what production
asks when it deposits — does not subtract it, because production is who the room is being held for.

Without this the model is unplayable, and not by a margin. A standing feed fills a buffer until the
destination reports full, and a schematic whose output is bulkier per unit than its input then has
nowhere to put the result: Matter Mix separates into Basic Metals at 4,000 in to 800 out, which is
3.2% of a reactor buffer freed against 6.4% needed. The shipped vessel deadlocked permanently at
about tick 14,400 — every reactor and factory pinned at `DESTINATION_FULL`, robot frame production
frozen at 28,500 and unchanged 158,000 ticks later.

Buffer size does not fix it and neither does content tuning: any unbounded feed reaches the brim
eventually, and a player-authored program will be able to arrange one deliberately once programs
exist. The reservation makes the trap unreachable rather than unlikely.

A whole run's output is reserved rather than the amount by which it exceeds the run's inputs. The
stronger number means a buffer filled exactly to the reservation still has room for the output
*before* its inputs are consumed, so no ordering of deliveries against runs can trap it. A facility
with no loaded schematic falls back to the first task in its queue, so a fresh campaign reserves
from the first tick; one with neither reserves nothing, which is right, because it has no output to
place.

## Consequences to watch

**The shipped vessel fills up, and that is now visible.** Under independent silos the global hold
passed 100% at roughly tick 3,600 and kept going. It cannot, so the chain stops when the hold is
genuinely full — around tick 14,400, with Robot Frames taking 47% of the hold's volume and nothing
anywhere that consumes one.

This is not the deadlock above and must not be confused with it. No facility is trapped; every
buffer still has its reserved room, and draining the hold restarts the whole chain. The vessel has
simply produced everything its hold can carry, because the only consumer the GDD gives Robot Frames
is a mission, and missions do not exist yet. The scenario notes already say the acquisition loop is
missing on purpose. This makes the other end of that gap visible too.

Whether fourteen thousand ticks is the right number is a balance question for the scenario, and it
is deliberately not adjusted here.
