# Adopting the UI/UX Review — Design

Date: 2026-10-19
Status: Part 1 built (2026-10-19). On 2026-10-20 it compiled and all three suites passed, but it
has not been checked visually. Part 2 is designed and not built, except for H's tabular numerals,
which are done. Its owner decisions are marked **Owner**.

## Goal

The 2026-10-19 UI/UX review (`docs/reviews/2026-10-19-ui-ux-review.md`) asks three questions for
a new player: *what am I trying to achieve, what should I do next, and how will I know it
worked?* Its eleven recommendations were checked against the code, and the adjudication closes
that review. This design does two things:

1. **Part 1** records what was built at once: the shell-only items that need no kernel or content
   change, and the reasoning behind each.
2. **Part 2** designs everything else, in delivery order. For each item it gives the decisions an
   implementer would otherwise make differently, and the questions only the project owner can
   answer.

## Source material

- `docs/reviews/2026-10-19-ui-ux-review.md`, the review and its adjudication.
- CLAUDE.md, *The Godot shell*: no hard-coded colour, every command through `ShellActions`, the
  snapshot replaced wholesale, and panel ids never renamed.
- U3 (`ExecutorCondition`: only output that cannot be put down is blocked), K8 (`WaitCause`,
  `AlertCode.PlanWaiting`), and U4
  (`docs/superpowers/specs/2026-10-11-playable-inspector-and-move-control-design.md`).
- `docs/superpowers/specs/2026-09-16-editable-production-plans-design.md`: a draft is never saved.
- `docs/superpowers/specs/2026-09-13-vessel-construction-interface-design.md`: the whole-card
  unbuilt alpha, one silhouette and no second ramp.

## The colour vocabulary

UX-06 asks for shared meanings. Part 1 applies them, and Part 2 must keep to them. A colour never
appears without a word or glyph beside it.

| Colour | Means | Examples |
| :--- | :--- | :--- |
| `StateOk` | Work happening or done | RUNNING, DONE, BUILT, NOMINAL |
| `Accent` | Selection and focus; "this is the one you are on" | the active rail entry, a selected card's border |
| `TextPrimary` | Routine fact, or work held by a decision | QUEUE, PLAN, HELD, OUTRANKED, a changeover in the log |
| `StateWarn` | Not producing, for a reason that resolves itself or wants a look | waiting for input or power, a plan waiting behind another, a shortage |
| `StateFault` | Output that cannot be put down, or a kernel fault | a frozen belt, DESTINATION FULL, the driver's fault message |

A postponement shown on its own takes `Conditions.Colour(reason)`. That is the colour
`Conditions.Standing` gives a facility in the standing the reason produces, read through
`UtilizationWindow.CategoryOf`, so the log, the task rows and the cards cannot disagree.

---

## Part 1 — Built

### 1. Time and health are two readings; the kernel's alerts reach the screen (UX-04)

The status bar used to say NOMINAL whenever no kernel fault had occurred, beside a red count it
derived itself. `WorldSnapshot.Alerts` was read nowhere in `dimenship/scripts`, so
`AlertCode.PlanWaiting` was raised and seen by nobody.

- **Time** is its own label, PAUSED or RUNNING ×N, drawn in ordinary text. Pausing is not a
  warning.
- **Health** is a word and a glyph:
  - the driver's fault message, red;
  - BLOCKED, red, while anything is blocked by U3's rule;
  - CRITICAL, red, for a critical kernel alert (nothing raises one yet);
  - ATTENTION, amber, while only warnings are live;
  - NOMINAL otherwise.
- **The attention summary** is a button: "2 BLOCKED · 1 PLAN WAITING". The items are blocked
  facilities, blocked lines, then live, unacknowledged kernel alerts, in that order. Each press
  opens the next item and walks the list.
  - A plan opens in Operations, through `OperationsRequested`.
  - A facility or line is selected, inspected, and the base graph is shown again. Showing it again
    remounts the graph, which reads the selection on mount.
- **Double counting.** An `ExecutorBlocked` alert is skipped, because the derived reading already
  covers it. A waiting plan stays its own item even when the plan it waits behind is the one a
  blocked facility runs, because the two need different controls.
- The summary is recounted when the snapshot reference changes, not every frame.

### 2. Colour meanings applied (UX-06)

- The rail's active entry is `Accent`, and the other entries are `TextPrimary`. Zone headings are
  `TextPrimary`. Both used `StateWarn`.
