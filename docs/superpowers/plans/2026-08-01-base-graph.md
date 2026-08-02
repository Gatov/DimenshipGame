# Base Graph Focus View — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat Overview focus with the graph the game is read from — transport lines gain real `From`/`To` endpoints so routes become topology, storages and facilities are drawn as authored node cards wired by orthogonal edges coloured by live load, and a selected node's detail renders in a new Facility Inspector panel. The view is strictly read-only.

**Architecture:** Route endpoints, the four new snapshot values and the authored placements land in `Dimenship.Core`, still engine-free, still integer-only. The graph's arithmetic — flow bands, cell rects, edge polylines, edge hit-testing — lands in `Dimenship.Shell`, which references nothing and is therefore unit-testable without an engine or an editor. Only drawing, input and layout live in `dimenship/scripts/ui/focus/`.

**Tech Stack:** .NET 8, C# 12, NUnit 4.6.1. Godot 4.7.1-mono for the view only.

**Spec:** `docs/superpowers/specs/2026-08-01-base-graph-design.md`

## The owner's deviation from the spec

The graph does **not** land in the `base_graph` placeholder. It **replaces** the Overview focus.

- `ShellRoot.OverviewId` (value `"overview"`) constructs `BaseGraphFocus`; its descriptor is retitled `"Base Graph"`.
- `ShellRoot.BaseGraphId` and its `Placeholder(...)` registration are deleted.
- `dimenship/scripts/ui/panels/OverviewFocus.cs` is deleted.
- `DefaultLayout.ActiveFocus` is already `OverviewId`, so the graph becomes the default focus with no change to the default layout.
- Saved layouts naming `base_graph` fall back through the existing, already-tested `LayoutSerializer.Resolve` path with one warning.

Everything else in the spec stands, including its Out of scope list.

## Global Constraints

- Target framework is `net8.0` for every project. No new assemblies are created by this plan.
- **No `float` or `double` anywhere in `Dimenship.Core`.** `BaseGraphLayout` is grid cells, not pixels, partly for this reason.
- `Dimenship.Core` must never reference `GodotSharp` or `Dimenship.Shell`.
- **`Dimenship.Shell` keeps zero project references.** It cannot name `ExecutorId` or `StorageId`, which is why `GraphSelection` carries a raw `string`. Do not add a reference to make the graph code tidier — that reference direction is load-bearing.
- Iteration order comes from `WorldDefinition`'s lists, never from dictionary enumeration.
- Tests precede the code they cover. NUnit constraint model, `Assert.That(actual, Is.EqualTo(expected), "prose reason")`, fixtures named `Subject_Behaviour_AndFurtherClause`.
- Nothing outside `ShellPalette` names a colour. Every state colour is paired with a text code. Only a live measured value may glow.
- Every task ends with `dotnet build DimenshipGame.sln` clean at **zero warnings** and `dotnet test` green.
- **The Godot editor is not on `PATH` here.** No task may claim any visual behaviour is verified. The Godot project does compile here, so the view is compiler-verified; running it is the project owner's step, listed in Task 6.
- One commit per task, on `feat/base-graph-focus`.

## Baseline

`bcba986`, working tree clean, **115 tests passing** — 107 in `Dimenship.Core.Tests`, 8 in `Dimenship.Shell.Tests`. `dotnet build DimenshipGame.sln` clean at zero warnings.

---

## Execution status

All seven tasks complete.

| Task | State | Commits | Tests after |
| :--- | :--- | :--- | :--- |
| 0 — Plan document | Complete | `99f40bb` | 115 |
| 1 — Shell graph arithmetic | Complete | `b2804f1` | 153 |
| 2 — Kernel routes, snapshot, placements | Complete | `dcd2105` | 175 |
| 3 — Facility Inspector panel | Complete | `edcebd7` | 176 |
| 4 — Selection plumbing and palette | Complete | `edcebd7` | 176 |
| 5 — The graph view | Complete | see below | 176 |
| 6 — Replace Overview | Complete | see below | 176 |

**Deviations from this plan, and why:**

1. **Tasks 3 and 4 were swapped and committed together.** The Facility Inspector reads
   `ShellContext.CurrentSelection`, which Task 4 creates, so the panel could not compile before the
   plumbing existed. Task 4's palette tokens came along with them.
