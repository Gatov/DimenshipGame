# UI Shell — Design

Date: 2026-07-28
Status: Approved

## Goal

Build the shell the rest of Dimenship lives inside: a desktop development-environment / SCADA hybrid with fixed zones, swappable information panels, a focus selector, and a status bar carrying live vessel telemetry.

The slice ships the frame plus three genuinely working panels driven by a minimal but real simulation kernel. The three large focus views named in the design conversation — base graph, doctrine editor, bot construction — ship as registered placeholders. They are each large enough to deserve their own spec, and the point of this slice is to fix the contract they will mount into before any of them is built.

## Relationship to the GDD

The GDD (`Dimenship_GDD.md`, v0.5) describes the whole game. It decomposes into roughly ten independent subsystems; this spec covers one of them, the UI shell, plus the thinnest viable seed of the simulation kernel it needs in order to display anything true.

Two GDD statements are superseded by decisions recorded here:

- **§3 / document header, "Android-first."** This project is desktop-first. A dense multi-panel information display is native to pointer input and a large viewport, and degrades to a stacked mobile profile far more gracefully than a mobile IA scales up. Mobile becomes a later layout profile over the same panel system, not a parallel UI.
- **§9, mobile IA with 4-5 primary tabs.** Replaced by the zone model below. The rail plays the role the tab bar would have played, and becomes a bottom tab bar in the eventual mobile profile.

One GDD statement is placed at risk and is recorded as an open item rather than resolved: **§2, "failures cost real time."** Player-facing transport controls include a speed multiplier, which lets the player skip the waiting that pillar depends on. See Open Items.

## Current state

- `DimenshipGame.sln` holds three projects: `dimenship/Dimenship.csproj` (Godot 4.7.1-mono, `net8.0`), `src/Dimenship.Core`, `tests/Dimenship.Core.Tests` (NUnit).
- `Dimenship.Core` contains only `GameInfo`. It references nothing, and specifically never `GodotSharp` — the invariant that keeps `dotnet test` runnable without the engine.
- `dimenship/scenes/StartScreen.tscn` is the main scene. Its Play button loads `res://scenes/Game.tscn`, a placeholder `Node2D` with a label.
- The Godot editor is not on `PATH` in the development environment. Anything requiring the engine to run is verified by the user, not by the agent.

## Decisions

| Question | Decision |
| :--- | :--- |
| Platform | Desktop-first. Mobile is a later layout profile over the same panels. |
| Layout model | Fixed zones with swappable panels. No inter-zone dragging, no floating windows, no tabs. |
| Slice scope | Shell frame + three real panels + minimal real simulation kernel. Focus views are placeholders. |
| Data source | Real kernel in `Dimenship.Core`, engine-free and unit-tested. Nothing is throwaway. |
| Input | Mouse-first with basic keyboard traversal and a small accelerator set. No command palette yet. |
| Visual direction | SCADA industrial: near-black ground, hairline borders, hard edges, amber/cyan/red state colors. |
| Time control | Full player transport — run, pause, step, ×1 / ×5 / ×30. |
| UI ↔ sim binding | Immutable snapshot, panels poll. |
| Verification | Core and Shell unit tests here; everything visual confirmed by the user in the editor. |

## Repository layout

```
src/Dimenship.Core/                 existing, grows
  GameInfo.cs
  Simulation/
    SimulationEngine.cs
    WorldDefinition.cs
    WorldSnapshot.cs
    SimEvent.cs
    Ids.cs
src/Dimenship.Shell/                NEW
  ZoneKind.cs
  PanelId.cs
  PanelDescriptor.cs
  LayoutState.cs
  LayoutSerializer.cs
tests/Dimenship.Core.Tests/         existing, grows
tests/Dimenship.Shell.Tests/        NEW
dimenship/
  scenes/Shell.tscn              the only hand-authored scene
  scripts/ui/*.cs                frame, zones, rail, driver, theme
  scripts/ui/panels/*.cs         panels and focus views
```

Reference direction, extending the rule set from the start-screen spec:

