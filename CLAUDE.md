# CLAUDE.md

Guidance for AI assistants working in this repository.

## What this is

**Dimenship** — a deterministic, operational-time strategy game built in **Godot 4.7.1 with C#**.
The player supervises a dimensional vessel through a SCADA-style schematic: production facilities,
transport lines, storage, energy, and (eventually) autonomous missions, robots and a case board.

`docs/Game Design v0.9.md` is the authoritative design document. When two documents disagree on
vocabulary or behaviour, the GDD wins — that rule is stated in the specs and is followed by the code
(`FacilityType`'s members, item ids and facility labels all come from GDD §5.8 / §5.9).

## Layout

```
DimenshipGame.sln            All five projects
Directory.Build.props        Nullable enable, LangVersion latest — applies to every project
src/Dimenship.Core/          The simulation kernel. No Godot, no float.
src/Dimenship.Shell/         Engine-free shell types: panel ids, layout state, graph geometry.
dimenship/                   The Godot project (res:// root). References both src projects.
tests/Dimenship.Core.Tests/  NUnit tests for the kernel.
tests/Dimenship.Shell.Tests/ NUnit tests for the shell types.
docs/                        GDD, transcribed specs, design specs, plans, reviews.
.claude/skills/              Repo-local skills (currently svg-icon-maker).
```

### `src/Dimenship.Core` — the kernel

| Namespace | What lives there |
| :--- | :--- |
| `Content/` | The catalog and scenarios: `ContentCatalog`, `Scenario`, `Archetypes`, the JSON loader (`JsonContentSource`) and its file-system seam (`IContentFileSystem`). |
| `Simulation/` | `SimulationEngine`, `WorldSnapshot`, `Ids`, `Quantities`, `Units`, `SimEvent`. |
| `Production/` | `SchematicDefinition`, `SchematicCatalog`, `ProductionTask`, `TransportTask`. |
| `Planning/` | `ProductionPlanner` (pure) over `IWorldView`. |
| `State/` | `WorldState` and its ledgers, `VesselState`, `ScenarioSeeder`, and `State/Save/` (the save DTOs and `WorldSave`). |
| `Presentation/` | `BaseGraphLayout` / `BaseGraphNodes` — grid cells, not pixels. `ConstructionProgress` — one unbuilt slot's phase, projected from a snapshot and thrown away. |

### `src/Dimenship.Shell` — engine-free shell types

`PanelId`, `PanelDescriptor`, `ZoneKind`, `LayoutState`, `LayoutSerializer`, `GraphGeometry`,
`GraphSelection`, `FlowBands`. Pure logic that the Godot layer renders, so it can be tested without
booting an engine. It does not reference `Dimenship.Core`.

### `dimenship/` — the Godot project

```
scenes/StartScreen.tscn      Main scene (project.godot points here)
scenes/Shell.tscn            The game shell; script is scripts/ui/ShellRoot.cs
scripts/ShellContent.cs      Loads the catalog once per process out of res://content
scripts/GodotContentFileSystem.cs   The one place in the repo that must name a Godot type to read content
scripts/ui/                  Shell chrome: ShellRoot, Zone, Rail, StatusBar, ShellActions, panels
scripts/ui/focus/            Centre views — BaseGraphFocus, cards, GraphCanvas, IconSlot
scripts/ui/focus/programs/   The programming view (a concept mock — see below)
scripts/ui/focus/loadouts/   The loadout composer (a concept mock — see below)
content/                     The JSON content tree: manifest.json, catalog/, scenarios/
assets/icons/{facility,item,status,control}/   Flat SVG icons, tinted at runtime
```

## The rules that are enforced, not just agreed

`tests/Dimenship.Core.Tests/Content/CoreAssemblyTests.cs` asserts two invariants by reflection.
Breaking either fails the build's tests, and both fail quietly if left to discipline:

1. **`Dimenship.Core` references neither Godot nor `Dimenship.Shell`.** The kernel is replaceable-UI
   by construction. Anything that must touch Godot goes behind a seam — `IContentFileSystem` is the
   worked example, implemented by `GodotContentFileSystem` in the game and by
   `MemoryContentFileSystem` / `DirectoryContentFileSystem` in tests.
2. **No `float` or `double` anywhere in `Dimenship.Core`** — not in a field, property, parameter or
   return type. Two machines replaying the same tick must reach the same number, and floating point
   does not promise that.

Additional guards in the test suite: `NoStateType_HoldsAContentRecord` (state names content by id
only), `EveryValueTheSnapshotShows_ComesFromTheCatalogAndTheState`, and
`DeterminismSurvivesASave_WhichIsWhatCatchesAFieldLivingOnlyInTheEngine`.

## Numeric conventions

- **Integers only in the kernel.** Quantities are **milli-units** (`3600000` = 3,600 units), work is
  in milli-work, energy in the same milli-watt units as capacity and draw.
- **Ratios are permille**: `WorkRatePermille`, `CapacityPermille`, `IntegrityPermille`, all `1000` =
  100%. Divide by `1000` (or `StorageArchetype.FullHold`) at the point of use.
- **One tick is one simulated second.** Scale through `Units.TicksPerMinute` / `TicksPerHour`; never
  hand-roll a `60`.
- The content loader **rejects a fractional literal outright** — there is no float in the JSON format.

## The four-tier data model

Specified in `docs/superpowers/specs/2026-08-10-static-content-and-world-state-design.md`. Sorting a
new piece of data into the right tier is the first question to ask on any kernel change.

1. **Catalog** (`ContentCatalog`) — the rulebook. Loaded **once per process**, immutable, shared by
   every world opened during that run. Never copied into a save; a save references it by
   `ContentVersion` and by ids, and by nothing else.
2. **Scenario** (`Scenario`) — one campaign's authored starting position and node placements.
   **Retained, not discarded**: it holds every node slot the campaign will ever show, including ones
   nothing has been built in yet. Placement stays in content and never reaches a save, so editing a
   layout reaches campaigns already in progress.
3. **World State** (`WorldState`) — everything that changes during play. Mutable classes, not
   records: the engine mutates it hundreds of times per tick. **State stores ids and deltas, never
   definitions, and never a value the catalog can already answer** — a label copied onto an instance
   means a rename in content never reaches an existing save.
4. **Authored content** — what the player wrote (programs). Ids carry a `user:` prefix, which the
   catalog id pattern `^[a-z][a-z0-9_]*$` cannot represent, so the two id spaces are provably
   disjoint rather than disjoint by convention.

Name resolution goes through `WorldState.NameOf(catalog, instance)` and nowhere else, so no call
site can forget the archetype fallback.

## Determinism contract

- `SimulationEngine` holds **no wall-clock reference and constructs no random source**. All time
  enters through `Advance(long ticks)`; randomness comes from `WorldState.Random`'s per-domain
  streams (`RngDomain` is append-only, and a shorter saved array is extended rather than rejected).
- Pause, speed multiplier and catch-up live in `SimulationDriver` (the Godot layer), deliberately
  outside the reproducible path.
- **Declaration order is the determinism contract.** Content order, executor order, item order — a
  helper that reorders anything is hiding a bug. `WorldBuilder` in the tests preserves it for this
  reason.
- **A tick runs transport, then commissioning, then production, in that order.** Commissioning is
  the phase between them: for each unbuilt facility whose archetype names a `ConstructionUnit` and
  whose local storage already holds a whole unit (1000 milli-units of it), the phase withdraws
  exactly that much, sets `Built = true`, and emits `FacilityBuilt`. The placement is the
  determinism contract itself — a unit a transport delivers this tick commissions this tick, and
  the facility's first production run is next tick, never the same one. Local storage is the
  interim stand-in for an upgrade socket
  (`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`); nothing here
  builds a line, which stays authored `Built` and only that.
- **A transport line is a conveyor that holds cargo.** A line has an authored `LengthTicks` and a
  `Belt` of exactly that many slots, index `0` at the destination end. Its tick is **unload, then
  advance, then load**: unloading first is what keeps material reaching the head usable by
  production the same tick; advancing before loading is what makes a slot loaded now sit a full
  `LengthTicks` from being deliverable. One slot holds one item for one task, because a line
  services at most one task per tick — which is why **capacity is `ThroughputPerTick × LengthTicks`
  and is never authored**. A head that cannot be emptied **freezes the belt in place**: nothing
  advances, nothing is picked up, and the fill stays exactly where it was, however partial. The
  opposing direction of a two-way link is a separate `TransportInstance` with its own belt and
  `BlockReason`, and no code path lets one stop the other. **Blocked means cargo it cannot put
  down, and nothing else**: only a frozen belt sets `AllQueuedTasksBlocked` and a `BlockReason`, and
  the transport `Postpone` deliberately does not touch the line. Queued work that could not be
  picked up onto an empty belt is `ExecutorStatus.NothingToCarry` — transport-only, no reason, not a
  fault — because the shortage belongs to the storage upstream, which reports it already; the
  transfer still carries `InsufficientSourceMaterial` on its own row. A line still delivering with
  nothing left to pick up is `RunningTask`, because a belt draining after its source ran dry is
  working. Progress counts **deliveries**
  (`MovedQuantity`); pickup is counted separately (`LoadedQuantity`), and `Current` clears when a
  transfer is entirely aboard rather than when it completes, so the belt is never parked between two
  hauls. Conditions gate pickup only — cargo already travelling always arrives. This is the GDD's
  §5.10 promise (*a part in transit is never lost*) made real; before it, `TryMove` withdrew and
  deposited inside one tick and nothing was ever in transit. See
  `docs/superpowers/specs/2026-09-04-conveyor-belt-design.md`.
- **`Built` gates an executor everywhere.** An unbuilt facility or transport line draws no standing
  power, is stepped by nothing, is invisible to `ProductionPlanner`, and is refused by
  `SimulationEngine.Enqueue` — the two old task entry points and today's one collapsed into a single
  check. Its local storage still accepts a delivery, because holding back the room commissioning
  needs would be exactly what stops it from ever completing.
- `PostponeReason.ConditionNotMet` is **appended last**, deliberately: declaration order is
  root-cause priority (see `PostponeReasons.RootCause` in `Simulation/Ids.cs`), and a task script
  whose start condition is false must still report a missing input or a full destination over the
  condition, so a factory short of ore is never told it is gated rather than starved.
- Dictionaries built in the engine constructor are **indexes rebuilt from state, never saved**.
- The save contract is that `(catalog, state)` is sufficient: advance 500 ticks, save, load, advance
  500 more must equal advancing 1,000 in one go, byte-for-byte.

## Content pipeline

- Content is JSON under `dimenship/content/` (inside the Godot project, because `res://` is that
  directory and content outside it is content the export never sees).
- `manifest.json` lists every catalog file and every scenario. **A catalog file listed must exist**,
  and the required set is fixed in `JsonContentSource` rather than inferred from the manifest.
- The loader runs in two phases: **parse** (malformed JSON, unknown field, fractional number,
  unknown enum name) then **link** (resolve every id, check every invariant).
- **Errors are collected, not thrown on the first one.** An author fixing eleven dangling ids should
  see eleven messages, not eleven runs. The engine constructor still throws, because a constructor
  is meeting a programmer.
- A content load failure in the game is **fatal and says so** (`ShellContent`), never limped along.
- `catalog/programs.json` is required to be **empty** until the program language exists. Do not
  invent a schema for it.
- Each JSON file carries a `notes` field explaining its own conventions — read it before editing.
- The test project copies the shipped tree beside the test binary, so the fidelity test reads the
  same files the game will. Every other loader test builds its tree in memory.

## Save format

`src/Dimenship.Core/State/Save/` — `SaveFile.cs` holds one DTO per state type, mirroring the tree
rather than reusing it, and `WorldSave.cs` maps between them.

- `SaveEnvelope` carries `saveVersion`, `contentVersion`, `savedAtTick` around the world. Versions
  live on the file, not inside the world.
- A line's belt is written **sparsely, sorted by position** — the occupied slots only, never an
  array with a null per empty one. A saved position the belt no longer has is reported as content
  drift, not clamped or dropped: both would be a vessel silently changing how much material it owns
  across a load.
- Every DTO field is **nullable**, so a missing field is a reported error rather than a silent
  default. `[JsonUnmappedMemberHandling(Disallow)]` rejects unknown fields.
- **Sets are written sorted** and ordered collections as arrays, so two saves of one world are
  byte-identical and a diff between saves means something.
- A newer `saveVersion` is refused rather than half-read. Content drift (a save naming an id the
  catalog no longer has) is **reported, listing every reference**, never absorbed.
- Every load resumes paused. `TimeFlow` is not saved; `AutoPauseOnCriticalAlert` is, because it is a
  preference the player set.

## The Godot shell

- **`WorldSnapshot` is replaced wholesale, never mutated**, so `ShellRoot._Process` uses reference
  inequality as an exact change test — no dirty flags, no per-field comparison.
- **Panel ids are persisted in `user://layout.json`** and must not be renamed casually. Three carry
  historical names on purpose: the centre view is `"overview"` although it draws the base graph, the
  programming view is `"doctrine"` although it is titled Programs, and the Operations view is
  `"processes"` although its title moved on from Processes once it stopped being a placeholder.
  Renaming any of them would silently reset every player's layout to gain nothing.
- Focus views are ordered by **title** for the `Ctrl+1..9` accelerators, so retitling a view moves
  its shortcut.
- All commands route through `ShellActions`; accelerators and buttons never bind handlers directly.
  Current bindings: `Space` pause, `.` step, `[` / `]` speed, `Esc` release focus, `Ctrl+I`
  inspector, `` Ctrl+` `` console, `Ctrl+1..9` focus views. `ShellActions.Suspended` holds all of
  them while a modal surface is up — the flag lives beside the table so a modal never has to carry
  its own copy of which keys to swallow.
- **Settings are the second `user://` file**, `user://settings.json`, and follow the layout file's
  conventions exactly: `SettingsState` / `SettingsSerializer` in `Dimenship.Shell` (engine-free and
  unit-tested), `SettingsStore` in the Godot layer, degraded input producing warnings rather than an
  exception, and a bad file quarantined to `.bad` rather than deleted. Every DTO field there is
  nullable, unlike the layout's — a missing volume must be reported, not deserialize to silence.
  `Settings` is the only way to change one, and `SettingsApplier` the only place one reaches
  `DisplayServer`, `AudioServer` or the root viewport. See
  `docs/superpowers/specs/2026-08-20-settings-design.md`.
- `SettingsOverlay` is a `Control` over the running scene, never a `Window`: the frost shader
  samples `SCREEN_UV`, and a second viewport would sample nothing. It carries its own theme, because
  the start screen has none. Its Gameplay tab is a deliberate, labelled stub.
- **The audio buses are `Master`, `Music` and `SFX`; only Music has a source.**
  `default_bus_layout.tres` and `AudioBuses` give the Sound settings somewhere real to land.
  `MusicPlayer` is a Godot **autoload** — the only one in the project — looping
  `assets/audio/gravity_between_stars.mp3` into the Music bus for the life of the process, because
  the start screen and the shell are separate scenes and the track has to survive
  `ChangeSceneToFile`. Being the first node in the tree, it applies the settings before it plays,
  so the track never sounds for a frame at the engine's default volume. Disabling music **mutes
  the bus and leaves the track running**, which is why nothing in the audio path subscribes to
  `Settings.Changed`. Nothing plays into SFX yet, and the tab says so.
- **Nothing in the shell may hard-code a colour.** `ShellPalette` is the single source of truth for
  colours, spacing and type sizes; `ShellTheme` builds the Godot theme and supplies the box
  vocabulary (pane, box, card, chip, meter, divider) by name. A new control type is themed there,
  not at the call site — `HSlider` is the worked example, including the handle texture, which is
  generated from a palette colour because Godot draws it from a texture rather than a stylebox.
  `OptionButton` needs no entry: theme items resolve through the class chain, so it inherits
  `Button`'s.
- Icons go through `IconSlot`, which reserves its space whether or not a file is present and tints
  every glyph from the palette — an icon with a colour of its own would be a literal outside the
  palette. Domains: `facility`, `item`, `status`, `control` under `res://assets/icons`. They import
  at `svg/scale=2.0`, which is why button icons are capped at row size in the theme.
- An edge's colour band reads a line's **intake** (`LoadedLastTick`), not what it delivered:
  intake is what working at a fraction of throughput means for a conveyor, and it is the reading
  that is right on the first tick of a haul. Beside each arrowhead, in that direction's own band
  colour, `GraphCanvas` draws how full that direction's belt is — per side, with no merged figure,
  because the worse-of-two rule that suits the shared stroke would report a jam on the side that
  has none.
- An unbuilt facility draws dimmed on the base graph, through `ShellPalette.UnbuiltModulate` — an
  alpha-only modulate, not a second colour ramp, applied to the whole card (`NodeCard.SetBuilt`) or
  edge (`GraphCanvas.Edge.Built`) at once rather than to each reading inside it separately.
  `ExecutorCard.Refresh` reads `ExecutorState.Built` off every snapshot delivery, which is what
  re-chromes a card the tick commissioning sets it — there is no separate `FacilityBuilt` event
  handler, because the snapshot already carries the flag on every delivery there is one to read.
- `ProgramsFocus` and everything under `scripts/ui/focus/programs/` is an explicitly labelled
  **concept mock**: it authors programs, nothing executes them, nothing persists. Its mutable model
  lives in the Godot assembly on purpose so it cannot break the tested kernel. Do not build the real
  program runtime on top of it — when that system ships, the model moves to `Dimenship.Core` and
  becomes records.
- `LoadoutsFocus` and everything under `scripts/ui/focus/loadouts/` is the second labelled
  **concept mock**, on the same terms: it composes loadout templates, nothing builds them, nothing
  persists, and no robot, socket storage or refit task exists anywhere in the kernel. Its one live
  reading is the build cost, which compares against `WorldSnapshot.Resources` rather than inventing
  stock. Its vocabulary is fixed and deliberate — **loadout template**, **socket**, **part** — and
  in particular *part* is not *module*, because `module` is the shipped bulk commodity and the GDD
  glossary has that collision open. See
  `docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md`, including its *Not built*
  list, before building anything on it.
- `OperationsFocus` and everything under `scripts/ui/focus/operations/` is **not** a concept mock,
  unlike `ProgramsFocus` and `LoadoutsFocus` beside it: it is the first surface in `dimenship/` that
  calls into the kernel rather than only reading a snapshot. Its composer's live preview comes from
  `ShellContext.ComposePlan`, bound to `SimulationDriver.Plan`, and APPROVE commits through
  `ShellActions.PlanApproved` into `SimulationDriver.Commit`, which is `SimulationEngine.Commit`
  wrapped the way `Advance` already was — a caught fault sets `FaultMessage` and refuses further
  calls, the same as every other entry point. The composed plan is discarded the moment it is
  approved; nothing about it is held past that call, and what happened is read back off the next
  snapshot's `Plans` and `Tasks` lists like everything else in the shell. Build mode enumerates every
  scenario facility whose archetype names a construction unit, filtered per snapshot to whichever
  the snapshot still reports unbuilt — Reactor Beta, Factory Beta, Factory Gamma and both Launch
  Pads on a new campaign, since the opening build is only the Extractor, Reactor Alpha and Factory
  Alpha. The Alphas name a unit as well (it belongs to the archetype, not the slot) and drop out
  because they open built. Pressing APPROVE clears the draft and disables the button until
  the composer changes, which is what stops a second press from committing the same plan again.
  `Processes` was the last `PlaceholderPanel`; it is not one now.
  See `docs/superpowers/specs/2026-09-03-launch-pad-design.md` Decision 8.
- **An unbuilt slot reads as unbuilt without a second colour.** `UnbuiltModulate` above is still the
  whole of the colour story — one alpha silhouette, no parallel ramp — and the two signals added
  beside it are deliberately not colours. `ExecutorCard` spends an unbuilt card's schematic line on
  the construction phase instead (`UNBUILT`, `QUEUED`, `PRODUCING`, `IN TRANSIT`,
  `BLOCKED — {root cause}`, `COMMISSIONING`), read off `Presentation/ConstructionProgress.For` rather
  than re-derived, so the card, the inspector and the Operations detail cannot name one condition
  three ways. `ShellTheme.DrawDashedPolyline` — mark and space as the separate `ShellPalette`
  constants `DashLength` and `DashGap` — outlines an unbuilt card (`NodeCard._Draw`, over a frame
  whose stylebox gives its border up) and strokes an unbuilt route (`GraphCanvas`), both, because
  `factory_link_ab` and `factory_link_bc` ship unbuilt and a vocabulary covering cards but not the
  lines between them would say two different things about one state. Godot's own `DrawDashedLine` is
  **not** used and should not be swapped back in: it spends one length on the mark and the gap alike,
  and it restarts its pattern per call, which over the eight short segments of a `GraphCanvas` elbow
  arc draws a rounded corner solid. Selection stays a border colour and nothing else, so a selected
  unbuilt card is a dashed outline in the accent — the signals compose rather than contend for one
  channel. Anything that reads `_built`, `_selected` or `_focused` must `QueueRedraw()`, which is why
  `NodeCard.ApplyChrome` does it once for all three.
- **`ShellActions.OperationsRequested` is the inspector's construction button**, carrying a
  `PendingOperationsTarget` that `ShellContext` parks for `OperationsFocus.OnMount` to consume — the
  same reason `CurrentSelection` is parked there, since the panel that will read it does not exist
  when the button is pressed. It is a separate command from `FocusRequested`, not a widening of it:
  one says "show me that view" and the other "show me that view opened on this slot", and merging
  them would put a payload on every focus switch that only one of them ever has. The base graph's
  camera moved to `ShellContext.GraphZoom` / `GraphPan` for the round trip that button starts, since
  `Zone.Show` frees the panel on every view change — and `BaseGraphFocus.OnMount` restores the pair
  **and then calls `ApplyTransform()` again**, which is easy to leave out and is the only thing that
  writes the canvas's `Position` and `Scale`; `_Ready` has already run by then, with the resting
  camera. Session-lifetime state, deliberately not in `user://layout.json`. Alongside it,
  `FacilityArchetype.purpose` is a required one-sentence player-facing string in the catalog, read by
  both the inspector's PURPOSE row and the construction preview's capability line — content, never
  derived from `FacilityType`, so a fifth facility kind is a content edit and not a code change. See
  `docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md`.

## Building and testing

```bash
dotnet build DimenshipGame.sln
dotnet test tests/Dimenship.Core.Tests
dotnet test tests/Dimenship.Shell.Tests
dotnet test DimenshipGame.sln          # both suites
```

Notes:

- `dimenship/Dimenship.csproj` uses `Godot.NET.Sdk/4.7.1` and needs that SDK on the NuGet feed; the
  three non-Godot projects build with a plain .NET 8 SDK. **If only the SDK for plain projects is
  available, build and test the `src/` and `tests/` projects directly rather than the whole
  solution** — the kernel and shell suites are where the behaviour lives.
- There is **no `dotnet` toolchain preinstalled in the cloud session container**. Verify changes by
  reading carefully and by running the suites wherever a toolchain exists; do not claim tests passed
  if you could not run them.
- CI is `.github/workflows/ci.yml` and is **on-demand only** — `workflow_dispatch`, run from the
  Actions tab against a chosen ref. It restores, builds and tests `src/` and `tests/` on .NET 8,
  uploads the `.trx` results and writes a per-suite table to the run summary, then builds the Godot
  assembly as its own step (`Godot.NET.Sdk/4.7.1` resolves from nuget.org, so a stock runner is
  enough; the step is separate, and behind an input, so an SDK-availability failure is never
  mistaken for a failing suite). Nothing runs on push or on a pull request; a run happens because
  someone asked for one.
- **NUnit is held at 4.5.1 in both test projects, on purpose.** 4.6.0 added an
  `Assert.Throws<T>(Action)` overload beside the existing `TestDelegate` one; both are void-returning
  and take no arguments, so every lambda call site becomes `CS0121` ambiguous and the kernel suite
  stops compiling entirely. The pin carries that reason in a comment beside it. Raise it only once
  NUnit withdraws the overload.
- Every project targets `net8.0`. The GDD says .NET 10; the repository does not, and the specs
  record that discrepancy deliberately. Do not "fix" the target framework without being asked.
- Godot itself is only needed to run the game (`dimenship/project.godot`, main scene
  `res://scenes/StartScreen.tscn`).

## Code conventions

- **Nullable reference types are on everywhere**; `ImplicitUsings` is on in every project.
- Ids are `readonly record struct` wrappers over a `string` or `long`, one per concept, each with a
  `ToString()`. Do not pass bare strings where an id type exists — the whole point is that
  "Factory Alpha" (an `ExecutorId`) and "standard factory" (a `FacilityArchetypeId`) cannot be
  confused.
- Immutable data is `sealed record`; live mutable state is `sealed class` with `required` members.
- **XML doc comments explain *why*, and name the failure they prevent.** This is the house style and
  it is unusually consistent — see `ContentCatalog`, `WorldState`, `UtilizationWindow`,
  `EnergyState`. New public types are expected to carry the same kind of comment: what the thing is,
  what alternative was rejected, and what goes wrong if the rule is broken. Match the register of
  the surrounding file; do not add thin comments that restate the signature.
- Where something is deliberately absent (shipped programs, the robot domain, mission systems), say
  so in the doc comment rather than leaving a gap.

## Tests

NUnit 4 (`Microsoft.NET.Test.Sdk` 18.8.1, `NUnit3TestAdapter` 6.2.0), mirroring the source folders.

- Test names read as sentences with underscores between clauses:
  `RunLasts_CeilingOfEffortOverWorkRate`, `EveryStorage_IsEitherPlaced_OrOneFacilitysBuffer`,
  `PartialExecution_ProducesWhatItCan_Postpones_ThenResumes`.
- `WorldBuilder` builds a catalog and scenario for kernel tests without the JSON loader — loader
  rules are tested against the loader.
- `Shipped` loads the real content tree once for tests that want the actual vessel.
- `ContentTree` / `MemoryContentFileSystem` build a minimal valid tree; a loader test copies it and
  **breaks exactly one thing**, so the failure is about the rule under test.

## Documentation workflow

Design work lands in `docs/` before the code does:

- `docs/Game Design v0.9.md` — the foundation GDD, and the tiebreaker on vocabulary.
- `docs/specs/` — transcriptions of the project owner's handwritten pages (schematics, planning and
  task execution). These are the cited copies; duplicates at the `docs/` root are superseded.
