# Visual Style System, Pass 1: Tokens and the Base Graph — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Overview / Base Graph focus into line with the concept art by implementing the style system that governs it — the palette tokens, the radius scale, the box primitives — and then restyling the graph itself: node cards gain an identifier badge and an icon slot, edges lose their glow and gain arc elbows, and the legend becomes a box of stroke swatches.

**Architecture:** Every colour, radius, spacing and type size lands in `ShellPalette`, and every `StyleBoxFlat` recipe in `ShellTheme`. No call site names a colour or a radius. The one kernel change is a `Badge` string on `NodePlacement`, which is authored content beside the placements it belongs to and therefore testable in `Dimenship.Core.Tests`; `Dimenship.Core` stays integer-only and Godot-free. `GraphGeometry.CellHeight` rises to fit the new card anatomy, which is arithmetic in `Dimenship.Shell` and unit-tested there. Everything else is drawing and layout in `dimenship/scripts/ui/`.

**Tech Stack:** .NET 8, C# 12, NUnit 4.6.1. Godot 4.7.1-mono for the view only.

**Spec:** `docs/superpowers/specs/2026-08-02-visual-style-system-design.md`, which amends `docs/superpowers/specs/2026-08-01-base-graph-design.md`. Both stand.

## Scope

This is **pass 1 of the style system, and it stops at the graph.** Tokens, the seven box primitives, the frost fix and the `BaseGraphFocus` restyle are in. The spec's *Composition* table — status bar relocated to the top as the six-group top bar, rail reoriented into a horizontal tab strip with disabled tabs, `FacilityInspectorPanel` restyled, the five-box bottom dock — is **pass 2** and is not in this plan. The primitives are built here so pass 2 composes them rather than re-deriving them.

Also out, unchanged from the spec's own Out of scope: tracking (`0.14em`, blocked on the font family in [issue #3](https://github.com/Gatov/DimenshipGame/issues/3)), gradient meter fills, animation of any kind, and every simulation system the concept implies but the kernel lacks.

## Global Constraints

- Target framework is `net8.0` for every project. No new assemblies.
- **No `float` or `double` anywhere in `Dimenship.Core`.** The badge is a string; it adds no geometry.
- `Dimenship.Core` must never reference `GodotSharp` or `Dimenship.Shell`. **`Dimenship.Shell` keeps zero project references.**
- **Nothing outside `ShellPalette` names a colour, a radius, a spacing or a type size.** This pass exists partly to make that true of the graph, so a literal introduced here is a defect in the work itself.
- **No state is encoded in colour alone.** Every state colour sits beside a word that says the same thing.
- **Nothing glows.** The glow *rule* stands and is vacuously satisfied once the edge glow pass is deleted.
- Tests precede the code they cover. NUnit constraint model, `Assert.That(actual, Is.EqualTo(expected), "prose reason")`, fixtures named `Subject_Behaviour_AndFurtherClause`.
- Every task ends with `dotnet build DimenshipGame.sln` clean at **zero warnings** and `dotnet test` green.
- **The Godot editor is not on `PATH` here.** No task may claim any visual behaviour is verified. Running it is the project owner's step, listed in Task 7.
- One commit per task, on `claude/base-overview-graph-align-6p5j6v`.

## Baseline

`f1b0d0b`, working tree clean. The prior plan finished at **176 tests** — 168 in `Dimenship.Core.Tests`, 8 in `Dimenship.Shell.Tests`.

**Environment note:** the container this plan was written in has **no .NET SDK**, and the network policy blocks `builds.dotnet.microsoft.com`, so neither the baseline nor any task's verification could be run here. Every `dotnet` command below is unrun. A worker with an SDK must run them before marking a task complete, and must treat a failure as this plan's defect rather than a surprise.

---

## Execution status

| Task | State | Commits | Tests after |
| :--- | :--- | :--- | :--- |
| 0 — Plan document | Complete | `9fb475b` | not run |
| 1 — Tokens | Complete | `eae8456` | not run |
| 2 — Box primitives and the frost fix | Complete | `fe8b8bf` | not run |
| 3 — Icons | Complete | `1976443` | not run |
| 4 — Node badges | Complete | `81a838c` | not run |
| 5 — Card anatomy | Complete | `41edcf1` | not run |
| 6 — Edges, legend and strip | Complete | `346bc18`, `fb4a4d8` | not run |
| 7 — Owner's visual pass | Pending | | |

