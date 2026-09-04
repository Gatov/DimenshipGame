# Transport Lines As Conveyors — Implementation Plan

Status: Built

**Goal:** A transport line becomes a conveyor of authored length that holds cargo between ticks,
reports how much of its capacity is in flight, and freezes in place — on its own, never taking the
opposing direction with it — when its destination cannot accept a delivery.

**Spec:** `docs/superpowers/specs/2026-09-04-conveyor-belt-design.md`.
**Issue:** [#34](https://github.com/Gatov/DimenshipGame/issues/34).

**Architecture:** `TransportInstance` gains a positional belt of `LengthTicks` slots and the two
tick readings a belt makes differ. `StepHauler` becomes unload → advance → load. Length is authored
on the route; capacity is derived from it. The save writes the belt sparsely by position. The shell
reads intake for the edge band and draws the in-flight percentage beside each arrowhead.

## Why

The GDD promised since §5.10 that a part in transit is never lost and that a transport holds it and
retries. The kernel did the opposite: `TryMove` withdrew and deposited inside one tick, so nothing
was ever in transit and there was nothing to hold. The issue asks for the reading that gap makes
impossible — how full each side of a link is — and the honest way to supply it is to make the thing
real rather than to invent a number for a belt that does not exist.

The second reason is legibility. A line's edge colour reported one number, `MovedLastTick`, which
under a belt splits into what was taken on and what was put down. Those differ exactly when
something interesting is happening: a line draining after its source ran dry, or one loading before
anything has crossed. One reading could not say either.

## What was built

1. **Kernel.** `BeltSlot` and `TransportInstance.Belt` / `LengthTicks` / `LoadedLastTick` /
   `DeliveredLastTick` in `State/VesselState.cs`; `TaskInstance.LoadedQuantity`. `StepHauler` split
   into `Unload` / `Advance` / `Load`, with `CanMove` becoming `CanLoad` — the destination no longer
   appears at pickup at all, because room is asked for at the far end of the belt a whole
   `LengthTicks` later. `Freeze` reports the line and every transfer behind the head on
   `DestinationFull`. No new `PostponeReason` member was needed.
2. **Content.** `lengthTicks` on `ScenarioRoute` and its DTO, optional and defaulting to 1, rejected
   below 1. Authored on all eighteen shipped routes as the Manhattan distance between the cards each
   route joins. The `notes` in `transports.json` and `default_vessel.json` say where length lives
   and that capacity is derived.
3. **Save.** `TransportDto.LengthTicks` and a sparse position-ordered `Belt`; `LoadedQuantity` on the
   task DTO. Positions outside the belt and belt items the catalog lost are reported as drift.
   `saveVersion` stays at 1.
4. **Planner.** `PlannerTransport.LengthTicks`, and `EstimateTicks` pays the belt once per leg —
   once, not once per tick of loading, because the line keeps loading while the earlier cargo
   travels and only the last slot has the whole journey ahead of it.
5. **Shell.** The edge band reads `LoadedLastTick`; `GraphCanvas.Edge` carries a fill permille per
   direction and draws it beside that direction's arrowhead; the transport inspector gains `ROUTE`
   with a tick count, `IN FLIGHT`, and `LOADED` / `DELIVERED` in place of `MOVED`, and `CARRYING`
   reads the belt rather than the current task.

## Behaviour changes accepted

- **A destination-full head stalls the whole line.** The rigid belt's price, tested from both sides:
  a source-side block still lets the queue behind it run, a destination-side one does not.
- **Every hop is `LengthTicks` slower**, so commissioning, feed timings and plan estimates all move.
  `AFacilityCommissions_TheTickItsUnitArrives_AndProducesTheTickAfter` needed two ticks rather than
  one; the delivery and the commissioning are still the same tick, which is the part under test.
- **`AFedFacility_KeepsRunning_ForAsLongAsThereIsAnythingToFeedIt`** now ends its hundred ticks
  blocked on `InsufficientInputMaterial` rather than mid-run — a hundred ticks of a five-a-tick feed
  is the whole of the hold's ore. The assertion moved to the claim the test was always making: it
  stops on the shortage it actually has, and never on a destination it cannot deposit into.

## Follow-up: what "blocked" means

The first cut let a task's postponement set the line's own `BlockReason`, so a line whose source was
empty reported itself blocked with an empty belt — visible on the shipped vessel from tick 1, where
`extractor_out` read blocked and counted as an alert before it had ever carried anything.

Corrected in Decision 11 of the spec. A line is blocked only when its belt is frozen; `Postpone` no
longer touches the line, only the transfer. Queued work that could not be picked up onto an empty
belt is the new transport-only `ExecutorStatus.NothingToCarry`, and a line still delivering with
nothing left to pick up is `RunningTask`. Three tests moved with it: the partial-transfer case now
expects `NothingToCarry` with a null block reason, `Postponement_IsRecordedOnce` no longer expects an
`AllTasksBlocked` event, and the shipped vessel's first-tick event sequence lost that event too.

## Verification

```bash
dotnet test DimenshipGame.sln
```

259 kernel tests and 64 shell tests pass; the Godot assembly builds. New coverage: belt travel time,
derived capacity, a belt frozen at three tenths that neither fills nor draws down its source, a
frozen belt resuming where it stopped, a condition that gates pickup while the belt still clears,
route length rejected at zero and defaulted at one, and cargo mid-flight surviving a save with every
parcel on the slot it was on.