- `docs/Dimenship Programming v0.1.md` — programs as first-class objects.
- `docs/Dimenship Mission Visualization Proposal.md` — the Mission Monitor: a docked, watch-only
  left-to-right rendering of a mission the simulation is already resolving. Nothing of it is built.
- `docs/superpowers/specs/` — dated design specs (`YYYY-MM-DD-topic-design.md`), each with a Goal,
  Source material, and the decisions with their reasoning.
- `docs/superpowers/plans/` — dated implementation plans (`YYYY-MM-DD-topic.md`) with a `Status:`
  line (Draft / Built) and a `## Why`.
- `docs/reviews/` — reviews of a design, and the adjudication of that review.

For anything larger than a small fix, read the relevant spec first; the code cites these documents
by filename in its comments, and the specs record decisions the code alone does not explain.

### Designed but not built

`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md` specifies equipment
refit, salvage and facility construction, and **none of it exists in code** — there are no slots, no
reverse runs and no construction units today. Read it before implementing any of them, because its
central decisions are ones an implementer would otherwise make differently and wrongly:

- **Two words in that spec already mean something else in this repo. Do not merge the meanings.**
  `module` is a shipped **bulk commodity** (`"id": "module"`, *Robot Module*, `holdCapacity`
  120000), produced by `assemble_modules` and consumed by `assemble_frames` — an ordinary stored,
  fungible factory-chain ingredient. `slot` is an authored **facility node position**
  (`FacilityState.BuiltAtStart` false), which is how the fixed layout reveals facilities as they are
  built. The spec's *module* is a fitted piece of equipment and its *slot* is the socket holding
  one; neither is the shipped concept. Whether the new concepts get different words is an open
  vocabulary question for the GDD — see the spec's open items — but a change that quietly makes the
  shipped `module` item unstorable would break the factory chain.