2. **`ExecutorState` gained `LocalStorage`,** which neither the spec nor this plan listed. The
   spec's inspector table asks for a selected facility's local storage contents, and the storage a
   facility works is not derivable from anything else on the snapshot. One field, one test.
3. **`ShellRoot.SelectionChanged` redelivers the cached snapshot to the inspector zone.** Without
   it the panel would render the previous selection until the next tick, because `_Process` pushes
   a snapshot only when the snapshot itself changes and a click does not change it.
4. **The power node is pinned to the right of the widest authored column,** not above the graph.
   The properties that matter — a fixed cell, no edges, never colliding — hold either way, and the
   authored grid starts at row zero, so "above" would have meant negative rows and a content size
   measured from something other than the origin.
5. **A merged edge is clicked twice to reach its return leg.** An opposing pair draws as one
   double-headed edge, which would otherwise leave the second line selectable only through its
   endpoints.

---

### Task 0: Plan document

**Files:**
- Create: `docs/superpowers/plans/2026-08-01-base-graph.md`

- [x] **Step 1:** Record the baseline test count by running `dotnet test`, not by trusting a prior plan's final figure.
- [x] **Step 2:** Write this plan, recording the owner's Overview deviation explicitly so the divergence from the committed spec is discoverable from the repository alone.
- [x] **Step 3:** Commit before any code.

---

### Task 1: `Dimenship.Shell` graph arithmetic

Pure integer maths — no engine, no Godot, no `Control`. This is where the bulk of the graph's correctness lives so that almost none of it needs the editor to verify.

**Files:**
- Create: `src/Dimenship.Shell/GraphSelection.cs`
- Create: `src/Dimenship.Shell/FlowBands.cs`
- Create: `src/Dimenship.Shell/GraphGeometry.cs`
- Test: `tests/Dimenship.Shell.Tests/FlowBandsTests.cs`
- Test: `tests/Dimenship.Shell.Tests/GraphGeometryTests.cs`

**Interfaces produced:**

```csharp
public enum GraphNodeKind { Executor, Transport, Storage, Power }
public readonly record struct GraphSelection(GraphNodeKind Kind, string Id);

public enum FlowBand { Idle, Low, Normal, High, Blocked }
public static class FlowBands
{
    public static FlowBand Classify(long movedLastTick, long throughputPerTick, bool blocked);
}

public static class GraphGeometry
{
    public const int CellWidth = 220, CellHeight = 96;
    public const int GutterX = 48, GutterY = 40;

    public static (int X, int Y, int W, int H) CellRect(int column, int row);
    public static IReadOnlyList<(int X, int Y)> EdgePolyline(
        (int X, int Y, int W, int H) from, (int X, int Y, int W, int H) to, int parallelIndex);
    public static (int W, int H) ContentSize(IEnumerable<(int Column, int Row)> cells);
    public static int HitDistanceSquared(IReadOnlyList<(int X, int Y)> polyline, int x, int y);
}
```

- [x] **Step 1: Tests for `FlowBands.Classify`.** Boundaries at 0, 1, 329/330, 799/800 and 1000 permille; `blocked` overrides a high reading; zero and negative throughput are `Idle`.
- [x] **Step 2: `FlowBands.Classify`.** `blocked` wins over every load reading. `throughputPerTick <= 0` returns `Idle` — never a divide. Otherwise permille is `movedLastTick * 1000 / throughputPerTick`, floored: `0` → `Idle`, `1..329` → `Low`, `330..799` → `Normal`, `>= 800` → `High`.
- [x] **Step 3: Tests for `CellRect` and `ContentSize`.** Column and row zero, then beyond; `ContentSize` bounds an arbitrary cell set including a non-zero origin.
- [x] **Step 4: `CellRect` and `ContentSize`.** `CellRect(c, r)` is `(c * (CellWidth + GutterX), r * (CellHeight + GutterY), CellWidth, CellHeight)`. `ContentSize` is the bounding box over `CellRect` of every supplied cell; an empty set is `(0, 0)`.
- [x] **Step 5: Tests for `EdgePolyline`.** Same-row, same-column and diagonal pairs; both directions of each; parallel offsets at indices 0 and 1 do not coincide; every segment is axis-aligned.
- [x] **Step 6: `EdgePolyline`.** Orthogonal segments only, elbowing in the gutter between the two cells:
  - **Same row:** leave the source's facing vertical side at mid-height, one horizontal run, arrive at the target's facing side.
  - **Same column:** the vertical mirror — leave the facing horizontal side at mid-width, one vertical run, arrive at the target's facing side.
  - **Diagonal:** leave the facing vertical side, run horizontally to the mid-gutter x between the two columns, turn vertically to the target's centre y, then run horizontally into the target's facing side.
  - **Parallel offset:** every point is displaced by `parallelIndex * 6` perpendicular to its segment's axis — horizontal runs shift in y, vertical runs shift in x — so parallel edges between one pair never overprint.
  - Merging an opposing pair into one double-headed edge is the **view's** job, not this method's.
