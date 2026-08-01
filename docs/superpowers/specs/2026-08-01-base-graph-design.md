# Base Graph — Design

Date: 2026-08-01
Status: Draft

## Goal

Replace the `base_graph` placeholder with the focus view the game is read from: an authored node graph of the vessel's facilities and storages, wired by real transport routes, coloured by live state, with a selected node's detail rendered in the Inspector zone.

Three things ship together because none of them is useful alone:

1. **Routes become real topology.** A transport executor gains a `From`/`To` storage pair. Today any transport line can serve any transfer, which means there is no topology to draw and no constraint to plan against.
2. **The graph view itself** — authored placements, orthogonal edges, node cards, pan and zoom, selection.
3. **A Facility Inspector panel** in the Inspector zone, driven by the graph's selection.

The view is strictly read-only. It mutates nothing.

## Source material

Two interface concepts supplied by the project owner: a base overview graph, and a Scratch-like program editor. **This spec covers the graph only.** The program editor is an independent subsystem and gets its own spec; nothing here should be shaped to anticipate it beyond what the existing panel contract already allows.

The concepts are direction, not a pixel target. Where a concept element implies a system that does not exist — mission docks, network alerts, crew AI, quick actions, build state — it is named in Out of scope rather than faked.

## Relationship to prior specs

`docs/superpowers/specs/2026-07-28-ui-shell-design.md` and `2026-07-30-production-planning-design.md` both stand. The zone model, the panel contract, the snapshot-and-poll binding, the reference direction between assemblies, and the integer-only rule in `Dimenship.Core` are all binding here.

One item the production spec left open is closed: the transport line gains endpoints. One item it deliberately deferred — queue and task *views* — is partially served, since a selected executor's queue is exactly what the Facility Inspector shows.

## Current state

- `SimulationEngine`, executors, tasks, storages and transport all exist and are tested. `WorldSnapshot` already carries `Storages`, `Executors`, `Transports`, `Sinks` and both task lists.
- `TransportExecutorDefinition` is `(Id, Label, ThroughputPerTick, StandingPowerDraw)`. It has no endpoints; a transfer names its own `From`/`To` and any line will carry it.
- `ShellRoot` registers `base_graph` as a `PlaceholderPanel` in the `Focus` zone.
- `ShellActions` carries focus, transport and zone commands. `ShellContext` exposes only `Actions`.
- The default world is a four-executor vessel: extractor, smelter, feed line, return line, one hold, one buffer.

## Decisions

| Question | Decision |
| :--- | :--- |
| Edge meaning | An authored transport route. Colour is that route's live load band. |
| Route direction | Directional. A two-way link is two definitions; the view merges an opposing pair into one double-headed edge. |
| Routing depth | Direct routes only. No multi-hop pathfinding. |
| Node placement | Authored grid cells in `Dimenship.Core`, integers only. |
| Build / reveal | Out. Every node in the world definition is drawn from the first frame. |
| Selection detail | A new Facility Inspector panel in the Inspector zone. Not a column inside the focus view. |
| Graph commands | None. Read-only. |
| Power | One pinned, edgeless Power node. Energy is a global pool and drawing power edges would be a lie. |
| Rendering | A custom `Control` drawing edges in `_Draw`, with one child `Control` per node. |
| Persistence | None. Pan, zoom and selection are session-local. |

## Kernel changes

### Routes

```csharp
public sealed record TransportExecutorDefinition(
    ExecutorId Id,
    string Label,
    StorageId From,
    StorageId To,
    long ThroughputPerTick,
    long StandingPowerDraw);
```

Consequences, each of which is a test:

- `WorldDefinition` construction rejects a route whose `From` or `To` names an unknown storage, and rejects `From == To`.
- A transport executor accepts only tasks whose source and destination match its route. `SimulationEngine.Commit` throws on a mismatched `PlannedTransfer` rather than queueing a task no line can run; the same validation covers `InitialTransfer`.
- `ProductionPlanner` selects a transport by matching the route, not by taking the first line. No matching route yields a `PlanShortage` with the existing `ShortageKind.NoCompatibleExecutor`.
- The default world's feed line becomes `MainHold → SmelterBuffer` and its return line `SmelterBuffer → MainHold`, which is what they already do in practice.