- **The event log** colours by standing:
  - every postponement through `Conditions.Colour`;
  - routine events (queue, priority, hold, release, cancel, move, claim, changeover) in plain
    text;
  - shortages, power and raised alerts in amber;
  - a production `AllTasksBlocked` reads STALL in plain text, because its postponement line
    beside it names the cause in that cause's colour, and it may be a wait for ore. A logistics
    `AllTasksBlocked` is a frozen belt and reads BLOCK in red.
  - The table now covers every `EventCode`, including the twelve added since it was written (K2
    to K8). Those fell through to a colourless EVENT, against the table's own comment.
- **Task rows** in the inspector and in the plan detail take `Conditions.Colour`. The inspector's
  rows were red for every postponement.

### 3. Player words (UX-09, the shell-only part)

- `Labels.Issue` words every `DraftIssueKind`, in facility, line and storage terms rather than
  executor and endpoint. Its fallback names the kind as "UNWORDED ISSUE (…)", so a kind added to
  the kernel without a sentence is visible rather than passing for a sentence.
- `Labels.Unplannable` replaces Operations' private `Describe`, so a draft issue and a committed
  plan's unplannable entry use the same words.
- `Conditions.Describe` reads as words (MISSING INPUT, INPUT HELD FOR ANOTHER PLAN, ORDER ON
  HOLD), not enum names.
- The approve hint says what approving does: the vessel queues the work and does it over time.

### 4. Readability (UX-02, the palette part)

- `TextDim` is now `6B8EA2`, about 5.1:1 over `BgPanel`. It carries nearly every caption and
  explanation.
- `TextFaint` is now `4F6B78`, about 3.2:1. It is kept below the 4.5:1 benchmark on purpose, for
  inactive and optional readings only.
- `FontMicro` goes from 9 to 10 and `FontBody` from 11 to 12. One step only, because the graph's
  cards are fixed 220 × 128 cells. Further enlargement is the interface-scale setting's job.
- `FlowIdle` is `TextDim`, so idle edges also brighten.
- **Not verified visually.** The session that built this had no toolchain. On 2026-10-20 the
  Godot assembly compiled and a single in-engine frame at 1920 × 1080 was looked at, by then in
  the new typeface (`2026-10-20-console-typeface-design.md`). The cards, the inspector at its
  240 px minimum, and the status bar still have to be checked at 100% and at the largest
  interface scale, with the backdrop on and off. If a card overflows, the fix is in the card's
  layout, not a revert of the size.

### 5. The Operations composer survives a view change (UX-01, the Operations part)

`Zone.Show` frees the panel, and the draft lived in its fields. The inspector's VIEW PLAN round
trip, and the composer's own BACK and "+ NEW" buttons, threw away a half-composed order.

- `OperationsFocus.OnUnmount` writes an `OperationsComposerState` to `ShellContext`, and `OnMount`
  restores it. The state holds:
  - mode, build target by facility (not by index), produce target and quantity;
  - the draft, undo and redo, the selected step;
  - the approve lock, and an open plan detail.
- **Controls are restored without their change signals**, because each handler recomposes from
  scratch. The goal is re-tracked last. The first snapshot therefore refreshes the draft against
  the world through the ordinary `WorldRefresh` path, so a draft composed before the trip never
  shows stale feasibility.
- **BACK and "+ NEW" resume the draft.** DISCARD is the one control that throws it away.
- An inspector BUILD for the slot the kept draft already builds resumes that draft. A different
  slot is a different goal and recomposes.
- **Session lifetime only.** A draft is never saved, by the editable-plans design. A context
  belongs to one shell, so a new campaign starts with none.

### 6. A new campaign starts paused (UX-03, first item)

`SimulationDriver` opens at speed index 0, with the resume speed at ×1.

### 7. Previews are marked before they are opened (UX-08, first item)

`PanelDescriptor` gains an optional `PreviewNote`, a sentence rather than a flag, so a preview mark
always has something to say. The rail shows "Programs · PREVIEW" and "Robotics · PREVIEW". The note
is the tooltip, and it states the actual retention: edits last only until you leave the view.
Titles are unchanged, so the `Ctrl+1..9` order holds.

---

## Part 2 — Designed, not built

In delivery order. Each item says what to build, the decisions that constrain it, and what is
still open.

### A. Keep edits in the concept mocks (UX-01, the rest)

Programs and Robotics rebuild their sample libraries on every mount (`ProgramLibrary.Create()`).

- Park the mock's model on `ShellContext`, the way Part 1 Decision 5 parks the composer: the
  program list, the selected index, and the undo and redo stacks per selection; the loadout
  templates and their selection likewise. `LoadoutsFocus._detailsOpen` is already static, and it
  moves there too, so the pattern is single.
- The model stays in the Godot assembly, by the mocks' own terms in CLAUDE.md. Parking a mutable
  list in the context is not a reason to move it to `Dimenship.Core`.