- `Dimenship.csproj` → `Dimenship.Core.csproj`, `Dimenship.Shell.csproj`
- `Dimenship.Core.Tests.csproj` → `Dimenship.Core.csproj`
- `Dimenship.Shell.Tests.csproj` → `Dimenship.Shell.csproj`
- `Dimenship.Core.csproj` → nothing
- `Dimenship.Shell.csproj` → nothing

`Shell` and `Core` do not reference each other. That is deliberate and is the reason `Shell` is a separate assembly rather than a namespace: it makes it structurally impossible for simulation types to leak into layout types or the reverse. `Shell` knows panels only as identifiers and descriptors; the mapping from identifier to a constructed panel lives on the Godot side, so no engine type ever enters a testable assembly.

Both new projects target `net8.0`, matching the existing three, and inherit `Nullable` and `LangVersion` from the root `Directory.Build.props`. `Dimenship.Shell.Tests` mirrors the existing test project: `IsPackable=false`, NUnit 4.x, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`.

## Simulation kernel

`SimulationEngine` is pure and time-source-free.

```csharp
public sealed class SimulationEngine
{
    public SimulationEngine(WorldDefinition definition);
    public WorldSnapshot Snapshot { get; }
    public void Advance(long ticks);
}
```

It never reads `DateTime.Now`, never constructs a `Random`, and holds no reference to wall-clock time. All time enters through `Advance`. Pause, speed multiplier, and any future offline catch-up live in the Godot driver, which keeps transport entirely outside the deterministic core.

One tick is one simulated second.

**Quantities are `long` milli-units, never floating point.** Resource amounts, rates, and energy (in milliwatts) are all integers. Determinism must not rest on floating-point reproducibility across platforms and runtimes. Floats appear nowhere in the kernel; layout split positions are integers too.

### Snapshot

```csharp
public sealed record WorldSnapshot(
    long Tick,
    IReadOnlyList<ResourceStock> Resources,
    EnergyState Energy,
    IReadOnlyList<FacilityState> Facilities,
    IReadOnlyList<SimEvent> RecentEvents,
    long TotalEventsEmitted);

public sealed record ResourceStock(ResourceId Id, long Amount, long Capacity, long NetRatePerTick);
public sealed record EnergyState(long Capacity, long Draw, long Reserve, int CapHits);
public sealed record FacilityState(FacilityId Id, FacilityKind Kind, FacilityStatus Status, long PowerDraw, EventCode? BlockReason);
```

The snapshot is immutable and replaced wholesale each time it changes. Panels read it; nothing mutates it. This is what makes panel behavior testable in principle and kernel behavior testable in practice — a snapshot is a value you can assert against.

### Events

```csharp
public sealed record SimEvent(
    long Tick,
    EventCategory Category,
    EventCode Code,
    string Subject,
    IReadOnlyDictionary<string, long> Data);