- [x] **Step 7: Tests for `HitDistanceSquared`.** Zero on a vertex and on a point along a segment; grows with perpendicular distance; a point past a segment's end measures to the endpoint, not the infinite line.
- [x] **Step 8: `HitDistanceSquared`.** Minimum squared distance from the point to any segment of the polyline, integer throughout. This is what makes an edge clickable — a transport line has no card of its own.

**Verification:** `dotnet test` green. Nothing in this task touches Core or Godot, so the rest of the suite must be untouched.

---

### Task 2: Kernel — routes, snapshot fields, placements

The behaviour change, and the task whose real cost is test migration.

**Files:**
- Modify: `src/Dimenship.Core/Simulation/WorldDefinition.cs`
- Modify: `src/Dimenship.Core/Simulation/WorldSnapshot.cs`
- Modify: `src/Dimenship.Core/Simulation/SimulationEngine.cs`
- Modify: `src/Dimenship.Core/Planning/IWorldView.cs`
- Modify: `src/Dimenship.Core/Planning/ProductionPlanner.cs`
- Create: `src/Dimenship.Core/Presentation/BaseGraphLayout.cs`
- Modify: `tests/Dimenship.Core.Tests/WorldBuilder.cs`
- Modify: `tests/Dimenship.Core.Tests/Production/TransportTests.cs`
- Modify: `tests/Dimenship.Core.Tests/Planning/ProductionPlannerTests.cs`
- Modify: `tests/Dimenship.Core.Tests/Planning/WorkedExampleTests.cs`
- Create: `tests/Dimenship.Core.Tests/Presentation/BaseGraphLayoutTests.cs`

**Interfaces produced:**

```csharp
public sealed record TransportExecutorDefinition(
    ExecutorId Id, string Label, StorageId From, StorageId To,
    long ThroughputPerTick, long StandingPowerDraw);

public sealed record PlannerTransport(ExecutorId Id, StorageId From, StorageId To, long QueuedTransfers);

public sealed record NodePlacement(int Column, int Row);
public sealed record BaseGraphLayout(
    IReadOnlyDictionary<ExecutorId, NodePlacement> Producers,
    IReadOnlyDictionary<StorageId, NodePlacement> Storages)
{
    public static BaseGraphLayout ForDefaultWorld();
}
```

Snapshot additions — `From`, `To`, `CarriedItem`, `ThroughputPerTick`, `MovedLastTick` on `TransportExecutorState`; `TotalAmount`, `TotalCapacity` on `StorageState`; `RunTicksTotal` on `ExecutorState`.

- [x] **Step 1: Tests for route validation.** A route naming an unknown `From` or `To` storage throws; `From == To` throws; a transfer whose endpoints do not match its line's route is rejected, both as an `InitialTransfer` and on `Commit`. Follow the `ATransportLineWithNoThroughput_IsADefinitionError` template.
- [x] **Step 2: Add `From`/`To` to `TransportExecutorDefinition`,** and validate in the `SimulationEngine` constructor beside the existing throughput and duplicate-id checks. `WorldDefinition` itself validates nothing today; keep it that way. Messages in house style — the offending id in single quotes, then a sentence saying why it is an error.
- [x] **Step 3: Match the route in `EnqueueTransfer`,** which already rejects unknown executor, unknown `from`, unknown `to`, and `from == to`. `Commit` and the constructor's `InitialTransfer` loop both route through it and inherit the check for free.
- [x] **Step 4: Update the default world.** `feed_line` becomes `MainHold → SmelterBuffer`, `return_line` becomes `SmelterBuffer → MainHold` — exactly what its `InitialTransfers` already do, so no behaviour changes. `DefaultWorld_FirstTick_EmitsExactEventSequence` and the `TotalEventsEmitted == 4` assertion must stay green **with no edit**. If either needs changing, stop and re-read this step.
- [x] **Step 5: Migrate `WorldBuilder.Transport`** to take `From`/`To`, then fix every call site. Three need real thought:
  - `WorkedExampleTests` gives one `Hauler` line six distinct routes (`Hold ↔ RefineryBuffer`, `Hold ↔ FactoryBuffer`, `Hold ↔ ArmorBuffer`). Replace it with six lines named for their routes and update the plan-assertion strings that name `hauler`. The plan's *shape* — which runs land on which facility, which shortages appear — must not change; if it does, the planner change in Step 7 is wrong.
  - `ATransportLine_FinishesOneTransferBeforeStartingTheNext` queues `Hold→Buffer` and `Hold→far` on one line, now illegal. Restructure to two transfers on the same `Hold→Buffer` route and assert sequencing from the two `TransportTaskState.MovedQuantity` values instead of two destinations. The behaviour under test is preserved.
  - `ABlockedTransfer_DoesNotStallTheOnesBehindIt` blocks by using an empty source storage, also now illegal. Restructure so both transfers ride `Hold→Buffer` and the first is blocked by requesting an item with nothing on hand at `Hold`.
  - `StorageTests`' `Array.Empty<TransportExecutorDefinition>()` call sites are unaffected; confirm by compile.