**"Not run" is literal.** There is no .NET SDK in the environment these commits were written in and the
network policy blocks its download, so `dotnet build` and `dotnet test` were never executed against any of
them. What *was* done in place of a compiler: every changed file re-read after editing, a brace-and-paren
balance check with a real C# lexer over all 68 source files, and every `ShellTheme.Surface` call site
enumerated by search rather than by memory. Treat the first build as part of Task 7.

**Deviations from the spec, and why:**

1. **The badge string is authored, not derived.** The spec says node cards gain an identifier badge and does not say where the identifier comes from. A grid coordinate cannot produce the concept's `2A` / `3B`, which express a tier and a sibling within it, so `NodePlacement` carries the badge and `BaseGraphLayout.ForDefaultWorld` authors it. That also makes "no two nodes share a badge" a test rather than something noticed on screen.
2. **`PowerCard`'s badge is a view-side constant.** Power has no placement — it is pinned by the view, for the reasons the base-graph plan records — so it has nowhere in `BaseGraphLayout` to be authored.
3. **The band code stays drawn beside each edge.** The spec's edge section lists style changes only and the legend does not discharge the no-colour-alone rule for a specific edge, so the label the base-graph spec required stays.
4. **The theme's default `PanelContainer` keeps `SpaceMd` padding, not the `Pane` recipe's `Space2Xl`.** It gains the radius, because that is what makes every panel stop looking square. Its padding is left alone because the theme default backs every panel in the shell, and re-insetting all of them by 24px is a composition change belonging to pass 2. `ShellTheme.Pane()` exists and is applied there.
5. **`FrostPane` gained a `Radius`.** The frost is drawn through a rounded shape now, and the rail frosts buttons rather than panes — a `RadiusLg` fill under a `RadiusMd` button would pull the fill inside its own border. One property, defaulting to the pane case.
6. **`ResourceStrip` is restyled but not moved.** The concept puts resource totals in the bottom dock, which is pass 2. Leaving the strip on its old chrome for one pass would make it the only unstyled thing on the screen, so it adopts the `Box` recipe where it stands.

---

### Task 0: Plan document

**Files:**
- Create: `docs/superpowers/plans/2026-08-04-visual-style-base-graph.md`

- [x] **Step 1:** Record the baseline, and record that it could not be re-run here rather than copying a prior plan's figure as though it had been.
- [x] **Step 2:** Write this plan, recording the pass-1 scope cut and the badge decision so both are discoverable from the repository alone.
- [x] **Step 3:** Commit before any code.

---

### Task 1: Tokens

**Files:**
- Modify: `dimenship/scripts/ui/ShellPalette.cs`

- [x] **Step 1: The two new colours.** `Accent` = `#58A6D9`, `TextTitle` = `#D6E4EC`.
- [x] **Step 2: Remap the flow ramp.** `FlowLow` becomes `StateOk`, `FlowNormal` becomes `Accent`. The `#0E8C7A` literal is deleted, not left unreferenced. `FlowIdle`, `FlowHigh` and `FlowBlocked` are unchanged. **`FlowBands.Classify` is not touched** — this is a palette change and the band logic is already tested.
- [x] **Step 3: The radius scale.** `RadiusSm` = 2, `RadiusMd` = 4, `RadiusLg` = 8, each with the constraint that motivates it in its doc comment.
- [x] **Step 4: The two scale additions.** `Space2Xl` = 24 and `FontDisplay` = 26.

**Verification:** `dotnet build` clean. No behaviour changes, so the suite must be untouched.

---

### Task 2: Box primitives and the frost fix

**Files:**
- Modify: `dimenship/scripts/ui/ShellTheme.cs`
- Modify: `dimenship/scripts/ui/FrostPane.cs`
- Modify: `dimenship/scripts/ui/focus/NodeCard.cs`, `focus/ResourceStrip.cs`, `panels/EnergyBudgetPanel.cs`, `panels/FacilityInspectorPanel.cs` (call sites only)