```

Events are structured, not prose. GDD §8 calls telemetry "machine-readable log events explaining what happened and why, core feedback loop, not flavor text" — that requirement is what makes the console's category filter possible at all, and what will later make bottleneck detection possible without parsing strings. The console panel owns formatting.

`EventCategory`: `Production`, `Power`, `Fault`.

`EventCode`: `Run`, `BlockMissingInput`, `BlockPowerCap`, `PowerCapReached`, `StockFull`.

`BlockPowerCap` and `PowerCapReached` are not duplicates and must not be merged. The first is per-facility — this facility could not run this tick because granting its draw would exceed capacity. The second is vessel-wide — total draw reached capacity, whether or not anything was blocked as a result. The Energy Budget panel counts the second; the facility list explains itself with the first.

`RecentEvents` is bounded at 512. `TotalEventsEmitted` counts every event ever emitted, including evicted ones, so the console can detect a gap and say so rather than silently skipping. Silent loss in a telemetry surface is worse than no telemetry.

### v1 world content

The thinnest content that makes every panel show real, changing, occasionally-failing numbers:

| Piece | Purpose |
| :--- | :--- |
| `Ore`, `Alloy` | raw → refined, the beginning of GDD §6.2's processing chain |
| Energy: capacity, draw, reserve | GDD §6.3's global constraint, made visible |
| `Extractor_01` | draws power, produces ore |
| `Smelter_A` | consumes ore, draws power, produces alloy |
| Stabilization field | constant baseline power draw (GDD glossary) |

Between them these produce `Run`, `BlockMissingInput`, and `PowerCapReached` events under ordinary operation, so the console shows genuine telemetry from the first run rather than placeholder text.

## Shell frame

Six regions, three of which are zones:

| Region | Behavior |
| :--- | :--- |
| Menubar | `Vessel` (return to start screen, quit), `View` (zone toggles, reset layout), `Debug`. `Debug` is present only when `OS.IsDebugBuild()` is true. |
| Left rail | Focus selector and zone toggles. Fixed; not a panel host. |
| Centre | Focus host. Exactly one focus view, chosen from the rail. |
| Inspector (right) | Panel host. One panel, chosen from its own header. |
| Console (bottom) | Panel host. Same contract as Inspector. |
| Status bar | Sim time, tick, stratum label, alert count, transport controls. |

The status bar's stratum label is a static string in v1. There are no strata until that subsystem exists, and a hardcoded label that looks live would be a small lie in a UI whose entire purpose is telling the truth about system state; it is rendered in `text.dim` and reads `STRATUM N-2 (fixed)`. The alert count is derived, not stored: it is the number of facilities whose `Status` is blocked in the current snapshot.

Splitters sit between Centre/Inspector and Centre/Console. Inspector and Console collapse. Panels cannot be dragged between zones, torn out, or stacked as tabs — that was the explicit scope decision, and it is what keeps the frame a few days of work rather than a UI framework project of its own.

### Panel contract

```csharp
public abstract partial class PanelBase : Control
{
    public abstract PanelId Id { get; }
    public abstract string Title { get; }
    public virtual void OnMount(ShellContext context) { }
    public abstract void OnSnapshot(WorldSnapshot snapshot);
    public virtual void OnUnmount() { }
}
```

`ShellContext` is the small surface a panel is allowed to touch: the `ShellActions` dispatch, and nothing else. It exists so a panel can raise an action without holding a reference to `ShellRoot` and reaching through it.

`ShellRoot` compares the engine's `Snapshot` reference each `_Process` frame and calls `OnSnapshot` on mounted panels only when it has changed. Snapshots are replaced wholesale rather than mutated, so reference equality is a sufficient and exact change test — no dirty flags, no per-field comparison.

A panel never reaches into the engine for state; the shell hands it the snapshot. Focus views use the identical contract — a focus view is simply a panel the rail mounts into Centre. That symmetry is the whole point of the slice: base graph, doctrine editor, and bot construction each become an independent spec that plugs in without touching the frame.

`PanelDescriptor` records which zone kind a panel may occupy — `ZoneKind` is `Focus` or `Panel` — so the layout loader can reject a saved layout that puts a focus view in the console. The Shell assembly needs no identifier for individual zones: `LayoutState` names them as explicit fields, which is simpler than a dictionary keyed by an enum and makes a malformed layout a compile error rather than a missing key.

`PanelRegistry` maps `PanelId` → `Func<PanelBase>` and lives in the Godot project. Factories rather than `PackedScene` because every panel is built in C#: a `.tscn` per panel would be a stub whose only content is a script reference, and each one would be another hand-authored scene that cannot be validated without the editor.

### What ships in the zones

Real and working:

- **Overview** (focus) — resource tiles with fill bars and net rates, plus a facility list showing live status and block reasons. Deliberately cheap: labels, bars, and rows. No graph code.
- **Energy Budget** (inspector) — capacity, draw, per-consumer breakdown, reserve, cap-hit count.
- **Event Log** (console) — structured events, category filter, drop-gap markers.

Registered placeholders, each rendering a card naming what it will become: **Base Graph**, **Robotics**, **Doctrine**, **Processes**.

## Theme

One source of truth: `dimenship/scripts/ui/ShellPalette.cs`, holding every color, spacing step and type size, with `ShellTheme.Build()` assembling a Godot `Theme` from it. Nothing else in the shell may name a color literal.

A `.tres` `Theme` resource would be the engine-native form, but a `Theme` carrying `StyleBox` sub-resources has no schema that can be hand-authored reliably, and the editor is not available in this environment. One C# file satisfies the same one-place-to-change requirement.

| Token | Value | Use |
| :--- | :--- | :--- |
| `bg.base` | `#0A0D0F` | focus and console ground |
| `bg.panel` | `#12181C` | rail, inspector, status bar, bar troughs |
| `border` | `#1E2A31` | hairlines, 1px, no corner radius |
| `text.primary` | `#8FA3AD` | values, body text |
| `text.dim` | `#4A6270` | labels |
| `text.faint` | `#3D525C` | timestamps and other non-essential text only |
| `state.ok` | `#00E5C0` | running, healthy |
| `state.warn` | `#FFB000` | near-cap, deferred; also the accent color |
| `state.fault` | `#FF4D4D` | blocked, fault |