- [x] **Step 6: Tests for planner route selection.** The planner picks the line whose route matches rather than the least loaded of all lines, and reports `ShortageKind.NoCompatibleExecutor` when no route matches. Load-based tie-breaking among *matching* lines still holds.
- [x] **Step 7: Route-match in the planner.** `PlannerTransport` gains `From`/`To`, projected from the definition. `ChooseTransport()` becomes `ChooseTransport(StorageId from, StorageId to)` — filter to matching routes first, then keep the existing lowest-`QueuedTransfers + _transportLoad` rule tie-broken by definition order. Its one caller, `Move`, passes its own endpoints.
- [x] **Step 8: Tests for the new snapshot values.** `MovedLastTick` equals the quantity delivered that tick and returns to zero on an idle tick; storage totals equal the sum over items in item-definition order; `RunTicksTotal` is unchanged by a postponement mid-run.
- [x] **Step 9: Add the snapshot fields.** `MovedLastTick` is a counter on the `Hauler` runtime class, reset in the per-tick hauler loop beside the `PowerDraw` reset and incremented in `TryMove` beside `task.MovedQuantity += quantity`. `TotalAmount`/`TotalCapacity` are summed over `Items` where `StorageState` is built — derivable on purpose, because two surfaces summing independently can disagree about a fill percentage. `RunTicksTotal` is **derived, not stored**, mirroring `RunTicksRemaining`: `ceil(EffortPerRun / WorkRatePerTick)` while a run is active, else zero. Neither input changes mid-run, so this satisfies "fixed at run start" without a new mutable field.
- [x] **Step 10: Tests for `BaseGraphLayout`.** Every production executor and every storage in the default world has a placement; no two placements share a cell; every route endpoint is a placed storage. These are the assertions that turn a layout mistake into a failing test instead of a bug the owner finds by looking at the screen.
- [x] **Step 11: `BaseGraphLayout`** in a new `Presentation` folder — grid cells, not pixels, because pixel geometry belongs to the view and changes with zoom while "the smelter sits right of the hold" is content. Default world: `Extractor01 (0,0)`, `SmelterA (2,0)`, `MainHold (0,1)`, `SmelterBuffer (2,1)`. Transport executors get no placement; they are edges. Power gets none either; the view pins it.

**Verification:** `dotnet test` green. The three determinism fixtures — `Advance_InOneCall_MatchesManySingleTickCalls`, `DefaultWorld_FirstTick_EmitsExactEventSequence`, `TwoEnginesFromTheSameDefinition_ProduceIdenticalEventStreams` — must pass **unedited**.

---

### Task 3: Facility Inspector panel

**Files:**
- Create: `dimenship/scripts/ui/panels/FacilityInspectorPanel.cs`

`PanelId` `facility_inspector`, `ZoneKind.Panel`. Built on the `EnergyBudgetPanel` idiom exactly: all UI in `_Ready()`, `HBoxContainer` → label / `ExpandFill` spacer / value rows, `ShellTheme.Surface` styleboxes, the grow/shrink/update three-loop list sync, `_lastX`-guarded colour overrides, `Units.Format` for every quantity.