- [x] **Step 1: Split `Surface`.** `Surface(Color fill)` currently serves both pane chrome and progress-bar fills, so adding radius to it would round the 4px bar fills. It becomes `Surface(Color fill, int radius)`. **Every bar call site passes `0`:** `NodeCard.Bar`, `ResourceStrip`, `EnergyBudgetPanel` including its two cached static fills, `FacilityInspectorPanel`. A `RadiusSm` corner on a 4px bar rounds away a third of its length at each end and a 3% fill becomes a lozenge that never reaches zero width.
- [x] **Step 2: The recipes.** `Pane` (`BgPanel`, `RadiusLg`, `Space2Xl` padding), `Box` (`BgPanel`, `RadiusLg`, `SpaceLg`), `Card(bool selected)` (`BgGlass`, `RadiusLg`, `SpaceMd`, border `Border` or `Accent`), `Chip(bool active)` (fill `Border` or `Accent`, `RadiusMd`, no border), `MeterTrough` / `MeterFill`, and a 1px square `Divider`. Each is built from the palette and nowhere else.
- [x] **Step 3: Accent replaces the borrowed warning colour.** The theme's `font_hover_color` for `Button` moves from `StateWarn` to `TextTitle`, and focus is a 1px `Accent` border replacing the resting border — never an outer ring, which would grow a control's drawn bounds inside a container that has already laid it out.
- [x] **Step 4: `FrostPane._Draw` through a rounded shape.** `DrawRect` squares the corners and would protrude past a `RadiusLg` border. Replace it with `DrawStyleBox` against a radius-matched `StyleBoxFlat`, keeping the existing `Grow(-1)` hairline inset. `DrawStyleBox` issues its primitives on the same canvas item, so the frost material still applies.

**Verification:** `dotnet build` clean at zero warnings. Suite untouched.

---

### Task 3: Icons

**Files:**
- Create: `dimenship/assets/icons/facility/{extractor,refinery,factory,storage,power}.svg` and their `.import` files
- Create: `dimenship/scripts/ui/focus/IconSlot.cs`

- [x] **Step 1: Five flat SVGs.** Single path, **no baked colour** — an icon carrying its own colour is a colour literal outside the palette. Artwork occupies the centre 80% of a 40×40 viewBox so silhouettes of different shapes optically match.
- [x] **Step 2: Import at 2×.** `svg/scale=2.0` in each `.import`, so the 40×40 card slot is still crisp at the graph's 200% zoom step.
- [x] **Step 3: `IconSlot`.** A fixed-size `TextureRect` tinted from the palette via `Modulate`. **A missing file renders as an empty slot of the correct size** — never a broken-texture placeholder, never a collapsed layout.

**Verification:** `dotnet build` clean. Whether the icons *look* right is Task 7.

---

### Task 4: Node badges

**Files:**
- Modify: `src/Dimenship.Core/Presentation/BaseGraphLayout.cs`
- Modify: `tests/Dimenship.Core.Tests/Presentation/BaseGraphLayoutTests.cs`

- [x] **Step 1: Fix the cell-uniqueness test first.** `NoTwoNodes_ShareACell` dedupes whole `NodePlacement` records. Once `Badge` joins the record, two nodes in one cell with different badges compare unequal and the test passes while the graph overprints. It must dedupe `(Column, Row)` tuples. **This step precedes the record change**, so the failure mode is demonstrated rather than assumed.
- [x] **Step 2: Tests for the badge.** Every placement's badge is non-empty; no two placements share one, across producers and storages together.
- [x] **Step 3: `NodePlacement(int Column, int Row, string Badge)`** and the authored badges, read along the material path: `Extractor01` `1`, `MainHold` `2`, `SmelterA` `3`, `SmelterBuffer` `4`. Sibling letters (`3A`, `3B`) appear when a tier gains siblings; the default vessel has none.

**Verification:** `dotnet test` green, +2 tests.

---

### Task 5: Card anatomy