Type: one embedded OFL monospace at four sizes — 9 micro, 11 body, 13 heading, 22 numeric-large. Labels are uppercase with `0.14em` tracking; values never are. Spacing scale: 2 / 4 / 8 / 12 / 16.

**Glow rule.** Glow is permitted only on fills representing a live measured value — bars and state indicators. Never on chrome, borders, or text. Without a rule this specific, the direction degrades into decoration; with it, glow carries information.

`text.faint` on `bg.base` is a low-contrast pair by design. It carries only timestamps and never information the player must act on. Nothing actionable is encoded in color alone: every state color is paired with a text code (`RUNNING`, `BLOCKED`, `MISSING_INPUT`).

## Input and transport

Mouse: click, splitter drag, hover states, wheel scroll in lists.

Keyboard: `Tab` / `Shift+Tab` traversal, `Enter` to activate, `Esc` to dismiss. Accelerators: `Ctrl+1`…`Ctrl+5` select focus views, `Space` toggles pause, `.` steps one tick, `[` and `]` change speed, `Ctrl+I` and ``Ctrl+` `` toggle the Inspector and Console zones.

There is no command palette. Every accelerator nonetheless routes through a single `ShellActions` dispatch rather than binding handlers directly at each call site, so adding a palette later is additive rather than a rewrite.

Transport lives entirely in the Godot driver. It holds a multiplier from `{0, 1, 5, 30}` where `0` means paused, accumulates real elapsed time into a tick accumulator, and calls `Advance(n)`. Step is `Advance(1)` while paused.

**Pause is session-local.** Closing the application accrues real elapsed time regardless of pause state. The rule is stated here so the eventual catch-up implementation has an answer; there is no exploit in either direction, since waiting is a cost rather than a reward.

## Persistence

**v1 persists layout only.** The simulation starts from a fixed initial `WorldDefinition` on every launch.

```csharp
public sealed record LayoutState(
    PanelId ActiveFocus,
    PanelId InspectorPanel,
    PanelId ConsolePanel,
    int InspectorSplitOffset,
    int ConsoleSplitOffset,
    bool InspectorCollapsed,
    bool ConsoleCollapsed);

public sealed record LayoutLoadResult(LayoutState State, IReadOnlyList<string> Warnings, bool UsedDefault);

public static class LayoutSerializer
{
    public static string ToJson(LayoutState state);

    public static LayoutLoadResult Load(
        string? json,
        IReadOnlyDictionary<PanelId, PanelDescriptor> known,
        LayoutState defaults);
}
```

Split positions are stored as integer `SplitContainer` pixel offsets rather than fractions of the viewport. Godot already clamps an offset against its children's minimum sizes, and reimplementing that against a resizing viewport buys nothing the player would notice.

Saved to `user://layout.json`. `Load` takes the known panel descriptors — descriptors rather than bare identifiers, because rejecting a focus view saved into the console zone requires knowing each panel's `ZoneKind` — and returns warnings alongside an always-valid result. Putting the fallback logic in the engine-free assembly rather than in the Godot layer is what makes every degraded-input case a unit test instead of a manual experiment.

