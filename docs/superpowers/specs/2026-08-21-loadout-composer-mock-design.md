# Loadout Composer — Design (concept mock)

Date: 2026-08-21
Status: Draft

> **Presentation superseded** by `2026-09-14-glass-console-loadout-editor-design.md`. The socket
> rows, the part palette and drag and drop are replaced by a drawn frame with a box per socket and
> a picker beside the selected box, and *part* is renamed **fitting**. The rules, the sample
> content, the verdicts and the *Not built* list below still hold.

## Goal

Replace the `robotics` placeholder with a **concept mock** of the loadout composer: the player picks
a robot frame, fits a part into each of its sockets, and reads a live stat rollup and build cost as
they do it.

It answers one question — *is composing a robot by filling sockets, judged against a live rollup,
pleasant to use?* — before the subsystem underneath it is designed. Nothing it authors is executed,
persisted, or seen by the simulation, and no kernel code changes.

This is deliberately the opposite split from the programming view. That spec shipped an editor and
its runtime together, because an editor that edits nothing is a placeholder with more pixels. Here
the runtime is not merely unbuilt but **unspecified**: there are no robot instances, no socket
storages, no fitted-equipment tier and no refit tasks anywhere in `Dimenship.Core`, and
`2026-08-20-recycling-refit-and-construction-design.md` — the document that would specify them — is
itself unbuilt and carries open vocabulary items. Building the surface first is how we find out
whether the surface is worth the subsystem.

## Source material

- **`docs/Game Design v0.9.md` §5.10** — the material model and the vocabulary. A fitted module is an
  item occupying a socket; a machine's effective rates come from what its sockets hold; a machine
  with an empty socket runs at its **unequipped rating**. Authoritative.
- **`docs/Game Design v0.9.md` §7.4** — "Detailed loadout editing and doctrine management remain in
  the Robotics screen." This spec is the first half of that screen.
- **`docs/Game Design v0.9.md` §10** — the MVP content budget: "Two robot frames plus tool, sensor,
  storage, power/defense, basic investigation module, basic weapon/armor package." The sample data
  below is exactly that list and no wider.
- **`docs/superpowers/specs/2026-08-20-recycling-refit-and-construction-design.md`** — how a real
  refit would work (sockets are storages, install and remove are `TransportTask`s). Binding on what
  this mock must **not** imply, and the reason it shows a template rather than a robot.
- **`docs/superpowers/specs/2026-08-11-programming-view-design.md`** and
  **`2026-08-02-visual-style-system-design.md`** — the concept-mock precedent and the box
  vocabulary. Binding without restatement: the zone model, the panel contract, the palette rule,
  and the rule that a box whose source does not exist is not built rather than stubbed.

## Vocabulary

Three of the four words in play are already taken by something else in this repository, so they are
fixed here before any code is written.

| Word | Meaning here | Why not the obvious alternative |
| :--- | :--- | :--- |
| **Loadout Template** | The authored, named, reusable thing the player creates and edits. | GDD §7.4 and §8.2 say "loadouts… templates". *Design* is not a GDD word, and neither is *blueprint*. A template is not bound to a robot: there are no robots. |
| **Socket** | A position on a frame that holds one part. | GDD v0.9.1 adopted **socket** precisely because `slot` already names an authored facility node position (`FacilityState.BuiltAtStart`). Using *slot* here would collide with the schematic. |
| **Part** | What is fitted into a socket. | **Not "module".** `module` is a shipped bulk commodity — `"id": "module"`, *Robot Module*, `holdCapacity` 120000, produced by `assemble_modules` — and the GDD glossary flags the collision between it and *Fitted Module* as an open naming item. A mock is not the place to close a vocabulary question the GDD has open; it takes a third word and says so. When the GDD settles the name, this view renames with it. |
| **Frame** | The chassis a template is built on. | GDD §10, and the shipped `robot_frame` item, which the build cost spends. |

The panel keeps the title **"Robotics"** and the identifier `robotics`. The title is what
`ShellRoot` sorts focus views by for the `Ctrl+1..9` accelerators, so keeping it leaves Base Graph
1, Processes 2, Programs 3, Robotics 4 exactly where players already have them; the identifier is
written into `user://layout.json` and renaming it would reset saved layouts to gain nothing.

## What is real and what is not

The mock is honest about which half is which, because a mock that blurs the line teaches its
reviewer the wrong thing.

**Invented, and forgotten when the game closes:** frames, sockets, parts, their stats, their costs,
the templates themselves. There is no persistence, deliberately — a template that survived a restart
would be a save format, and a save format for a subsystem that does not exist is the thing this mock
is trying to avoid committing to.

