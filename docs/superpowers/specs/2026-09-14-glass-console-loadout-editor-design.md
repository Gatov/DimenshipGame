# Glass Console Loadout Editor — Design

Date: 2026-09-14
Status: Draft

## Goal

Re-present the loadout composer as a machine on the ship's console instead of a form. The selected
template's frame is drawn in the centre as pale-cyan line art on dark glass; every socket is a box
around it, showing what is fitted, joined to its connector on the frame by a thin leader line.
Choosing what goes in a socket happens in a picker that opens beside that socket's box, and the
effect of a candidate on power, stats and cost is shown before it is chosen.

This is a **presentation change to a concept mock and nothing else.** The mock's rules — its frames,
socket kinds, fittings, stats, costs, verdicts and net power budget — are carried over as they
stand, with one exception (Decision 2). Nothing it authors is executed, persisted or seen by the
simulation, `Dimenship.Core` does not change, and `QUEUE BUILD` stays disabled for the reason it is
disabled today.

The asset budget is the ticket's and is a design constraint rather than a wish: **one fixed
illustration per frame, one reusable standalone image per fitting.** No per-frame perspectives, no
fitted variants, no occlusion, no 3D. Every decision below that touches art is chosen to stay inside
it.

## Source material

- **[Ticket #38 — Glass Console Loadout Editor](https://github.com/Gatov/DimenshipGame/issues/38)** —
  the brief, authoritative for the interface style, the frame-and-box presentation, the intended
  functionality and the graphics data. Its word *slot* is read as **socket** throughout, as the
  ticket itself asks ("'slot' refers to an equipment socket, not a facility position").
- **The sketch attached to ticket #38** (a photographed print of a mockup; transcribed here because
  the image is not in the repository). Left column **LOADOUTS**: one card per template, each a small
  copy of its frame art above the template name, the selected card outlined. Centre header: template
  name *Deep Prospector*, frame name *Surveyor frame* beneath it, a **Change frame** button. Centre
  stage: a wheeled rover with a manipulator arm and a sensor mast; boxes labelled *TOOL — Mining Head
  Mk1* and *POWER — Power Core Mk1* on the left, *SENSOR — Basic Sensor Array* (selected, bright
  border) and *INVESTIGATION — Empty* with a dashed `+` on the right; each box joined to a connector
  dot on the frame by a line that leaves the box horizontally and then runs straight to the dot. A
  popover titled *Compatible sensors* with a close cross, notched to the selected box, lists sensors
  with image and name, a *Fitted* chip on the current one, a stat-change line on the hovered one,
  and a *Clear slot* row last. A bottom strip carries **POWER BUDGET** as a bar — solid for the
  current draw, hatched for the preview — reading `310 / 600` over `Preview: 430 / 600`, then
  **Build cost** as item icons with amounts. Two handwritten notes: *"Fixed frame + chassis
  artwork"* pointing at the rover, and *"Equipment changes only in these boxes"* pointing at the
  boxes.
- **`docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md`** — the mock this restyles.
  Binding on everything this document does not explicitly change: the concept-mock status, the
  sample content, the verdicts, the empty-socket rule, the live build-cost reading, the panel id
  `robotics` and title *Robotics*, and the rule that the invented catalog is neither shipped content
  nor tested content.
- **`docs/superpowers/specs/2026-08-21-bot-composition-design.md`** — the source of the word
  **fitting** (its *A vocabulary decision, made rather than deferred*). Its socket topology (Mobility
  · Hardpoint · Systems · Payload) and its frame-baselined power are **not** adopted here; see
  Decision 1.
- **`docs/superpowers/specs/2026-08-02-visual-style-system-design.md`** and the CLAUDE.md rules for
  the shell — binding without restatement: no hard-coded colour, `ShellPalette` as the only source
  of colour, spacing and type size, the box vocabulary in `ShellTheme`, icons through `IconSlot` and
  tinted from the palette, and selection as a border colour only.

## Vocabulary

