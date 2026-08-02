# Visual Style System — Design

Date: 2026-08-02
Status: Draft

## Goal

Turn the operational-console concept art into a written style system: the tokens, the box vocabulary, the control anatomy, and the composition of the Overview / Base Graph focus.

**This document specifies appearance only.** It introduces no simulation system, no command, and no data the snapshot does not already carry. Where the concept shows a subsystem that does not exist — drones, research, missions, case board, crew AI, strata, quick actions — this document specifies the *chrome those things would wear* and says nothing about the things themselves. The zone model, the panel contract, the snapshot-and-poll binding and the reference direction between assemblies are unchanged.

## Source material

One concept image supplied by the project owner: the operational console showing the base graph, a selected-facility inspector, a top status bar, a tab strip and a bottom dock of five boxes.

The concept is direction, not a pixel target. Seven items were reviewed against Godot before this document was written; their resolutions are recorded below and are binding on the rest of the document.

## Concept review outcomes

| # | Item | Resolution |
| :--- | :--- | :--- |
| 1 | Isometric 3D facility icons | **Flat vector.** Single-colour SVG tinted from the palette. An icon *contract* is specified so rendered art can replace it later without touching layout. |
| 2 | Station hull geometry in the backdrop | **Dropped.** It carries no function. The backdrop stays one static plate. Revisit only to keep the scene alive. |
| 3 | Glow and bloom | **Dropped.** Nothing glows. No `hdr_2d`, no `WorldEnvironment`, no baked halo sprites. The glow *rule* is retained as a constraint against the day it returns. |
| 4 | Frosted blur behind stacked panels | **Not needed.** The concept stacks nothing. The existing direct-backdrop-sample shader stays, and panel overlap is forbidden by layout rather than solved by shader. |
| 5 | Corner radius and softer chrome | **Adopted**, amending the prior shell spec. Two constraints attach — see *Radius constraints*. |
| 6 | Typography | **Deferred** to [issue #3](https://github.com/Gatov/DimenshipGame/issues/3). This document names type *sizes and roles*, never a family. |
| 7 | Fixed dashboard vs. resizable zones | **Zones win.** The concept's bottom dock is specified as a panel *style* — a box grid inside the existing console zone — not as a new layout primitive. |

## Relationship to prior specs

`2026-07-28-ui-shell-design.md` and `2026-08-01-base-graph-design.md` both stand except where amended here.

**Amendments to `2026-07-28-ui-shell-design.md`:**

1. `border` is no longer "1px, no corner radius". It is 1px with a radius drawn from the radius scale below. Chrome that must stay square is named explicitly.
2. The colour table gains two tokens, `accent` and `text.title`, and the type scale gains one size, `font.display`.
3. The glow rule stands as written and is now vacuously satisfied: nothing in the shell glows.

**Amendments to `2026-08-01-base-graph-design.md`:**

4. The flow-band colour ramp is remapped. `flow.low` becomes the bright green currently called `state.ok`, and `flow.normal` becomes the new `accent` blue. The band *logic* in `FlowBands.Classify` is untouched — this is a palette change only.
5. Node cards gain an identifier badge and an icon slot. Their content, selection behaviour and hit areas are unchanged.

**Unchanged and still binding:** nothing outside `ShellPalette` names a colour literal; no state is encoded in colour alone — every state colour sits beside a text code; `Dimenship.Core` stays integer-only and Godot-free; `Dimenship.Shell` stays engine-free.

## Tokens

`ShellPalette` remains the single source of truth. Everything below is a field in that file.

### Colour

Existing tokens, unchanged in value:

| Token | Value | Use |
| :--- | :--- | :--- |
| `BgBase` | `#0A0D0F` | focus and console ground |
| `BgPanel` | `#12181C` | opaque box fills, bar troughs |
| `BgScrim` | `#0A0D0FA6` | laid over the backdrop image |
| `BgGlass` | `#12181CA6` | mixed over the blurred backdrop inside a frosted pane |
| `BgGlassHover` | `#1E2A3199` | hover fill for a glassed control |
| `BgGlassPressed` | `#1E2A31CC` | press fill for a glassed control |
| `Border` | `#1E2A31` | hairlines, 1px |
| `TextPrimary` | `#8FA3AD` | values, body text |
| `TextDim` | `#4A6270` | labels |
| `TextFaint` | `#3D525C` | timestamps and other non-essential text only |
| `StateOk` | `#00E5C0` | running, healthy |
| `StateWarn` | `#FFB000` | near-cap, deferred |
| `StateFault` | `#FF4D4D` | blocked, fault |

New:

| Token | Value | Use |
| :--- | :--- | :--- |
| `Accent` | `#58A6D9` | active tab, focused control, informational status dot, `FlowNormal` |
| `TextTitle` | `#D6E4EC` | card titles, panel titles, the large numeric readout |

`Accent` is the one colour the concept uses that the palette had no name for: the active tab, the storage fill bar and the normal-load edges are all the same light blue, and they are all "this is the thing that is working, and nothing is wrong with it". `StateWarn` was previously doubling as the accent colour; it stops doing that here, because a selection highlight that reuses the warning colour teaches the player that selection means warning.

`TextTitle` is a brighter tier than `TextPrimary`. The concept's card and panel titles are near-white while their body values are not, and a title rendered at `TextPrimary` on `BgGlass` disappears into its own rows.

**Flow ramp, remapped:**

| Token | Now | Was |
| :--- | :--- | :--- |
| `FlowIdle` | `TextDim` | `TextDim` |
| `FlowLow` | `StateOk` | `#0E8C7A` |
| `FlowNormal` | `Accent` | `StateOk` |
| `FlowHigh` | `StateWarn` | `StateWarn` |
| `FlowBlocked` | `StateFault` | `StateFault` |

The ramp reads grey → green → blue → orange → red: idle, plenty of headroom, working, near capacity, stopped. The previously-unique `#0E8C7A` is retired; it existed only because the old ramp had nowhere else to put "moving but well short of capacity", and the remap gives that band a colour a player can actually distinguish from `FlowNormal` at 2px stroke width.

### Radius

| Token | Value | Applies to |
| :--- | :--- | :--- |
| `RadiusSm` | 2 | bar troughs, and bar fills at 6px height or above |
| `RadiusMd` | 4 | buttons, tabs, chips, badges, item rows |
| `RadiusLg` | 8 | panes, boxes, node cards, the legend |

**Radius constraints.** Two things stay square, and the reason is the same for both — a curve smaller than the shape that carries it reads as a rendering fault rather than a style:

- **Dividers and hairline separators.** A 1px rule has no corner to round.
- **Progress-bar fills at heights below 6px.** The inline card meters are 4px tall. `RadiusSm` at 4px height rounds away a third of the bar's length at each end, and a bar at 3% fill becomes a lozenge that never reaches zero width.

A third constraint applies to the graph canvas specifically: zoom steps of 50 / 75 / 150 / 200 % scale the whole canvas, so an 8px radius is drawn at 4, 6, 12 and 16px. All are acceptable. `StyleBoxFlat.CornerDetail` stays at its default of 8, which is enough segments through 200% and cheaper than raising it.

### Spacing

Existing: 2 / 4 / 8 / 12 / 16 (`SpaceXs` … `SpaceXl`).

New: `Space2Xl` = 24, for pane padding and the gaps between top-bar groups. The concept's panes breathe more than a 16px inset allows, and stretching `SpaceXl` to cover both a row gap and a pane inset is how a spacing scale stops meaning anything.

### Type

Sizes are roles, not families. The family is [issue #3](https://github.com/Gatov/DimenshipGame/issues/3).

| Token | Size | Role |
| :--- | :--- | :--- |
| `FontMicro` | 9 | section headers, timestamps, legend, status codes, item rows |
| `FontBody` | 11 | values, list rows, button text |
| `FontHeading` | 13 | card titles, panel titles, tab labels |
| `FontNumeric` | 22 | large single values in a panel |
| `FontDisplay` | 26 | the operational-time readout in the top bar — one instance, deliberately |

**Casing and tracking.** Labels, section headers, tab labels, status codes and button text are uppercase with `0.14em` tracking. Values are never uppercased and never tracked; a quantity is not a label and tracking a digit column breaks its alignment.

## Box vocabulary

Seven primitives. Everything in the console is one of these or a composition of them. Each is a `StyleBoxFlat` recipe built by `ShellTheme`; none is hand-authored per site.

### 1. Pane

The top-level frosted container: the inspector, the focus surround, the top bar.

- Fill: frosted (see *Frost*), which is opaque by construction.
- Border: 1px `Border`, `RadiusLg`.
- Padding: `Space2Xl`.
- Optional title bar: `FontHeading` `TextTitle` uppercase tracked, followed by a full-width 1px `Border` divider at `SpaceLg` below.

**Panes never overlap other panes.** The frost shader samples the backdrop directly rather than the screen, so a pane laid over another pane would show blurred nebula where the pane beneath should be. Layout enforces this; there is nothing to detect at runtime.

### 2. Box

A bordered sub-container that is not itself frosted: the five bottom-dock boxes, the graph legend, a grouped section inside a pane.

- Fill: `BgPanel`, opaque.
- Border: 1px `Border`, `RadiusLg`.
- Padding: `SpaceLg`.
- Header: `FontMicro` `TextDim` uppercase tracked, `SpaceMd` below.

A box sits *on* a pane or on the ground. It does not frost, because a second frosted layer would sample the same backdrop and produce no visible depth — only cost.

### 3. Card

A graph node. Structurally a `Box` with three additions.

- Fill: `BgGlass` over the frosted canvas.
- Border: 1px, `RadiusLg`. `Border` at rest, `Accent` when selected.
- Padding: `SpaceMd`.
- **Badge** — the node identifier (`1`, `2A`, `3B`), a `Chip` pinned to the card's top-left, inset `SpaceSm` from both edges, drawn above the card border.
- **Icon slot** — 40×40, leading, vertically centred against the title and status rows.
- **Selection is a border colour change only.** No glow, no scale, no outline growth. Growth would shift the card's hit area and reflow its neighbours in a laid-out canvas.

### 4. Chip

A small pill: identifier badges, speed multipliers, alert counts, the `4x` marker beside the clock.

- Fill: `Border` at rest, `Accent` when active.
- Border: none.
- Radius: `RadiusMd`.
- Padding: `SpaceSm` horizontal, `SpaceXs` vertical.
- Text: `FontMicro`, uppercase, `TextTitle` on an active chip and `TextPrimary` otherwise.

### 5. Row

The workhorse. A label on the left and a value on the right, both baseline-aligned.

- Label: `FontMicro` or `FontBody`, `TextDim`, uppercase, tracked.
- Value: `FontBody`, right-aligned, `TextTitle` for a plain quantity or a state colour for a state.
- Height: content, with `SpaceSm` separation between rows.
- Optional leading 16×16 icon, then `SpaceMd`, then the label.
- Optional leading 8px status dot in a state colour — **only ever beside a word that says the same thing.**

A row with a hover state (list rows, selectable rows) fills `BgGlassHover` at `RadiusMd` across its full width including padding.

### 6. Meter

A bar, optionally with a right-aligned percentage.

- Trough: `BgBase`, `RadiusSm`, no border.
- Fill: a single flat state colour, `RadiusSm`, squared when the bar is under 6px tall.
- Heights: 4px inline in a card, 6px in a pane, 8px for the top-bar energy meter.
- Percentage, when shown: `FontMicro`, `TextPrimary`, right of the bar, in a fixed-width slot so a bar's length does not change when its value crosses from 9% to 10%.

**Fills are flat, not gradient**, despite the concept's energy meter. A `Gradient` resource is a second place colours would live, and the rule that nothing outside `ShellPalette` names a colour is worth more than the gradient is. Recorded as an open item.

**Never divide by a zero denominator.** A meter with no capacity renders empty at 0%.

### 7. Divider

- 1px, `Border`, no radius, no margin of its own — the surrounding layout owns the space.
- Horizontal under a pane title; vertical between top-bar groups, inset `SpaceMd` from the group's top and bottom.

## Controls

All five interaction states are specified for every control: **normal, hover, pressed, disabled, focused**. Focus is not optional — the shell's `Tab` traversal is a committed feature and a control that cannot show focus breaks it.

Focus is drawn as a 1px `Accent` border replacing the resting border. Never an outer ring: a ring grows the control's drawn bounds inside a container that has already laid it out, and the neighbours shift.

### Button

Three variants, differing only in fill.

| Variant | Normal | Hover | Pressed | Disabled |
| :--- | :--- | :--- | :--- | :--- |
| **Default** — on an opaque box | `BgPanel` | `Border` | `Border`, text `TextTitle` | `BgBase`, text `TextDim` |
| **Glass** — on a frosted pane | transparent | `BgGlassHover` | `BgGlassPressed` | transparent, text `TextDim` |
| **Primary** — the one action a pane leads with | `Accent` at 20% over the surface | `Accent` at 32% | `Accent` at 44% | `BgBase`, text `TextDim` |

All three: 1px `Border` (or `Accent` for primary), `RadiusMd`, `SpaceSm` vertical and `SpaceMd` horizontal padding, `FontBody` uppercase tracked, text `TextPrimary`.

The existing `ShellTheme.ApplyGlass` already implements the glass variant. Primary is new and is used only for the inspector's leading action.

### Segmented group

The speed selector (`1x` / `2x` / `4x`) and any other small exclusive choice.

- A single 1px `Border` box at `RadiusMd` around the group; 1px `Border` dividers between segments; no border on the segments themselves.
- Active segment: `Accent` fill at 20%, text `TextTitle`.
- Inactive: transparent, text `TextDim`, hover `BgGlassHover`.
- Corner segments inherit the group's radius on their outer corners only.

**Radius on interior segment corners is zero.** Rounding every segment inside a rounded group produces a visible double curve at each end.

### Tab strip

- Tabs sit on the ground, not in a box. `FontHeading` uppercase tracked.
- Inactive: transparent fill, `TextDim`, no border. Hover: `BgGlassHover`, `TextPrimary`.
- Active: `BgGlass` fill, 1px `Accent` border, `RadiusMd`, text `TextTitle`.
- Padding `SpaceMd` vertical, `SpaceLg` horizontal; `SpaceXs` separation between tabs.
- A tab whose subsystem does not exist is **rendered disabled** — `TextFaint`, no hover, not focusable — rather than hidden. A tab strip that changes length as systems ship moves every tab the player has learned the position of.

### Status dot

An 8px filled circle in a state colour, `RadiusSm` is irrelevant — it is drawn as a circle. Always immediately left of text that carries the same meaning in words.

### Scroll and lists

Godot's default `ScrollContainer` chrome, restyled: 6px wide grabber, `Border` fill, `RadiusSm`, transparent trough. Lists are `VBoxContainer` of `Row`s with `SpaceSm` separation.

## Frost

Unchanged from the existing implementation, with one required fix.

`assets/frosted_glass.gdshader` samples the backdrop texture directly at a mip LOD rather than copying the screen. The comment in that file states the constraint plainly: the backdrop is the only thing ever behind a pane. **Resolution 4 makes that constraint permanent** — panes do not overlap panes — so no `BackBufferCopy` and no screen-texture mipmaps are needed, and the Mobile renderer's lack of a mipmap guarantee never becomes a problem.

**Required change:** `FrostPane._Draw` calls `DrawRect`, which draws a square-cornered fill. Under a pane with `RadiusLg` this leaves four opaque square corners protruding past the rounded border. It must draw the frosted fill through a rounded shape instead — `CanvasItem.DrawStyleBox(styleBox, rect)` with a radius-matched `StyleBoxFlat`, which issues its primitives on the same canvas item and therefore through the same shader material.

The existing `Grow(-1)` inset that preserves the hairline still applies.

## Icon contract

Flat vector now; the contract is what lets rendered art replace it later without touching a single layout.

| Slot | Size | Used by |
| :--- | :--- | :--- |
| Card | 40×40 | graph node cards, inspector header |
| Row | 16×16 | item rows, resource table, alert severity |
| Control | 20×20 | top-bar group icons, icon buttons |

Rules:

- **Single-path SVG, no baked colour.** Imported as a `Texture2D` and tinted at draw time from `ShellPalette`. An icon that carries its own colour is a colour literal outside the palette.
- **Safe area:** artwork occupies the centre 80% of the slot; the outer 10% on each edge stays empty so icons of different silhouettes optically match.
- **Imported at 2× the slot size** so the card slot stays crisp at the graph's 200% zoom step.
- **Path:** `res://assets/icons/<domain>/<name>.svg`, where `<domain>` is `facility`, `item`, `status` or `control`.
- A missing icon renders as an empty slot of the correct size, never a broken-texture placeholder and never a collapsed layout.

## The Overview / Base Graph focus

The concept's screen, mapped onto the existing zone model.

### Composition

| Concept region | Zone | Notes |
| :--- | :--- | :--- |
| Top bar | Status bar, **relocated to the top** | Currently `StatusBar` is a bottom strip. The concept puts transport, clock, energy and alerts across the top. |
| Tab strip | Rail | The rail already selects focus views. It becomes a horizontal strip beneath the top bar. |
| Graph | Focus | `BaseGraphFocus`, restyled. |
| Selected Facility | Inspector | `FacilityInspectorPanel`, restyled. |
| Bottom dock | Console | One panel containing a horizontal grid of `Box`es. |

Relocating the status bar and reorienting the rail are layout changes to `ShellRoot`, not new primitives. Zone splitters, collapse and layout persistence all survive.

### Top bar

A `Pane` spanning the full width, 72px tall with `SpaceLg` vertical and `Space2Xl` horizontal padding — the pane's default `Space2Xl` inset leaves too little height for a caption above a `FontDisplay` readout. Groups are separated by vertical `Divider`s at `Space2Xl` spacing. Each group is a `FontMicro` `TextDim` uppercase tracked caption above its content.

| Group | Content |
| :--- | :--- |
| Brand | `DIMENSHIP` at `FontHeading` `TextTitle` tracked, `OPERATIONAL CONSOLE` at `FontMicro` `TextFaint` beneath. |
| Timeflow | Three icon buttons — pause, play, step — then a segmented group of the non-zero speeds. Step is disabled while running, which `StatusBar` already does. |
| Operational time | The formatted sim time at `FontDisplay` `TextTitle`, with the active speed as a `Chip` beside it. |
| Energy | A control icon, `12.2 / 12.0 GW` at `FontBody`, the percentage right-aligned, an 8px `Meter` beneath. Fill colour is the band: `StateOk` under 80%, `StateWarn` to 100%, `StateFault` above. |
| Stability | A control icon, the percentage at `FontNumeric`, and a state-coloured word beneath. **The word is required** — the percentage's colour is not allowed to carry the meaning alone. |
| Alerts | One `Chip` per severity: an icon, a count, a state colour, and a `VIEW ALL` default button. |

Stability has no source in `WorldSnapshot`. It renders as `—` with a `TextFaint` `NO SOURCE` beneath until a system supplies it. It is specified here because the concept shows it and the group's chrome must be decided; it is not a request for the system.

### Graph canvas

Card anatomy, top to bottom:

1. **Badge** — `Chip` with the node identifier, pinned top-left, overlapping the border.
2. **Header row** — 40×40 icon, `SpaceMd`, then a column: title at `FontHeading` `TextTitle` uppercase, and the status line beneath at `FontMicro` — the label `Status:` in `TextDim` and the code in its state colour.
3. **Metric rows** — up to two `Row`s at `FontMicro`.
4. **Meter** — 4px, squared fill, with its percentage right-aligned.

`NodeCard` already owns the title, status line, selection and hit area; the badge, the icon slot and the restyled chrome are additions to it. `ExecutorCard`, `StorageCard` and `PowerCard` keep their bodies.

**Edges** keep the geometry `GraphGeometry.EdgePolyline` already produces: three orthogonal segments, mid-gutter elbow, parallel offsets, arrowheads, an opposing pair merged into one double-headed edge. Style changes only:

- 2px stroke in the band colour, antialiased.
- Elbows drawn with a 6px arc rather than a hard corner. `GraphGeometry` continues to return the polyline; the arc is inserted by the drawing code and is never part of the hit test, which stays on the straight polyline `HitDistanceSquared` already measures.
- Arrowheads: 8px filled triangles in the band colour.
- A selected edge draws at 3px, colour unchanged. Not a glow.
- No animation. Nothing redraws per frame.

**Legend** — a `Box`, pinned bottom-left of the canvas and outside the pan/zoom transform, with five rows: a 24×2 stroke in the band colour, `SpaceMd`, the band name at `FontMicro` `TextDim`.

### Selected Facility inspector

A `Pane` with a title bar reading `SELECTED FACILITY`.

1. **Header** — 40×40 icon, the facility name at `FontHeading` `TextTitle`, its type beneath at `FontMicro` `TextDim`.
2. **Summary rows** — `Row`s for status, health and the like, some carrying a 6px `Meter` beneath the row.
3. **Sections** — a `FontMicro` `TextDim` uppercase tracked header, a full-width `Divider`, then rows. Section headers may carry a parenthetical qualifier at `TextFaint`, as in `BASIC (PER SECOND)`.
4. **Item rows** — 16×16 icon, name, and a right-aligned `2.8 / 3.5 m³`.
5. **Footer** — a `Divider`, then a button row: one Primary and the rest Default, equal width, `SpaceMd` separation.

The empty state is `NO SELECTION` at `FontBody` `TextDim`, centred, with no chrome around it.

### Bottom dock

One console panel holding an `HBoxContainer` of `Box`es, each expanding equally, `SpaceLg` separation. Five box *styles*, each specified independently of whether its data source exists:

| Style | Anatomy |
| :--- | :--- |
| **Status list** | `Row`s with a leading 8px status dot, label left, value right in a state colour. |
| **Alert feed** | Rows of severity icon, `FontMicro` `TextFaint` timestamp, then the message in the severity's colour. Newest first, scrolling, oldest clipped. |
| **Progress list** | Per entry: a `FontBody` line, then a full-width 6px `Meter`. `SpaceLg` between entries. An empty slot renders its identifier and `NONE` at `TextFaint`. |
| **Resource table** | A `FontMicro` `TextDim` column-header row, a `Divider`, then rows of 16×16 icon, name, right-aligned amount, and a trend cell — an arrow glyph and a signed percentage in `StateOk` or `StateFault`. Trend columns are fixed-width so signs align. |
| **Action stack** | Full-width Default buttons, left-aligned text, `SpaceSm` separation. |

Only **Status list**, **Alert feed** and **Resource table** have data today. A box whose source does not exist is not built; it is not faked and not stubbed.

## Godot implementation assessment

Difficulty is effort-to-correct, not effort-to-first-pixel.

### Trivial — `StyleBoxFlat` and container work

| Element | Mechanism |
| :--- | :--- |
| Corner radius everywhere | `StyleBoxFlat.CornerRadius*`, default `CornerDetail` of 8 |
| Boxes, panes, cards, chips, rows | `StyleBoxFlat` + `PanelContainer` / `VBoxContainer` / `HBoxContainer` |
| Button variants and all five states | `Theme.SetStylebox` per state; the existing `ApplyGlass` pattern extends unchanged |
| Tab strip | `Button` with per-state styleboxes; active is a stylebox swap |
| Dividers | `ColorRect` at 1px, or `HSeparator` restyled |
| Rows, tables, column alignment | `HBoxContainer` with `CustomMinimumSize` on the value cells |
| Meters | The existing `NodeCard.Bar` pattern — `ProgressBar` with `fill` and `background` stylebox overrides |
| Status dots | `DrawCircle` in a fixed-size `Control` |
| Top-bar relocation, rail reorientation | `ShellRoot` layout change; splitters and persistence unaffected |

### Small — a known mechanism with one catch each

| Element | Mechanism | Catch |
| :--- | :--- | :--- |
| **Frost under a rounded pane** | `DrawStyleBox` replacing `DrawRect` in `FrostPane._Draw` | `DrawRect` squares the corners under a rounded border. Must be fixed with the radius, not after. |
| **`ShellTheme.Surface` split** | Radius-aware variants | `Surface` is currently shared by pane chrome *and* progress-bar fills. Adding radius to it rounds the 4px bar fills. It must become `Surface(fill, radius)` with the bar call sites passing zero. |
| **Card badge overlapping the border** | A `Chip` `Control` positioned by the card, drawn after it | A `PanelContainer`'s stylebox clips nothing, so a child laid out at a negative offset draws over the border correctly — but it must not be inside the content `VBoxContainer` or the container will lay it out in the flow. |
| **Segmented group** | One bordered `HBoxContainer` of borderless buttons with separator children | Outer corners get radius, interior corners get zero. Four distinct styleboxes: left, middle, right, and single. |
| **Rounded edge elbows** | Insert an arc into the polyline at draw time | The hit test must keep using the straight polyline. `GraphGeometry.HitDistanceSquared` is unit-tested against straight segments and stays that way. |
| **Vector icons** | SVG imported as `Texture2D`, tinted via `Modulate` | Godot rasterises SVG at import time at a fixed scale. Set the import scale to 2× the slot so the 200% zoom step stays crisp. |
| **Card height** | `GraphGeometry.CellHeight` | A 40px icon row, a status line, two metric rows and a meter do not fit 96px with `SpaceMd` padding. `CellHeight` likely rises to about 120. It is a `const` that `CellRect` and `ContentSize` are unit-tested against, so the tests assert relationships rather than pixel values — confirm that before changing it. |
| **Tracking (`0.14em`)** | `Label.AddThemeConstantOverride("outline_size")` does **not** do this | Godot has no letter-spacing theme constant. Either a font resource with `extra_spacing_glyph` set, or `RichTextLabel`. The font resource is the cheaper path and lands with [issue #3](https://github.com/Gatov/DimenshipGame/issues/3). |

### Deferred by decision — no implementation

Isometric 3D icons, backdrop hull geometry, glow and bloom, screen-space blur for stacked panes, gradient meter fills, edge flow animation.

Each of these is deferred because it was judged not worth its cost now, not because it is hard. The two that *are* genuinely hard if they return:

- **Rendered isometric icons** are an art-pipeline problem — consistent lighting, camera angle and scale across a dozen facilities — with no engine component at all. The icon contract above is what keeps that a pure asset swap.
- **Screen-space frost** would require `BackBufferCopy` plus screen-texture mipmaps, which the Mobile renderer does not guarantee. Resolution 4 removes the need permanently, and it should not be reintroduced without also revisiting the renderer.

### Nothing here needs the editor to verify

Every mechanism above is C# building Godot objects at runtime. No `.tscn` is hand-authored, no `.tres` theme resource is hand-edited, and the palette-to-theme path stays a single C# file — which is the same reason the original shell spec chose C# over a `Theme` resource.

## Error handling

| Failure | Behaviour |
| :--- | :--- |
| An icon file is missing | Empty slot at the correct size. Layout does not collapse. |
| A meter's denominator is zero | Renders empty at 0%. No division. |
| A value's state has no colour mapping | `TextPrimary` and the literal code. Never an uncoloured blank. |
| A dock box's data source does not exist | The box is not built. Not stubbed, not faked. |
| A tab's subsystem does not exist | Rendered disabled at `TextFaint`, not hidden. |
| A pane would overlap another pane | A layout bug, caught by looking at it. There is no runtime guard and there should not be one. |

## Out of scope

Every simulation system the concept implies and the kernel lacks: drones, research, missions and mission docks, case board, crew AI, network alerts as a stored feed, strata and expeditions, stability, quick actions, build and construction state. Animation of any kind. A mobile or narrow-viewport profile. The program editor. Player-authored layout or node dragging. Sound.

## Open items

1. **Font family.** [Issue #3](https://github.com/Gatov/DimenshipGame/issues/3). Tracking depends on it; every size token here is stable regardless of the outcome.
2. **Gradient meter fills.** Rejected here to keep `ShellPalette` the only place colours live. If it returns, a named `Gradient` per token in the palette file is the shape that keeps the rule.
3. **Glow.** Dropped. If it returns, note that screen-space glow cannot distinguish a bar from a label and therefore cannot honour the glow rule; per-element baked sprites can.
4. **Backdrop hull geometry.** Dropped as functionless. Revisit only as a static art plate; a 3D `SubViewport` interacts badly with the direct-sample frost shader.
5. **`Accent` value.** `#58A6D9` is read off the concept and is the one new colour with no prior use to anchor it. Expect to tune it once against the real backdrop.
6. **Disabled tabs.** Rendering unbuilt subsystems as disabled tabs keeps positions stable but shows the player a menu of things they cannot do. Reconsider if the count of disabled tabs stays high for long.

## Verification

Automated: `dotnet build DimenshipGame.sln` clean and `dotnet test` green. `Dimenship.Shell` gains no arithmetic here, so no new unit tests fall out of this document — it is a styling document, and its correctness is visual.

Manual, in the editor, the user's step:

- Panes, boxes and cards draw with radius, and no square frost corner protrudes past any rounded border.
- Card meters at 4px are square-ended and reach visible zero width at 0%.
- A segmented group shows no double curve at either end.
- The five flow bands are distinguishable from each other at 2px stroke on the real backdrop, at 50% and at 200% zoom.
- Every state colour on screen sits beside a word saying the same thing.
- Tab, hover, press and disabled states are visible on every control class, and `Tab` traversal shows focus on each.
- A card icon is crisp at 200% zoom.
- Nothing glows.

The Godot editor is not on `PATH` in this environment. No visual item is reported as verified before the user confirms it.