**Real:** the vessel's material stock. The build-cost box reads `WorldSnapshot.Resources`, which
already carries vessel-wide amounts per item, and reports what the vessel actually holds against
what a template would cost. This is why `OnSnapshot` has a body here where `ProgramsFocus` leaves it
empty: an affordability readout against a made-up number would be the one part of the mock a
reviewer could not trust.

## The composer

Three columns, on the programming view's skeleton: two nested `HSplitContainer`s, so the player can
trade palette width for composer width, with the offsets session-local. `LayoutState` describes
zones and never the interior of a focus view.

**Left — the template library.** The templates, a `+ NEW TEMPLATE` button, and a *Selected Template
Info* box: frame, sockets filled *n/m*, verdict.

**Centre — the composer.** A header carrying the template name, its frame, its verdict chip, and a
disabled `QUEUE BUILD` button with the reason beside it in words — `CONCEPT — NOTHING IS BUILT FROM
THIS` — the way `ProgramsFocus` states its disabled `ACTIVATE` rather than leaving a greyed button
for the player to guess at. Then the frame selector, then one row per socket, then the rollup.

**Right — the part palette.** Search, a socket-kind tab strip, and rows that are drag sources.
Selecting a socket filters the palette to the kind that socket accepts. It lives inside the focus
zone rather than the inspector zone for the same reason the block palette does: a palette the player
can replace with the Energy Budget panel is a palette that will be missing exactly when it is
needed.

### Fitting a part: both gestures

Click and drag both work, and they are not redundant.

- **Click**: select a socket, the palette filters to its kind, click a part to fit it. Keyboard- and
  trackpad-friendly, and it is the gesture that survives when the list is long enough to scroll.
- **Drag**: a `LoadoutDragData : RefCounted` payload — a `RefCounted` because Godot's drag payload
  is a `Variant`, which carries a `GodotObject` and not a plain C# object — carrying either a part
  off the palette or the part currently in a socket. A socket accepts a drop only when the kinds
  match; dragging a fitted part onto another socket of the same kind swaps the two; dragging one
  back to the palette removes it.

Kind-matching is enforced at the drop, not reported afterwards: an incompatible socket simply does
not highlight. A validation message for a state the editor can refuse to enter is a message the
player learns to ignore.

### Empty sockets are legal

A template with an empty socket is **incomplete, not invalid**, and is edited and kept like any
other. This follows GDD §5.10 directly: a machine missing a part runs at its unequipped rating, and
the refit spec makes the same point about a cancelled refit — the empty socket is a legible
consequence, and guarding against it would require exactly the transactional object both documents
avoid. The rollup shows the frame's baseline for that stat and says which socket is empty.

### The rollup, with attribution

Six stats: **mass, power, durability, cargo, scan, work rate**. Every row shows the frame's baseline
and one line per fitted part naming what it contributed, then the total. Attribution rather than a
single total column, because the question the composer exists to make answerable is *which part is
costing me this*, and a lone total makes the player derive it by removing parts one at a time.

**Power is a net budget**: a core supplies positive, everything else consumes negative. A template
whose net is below zero is over-budget. This is the only invented constraint in the mock and it
earns its place — without a constraint, composing is shopping, and shopping is pleasant no matter
how the interface is arranged.

Verdicts, shown as a chip and never by colour alone: `COMPLETE`, `INCOMPLETE` (a socket is empty),
`OVER BUDGET` (net power below zero).

### Build cost

The frame's own cost plus every fitted part's, summed per item, in milli-units of the shipped items
only — `basic_metals`, `technical_materials`, `component`, `module`, `robot_frame`. Each row carries
its icon from the existing `item` domain, the amount required, the amount the vessel holds from the
snapshot, and `SHORT n.n` where it holds less.

Costs name the bulk `module` commodity as an ingredient, which is correct and worth stating plainly:
the fungible thing a Factory assembles is a legitimate input to building a part, and it is a
different concept from the part itself. This is the collision the vocabulary table exists to keep
visible.

## Sample content

Two frames, per GDD §10. Stats are plain integers in the mock's own units; item quantities are
milli-units, as everywhere else.

| Frame | Sockets | Baseline mass / power / durability / cargo / scan / work | Cost |
| :--- | :--- | :--- | :--- |
| Surveyor Frame | Tool, Sensor, Power, Investigation | 800 / 0 / 400 / 0 / 0 / 0 | 1 robot_frame, 4 component |
| Hauler Frame | Tool, Cargo, Power, Defense, Sensor | 1400 / 0 / 700 / 200 / 0 / 0 | 2 robot_frame, 6 component |

Two to three parts per socket kind — enough that every socket is a choice and no socket is a menu.