- **An equipment socket is a storage.** A fitted module is an item occupying one, so equipping and
  removing are `TransportTask`s. There is no `RefitOrder`, no `UpgradeTask` and no refit state
  machine — a refit is a plan of ordinary tasks, and every intermediate state is "a part is in a
  storage".
- **Salvage reverses the build schematic; never author a recycle schematic.** A recycle names the
  build schematic and a direction, and returns its inputs scaled by a permille recovery fraction.
  Authoring a separate recycle recipe duplicates the inputs into a record that can drift from them,
  and the symptom of that drift is an economy exploit: rebalance a recipe, forget its twin, and the
  vessel becomes a material source.
- **Fitted equipment is never in Resource Storage** — only in a socket, a facility buffer, or a
  transport in flight. *A transport in flight is now a literal place*: cargo sits on a belt slot,
  and a line whose destination is not ready freezes holding it. Storage is `ItemId → long` and
  holds quantities of interchangeable goods; a stockpiled part with wear would need per-instance
  identity. A transport whose destination is not ready holds its part and retries. There is no
  fallback line and no timeout. **This restricts only the equipment tier**; materials and components, including the shipped `module` commodity, are
  stored normally.
- **Multi-amount deposit is for the reverse direction only.** A reverse run deposits several
  `ItemAmount`s and must hold all of them until all fit; depositing what fits and dropping the rest
  destroys material. `SchematicDefinition` is unchanged and a forward run still has one output.