| Word | Meaning here | Why |
| :--- | :--- | :--- |
| **Fitting** | What occupies a socket. Replaces the mock's *part*. | Bot-composition settled the equipment tier's name as *fitting*, because `module` is the shipped bulk commodity. The mock took *part* as a placeholder "until the GDD decides"; a settled word exists, so the mock renames with it. |
| **Socket** | A position on a frame that holds one fitting. Now has an **id**. | Unchanged. Never *slot*: a slot is an authored facility position on the base graph. |
| **Anchor** | The point on the frame artwork where a socket's connector is drawn, and where its leader line ends. | The ticket's *attachment point*. A presentation coordinate, not a model concept. |
| **Fitting box** | The on-stage element showing one socket's kind, its fitted fitting and that fitting's image. | The ticket's *equipment box*. A visual element, not an inventory object; nothing is stored in it. |
| **Frame art** | A frame's illustration plus its placement sidecar. | The ticket's *graphics definition*. |

The GDD glossary still says *Fitted Module*. Reconciling it is already listed as a GDD edit by the
bot-composition spec and stays there; this document does not edit the GDD.

## Decisions

### 1. The mock's rules stay; only the word changes

The ticket says existing loadout rules remain the basis for validation, and it is right to: this is
a presentation question, and changing the rules underneath it would make the two impossible to
judge separately. So the six socket kinds, the two frames (Surveyor: Tool, Sensor, Power,
Investigation; Hauler: Tool, Cargo, Power, Defense, Sensor), the sixteen fittings and their stats
and costs, the net power budget and the verdicts `COMPLETE` / `INCOMPLETE` / `OVER BUDGET` are
carried over unchanged.

Bot-composition's topology was the alternative. Its art brief would fit the ticket's language
(manipulators, a tool or weapon mounted on one) slightly better, and drawing the art once against
the topology we expect to keep has some appeal. It was rejected because it would change what the
view composes as well as how it looks, in the same step, and because the art budget — two line
drawings — makes redrawing both frames when that topology lands a small cost.

The word does change: `PartDef` → `FittingDef`, `LoadoutCatalog.Parts` → `Fittings`,
`LoadoutCatalog.Part(id)` → `Fitting(id)`, and every player-facing string and doc comment that says
*part*. `LoadoutDraft.Fitted` already reads correctly and keeps its name.

### 2. Sockets gain ids, and a frame swap carries fittings by id

The ticket's graphics data is keyed by socket id, and the mock's sockets have none — they are
positions in a `SocketKind[]`. `FrameDef.Sockets` becomes `IReadOnlyList<SocketDef>`, where
`SocketDef(string Id, SocketKind Kind)`:

| Frame | Socket ids, in declaration order |
| :--- | :--- |
| Surveyor Frame | `tool`, `sensor`, `power`, `investigation` |
| Hauler Frame | `tool`, `cargo`, `power`, `defense`, `sensor` |

Ids match the catalog pattern `^[a-z][a-z0-9_]*$` and are unique within a frame. `Fitted` stays a
positional list parallel to `Sockets`; declaration order is still the display order.

**`LoadoutDraft.Refit` changes rule**, and it is the only rule this document changes. Today a fitting
survives a frame swap when the new frame has the same kind at the same index, so Surveyor → Hauler
keeps Tool and Power and drops the Sensor, because the Hauler's sensor sits at index 4 rather than
1. With ids, a fitting survives when the new frame has a socket with **the same id and the same
kind**, so tool, sensor and power all carry. This is not the "hunting for a compatible socket
elsewhere" the mock spec rejected — that would move a fitting to a socket the player did not choose.
Matching by id moves it only to the socket that is, by name, the same one. The positional rule was
the best the mock could do without ids; once ids exist it is the worse rule.

### 3. Fitting images are found by convention

`res://assets/loadouts/fittings/{fitting id}.svg`, loaded through `IconSlot`'s quiet
`ResourceLoader` path, so a missing file is an empty image area rather than a fault. `FittingDef`
gains no image field: the id is already unique, and a field would restate it in a second place that
could drift. This is how the ticket's "equipment definitions reference their reusable standalone
images" is met.

Fitting images import as textures at `svg/scale=2.0`, like the icons. They are drawn at 64 px in a
box and 40 px in the picker, both below the imported size.

### 4. Frame art is an SVG and a JSON sidecar, beside each other

`dimenship/assets/loadouts/frames/{name}.svg` and `{name}.json`, one pair per frame. The sidecar:

