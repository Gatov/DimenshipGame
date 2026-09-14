# Glass Console Loadout Editor Implementation Plan

Status: Draft

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redraw the Robotics loadout composer as a frame illustration with connected fitting boxes, a contextual picker with live preview, and a bottom power/stats/cost strip — without changing what the mock composes.

**Architecture:** Two engine-free, unit-tested units land in `Dimenship.Shell` (`FrameArtSerializer` for the per-frame JSON placement sidecar, `StageGeometry` for fit/box/leader/popover maths). Everything else stays in the Godot assembly under `dimenship/scripts/ui/focus/loadouts/`, following `GraphCanvas`'s pattern: lines drawn, cards as child controls. Frame art is hand-written SVG rasterised at runtime and tinted by a small glow shader whose colour comes from `ShellPalette`.

**Tech Stack:** Godot 4.7.1 with C# (.NET 8), NUnit 4.5.1, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-09-14-glass-console-loadout-editor-design.md` — read it before starting; decisions are cited below as *D1…D15*.

## Why

Ticket #38 asks for the composer to stop reading as a form. The mock's question — *is composing by filling sockets against a live rollup pleasant?* — cannot be answered by a list of rows; it needs the machine on screen. The spec keeps the mock's rules fixed so that the only thing this change tests is the presentation.

## Global Constraints

- `Dimenship.Core` is not touched. `Dimenship.Shell` must not reference `Dimenship.Core` or Godot.
- The view stays a **concept mock**: no persistence, `QUEUE BUILD` disabled, `CONCEPT — NOTHING IS BUILT FROM THIS` shown. Panel id `robotics`, title `Robotics` — unchanged.
- Vocabulary: **fitting** (never *part* in new code or UI text), **socket** (never *slot*).
- No colour literal anywhere in C# outside `ShellPalette`. Shader colour arrives through a uniform set from `ShellPalette`.
- Declaration order is display order: sockets, fittings, frames are never sorted.
- Box positions are authored and **never moved automatically** (D8).
- Invented content (catalog, sidecars) is **not** unit-tested (mock spec rule). Only the two Shell units get tests.
- NUnit stays at 4.5.1. Target framework stays `net8.0`.
- Test names read as sentences with underscores between clauses.
- Commit subjects: conventional prefix + lowercase declarative sentence about behaviour. Bodies are prose. End every commit message with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Godot `.uid` / `.import` sidecars are committed with their files.
- Godot binary on this machine: `/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe` (referred to below as `$GODOT`). `--headless --path dimenship --import` imports assets and writes sidecars, then quits.

## File map

| File | Status | Responsibility |
| :--- | :--- | :--- |
| `src/Dimenship.Shell/FrameArtSerializer.cs` | Create | Sidecar DTO, parse, catalog-free validation, `Check` against a frame's socket ids |
| `src/Dimenship.Shell/StageGeometry.cs` | Create | `Fit`, `ToStage`, `BoxRect`, `Leader`, `Problems`, `PopoverRect` |
| `tests/Dimenship.Shell.Tests/FrameArtSerializerTests.cs` | Create | Parser tests, break-one-thing style |
| `tests/Dimenship.Shell.Tests/StageGeometryTests.cs` | Create | Geometry tests |
| `dimenship/scripts/ui/focus/loadouts/LoadoutModel.cs` | Modify | `SocketDef`, `FittingDef`, `StatBlock -`, `CarryOver`, id-based `Refit` |
| `dimenship/scripts/ui/focus/loadouts/LoadoutCatalog.cs` | Modify | Socket ids, `Fittings`, `Fitting(id)`, `FittingArtPath` |
| `dimenship/scripts/ui/focus/loadouts/LoadoutRollup.cs` | Modify | Rename; `Supply` / `Draw` |
| `dimenship/scripts/ui/focus/loadouts/StatFormat.cs` | Modify | `Change`, `Compact` stats |
| `dimenship/scripts/ui/focus/loadouts/ItemStock.cs` | Create | Vessel stock read off the snapshot, shared by cost readouts |
| `dimenship/scripts/ui/focus/loadouts/CostBox.cs` | Modify | Reads `ItemStock` |
| `dimenship/scripts/ui/focus/loadouts/FrameArtLibrary.cs` | Create | Load sidecar + SVG per frame, report warnings, rasterise per scale step |
| `dimenship/scripts/ui/focus/loadouts/ProjectionGlow.cs` | Create | Builds the glow `ShaderMaterial` |
| `dimenship/scripts/ui/focus/loadouts/FittingBox.cs` | Create | One socket's box |
| `dimenship/scripts/ui/focus/loadouts/LeaderLayer.cs` | Create | Draws leader lines and anchors |
| `dimenship/scripts/ui/focus/loadouts/LoadoutStage.cs` | Create | Art + leaders + boxes + popover placement + fallback |
| `dimenship/scripts/ui/focus/loadouts/BudgetBar.cs` | Create | Draw-against-supply bar with hatched preview |
| `dimenship/scripts/ui/focus/loadouts/LoadoutStrip.cs` | Create | POWER BUDGET · STATS · BUILD COST · DETAILS |
| `dimenship/scripts/ui/focus/loadouts/StagePopover.cs` | Create | Popover chrome: title, close, notch, outside-click close |
| `dimenship/scripts/ui/focus/loadouts/FittingPicker.cs` | Create | Compatible fittings + CLEAR SOCKET, preview events |
| `dimenship/scripts/ui/focus/loadouts/FrameChooser.cs` | Create | Frames with thumbnails and carry-over preview |
| `dimenship/scripts/ui/focus/loadouts/TemplateList.cs` | Modify | Thumbnail cards; info box removed |
| `dimenship/scripts/ui/focus/loadouts/LoadoutsFocus.cs` | Modify | Composes all of the above |
| `dimenship/scripts/ui/focus/loadouts/SocketRow.cs` | Delete | Replaced by `FittingBox` (Task 5) |
| `dimenship/scripts/ui/focus/loadouts/PartPalette.cs`, `LoadoutDragData.cs` | Delete | Replaced by the picker (Task 7) |
| `dimenship/scripts/ui/focus/IconSlot.cs` | Modify | `FromPath` / `SetPath` for non-icon art |
| `dimenship/scripts/ui/ShellPalette.cs` | Modify | `Projection`, `ProjectionGuide` |
| `dimenship/scripts/ui/ShellTheme.cs` | Modify | `Popover()` stylebox |
| `dimenship/assets/projection.gdshader` | Create | Tint + halo |
| `dimenship/assets/loadouts/frames/{surveyor_frame,hauler_frame}.{svg,json}` | Create | Frame art and placement |
| `dimenship/assets/loadouts/fittings/*.svg` | Create | 16 fitting images |
| `CLAUDE.md`, mock spec | Modify | Docs (Task 9) |

**No unit tests exist or are possible for the Godot assembly.** Godot tasks are verified by `dotnet build dimenship/Dimenship.csproj` (must report `0 Error(s)`) and by the in-editor checks each task lists. Run the game with `/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64.exe --path dimenship`, start a campaign, and open **Robotics** from the rail.

---

### Task 1: Part becomes fitting, sockets gain ids, a frame swap carries by id

**Files:**
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutModel.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutCatalog.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutRollup.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutsFocus.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/SocketRow.cs`, `PartPalette.cs`, `LoadoutDragData.cs` (rename only; deleted in later tasks)
- Modify: `dimenship/scripts/ui/focus/loadouts/RollupGrid.cs`, `StatFormat.cs` (comments/text only)

**Interfaces:**
- Produces:
  - `public sealed record SocketDef(string Id, SocketKind Kind);`
  - `FrameDef.Sockets : IReadOnlyList<SocketDef>`
  - `public sealed record FittingDef(string Id, string Label, SocketKind Kind, StatBlock Delta, IReadOnlyList<ItemCost> Cost, string Note);`
  - `LoadoutCatalog.Fittings : IReadOnlyList<FittingDef>`, `LoadoutCatalog.Fitting(string? id) : FittingDef?`, `LoadoutCatalog.OfKind(SocketKind) : IEnumerable<FittingDef>`
  - `LoadoutCatalog.FittingArtPath(string fittingId) : string` → `res://assets/loadouts/fittings/{id}.svg`
  - `StatBlock operator -(StatBlock a, StatBlock b)`
  - `static List<string?> LoadoutDraft.CarryOver(FrameDef from, IReadOnlyList<string?> fitted, FrameDef to)`
  - `LoadoutDraft.Refit(FrameDef from, FrameDef to)` — now id-based

- [ ] **Step 1: Change the model types in `LoadoutModel.cs`**

Add `operator -` to `StatBlock`, directly below `operator +`:

```csharp
    /// <summary>
    /// The difference a swap makes: candidate minus current. What the picker's preview line reads,
    /// so it says what choosing a fitting would change rather than what the fitting is.
    /// </summary>
    public static StatBlock operator -(StatBlock a, StatBlock b) => new(
        a.Mass - b.Mass,
        a.Power - b.Power,
        a.Durability - b.Durability,
        a.Cargo - b.Cargo,
        a.Scan - b.Scan,
        a.WorkRate - b.WorkRate);
```

Insert above `FrameDef`:

```csharp
/// <summary>
/// One socket on a frame: an id the frame art's placement file is keyed by, and the kind of fitting
/// it accepts.
/// <para>
/// The id exists because the art needs a name for each connector, and positions cannot be one: a
/// placement keyed by index silently re-points when a frame's socket list is reordered. It also
/// gives a frame swap a better rule — see <see cref="LoadoutDraft.CarryOver"/>.
/// </para>
/// </summary>
public sealed record SocketDef(string Id, SocketKind Kind);
```

Change `FrameDef`'s `IReadOnlyList<SocketKind> Sockets` to `IReadOnlyList<SocketDef> Sockets`.

Replace the `PartDef` record and its doc comment with:

```csharp
/// <summary>
/// A thing that occupies one socket. Called a <b>fitting</b>, the word
/// <c>docs/superpowers/specs/2026-08-21-bot-composition-design.md</c> settled for the equipment
/// tier, and never a module: <c>module</c> is a shipped bulk commodity (<c>"id": "module"</c>,
/// <i>Robot Module</i>) that a fitting's build cost can name as an ingredient.
/// </summary>
public sealed record FittingDef(
    string Id,
    string Label,
    SocketKind Kind,
    StatBlock Delta,
    IReadOnlyList<ItemCost> Cost,
    string Note);
```

Replace `LoadoutDraft.Refit` and its doc comment with:

```csharp
    /// <summary>
    /// Which fittings survive a swap from <paramref name="from"/> to <paramref name="to"/>, as a
    /// list parallel to the new frame's sockets. A fitting carries when the new frame has a socket
    /// with the <b>same id and the same kind</b>.
    /// <para>
    /// Matching by position was the rule before sockets had ids, and it dropped fittings for no
    /// reason a player could see: the Hauler's sensor sits at index 4, not 1, so a Surveyor's
    /// sensor was lost on the way across. Matching by id is still not a hunt for a compatible
    /// socket elsewhere on the frame — that would move a fitting somewhere the player did not put
    /// it — because it only ever lands in the socket that is, by name, the same one.
    /// </para>
    /// </summary>
    public static List<string?> CarryOver(FrameDef from, IReadOnlyList<string?> fitted, FrameDef to)
    {
        var carried = new List<string?>();

        foreach (var socket in to.Sockets)
        {
            string? kept = null;

            for (var i = 0; i < from.Sockets.Count && i < fitted.Count; i++)
            {
                if (from.Sockets[i] == socket)
                {
                    kept = fitted[i];
                    break;
                }
            }

            carried.Add(kept);
        }

        return carried;
    }

    /// <summary>Swaps the frame, keeping what <see cref="CarryOver"/> keeps and dropping the rest.</summary>
    public void Refit(FrameDef from, FrameDef to)
    {
        var kept = CarryOver(from, Fitted, to);

        FrameId = to.Id;
        Fitted.Clear();
        Fitted.AddRange(kept);
    }
```

(`from.Sockets[i] == socket` compares both id and kind, because `SocketDef` is a record.)

Update `FilledSockets`'s lambda parameter name from `part` to `fitting`. In the file's header comment and in `Verdict.OverBudget`'s summary, replace "part"/"parts" with "fitting"/"fittings".

- [ ] **Step 2: Rename in `LoadoutCatalog.cs`**

Replace the two frame socket arrays:

```csharp
            new[]
            {
                new SocketDef("tool", SocketKind.Tool),
                new SocketDef("sensor", SocketKind.Sensor),
                new SocketDef("power", SocketKind.Power),
                new SocketDef("investigation", SocketKind.Investigation),
            },
```

and for the Hauler:

```csharp
            new[]
            {
                new SocketDef("tool", SocketKind.Tool),
                new SocketDef("cargo", SocketKind.Cargo),
                new SocketDef("power", SocketKind.Power),
                new SocketDef("defense", SocketKind.Defense),
                new SocketDef("sensor", SocketKind.Sensor),
            },
```