### Placement

New file, `src/Dimenship.Core/Presentation/BaseGraphLayout.cs`:

```csharp
public sealed record NodePlacement(int Column, int Row);

public sealed record BaseGraphLayout(
    // Production executors only. Transport executors are edges and are never placed.
    IReadOnlyDictionary<ExecutorId, NodePlacement> Producers,
    IReadOnlyDictionary<StorageId, NodePlacement> Storages)
{
    public static BaseGraphLayout ForDefaultWorld();
}
```

Grid cells, not pixels: pixel geometry belongs to the view and changes with zoom, while "the smelter sits right of the hold" is content.

It lives in `Dimenship.Core` rather than `Dimenship.Shell` because `Shell` cannot name `ExecutorId` — it knows panels as identifiers and nothing else, and that reference direction is load-bearing. Putting placements beside the world definition makes *every executor and storage is placed, no two share a cell, and every route endpoint is placed* a `Core.Tests` assertion instead of a bug the user finds by looking at the screen. The cost is one folder of presentation data inside the simulation assembly. Accepted, and it carries no Godot type.

Transport executors get no placement. They are edges.

### Snapshot additions

```csharp
public sealed record TransportExecutorState(
    ExecutorId Id,
    string Label,
    StorageId From,                 // new
    StorageId To,                   // new
    ExecutorStatus Status,
    TaskId? CurrentTask,
    ItemId? CarriedItem,            // new — the active task's item, for the inspector
    long ThroughputPerTick,         // new
    long MovedLastTick,             // new
    long PowerDraw,
    PostponeReason? BlockReason);

public sealed record StorageState(
    StorageId Id,
    string Label,
    long TotalAmount,               // new
    long TotalCapacity,             // new
    IReadOnlyList<ItemStock> Items);

public sealed record ExecutorState(
    /* … unchanged … */
    long RunTicksRemaining,
    long RunTicksTotal,             // new
    /* … unchanged … */);
```

`MovedLastTick` is what the edge's load band is computed from; without it the view can only distinguish running from not.

`TotalAmount` and `TotalCapacity` are summed over `Items` in item-definition order. They are derivable, and that is the point: two surfaces summing independently can disagree about whether an item with zero capacity counts, and a storage node and a storage panel disagreeing about a fill percentage is exactly the kind of small lie this UI cannot afford.

`RunTicksTotal` makes a run progress bar honest. `RunTicksRemaining` alone cannot express progress.

## Shell assembly additions

Engine-free, Godot-free, unit-tested — this is where the graph's arithmetic lives so that almost none of it needs the editor to verify.

```csharp
public enum GraphNodeKind { Executor, Transport, Storage, Power }

public readonly record struct GraphSelection(GraphNodeKind Kind, string Id);

public enum FlowBand { Idle, Low, Normal, High, Blocked }

public static class FlowBands
{
    // Blocked wins over every load reading. Zero or negative throughput is Idle, never a divide.
    public static FlowBand Classify(long movedLastTick, long throughputPerTick, bool blocked);
}
```

Bands by permille of throughput: `0` → `Idle`, `1..329` → `Low`, `330..799` → `Normal`, `≥ 800` → `High`. Integer arithmetic, `moved * 1000 / throughput`, floor.