```json
{
  "notes": "Placement for the Surveyor Frame's artwork. Coordinates are SVG user units in the artwork's own viewBox; box positions are centres, and box size belongs to the theme. Placement only: which fitting fits which socket is LoadoutCatalog's.",
  "frame": "surveyor_frame",
  "artwork": "surveyor.svg",
  "canvas": { "width": 1200, "height": 720 },
  "sockets": [
    { "id": "tool",   "anchor": { "x": 470, "y": 250 }, "box": { "x": 210,  "y": 170 } },
    { "id": "sensor", "anchor": { "x": 690, "y": 210 }, "box": { "x": 1000, "y": 150 } }
  ]
}
```

- **One coordinate space.** `canvas` equals the SVG's `viewBox`. The frame is drawn in the middle of
  the canvas and the margins are where the boxes go, so artwork, anchors and box centres share one
  system and scale together — the ticket's requirement, met by construction rather than by a
  conversion step.
- **Integers only**, in the house style. A fractional coordinate is reported, not rounded.
- **Box size is not authored.** Boxes carry text, and text stays at `ShellPalette`'s sizes so it is
  sharp at every stage scale. Only a box's centre scales; its size is the theme's. The ticket asks
  for a centre and nothing more.
- **Placement only.** The sidecar says nothing about which fitting fits which socket. That stays in
  `LoadoutCatalog`, which is the ticket's "graphics data does not define compatibility" made
  structural: a sidecar edit cannot change what is legal.

A C# record beside the catalog was the alternative, and it was the lighter one. The sidecar was
chosen so that moving a box is an asset edit, made next to the drawing it belongs to, without a
recompile. That puts a parser in the design (Decision 5), which is its price.

Frame SVGs import with **Keep File**, because they are rasterised at runtime from their source
(Decision 7) and the source has to reach the export untouched.

### 5. The sidecar parser follows the settings file's conventions

`FrameArtSerializer` in `Dimenship.Shell`, modelled on `SettingsSerializer`: a DTO whose every field
is nullable so a missing one is reported rather than defaulted, unknown fields rejected, and
problems **collected as warnings rather than thrown**. It returns a `FrameArt` (or null) and the
warnings. It cannot reference the mock's catalog — `Dimenship.Shell` sees neither the kernel nor the
Godot assembly — so validation is split along that line:

- **In `Dimenship.Shell`**, catalog-free: the file is non-empty and well-formed, every field is
  present, no field is unknown, coordinates are integers, socket ids are unique and match the id
  pattern, and every anchor lies inside the canvas.
- **In the Godot layer**, against `FrameDef`: the sidecar's `frame` is the frame being drawn, its
  socket ids are **exactly** the frame's socket ids — none missing, none extra — and the named
  artwork file exists.

Box overlap and line crossing are deliberately not parser checks. Whether two boxes overlap depends
on the scale they are drawn at, which the parser cannot know; `StageGeometry` checks it (Decision 8).

### 6. A bad sidecar falls back for the whole frame, and says so

Any warning on a frame's art means that frame is drawn **without** art: no illustration, no leader
lines, no anchors, and the boxes stacked in a plain column in socket declaration order. One line in
the stage states the first warning in words — `FRAME ART UNAVAILABLE — surveyor.json: socket
'sensor' has no placement` — and every warning goes to `GD.PushWarning`.

Composing keeps working in the fallback, because the model never depends on the art. Falling back
for the whole frame rather than for the offending socket was chosen because a stage with some boxes
placed and some not reads as a layout, and the player would take it for the intended one. A content
load failure is fatal elsewhere in the shell (`ShellContent`); this is not content, and the view is
a mock, so the settings file's degrade-and-report is the right precedent rather than content's
refuse-to-start.

### 7. The stage is drawn the way the base graph is

`LoadoutStage : Control` replaces the socket rows, and follows `GraphCanvas`: lines are drawn,
cards are child controls. Back to front:

1. **The drawing** — a `TextureRect` using `res://assets/projection.gdshader`. The shader tints the
   white line art with a `tint` uniform set from `ShellPalette.Projection`, so the shader holds no
   colour of its own, and adds a small halo by sampling the texture's alpha around each pixel. The
   glow is on the drawing only; text is never blurred, which is the ticket's "keep text sharp".
2. **The leader layer** — a `Control` that only draws: each socket's leader line and a dot at its
   anchor. Unselected lines use `ShellPalette.ProjectionGuide`, a quieter shade.
