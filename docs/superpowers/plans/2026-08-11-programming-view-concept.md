# Programming View — Concept Mock

Date: 2026-08-11
Status: Built, unverified — see *Verification*

## What this is

A playable mock of the programming view, built to answer one question before
`2026-08-11-programming-view-design.md` is executed: **is authoring a rule by dragging blocks into
an indent ladder pleasant to use, or is it fighting the mouse?**

The full spec ships a program model in `Dimenship.Core`, three kernel changes, a deterministic
runtime in phase 0 of the tick, a validator, telemetry, and the editor. The editor is the last
thing in that list and the only thing whose value is uncertain. If dragging blocks around is
unpleasant, everything underneath it has been built for the wrong surface.

So this builds the editor and nothing else. **It authors programs and nothing executes them.**

## What is in it

- The three columns inside the `doctrine` focus zone: library, editor, palette.
- The indent ladder — `RULE` / `WHEN` / `THEN` / `IF` / `ELSE IF` / `ELSE` — nested to any depth,
  with a fixed gutter and a vertical rule per branch body.
- Drag and drop: palette templates into a body, existing blocks reordered and re-parented, and
  illegal drops refused rather than guessed at.
- Every slot editable: target dropdowns, comparison, and bounded number fields that clamp.
- Delete, and undo/redo bounded at 64 entries, so a botched drag is recoverable.
- Three seeded programs, commanding only what `WorldDefinition.CreateDefault` contains.

## What is not in it

No `ProgramRuntime`, no tick phase 0, no conflict resolution, no telemetry. No `ProgramValidator`
and no `IssueCode`. No `program_dock` — no validation, trace or metrics boxes. No kernel changes:
task `Priority`, `Reservation` and the `SafetyLock` fix are all still unwritten, so `Dimenship.Core`,
`Dimenship.Shell` and both test projects are untouched by this change. No persistence, no
parameters tab, no activation.

`ACTIVATE` renders disabled with its reason in words beside it: there is no runtime to activate
into. That is the honest statement of where the system stands, and it is what the button will say
until phase 0 exists.

## Divergences from the spec

Three, each taken to get feedback sooner, each of which the real build should drop.

1. **The model is mutable classes in `dimenship/scripts/ui/focus/programs/`, not immutable records
   in `Dimenship.Core/Programs/`.** An editor over immutable records needs a tree-rewrite path on
   every edit — real work with no bearing on the question being asked. Keeping it out of Core also
   means the spike cannot break the tested kernel and reverts in one commit. The shapes match the
   spec one for one, so moving them to Core and making them records is mechanical.

2. **The ladder is Godot containers, not a measured canvas.** The spec puts `BlockPath`,
   `BlockLayout.Measure` and `DropTarget.Resolve` in `Dimenship.Shell` as engine-free integer
   arithmetic with tests. That is right for a custom-drawn canvas. Here `RuleCanvas` flattens the
   statement tree into a list of rows, each carrying its own depth, and Godot lays them out — which
   is also what keeps the gutter at a constant x while the rows indent past it.

3. **Drop resolution is per-control, using Godot's native `_GetDragData` / `_CanDropData` /
   `_DropData`.** Each block decides from the pointer's local Y whether a drop lands before it or
   after it; a condition row takes conditions only; each body carries a `BlockDropStrip` that
   appends. There is no global resolver.

Divergence 2 and 3 exist together for one reason worth recording: neither `dotnet` nor Godot was
available in the session that wrote this, so every line was written blind and compiled for the
first time on the project owner's machine. Pixel arithmetic that cannot be run is worse than no
pixel arithmetic.

## Design decisions the mock makes

Several small questions had to be answered to make the editor work at all. None of them are binding
on the spec, but they are what the mock is testing:

- **A block is dragged by its keyword cap or its padding, never by a slot.** A slot's own control
  takes the press, so clicking a dropdown opens it rather than starting a drag. This is the single
  most likely thing to feel wrong, and it is the reason the caps are as wide as they are.
- **Dropping a condition into a body creates a branch around it.** `IF <condition> THEN ⟨empty⟩` is
  what the player wanted; making them drag a branch and then a condition into it is two motions for
  one thought.
- **Only the leading `IF` is a drag source for its branch.** Dragging an `ELSE IF` and having the
  arms above it come too would be the canvas moving something the player did not grab.
- **Every body carries a drop strip**, whether or not it is empty. An empty branch would otherwise
  be a target the player can see and cannot hit.
- **An unreachable `ELSE IF` and an empty branch are drawn, not refused.** The validator that would
  flag them is not built; the editor does not pretend to be it.

## Verification

`dotnet build DimenshipGame.sln` and `dotnet test` were **not run** — neither tool exists in the
environment this was written in. Expect compile errors on the first build.

Once it builds, in Godot: open `Shell.tscn`, press `Ctrl+3` — **the accelerator moved**. `ShellRoot`
builds `FocusOrder` with `OrderBy(d => d.Title)`, and `Programs` sorts after `Processes` where
`Doctrine` sorted before it, so this view is `Ctrl+3` and `Processes` is now `Ctrl+2`. The panel
identifier is still `doctrine`, so saved layouts keep working.

Then the things worth judging:

- Drag a condition from the palette into a rule body; drag an action onto a condition row and watch
  it refuse.
- Drag a block from one branch arm into another, then `Ctrl+Z`.
- Open a target dropdown: it offers Main Hold and Smelter Buffer and nothing else, because that is
  what the vessel has.
- `Delete` on a selected `IF` removes the whole branch.
- Tab traversal reaches blocks, slots and palette rows, and focus is visible on each. Nothing glows.

## Next

If the ladder reads well and the drags land where they are aimed, the spec's build order stands
unchanged and this folder is deleted as its steps 5 through 7 replace it. If it does not, the thing
to change is the editor — and it will have cost one commit rather than the runtime underneath it.