```csharp
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

Edges are three orthogonal segments: leave the source from the side facing the target, elbow at the mid-gutter, arrive at the facing side of the target. Parallel edges between the same pair are offset by `parallelIndex * 6` pixels so they do not overprint. An opposing pair is drawn once with an arrowhead at each end.

`HitDistanceSquared` is what makes an edge clickable, which is how a transport line gets inspected — it has no node of its own.

## The focus view

`dimenship/scripts/ui/focus/` — new folder:

| File | Role |
| :--- | :--- |
| `BaseGraphFocus.cs` | `PanelBase` for `base_graph`. Owns the snapshot, selection, pan and zoom. |
| `GraphCanvas.cs` | `Control`. Draws edges and arrowheads in `_Draw`; hosts node cards as children. |
| `NodeCard.cs` | Shared card chrome: border, title, status line, selection highlight, hit area. |
| `ExecutorCard.cs`, `StorageCard.cs`, `PowerCard.cs` | Per-kind bodies. |
| `GraphLegend.cs` | The five flow bands, pinned bottom-left. |

Card content, all of it already on the snapshot:

- **Executor** — label, `FacilityType`, status text (`RUNNING` / `SWITCHING` / `IDLE` / `BLOCKED`), configured schematic, queued task count, and a run progress bar from `RunTicksRemaining` against `RunTicksTotal`. Blocked cards show the `PostponeReason` code.
- **Storage** — label, a fill bar from `TotalAmount` against `TotalCapacity`, and up to three item rows in item order.
- **Power** — capacity, draw, reserve, cap hits, starved ticks. Pinned to a fixed cell above the graph, no edges.

Colour never carries meaning alone: every state colour sits beside its text code, as the shell spec requires.

`ShellPalette` gains five flow tokens — `FlowIdle` (= `TextDim`), `FlowLow`, `FlowNormal` (= `StateOk`), `FlowHigh` (= `StateWarn`), `FlowBlocked` (= `StateFault`). They alias existing colours today, but the rule is that nothing outside the palette names a colour, and an edge asking for `StateWarn` when it means "high load" is how that rule erodes.

The glow rule holds: an edge is a live measured value, so it may glow; the grid, borders and labels may not.

### Interaction

| Input | Effect |
| :--- | :--- |
| Click a card or an edge | Select it. |
| Click empty canvas | Clear selection. |
| Drag empty canvas | Pan. |
| Wheel | Zoom through `50 / 75 / 100 / 150 / 200`%, anchored at the cursor. |
| `F` | Fit all content to the viewport, snapped to the nearest zoom step. |
| `Tab` / `Enter` | Cards are focusable `Control`s, so the shell's existing traversal reaches them and `Enter` selects. No graph-specific key. |

Edges are drawn, not `Control`s, so they are reachable by pointer only. Keyboard access to a transport line is through its endpoints' cards until a reason to do better appears.

Zoom is a `Scale` on the canvas container. Fixed steps rather than continuous zoom keep text on whole-pixel sizes.

Selection publishes through `ShellActions`:

```csharp
public Action<GraphSelection?>? SelectionChanged;
public Action? InspectRequested;
```

`ShellRoot` handles `InspectRequested` by swapping the Inspector zone to `facility_inspector` and expanding the zone if it is collapsed. **Unconditionally, on every selection.** The player clicked a node to see its detail; a rule that only sometimes shows it would be worse than one that always does. This is a reversible decision, and it is the reason selection routes through `ShellActions` instead of the panel reaching for the zone directly.

`ShellContext` gains `CurrentSelection` so the inspector panel can render correctly when it is mounted after the selection was made, rather than showing an empty state until the player clicks again.

### Redraw

`GraphCanvas.QueueRedraw()` on snapshot change, on pan, on zoom and on selection change. Nothing animates, so there is no per-frame redraw. Flowing dashes along active edges are attractive and are an open item, not v1 — they would force a redraw every frame for a graph that changes once a tick.

## Facility Inspector panel

`PanelId` `facility_inspector`, `ZoneKind.Panel`, in `dimenship/scripts/ui/panels/FacilityInspectorPanel.cs`.

| Selection | Content |
| :--- | :--- |
| Executor | Status, configured schematic, current run progress and energy charged, queued production tasks with state and last postpone reason, local storage contents. |
| Transport | Route endpoints, carried item, moved against throughput, status, queued transfers with requested and moved quantities. |
| Storage | Every item row: amount, capacity, fill. |
| Power | Capacity, draw, reserve, cap hits, starved ticks — deliberately a subset of the Energy Budget panel, which keeps the per-consumer breakdown. |
| None | `NO SELECTION`. |

It reads `ShellContext.CurrentSelection` and resolves the identifier against the snapshot on every `OnSnapshot`. It holds no reference to the graph view.

## Error handling

| Failure | Behaviour |
| :--- | :--- |
| An executor or storage has no placement | Drawn in an `UNPLACED` strip along the canvas bottom, plus one console warning. Never silently hidden. |
| Two nodes share a cell | Both drawn, the second offset by half a cell, plus a console warning. |
| A route names an unknown storage, or `From == To` | `WorldDefinition` construction throws. Content error, caught by a test, never reaches the view. |
| A committed transfer has no matching route | `Commit` throws. |
| Selection names a node absent from the current snapshot | Inspector renders `NO LONGER PRESENT`; the graph clears the highlight. |
| Zero or negative throughput on a route | `FlowBand.Idle`. No division. |
| Zero `TotalCapacity` on a storage | Fill bar renders empty at 0%. No division. |
| Zero `RunTicksTotal` | Progress bar renders empty. No division. |

## Build order

Tests precede the code they cover.

1. `Dimenship.Shell` — `GraphSelection`, `FlowBands`, `GraphGeometry`, and their tests. Pure integer arithmetic, no engine, no Godot.
2. `Dimenship.Core` — route endpoints on `TransportExecutorDefinition`, world validation, engine task matching, `MovedLastTick`, snapshot fields, planner route selection, `BaseGraphLayout`, and their tests. The default world and its layout are updated together.
3. `FacilityInspectorPanel`, registered in the Inspector zone.
4. `ShellActions` selection surface, `ShellContext.CurrentSelection`, `ShellRoot` swap-on-inspect.
5. `GraphCanvas`, `NodeCard` and the three card kinds, `GraphLegend`.
6. `BaseGraphFocus` replacing the placeholder registration.
7. `ShellPalette` flow tokens.

## Verification

Automated, runnable here — `dotnet build DimenshipGame.sln` clean, `dotnet test` green.

`Dimenship.Shell.Tests`: band boundaries at 0, 1, 329/330, 799/800 and 1000 permille; blocked overrides a high reading; zero throughput is `Idle`; `CellRect` for column and row zero and beyond; polylines for same-row, same-column and diagonal pairs; parallel offsets do not coincide; `ContentSize` bounds an arbitrary cell set; `HitDistanceSquared` is zero on a vertex and grows off the line.

`Dimenship.Core.Tests`: a route with an unknown endpoint throws; `From == To` throws; a transfer whose endpoints do not match its line's route is rejected on commit; the planner picks the line whose route matches and reports `NoCompatibleExecutor` when none does; `MovedLastTick` equals the quantity delivered that tick and returns to zero on an idle tick; storage totals equal the sum over items in item order; `RunTicksTotal` is fixed at run start and unchanged by a postponement; every production executor and every storage in the default world has a placement; no two placements share a cell; every route endpoint is a placed storage.

Manual, the user's step, in the editor:

- The graph draws the default vessel: hold, buffer, extractor, smelter, two edges, a power node.
- Edge colour changes as the smelter cycles between running and postponed.
- Clicking a node opens the Facility Inspector showing that node; clicking an edge shows the transport line.
- Pan, wheel zoom, and `F` behave.
- After a restart the Inspector zone still holds `facility_inspector`; the graph opens at 100%, unpanned and unselected, because none of that is persisted.

The Godot editor is not on `PATH` here. No visual item is reported as verified before the user confirms it.

## Out of scope

Construction, build cost and the piece-by-piece reveal. Any command issued from the graph — pause, priority, queue edit. Multi-hop transport routing. Mission docks, expeditions, crew AI, network alerts, quick-action rails, strata and mission status — none of these systems exist. Edge flow animation. A minimap. Node dragging or player-authored layout. The program editor. The mobile profile.

## Open items

1. **Reveal.** Deferred by decision. When construction ships, a node gains a commissioned state and the graph renders uncommissioned nodes as dim slots; `BaseGraphLayout` is already the place a slot's position would be authored.
2. **Multi-hop routing.** With direct routes only, a planner facing hold → A → B reports a shortage rather than chaining two transfers. Fine for a small vessel, wrong for a large one.
3. **Per-route item filters.** A route carries anything. Declaring what a line may carry would let the planner reject impossible transfers earlier and let edges be labelled by material.
4. **Edge animation.** Directional dashes would read load at a glance, at the cost of a per-frame redraw.
5. **Scale.** Hand-authored placements and per-node `Control`s are right for a vessel of a dozen nodes. Beyond roughly fifty, culling by viewport and a minimap start to matter.
6. **Layout authoring.** Placements are written by hand in C#. If the base grows, a data file and a viewer become worth more than they cost.