3. **`FittingBox` children** — the theme's card vocabulary on the glass ground. Socket kind in
   capitals at `FontMicro` in `TextDim`, fitting name at `FontBody`, and the fitting's image at
   64 px through `IconSlot`, tinted `Projection`. An empty box reads `EMPTY` in words over a dashed
   `+` placeholder drawn with `ShellTheme.DrawDashedPolyline` — the same dashed vocabulary the base
   graph uses for "nothing is here yet", so the two views say one thing one way.
4. **Popovers** (Decisions 9 and 10).

The frame SVG is **rasterised at runtime** (`Image.LoadSvgFromString`) at the stage's scale, rounded
up to a step of 0.25 and cached per step. An import at a fixed `svg/scale` was the alternative; it
either blurs lines when the zone is enlarged or wastes memory at every size, and thin lines are the
whole of this art.

Two palette entries are added — `Projection` and `ProjectionGuide` — and nothing on the stage uses a
colour that is not in `ShellPalette`.

A `SubViewport` with engine glow was rejected: bloom would reach the text and the boxes as well as
the drawing, and a second viewport is exactly what `SettingsOverlay` avoids because the frost shader
samples `SCREEN_UV`. Drawing the texture several times at offsets for a halo was kept as a fallback
only; it is lumpy, and it is not simpler once tinting is in.

### 8. Stage geometry is pure and tested

`StageGeometry` in `Dimenship.Shell`, beside `GraphGeometry` and in its idiom — integer tuples, scale
as permille:

- `Fit(canvas, stage)` — letterboxes the canvas into the stage, centred; the drawing is never
  stretched.
- `BoxRect(centre, boxSize, fit, stage)` — the scaled centre, with the box clamped inside the stage.
- `Leader(boxRect, anchor)` — leaves the box at the midpoint of the side facing the anchor, runs a
  short horizontal stub, then goes straight to the anchor. The sketch's shape.
- `Problems(boxes, leaders)` — box overlapping box, leader crossing leader, and leader passing
  through a box other than its own.
- `PopoverRect(boxRect, popoverSize, canvasCentre, stage)` — Decision 9's placement.

**Boxes are never moved automatically.** Their positions are authored, and an automatic layout would
be a second, unauthored answer to where a box belongs. When `Problems` is non-empty at the current
stage size, the stage shows one quiet line — `BOXES OVERLAP AT THIS SIZE — WIDEN THE VIEW` — and
pushes a single warning. Sidecars are authored to pass at the smallest size on `ScreenSizes.Ladder`
(1280 × 720), which is the ticket's "author box positions to avoid overlap and crossing lines" as a
rule with a check behind it.

### 9. Fitting is click-only, through a picker beside the box

The mock's permanent palette goes, and with it every drag gesture: dragging off the palette has no
palette, dragging back to it to remove has nothing to drop on, and box-to-box swapping only ever
applied between two sockets of one kind, which neither sample frame has. `PartPalette`,
`LoadoutDragData` and `SocketRow` are deleted.

`FittingPicker` is a popover:

- **Opens** on a click or `Enter` on a box, on the side of the box facing away from the canvas
  centre, flipping when there is no room and clamped vertically inside the stage, with a notch at
  its box. Selecting a different box moves it there.
- **Title** `COMPATIBLE {KIND} FITTINGS`, and a close cross.
- **Rows** in declaration order (`LoadoutCatalog.OfKind`): 40 px image, name, and a `FITTED` chip on
  the fitting currently in the socket. Compatibility is `LoadoutCatalog`'s, so the picker lists only
  what the socket accepts, and there is no incompatible row to explain.
- **`CLEAR SOCKET`** is the last row — the ticket's explicit clear action. Disabled and marked
  `ALREADY EMPTY` on an empty socket. `Delete` still clears the selected socket, as it does today.
- **Closes** on the cross, `Esc`, a click outside it, or a choice.

Choosing fits the fitting through the existing `RecordEdit`, so it is one undo step; the picker
closes and the box stays selected.

### 10. Preview is a rollup of a copy, never an edit

Hovering or keyboard-focusing a picker row — `CLEAR SOCKET` included — previews it. The composer
clones the template, applies the candidate, and runs `LoadoutRollup.Of` on the clone. The template
is not touched and nothing reaches the undo stack, so a preview can never become an edit the player
did not make.

The preview shows in three places:

- **The row** gains a second line naming only the stats that change, as signed differences from the
  fitting currently in the socket: `SCAN +270 · MASS +50 · POWER −120`.
- **The bottom strip** (Decision 12) hatches the power difference and annotates each affected stat
  and cost figure with its `+n` / `−n`.