- **Scroll position** is kept per view, as a `Vector2` beside the model. It is restored after the
  first layout, because a `ScrollContainer` clamps a value set before its content exists.
- **When built, change the preview note** (Part 1 Decision 7) to "edits last until you close the
  game". The note must always state the actual behaviour.
- **Owner:** should mock edits survive a restart? That needs a third `user://` file following the
  layout and settings conventions. The recommendation is no, until the systems are real: saving a
  mock's output invites the player to invest in a format the real system will not read.

### B. Opening guidance (UX-03, the rest)

There is no objective system. The word *objective* is reserved: the GDD uses it for story
objectives, and K6 chose not to name committed plans that.

- **Name: a briefing.** A scenario-authored, ordered list of steps. Each step has a title, one
  sentence of why, the action that starts it, and a completion test.
- **Content, not code.** The briefing goes in the scenario JSON (`briefing: [...]`), parsed and
  linked like the rest, with collected errors. A step's completion test is a closed vocabulary
  the loader checks:
  - `facilityBuilt: <executorId>`;
  - `planCompleted: { item, atLeast }`;
  - `itemInHold: { item, atLeast }`.
  These are read from the snapshot by a new `Presentation/BriefingProgress` projection: pure,
  thrown away, never stored. There is no scripting language. It must not grow into one before the
  program language exists.
- **The first briefing**, on the shipped vessel:
  1. "Commission Factory Beta." Action: select it. The inspector's BUILD is the next press.
     Complete on `facilityBuilt: factory_b`.
  2. "Order bulkheads." Action: open Operations in Produce mode on `bulkhead`. Complete on a plan
     for it completing.

  This teaches the inspector build path (U4), Operations, and that an order queues work.
- **Where it shows:** a single line above the base graph (shared with E below), not a modal. It is
  dismissable, and the dismissal is a settings preference (`SettingsState`), not world state.
- **Tier question (Owner):** is "which steps are done" world state, saved with the campaign? Or is
  it derived every time from the snapshot? Derived is recommended. Every proposed test reads
  current state, so nothing new needs saving. The cost is that a step whose condition later stops
  holding (stock spent) reads as undone again. For that reason steps are tested in order and a
  step stays done once a later one is, which is still derived.
- **Owner:** should the briefing live in `default_vessel.json`, or in a separate introductory
  scenario? The start screen offers one campaign today.

### C. Diagnosis to intervention (UX-05)

The foundation exists: `WaitCause` names the other plan, and a `DetailRow` holding a plan opens it.

- **Wherever `WaitCause` names a plan**, the row becomes "WAITING BEHIND PLAN #n — {reason}" and
  opens that plan, not the row's own. The rows are the inspector's queue row, the plan detail's
  task rows, and the attention summary's tooltip. The row's own plan stays one press away on its
  `FOR PLAN` line.
- **DESTINATION FULL** offers "INSPECT {storage}". It selects the storage through
  `SelectionChanged`, the same way the attention summary selects a facility.
- **Plan highlight on the graph.** Selecting a plan in Operations sets
  `ShellContext.HighlightedPlan`. The base graph draws that plan's facilities and lines with a
  highlight border, in `Accent` at reduced alpha so it is distinct from selection, and dims
  nothing. The set comes from a new `Presentation/PlanFootprint` projection (executors and
  transports with an unfinished task of the plan). It is cleared by Esc or by selecting nothing.
- **The camera** already survives the round trip (`GraphZoom` / `GraphPan`). Selection survives
  through `CurrentSelection`. Nothing new is needed there.
- No new commands. Everything here navigates. The controls that act stay where U1 and U4 put them.

### D. Unbuilt facilities stay readable (UX-07) — reverses a recorded decision

The construction-interface design applies `UnbuiltModulate` (alpha 0.4) to the whole card, so the
name and purpose fade with the structure.

- **Proposal:** the alpha applies to the card's body (meters, icon, frame) and not to its title,
  its PURPOSE line or its construction-phase line. Those draw at full strength. There is still
  one silhouette and no second ramp: the alpha moves, and no colour is added. The dashed outline
  stays.
- **Orderable versus cannot proceed:** the UNBUILT phase line gains the `BuildQuote` verdict:
  - "UNBUILT — CAN BE ORDERED";
  - "UNBUILT — {first blocking issue}", worded by `Labels.Issue`.
  The quote is composed per snapshot only for unbuilt slots (five on a new campaign), through the
  same `ComposeDraft` door the inspector uses. If that proves too costly at ×30, compose on a
  snapshot change only for the slot under the pointer, and leave the others reading UNBUILT.