**Files:**
- Modify: `src/Dimenship.Shell/GraphGeometry.cs`
- Modify: `dimenship/scripts/ui/focus/NodeCard.cs`, `ExecutorCard.cs`, `StorageCard.cs`, `PowerCard.cs`, `BaseGraphFocus.cs`

- [x] **Step 1: `CellHeight` 96 → 128.** A 40px icon row, a status line, two metric rows and a 4px meter do not fit 96 with `SpaceMd` padding. `GraphGeometryTests` asserts *relationships* (`StrideY = CellHeight + GutterY`), not pixel values — confirm that before changing the constant, and change nothing in the test if it holds. `CellWidth` stays 220.
- [x] **Step 2: The badge chip.** A `Chip` at the card's top-left, inset `-SpaceSm` so it overlaps the border, added as a **direct child of the `PanelContainer` and not of the content `VBoxContainer`** — a container lays out every `Control` child, and a badge inside the flow would push the header down instead of overlapping the border. `PanelContainer` clips nothing, so a negative offset draws over the border correctly.
- [x] **Step 3: The header row.** A 40×40 `IconSlot`, `SpaceMd`, then a column of the title at `FontHeading` `TextTitle` uppercase and the status line beneath at `FontMicro` — the word `STATUS` in `TextDim` beside the code in its state colour.
- [x] **Step 4: The meter.** 4px, squared fill, with a right-aligned percentage in a **fixed-width slot** so the bar's length does not change when the value crosses 9% to 10%.
- [x] **Step 5: Selection is a border colour change only.** `Accent`, not `StateWarn`. No glow, no scale, no outline growth — growth would shift the card's hit area.
- [x] **Step 6: Per-kind icons and badges.** `ExecutorCard` picks its icon from `FacilityType`; `StorageCard` and `PowerCard` take theirs by kind. `BaseGraphFocus` passes each placement's badge into the card it builds.

**Verification:** `dotnet build DimenshipGame.sln` clean at zero warnings, `dotnet test` green with no change in count.

---

### Task 6: Edges, legend and strip

**Files:**
- Modify: `dimenship/scripts/ui/focus/GraphCanvas.cs`, `GraphLegend.cs`, `ResourceStrip.cs`

- [x] **Step 1: Delete the glow pass.** The 7px translucent underlay goes. Nothing glows.
- [x] **Step 2: Selection is a 3px stroke in the band colour**, replacing the 6px `TextPrimary` underlay. A selected edge is thicker, not haloed, and not recoloured — its colour is a live reading and selection must not overwrite it.
- [x] **Step 3: Arc elbows.** A 6px arc at each corner, inserted **at draw time only**. `GraphGeometry.EdgePolyline` keeps returning the straight polyline and `HitDistanceSquared` keeps measuring it, because both are unit-tested against straight segments and an arc in the hit path would invalidate those tests for a purely cosmetic gain.
- [x] **Step 4: 8px filled triangle arrowheads** in the band colour, at each live end of the edge.
- [x] **Step 5: The legend becomes a `Box`** of five rows: a 24×2 stroke in the band colour, `SpaceMd`, the band name at `FontMicro` `TextDim`. Still pinned bottom-left, still outside the pan and zoom transform, because a key that scrolled away with the graph would be missing exactly when a new player needed it.
- [x] **Step 6: `ResourceStrip` tiles adopt the `Box` recipe** and the `Accent` fill. Content unchanged; the strip moves into the bottom dock in pass 2.

**Verification:** `dotnet build DimenshipGame.sln` clean at zero warnings, `dotnet test` green with no change in count.

---

### Task 7: The owner's visual pass

Nothing in this task is code, and no worker may mark any of it done.

- [ ] Cards, the legend and the focus pane draw with radius, and no square frost corner protrudes past any rounded border.
- [ ] Card meters at 4px are square-ended and reach visible zero width at 0%.
- [ ] A card's badge sits over its border at the top-left, and its icon is crisp at the 200% zoom step.
- [ ] The five flow bands are distinguishable from each other at 2px stroke on the real backdrop, at 50% and at 200%.
- [ ] A selected card's border is `Accent` rather than the warning colour, and a selected edge thickens without glowing.
- [ ] Every state colour on screen sits beside a word saying the same thing.
- [ ] Nothing glows.