- **In words**, when the preview would change the verdict: `PREVIEW: OVER BUDGET BY 120`.

The box and the frame art do not change during a preview, which is both the sketch and the ticket's
"the frame artwork remains unchanged throughout preview and fitting".

### 11. The frame chooser is the same popover, previewing the carry-over

**CHANGE FRAME** in the header opens a popover built the same way: one row per frame with its
thumbnail, name, socket count and note, and a `CURRENT` chip. Hovering a row previews Decision 2's
carry-over in words — `KEEPS tool · sensor · power — DROPS investigation` — so the player reads what
a swap will cost before making it. Choosing calls `Refit` and records one undo step.

### 12. The bottom strip carries power, stats and cost; details open over the stage

`LoadoutStrip` is one glass box in four sections:

- **POWER BUDGET** — a `BudgetBar : Control` that draws itself. The bar's length is **supply** (every
  positive power contribution — in the sample content, whichever power fitting is fitted), its
  solid fill is **draw** (every negative one), labelled `310 / 600`.
  `Rollup` gains `Supply` and `Draw` beside its existing net, from the same per-fitting
  contributions, so the three can never disagree. The preview's difference is hatched, in either
  direction, labelled `PREVIEW 430 / 600`. Over budget, the fill runs to the end in `StateFault` with
  an overflow mark and `OVER BUDGET BY 120` in words; with no core fitted it reads `NO POWER SOURCE —
  0 / 0`.
  - Draw against supply replaces the mock's single net figure on screen. The sketch reads that way,
    and a bar with a capacity is what makes a hatched preview segment mean anything.
- **STATS** — mass, durability, cargo, scan and work rate, each as label, icon and number (`MASS
  1,240`). The label is there so an absent icon — mass has none — never leaves a number
  unexplained, and so the reading does not rest on an icon or a colour alone.
- **BUILD COST** — each item's icon and required amount. An item the vessel cannot cover says `SHORT`
  in words. This is still the mock's one live reading, from `WorldSnapshot.Resources`.
- **DETAILS** — a toggle.

DETAILS opens a drawer **laid over** the bottom of the stage, holding the existing `RollupGrid`
(per-fitting attribution) and `CostBox` (held against required, `SHORT` rows) side by side and
unchanged. Over, not above: a drawer that pushed the stage up would resize it, and a resized stage
moves every box, so opening the details would rearrange the thing being examined. The toggle's
state is session-local.

A tooltip per stat was the lighter alternative for the breakdown and was rejected: a tooltip cannot
hold a six-row attribution legibly, and a keyboard cannot reach one.

### 13. Header and library

**Header:** the template name at `FontHeading` in `TextTitle`, the frame name beneath it, **CHANGE
FRAME**, the verdict chip in words, `CONCEPT — NOTHING IS BUILT FROM THIS`, and the disabled
**QUEUE BUILD**. The sketch omits the last three; they stay, because the mock's honesty rule is the
one thing about it that is not presentation.

**Library (LOADOUTS):** `TemplateList` cards, each carrying the template's frame art as a thumbnail
— the same SVG rasterised small and tinted, without the glow — its name, and a micro line
`3/4 · INCOMPLETE`. That line replaces the mock's *Selected Template Info* box, whose three readings
it carries. **+ NEW TEMPLATE** sits at the foot of the list. Undo stays per template and is still
cleared when the selection changes.

### 14. Keyboard

Fitting boxes take focus in socket declaration order (`Tab`, arrow keys). `Enter` opens the picker,
arrow keys move through its rows (previewing as they go), `Enter` chooses, `Esc` closes. `Delete`
clears, and `Ctrl+Z` / `Ctrl+Y` are unchanged. While a popover is open,
`LoadoutsFocus._UnhandledKeyInput` accepts `Esc` and closes it; Godot delivers unhandled key input
before `ShellRoot._UnhandledInput`, where `Esc` means "release focus", so the shell's binding is
reached only once no popover is up.

### 15. Art direction

Every file is line art: `fill="none"`, strokes in white — the tint base, as the existing icons are
white and tinted at runtime — width 2 for outlines and 1 for detail, round caps and joins. No text,
no gradients, no filters and no baked glow; the shader owns the glow, and an asset that carried its
own would be a colour outside the palette.