| Selection | Content |
| :--- | :--- |
| Executor | Status, configured schematic, run progress and energy charged, queued production tasks with state and last postpone reason, local storage contents. |
| Transport | Route endpoints, carried item, moved against throughput, status, queued transfers with requested and moved quantities. |
| Storage | Every item row: amount, capacity, fill. |
| Power | Capacity, draw, reserve, cap hits, starved ticks — deliberately a subset of Energy Budget, which keeps the per-consumer breakdown. |
| None | `NO SELECTION`. |
| Selected id absent from the snapshot | `NO LONGER PRESENT`. |

- [x] **Step 1:** Build the panel against `ShellContext.CurrentSelection`, re-resolving the identifier against the snapshot on every `OnSnapshot`. It holds no reference to the graph view.
- [x] **Step 2:** Zero `TotalCapacity`, zero `RunTicksTotal` and zero `ThroughputPerTick` render empty bars. No division.

**Verification:** `dotnet build` clean. Behaviour is the owner's to confirm in Task 6.

---

### Task 4: Selection plumbing and palette tokens

**Files:**
- Modify: `dimenship/scripts/ui/ShellActions.cs`
- Modify: `dimenship/scripts/ui/ShellContext.cs`
- Modify: `dimenship/scripts/ui/ShellRoot.cs`
- Modify: `dimenship/scripts/ui/ShellPalette.cs`

- [x] **Step 1:** `ShellActions` gains `Action<GraphSelection?>? SelectionChanged` and `Action? InspectRequested`, as public fields matching the existing style.
- [x] **Step 2:** `ShellContext` gains `GraphSelection? CurrentSelection`, so the inspector renders correctly when it is mounted *after* the selection was made rather than showing an empty state until the player clicks again.
- [x] **Step 3:** `ShellRoot` registers `facility_inspector` and handles `InspectRequested` by swapping the Inspector zone to it and expanding the zone if collapsed — **unconditionally, on every selection**. The player clicked a node to see its detail; a rule that only sometimes shows it would be worse than one that always does. This is the reason selection routes through `ShellActions` instead of the panel reaching for the zone.
- [x] **Step 4:** `ShellPalette` gains `FlowIdle` (= `TextDim`), `FlowLow`, `FlowNormal` (= `StateOk`), `FlowHigh` (= `StateWarn`), `FlowBlocked` (= `StateFault`). Only `FlowLow` is a genuinely new colour. They alias today, but an edge asking for `StateWarn` when it means "high load" is how the palette rule erodes.

**Verification:** `dotnet build` clean at zero warnings.

---

### Task 5: The graph view

**Files:**
- Create: `dimenship/scripts/ui/focus/BaseGraphFocus.cs`
- Create: `dimenship/scripts/ui/focus/GraphCanvas.cs`
- Create: `dimenship/scripts/ui/focus/NodeCard.cs`
- Create: `dimenship/scripts/ui/focus/ExecutorCard.cs`
- Create: `dimenship/scripts/ui/focus/StorageCard.cs`
- Create: `dimenship/scripts/ui/focus/PowerCard.cs`
- Create: `dimenship/scripts/ui/focus/GraphLegend.cs`
- Create: `dimenship/scripts/ui/focus/ResourceStrip.cs`

**Node tree, and why.** `BaseGraphFocus` hosts a plain `Control _viewport { ClipContents = true, MouseFilter = Stop }`, which hosts `GraphCanvas`. `GraphCanvas` is a plain `Control`, deliberately **not** a container: a container lays out every `Control` child, which would fight the explicit `Position`/`Size` that `CellRect` gives each card. This is the same constraint `FrostPane` documents, resolved the other way — `Control` rather than `Node2D`, because cards must be focusable and take GUI input.

- [x] **Step 1: `GraphCanvas`.** `MouseFilter = Ignore` — it draws, it does not take input. `_Draw` renders each edge as a polyline plus arrowheads, coloured by `FlowBands.Classify(MovedLastTick, ThroughputPerTick, blocked)` through the new palette tokens. An opposing route pair merges to one double-headed edge; parallel edges between one pair use ascending `parallelIndex`. Only edges may glow — they are a live measured value; the grid, borders and labels may not.
- [x] **Step 2: `NodeCard` and the three card kinds.** Cards are `GraphCanvas` children with `MouseFilter = Stop` and `FocusMode = All`, so Godot's deepest-child-first GUI delivery gives a card click priority over the viewport, and the shell's existing `Tab` traversal reaches them with `Enter` selecting. No graph-specific key.
  - **Executor** — label, `FacilityType`, `RUNNING`/`SWITCHING`/`IDLE`/`BLOCKED` with the `PostponeReason` code when blocked, configured schematic, queued task count, run progress from `RunTicksRemaining` against `RunTicksTotal`.
  - **Storage** — label, fill bar from `TotalAmount` against `TotalCapacity`, up to three item rows in item order.
  - **Power** — capacity, draw, reserve, cap hits, starved ticks. Pinned to a fixed cell above the graph with no edges: energy is a global pool and drawing power edges would be a lie.