## Git conventions

- Work on `claude/<topic>` branches off `master`; merge via pull request.
- Commit subjects use a conventional prefix (`feat:`, `test:`, `docs:`, `refactor:`) followed by a
  **lowercase declarative sentence describing the behaviour**, not the files touched — e.g.
  `feat: a world can be put down and picked up`,
  `test: the in-memory content tree stops caring about line endings`.
- Commit bodies are prose paragraphs explaining what changed and why, including what was rejected.
  They are long by design in this repository; match that.
- The Godot project normalises line endings to LF (`dimenship/.gitattributes`).
- `.godot/`, `bin/`, `obj/`, `.superpowers/` and IDE folders are ignored.
- Godot `.uid` and `.import` sidecar files are committed alongside their assets — keep them in sync
  when adding or removing an asset or script.

## Gotchas

- A route's `lengthTicks` lives on the **scenario route**, not on the transport archetype: it is
  physical distance, and the four hold-star legs share two archetypes without sharing a distance.
  The shipped vessel's lengths are the Manhattan distance in grid cells between the two cards a
  route joins. It is optional and defaults to `1`; below `1` is rejected.
- Adding an item, facility, schematic or scenario means editing JSON in `dimenship/content/`, not
  C#. The vessel is content; there is no longer a hand-written `WorldDefinition` to construct one
  from.
- Adding a `RngDomain` is append-only. Inserting one in the middle re-points every existing save.
- Facilities with `commandable: false` (the Emergency Hydrogen Extractor) must stay unschedulable —
  no scenario may queue a task on one, and no program may target one. This is a GDD rule, stated
  three times.
- `ProductionPlanner.MaxDepth` (32) turns a cyclic schematic chain into a diagnosable shortage
  rather than a stack overflow.
- When several lines run one leg, the planner picks by **least load, then highest throughput, then
  declaration order**. Throughput sits ahead of declaration order because two lines already feed
  Factory Beta's buffer at 7 and 50 a tick, and declaration order alone sent a whole construction
  unit down the slow one. `EveryShippedUnbuiltSlot_CommissionsFromAQuietVessel_AsFastAsTheFirstLaunchPad`
  pins it on shipped content.
- `EnergyState.CapHits` and `StarvedTicks` are independent; reading either alone will mislead.
- The `TaskRegistry` retires finished tasks into a bounded window (512), as does the journal. Do not
  make either unbounded.