- **Owner:** approve reversing the whole-card alpha. If declined, the fallback keeps the alpha
  and raises it from 0.4 to 0.6.

### E. The overview's attention line (UX-10)

Depends on B and on Part 1 Decision 1.

- One line above the base graph:
  - the current briefing step, if any;
  - the first attention item, worded as the status bar words it and opening the same way;
  - the next useful action, as a button: the briefing step's action, or "OPEN PLAN #n" for the
    first waiting plan.
  It is never a second panel, and it is collapsible.
- **Resources:** while a plan is highlighted (C), `ResourceStrip` orders that plan's goal and
  inputs first and dims nothing else. The strip's order otherwise stays catalog order, the
  declaration-order rule.
- **Duplicate summaries.** The review's evidence here is the concept image
  `docs/Dimenship Base Overview.png`, not the running shell. Audit the running shell first;
  nothing is consolidated until a duplicate is found there.
- The fixed layout is untouched. Attention changes emphasis, never position.

### F. Completion that says what changed (UX-11)

- **A notice line**, the same strip as E or the status bar's right end. It appears for exactly two
  events: `FacilityBuilt` and `PlanCompleted`. Nothing routine ever posts to it.
  - Facility: "{label} commissioned — {purpose}. VIEW". The purpose is the catalog's
    `FacilityArchetype.purpose`. VIEW selects it.
  - Plan: "Plan #n done — {qty} {item} delivered. VIEW". VIEW opens it.
  - It lasts until dismissed or replaced. A burst shows the latest and a count.
- **The auto-pause on `PlanCompleted`** (`SimulationDriver.PauseForCompletion`) is currently
  silent. The status bar reads PAUSED with no reason. The notice is its reason. Until F is built,
  that is a known gap.
- **Accepted versus done:** an approved plan's first notice is in Operations, not the strip: "Order
  queued — plan #n. Work starts as facilities free up." This completes Part 1 Decision 3's hint.
- **Measured improvement** ("waiting fell 40%") needs a before-and-after window per facility.
  `UtilizationWindow` has the data. Deferred until B and E land, because it is reward on top of
  comprehension.

### G. Remaining wording (UX-09)

- **Schematic labels are content.** Schematic ids show raw in the plan detail, the queue rows and
  MOVE. Add a required `label` to every schematic in `catalog/schematics.json`, `Label` on
  `SchematicDefinition`, and `Labels.Schematic`. The loader rejects a missing label, as it does
  for items. This is a catalog change, so the fidelity test and every loader test tree need the
  field.
- **Time units.** `Changeover · 12T` and the estimate read ticks. Use `Units.FormatSimTime`, since
  a tick is a second, in ordinary text. Keep the tick count in the inspector's technical rows and
  the status bar's tick readout.
- **Refusals.** Every `CommandRefused` reaching the shell must show its `Reason`. Audit the U1
  controls and MOVE for a refusal path that shows only a colour.
- The inspector's `FACILITY · {FacilityType}` subtitle reads a raw enum name. Use the archetype's
  label.

### H. Remaining readability (UX-02)

- **Tabular numerals.** *Resolved 2026-10-20 by the console typeface
  (`2026-10-20-console-typeface-design.md`): the shell now ships IBM Plex. Every reading marked
  `ShellTheme.Value` draws in Plex Mono, whose digits are fixed-width, so no `tnum` feature is
  needed. Operations and the two concept mocks have not been marked yet.*
- **Verification matrix.** At 100% and at the maximum interface scale, with the backdrop on and
  off, check the base graph cards, the inspector at its minimum width, Operations with a long
  draft, and the status bar with three attention kinds live. Record the result in a review note.
  Without it, Part 1 Decision 4 is unverified.

### I. Colour, the rest (UX-06)

- **High transport load is drawn amber** (`FlowHigh = StateWarn`). By the vocabulary above, amber
  means waiting or wants a look, and a fully loaded conveyor is working.
  - Proposal: `FlowHigh` becomes a brighter `Accent`, and the legend says "busy — not a fault".
  - **Owner:** the graph's bands were tuned by eye. Confirm before changing them.
- **The remaining `StateWarn` uses** are audited against the table. A changeover on a card stays
  amber, because the facility is not producing and the cause resolves itself. A held plan in
  the plan list stays amber, because it is stopped and wants a look.

## Not built, and not planned here

- Acknowledging an alert. `AlertState.Acknowledged` exists, nothing sets it, and the attention
  summary already skips acknowledged alerts. A control for it is a kernel command first (C0).
- An attention panel listing every item. The cycling button is enough until there are more than a
  handful.
- Player validation sessions. The review's checklist is the protocol. Run it after B, D and F,
  because before them half its tasks have nothing to find.