Rename `Parts` → `Fittings` (type `IReadOnlyList<FittingDef>`), `PartIndex` → `FittingIndex`, the public `Part(string? id)` → `Fitting(string? id)` returning `FittingDef?`, the private factory `Part(...)` → `Define(...)` returning `FittingDef` (update all sixteen call sites), and `OfKind` to return `IEnumerable<FittingDef>` from `Fittings`. Replace "part"/"parts" with "fitting"/"fittings" in every doc comment (keep the `Items.Module` comment's meaning: the bulk commodity is *spent to build a fitting*, see `FittingDef`).

Add, below `OfKind`:

```csharp
    /// <summary>
    /// Where a fitting's standalone image lives. By convention from the id rather than a field on
    /// <see cref="FittingDef"/>: the id is already unique, and a path field would restate it in a
    /// second place that could drift. One image per fitting, reused by the box, the picker and
    /// anything else that shows one.
    /// </summary>
    public static string FittingArtPath(string fittingId) =>
        $"res://assets/loadouts/fittings/{fittingId}.svg";
```

- [ ] **Step 3: Rename in `LoadoutRollup.cs`**

`var part = ... LoadoutCatalog.Part(...)` → `var fitting = ... LoadoutCatalog.Fitting(...)`, and every use of `part` in that loop → `fitting`. Replace "part"/"parts" with "fitting"/"fittings" in the doc comments (including `Contribution`'s `Source` param doc).

- [ ] **Step 4: Keep the soon-deleted files compiling**

- `SocketRow.cs`: `PartDef?` → `FittingDef?` (field, constructor parameter); `new LoadoutDragData { Part = _fitted, ... }` → `{ Fitting = _fitted, ... }`.
- `LoadoutDragData.cs`: `public required PartDef Part` → `public required FittingDef Fitting`; `Fits` returns `Fitting.Kind == kind`.
- `PartPalette.cs`: `Action<PartDef>` → `Action<FittingDef>`; `PartRow`'s `PartDef` → `FittingDef`; `new LoadoutDragData { Part = _part }` → `{ Fitting = _part }`; `ShowFrame(IEnumerable<SocketKind> sockets)` is unchanged; placeholder text `"Search parts"` → `"Search fittings"`.
- `RollupGrid.cs`: replace the over-budget note's text with `"The fittings draw more than the fitted core supplies. Nothing here is built, so nothing is stopped by it."`, and "which part is costing me this" / "pulling parts out" in the doc comment with "which fitting…" / "pulling fittings out".
- `StatFormat.cs`: in doc comments, "the socket rows and the palette" stays for now (those types still exist until Tasks 5 and 7); change only "part" → "fitting".

- [ ] **Step 5: Update `LoadoutsFocus.cs` to the new shapes**

- `Fit(PartDef part)` → `Fit(FittingDef fitting)`; inside, `part` → `fitting`, `frame.Sockets[_socket] == part.Kind` → `frame.Sockets[_socket].Kind == fitting.Kind`, and the two loops' `frame.Sockets[i] == part.Kind` → `frame.Sockets[i].Kind == fitting.Kind`; `template.Fitted[target] = fitting.Id`.
- `Drop`: `payload.Part.Id` → `payload.Fitting.Id`.
- `Rebuild`: `LoadoutCatalog.Part(template.Fitted[i])` → `LoadoutCatalog.Fitting(template.Fitted[i])`; `new SocketRow(i, rollup.Frame.Sockets[i], part, ...)` → `new SocketRow(i, rollup.Frame.Sockets[i].Kind, fitting, ...)`; `_palette.ShowFrame(rollup.Frame.Sockets)` → `_palette.ShowFrame(rollup.Frame.Sockets.Select(socket => socket.Kind))` (add `using System.Linq;`); `_palette.ShowKind(rollup.Frame.Sockets[_socket])` → `...Sockets[_socket].Kind`.
- Doc comments: "part palette" → "fitting palette", "part" → "fitting".

- [ ] **Step 6: Build**

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q`
Expected: `Build succeeded.` `0 Error(s)`. Then confirm no stray identifiers:

Run: `grep -rn "PartDef\|LoadoutCatalog\.Part(\|\.Parts\b" dimenship/scripts`
Expected: no output.

- [ ] **Step 7: Verify in the game**

Open Robotics. Select *Survey Pattern A* (Surveyor, all four filled). Press **Hauler Frame**. Expected: Tool (*Precision Manipulator*), Power (*Power Core Mk1*) **and Sensor (*Basic Sensor Array*)** survive; Cargo and Defense are empty; Investigation's fitting is gone. Press `Ctrl+Z`: back to Surveyor with all four.

- [ ] **Step 8: Commit**

```bash
git add dimenship/scripts/ui/focus/loadouts
git commit -F - <<'EOF'
refactor: a part is a fitting, and a frame swap keeps it by socket name

The loadout mock called what sits in a socket a part, as a placeholder until
the vocabulary was settled. Bot-composition settled it as fitting, so the mock
renames with it: FittingDef, LoadoutCatalog.Fittings and Fitting(id), and
every string and comment that said part.

Sockets gain ids, because the glass console's frame art is keyed by them. With
ids, a frame swap carries a fitting when the new frame has a socket of the
same id and kind, rather than the same kind at the same index. The positional
rule dropped a Surveyor's sensor on the way to a Hauler, whose sensor sits
last; matching by name keeps it without ever moving a fitting to a socket the
player did not choose.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 2: The frame art sidecar parses and validates in `Dimenship.Shell`

**Files:**
- Create: `src/Dimenship.Shell/FrameArtSerializer.cs`
- Test: `tests/Dimenship.Shell.Tests/FrameArtSerializerTests.cs`

**Interfaces:**
- Produces (namespace `Dimenship.Shell`):
  - `public sealed record FrameArtSocket(string Id, (int X, int Y) Anchor, (int X, int Y) Box);`
  - `public sealed record FrameArt(string Frame, string Artwork, (int W, int H) Canvas, IReadOnlyList<FrameArtSocket> Sockets);`
  - `public sealed record FrameArtLoadResult(FrameArt? Art, IReadOnlyList<string> Warnings);` — `Art` is null exactly when `Warnings` is non-empty.
  - `public static FrameArtLoadResult FrameArtSerializer.Load(string? json)`
  - `public static IReadOnlyList<string> FrameArtSerializer.Check(FrameArt art, string frameId, IReadOnlyList<string> socketIds)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Dimenship.Shell.Tests/FrameArtSerializerTests.cs`:

```csharp
using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class FrameArtSerializerTests
{
    /// <summary>
    /// A minimal valid sidecar. Every test but the first copies it and breaks exactly one thing,
    /// so a failure is about the rule under test and not about the fixture.
    /// </summary>
    private const string Valid = """
        {
          "notes": "fixture",
          "frame": "test_frame",
          "artwork": "test_frame.svg",
          "canvas": { "width": 1200, "height": 720 },
          "sockets": [
            { "id": "tool", "anchor": { "x": 330, "y": 160 }, "box": { "x": 170, "y": 160 } },
            { "id": "sensor", "anchor": { "x": 648, "y": 222 }, "box": { "x": 1030, "y": 160 } }
          ]
        }
        """;

    private static FrameArt ValidArt() => FrameArtSerializer.Load(Valid).Art!;

    [Test]
    public void AValidSidecar_Parses_WithNoWarnings()
    {
        var result = FrameArtSerializer.Load(Valid);

        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.Art, Is.Not.Null);
        Assert.That(result.Art!.Frame, Is.EqualTo("test_frame"));
        Assert.That(result.Art.Artwork, Is.EqualTo("test_frame.svg"));
        Assert.That(result.Art.Canvas, Is.EqualTo((1200, 720)));
        Assert.That(result.Art.Sockets, Is.EqualTo(new[]
        {
            new FrameArtSocket("tool", (330, 160), (170, 160)),
            new FrameArtSocket("sensor", (648, 222), (1030, 160)),
        }));
    }

    [Test]
    public void TheSocketOrder_IsTheFilesOrder_NeverSorted()
    {
        var art = ValidArt();

        Assert.That(art.Sockets.Select(socket => socket.Id), Is.EqualTo(new[] { "tool", "sensor" }));
    }

    [Test]
    public void NotesAreOptional_BecauseTheyAreDocumentation_NotPlacement()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"notes\": \"fixture\",", string.Empty));

        Assert.That(result.Warnings, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AnEmptyFile_IsReported_AndYieldsNoArt(string json)
    {
        var result = FrameArtSerializer.Load(json);

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement file is empty" }));
    }

    [Test]
    public void NoFileContentAtAll_IsReportedAsEmpty()
    {
        var result = FrameArtSerializer.Load(null);

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement file is empty" }));
    }

    [Test]
    public void MalformedJson_IsReported_WithoutThrowing()
    {
        var result = FrameArtSerializer.Load("{ \"frame\": ");

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.StartWith("placement file is not valid"));
    }

    [Test]
    public void AMissingField_IsNamed()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"artwork\": \"test_frame.svg\",", string.Empty));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement names no artwork" }));
    }

    [Test]
    public void AMissingSocketCoordinate_IsNamed_WithItsSocket()
    {
        var result = FrameArtSerializer.Load(
            Valid.Replace("\"anchor\": { \"x\": 330, \"y\": 160 }", "\"anchor\": { \"x\": 330 }"));

        Assert.That(result.Warnings, Is.EqualTo(new[] { "socket 'tool' anchor names no y" }));
    }

    [Test]
    public void AnUnknownField_IsRejected_RatherThanIgnored()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"notes\": \"fixture\",", "\"colour\": \"cyan\","));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("colour"));
    }

    [Test]
    public void AFractionalCoordinate_IsReported_NotRounded()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"x\": 330,", "\"x\": 330.5,"));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("$.sockets[0].anchor.x"));
    }

    [Test]
    public void ADuplicateSocketId_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"id\": \"sensor\"", "\"id\": \"tool\""));

        Assert.That(result.Warnings, Is.EqualTo(new[] { "socket id 'tool' is placed twice" }));
    }

    [Test]
    public void ASocketIdOutsideTheCatalogPattern_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"id\": \"sensor\"", "\"id\": \"Sensor-1\""));

        Assert.That(result.Warnings, Is.EqualTo(new[] { "socket id 'Sensor-1' is not a valid id" }));
    }

    [Test]
    public void AnAnchorOutsideTheCanvas_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"x\": 648,", "\"x\": 1300,"));

        Assert.That(result.Warnings, Is.EqualTo(new[]
        {
            "socket 'sensor' anchor (1300, 222) lies outside the 1200x720 canvas",
        }));
    }

    [Test]
    public void ANonPositiveCanvas_IsReported()
    {
        var result = FrameArtSerializer.Load(Valid.Replace("\"width\": 1200", "\"width\": 0"));

        Assert.That(result.Art, Is.Null);
        Assert.That(result.Warnings, Does.Contain("canvas width must be positive, not 0"));
    }

    [Test]
    public void NoSockets_IsReported()
    {
        var json = Valid[..Valid.IndexOf("\"sockets\"", StringComparison.Ordinal)] + "\"sockets\": [] }";
        var result = FrameArtSerializer.Load(json);

        Assert.That(result.Warnings, Is.EqualTo(new[] { "placement names no sockets" }));
    }

    [Test]
    public void Check_AMatchingFrame_HasNoWarnings()
    {
        Assert.That(FrameArtSerializer.Check(ValidArt(), "test_frame", new[] { "tool", "sensor" }), Is.Empty);
    }

    [Test]
    public void Check_ASocketTheFrameHasButTheFileDoesNot_IsNamed()
    {
        var warnings = FrameArtSerializer.Check(ValidArt(), "test_frame", new[] { "tool", "sensor", "power" });

        Assert.That(warnings, Is.EqualTo(new[] { "socket 'power' has no placement" }));
    }

    [Test]
    public void Check_ASocketTheFilePlacesButTheFrameLacks_IsNamed()
    {
        var warnings = FrameArtSerializer.Check(ValidArt(), "test_frame", new[] { "tool" });

        Assert.That(warnings, Is.EqualTo(new[] { "socket 'sensor' is not on frame 'test_frame'" }));
    }

    [Test]
    public void Check_APlacementForAnotherFrame_IsNamed()
    {
        var warnings = FrameArtSerializer.Check(ValidArt(), "hauler_frame", new[] { "tool", "sensor" });

        Assert.That(warnings, Is.EqualTo(new[] { "placement is for frame 'test_frame', not 'hauler_frame'" }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dimenship.Shell.Tests --filter FullyQualifiedName~FrameArtSerializerTests`
Expected: build FAILS with `CS0103`/`CS0246` — `FrameArtSerializer`, `FrameArt`, `FrameArtSocket` do not exist.

- [ ] **Step 3: Implement `src/Dimenship.Shell/FrameArtSerializer.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dimenship.Shell;

/// <summary>
/// One socket's placement on a frame's artwork: where its connector is drawn, and where its box
/// is centred. Both in the artwork's own coordinates, so the drawing, the boxes and the leader
/// lines scale together — see <see cref="StageGeometry"/>.
/// </summary>
public sealed record FrameArtSocket(string Id, (int X, int Y) Anchor, (int X, int Y) Box);

/// <summary>
/// A frame's illustration and where each of its sockets sits on it. Placement only: which fitting
/// fits which socket is the loadout catalog's, so no edit to this file can change what is legal.
/// </summary>
/// <param name="Canvas">The artwork's <c>viewBox</c> size. The frame is drawn in its middle and the
/// margins are where the boxes go.</param>
public sealed record FrameArt(
    string Frame,
    string Artwork,
    (int W, int H) Canvas,
    IReadOnlyList<FrameArtSocket> Sockets);

/// <summary>Outcome of reading one placement file. <see cref="Art"/> is null exactly when there are warnings.</summary>
public sealed record FrameArtLoadResult(FrameArt? Art, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a frame art placement file (<c>assets/loadouts/frames/{frame}.json</c>) on the settings
/// file's contract: every degraded input produces warnings rather than an exception, because a
/// broken placement is a presentation problem and the composer must keep working without its art.
/// <para>
/// Unlike <see cref="SettingsSerializer"/>, a problem here yields <b>no</b> result rather than a
/// defaulted one. A half-placed frame reads as a layout, and the player would take it for the
/// intended one; the view falls back to a plain column for the whole frame instead
/// (<c>2026-09-14-glass-console-loadout-editor-design.md</c>, Decision 6).
/// </para>
/// <para>
/// Every DTO field is nullable so a missing one is reported rather than read as zero, unknown
/// fields are rejected so a misspelt <c>ancor</c> is not silently ignored, and coordinates are
/// integers so a fractional one fails parsing rather than being rounded somewhere nobody sees.
/// </para>
/// <para>
/// This assembly cannot see the loadout catalog, so validation is split along that line:
/// <see cref="Load"/> checks what a file can get wrong on its own, and <see cref="Check"/> compares
/// it with a frame's socket ids, which the Godot layer supplies.
/// </para>
/// </summary>
public static class FrameArtSerializer
{
    /// <summary>The catalog's id pattern, so a socket id could be a content id if sockets ever become content.</summary>
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record PointDto(int? X, int? Y);

    private sealed record SizeDto(int? Width, int? Height);

    private sealed record SocketDto(string? Id, PointDto? Anchor, PointDto? Box);

    private sealed record Dto(
        string? Notes,
        string? Frame,
        string? Artwork,
        SizeDto? Canvas,
        List<SocketDto?>? Sockets);

    public static FrameArtLoadResult Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failed("placement file is empty");
        }

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, Options);
        }
        catch (JsonException e)
        {
            return Failed($"placement file is not valid: {e.Message}");
        }

        if (dto is null)
        {
            return Failed("placement file deserialized to null");
        }

        var warnings = new List<string>();
        var frame = Required(dto.Frame, "frame", warnings);
        var artwork = Required(dto.Artwork, "artwork", warnings);
        var canvas = ReadCanvas(dto.Canvas, warnings);
        var sockets = ReadSockets(dto.Sockets, canvas, warnings);

        return warnings.Count > 0 || frame is null || artwork is null || canvas is null
            ? new FrameArtLoadResult(null, warnings)
            : new FrameArtLoadResult(new FrameArt(frame, artwork, canvas.Value, sockets), warnings);
    }

    /// <summary>
    /// Compares a parsed placement with the frame it is meant for. The socket ids must match
    /// exactly — none missing, none extra — because a socket with no placement has nowhere to draw
    /// its box, and a placement for a socket the frame lacks is a file written for another frame.
    /// </summary>
    public static IReadOnlyList<string> Check(FrameArt art, string frameId, IReadOnlyList<string> socketIds)
    {
        var warnings = new List<string>();

        if (art.Frame != frameId)
        {
            warnings.Add($"placement is for frame '{art.Frame}', not '{frameId}'");
        }

        var placed = art.Sockets.Select(socket => socket.Id).ToHashSet();

        foreach (var id in socketIds)
        {
            if (!placed.Contains(id))
            {
                warnings.Add($"socket '{id}' has no placement");
            }
        }

        var known = socketIds.ToHashSet();

        foreach (var socket in art.Sockets)
        {
            if (!known.Contains(socket.Id))
            {
                warnings.Add($"socket '{socket.Id}' is not on frame '{frameId}'");
            }
        }

        return warnings;
    }

    private static FrameArtLoadResult Failed(string warning) => new(null, new[] { warning });

    private static string? Required(string? value, string name, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        warnings.Add($"placement names no {name}");
        return null;
    }

    private static (int W, int H)? ReadCanvas(SizeDto? dto, List<string> warnings)
    {
        if (dto is null)
        {
            warnings.Add("placement names no canvas");
            return null;
        }

        var width = Positive(dto.Width, "width", warnings);
        var height = Positive(dto.Height, "height", warnings);

        return width is { } w && height is { } h ? (w, h) : null;
    }

    private static int? Positive(int? value, string name, List<string> warnings)
    {
        if (value is not { } number)
        {
            warnings.Add($"canvas names no {name}");
            return null;
        }

        if (number > 0)
        {
            return number;
        }

        warnings.Add($"canvas {name} must be positive, not {number}");
        return null;
    }

    private static IReadOnlyList<FrameArtSocket> ReadSockets(
        List<SocketDto?>? dtos, (int W, int H)? canvas, List<string> warnings)
    {
        var sockets = new List<FrameArtSocket>();

        if (dtos is null || dtos.Count == 0)
        {
            warnings.Add("placement names no sockets");
            return sockets;
        }

        var seen = new HashSet<string>();

        for (var i = 0; i < dtos.Count; i++)
        {
            if (dtos[i] is not { } dto)
            {
                warnings.Add($"socket {i + 1} is null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                warnings.Add($"socket {i + 1} names no id");
                continue;
            }

            if (!IdPattern.IsMatch(dto.Id))
            {
                warnings.Add($"socket id '{dto.Id}' is not a valid id");
                continue;
            }

            if (!seen.Add(dto.Id))
            {
                warnings.Add($"socket id '{dto.Id}' is placed twice");
                continue;
            }

            var anchor = Point(dto.Anchor, $"socket '{dto.Id}' anchor", warnings);
            var box = Point(dto.Box, $"socket '{dto.Id}' box", warnings);

            if (anchor is not { } a || box is not { } b)
            {
                continue;
            }

            if (canvas is { } c && (a.X < 0 || a.Y < 0 || a.X > c.W || a.Y > c.H))
            {
                warnings.Add($"socket '{dto.Id}' anchor ({a.X}, {a.Y}) lies outside the {c.W}x{c.H} canvas");
                continue;
            }

            sockets.Add(new FrameArtSocket(dto.Id, a, b));
        }

        return sockets;
    }

    private static (int X, int Y)? Point(PointDto? dto, string what, List<string> warnings)
    {
        if (dto is null)
        {
            warnings.Add($"{what} is missing");
            return null;
        }

        if (dto.X is not { } x)
        {
            warnings.Add($"{what} names no x");
            return null;
        }

        if (dto.Y is not { } y)
        {
            warnings.Add($"{what} names no y");
            return null;
        }

        return (x, y);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Dimenship.Shell.Tests --filter FullyQualifiedName~FrameArtSerializerTests`
Expected: all PASS. If `AnUnknownField_IsRejected_RatherThanIgnored` fails because the JSON exception does not name the member, check that `UnmappedMemberHandling` is set on `Options` (it is a .NET 8 `JsonSerializerOptions` property).

- [ ] **Step 5: Run the whole Shell suite**

Run: `dotnet test tests/Dimenship.Shell.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Dimenship.Shell/FrameArtSerializer.cs tests/Dimenship.Shell.Tests/FrameArtSerializerTests.cs
git commit -F - <<'EOF'
feat: a frame's art placement is read from a file that reports what is wrong with it

Each loadout frame's illustration gets a JSON sidecar recording, per socket,
where its connector is drawn and where its box is centred, in the artwork's
own viewBox coordinates. FrameArtSerializer reads it on the settings file's
contract: warnings rather than exceptions, every field nullable so a missing
one is named, unknown fields rejected, integers only.

Unlike settings, a problem yields no result rather than a defaulted one: a
half-placed frame would read as the intended layout. The file cannot see the
loadout catalog, so Check compares it with a frame's socket ids separately,
and the Godot layer supplies them.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Stage geometry is pure, integer and tested

**Files:**
- Create: `src/Dimenship.Shell/StageGeometry.cs`
- Test: `tests/Dimenship.Shell.Tests/StageGeometryTests.cs`

**Interfaces:**
- Produces (namespace `Dimenship.Shell`):
  - `public readonly record struct StageFit(int ScalePermille, int OffsetX, int OffsetY);`
  - `public enum StageProblemKind { BoxesOverlap, LeadersCross, LeaderThroughBox }`
  - `public sealed record StageProblem(StageProblemKind Kind, int First, int Second);`
  - `StageGeometry.LeaderStub = 16`, `StageGeometry.PopoverGap = 12`
  - `StageFit Fit((int W, int H) canvas, (int W, int H) stage)`
  - `int Scale(int value, int permille)`
  - `(int X, int Y) ToStage(StageFit fit, (int X, int Y) point)`
  - `(int X, int Y, int W, int H) BoxRect(StageFit fit, (int X, int Y) centre, (int W, int H) box, (int W, int H) stage)`
  - `IReadOnlyList<(int X, int Y)> Leader((int X, int Y, int W, int H) box, (int X, int Y) anchor)`
  - `IReadOnlyList<StageProblem> Problems(IReadOnlyList<(int X, int Y, int W, int H)> boxes, IReadOnlyList<IReadOnlyList<(int X, int Y)>> leaders)`
  - `(int X, int Y, int W, int H) PopoverRect((int X, int Y, int W, int H) box, (int W, int H) popover, int centreX, (int W, int H) stage)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Dimenship.Shell.Tests/StageGeometryTests.cs`:

```csharp
using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class StageGeometryTests
{
    private static readonly StageFit Identity = new(1000, 0, 0);

    [Test]
    public void Fit_ANarrowStage_LetterboxesVertically_AndCentres()
    {
        var fit = StageGeometry.Fit((1200, 720), (600, 720));

        Assert.That(fit, Is.EqualTo(new StageFit(500, 0, 180)));
    }

    [Test]
    public void Fit_AWideStage_LetterboxesHorizontally_AndCentres()
    {
        var fit = StageGeometry.Fit((1200, 720), (2400, 720));

        Assert.That(fit, Is.EqualTo(new StageFit(1000, 600, 0)));
    }

    [Test]
    public void Fit_AnEmptyStage_ScalesToNothing_RatherThanDividingByZero()
    {
        Assert.That(StageGeometry.Fit((1200, 720), (0, 0)), Is.EqualTo(new StageFit(0, 0, 0)));
    }

    [Test]
    public void ToStage_ScalesThenOffsets()
    {
        Assert.That(StageGeometry.ToStage(new StageFit(500, 10, 20), (200, 100)), Is.EqualTo((110, 70)));
    }

    [Test]
    public void BoxRect_CentresTheBoxOnItsScaledCentre()
    {
        var rect = StageGeometry.BoxRect(Identity, (100, 100), (184, 112), (1200, 720));

        Assert.That(rect, Is.EqualTo((8, 44, 184, 112)));
    }

    [Test]
    public void BoxRect_ABoxPastTheEdge_IsClampedInsideTheStage()
    {
        var rect = StageGeometry.BoxRect(Identity, (50, 700), (184, 112), (1200, 720));

        Assert.That(rect, Is.EqualTo((0, 608, 184, 112)));
    }

    [Test]
    public void BoxRect_TheBoxSizeDoesNotScale_BecauseItsTextMustNot()
    {
        var rect = StageGeometry.BoxRect(new StageFit(500, 0, 0), (400, 400), (184, 112), (1200, 720));

        Assert.That((rect.W, rect.H), Is.EqualTo((184, 112)));
        Assert.That((rect.X, rect.Y), Is.EqualTo((108, 144)));
    }

    [Test]
    public void Leader_ToAnAnchorOnTheRight_LeavesTheRightSide_WithAHorizontalStub()
    {
        var leader = StageGeometry.Leader((0, 0, 100, 50), (300, 10));

        Assert.That(leader, Is.EqualTo(new[] { (100, 25), (116, 25), (300, 10) }));
    }

    [Test]
    public void Leader_ToAnAnchorOnTheLeft_LeavesTheLeftSide()
    {
        var leader = StageGeometry.Leader((300, 0, 100, 50), (0, 40));

        Assert.That(leader, Is.EqualTo(new[] { (300, 25), (284, 25), (0, 40) }));
    }

    [Test]
    public void Leader_ToAnAnchorBelowTheBoxsSpan_LeavesTheBottom()
    {
        var leader = StageGeometry.Leader((0, 0, 100, 50), (50, 200));

        Assert.That(leader, Is.EqualTo(new[] { (50, 50), (50, 66), (50, 200) }));
    }

    [Test]
    public void Leader_TheStubNeverOvershootsAnAnchorCloserThanItsLength()
    {
        var leader = StageGeometry.Leader((0, 0, 100, 50), (108, 25));

        Assert.That(leader, Is.EqualTo(new[] { (100, 25), (108, 25), (108, 25) }));
    }

    [Test]
    public void Problems_ACleanLayout_HasNone()
    {
        var boxes = new[] { (0, 0, 100, 50), (0, 100, 100, 50) };
        var leaders = new[]
        {
            StageGeometry.Leader(boxes[0], (300, 25)),
            StageGeometry.Leader(boxes[1], (300, 125)),
        };

        Assert.That(StageGeometry.Problems(boxes, leaders), Is.Empty);
    }

    [Test]
    public void Problems_OverlappingBoxes_AreReported()
    {
        var boxes = new[] { (0, 0, 100, 50), (50, 25, 100, 50) };
        var leaders = new IReadOnlyList<(int X, int Y)>[] { new[] { (100, 25) }, new[] { (150, 50) } };

        Assert.That(
            StageGeometry.Problems(boxes, leaders),
            Does.Contain(new StageProblem(StageProblemKind.BoxesOverlap, 0, 1)));
    }

    [Test]
    public void Problems_BoxesThatOnlyTouch_DoNotOverlap()
    {
        var boxes = new[] { (0, 0, 100, 50), (100, 0, 100, 50) };
        var leaders = new IReadOnlyList<(int X, int Y)>[] { new[] { (50, 0) }, new[] { (150, 0) } };

        Assert.That(StageGeometry.Problems(boxes, leaders), Is.Empty);
    }

    [Test]
    public void Problems_CrossingLeaders_AreReported()
    {
        var boxes = new[] { (0, 0, 10, 10), (0, 200, 10, 10) };
        var leaders = new IReadOnlyList<(int X, int Y)>[]
        {
            new[] { (10, 5), (300, 205) },
            new[] { (10, 205), (300, 5) },
        };

        Assert.That(
            StageGeometry.Problems(boxes, leaders),
            Is.EqualTo(new[] { new StageProblem(StageProblemKind.LeadersCross, 0, 1) }));
    }

    [Test]
    public void Problems_ALeaderThroughAnotherBox_IsReported_ButNotThroughItsOwn()
    {
        var boxes = new[] { (0, 0, 100, 50), (200, 0, 100, 50) };
        var leaders = new IReadOnlyList<(int X, int Y)>[]
        {
            new[] { (100, 25), (400, 40) },
            new[] { (300, 25), (500, 0) },
        };

        Assert.That(
            StageGeometry.Problems(boxes, leaders),
            Is.EqualTo(new[] { new StageProblem(StageProblemKind.LeaderThroughBox, 0, 1) }));
    }

    [Test]
    public void PopoverRect_ABoxRightOfCentre_OpensToItsRight_WhenThereIsRoom()
    {
        var rect = StageGeometry.PopoverRect((800, 100, 184, 112), (300, 200), 600, (1400, 720));

        Assert.That(rect, Is.EqualTo((996, 100, 300, 200)));
    }

    [Test]
    public void PopoverRect_FlipsToTheOtherSide_WhenThePreferredSideHasNoRoom()
    {
        var rect = StageGeometry.PopoverRect((800, 100, 184, 112), (300, 200), 600, (1200, 720));

        Assert.That(rect, Is.EqualTo((488, 100, 300, 200)));
    }

    [Test]
    public void PopoverRect_ABoxLeftOfCentre_PrefersItsLeft_AndFlipsRightWhenCramped()
    {
        var rect = StageGeometry.PopoverRect((100, 100, 184, 112), (300, 200), 600, (1200, 720));

        Assert.That(rect, Is.EqualTo((296, 100, 300, 200)));
    }

    [Test]
    public void PopoverRect_IsClampedVertically_InsideTheStage()
    {
        var rect = StageGeometry.PopoverRect((800, 600, 184, 112), (300, 200), 600, (1400, 720));

        Assert.That(rect, Is.EqualTo((996, 520, 300, 200)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dimenship.Shell.Tests --filter FullyQualifiedName~StageGeometryTests`
Expected: build FAILS — `StageGeometry`, `StageFit`, `StageProblem` do not exist.

- [ ] **Step 3: Implement `src/Dimenship.Shell/StageGeometry.cs`**

```csharp
namespace Dimenship.Shell;

/// <summary>How a frame's artwork is fitted into the stage: a uniform scale and a centring offset.</summary>
public readonly record struct StageFit(int ScalePermille, int OffsetX, int OffsetY);

public enum StageProblemKind
{
    BoxesOverlap,
    LeadersCross,
    LeaderThroughBox,
}

/// <summary>
/// One authoring problem at the current stage size. <see cref="First"/> and <see cref="Second"/>
/// are socket indices: two boxes, two leaders, or a leader and the box it passes through.
/// </summary>
public sealed record StageProblem(StageProblemKind Kind, int First, int Second);

/// <summary>
/// Where the loadout composer's frame art, fitting boxes, leader lines and popovers sit. Artwork
/// coordinates in, stage pixels out — the stage-side counterpart of <see cref="GraphGeometry"/>, in
/// its idiom: integer tuples throughout, scale as permille, so every case is exactly testable
/// without an editor.
/// <para>
/// Box <b>positions</b> scale with the artwork and box <b>sizes</b> do not: a box carries text, and
/// text stays at the palette's sizes so it is sharp at every scale. The consequence is that boxes
/// can collide at small sizes, and <see cref="Problems"/> is how the view finds out. Nothing here
/// moves a box to avoid a collision — positions are authored, and an automatic layout would be a
/// second, unauthored answer to where a box belongs.
/// </para>
/// </summary>
public static class StageGeometry
{
    /// <summary>How far a leader runs straight out of its box before turning toward its anchor.</summary>
    public const int LeaderStub = 16;

    /// <summary>The space between a box and a popover opened beside it.</summary>
    public const int PopoverGap = 12;

    /// <summary>
    /// Letterboxes the canvas into the stage and centres it. Uniform scale only: a stretched
    /// drawing is a different machine.
    /// </summary>
    public static StageFit Fit((int W, int H) canvas, (int W, int H) stage)
    {
        if (canvas.W <= 0 || canvas.H <= 0 || stage.W <= 0 || stage.H <= 0)
        {
            return new StageFit(0, 0, 0);
        }

        var scale = (int)Math.Min((long)stage.W * 1000 / canvas.W, (long)stage.H * 1000 / canvas.H);

        return new StageFit(
            scale,
            (stage.W - Scale(canvas.W, scale)) / 2,
            (stage.H - Scale(canvas.H, scale)) / 2);
    }

    public static int Scale(int value, int permille) => (int)((long)value * permille / 1000);

    public static (int X, int Y) ToStage(StageFit fit, (int X, int Y) point) =>
        (fit.OffsetX + Scale(point.X, fit.ScalePermille), fit.OffsetY + Scale(point.Y, fit.ScalePermille));

    /// <summary>A box of fixed pixel size centred on its scaled centre, clamped inside the stage.</summary>
    public static (int X, int Y, int W, int H) BoxRect(
        StageFit fit, (int X, int Y) centre, (int W, int H) box, (int W, int H) stage)
    {
        var (x, y) = ToStage(fit, centre);

        return (
            Clamp(x - (box.W / 2), stage.W - box.W),
            Clamp(y - (box.H / 2), stage.H - box.H),
            box.W,
            box.H);
    }

    /// <summary>
    /// A leader from a box to its anchor: leave the middle of the side facing the anchor, run
    /// <see cref="LeaderStub"/> straight out, then go directly to the anchor — the shape the ticket's
    /// sketch draws. The stub never runs past an anchor closer than its length.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> Leader((int X, int Y, int W, int H) box, (int X, int Y) anchor)
    {
        var midX = box.X + (box.W / 2);
        var midY = box.Y + (box.H / 2);
        var right = box.X + box.W;
        var bottom = box.Y + box.H;

        if (anchor.X >= right)
        {
            return new[] { (right, midY), (Math.Min(right + LeaderStub, anchor.X), midY), anchor };
        }

        if (anchor.X <= box.X)
        {
            return new[] { (box.X, midY), (Math.Max(box.X - LeaderStub, anchor.X), midY), anchor };
        }

        if (anchor.Y >= bottom)
        {
            return new[] { (midX, bottom), (midX, Math.Min(bottom + LeaderStub, anchor.Y)), anchor };
        }

        if (anchor.Y <= box.Y)
        {
            return new[] { (midX, box.Y), (midX, Math.Max(box.Y - LeaderStub, anchor.Y)), anchor };
        }

        // The anchor is under the box. Drawing a line to it would be drawing inside the box, and
        // Problems cannot see it either; the box covering its own connector is visible on its own.
        return new[] { (midX, midY), anchor };
    }

    /// <summary>
    /// Every box overlapping another, leader crossing another, and leader passing through a box
    /// other than its own. Boxes that only share an edge do not overlap. Index <c>i</c> of
    /// <paramref name="leaders"/> belongs to box <c>i</c>.
    /// </summary>
    public static IReadOnlyList<StageProblem> Problems(
        IReadOnlyList<(int X, int Y, int W, int H)> boxes,
        IReadOnlyList<IReadOnlyList<(int X, int Y)>> leaders)
    {
        var problems = new List<StageProblem>();

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (Overlaps(boxes[i], boxes[j]))
                {
                    problems.Add(new StageProblem(StageProblemKind.BoxesOverlap, i, j));
                }
            }
        }

        for (var i = 0; i < leaders.Count; i++)
        {
            for (var j = i + 1; j < leaders.Count; j++)
            {
                if (Cross(leaders[i], leaders[j]))
                {
                    problems.Add(new StageProblem(StageProblemKind.LeadersCross, i, j));
                }
            }
        }

        for (var i = 0; i < leaders.Count; i++)
        {
            for (var j = 0; j < boxes.Count; j++)
            {
                if (i != j && Enters(leaders[i], boxes[j]))
                {
                    problems.Add(new StageProblem(StageProblemKind.LeaderThroughBox, i, j));
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Where a popover opens beside a box: on the side facing away from the artwork's centre, so it
    /// covers margin rather than the machine; on the other side when the preferred one has no room;
    /// clamped inside the stage either way, top-aligned with the box.
    /// </summary>
    public static (int X, int Y, int W, int H) PopoverRect(
        (int X, int Y, int W, int H) box, (int W, int H) popover, int centreX, (int W, int H) stage)
    {
        var left = box.X - PopoverGap - popover.W;
        var right = box.X + box.W + PopoverGap;
        var preferRight = box.X + (box.W / 2) >= centreX;

        var x = preferRight
            ? (Fits(right) ? right : Fits(left) ? left : right)
            : (Fits(left) ? left : Fits(right) ? right : left);

        return (Clamp(x, stage.W - popover.W), Clamp(box.Y, stage.H - popover.H), popover.W, popover.H);

        bool Fits(int candidate) => candidate >= 0 && candidate + popover.W <= stage.W;
    }

    private static int Clamp(int value, int max) => Math.Max(0, Math.Min(value, max));

    private static bool Overlaps((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    private static bool Cross(IReadOnlyList<(int X, int Y)> a, IReadOnlyList<(int X, int Y)> b)
    {
        for (var i = 1; i < a.Count; i++)
        {
            for (var j = 1; j < b.Count; j++)
            {
                if (SegmentsIntersect(a[i - 1], a[i], b[j - 1], b[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Enters(IReadOnlyList<(int X, int Y)> leader, (int X, int Y, int W, int H) box)
    {
        var corners = new[]
        {
            (box.X, box.Y), (box.X + box.W, box.Y), (box.X + box.W, box.Y + box.H), (box.X, box.Y + box.H),
        };

        for (var i = 0; i < leader.Count; i++)
        {
            if (Inside(leader[i], box))
            {
                return true;
            }

            if (i == 0)
            {
                continue;
            }

            for (var edge = 0; edge < 4; edge++)
            {
                if (SegmentsIntersect(leader[i - 1], leader[i], corners[edge], corners[(edge + 1) % 4]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Inside((int X, int Y) p, (int X, int Y, int W, int H) box) =>
        p.X > box.X && p.X < box.X + box.W && p.Y > box.Y && p.Y < box.Y + box.H;

    private static bool SegmentsIntersect((int X, int Y) p1, (int X, int Y) p2, (int X, int Y) q1, (int X, int Y) q2)
    {
        var o1 = Orientation(p1, p2, q1);
        var o2 = Orientation(p1, p2, q2);
        var o3 = Orientation(q1, q2, p1);
        var o4 = Orientation(q1, q2, p2);

        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        return (o1 == 0 && OnSegment(p1, p2, q1))
            || (o2 == 0 && OnSegment(p1, p2, q2))
            || (o3 == 0 && OnSegment(q1, q2, p1))
            || (o4 == 0 && OnSegment(q1, q2, p2));
    }

    private static int Orientation((int X, int Y) p, (int X, int Y) q, (int X, int Y) r) =>
        Math.Sign(((long)(q.X - p.X) * (r.Y - p.Y)) - ((long)(q.Y - p.Y) * (r.X - p.X)));

    private static bool OnSegment((int X, int Y) p, (int X, int Y) q, (int X, int Y) r) =>
        r.X >= Math.Min(p.X, q.X) && r.X <= Math.Max(p.X, q.X)
        && r.Y >= Math.Min(p.Y, q.Y) && r.Y <= Math.Max(p.Y, q.Y);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Dimenship.Shell.Tests --filter FullyQualifiedName~StageGeometryTests`
Expected: all PASS.

Note on `Problems_ALeaderThroughAnotherBox_IsReported_ButNotThroughItsOwn`: leader 0 slopes from (100,25) to (400,40) and is at y≈30 when it reaches box 1's left edge, so it passes through box 1. Leader 1 starts on box 1's own edge and climbs away to the right, above leader 0, so the two never touch and exactly one problem is reported. (A leader starting *on* another leader's path counts as a crossing, which is why leader 1 does not start on y=25.)

- [ ] **Step 5: Run the whole Shell suite, then commit**

Run: `dotnet test tests/Dimenship.Shell.Tests`
Expected: all PASS.

```bash
git add src/Dimenship.Shell/StageGeometry.cs tests/Dimenship.Shell.Tests/StageGeometryTests.cs
git commit -F - <<'EOF'
feat: the loadout stage's geometry is integer arithmetic with tests behind it

StageGeometry fits a frame's artwork into the stage, places fixed-size fitting
boxes on their scaled centres, routes each leader out of the side facing its
anchor with a short straight stub, and places a popover beside a box on the
side away from the machine, flipping when there is no room.

Box sizes do not scale because boxes carry text, so boxes can collide on a
small stage. Problems reports overlaps, crossing leaders and leaders through
another box; nothing here moves a box, because positions are authored and an
automatic layout would be a second answer to where one belongs.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 4: The projection look — palette, popover chrome, glow shader, and the artwork

**Files:**
- Modify: `dimenship/scripts/ui/ShellPalette.cs`
- Modify: `dimenship/scripts/ui/ShellTheme.cs`
- Modify: `dimenship/scripts/ui/focus/IconSlot.cs`
- Create: `dimenship/assets/projection.gdshader`
- Create: `dimenship/scripts/ui/focus/loadouts/ProjectionGlow.cs`
- Create: `dimenship/assets/loadouts/frames/surveyor_frame.svg`, `surveyor_frame.json`, `surveyor_frame.svg.import`
- Create: `dimenship/assets/loadouts/frames/hauler_frame.svg`, `hauler_frame.json`, `hauler_frame.svg.import`
- Create: `dimenship/assets/loadouts/fittings/*.svg` (16 files) and their generated `.import` files

**Interfaces:**
- Produces:
  - `ShellPalette.Projection`, `ShellPalette.ProjectionGuide` (`Color`)
  - `ShellTheme.Popover() : StyleBoxFlat`
  - `IconSlot.FromPath(string path, int size, Color tint) : IconSlot`, `IconSlot.SetPath(string path)`
  - `ProjectionGlow.Create() : ShaderMaterial?`
  - Frame files named by frame id: `res://assets/loadouts/frames/{frameId}.json`, naming `"artwork": "{frameId}.svg"`
  - Fitting images at `LoadoutCatalog.FittingArtPath(id)`

- [ ] **Step 1: Palette entries**

In `ShellPalette.cs`, after `TextTitle`, add:

```csharp
    /// <summary>
    /// The loadout composer's line art: a pale cyan, as a laser projected onto dark glass. Kept
    /// apart from <see cref="Accent"/> on purpose — the art is always on screen, and selection has
    /// to stand out against it, so the two cannot be one colour.
    /// </summary>
    public static readonly Color Projection = Color.FromHtml("9CE6F2");

    /// <summary>
    /// An unselected leader line: the projection colour, quieted, so five guides do not compete
    /// with the drawing they point into. A selected leader uses <see cref="Accent"/>.
    /// </summary>
    public static readonly Color ProjectionGuide = Color.FromHtml("2E5A66");
```

- [ ] **Step 2: The popover stylebox**

In `ShellTheme.cs`, after `Chip`, add:

```csharp
    /// <summary>
    /// A popover's chrome: the loadout composer's fitting picker and frame chooser. An opaque panel
    /// fill rather than glass, because a popover sits over the glowing frame art and a translucent
    /// one would put line art behind its text. The accent border ties it to the box it opened from,
    /// which is selected in the same colour.
    /// </summary>
    public static StyleBoxFlat Popover()
    {
        var box = Surface(ShellPalette.BgPanel, ShellPalette.RadiusLg, ShellPalette.Accent);
        box.SetContentMarginAll(ShellPalette.SpaceMd);
        return box;
    }
```

- [ ] **Step 3: `IconSlot` learns to load from a full path**

In `IconSlot.cs`, replace the existing `SetIcon` method with the three members below:

```csharp
    /// <summary>
    /// A slot for art that is not an icon — a loadout fitting's standalone image — loaded from a
    /// full <c>res://</c> path on the same quiet terms as an icon: missing is an empty slot of the
    /// right size, never a fault, and it is tinted from the palette like everything else.
    /// </summary>
    public static IconSlot FromPath(string path, int size, Color tint)
    {
        var slot = new IconSlot(size, tint);
        slot.SetPath(path);
        return slot;
    }

    /// <summary>Swaps the icon shown, for a slot whose subject changes between snapshots.</summary>
    public void SetIcon(string domain, string name) => SetPath($"{Root}/{domain}/{name}.svg");

    /// <summary>Swaps the art shown by full path, with the same remembered-path guard.</summary>
    public void SetPath(string path)
    {
        if (_shown == path)
        {
            return;
        }

        _shown = path;
        Texture = Load(path);
    }
```

- [ ] **Step 4: The glow shader**

Create `dimenship/assets/projection.gdshader`:

```glsl
shader_type canvas_item;

// The loadout composer's frame art, drawn as a laser projection on dark glass.
//
// The artwork is white line work; this shader replaces its colour with `tint` and adds a soft halo
// sampled from the alpha around each pixel. The colour arrives as a uniform from
// ShellPalette.Projection, so the palette stays the single source of truth for colour. The glow
// belongs to the drawing only — no text is drawn through this material, so text stays sharp.
// A SubViewport with engine glow was rejected: its bloom would reach the text and the boxes too.

uniform vec4 tint : source_color = vec4(1.0);
uniform float halo_px : hint_range(0.0, 8.0) = 3.0;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.35;

void fragment() {
	float line = texture(TEXTURE, UV).a;
	float halo = 0.0;

	// Twelve directions at two radii: enough for a halo with no visible spokes around 1-2px
	// strokes, and 24 fetches on a texture that is mostly transparent.
	for (int i = 0; i < 12; i++) {
		float angle = float(i) * 0.5235988;
		vec2 offset = vec2(cos(angle), sin(angle)) * halo_px * TEXTURE_PIXEL_SIZE;
		halo += texture(TEXTURE, UV + offset).a;
		halo += texture(TEXTURE, UV + offset * 0.5).a;
	}

	halo /= 24.0;
	COLOR = vec4(tint.rgb, max(line, halo * halo_strength) * tint.a);
}
```

- [ ] **Step 5: `ProjectionGlow`**

Create `dimenship/scripts/ui/focus/loadouts/ProjectionGlow.cs`:

```csharp
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Builds the material the loadout stage draws frame art through: <c>projection.gdshader</c> with
/// its tint set from <see cref="ShellPalette.Projection"/>. Optional in the way
/// <see cref="ShellBackdrop"/>'s frost is: a missing shader returns null, and the caller falls
/// back to a plain palette modulate rather than failing — the art still reads, only without its
/// glow.
/// </summary>
public static class ProjectionGlow
{
    private const string ShaderPath = "res://assets/projection.gdshader";

    private static bool _resolved;
    private static Shader? _shader;

    /// <summary>A fresh material per use: uniforms are per-material, and there is nothing to share.</summary>
    public static ShaderMaterial? Create()
    {
        if (!_resolved)
        {
            _resolved = true;

            if (ResourceLoader.Exists(ShaderPath))
            {
                _shader = GD.Load<Shader>(ShaderPath);
            }
            else
            {
                GD.PushWarning($"{ShaderPath} not found; frame art is drawn without its glow.");
            }
        }

        if (_shader is null)
        {
            return null;
        }

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("tint", ShellPalette.Projection);
        return material;
    }
}
```

- [ ] **Step 6: The Surveyor Frame**

Create `dimenship/assets/loadouts/frames/surveyor_frame.svg`. The `r="10"` port rings are the anchors in the sidecar and must stay in step with it: tool (330,160), sensor (648,222), power (575,399), investigation (792,390).

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="720" viewBox="0 0 1200 720" fill="none" stroke="#ffffff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
  <!-- Surveyor Frame. Line art only: colour and glow are the shader's. Every ringed port is a
       socket connector, drawn whether or not anything is fitted. Port centres must match
       surveyor_frame.json. -->
  <rect x="430" y="330" width="340" height="110" rx="14"/>
  <path d="M430 360 H770" stroke-width="1"/>
  <path d="M460 345 H520 M680 345 H740" stroke-width="1"/>
  <path d="M450 422 H530 M640 422 H750" stroke-width="1"/>
  <path d="M500 440 V448 M700 440 V448"/>
  <path d="M470 452 H730" stroke-width="1"/>
  <circle cx="500" cy="500" r="52"/>
  <circle cx="500" cy="500" r="40" stroke-width="1"/>
  <circle cx="500" cy="500" r="14"/>
  <path d="M500 448 V460 M500 540 V552 M448 500 H460 M540 500 H552" stroke-width="1"/>
  <circle cx="700" cy="500" r="52"/>
  <circle cx="700" cy="500" r="40" stroke-width="1"/>
  <circle cx="700" cy="500" r="14"/>
  <path d="M700 448 V460 M700 540 V552 M648 500 H660 M740 500 H752" stroke-width="1"/>
  <path d="M640 330 V236 M656 330 V236 M628 236 H668"/>
  <path d="M640 300 H656 M640 268 H656" stroke-width="1"/>
  <circle cx="648" cy="222" r="10"/>
  <circle cx="648" cy="222" r="4" stroke-width="1"/>
  <circle cx="450" cy="330" r="14"/>
  <path d="M455 318 L371 243 M445 340 L361 253"/>
  <circle cx="366" cy="248" r="12"/>
  <path d="M377 244 L341 167 M355 252 L321 170"/>
  <circle cx="330" cy="160" r="10"/>
  <circle cx="330" cy="160" r="4" stroke-width="1"/>
  <rect x="540" y="375" width="70" height="48" rx="4"/>
  <circle cx="575" cy="399" r="10"/>
  <circle cx="575" cy="399" r="4" stroke-width="1"/>
  <path d="M770 382 H782 V398 H770"/>
  <circle cx="792" cy="390" r="10"/>
  <circle cx="792" cy="390" r="4" stroke-width="1"/>
</svg>
```

Create `dimenship/assets/loadouts/frames/surveyor_frame.json`:

```json
{
  "notes": "Placement for the Surveyor Frame's artwork (surveyor_frame.svg). Coordinates are SVG user units in the artwork's own 1200x720 viewBox. anchor is the centre of a socket's ringed port in the drawing; box is the centre of its fitting box, whose size belongs to the theme and does not scale with the stage. Placement only: which fitting fits which socket is LoadoutCatalog's. Keep boxes clear of each other and leaders uncrossed at the smallest window (1280x720); the stage says so when they are not.",
  "frame": "surveyor_frame",
  "artwork": "surveyor_frame.svg",
  "canvas": { "width": 1200, "height": 720 },
  "sockets": [
    { "id": "tool", "anchor": { "x": 330, "y": 160 }, "box": { "x": 170, "y": 160 } },
    { "id": "sensor", "anchor": { "x": 648, "y": 222 }, "box": { "x": 1030, "y": 160 } },
    { "id": "power", "anchor": { "x": 575, "y": 399 }, "box": { "x": 170, "y": 480 } },
    { "id": "investigation", "anchor": { "x": 792, "y": 390 }, "box": { "x": 1030, "y": 480 } }
  ]
}
```

- [ ] **Step 7: The Hauler Frame**

Create `dimenship/assets/loadouts/frames/hauler_frame.svg`. Anchors: tool (320,175), cargo (700,252), power (555,393), defense (862,400), sensor (487,222).

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="720" viewBox="0 0 1200 720" fill="none" stroke="#ffffff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
  <!-- Hauler Frame. Line art only: colour and glow are the shader's. Every ringed port is a
       socket connector, drawn whether or not anything is fitted. Port centres must match
       hauler_frame.json. -->
  <rect x="400" y="320" width="440" height="120" rx="12"/>
  <path d="M400 350 H840" stroke-width="1"/>
  <path d="M420 336 H470 M780 336 H820" stroke-width="1"/>
  <path d="M600 320 V270 H820 V320"/>
  <path d="M640 270 V320 M680 270 V320 M740 270 V320 M780 270 V320" stroke-width="1"/>
  <path d="M700 262 V270"/>
  <circle cx="700" cy="252" r="10"/>
  <circle cx="700" cy="252" r="4" stroke-width="1"/>
  <path d="M420 430 H830" stroke-width="1"/>
  <path d="M460 424 V436 M560 424 V436 M660 424 V436 M760 424 V436" stroke-width="1"/>
  <rect x="840" y="380" width="12" height="40" rx="2"/>
  <circle cx="862" cy="400" r="10"/>
  <circle cx="862" cy="400" r="4" stroke-width="1"/>
  <path d="M430 452 H810" stroke-width="1"/>
  <path d="M470 440 V454 M620 440 V454 M770 440 V454"/>
  <circle cx="470" cy="500" r="46"/>
  <circle cx="470" cy="500" r="34" stroke-width="1"/>
  <circle cx="470" cy="500" r="12"/>
  <path d="M470 454 V466 M470 534 V546 M424 500 H436 M504 500 H516" stroke-width="1"/>
  <circle cx="620" cy="500" r="46"/>
  <circle cx="620" cy="500" r="34" stroke-width="1"/>
  <circle cx="620" cy="500" r="12"/>
  <path d="M620 454 V466 M620 534 V546 M574 500 H586 M654 500 H666" stroke-width="1"/>
  <circle cx="770" cy="500" r="46"/>
  <circle cx="770" cy="500" r="34" stroke-width="1"/>
  <circle cx="770" cy="500" r="12"/>
  <path d="M770 454 V466 M770 534 V546 M724 500 H736 M804 500 H816" stroke-width="1"/>
  <path d="M480 320 V236 M494 320 V236 M470 236 H504"/>
  <path d="M480 290 H494 M480 262 H494" stroke-width="1"/>
  <circle cx="487" cy="222" r="10"/>
  <circle cx="487" cy="222" r="4" stroke-width="1"/>
  <circle cx="420" cy="320" r="14"/>
  <path d="M426 308 L356 244 M414 332 L342 258"/>
  <circle cx="348" cy="250" r="12"/>
  <path d="M358 244 L330 183 M338 254 L312 184"/>
  <circle cx="320" cy="175" r="10"/>
  <circle cx="320" cy="175" r="4" stroke-width="1"/>
  <rect x="520" y="370" width="70" height="46" rx="4"/>
  <circle cx="555" cy="393" r="10"/>
  <circle cx="555" cy="393" r="4" stroke-width="1"/>
</svg>
```

Create `dimenship/assets/loadouts/frames/hauler_frame.json`:

```json
{
  "notes": "Placement for the Hauler Frame's artwork (hauler_frame.svg). Coordinates are SVG user units in the artwork's own 1200x720 viewBox. anchor is the centre of a socket's ringed port in the drawing; box is the centre of its fitting box, whose size belongs to the theme and does not scale with the stage. Placement only: which fitting fits which socket is LoadoutCatalog's. Keep boxes clear of each other and leaders uncrossed at the smallest window (1280x720); the stage says so when they are not.",
  "frame": "hauler_frame",
  "artwork": "hauler_frame.svg",
  "canvas": { "width": 1200, "height": 720 },
  "sockets": [
    { "id": "tool", "anchor": { "x": 320, "y": 175 }, "box": { "x": 170, "y": 120 } },
    { "id": "cargo", "anchor": { "x": 700, "y": 252 }, "box": { "x": 1030, "y": 160 } },
    { "id": "power", "anchor": { "x": 555, "y": 393 }, "box": { "x": 170, "y": 600 } },
    { "id": "defense", "anchor": { "x": 862, "y": 400 }, "box": { "x": 1030, "y": 480 } },
    { "id": "sensor", "anchor": { "x": 487, "y": 222 }, "box": { "x": 170, "y": 360 } }
  ]
}
```

- [ ] **Step 8: Mark the frame SVGs Keep File**

They are rasterised at runtime from source (`Image.LoadSvgFromString`), so the source must reach an export untouched. Create `dimenship/assets/loadouts/frames/surveyor_frame.svg.import` and `dimenship/assets/loadouts/frames/hauler_frame.svg.import`, each with exactly:

```ini
[remap]

importer="keep"
```

- [ ] **Step 9: The sixteen fitting images**

Every file under `dimenship/assets/loadouts/fittings/` opens with this root element and closes with `</svg>`; only the body differs:

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96" fill="none" stroke="#ffffff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
```

`mining_head_mk1.svg`:
```svg
  <rect x="58" y="34" width="26" height="28" rx="3"/>
  <path d="M58 36 L20 48 L58 60 Z"/>
  <path d="M50 38 L46 58 M42 41 L38 55 M34 44 L31 52" stroke-width="1"/>
  <path d="M64 40 V56 M72 40 V56" stroke-width="1"/>
  <path d="M84 42 H90 M84 54 H90"/>
```

`salvage_cutter.svg`:
```svg
  <circle cx="40" cy="48" r="24"/>
  <circle cx="40" cy="48" r="6"/>
  <path d="M40 24 L43 18 M57 31 L63 28 M64 48 L70 50 M57 65 L60 71 M40 72 L37 78 M23 65 L17 67 M16 48 L10 46 M23 31 L20 25" stroke-width="1"/>
  <path d="M46 48 H86 M86 40 V56"/>
  <rect x="70" y="42" width="12" height="12" rx="2" stroke-width="1"/>
```

`precision_manipulator.svg`:
```svg
  <rect x="60" y="40" width="26" height="16" rx="3"/>
  <path d="M60 44 L40 36 L24 40 M60 52 L40 60 L24 56"/>
  <path d="M24 40 L18 44 M24 56 L18 52" stroke-width="1"/>
  <circle cx="40" cy="36" r="3" stroke-width="1"/>
  <circle cx="40" cy="60" r="3" stroke-width="1"/>
  <path d="M86 48 H92"/>
```

`basic_sensor_array.svg`:
```svg
  <rect x="18" y="26" width="60" height="30" rx="8"/>
  <circle cx="36" cy="41" r="8"/>
  <circle cx="36" cy="41" r="3" stroke-width="1"/>
  <circle cx="60" cy="41" r="8"/>
  <circle cx="60" cy="41" r="3" stroke-width="1"/>
  <path d="M48 56 V72 M36 76 H60"/>
```

`deep_scan_array.svg`:
```svg
  <rect x="10" y="30" width="76" height="28" rx="6"/>
  <circle cx="28" cy="44" r="7"/>
  <circle cx="48" cy="44" r="9"/>
  <circle cx="48" cy="44" r="3" stroke-width="1"/>
  <circle cx="68" cy="44" r="7"/>
  <path d="M30 22 Q48 12 66 22" stroke-width="1"/>
  <path d="M48 58 V72 M34 76 H62"/>
```

`phase_resonance_probe.svg`:
```svg
  <path d="M28 60 Q48 20 68 60"/>
  <path d="M24 60 H72"/>
  <circle cx="48" cy="34" r="4"/>
  <path d="M48 38 V60" stroke-width="1"/>
  <path d="M36 22 Q48 14 60 22 M30 16 Q48 4 66 16" stroke-width="1"/>
  <path d="M48 60 V74 M38 78 H58"/>
```

`standard_cargo_pod.svg`:
```svg
  <rect x="16" y="28" width="64" height="44" rx="4"/>
  <path d="M16 38 H80 M36 28 V72 M60 28 V72" stroke-width="1"/>
  <path d="M40 22 H56"/>
```

`expanded_hold_pod.svg`:
```svg
  <rect x="8" y="30" width="80" height="40" rx="4"/>
  <path d="M48 30 V70"/>
  <path d="M8 40 H88 M28 30 V70 M68 30 V70" stroke-width="1"/>
  <path d="M20 24 H36 M60 24 H76"/>
```

`power_core_mk1.svg`:
```svg
  <rect x="18" y="32" width="60" height="32" rx="6"/>
  <path d="M34 32 V64 M62 32 V64" stroke-width="1"/>
  <circle cx="48" cy="48" r="6" stroke-width="1"/>
  <path d="M78 42 H86 M78 54 H86"/>
```

`power_core_mk2.svg`:
```svg
  <rect x="12" y="28" width="72" height="40" rx="8"/>
  <path d="M26 28 V68 M40 28 V68 M56 28 V68 M70 28 V68" stroke-width="1"/>
  <path d="M84 40 H90 M84 56 H90"/>
  <path d="M30 22 H66 M30 74 H66"/>
```

`endurance_cell.svg`:
```svg
  <rect x="30" y="20" width="36" height="60" rx="4"/>
  <path d="M40 20 V14 H56 V20"/>
  <path d="M30 40 H66 M30 60 H66" stroke-width="1"/>
  <path d="M48 26 V34 M44 30 H52" stroke-width="1"/>
```

`ablative_plating.svg`:
```svg
  <path d="M30 20 L46 28 V46 L30 54 L14 46 V28 Z"/>
  <path d="M62 20 L78 28 V46 L62 54 L46 46 V28 Z"/>
  <path d="M46 46 L62 54 V72 L46 80 L30 72 V54 Z"/>
```

`countermeasure_pod.svg`:
```svg
  <rect x="14" y="34" width="68" height="28" rx="14"/>
  <circle cx="32" cy="48" r="5"/>
  <circle cx="48" cy="48" r="5"/>
  <circle cx="64" cy="48" r="5"/>
  <path d="M40 34 V26 H56 V34" stroke-width="1"/>
```

`evidence_collector.svg`:
```svg
  <rect x="28" y="30" width="40" height="50" rx="4"/>
  <path d="M24 30 H72 V24 H24 Z"/>
  <path d="M40 24 V16 H56 V24"/>
  <path d="M28 46 H68" stroke-width="1"/>
  <rect x="38" y="54" width="20" height="16" rx="2" stroke-width="1"/>
```

`forensic_sampler.svg`:
```svg
  <rect x="50" y="22" width="18" height="44" rx="3"/>
  <path d="M59 66 V80"/>
  <path d="M56 80 L59 88 L62 80" stroke-width="1"/>
  <path d="M22 40 H34 V70 Q34 76 28 76 Q22 76 22 70 Z"/>
  <path d="M22 58 H34" stroke-width="1"/>
  <path d="M50 34 H40 V40" stroke-width="1"/>
```

`contradiction_logger.svg`:
```svg
  <rect x="18" y="26" width="60" height="44" rx="4"/>
  <rect x="26" y="34" width="44" height="20" rx="2" stroke-width="1"/>
  <path d="M30 40 H50 M30 46 H62" stroke-width="1"/>
  <path d="M26 62 H38 M44 62 H52 M58 62 H70"/>
```

Check the set against the catalog:

Run: `ls dimenship/assets/loadouts/fittings/*.svg | wc -l`
Expected: `16`, each file name equal to a `LoadoutCatalog.Fittings` id.

- [ ] **Step 10: Import, then raise the fitting images to the icons' import scale**

```bash
GODOT=/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe
"$GODOT" --headless --path dimenship --import
sed -i 's|^svg/scale=1.0$|svg/scale=2.0|' dimenship/assets/loadouts/fittings/*.svg.import
"$GODOT" --headless --path dimenship --import
```

Verify:
- `grep -l "svg/scale=2.0" dimenship/assets/loadouts/fittings/*.svg.import | wc -l` → `16`
- `grep -h "importer=" dimenship/assets/loadouts/frames/*.svg.import` → `importer="keep"` twice
- `ls dimenship/assets/projection.gdshader.uid dimenship/scripts/ui/focus/loadouts/ProjectionGlow.cs.uid` → both exist

- [ ] **Step 11: Build and commit**

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q`
Expected: `0 Error(s)`. Nothing visible changes yet; the art is first drawn in Task 5.

```bash
git add dimenship/scripts/ui/ShellPalette.cs dimenship/scripts/ui/ShellTheme.cs dimenship/scripts/ui/focus/IconSlot.cs \
  dimenship/assets/projection.gdshader dimenship/assets/projection.gdshader.uid \
  dimenship/scripts/ui/focus/loadouts/ProjectionGlow.cs dimenship/scripts/ui/focus/loadouts/ProjectionGlow.cs.uid \
  dimenship/assets/loadouts
git commit -F - <<'EOF'
feat: the loadout frames and fittings are drawn as projected line art

Two frame illustrations and sixteen fitting images, hand-written as white
line art with no colour of their own, plus the glow shader that tints them
from ShellPalette.Projection and adds a halo sampled from the strokes' alpha.
The glow is the drawing's alone, so no text is ever blurred.

Each frame carries a JSON placement sidecar naming, per socket id, where its
connector port sits in the drawing and where its box is centred, both in the
artwork's viewBox. Frame SVGs are Keep File because the stage rasterises them
at runtime at its own scale; fitting images import at the icons' scale.

The palette gains Projection and ProjectionGuide, the theme a popover box,
and IconSlot a way to load art by full path.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 5: The stage draws the frame with a box per socket, and falls back when its art is bad

**Files:**
- Create: `dimenship/scripts/ui/focus/loadouts/FrameArtLibrary.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/FittingBox.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/LeaderLayer.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/LoadoutStage.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutsFocus.cs`
- Delete: `dimenship/scripts/ui/focus/loadouts/SocketRow.cs`, `SocketRow.cs.uid`

**Interfaces:**
- Consumes: `FrameArtSerializer.Load/Check`, `FrameArt`, `StageGeometry.*` (Tasks 2–3); `ProjectionGlow.Create`, `IconSlot.FromPath/SetPath`, `ShellPalette.Projection/ProjectionGuide`, `LoadoutCatalog.FittingArtPath` (Tasks 1, 4)
- Produces:
  - `public sealed record FrameArtEntry(FrameArt? Art, string? Svg, IReadOnlyList<string> Warnings);`
  - `FrameArtLibrary.For(FrameDef frame) : FrameArtEntry`, `FrameArtLibrary.Rasterise(FrameDef frame, int scalePermille) : ImageTexture?`
  - `FittingBox(int index, SocketDef socket)`, `FittingBox.Width = 184`, `Height = 112`, `ImageSize = 64`, `Refresh(FittingDef? fitting, bool selected)`, `Action<int>? Chosen`, `Action<int>? Focused`
  - `LeaderLayer.Show(IReadOnlyList<IReadOnlyList<(int X, int Y)>> leaders, int selected)`
  - `LoadoutStage.Show(FrameDef frame, LoadoutDraft template, int selected)`, `FocusBox(int socket)`, `BoxRect(int socket) : Rect2`, `CanvasCentreX : int`, `Action<int>? SocketChosen`, `Action<int>? SocketFocused`

- [ ] **Step 1: `FrameArtLibrary`**

Create `dimenship/scripts/ui/focus/loadouts/FrameArtLibrary.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One frame's art as loaded: its placement and SVG source when both are good, or the warnings
/// saying why not. <see cref="Art"/> and <see cref="Svg"/> are null exactly when there are warnings.
/// </summary>
public sealed record FrameArtEntry(FrameArt? Art, string? Svg, IReadOnlyList<string> Warnings);

/// <summary>
/// Loads each frame's placement sidecar and SVG from <c>res://assets/loadouts/frames/</c>, named by
/// frame id, and rasterises the SVG at the size the stage draws it.
/// <para>
/// This is the Godot half of the sidecar's validation: <see cref="FrameArtSerializer"/> checks what
/// a file can get wrong on its own, and this adds what needs the catalog or the file system — the
/// socket ids against <see cref="FrameDef"/>, and whether the named artwork exists. Any warning
/// means no art for that frame; the stage falls back to a plain column and says why in words
/// (<c>2026-09-14-glass-console-loadout-editor-design.md</c>, Decision 6). Warnings are pushed once
/// per frame per process, because the result is cached and a broken file does not mend itself.
/// </para>
/// <para>
/// The SVG is rasterised at runtime rather than imported at a fixed scale, rounded up to a quarter
/// step and cached per step. A fixed import either blurs thin lines when the zone is enlarged or
/// spends memory at every size, and thin lines are the whole of this art.
/// </para>
/// </summary>
public static class FrameArtLibrary
{
    private const string Root = "res://assets/loadouts/frames";

    /// <summary>Rasterise in quarter-scale steps, so a window drag does not re-render every pixel it passes.</summary>
    private const int StepPermille = 250;

    private static readonly Dictionary<string, FrameArtEntry> Entries = new();
    private static readonly Dictionary<(string Frame, int Step), ImageTexture?> Textures = new();

    public static FrameArtEntry For(FrameDef frame)
    {
        if (!Entries.TryGetValue(frame.Id, out var entry))
        {
            entry = Load(frame);
            Entries[frame.Id] = entry;
        }

        return entry;
    }

    /// <summary>
    /// The frame's drawing rendered at least as large as <paramref name="scalePermille"/> of its
    /// canvas. Null when the frame has no good art, when the scale is zero, or when the SVG will not
    /// rasterise — which is warned once and then remembered.
    /// </summary>
    public static ImageTexture? Rasterise(FrameDef frame, int scalePermille)
    {
        var entry = For(frame);

        if (entry.Svg is null || scalePermille <= 0)
        {
            return null;
        }

        var step = (scalePermille + StepPermille - 1) / StepPermille;

        if (Textures.TryGetValue((frame.Id, step), out var cached))
        {
            return cached;
        }

        var image = new Image();
        var error = image.LoadSvgFromString(entry.Svg, step * StepPermille / 1000f);
        ImageTexture? texture = null;

        if (error == Error.Ok)
        {
            texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            GD.PushWarning($"Frame art '{frame.Id}' could not be rasterised: {error}.");
        }

        Textures[(frame.Id, step)] = texture;
        return texture;
    }

    private static FrameArtEntry Load(FrameDef frame)
    {
        var file = $"{frame.Id}.json";
        var path = $"{Root}/{file}";
        var warnings = new List<string>();
        FrameArt? art = null;
        string? svg = null;

        if (!FileAccess.FileExists(path))
        {
            warnings.Add("no placement file");
        }
        else
        {
            var result = FrameArtSerializer.Load(FileAccess.GetFileAsString(path));
            warnings.AddRange(result.Warnings);

            if (result.Art is { } parsed)
            {
                warnings.AddRange(FrameArtSerializer.Check(
                    parsed, frame.Id, frame.Sockets.Select(socket => socket.Id).ToList()));

                var artwork = $"{Root}/{parsed.Artwork}";

                if (FileAccess.FileExists(artwork))
                {
                    svg = FileAccess.GetFileAsString(artwork);
                }
                else
                {
                    warnings.Add($"artwork '{parsed.Artwork}' is missing");
                }

                art = parsed;
            }
        }

        if (warnings.Count == 0)
        {
            return new FrameArtEntry(art, svg, warnings);
        }

        var named = warnings.Select(warning => $"{file}: {warning}").ToList();

        foreach (var warning in named)
        {
            GD.PushWarning($"Frame art: {warning}");
        }

        return new FrameArtEntry(null, null, named);
    }
}
```

- [ ] **Step 2: `FittingBox`**

Create `dimenship/scripts/ui/focus/loadouts/FittingBox.cs`:

```csharp
using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One socket on the stage: what kind it is, what is fitted in it, and that fitting's image. The
/// ticket's <i>equipment box</i> — a visual element, not an inventory object; nothing is stored in
/// it.
/// <para>
/// Its size is fixed in pixels and never scales with the frame art, because it carries text and
/// text stays at the palette's sizes. Only its centre moves with the drawing.
/// </para>
/// <para>
/// An empty socket says <c>EMPTY</c> in words over a dashed <c>+</c>, drawn with
/// <see cref="ShellTheme.DrawDashedPolyline"/> — the base graph's vocabulary for "nothing is here
/// yet", so the two views say one thing one way. Selection is the card's border colour and nothing
/// else, the shell's one selection signal.
/// </para>
/// </summary>
public sealed partial class FittingBox : PanelContainer
{
    public const int Width = 184;

    /// <summary>Kind line, name line, a 64px image, the card's padding, and nothing to spare.</summary>
    public const int Height = 112;

    public const int ImageSize = 64;

    private readonly int _index;
    private readonly SocketDef _socket;

    private FittingDef? _fitting;
    private bool _selected;

    private Label _name = null!;
    private IconSlot _image = null!;
    private EmptyMark _empty = null!;

    public FittingBox(int index, SocketDef socket)
    {
        _index = index;
        _socket = socket;

        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(Width, Height);
        ClipContents = true;
    }

    /// <summary>Raised with this box's socket index on a click or <c>ui_accept</c>.</summary>
    public Action<int>? Chosen { get; set; }

    /// <summary>Raised when keyboard focus lands here, so focus and selection are one thing.</summary>
    public Action<int>? Focused { get; set; }

    public override void _Ready()
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        AddChild(column);

        var kind = new Label
        {
            Text = LoadoutCatalog.Label(_socket.Kind).ToUpperInvariant(),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        kind.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        kind.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(kind);

        _name = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        column.AddChild(_name);

        var art = new CenterContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(art);

        _image = new IconSlot(ImageSize, ShellPalette.Projection);
        art.AddChild(_image);

        _empty = new EmptyMark();
        art.AddChild(_empty);

        FocusEntered += () => Focused?.Invoke(_index);
        ApplyChrome();
    }

    public void Refresh(FittingDef? fitting, bool selected)
    {
        _fitting = fitting;
        _selected = selected;

        if (IsNodeReady())
        {
            ApplyChrome();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            GrabFocus();
            Chosen?.Invoke(_index);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            Chosen?.Invoke(_index);
            AcceptEvent();
        }
    }

    private void ApplyChrome()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Card(_selected));

        _name.Text = _fitting?.Label ?? "EMPTY";
        _name.AddThemeColorOverride(
            "font_color", _fitting is null ? ShellPalette.TextDim : ShellPalette.TextTitle);

        if (_fitting is null)
        {
            _image.Clear();
            _image.Visible = false;
            _empty.Visible = true;
            return;
        }

        _image.SetPath(LoadoutCatalog.FittingArtPath(_fitting.Id));
        _image.Visible = true;
        _empty.Visible = false;
    }

    /// <summary>The empty state's dashed square and plus, the size of the image it stands in for.</summary>
    private sealed partial class EmptyMark : Control
    {
        public EmptyMark()
        {
            CustomMinimumSize = new Vector2(ImageSize, ImageSize);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            const float inset = ShellPalette.SpaceMd;
            const float far = ImageSize - ShellPalette.SpaceMd;
            const float mid = ImageSize / 2f;
            const float arm = ShellPalette.SpaceMd;

            ShellTheme.DrawDashedPolyline(
                this,
                new[]
                {
                    new Vector2(inset, inset), new Vector2(far, inset), new Vector2(far, far),
                    new Vector2(inset, far), new Vector2(inset, inset),
                },
                ShellPalette.TextDim,
                1f);

            DrawLine(new Vector2(mid - arm, mid), new Vector2(mid + arm, mid), ShellPalette.TextDim, 1f, true);
            DrawLine(new Vector2(mid, mid - arm), new Vector2(mid, mid + arm), ShellPalette.TextDim, 1f, true);
        }
    }
}
```

- [ ] **Step 3: `LeaderLayer`**

Create `dimenship/scripts/ui/focus/loadouts/LeaderLayer.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The stage's leader lines and connector dots, drawn between the frame art and the boxes. A
/// drawing layer and nothing else — it takes no input.
/// <para>
/// The selected socket's line, dot and ring are <see cref="ShellPalette.Accent"/>, in step with its
/// box's border: one selection signal carried by three elements at once, never a second colour.
/// It is drawn last so no guide crosses over it.
/// </para>
/// </summary>
public sealed partial class LeaderLayer : Control
{
    private const float LineWidth = 1f;
    private const float DotRadius = 3f;
    private const float RingRadius = 7f;
    private const int RingSegments = 24;

    private readonly List<Vector2[]> _leaders = new();
    private int _selected = -1;

    public LeaderLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Index <c>i</c> of <paramref name="leaders"/> belongs to socket <c>i</c>; its last point is the anchor.</summary>
    public void Show(IReadOnlyList<IReadOnlyList<(int X, int Y)>> leaders, int selected)
    {
        _leaders.Clear();

        foreach (var leader in leaders)
        {
            _leaders.Add(leader.Select(point => new Vector2(point.X, point.Y)).ToArray());
        }

        _selected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (var i = 0; i < _leaders.Count; i++)
        {
            if (i != _selected)
            {
                DrawLeader(_leaders[i], selected: false);
            }
        }

        if (_selected >= 0 && _selected < _leaders.Count)
        {
            DrawLeader(_leaders[_selected], selected: true);
        }
    }

    private void DrawLeader(Vector2[] points, bool selected)
    {
        if (points.Length == 0)
        {
            return;
        }

        if (points.Length > 1)
        {
            DrawPolyline(
                points, selected ? ShellPalette.Accent : ShellPalette.ProjectionGuide, LineWidth, antialiased: true);
        }

        var anchor = points[^1];
        DrawCircle(anchor, DotRadius, selected ? ShellPalette.Accent : ShellPalette.Projection);

        if (selected)
        {
            DrawArc(anchor, RingRadius, 0f, Mathf.Tau, RingSegments, ShellPalette.Accent, LineWidth, antialiased: true);
        }
    }
}
```

- [ ] **Step 4: `LoadoutStage`**

Create `dimenship/scripts/ui/focus/loadouts/LoadoutStage.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The centre of the loadout composer: the selected template's frame drawn as projected line art,
/// one <see cref="FittingBox"/> per socket around it, and a leader line from each box to its
/// connector. Drawn the way <see cref="GraphCanvas"/> draws the base graph — lines drawn, cards as
/// child controls — back to front: the art, the <see cref="LeaderLayer"/>, the boxes, then any
/// popover.
/// <para>
/// The frame art never changes with what is fitted. The ticket's asset budget is one illustration
/// per frame, and every connector is drawn whether or not anything is in it; fittings appear only
/// in their boxes.
/// </para>
/// <para>
/// Boxes are placed where the frame's sidecar puts them and are <b>never moved</b> to avoid one
/// another. At a stage size where they collide, the stage says so in one line and warns once; an
/// automatic layout would be a second, unauthored answer to where a box belongs. When a frame's
/// art is unusable the stage draws no art at all, stacks the boxes in a plain column, and names
/// the first problem — composing keeps working, because nothing in the model depends on the art.
/// </para>
/// <para>
/// Boxes are kept across refreshes while the frame is unchanged, so an edit does not take keyboard
/// focus away from the box the player is on.
/// </para>
/// </summary>
public sealed partial class LoadoutStage : Control
{
    private const string OverlapNotice = "BOXES OVERLAP AT THIS SIZE — WIDEN THE VIEW";

    /// <summary>Frames already warned about overlapping at some size, so a window drag warns once.</summary>
    private static readonly HashSet<string> WarnedOverlap = new();

    private readonly List<FittingBox> _boxes = new();

    private TextureRect _art = null!;
    private LeaderLayer _leaders = null!;
    private Label _notice = null!;

    private FrameDef? _frame;
    private FrameArtEntry? _entry;
    private int _selected;
    private StageFit _fit;

    /// <summary>A box was clicked or activated with <c>ui_accept</c>.</summary>
    public Action<int>? SocketChosen { get; set; }

    /// <summary>A box took keyboard focus.</summary>
    public Action<int>? SocketFocused { get; set; }

    /// <summary>The artwork's horizontal centre in stage pixels: which side of the machine a box is on.</summary>
    public int CanvasCentreX => _entry?.Art is { } art
        ? _fit.OffsetX + StageGeometry.Scale(art.Canvas.W / 2, _fit.ScalePermille)
        : (int)(Size.X / 2);

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Pass;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        _art = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };

        if (ProjectionGlow.Create() is { } glow)
        {
            _art.Material = glow;
        }
        else
        {
            _art.SelfModulate = ShellPalette.Projection;
        }

        AddChild(_art);

        _leaders = new LeaderLayer();
        AddChild(_leaders);

        _notice = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _notice.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
        _notice.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        AddChild(_notice);

        Resized += Layout;
    }

    public void Show(FrameDef frame, LoadoutDraft template, int selected)
    {
        _selected = selected;

        if (_frame?.Id != frame.Id || _boxes.Count != frame.Sockets.Count)
        {
            _frame = frame;
            _entry = FrameArtLibrary.For(frame);

            foreach (var box in _boxes)
            {
                box.QueueFree();
            }

            _boxes.Clear();

            for (var i = 0; i < frame.Sockets.Count; i++)
            {
                var box = new FittingBox(i, frame.Sockets[i])
                {
                    Chosen = index => SocketChosen?.Invoke(index),
                    Focused = index => SocketFocused?.Invoke(index),
                };

                _boxes.Add(box);
                AddChild(box);
            }
        }

        for (var i = 0; i < _boxes.Count; i++)
        {
            var fitting = i < template.Fitted.Count ? LoadoutCatalog.Fitting(template.Fitted[i]) : null;
            _boxes[i].Refresh(fitting, i == selected);
        }

        Layout();
    }

    public void FocusBox(int socket)
    {
        if (socket >= 0 && socket < _boxes.Count)
        {
            _boxes[socket].GrabFocus();
        }
    }

    /// <summary>A box's rectangle in stage coordinates, for placing a popover beside it.</summary>
    public Rect2 BoxRect(int socket) =>
        socket >= 0 && socket < _boxes.Count
            ? new Rect2(_boxes[socket].Position, _boxes[socket].Size)
            : new Rect2();

    private void Layout()
    {
        if (_frame is null || _entry is null || !IsNodeReady())
        {
            return;
        }

        _leaders.Position = Vector2.Zero;
        _leaders.Size = Size;

        if (_entry.Art is not { } art)
        {
            Fallback(_entry);
            return;
        }

        var stage = ((int)Size.X, (int)Size.Y);
        _fit = StageGeometry.Fit(art.Canvas, stage);

        _art.Visible = true;
        _art.Position = new Vector2(_fit.OffsetX, _fit.OffsetY);
        _art.Size = new Vector2(
            StageGeometry.Scale(art.Canvas.W, _fit.ScalePermille),
            StageGeometry.Scale(art.Canvas.H, _fit.ScalePermille));
        _art.Texture = FrameArtLibrary.Rasterise(_frame, _fit.ScalePermille);

        var rects = new List<(int X, int Y, int W, int H)>();
        var leaders = new List<IReadOnlyList<(int X, int Y)>>();

        for (var i = 0; i < _boxes.Count; i++)
        {
            // FrameArtLibrary has already checked that the placement names exactly this frame's
            // sockets, so every socket has exactly one.
            var placement = art.Sockets.First(socket => socket.Id == _frame.Sockets[i].Id);
            var rect = StageGeometry.BoxRect(
                _fit, placement.Box, (FittingBox.Width, FittingBox.Height), stage);

            Place(_boxes[i], rect);
            rects.Add(rect);
            leaders.Add(StageGeometry.Leader(rect, StageGeometry.ToStage(_fit, placement.Anchor)));
        }

        _leaders.Show(leaders, _selected);

        var overlapping = StageGeometry.Problems(rects, leaders).Count > 0;
        ShowNotice(overlapping ? OverlapNotice : string.Empty, atTop: false);

        if (overlapping && WarnedOverlap.Add(_frame.Id))
        {
            GD.PushWarning(
                $"Frame art '{_frame.Id}': boxes overlap or leaders cross at a {stage.Item1}x{stage.Item2} stage.");
        }
    }

    /// <summary>
    /// No art: boxes in socket declaration order down a plain column, wrapping into a second
    /// column when the stage is too short, under a line naming what is wrong.
    /// </summary>
    private void Fallback(FrameArtEntry entry)
    {
        _art.Visible = false;
        _art.Texture = null;
        _leaders.Show(Array.Empty<IReadOnlyList<(int X, int Y)>>(), -1);

        ShowNotice($"FRAME ART UNAVAILABLE — {entry.Warnings[0]}", atTop: true);

        var top = (ShellPalette.SpaceMd * 2) + (int)_notice.GetCombinedMinimumSize().Y;
        var x = ShellPalette.SpaceMd;
        var y = top;

        foreach (var box in _boxes)
        {
            if (y + FittingBox.Height > Size.Y && y > top)
            {
                x += FittingBox.Width + ShellPalette.SpaceMd;
                y = top;
            }

            Place(box, (x, y, FittingBox.Width, FittingBox.Height));
            y += FittingBox.Height + ShellPalette.SpaceMd;
        }
    }

    private void ShowNotice(string text, bool atTop)
    {
        _notice.Text = text;

        var height = _notice.GetCombinedMinimumSize().Y;
        _notice.Position = new Vector2(
            ShellPalette.SpaceMd,
            atTop ? ShellPalette.SpaceMd : Size.Y - height - ShellPalette.SpaceMd);
        _notice.MoveToFront();
    }

    private static void Place(Control control, (int X, int Y, int W, int H) rect)
    {
        control.Position = new Vector2(rect.X, rect.Y);
        control.Size = new Vector2(rect.W, rect.H);
    }
}
```

- [ ] **Step 5: Put the stage in the composer**

In `LoadoutsFocus.cs`:

1. Replace the field `private VBoxContainer _sockets = null!;` with `private LoadoutStage _stage = null!;`.
2. In `Composer()`, replace

```csharp
        _sockets = new VBoxContainer();
        _sockets.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(_sockets);
```

with

```csharp
        _stage = new LoadoutStage
        {
            SocketChosen = SelectSocket,
            SocketFocused = SelectSocket,
        };
        column.AddChild(_stage);
```

3. Make `SelectSocket` idempotent, because a click both focuses a box and chooses it:

```csharp
    private void SelectSocket(int socket)
    {
        if (_socket == socket)
        {
            return;
        }

        _socket = socket;
        Rebuild();
    }
```

4. In `Rebuild()`, delete the `foreach (var child in _sockets.GetChildren())` loop at the top and the whole `for` loop that creates `SocketRow`s; after `var rollup = LoadoutRollup.Of(template);` add:

```csharp
        _stage.Show(rollup.Frame, template, _socket);
```

5. Delete the `Drop(LoadoutDragData payload, int socket)` method (only `SocketRow` called it).
6. In the class doc comment, change "a frame and its sockets in the middle" to "the frame drawn with a box per socket in the middle".

- [ ] **Step 6: Delete `SocketRow`**

```bash
git rm dimenship/scripts/ui/focus/loadouts/SocketRow.cs dimenship/scripts/ui/focus/loadouts/SocketRow.cs.uid
```

- [ ] **Step 7: Build and generate sidecars**

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q`
Expected: `0 Error(s)`.

Run: `/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path dimenship --import`
Expected: `.uid` files appear for `FrameArtLibrary.cs`, `FittingBox.cs`, `LeaderLayer.cs`, `LoadoutStage.cs`.

- [ ] **Step 8: Verify in the game**

1. Open Robotics. *Survey Pattern A*: the Surveyor rover drawn in pale cyan with a soft glow; four boxes (Tool and Power left, Sensor and Investigation right), each with its fitting's name and image; a thin line from each box to a dot on its port.
2. Click the Sensor box: its border, its line and a ring around its dot turn accent blue together. `Tab` moves selection through the boxes in order tool → sensor → power → investigation.
3. *Salvage Hauler*: five boxes on the Hauler; Defense reads `EMPTY` over a dashed `+`.
4. Resize the window down to 1280×720: the art scales and stays sharp; boxes keep their size; if `BOXES OVERLAP AT THIS SIZE — WIDEN THE VIEW` appears at the bottom-left, move the offending `box` centres in the sidecar apart (vertically) and re-check — the sidecars are expected to pass at this size.
5. Break the sidecar on purpose: in `hauler_frame.json` change `"id": "defense"` to `"id": "armour"`, run the game, select *Salvage Hauler*. Expected: no drawing, five boxes in a column, and `FRAME ART UNAVAILABLE — hauler_frame.json: socket 'defense' has no placement`. Fitting still works through the palette. **Revert the change.**
6. The palette on the right still fits fittings by click (it is removed in Task 7).

- [ ] **Step 9: Commit**

```bash
git add dimenship/scripts/ui/focus/loadouts
git commit -F - <<'EOF'
feat: the loadout composer draws the frame, with a box on a line to every socket

LoadoutStage replaces the socket rows. The selected template's frame is drawn
as projected line art fitted into the stage, every socket gets a fixed-size
box centred where the frame's sidecar places it, and a leader runs from each
box to its connector. Selection lights the box border, the leader and a ring
on the connector together, in the shell's one selection colour.

A frame whose art is unusable falls back for the whole frame: no drawing, the
boxes in a plain column, and the first problem named in words. Boxes are never
moved to avoid each other; a collision at the current size is reported in one
line instead. Boxes are kept across edits while the frame is unchanged, so an
edit does not take keyboard focus away from the box being edited.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Power reads as draw against supply, with stats and cost beside it, and the details open over the stage

**Files:**
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutRollup.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/StatFormat.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/ItemStock.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/CostBox.cs`
- Modify: `dimenship/scripts/ui/ShellTheme.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/BudgetBar.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/LoadoutStrip.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutsFocus.cs`

**Interfaces:**
- Consumes: `LoadoutStage` (Task 5)
- Produces:
  - `Rollup` gains trailing members `long Supply, long Draw` (so `Totals.Power == Supply - Draw`)
  - `StatFormat.Compact : IReadOnlyList<(string Header, string? Icon, Func<StatBlock, long> Read)>`, `StatFormat.Change(StatBlock difference) : string`
  - `ItemStock.Update(WorldSnapshot) : bool`, `ItemStock.Held(string itemId) : long`
  - `CostBox(ItemStock stock)` (its `OnSnapshot` is removed)
  - `ShellTheme.VerticalDivider() : Control`
  - `BudgetBar.Show(long supply, long draw, long? previewSupply, long? previewDraw)`
  - `LoadoutStrip(ItemStock stock)`, `Refresh(Rollup current, Rollup? preview)`, `SetDetails(bool open)`, `Action<bool>? DetailsToggled`
  - `LoadoutsFocus.RefreshReadouts()` (private) — the one place the readouts are recomputed

- [ ] **Step 1: `Rollup` carries supply and draw**

In `LoadoutRollup.cs`, add two trailing members to `Rollup`:

```csharp
/// <summary>
/// What the composer reads out: the totals, who contributed what to them, the summed cost, and
/// where the template stands.
/// <para>
/// <see cref="Supply"/> and <see cref="Draw"/> split the net power total into its two halves —
/// every positive contribution, and every negative one made positive — because the power bar
/// reads draw against supply and a single net figure cannot be drawn as a bar with a capacity.
/// Both come from the same contributions as <see cref="Totals"/>, so
/// <c>Totals.Power == Supply - Draw</c> always.
/// </para>
/// </summary>
public sealed record Rollup(
    FrameDef Frame,
    StatBlock Totals,
    IReadOnlyList<Contribution> Contributions,
    IReadOnlyList<ItemCost> Cost,
    Verdict Verdict,
    int EmptySockets,
    long Supply,
    long Draw);
```

and replace the `return` of `LoadoutRollup.Of` with:

```csharp
        var supply = contributions.Where(entry => entry.Stats.Power > 0).Sum(entry => entry.Stats.Power);
        var draw = -contributions.Where(entry => entry.Stats.Power < 0).Sum(entry => entry.Stats.Power);

        return new Rollup(
            frame, totals, contributions, Sum(cost), Judge(totals, empty), empty, supply, draw);
```

- [ ] **Step 2: `StatFormat` gains the strip's stats and the swap line**

In `StatFormat.cs`, after `Columns`, add:

```csharp
    /// <summary>
    /// The five stats the bottom strip shows compactly, each with the <c>status</c> icon its socket
    /// kind already borrows. Power is not here: it has a section of its own. Mass has no icon, and
    /// the header beside every number is why that costs nothing — a reading never rests on an icon
    /// alone.
    /// </summary>
    public static readonly IReadOnlyList<(string Header, string? Icon, Func<StatBlock, long> Read)> Compact =
        new (string, string?, Func<StatBlock, long>)[]
        {
            ("MASS", null, stats => stats.Mass),
            ("DURABILITY", "durability", stats => stats.Durability),
            ("CARGO", "capacity", stats => stats.Cargo),
            ("SCAN", "stability", stats => stats.Scan),
            ("WORK", "rate", stats => stats.WorkRate),
        };

    /// <summary>
    /// What a swap would change, naming only the stats it moves — <c>SCAN +270 · MASS +50</c>. The
    /// picker's preview line, which says what choosing would do rather than what the fitting is.
    /// </summary>
    public static string Change(StatBlock difference)
    {
        var parts = Columns
            .Select(column => (column.Header, Value: column.Read(difference)))
            .Where(entry => entry.Value != 0)
            .Select(entry => $"{entry.Header} {Signed(entry.Value)}");

        var line = string.Join(" · ", parts);

        return line.Length > 0 ? line : "NO CHANGE";
    }
```

Also change `Summary`'s doc comment "What a fitted part contributes" → "What a fitting contributes", and `VerdictText`'s "in the library row and in the info box" → "in the library cards and in the strip".

- [ ] **Step 3: `ItemStock`**

Create `dimenship/scripts/ui/focus/loadouts/ItemStock.cs`:

```csharp
using System.Collections.Generic;
using Dimenship.Core.Simulation;

namespace Dimenship.Ui;

/// <summary>
/// The vessel's material stock, taken off each snapshot: the one live reading in the loadout mock.
/// Held once and shared by the strip's build cost and the details drawer's cost box, so the two
/// cannot disagree about what the vessel holds — two private copies of this dictionary is how they
/// would.
/// <para>
/// Only amounts are kept. Capacity and rate belong to the resource strip; a cost line asking "can
/// this be paid for now" has no use for either. Nothing is reserved or spent: the vessel does not
/// know any template exists.
/// </para>
/// </summary>
public sealed class ItemStock
{
    private readonly Dictionary<string, long> _held = new();

    /// <summary>Takes the snapshot's amounts. True when any changed, so the caller redraws only then.</summary>
    public bool Update(WorldSnapshot snapshot)
    {
        var changed = false;

        foreach (var stock in snapshot.Resources)
        {
            if (!_held.TryGetValue(stock.Id.Value, out var amount) || amount != stock.Amount)
            {
                _held[stock.Id.Value] = stock.Amount;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Milli-units held of an item. Zero for an item no snapshot has mentioned yet.</summary>
    public long Held(string itemId) => _held.TryGetValue(itemId, out var amount) ? amount : 0L;
}
```

- [ ] **Step 4: `CostBox` reads the shared stock**

In `CostBox.cs`:

1. Delete the `_held` field and the whole `OnSnapshot` method.
2. Add a field and constructor:

```csharp
    private readonly ItemStock _stock;

    public CostBox(ItemStock stock)
    {
        _stock = stock;
    }
```

3. In `Row`, replace `var held = _held.TryGetValue(line.ItemId, out var amount) ? amount : 0L;` with `var held = _stock.Held(line.ItemId);`.
4. Update the class doc comment's second paragraph: the held column comes from the shared `ItemStock`, which reads `WorldSnapshot.Resources`.

- [ ] **Step 5: A vertical rule in the theme**

In `ShellTheme.cs`, after `Divider()`:

```csharp
    /// <summary>A 1px vertical rule, for sections laid out side by side. The same square, marginless rule as <see cref="Divider"/>.</summary>
    public static Control VerticalDivider() => new ColorRect
    {
        Color = ShellPalette.Border,
        CustomMinimumSize = new Vector2(1, 0),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
```

- [ ] **Step 6: `BudgetBar`**

Create `dimenship/scripts/ui/focus/loadouts/BudgetBar.cs`:

```csharp
using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The power budget as a bar: its length is supply, its solid fill is draw. A preview hatches the
/// difference between the current draw and the candidate's, in whichever direction it goes, and a
/// preview that changes supply marks where the new end would be.
/// <para>
/// Over budget, the fill runs to the supply mark in <see cref="ShellPalette.StateFault"/> and a
/// tick stands at that mark. The words beside the bar — <c>OVER BUDGET BY 120</c> — carry the
/// state; the colour only agrees with them.
/// </para>
/// <para>
/// Drawn rather than built from a <c>ProgressBar</c>, because a progress bar has one value and this
/// has up to four, and a hatched segment is not a stylebox.
/// </para>
/// </summary>
public sealed partial class BudgetBar : Control
{
    private const float BarHeight = 8f;
    private const float Mark = 3f;
    private const float HatchStep = 4f;

    private long _supply;
    private long _draw;
    private long? _previewSupply;
    private long? _previewDraw;

    public BudgetBar()
    {
        CustomMinimumSize = new Vector2(160, BarHeight + (2 * Mark));
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(long supply, long draw, long? previewSupply, long? previewDraw)
    {
        _supply = supply;
        _draw = draw;
        _previewSupply = previewSupply;
        _previewDraw = previewDraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var width = Size.X;
        var trough = new Rect2(0, Mark, width, BarHeight);

        DrawRect(trough, ShellPalette.BgBase);
        DrawRect(trough, ShellPalette.Border, filled: false, width: 1f);

        var capacity = Math.Max(_supply, _previewSupply ?? 0);

        if (capacity <= 0)
        {
            return;
        }

        float X(long value) => Math.Clamp(value, 0, capacity) * width / capacity;

        var over = _draw > _supply;
        DrawRect(
            new Rect2(0, Mark, X(Math.Min(_draw, _supply)), BarHeight),
            over ? ShellPalette.StateFault : ShellPalette.Accent);

        if (over)
        {
            var at = X(_supply);
            DrawLine(new Vector2(at, 0), new Vector2(at, BarHeight + (2 * Mark)), ShellPalette.StateFault, 2f);
        }

        if (_previewDraw is { } previewDraw && previewDraw != _draw)
        {
            Hatch(X(Math.Min(_draw, previewDraw)), X(Math.Max(_draw, previewDraw)));
        }

        if (_previewSupply is { } previewSupply && previewSupply != _supply)
        {
            var at = X(previewSupply);
            DrawLine(new Vector2(at, 0), new Vector2(at, BarHeight + (2 * Mark)), ShellPalette.TextTitle, 1f);
        }
    }

    private void Hatch(float from, float to)
    {
        if (to - from < 1f)
        {
            return;
        }

        DrawRect(new Rect2(from, Mark, to - from, BarHeight), ShellPalette.TextTitle, filled: false, width: 1f);

        for (var x = from; x < to; x += HatchStep)
        {
            var end = Math.Min(x + BarHeight, to);
            DrawLine(
                new Vector2(x, Mark + BarHeight),
                new Vector2(end, Mark + BarHeight - (end - x)),
                ShellPalette.TextTitle,
                1f);
        }
    }
}
```

(Over budget the solid fill stops at the supply mark; the part of the draw beyond supply is off the bar by definition, and the words say how far.)

- [ ] **Step 7: `LoadoutStrip`**

Create `dimenship/scripts/ui/focus/loadouts/LoadoutStrip.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The strip under the stage: POWER BUDGET, STATS, BUILD COST, and a DETAILS toggle. It shows the
/// template as it stands and, while a picker row is previewed, what that candidate would change —
/// hatched on the bar, and as <c>+n</c> / <c>−n</c> beside each stat and cost figure it moves.
/// <para>
/// Every state is said in words as well as colour: an over-budget template reads
/// <c>OVER BUDGET BY 120</c>, a template with no core reads <c>NO POWER SOURCE</c>, and a cost
/// the vessel cannot cover reads <c>SHORT</c>.
/// </para>
/// <para>
/// The per-fitting attribution the old stat table showed is not lost: DETAILS opens it, unchanged,
/// in a drawer over the stage.
/// </para>
/// </summary>
public sealed partial class LoadoutStrip : PanelContainer
{
    private readonly ItemStock _stock;

    private BudgetBar _bar = null!;
    private Label _reading = null!;
    private Label _preview = null!;
    private HBoxContainer _stats = null!;
    private HBoxContainer _cost = null!;
    private Button _details = null!;

    public LoadoutStrip(ItemStock stock)
    {
        _stock = stock;
    }

    public Action<bool>? DetailsToggled { get; set; }

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Box());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        AddChild(row);

        var power = Section(row, "Power Budget");
        power.CustomMinimumSize = new Vector2(220, 0);

        _bar = new BudgetBar();
        power.AddChild(_bar);

        _reading = new Label();
        _reading.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        power.AddChild(_reading);

        _preview = new Label();
        _preview.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        _preview.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        power.AddChild(_preview);

        row.AddChild(ShellTheme.VerticalDivider());

        _stats = new HBoxContainer();
        _stats.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        Section(row, "Stats").AddChild(_stats);

        row.AddChild(ShellTheme.VerticalDivider());

        var cost = Section(row, "Build Cost");
        cost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _cost = new HBoxContainer();
        _cost.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        cost.AddChild(_cost);

        _details = new Button { Text = "DETAILS", ToggleMode = true, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_details);
        _details.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _details.Toggled += open => DetailsToggled?.Invoke(open);
        row.AddChild(_details);
    }

    /// <summary>Sets the toggle without raising <see cref="DetailsToggled"/>, for a view restoring its session state.</summary>
    public void SetDetails(bool open) => _details.SetPressedNoSignal(open);

    public void Refresh(Rollup current, Rollup? preview)
    {
        if (!IsNodeReady())
        {
            return;
        }

        _bar.Show(current.Supply, current.Draw, preview?.Supply, preview?.Draw);

        _reading.Text = PowerReading(current);
        _reading.AddThemeColorOverride(
            "font_color", current.Draw > current.Supply ? ShellPalette.StateFault : ShellPalette.TextTitle);
        _preview.Text = preview is null ? string.Empty : PreviewReading(current, preview);

        Clear(_stats);

        foreach (var (header, icon, read) in StatFormat.Compact)
        {
            var value = read(current.Totals);
            long? change = preview is null ? null : read(preview.Totals) - value;
            _stats.AddChild(Cell(header, icon is null ? null : ("status", icon), StatFormat.Plain(value), change is { } c && c != 0 ? StatFormat.Signed(c) : null, shortfall: false));
        }

        Clear(_cost);

        foreach (var (item, amount, change) in CostLines(current, preview))
        {
            _cost.AddChild(Cell(
                string.Empty,
                ("item", item),
                Units.Format(amount),
                change != 0 ? (change > 0 ? "+" : string.Empty) + Units.Format(change) : null,
                shortfall: amount > _stock.Held(item)));
        }
    }

    private static string PowerReading(Rollup rollup) =>
        rollup.Supply <= 0 ? $"NO POWER SOURCE — {rollup.Draw} / 0"
        : rollup.Draw > rollup.Supply ? $"{rollup.Draw} / {rollup.Supply} — OVER BUDGET BY {rollup.Draw - rollup.Supply}"
        : $"{rollup.Draw} / {rollup.Supply}";

    /// <summary>
    /// The preview's own reading, and — only when the candidate would change the verdict — what it
    /// would change it to, in words.
    /// </summary>
    private static string PreviewReading(Rollup current, Rollup preview)
    {
        var line = $"PREVIEW {preview.Draw} / {preview.Supply}";

        if (preview.Verdict == current.Verdict)
        {
            return line;
        }

        var verdict = preview.Verdict == Verdict.OverBudget
            ? $"OVER BUDGET BY {preview.Draw - preview.Supply}"
            : VerdictText.Of(preview.Verdict);

        return $"{line} · PREVIEW: {verdict}";
    }

    /// <summary>
    /// The current bill in its own order, then anything only the preview would add, each with the
    /// change the preview makes to it. Zero-amount lines are kept while previewed so a fitting that
    /// would remove an item's last line shows it going to zero rather than vanishing.
    /// </summary>
    private static IEnumerable<(string Item, long Amount, long Change)> CostLines(Rollup current, Rollup? preview)
    {
        var previewed = preview?.Cost.ToDictionary(line => line.ItemId, line => line.Amount)
            ?? new Dictionary<string, long>();
        var seen = new HashSet<string>();

        foreach (var line in current.Cost)
        {
            seen.Add(line.ItemId);
            var after = preview is null ? line.Amount : previewed.GetValueOrDefault(line.ItemId);
            yield return (line.ItemId, line.Amount, after - line.Amount);
        }

        if (preview is null)
        {
            yield break;
        }

        foreach (var line in preview.Cost)
        {
            if (!seen.Contains(line.ItemId))
            {
                yield return (line.ItemId, 0, line.Amount);
            }
        }
    }

    private static VBoxContainer Section(Container row, string caption)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        row.AddChild(column);

        var title = new Label { Text = caption.ToUpperInvariant() };
        title.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(title);

        return column;
    }

    private static Control Cell(string header, (string Domain, string Name)? icon, string value, string? change, bool shortfall)
    {
        var cell = new HBoxContainer();
        cell.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        if (header.Length > 0)
        {
            var name = new Label { Text = header };
            name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cell.AddChild(name);
        }

        cell.AddChild(icon is { } glyph
            ? new IconSlot(glyph.Domain, glyph.Name, IconSlot.RowSize, ShellPalette.TextPrimary)
            : new IconSlot(IconSlot.RowSize, ShellPalette.TextPrimary));

        var reading = new Label { Text = value };
        reading.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        reading.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        cell.AddChild(reading);

        if (change is not null)
        {
            var delta = new Label { Text = change };
            delta.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            delta.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cell.AddChild(delta);
        }

        if (shortfall)
        {
            var warning = new Label { Text = "SHORT" };
            warning.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
            warning.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cell.AddChild(warning);
        }

        return cell;
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            child.QueueFree();
        }
    }
}
```

Reformat the long `_stats.AddChild(Cell(...))` call over several lines to match the file's style.

- [ ] **Step 8: Compose the strip and drawer in `LoadoutsFocus`**

1. Add fields:

```csharp
    /// <summary>
    /// Whether the details drawer is open. Session-local by being static: the shell frees a focus
    /// view on every switch, and a drawer that closed itself each time the player looked away would
    /// be a preference nobody set. Deliberately not in <c>user://layout.json</c>, which describes
    /// zones and never a focus view's interior.
    /// </summary>
    private static bool _detailsOpen;

    private readonly ItemStock _stock = new();
    private LoadoutStrip _strip = null!;
    private PanelContainer _drawer = null!;
```

2. Replace the body of `Composer()` after `column.AddChild(ShellTheme.Divider());` (the stage, rollup and cost additions) with:

```csharp
        // The stage and the drawer share one area so the drawer can lie over the stage's bottom
        // edge. A drawer that pushed the stage up would resize it, and a resized stage moves every
        // box — opening the details would rearrange the thing being examined.
        var area = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(area);

        _stage = new LoadoutStage
        {
            SocketChosen = SelectSocket,
            SocketFocused = SelectSocket,
        };
        area.AddChild(_stage);
        _stage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _drawer = new PanelContainer { Visible = _detailsOpen, MouseFilter = MouseFilterEnum.Stop };
        _drawer.AddThemeStyleboxOverride("panel", ShellTheme.Box());
        area.AddChild(_drawer);
        _drawer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _drawer.GrowVertical = GrowDirection.Begin;

        var details = new HBoxContainer();
        details.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        _drawer.AddChild(details);

        _rollup = new RollupGrid { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddChild(_rollup);

        _cost = new CostBox(_stock) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddChild(_cost);

        _strip = new LoadoutStrip(_stock)
        {
            DetailsToggled = open =>
            {
                _detailsOpen = open;
                _drawer.Visible = open;
            },
        };
        column.AddChild(_strip);
```

3. At the end of `_Ready()`, after `Select(0);`, add `_strip.SetDetails(_detailsOpen);`.

4. Replace `OnSnapshot`:

```csharp
    /// <summary>
    /// Only the vessel's material stock is taken, for the two cost readouts. Nothing else here is
    /// live: a template commands nothing, so there is nothing about the vessel for it to be out of
    /// date with.
    /// </summary>
    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        if (_stock.Update(snapshot))
        {
            RefreshReadouts();
        }
    }
```

5. Add:

```csharp
    /// <summary>The strip and the drawer, recomputed together so they can never disagree.</summary>
    private void RefreshReadouts()
    {
        if (Current is not { } template)
        {
            return;
        }

        var rollup = LoadoutRollup.Of(template);

        _strip.Refresh(rollup, null);
        _rollup.Refresh(rollup);
        _cost.Refresh(rollup.Cost);
    }
```

6. In `Rebuild()`, replace `_rollup.Refresh(rollup);` and `_cost.Refresh(rollup.Cost);` with `RefreshReadouts();`.

- [ ] **Step 9: Build and generate sidecars**

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q` → `0 Error(s)`.
Run: `/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path dimenship --import` → `.uid` files for `ItemStock.cs`, `BudgetBar.cs`, `LoadoutStrip.cs`.

- [ ] **Step 10: Verify in the game**

1. *Survey Pattern A*: POWER BUDGET reads `330 / 600`, bar a little over half full in accent. STATS: `MASS 1180`, `DURABILITY 400`, `CARGO 0`, `SCAN 410`, `WORK 180`.
2. *Deep Prospector*: `720 / 600 — OVER BUDGET BY 120` in the fault colour; the bar is full with a tick at its end.
3. *Salvage Hauler*: `390 / 800`.
4. Select the Surveyor's Power box and press `Delete`: `NO POWER SOURCE — 330 / 0`; `Ctrl+Z` restores it.
5. BUILD COST lists each item with an icon; an item the vessel lacks carries `SHORT`. Let the simulation run: amounts held change without any template edit.
6. DETAILS opens a drawer over the bottom of the stage holding the stat rollup and the cost table; the boxes do not move. Switch to another focus view and back: the drawer is still open.

- [ ] **Step 11: Commit**

```bash
git add dimenship/scripts/ui
git commit -F - <<'EOF'
feat: a loadout's power reads as draw against supply, with its stats and bill beside it

A strip under the stage carries POWER BUDGET, STATS and BUILD COST. Power is
now a bar whose length is supply and whose fill is draw, with the net state
said in words: OVER BUDGET BY 120, or NO POWER SOURCE when nothing supplies
it. Rollup gains Supply and Draw from the same contributions as its total, so
the three can never disagree. The strip is built to show a candidate's effect
beside each figure; the picker that produces one follows.

The per-fitting attribution and the held-against-required cost table move
into a DETAILS drawer laid over the bottom of the stage rather than pushing it
up, because a resized stage would move every box. The vessel's stock is read
once into an ItemStock that both cost readouts share.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Fitting happens in a picker beside the box, previewed before it is chosen

**Files:**
- Create: `dimenship/scripts/ui/focus/loadouts/StagePopover.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/FittingPicker.cs`
- Create: `dimenship/scripts/ui/focus/loadouts/FrameChooser.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutStage.cs`
- Modify: `dimenship/scripts/ui/focus/loadouts/LoadoutsFocus.cs`
- Delete: `dimenship/scripts/ui/focus/loadouts/PartPalette.cs`, `PartPalette.cs.uid`, `LoadoutDragData.cs`, `LoadoutDragData.cs.uid`

**Interfaces:**
- Consumes: `ShellTheme.Popover()` (Task 4), `StageGeometry.PopoverRect` (Task 3), `LoadoutStage` (Task 5), `LoadoutStrip.Refresh(current, preview)` (Task 6), `StatFormat.Change`, `StatBlock -`, `LoadoutDraft.CarryOver` (Tasks 1, 6), `FrameArtLibrary.Rasterise` (Task 5)
- Produces:
  - `public partial class StagePopover : PanelContainer` — `StagePopover(string title)`, `Width = 300`, `Action? Closed`, `float? NotchY`, `bool NotchOnLeft`, `Close()`, `virtual FocusFirst()`, `protected virtual Fill(VBoxContainer rows)`
  - `FittingPicker(SocketDef socket, string? fitted)` — `Action<string?>? Chosen`, `Action<string?>? Previewed`, `Action? PreviewCleared`
  - `FrameChooser(FrameDef current, IReadOnlyList<string?> fitted)` — `Action<FrameDef>? Chosen`, `static string CarryOverLine(FrameDef from, IReadOnlyList<string?> fitted, FrameDef to)`
  - `LoadoutStage.ShowPopoverBeside(StagePopover popover, int socket)`, `LoadoutStage.ShowPopoverAt(StagePopover popover, float x)`

- [ ] **Step 1: `StagePopover`**

Create `dimenship/scripts/ui/focus/loadouts/StagePopover.cs`:

```csharp
using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The chrome shared by the loadout stage's two popovers — the fitting picker and the frame
/// chooser: a title, a close button, a column of rows, and a notch pointing at whatever opened it.
/// <para>
/// A <see cref="Control"/> laid over the stage, never a <c>Window</c> or <c>PopupPanel</c>, for
/// the reason <see cref="SettingsOverlay"/> gives: the shell's frost shader samples
/// <c>SCREEN_UV</c>, and a second viewport would sample nothing.
/// </para>
/// <para>
/// It closes on its own close button, on a click anywhere outside it, and when the view asks
/// (<c>Esc</c>, a different socket, an undo). The outside click is seen in <see cref="_Input"/> and
/// deliberately not consumed, so the click still lands on whatever it was aimed at — a click on
/// another box closes this picker and opens that box's in one gesture.
/// </para>
/// </summary>
public partial class StagePopover : PanelContainer
{
    public const int Width = 300;

    private const float NotchSize = 8f;

    private readonly string _title;
    private bool _closing;

    public StagePopover(string title)
    {
        _title = title;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(Width, 0);
    }

    /// <summary>Raised once, as the popover closes for any reason.</summary>
    public Action? Closed { get; set; }

    /// <summary>Where the notch meets this popover's side, in its own coordinates; null for no notch.</summary>
    public float? NotchY { get; set; }

    /// <summary>True when the notch is on the left edge, pointing left at a box to this popover's left.</summary>
    public bool NotchOnLeft { get; set; }

    protected VBoxContainer Rows { get; private set; } = null!;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Popover());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(header);

        var title = new Label
        {
            Text = _title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        header.AddChild(title);

        var close = new Button
        {
            Icon = IconSlot.Load("control", "close"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Close",
        };

        if (close.Icon is null)
        {
            close.Text = "✕";
        }

        ShellTheme.ApplyGlass(close);
        close.Pressed += Close;
        header.AddChild(close);

        Rows = new VBoxContainer();
        Rows.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        column.AddChild(Rows);

        Fill(Rows);
    }

    public void Close()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Closed?.Invoke();
        QueueFree();
    }

    /// <summary>Gives keyboard focus to the first row that accepts it.</summary>
    public virtual void FocusFirst()
    {
        foreach (var child in Rows.GetChildren())
        {
            if (child is Control { FocusMode: FocusModeEnum.All } row)
            {
                row.GrabFocus();
                return;
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }
            && !GetGlobalRect().HasPoint(GetGlobalMousePosition()))
        {
            Close();
        }
    }

    public override void _Draw()
    {
        if (NotchY is not { } y)
        {
            return;
        }

        var edge = NotchOnLeft ? 0f : Size.X;
        var tip = new Vector2(edge + (NotchOnLeft ? -NotchSize : NotchSize), y);
        var upper = new Vector2(edge, y - NotchSize);
        var lower = new Vector2(edge, y + NotchSize);

        DrawColoredPolygon(new[] { upper, tip, lower }, ShellPalette.BgPanel);
        DrawPolyline(new[] { upper, tip, lower }, ShellPalette.Accent, 1f, antialiased: true);
    }

    /// <summary>Adds this popover's rows. Called once, from <see cref="_Ready"/>.</summary>
    protected virtual void Fill(VBoxContainer rows)
    {
    }
}
```

- [ ] **Step 2: `FittingPicker`**

Create `dimenship/scripts/ui/focus/loadouts/FittingPicker.cs`:

```csharp
using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The fittings one socket accepts, opened beside that socket's box. Compatibility is the
/// catalog's (<see cref="LoadoutCatalog.OfKind"/>), so there is no incompatible row to list and
/// explain. The last row is <c>CLEAR SOCKET</c>, the ticket's explicit clear action, disabled and
/// marked <c>ALREADY EMPTY</c> on an empty socket.
/// <para>
/// Hovering or focusing a row previews it: the row shows what choosing it would change, and the
/// view hatches the same change onto the strip. A preview is never an edit — the view rolls up a
/// copy of the template — so it cannot reach the undo stack or the template.
/// </para>
/// </summary>
public sealed partial class FittingPicker : StagePopover
{
    private const int ImageSize = 40;

    private readonly SocketDef _socket;
    private readonly string? _fitted;
    private readonly List<Row> _rows = new();

    public FittingPicker(SocketDef socket, string? fitted)
        : base($"COMPATIBLE {LoadoutCatalog.Label(socket.Kind).ToUpperInvariant()} FITTINGS")
    {
        _socket = socket;
        _fitted = fitted;
    }

    /// <summary>Raised with the chosen fitting's id, or null for <c>CLEAR SOCKET</c>.</summary>
    public Action<string?>? Chosen { get; set; }

    /// <summary>Raised with the previewed fitting's id, or null when <c>CLEAR SOCKET</c> is previewed.</summary>
    public Action<string?>? Previewed { get; set; }

    public Action? PreviewCleared { get; set; }

    /// <summary>The fitted row when there is one, so opening the picker previews no change.</summary>
    public override void FocusFirst()
    {
        foreach (var row in _rows)
        {
            if (row.Fitted)
            {
                row.GrabFocus();
                return;
            }
        }

        base.FocusFirst();
    }

    protected override void Fill(VBoxContainer rows)
    {
        var current = LoadoutCatalog.Fitting(_fitted);
        var baseline = current?.Delta ?? default;

        foreach (var fitting in LoadoutCatalog.OfKind(_socket.Kind))
        {
            Add(rows, new Row(
                fitting.Label,
                LoadoutCatalog.FittingArtPath(fitting.Id),
                fitting.Id == _fitted ? "FITTED" : null,
                StatFormat.Change(fitting.Delta - baseline),
                enabled: true)
            {
                Previewed = () => Previewed?.Invoke(fitting.Id),
                Left = () => PreviewCleared?.Invoke(),
                Chosen = () => Chosen?.Invoke(fitting.Id),
            });
        }

        Add(rows, new Row(
            "CLEAR SOCKET",
            art: null,
            _fitted is null ? "ALREADY EMPTY" : null,
            StatFormat.Change(default(StatBlock) - baseline),
            enabled: _fitted is not null)
        {
            Previewed = () => Previewed?.Invoke(null),
            Left = () => PreviewCleared?.Invoke(),
            Chosen = () => Chosen?.Invoke(null),
        });
    }

    private void Add(VBoxContainer rows, Row row)
    {
        _rows.Add(row);
        rows.AddChild(row);
    }

    /// <summary>
    /// One candidate: image, name, a chip for the fitting already in the socket, and — while it is
    /// hovered or focused — the line saying what choosing it would change.
    /// </summary>
    private sealed partial class Row : PanelContainer
    {
        private readonly string _label;
        private readonly string? _art;
        private readonly string? _chip;
        private readonly string _change;
        private readonly bool _enabled;

        private Label _changeLine = null!;
        private bool _hot;

        public Row(string label, string? art, string? chip, string change, bool enabled)
        {
            _label = label;
            _art = art;
            _chip = chip;
            _change = change;
            _enabled = enabled;

            FocusMode = enabled ? FocusModeEnum.All : FocusModeEnum.None;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public Action? Previewed { get; set; }

        public Action? Left { get; set; }

        public Action? Chosen { get; set; }

        public bool Fitted => _chip == "FITTED";

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
            AddChild(row);

            row.AddChild(_art is null
                ? new IconSlot("control", "close", ImageSize, ShellPalette.TextDim)
                : IconSlot.FromPath(_art, ImageSize, ShellPalette.Projection));

            var column = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            row.AddChild(column);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            column.AddChild(line);

            var name = new Label
            {
                Text = _label,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeColorOverride("font_color", _enabled ? ShellPalette.TextTitle : ShellPalette.TextDim);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            line.AddChild(name);

            if (_chip is not null)
            {
                var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
                chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: Fitted));

                var text = new Label { Text = _chip, MouseFilter = MouseFilterEnum.Ignore };
                text.AddThemeColorOverride("font_color", Fitted ? ShellPalette.TextTitle : ShellPalette.TextDim);
                text.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
                chip.AddChild(text);
                line.AddChild(chip);
            }

            _changeLine = new Label
            {
                Text = _change,
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _changeLine.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            _changeLine.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(_changeLine);

            MouseEntered += Enter;
            MouseExited += Exit;
            FocusEntered += Enter;
            FocusExited += Exit;
        }

        public override void _GuiInput(InputEvent @event)
        {
            var pressed = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                || @event.IsActionPressed("ui_accept");

            if (!pressed)
            {
                return;
            }

            if (_enabled)
            {
                Chosen?.Invoke();
            }

            AcceptEvent();
        }

        private void Enter()
        {
            if (!_enabled)
            {
                return;
            }

            _hot = true;
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: true));
            _changeLine.Visible = true;
            Previewed?.Invoke();
        }

        private void Exit()
        {
            if (!_hot)
            {
                return;
            }

            _hot = false;
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));
            _changeLine.Visible = false;
            Left?.Invoke();
        }
    }
}
```

- [ ] **Step 3: `FrameChooser`**

Create `dimenship/scripts/ui/focus/loadouts/FrameChooser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The frames a template can be built on, each with its thumbnail, socket count and note. Hovering
/// or focusing a frame says what a swap to it would keep and drop, in words, before the player
/// makes it — <see cref="LoadoutDraft.CarryOver"/>'s rule, read out rather than discovered.
/// </summary>
public sealed partial class FrameChooser : StagePopover
{
    private const int ThumbnailWidth = 96;
    private const int ThumbnailHeight = 58;

    /// <summary>A quarter of the canvas: 300×180 for a 1200×720 frame, comfortably above the thumbnail.</summary>
    private const int ThumbnailScalePermille = 250;

    private readonly FrameDef _current;
    private readonly IReadOnlyList<string?> _fitted;

    public FrameChooser(FrameDef current, IReadOnlyList<string?> fitted)
        : base("CHANGE FRAME")
    {
        _current = current;
        _fitted = fitted;
    }

    public Action<FrameDef>? Chosen { get; set; }

    /// <summary>
    /// <c>KEEPS tool · sensor · power — DROPS investigation</c>: the socket ids whose fittings a swap
    /// carries, then the ids of fitted sockets it loses.
    /// </summary>
    public static string CarryOverLine(FrameDef from, IReadOnlyList<string?> fitted, FrameDef to)
    {
        var carried = LoadoutDraft.CarryOver(from, fitted, to);

        var kept = to.Sockets
            .Where((_, i) => carried[i] is not null)
            .Select(socket => socket.Id)
            .ToList();

        var dropped = from.Sockets
            .Where((socket, i) => i < fitted.Count && fitted[i] is not null && !kept.Contains(socket.Id))
            .Select(socket => socket.Id)
            .ToList();

        if (kept.Count == 0 && dropped.Count == 0)
        {
            return "NOTHING FITTED TO CARRY";
        }

        var keeps = kept.Count > 0 ? $"KEEPS {string.Join(" · ", kept)}" : "KEEPS NOTHING";

        return dropped.Count > 0 ? $"{keeps} — DROPS {string.Join(" · ", dropped)}" : keeps;
    }

    protected override void Fill(VBoxContainer rows)
    {
        foreach (var frame in LoadoutCatalog.Frames)
        {
            var current = frame.Id == _current.Id;

            rows.AddChild(new Row(frame, current, current ? string.Empty : CarryOverLine(_current, _fitted, frame))
            {
                Chosen = () => Chosen?.Invoke(frame),
            });
        }
    }

    private sealed partial class Row : PanelContainer
    {
        private readonly FrameDef _frame;
        private readonly bool _current;
        private readonly string _carry;

        private Label _carryLine = null!;

        public Row(FrameDef frame, bool current, string carry)
        {
            _frame = frame;
            _current = current;
            _carry = carry;

            FocusMode = current ? FocusModeEnum.None : FocusModeEnum.All;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public Action? Chosen { get; set; }

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
            AddChild(row);

            row.AddChild(new TextureRect
            {
                Texture = FrameArtLibrary.Rasterise(_frame, ThumbnailScalePermille),
                CustomMinimumSize = new Vector2(ThumbnailWidth, ThumbnailHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = ShellPalette.Projection,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            var column = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            row.AddChild(column);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            column.AddChild(line);

            var name = new Label
            {
                Text = _frame.Label,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            name.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            line.AddChild(name);

            if (_current)
            {
                var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
                chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: true));

                var text = new Label { Text = "CURRENT", MouseFilter = MouseFilterEnum.Ignore };
                text.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
                text.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
                chip.AddChild(text);
                line.AddChild(chip);
            }

            column.AddChild(Micro($"{_frame.Sockets.Count} SOCKETS", ShellPalette.TextDim));

            // Trimmed rather than wrapped, with the whole note as a tooltip: a wrapping label's
            // height depends on a width the popover does not have until it is placed, and the
            // popover is sized from its minimum before that.
            var note = Micro(_frame.Note, ShellPalette.TextFaint);
            note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            TooltipText = _frame.Note;
            column.AddChild(note);

            _carryLine = Micro(_carry, ShellPalette.TextPrimary);
            _carryLine.Visible = false;
            _carryLine.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            column.AddChild(_carryLine);

            MouseEntered += () => Hot(true);
            MouseExited += () => Hot(false);
            FocusEntered += () => Hot(true);
            FocusExited += () => Hot(false);
        }

        public override void _GuiInput(InputEvent @event)
        {
            var pressed = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                || @event.IsActionPressed("ui_accept");

            if (!pressed)
            {
                return;
            }

            if (!_current)
            {
                Chosen?.Invoke();
            }

            AcceptEvent();
        }

        private void Hot(bool hot)
        {
            if (_current)
            {
                return;
            }

            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: hot));
            _carryLine.Visible = hot;
        }

        private static Label Micro(string text, Color colour)
        {
            var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeColorOverride("font_color", colour);
            label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            return label;
        }
    }
}
```

- [ ] **Step 4: The stage places popovers**

In `LoadoutStage.cs`, add fields:

```csharp
    private StagePopover? _popover;
    private int _popoverSocket = -1;
    private float _popoverX;
```

add these members:

```csharp
    /// <summary>
    /// Opens a popover beside a socket's box, on the side away from the machine, and keeps it there
    /// as the stage resizes.
    /// </summary>
    public void ShowPopoverBeside(StagePopover popover, int socket)
    {
        AddChild(popover);
        _popover = popover;
        _popoverSocket = socket;
        PlacePopover();
    }

    /// <summary>Opens a popover along the stage's top edge at <paramref name="x"/>, under a header control.</summary>
    public void ShowPopoverAt(StagePopover popover, float x)
    {
        AddChild(popover);
        _popover = popover;
        _popoverSocket = -1;
        _popoverX = x;
        PlacePopover();
    }

    private void PlacePopover()
    {
        if (_popover is null || !IsInstanceValid(_popover) || _popover.IsQueuedForDeletion())
        {
            _popover = null;
            return;
        }

        var minimum = _popover.GetCombinedMinimumSize();
        var size = ((int)minimum.X, (int)minimum.Y);
        var stage = ((int)Size.X, (int)Size.Y);
        (int X, int Y, int W, int H) rect;

        if (_popoverSocket >= 0)
        {
            var box = BoxRect(_popoverSocket);
            var boxRect = ((int)box.Position.X, (int)box.Position.Y, (int)box.Size.X, (int)box.Size.Y);

            rect = StageGeometry.PopoverRect(boxRect, size, CanvasCentreX, stage);

            const float margin = 12f;
            _popover.NotchOnLeft = rect.X > boxRect.Item1;
            _popover.NotchY = Math.Clamp(
                box.Position.Y + (box.Size.Y / 2) - rect.Y, margin, Math.Max(margin, minimum.Y - margin));
        }
        else
        {
            rect = (Math.Clamp((int)_popoverX, 0, Math.Max(0, stage.Item1 - size.Item1)), 0, size.Item1, size.Item2);
            _popover.NotchY = null;
        }

        _popover.Position = new Vector2(rect.X, rect.Y);
        _popover.Size = new Vector2(rect.W, rect.H);
        _popover.MoveToFront();
        _popover.QueueRedraw();
    }
```

and call `PlacePopover();` as the last line of both `Layout()` branches — at the end of the art branch, and at the end of `Fallback(...)`.

- [ ] **Step 5: Rework `LoadoutsFocus` around the picker**

1. **Fields.** Remove `_palette` and `_frames`. Add:

```csharp
    private Label _frameName = null!;
    private Button _changeFrame = null!;

    /// <summary>The open picker or frame chooser, if any. At most one is ever open.</summary>
    private StagePopover? _popover;

    /// <summary>
    /// A copy of the template with the previewed candidate fitted, or null. Never the template
    /// itself: a preview that could reach the template could reach the undo stack.
    /// </summary>
    private LoadoutDraft? _preview;
```

2. **`_Ready`.** Replace everything from `var outer = new HSplitContainer` through the end of the method (the two nested splitters, the palette, `Select(0);` and `_strip.SetDetails(_detailsOpen);`) with one splitter — library and composer:

```csharp
        var split = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(split);

        _library = new TemplateList
        {
            Chosen = Select,
            NewRequested = AddTemplate,
        };
        split.AddChild(_library);

        split.AddChild(Composer());

        Select(0);
        _strip.SetDetails(_detailsOpen);
```

3. **`Composer()`.** Remove the `column.AddChild(Frames());` line. In the stage initialiser change `SocketChosen = SelectSocket` to `SocketChosen = OpenPicker`.

4. **`Header()`.** Replace the whole method:

```csharp
    private Control Header()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        row.AddChild(titles);

        _name = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _name.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        titles.AddChild(_name);

        _frameName = new Label();
        _frameName.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        _frameName.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        titles.AddChild(_frameName);

        _changeFrame = new Button { Text = "CHANGE FRAME", FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_changeFrame);
        _changeFrame.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _changeFrame.Pressed += OpenChooser;
        row.AddChild(_changeFrame);

        var chip = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: false));
        _verdict = new Label();
        _verdict.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        chip.AddChild(_verdict);
        row.AddChild(chip);

        // Said in words beside the control rather than left as a greyed button to guess at, the way
        // the programming view states its disabled ACTIVATE. There is no build task to queue and no
        // construction unit to queue it on. The ticket's sketch leaves these three out; they stay,
        // because the mock's honesty is the one thing about it that is not presentation.
        var reason = new Label
        {
            Text = "CONCEPT — NOTHING IS BUILT FROM THIS",
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        reason.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(reason);

        var queue = new Button
        {
            Text = "QUEUE BUILD",
            Disabled = true,
            FocusMode = FocusModeEnum.None,
        };
        queue.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(queue);

        return row;
    }
```

5. **Delete** `Frames()`, `HighlightFrame(...)` and `Fit(FittingDef fitting)`.

6. **Add** the picker, chooser and preview members:

```csharp
    /// <summary>
    /// Opens the picker for a socket, beside its box. Opening one closes any other: two popovers
    /// would be two previews competing for one strip.
    /// </summary>
    private void OpenPicker(int socket)
    {
        if (Current is not { } template)
        {
            return;
        }

        var frame = LoadoutCatalog.Frame(template.FrameId);

        if (socket < 0 || socket >= frame.Sockets.Count)
        {
            return;
        }

        SelectSocket(socket);
        ClosePopover();

        var picker = new FittingPicker(frame.Sockets[socket], template.Fitted[socket])
        {
            Chosen = fitting => Choose(socket, fitting),
            Previewed = fitting => Preview(socket, fitting),
            PreviewCleared = ClearPreview,
        };

        Track(picker);
        _stage.ShowPopoverBeside(picker, socket);
        picker.FocusFirst();
    }

    private void OpenChooser()
    {
        if (Current is not { } template)
        {
            return;
        }

        ClosePopover();

        var chooser = new FrameChooser(LoadoutCatalog.Frame(template.FrameId), template.Fitted)
        {
            Chosen = SwapFrame,
        };

        Track(chooser);
        _stage.ShowPopoverAt(chooser, _changeFrame.GlobalPosition.X - _stage.GlobalPosition.X);
        chooser.FocusFirst();
    }

    private void Track(StagePopover popover)
    {
        popover.Closed = () =>
        {
            if (_popover == popover)
            {
                _popover = null;
            }

            ClearPreview();
        };

        _popover = popover;
    }

    private void ClosePopover() => _popover?.Close();

    /// <summary>
    /// A choice from the picker. Choosing what is already fitted closes the picker without an undo
    /// entry, because an undo step that changes nothing is one the player has to press twice.
    /// </summary>
    private void Choose(int socket, string? fitting)
    {
        if (Current is not { } template || socket >= template.Fitted.Count)
        {
            return;
        }

        var changed = template.Fitted[socket] != fitting;

        template.Fitted[socket] = fitting;
        _socket = socket;
        ClosePopover();

        if (changed)
        {
            RecordEdit();
        }

        _stage.FocusBox(socket);
    }

    private void Preview(int socket, string? fitting)
    {
        if (Current is not { } template || socket >= template.Fitted.Count)
        {
            return;
        }

        if (template.Fitted[socket] == fitting)
        {
            ClearPreview();
            return;
        }

        var copy = template.Clone();
        copy.Fitted[socket] = fitting;
        _preview = copy;
        RefreshReadouts();
    }

    private void ClearPreview()
    {
        if (_preview is null)
        {
            return;
        }

        _preview = null;
        RefreshReadouts();
    }
```

7. **Close the popover whenever the template underneath it changes.** Add `ClosePopover();` as the first statement of `Select(int index)`, `SwapFrame(FrameDef frame)`, `Remove(int socket)` and `Step(...)`. In `Select`, also set `_preview = null;` before `Rebuild()`.

8. **`RefreshReadouts`** now passes the preview:

```csharp
        var rollup = LoadoutRollup.Of(template);
        var preview = _preview is null ? null : LoadoutRollup.Of(_preview);

        _strip.Refresh(rollup, preview);
        _rollup.Refresh(rollup);
        _cost.Refresh(rollup.Cost);
```

9. **`Rebuild()`.** Remove the `HighlightFrame(rollup.Frame);`, `_palette.ShowFrame(...)` and `_palette.ShowKind(...)` lines (and the `if` around the latter). After `_name.Text = template.Name;` add `_frameName.Text = rollup.Frame.Label;`.

10. **`_UnhandledKeyInput`.** Add as the first case of the `switch`:

```csharp
            // Before the shell sees it: ShellRoot._UnhandledInput takes Escape as "release focus",
            // and unhandled key input reaches this view first.
            case Key.Escape when _popover is not null:
                ClosePopover();
                _stage.FocusBox(_socket);
                AcceptEvent();
                break;
```

11. **Doc comment.** Replace "the part palette on the right" (and any remaining palette/drag wording) with: "a picker that opens beside a box, listing the fittings its socket accepts and previewing each on the strip before it is chosen".

- [ ] **Step 6: Delete the palette and its drag payload**

```bash
git rm dimenship/scripts/ui/focus/loadouts/PartPalette.cs dimenship/scripts/ui/focus/loadouts/PartPalette.cs.uid \
  dimenship/scripts/ui/focus/loadouts/LoadoutDragData.cs dimenship/scripts/ui/focus/loadouts/LoadoutDragData.cs.uid
```

Run: `grep -rn "PartPalette\|LoadoutDragData\|_palette\|_frames\b" dimenship/scripts`
Expected: no output.

- [ ] **Step 7: Build and generate sidecars**

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q` → `0 Error(s)`.
Run: `/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64_console.exe --headless --path dimenship --import` → `.uid` files for `StagePopover.cs`, `FittingPicker.cs`, `FrameChooser.cs`.

- [ ] **Step 8: Verify in the game**

1. The right-hand palette is gone; the header reads the template name over the frame name, then CHANGE FRAME, the verdict chip, the concept label and a disabled QUEUE BUILD.
2. *Survey Pattern A*, click the Sensor box: a popover titled `COMPATIBLE SENSOR FITTINGS` opens to the box's right with a notch pointing at it; three sensors with images, `FITTED` on Basic Sensor Array, and a disabled-looking last row `CLEAR SOCKET`.
3. Hover *Deep Scan Array*: its row shows `MASS +50 · POWER -120 · SCAN +270`; the strip hatches the bar and reads `PREVIEW 450 / 600`; SCAN shows `+270`. The Sensor box and the drawing do not change.
4. Hover *Phase Resonance Probe*: `PREVIEW 580 / 600`. Click it: the picker closes, the box now shows it, `Ctrl+Z` puts Basic Sensor Array back.
5. Open the Investigation picker and hover `CLEAR SOCKET`: the strip previews the draw dropping by 120. Choose it: the box reads `EMPTY`, the verdict chip `INCOMPLETE`.
6. Select *Deep Prospector* (`720 / 600 — OVER BUDGET BY 120`). Open its Sensor picker and hover *Basic Sensor Array* (−90 against the probe's −340): the strip reads `PREVIEW 470 / 600 · PREVIEW: COMPLETE`, because the candidate would change the verdict. Hover *Deep Scan Array* instead: `PREVIEW 590 / 600 · PREVIEW: COMPLETE`.
7. Click CHANGE FRAME: a popover under the button lists both frames with line-art thumbnails; the Surveyor carries `CURRENT`; hovering the Hauler reads `KEEPS tool · sensor · power — DROPS investigation`. Choose it: the Hauler appears with those three fitted.
8. Keyboard only: `Tab` to a box, `Enter` opens its picker with focus on the fitted row, arrow keys move and preview, `Enter` chooses, `Esc` closes the picker and returns focus to the box; a second `Esc` releases focus to the shell.
9. Click on empty stage or on the library while a picker is open: it closes. Click another box: the picker moves to that box in one click.

- [ ] **Step 9: Commit**

```bash
git add -A dimenship/scripts/ui/focus/loadouts
git commit -F - <<'EOF'
feat: a fitting is chosen beside its box, after seeing what it would change

Clicking a box opens a picker next to it listing the fittings that socket
accepts, with the fitted one marked and CLEAR SOCKET last. Hovering or
focusing a row previews it: the row names what choosing it would change, and
the strip hatches the same change onto the power bar and beside each stat and
cost. A preview rolls up a copy of the template, so it can never become an
edit or an undo step, and the frame art never changes with it.

CHANGE FRAME opens the same kind of popover over the two frames, saying what
a swap would keep and drop before it is made. The permanent palette and every
drag gesture go: dragging off the palette has no palette, and box-to-box swaps
only ever applied between two sockets of one kind, which neither frame has.
Escape closes a popover before the shell sees it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 8: The library shows each template as its machine

**Files:**
- Modify: `dimenship/scripts/ui/focus/loadouts/TemplateList.cs`

**Interfaces:**
- Consumes: `FrameArtLibrary.Rasterise(FrameDef, int)` (Task 5), `LoadoutRollup.Of`, `VerdictText`
- Produces: nothing new; `TemplateList.Refresh(IReadOnlyList<LoadoutDraft> templates, int selected)` keeps its signature.

- [ ] **Step 1: Drop the info box**

In `TemplateList.cs`:

1. Delete the `_info` field, the `AddChild(BoxSection.Create("Selected Template Info", out _info));` line, the `Clear(_info);` call, and everything in `Refresh` after the `for` loop that adds entries (the description label and the five `BoxSection.Row` lines, plus the early `return` guard before them).
2. Rename the section: `BoxSection.Create("Loadout Templates", out _rows)` → `BoxSection.Create("Loadouts", out _rows)`.
3. Change `CustomMinimumSize = new Vector2(260, 0);` to `CustomMinimumSize = new Vector2(220, 0);`.
4. Update the class summary to: "The composer's left column: one card per template — its frame drawn small, its name, and where it stands — and a way to add one. The card's micro line carries what the removed *Selected Template Info* box carried (frame fill and verdict); the template's description is the card's tooltip."

- [ ] **Step 2: Cards carry a thumbnail**

Add constants to `TemplateList`:

```csharp
    private const int ThumbnailHeight = 72;

    /// <summary>A quarter of the canvas, rasterised once per frame and cached: 300×180, above the card's size.</summary>
    private const int ThumbnailScalePermille = 250;
```

Replace `Entry._Ready()` with:

```csharp
        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(_selected));
            TooltipText = _template.Description;

            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            AddChild(column);

            var rollup = LoadoutRollup.Of(_template);

            // The same frame art the stage draws, small, tinted, and without the glow: a column of
            // glowing machines would compete with the one on the stage.
            column.AddChild(new TextureRect
            {
                Texture = FrameArtLibrary.Rasterise(rollup.Frame, ThumbnailScalePermille),
                CustomMinimumSize = new Vector2(0, ThumbnailHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = ShellPalette.Projection,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            var name = new Label
            {
                Text = _template.Name,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeColorOverride(
                "font_color", _selected ? ShellPalette.TextTitle : ShellPalette.TextPrimary);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            column.AddChild(name);

            var line = new Label
            {
                Text = $"{_template.FilledSockets}/{rollup.Frame.Sockets.Count} · {VerdictText.Of(rollup.Verdict)}",
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            line.AddThemeColorOverride("font_color", VerdictText.Colour(rollup.Verdict));
            line.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(line);
        }
```

(`ThumbnailHeight` and `ThumbnailScalePermille` are the outer class's private constants; a nested class can read them.)

- [ ] **Step 3: Build**

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q` → `0 Error(s)`.

- [ ] **Step 4: Verify in the game**

1. LOADOUTS shows three cards, each with its frame drawn small in pale cyan above the name, and `4/4 · COMPLETE`, `4/4 · OVER BUDGET`, `4/5 · INCOMPLETE` beneath, in words.
2. The selected card has the accent border. Hovering a card shows its description.
3. Clearing a socket updates the card's line; swapping a template's frame changes its thumbnail.
4. `+ NEW TEMPLATE` sits at the foot of the list and adds an empty Surveyor card (`0/4 · INCOMPLETE`).

- [ ] **Step 5: Commit**

```bash
git add dimenship/scripts/ui/focus/loadouts/TemplateList.cs
git commit -F - <<'EOF'
feat: each loadout template in the library is drawn as the machine it builds

The library's cards carry their frame's line art, small and without the
glow, above the template name and a line reading how many sockets are filled
and where the template stands, in words. That line carries what the Selected
Template Info box carried, so the box goes; the template's description moves
to the card's tooltip.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 9: The documents catch up, and the whole editor is checked end to end

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md`
- Modify: `docs/superpowers/plans/2026-09-14-glass-console-loadout-editor.md` (this file — `Status:`)

- [ ] **Step 1: `CLAUDE.md`**

1. In the `src/Dimenship.Shell` paragraph, change

```
`PanelId`, `PanelDescriptor`, `ZoneKind`, `LayoutState`, `LayoutSerializer`, `GraphGeometry`,
`GraphSelection`, `FlowBands`.
```

to

```
`PanelId`, `PanelDescriptor`, `ZoneKind`, `LayoutState`, `LayoutSerializer`, `GraphGeometry`,
`GraphSelection`, `FlowBands`, `FrameArtSerializer`, `StageGeometry`.
```

2. In the `dimenship/` layout block, under the `assets/icons/...` line, add:

```
assets/loadouts/{frames,fittings}/   Loadout frame line art + JSON placement sidecars, fitting images
```

3. Replace the whole `LoadoutsFocus` bullet (it begins "`LoadoutsFocus` and everything under `scripts/ui/focus/loadouts/` is the second labelled **concept mock**") with:

```markdown
- `LoadoutsFocus` and everything under `scripts/ui/focus/loadouts/` is the second labelled
  **concept mock**, on the same terms: it composes loadout templates, nothing builds them, nothing
  persists, and no robot, socket storage or refit task exists anywhere in the kernel. Its one live
  reading is the build cost, which compares against `WorldSnapshot.Resources` through one shared
  `ItemStock` rather than inventing stock. Its vocabulary is fixed and deliberate — **loadout
  template**, **socket**, **fitting** — and *fitting* is not *module*, because `module` is the
  shipped bulk commodity; *fitting* is the word `2026-08-21-bot-composition-design.md` settled, and
  the mock's earlier placeholder *part* is gone. It is presented as a **glass console**
  (`docs/superpowers/specs/2026-09-14-glass-console-loadout-editor-design.md`): `LoadoutStage` draws
  the frame's line art from `assets/loadouts/frames/`, rasterised at runtime and tinted and glowed
  by `projection.gdshader` from `ShellPalette.Projection`, with one `FittingBox` per socket joined to
  its connector by a leader line and a `FittingPicker` beside the selected box. A preview rolls up a
  **copy** of the template, never the template, so it cannot reach the undo stack. Box placement
  lives in a JSON sidecar beside each frame SVG, read by `Dimenship.Shell`'s `FrameArtSerializer`;
  box positions are authored and **never moved automatically** — `StageGeometry.Problems` reports a
  collision instead — and a bad sidecar falls back for the whole frame to a plain column that names
  the problem. Frame SVGs import as Keep File because the stage reads their source; fitting images
  are found by convention at `assets/loadouts/fittings/{id}.svg`. There is no drag and drop. See
  both specs, including their *Not built* lists, before building anything on it.
```

- [ ] **Step 2: The mock spec points at its successor**

In `docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md`, insert directly under the `Status: Draft` line:

```markdown

> **Presentation superseded** by `2026-09-14-glass-console-loadout-editor-design.md`. The socket
> rows, the part palette and drag and drop are replaced by a drawn frame with a box per socket and
> a picker beside the selected box, and *part* is renamed **fitting**. The rules, the sample
> content, the verdicts and the *Not built* list below still hold.
```

- [ ] **Step 3: Run every suite**

Run: `dotnet test tests/Dimenship.Shell.Tests`
Expected: all PASS, including `FrameArtSerializerTests` and `StageGeometryTests`.

Run: `dotnet test tests/Dimenship.Core.Tests`
Expected: all PASS (nothing in the kernel changed; this proves it).

Run: `dotnet build dimenship/Dimenship.csproj -nologo -v q`
Expected: `0 Error(s)`.

Run: `grep -rn "\bpart\b\|\bparts\b\|Part(" dimenship/scripts/ui/focus/loadouts`
Expected: no player-facing string or identifier meaning a fitting. (A comment quoting the old mock spec's history is acceptable; anything else is renamed.)

- [ ] **Step 4: The spec's in-editor checklist, end to end**

Run the game (`/c/Tools/Godot/Godot_v4.7.1-stable_mono_win64.exe --path dimenship`), start a campaign, open Robotics, and confirm each:

1. Both frames, with every socket filled and then emptied — boxes, images, `EMPTY` placeholders, leader lines to the right ports.
2. Picker preview on every row, `CLEAR SOCKET` included — row line, hatched bar, stat and cost `+n`.
3. A frame swap in each direction matches its carry-over preview line.
4. Undo and redo across fits, clears and swaps.
5. An over-budget template and a template with no core say so in words.
6. A sidecar broken on purpose falls back with its problem named (revert afterwards).
7. At a 1280×720 window neither frame reports `BOXES OVERLAP AT THIS SIZE`. If one does, move that sidecar's `box` centres apart and repeat; commit the sidecar change with the docs.
8. One full pass using only the keyboard: `Tab`, arrows, `Enter`, `Delete`, `Esc`, `Ctrl+Z`, `Ctrl+Y`.

- [ ] **Step 5: Mark this plan built and commit**

Change this file's `Status: Draft` to `Status: Built`.

```bash
git add CLAUDE.md docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md docs/superpowers/plans/2026-09-14-glass-console-loadout-editor.md
git commit -F - <<'EOF'
docs: the repository's guide and the mock spec describe the glass console

CLAUDE.md's loadout entry now describes what is built: the fitting
vocabulary, the stage and its picker, the placement sidecars and who reads
them, the rule that boxes are never moved automatically, and that a preview
rolls up a copy rather than the template. The Shell list gains the two new
units and the asset layout gains the loadouts folder.

The original mock spec keeps its rules and its Not built list, and says at
the top that its presentation has been superseded and by what.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```