- [x] **Step 3: `BaseGraphFocus` pan, zoom and selection.** Pan is `GraphCanvas.Position`; zoom is `GraphCanvas.Scale` at fixed steps 50/75/100/150/200%, anchored at the cursor by `newPos = cursor - (cursor - oldPos) * (newZoom / oldZoom)`. Fixed steps rather than continuous zoom keep text on whole-pixel sizes, and `Scale` transforms child input coordinates for free so card hit areas follow zoom with no extra work.
- [x] **Step 4: Input**, of which this project has none today — the only existing handler forwards keys to `ShellActions`. `_viewport._GuiInput` takes wheel-zoom, press-and-drag pan, and a click with no drag: convert viewport-local to canvas coordinates by `(local - canvas.Position) / zoom` and hit-test each edge polyline with `HitDistanceSquared`; nearest within a threshold selects that transport line, otherwise clear the selection. `F` (fit to viewport, snapped to the nearest zoom step) goes in `BaseGraphFocus._UnhandledKeyInput`, which runs ahead of `ShellRoot._UnhandledInput` and collides with no existing binding.
- [x] **Step 5: `GraphLegend`,** the five flow bands pinned bottom-left, outside the panned canvas.
- [x] **Step 6: `ResourceStrip`,** a compact row of resource tiles pinned above the canvas. `OverviewFocus`'s tiles are the only surface showing `ResourceStock.NetRatePerTick`, and storage nodes do not carry it; replacing a view must not silently drop information it was the sole source of.
- [x] **Step 7: Redraw** on snapshot change, pan, zoom and selection change. Nothing animates, so there is no per-frame redraw. Flowing dashes along active edges are attractive and are an open item, not v1 — they would force a redraw every frame for a graph that changes once a tick.
- [x] **Step 8: Error handling.** An executor or storage with no placement is drawn in an `UNPLACED` strip along the canvas bottom plus one `GD.PushWarning` — never silently hidden. Two nodes in one cell: both drawn, the second offset half a cell, plus a warning. A selection naming a node absent from the snapshot clears the highlight.

**Verification:** `dotnet build` clean at zero warnings. **No visual claim.**

---

### Task 6: Replace Overview, remove the placeholder

**Files:**
- Modify: `dimenship/scripts/ui/ShellRoot.cs`
- Delete: `dimenship/scripts/ui/panels/OverviewFocus.cs`

- [x] **Step 1:** `RegisterPanels()` maps `OverviewId` to `new BaseGraphFocus()`, descriptor retitled `"Base Graph"`. Delete the `BaseGraphId` field and its `Placeholder(...)` registration.
- [x] **Step 2:** Delete `OverviewFocus.cs`. Its `ResourceTile` moved to `focus/ResourceStrip.cs` in Task 5, Step 6.
- [x] **Step 3:** `DefaultLayout` is unchanged — `ActiveFocus` is already `OverviewId`.
- [x] **Step 4:** Sweep the repository and `docs/` for surviving `OverviewFocus` and `BaseGraphId` references. `LayoutSerializerTests`' `base_graph` ids are test-local fixture data and need no change; confirm by running.
- [x] **Step 5:** Hand the visual checks to the project owner.

**Verification:** `dotnet build DimenshipGame.sln` clean at zero warnings, `dotnet test` green.

Manual, the owner's step, in the editor — **not verifiable here and not to be claimed**:

- The graph draws the default vessel: hold, buffer, extractor, smelter, two edges, a power node.
- Edge colour changes as the smelter cycles between running and postponed.
- Clicking a node opens the Facility Inspector showing that node; clicking an edge shows the transport line.
- Pan, wheel zoom, and `F` behave.
- After a restart the Inspector zone still holds `facility_inspector`; the graph opens at 100%, unpanned and unselected, because none of that is persisted.
