# Settings — Design

Date: 2026-08-20
Status: Draft

## Goal

Give the player one settings surface, reachable from the start screen and from inside the shell,
carrying three tabs: **Gameplay** (a stub), **Graphics** and **Sound**. Settings persist across
runs and take effect when they are changed, not on the next launch.

This document specifies a preferences system and the audio bus layout it drives. It introduces no
simulation system, no command against the vessel, and no data the snapshot carries. The kernel is
untouched: `Dimenship.Core` gains nothing, and no setting reaches a save.

## Source material

`docs/superpowers/specs/2026-07-27-start-screen-and-solution-design.md` §138 recorded the decision
this document reverses:

> Settings → none; the button ships `Disabled = true` until a settings screen exists. A visibly
> inactive button is honest; a button that silently does nothing is not.

Line 176 of that spec lists "Settings screen, audio, save/load…" among its non-goals. Those were
non-goals of *that milestone*. The button's disabled state was always a placeholder for this work,
and the honesty rule it stated is the one this document inherits: nothing in the menu may be a
control that appears to do something and does not.

Two facts about the repository at the time of writing shape everything below:

1. **There is no audio.** No `AudioStreamPlayer`, no `AudioServer` call, no bus layout resource, no
   sound asset. The Sound tab is the first audio code in the project.
2. **There are no dialogs.** The only popup in the codebase is `MenuButton.GetPopup()`. There is no
   modal pattern to conform to, so this document establishes one.

## Decisions

### 1. The state lives in `Dimenship.Shell`, not in `Dimenship.Core`

`SettingsState` and its serializer sit beside `LayoutState` and `LayoutSerializer`, which are the
same problem solved once already: engine-free data, a degrading JSON reader, and a thin Godot-side
file wrapper. `Dimenship.Core` is the wrong home twice over — a settings record is not part of the
simulation, and `CoreAssemblyTests` forbids the engine types and the floating-point a naive volume
setting would reach for.

**Rejected:** putting settings in the save file. A save is a world; settings are preferences about
*this machine*. A world carried to another computer must not bring a resolution with it.

### 2. Ratios are permille, as everywhere else

Volume and interface scale are integers out of 1000. This matches `WorkRatePermille`,
`CapacityPermille` and `IntegrityPermille`, and it keeps `settings.json` integer-only — which
matters because it is the one file a player is likely to open in a text editor.

Decibels were rejected for the stored value: a slider's travel is linear in amplitude, so a file
holding decibels would be unreadable to whoever opens it and non-linear to whoever edits it. The
conversion to decibels happens once, in `SettingsApplier`, at the point the mixer is written to.

### 3. Every DTO field is nullable — unlike `LayoutSerializer`'s

`LayoutSerializer` takes its numeric fields non-nullable and clamps whatever arrives. That is safe
there because a missing split offset deserializes to `0`, which is a legitimate offset.

It is not safe here. A missing volume would deserialize to `0` and the player would get a silently
muted game rather than a reported problem. So every field is nullable and a missing one warns and
falls back, following the rule the save format already states: *a missing field is a reported error
rather than a silent default*.

An absent **group** warns once rather than once per field, because a settings file written before a
whole group existed is one thing that is absent, not five.

### 4. The window mode is stored by name

`"Windowed"`, not `0`. Reordering the enum would otherwise re-point every existing settings file,
and the file is meant to be legible. An unrecognised name warns and falls back to the default for
that field only; a name spelled as an ordinal (`"17"`) is rejected, because `Enum.TryParse` accepts
it and would otherwise produce a mode no `switch` has a case for.

### 5. The menu is a `Control` over the scene, not a `Window`

The frosted-glass shader samples the backdrop against `SCREEN_UV`. A real `Window` owns its own
viewport, so inside one the shader would sample nothing and the settings menu would be the single
surface in the game that does not look like the game.

It carries its own `Theme` because the start screen has none of its own — that scene predates
`ShellTheme` and still paints its background from a literal. Carrying the theme is what lets one
menu serve both hosts unchanged.

### 6. Changes apply and persist immediately. There is no OK and no Cancel

This matches the window layout, which already persists on every action. A menu that asks the player
to confirm a volume slider they have already heard take effect is asking about something already
decided.