World-state save/load and offline catch-up are deliberately excluded. Half-building them here would produce a seam in the wrong place; the driver is shaped so catch-up slots in later as "compute elapsed, `Advance`, done."

## Error handling

| Failure | Behavior |
| :--- | :--- |
| `PanelId` absent from the registry | the zone renders an inline fault panel naming the identifier; the shell continues running |
| `layout.json` absent | default layout, no warning |
| `layout.json` unparseable | default layout; the file is **renamed** to `layout.json.bad`, never deleted; warning to the console |
| Layout names an unknown panel | that zone falls back to its default; other zones are preserved |
| Layout puts a panel in a zone kind it does not allow | same per-zone fallback |
| Split offset outside `[-2000, 2000]` | clamped, with a warning |
| `Advance` throws | the driver catches, forces speed to `0`, and shows a fault banner carrying the tick number and exception type. The kernel is deterministic, so the failure is reproducible from that tick. |
| Event buffer overflow | gap marker rendered in the console; never silent |

## Build order

Tests precede the code they cover, as in the start-screen slice.

1. `Dimenship.Shell` and `Dimenship.Shell.Tests` — `LayoutState`, `LayoutSerializer`, and every degraded-input case. No engine involved, so this is pure TDD.
2. `Dimenship.Core.Simulation` and its tests — snapshot, events, engine, then the v1 world content.
3. Godot shell infrastructure — `PanelBase`, `Zone`, `PanelRegistry`, `ShellRoot`, `SimulationDriver`, `ShellActions`.
4. The three real panels and the four placeholders.
5. `ShellPalette` and `ShellTheme`.
6. Repoint `StartScreen`'s Play target from `res://scenes/Game.tscn` to `res://scenes/Shell.tscn`, and delete `Game.tscn`.

## Verification

Automated, runnable here:

- `dotnet build DimenshipGame.sln` — clean across all five projects.
- `dotnet test` — green.

`Dimenship.Core.Tests` covers: `Advance(10)` yields the same state as ten `Advance(1)` calls; identical initial definitions yield identical snapshot sequences; milli-unit arithmetic does not drift over long runs; the smelter emits `BlockMissingInput` below its input threshold; draw never exceeds capacity and `PowerCapReached` is emitted when it would; `TotalEventsEmitted` keeps incrementing after the buffer begins evicting.

`Dimenship.Shell.Tests` covers: `LayoutState` JSON round-trip; unparseable JSON returns defaults with `UsedDefault` set; an unknown panel identifier falls back for that zone only; a panel in a disallowed zone kind falls back; split offsets are clamped.

Manual, the user's step:

- The editor opens the project and imports the new scenes.
- Play mounts the shell; resource and energy values change over time.
- Panel swapping, splitter dragging, and zone collapse all work.
- Transport controls run, pause, step, and change speed.
- The layout survives a restart.

The Godot editor is not on `PATH` in the development environment. None of the manual items will be reported as verified before the user confirms them.

## Out of scope

Base graph, doctrine editor, and bot construction beyond registered placeholders. World-state save/load. Offline catch-up. The mobile layout profile. Command palette and rebindable keymaps. Missions, strata, robots, processes and the scheduler, quests, companions. Charts and derived reports. Audio. Export presets. CI.

## Open items

1. **Speed multiplier versus GDD §2.** Player-facing ×5 and ×30 remove real-time waiting as a cost. Pacing for energy regeneration, repair, and mission duration must be designed knowing this, or the multiplier must be constrained later. Recorded, not solved here.
2. **GDD revision.** The Android-first header, §3, and §9 need rewriting to match the desktop-first decision.
3. **Offline catch-up clamp.** How much elapsed real time is simulated on resume, and what happens beyond it, is decided with the persistence subsystem.
4. **Monospace font.** The shell ships using Godot's built-in font. Vendoring a font is a download decision for the project owner, not the implementer, so it is not part of this slice. JetBrains Mono (OFL 1.1) is the default candidate; the license must be confirmed and the file committed with its license text. `ShellPalette` is the single place a font would be introduced.
