# The Console Typeface — Design

Date: 2026-10-20
Status: Built. Godot 4.7.1 generated the `.import` sidecars (`--headless --import`). The Godot
assembly builds, all three suites pass, and the shell loads all three faces without a warning.
The look was checked once in-engine at 1920 × 1080 under the Compatibility renderer. The full
section-H matrix has not been run.

## Goal

Choose the console's typeface, which issue #3 has deferred since the first shell slice. The
project owner set the order of priorities for the choice: **how it looks first, then how easy it
is to read, then how simple it is to implement.** That order rules out picking the font that is
easiest to wire in and calling it done.

## Source material

- `docs/superpowers/specs/2026-07-28-ui-shell-design.md`, open item 4. It named JetBrains Mono as
  the default candidate, and asked for one embedded OFL monospace at four sizes, with uppercase
  labels tracked at `0.14em`.
- `docs/superpowers/specs/2026-08-02-visual-style-system-design.md`, item 6. It deferred the family
  to #3 and named sizes and roles only.
- `docs/superpowers/specs/2026-10-19-ux-review-adoption-design.md`, section H. Readings should use
  tabular numerals, and if the default font lacks them, an open-licence font that has them should
  ship.
- A blind trial page, published as a private claude.ai artifact. It drew eight candidates on
  mock-ups of the shell in `ShellPalette` colours, at the shell's pixel sizes and on 220 × 128
  cards, at 100–150% scale and over the glass backdrop. Names were hidden by default.

## The candidates

| | Face | Character | Figures |
| :--- | :--- | :--- | :--- |
| A | Open Sans SemiBold | Godot's built-in default, and the baseline | Equal width |
| B | JetBrains Mono | Shell spec's default candidate | Monospaced |
| C | IBM Plex Sans Condensed + IBM Plex Mono | Industrial engineering sans, matching mono | Equal width in both |
| D | B612 + B612 Mono | Airbus cockpit display face | Equal width |
| E | Barlow Semi Condensed | Road-sign grotesk | Proportional, has `tnum` |
| F | Share Tech Mono | Sci-fi console mono, one weight | Monospaced |
| G | Oxanium | Squared spaceship sans | Equal width |
| H | Atkinson Hyperlegible Next + Mono | Built so look-alike characters cannot be confused | Mono for readings |

Chakra Petch, Rajdhani and Saira were left out: none of them has equal-width figures or `tnum`, so
a counter would shift sideways as its digits changed.

## Decisions

1. **C, chosen by the project owner.** The faces are IBM Plex Sans Condensed (SemiBold and
   Regular) and IBM Plex Mono (Medium), version 2.3 of Mono and 1.3 of Sans Condensed, from the
   `google/fonts` repository. They are under the SIL OFL 1.1, whose text ships beside them in
   `dimenship/assets/fonts/OFL.txt`. The files are unmodified, as the licence's Reserved Font
   Name ("Plex") requires of anything distributed under that name.
2. **Three roles, not the one monospace the shell spec asked for.**
   - **Labels** are Sans Condensed SemiBold, and are the theme's default font, so no site marks
     them. Labels are the uppercase captions most of the console is made of. The condensed width
     is what keeps them inside a 220 px card, and SemiBold keeps the weight players already see
     in Open Sans SemiBold.
   - **Prose** is Sans Condensed Regular, through the `Prose` type variation. Prose is a sentence
     read as a sentence. There are few: a facility's purpose, a refusal, a placeholder panel's
     body.
   - **Values** are Plex Mono Medium, through the `Value` type variation. Values are readings that
     change while the player watches: the clock, counts, quantities and percentages, plus the
     event log, whose five-character codes assume a fixed-width face.

   The trial showed C in exactly this form, and C was chosen for how it looked in that form. One
   monospace for everything was candidate B, and it was not chosen.
3. **A site marks a role and never names a font.** The three paths live in `ShellPalette`, and
   `ShellTheme.Build` is the only code that loads them, the same arrangement colours already have.
   Changing a face is one line.
4. **Tabular numerals need no `tnum` feature.** Both families draw every digit at the same width
   by default, which closes UX-02's tabular-numerals item for every label marked `Value`.
5. **A face that does not load falls back to Godot's own font with a warning, never an error.**
   A content failure is fatal because content decides what the vessel does. A typeface only
   decides how it looks.
6. **Missing glyphs fall back to the system font.** Plex lacks `✕` (U+2715), which two
   remove-buttons use, and the Sans lacks `─` (U+2500), which only the event log uses and the
   Mono has. Godot's dynamic fonts allow system fallback by default, which is presumably how
   Open Sans showed `✕` before this change. Use a different glyph only if a check shows a box.

## Not done

- **The zero.** Plex Mono's default `0` has a dot in it. In-engine at `FontMicro` (10 px) that
  dot can make `3600.000` read as `3688.888`. `ss03` slashes the zero instead. `ss04` makes it
  plain, but then it is the same outline as the letter `O`. Either would be a `FontVariation` with
  that feature, wrapped around the Mono in `ShellTheme.Build`. The owner chose C with the default
  zero, so the default stays until they decide otherwise.
- **Label tracking (`0.14em`).** It is still blocked, now on a decision rather than on the family:
  the trial offered it as a toggle, and nobody has chosen it yet. A `FontVariation` with
  `spacing_glyph` on the default font is where it would go.
- **Readings outside the main play surfaces.** These surfaces mark their readings: the status bar,
  the cards, the resource strip, the energy panel, the inspector and `BoxSection.Row`. Operations
  and the two concept mocks still draw their quantities in the label face. Each is a one-line
  `ThemeTypeVariation` at the site, done when that surface is next touched.
- **The start screen.** It has no theme and keeps Godot's font at its scene-authored sizes.
- **The full in-engine check.** One 1920 × 1080 frame was compared before and after, under the
  Compatibility renderer. Section H's matrix in `2026-10-19-ux-review-adoption-design.md` is still
  to run, card overflow included: the maximum interface scale, the backdrop off, the inspector at
  its minimum width, and Operations.