The one refinement: a slider **previews** without writing while it is being dragged and writes once
the drag ends, for the reason `ShellRoot.BuildTree` gives about split-container drags — a write per
mouse-motion frame is a disk write per frame. `Settings` therefore tracks what is *applied* and what
is *on disk* separately, so a preview followed by a commit cannot be mistaken for a no-op and
dropped.

**Rejected:** a confirmation timer on resolution changes ("keeping this in 15s…"). It guards
against a mode the display cannot show. Resolution is offered only in windowed mode and only from
sizes that fit the current screen, which removes the failure the timer exists to catch.

### 7. Toggles are two-state buttons, not `CheckButton`s

Godot's `CheckButton` is a rounded switch drawn from its own textures — the one shape in this
console that would carry a colour the palette never chose. A `Button` in toggle mode already wears
the shell's styling, and it says which state it is in **in a word**, which is what the rule against
state carried by colour alone asks for.

`OptionButton` needs no theme entry: it derives from `Button` and Godot resolves theme items
through the class chain, which is why the existing menu-bar `MenuButton`s already wear the shell's
styling. `HSlider` is the one genuinely new control, and its handle is the one place the engine
insists on a texture rather than a stylebox — so the texture is generated from a palette colour at
theme-build time rather than shipped as an asset that could drift off the palette.

### 8. The backdrop governs the frost

The frost is a blur *of* the backdrop. With the backdrop off there is nothing to blur, and a frost
that kept drawing would leave the panes as the only place the nebula still showed. So:

- Turning the backdrop off hides every `FrostPane` and disables the frost row, with the reason
  shown beside it — a word, not a greyed control the player has to guess at.
- The two remain **separate stored fields**, so turning the backdrop back on restores the frost
  choice the player actually made rather than silently resetting it.

Each `FrostPane` answers the setting for itself rather than being switched off by a walk of the
tree, so a panel mounted after the change is already correct.

### 9. Gameplay is a stub, and says so

The tab exists and is empty, in the register `PlaceholderPanel` uses for the focus views that do
not exist yet: a heading, and a sentence naming what will live there — difficulty, autosave
cadence, auto-pause on a critical alert — and why none of it is offered.

**Rejected:** shipping a difficulty dropdown and an autosave interval that persist to a file
nothing reads. That would look finished and be a lie, which is the failure the start-screen spec's
disabled-button rule was written to prevent.

Note that `AutoPauseOnCriticalAlert` already exists as a *saved world* preference. It is not moved
here: it is a property of a world in progress, and a global default for it is a separate decision.

### 10. Audio buses ship with the setting that drives them

`Master`, `Music` and `SFX`, in `res://default_bus_layout.tres`. Nothing plays into them; the
sliders move real `AudioServer` volumes and are silent because the game is silent.

This is the honest version of the alternative in §9's rejection: the controls are real, the thing
they control is real, and what is missing — the audio itself — is stated in the tab rather than
left for the player to discover by turning everything up.

`AudioBuses.Ensure()` creates a missing bus at runtime with a warning, taking the same stance
`ShellBackdrop` takes towards a missing image: a missing asset degrades to something that works
rather than to a crash.

### 11. The menu suspends the accelerators, from `ShellActions`

`ShellActions` gains a `Suspended` flag its `Handle` checks first. The suspension lives there
rather than in the shell's input override because the accelerator table lives there: a modal that
had to know *which* keys to swallow would be a second copy of that table, and the two would drift.

Escape closes the menu, handled in `_Input` and marked handled so it beats
`ShellRoot._UnhandledInput`, where Escape already means "release GUI focus". One key cannot mean two
things while a modal surface is up.

## Out of scope

- Any gameplay setting with a system behind it (difficulty, autosave), pending those systems.
- A rebindable key map. `project.godot` has no `[input]` section and every accelerator is a
  hard-coded keycode in `ShellActions.Handle`; rebinding is its own design.
- Sound itself — music tracks, effects, a mixer UI. This ships the buses, not the audio.
- Effect-level graphics settings (anti-aliasing, shadow quality). The renderer is `mobile` and the
  console draws flat 2D; the backdrop image and the frost shader are the only two rendering costs
  the shell actually has, and those are the two switches offered.
- Accessibility settings beyond interface scale (colour-blind palettes, reduced motion). Nothing in
  the shell animates, and the palette question belongs with the typography work in issue #3.