| Socket | Part | Mass | Power | Dur | Cargo | Scan | Work | Cost |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| Tool | Mining Head Mk1 | 180 | −220 | — | — | — | +400 | 6 basic_metals, 2 component |
| Tool | Salvage Cutter | 140 | −260 | +40 | — | — | +300 | 4 basic_metals, 3 component |
| Tool | Precision Manipulator | 90 | −120 | — | — | +40 | +180 | 2 basic_metals, 4 component, 1 module |
| Sensor | Basic Sensor Array | 60 | −90 | — | — | +250 | — | 3 technical_materials, 2 component |
| Sensor | Deep Scan Array | 110 | −210 | — | — | +520 | — | 6 technical_materials, 4 component, 1 module |
| Sensor | Phase Resonance Probe | 70 | −340 | −40 | — | +400 | — | 8 technical_materials, 3 component, 2 module |
| Cargo | Standard Cargo Pod | 120 | — | — | +600 | — | — | 8 basic_metals, 1 component |
| Cargo | Expanded Hold Pod | 260 | −40 | — | +1400 | — | — | 14 basic_metals, 3 component |
| Power | Power Core Mk1 | 150 | +600 | — | — | — | — | 4 technical_materials, 3 component |
| Power | Power Core Mk2 | 210 | +1100 | — | — | — | — | 8 technical_materials, 5 component, 1 module |
| Power | Endurance Cell | 320 | +800 | +60 | — | — | — | 6 technical_materials, 4 component |
| Defense | Ablative Plating | 300 | — | +450 | — | — | — | 12 basic_metals |
| Defense | Countermeasure Pod | 130 | −180 | +180 | — | — | — | 5 technical_materials, 4 component, 1 module |
| Investigation | Evidence Collector | 80 | −120 | — | — | +120 | — | 3 technical_materials, 3 component |
| Investigation | Forensic Sampler | 110 | −160 | — | — | +200 | — | 5 technical_materials, 4 component, 1 module |
| Investigation | Contradiction Logger | 50 | −90 | — | — | +80 | — | 2 technical_materials, 5 component |

The library opens on three templates, chosen the way `ProgramLibrary`'s are — so that every state the
view can show is on screen before the player edits anything, and so the composer is never judged
against an empty canvas:

1. **Survey Pattern A** — Surveyor, every socket filled, power positive. `COMPLETE`.
2. **Deep Prospector** — Surveyor, a Deep Scan Array and a Phase Resonance Probe over a Power Core
   Mk1. `OVER BUDGET`.
3. **Salvage Hauler** — Hauler with its Defense socket empty. `INCOMPLETE`.

## Where the code lives

`dimenship/scripts/ui/focus/loadouts/`, in the **Godot assembly**, for the two reasons
`ProgramModel` records: the model is mutable classes because an editor over immutable records needs
a tree rewrite on every edit with no usability payoff at this stage, and living outside
`Dimenship.Core` means the spike cannot break the tested kernel and reverts in one commit. When the
real system ships, the model moves to `Core` and becomes records.

The rollup maths could have gone to `Dimenship.Shell`, where it would be unit-testable. It does not,
because the sample content would have to travel with it and would then be shipped, tested content
describing a subsystem that does not exist — an invented catalog with a test suite is a stronger
claim than this mock is entitled to make.

`ProgramBox` moves up to `dimenship/scripts/ui/BoxSection.cs` and is renamed. It was always the
generic second primitive of the box vocabulary rather than a programming-view type, and this view
wants it four more times; a copy would be how a vocabulary stops being one.

## Not built

Stated here and in the view's own doc comment, so nothing about the mock reads as a commitment:

- No robot instances, no roster, no wear or damage, no docked or deployed state.
- No refit: no socket storages, no `TransportTask`s, no reverse runs, no recall. Every one of those
  belongs to `2026-08-20-recycling-refit-and-construction-design.md` and none of them exists.
- No persistence, and no save format.
- No production: `QUEUE BUILD` is disabled, because there is no build task to queue and no
  construction unit to queue it on.
- No change to `Dimenship.Core` or `Dimenship.Shell`.
- No new icon domain. Socket kinds borrow existing `status` glyphs; a `part` domain is a follow-up,
  and `IconSlot` reserves its space either way.

## Open items

- **The name of the equipment tier.** *Part* is a placeholder for the GDD's unresolved *Fitted
  Module* / *Robot Module* collision. This view renames when the GDD decides.
- **Whether a template is the right unit at all.** The alternative is composing a specific robot
  directly, with no reusable template layer. The mock exists partly to make that question
  answerable by trying one of them.
- **Capability tags.** GDD §10 gates a story operation on "a specific investigation module" and on
  "improved sensors, stabilization, or basic drone defense". Those are capability flags, not stats,
  and they are omitted here because no mission system reads them.