- **Frames** — canvas 1200 × 720, the machine roughly 560 × 480 in the middle, margins left for the
  boxes. Every connector is drawn as a ringed port **whether or not anything is fitted**; the ticket
  requires it, and it is what keeps one illustration per frame sufficient.
  - **Surveyor** — a light four-wheeled rover: the tool connector at the wrist of a manipulator arm,
    the sensor on a mast, power behind a side bay hatch, the investigation port at the rear.
  - **Hauler** — a heavier six-wheeled body with a cargo bed: tool arm, cargo bed mount, power bay,
    an armour mounting rail for defense, a sensor mast.
- **Fittings** — sixteen standalone drawings, `viewBox="0 0 96 96"`, same stroke rules. Reused
  unchanged in boxes, in the picker and anywhere else a fitting is shown.

The art is hand-written SVG. Tracing raster art with `svg-icon-maker` was the alternative; tracing
yields filled regions rather than strokes, which works against the projected-line look, and it makes
exact anchor coordinates harder to author.

## Where the code lives

All of it in `dimenship/scripts/ui/focus/loadouts/`, the Godot assembly, except the two pieces that
are engine-free and carry no invented content:

| Unit | Assembly | Tested |
| :--- | :--- | :--- |
| `FrameArtSerializer`, `FrameArt` DTOs | `Dimenship.Shell` | Yes |
| `StageGeometry` | `Dimenship.Shell` | Yes |
| `LoadoutCatalog`, `LoadoutModel`, `LoadoutRollup` (renamed and extended) | Godot | No — invented content, per the mock spec |
| `LoadoutStage`, `FittingBox`, the leader layer, `FittingPicker`, the frame chooser, `LoadoutStrip`, `BudgetBar` | Godot | No — verified in the editor |
| `projection.gdshader` | `dimenship/assets/` | No |

The two Shell units describe placement and geometry in general and never name a frame, a socket or
a fitting, which is what lets them be tested without making the invented catalog tested content.

## Testing

**`tests/Dimenship.Shell.Tests`:**

- `FrameArtSerializerTests` — a valid sidecar parses with no warnings. Each other test copies a
  minimal valid sidecar and **breaks exactly one thing**: malformed JSON, an empty file, a missing
  field, an unknown field, a fractional coordinate, a duplicate socket id, an id failing the
  pattern, an anchor outside the canvas. Each yields one named warning and no exception.
- `StageGeometryTests` — `Fit` letterboxes on each axis and centres; `BoxRect` clamps a box at the
  edge; `Leader` leaves from the side facing its anchor and carries the stub; `Problems` reports an
  overlap, a crossing and a pass-through and is empty for a clean layout; `PopoverRect` flips when
  the preferred side has no room and stays inside the stage.

The shipped sidecars and the sample catalog are not tested, per the mock spec; the runtime check of
Decision 8 and the fallback of Decision 6 are what catch a broken one.

**In the editor** — both frames with every socket filled and emptied; picker preview on every row
including `CLEAR SOCKET`; a frame swap in each direction against its carry-over preview; undo and
redo across fits, clears and swaps; an over-budget template and a template with no core; a sidecar
broken on purpose; the smallest ladder window; and one full pass using only the keyboard.

## Not built

Recorded so a later reader does not assume an oversight:

- **Everything the mock spec does not build**, unchanged: no robots, no refit, no socket storages, no
  building from a template, no persistence and no save format.
- **Fitted or per-frame fitting art**, perspectives, occlusion, animation and 3D — the ticket's asset
  budget.
- **Drag and drop**, in every form (Decision 9).
- **Automatic box layout** (Decision 8).
- **Bot-composition's topology** and its frame-baselined power (Decision 1).
- **An export preset.** The frame SVGs are Keep File, but the `.json` sidecars need an include
  filter to reach an export; `content/` has the same gap, and no preset exists yet.
- **Any GDD edit**, including the *Fitted Module* glossary entry.
- **Any change to `Dimenship.Core`.**

## Open items

- **The `.json` include filter**, whenever an export preset is first written. It should cover
  `content/` and `assets/loadouts/` together.
- **Whether library thumbnails should glow.** Decision 13 leaves them flat so a column of six glowing
  rovers does not compete with the stage; decide from a running game.
- **When bot-composition's topology lands**, both frames are redrawn and their sidecars rewritten.
  The code in this document does not change for it: sockets are ids and kinds, and the stage draws
  whatever the sidecar places.
