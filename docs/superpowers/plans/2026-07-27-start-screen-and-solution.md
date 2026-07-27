# Start Screen + Solution Structure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the repository into a single solution — Godot 4.7.1 .NET game project, an engine-free `Dimenship.Core` logic library, and an NUnit test project — and ship a working start screen that proves the reference chain end to end.

**Architecture:** `dimenship/Dimenship.csproj` (Godot.NET.Sdk) references `src/Dimenship.Core` (plain class library, no Godot types). `tests/Dimenship.Core.Tests` (NUnit) also references Core. Core references nothing, so `dotnet test` runs without the engine. The start screen is a `Control`-rooted scene whose C# script reads its title and version text from `Dimenship.Core.GameInfo`.

**Tech Stack:** Godot 4.7.1-mono, .NET (net8.0), C#, NUnit 4, MSBuild solution file.

## Global Constraints

- Target framework is `net8.0` for **all three** projects. They must stay identical. No .NET 8 SDK is installed locally (only 6.0.428 / 9.0.312 / 10.0.203); SDK 10 builds net8.0 by restoring `Microsoft.NETCore.App.Ref` from NuGet. If that restore fails, raise all three projects together to the lowest TFM `Godot.NET.Sdk/4.7.1` accepts — never mix.
- Godot SDK version is `Godot.NET.Sdk/4.7.1`, matching the installed editor (`Godot_v4.7.1-stable_mono_win64`).
- The Godot C# project file must be named `Dimenship.csproj` and live next to `project.godot`, because `project.godot` declares `dotnet/project/assembly_name="Dimenship"`.
- `Dimenship.Core` must never reference `GodotSharp` or `using Godot`. This is what keeps tests engine-free.
- Do **not** run Godot's *Project → Tools → C# → Create solution*. It emits a competing `dimenship/Dimenship.sln`. Only `DimenshipGame.sln` at the repo root exists.
- Shell is PowerShell on Windows. Paths in commands use backslashes or forward slashes; MSBuild `ProjectReference` paths use backslashes.
- The Godot editor is not installed on PATH (unextracted zip in `~/Downloads`). No task may claim the scene renders correctly. Visual verification is handed to the user at the end.

---

### Task 1: Core library, tests, and shared build config

**Files:**
- Create: `Directory.Build.props`
- Create: `src/Dimenship.Core/Dimenship.Core.csproj`
- Create: `src/Dimenship.Core/GameInfo.cs`
- Create: `tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj`
- Test: `tests/Dimenship.Core.Tests/GameInfoTests.cs`
- Modify: `.gitignore`
- Modify: `DimenshipGame.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: nothing.
- Produces: `Dimenship.Core.GameInfo` — `public const string Title` (`"Dimenship"`), `public const string Version` (`"0.1.0"`), `public static string DisplayVersion { get; }` returning `"v0.1.0"`. Task 3's script reads `GameInfo.Title` and `GameInfo.DisplayVersion`.

- [ ] **Step 1: Create the shared build props**

`Directory.Build.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the Core library project file**

`src/Dimenship.Core/Dimenship.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Dimenship.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create the test project file**

`tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>Dimenship.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Dimenship.Core\Dimenship.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add the test packages**

Package versions are resolved by the CLI rather than pinned by hand, so the latest compatible NUnit stack is used.

```bash
dotnet add tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj package NUnit
```

```bash
dotnet add tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj package NUnit3TestAdapter
```

```bash
dotnet add tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj package Microsoft.NET.Test.Sdk
```

If any of these fail with a net8.0 targeting-pack restore error, that is the Global Constraints fallback firing — stop and report it rather than silently changing one project's TFM.

- [ ] **Step 5: Add both projects to the solution**

```bash
dotnet sln DimenshipGame.sln add src/Dimenship.Core/Dimenship.Core.csproj tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj
```

- [ ] **Step 6: Write the failing test**

`tests/Dimenship.Core.Tests/GameInfoTests.cs`:

```csharp
using NUnit.Framework;

namespace Dimenship.Core.Tests;

public class GameInfoTests
{
    [Test]
    public void Title_IsTheGameName()
    {
        Assert.That(GameInfo.Title, Is.EqualTo("Dimenship"));
    }

    [Test]
    public void DisplayVersion_PrefixesVersionWithV()
    {
        Assert.That(GameInfo.DisplayVersion, Is.EqualTo("v0.1.0"));
    }
}
```

- [ ] **Step 7: Run the test to verify it fails**

```bash
dotnet test tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj
```

Expected: build error `CS0103: The name 'GameInfo' does not exist in the current context` (or `CS0246`). A compile failure is the correct red state here — there is no `GameInfo` yet.

- [ ] **Step 8: Write the minimal implementation**

`src/Dimenship.Core/GameInfo.cs`:

```csharp
namespace Dimenship.Core;

/// <summary>Static identity of the game, shared by the engine layer and tests.</summary>
public static class GameInfo
{
    public const string Title = "Dimenship";
    public const string Version = "0.1.0";

    public static string DisplayVersion => $"v{Version}";
}
```

- [ ] **Step 9: Run the tests to verify they pass**

```bash
dotnet test tests/Dimenship.Core.Tests/Dimenship.Core.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 10: Update .gitignore**

Append to the root `.gitignore` (keep the existing `bin/`, `obj/`, `/packages/`, `riderModule.iml`, `/_ReSharper.Caches/` lines):

```
.idea/
.vs/
*.user
```

- [ ] **Step 11: Commit**

```bash
git add .gitignore Directory.Build.props DimenshipGame.sln src tests && git commit -m "feat: add Dimenship.Core library with NUnit test project"
```

---

### Task 2: Godot C# project wired into the solution

**Files:**
- Create: `dimenship/Dimenship.csproj`
- Modify: `DimenshipGame.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: `src/Dimenship.Core/Dimenship.Core.csproj` from Task 1.
- Produces: assembly `Dimenship`, root namespace `Dimenship`, targeting net8.0 with `Godot.NET.Sdk/4.7.1`. Task 3 adds `.cs` files that compile into it; the SDK globs `**/*.cs` automatically, so no file needs listing.

- [ ] **Step 1: Create the Godot project file**

`dimenship/Dimenship.csproj`:

```xml
<Project Sdk="Godot.NET.Sdk/4.7.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>Dimenship</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\Dimenship.Core\Dimenship.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add it to the solution**

```bash
dotnet sln DimenshipGame.sln add dimenship/Dimenship.csproj
```

- [ ] **Step 3: Build the whole solution to verify the SDK and reference resolve**

```bash
dotnet build DimenshipGame.sln
```

Expected: `Build succeeded`, 0 errors, three projects built. A `Godot.NET.Sdk` restore error here means the version string does not match a published SDK package — check the installed editor version before changing anything else.

- [ ] **Step 4: Verify Core actually flows into the Godot output**

```bash
ls dimenship/.godot/mono/temp/bin/Debug/Dimenship.Core.dll
```

Expected: the file exists. This is the proof that the Godot assembly will have Core available at runtime.

Note the path: `Godot.NET.Sdk` sets `BaseOutputPath` to `.godot/mono/temp/bin/` and `AppendTargetFrameworkToOutputPath=false`, so a Godot project's output never appears under `bin/<Config>/<TFM>/` the way an ordinary .NET project's does.

- [ ] **Step 5: Commit**

```bash
git add dimenship/Dimenship.csproj DimenshipGame.sln && git commit -m "feat: add Godot C# project referencing Dimenship.Core"
```

---

### Task 3: Start screen scene, script, and project entry point

**Files:**
- Create: `dimenship/scripts/StartScreen.cs`
- Create: `dimenship/scenes/StartScreen.tscn`
- Create: `dimenship/scenes/Game.tscn`
- Delete: `dimenship/start_game_screen.tscn`
- Modify: `dimenship/project.godot`

**Interfaces:**
- Consumes: `Dimenship.Core.GameInfo.Title` and `Dimenship.Core.GameInfo.DisplayVersion` from Task 1; the `Dimenship` assembly from Task 2.
- Produces: `Dimenship.StartScreen : Godot.Control`, attached to `res://scenes/StartScreen.tscn`, which is the project's `run/main_scene`.

The node paths in the script and the node names in the scene must match exactly. Both are written in this task; if you rename a node, rename it in both places.

- [ ] **Step 1: Write the start screen script**

`dimenship/scripts/StartScreen.cs`:

```csharp
using Dimenship.Core;
using Godot;

namespace Dimenship;

public partial class StartScreen : Control
{
    private const string GameScenePath = "res://scenes/Game.tscn";

    private Label _titleLabel = null!;
    private Label _versionLabel = null!;
    private Button _playButton = null!;
    private Button _quitButton = null!;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel");
        _versionLabel = GetNode<Label>("CenterContainer/VBoxContainer/VersionLabel");
        _playButton = GetNode<Button>("CenterContainer/VBoxContainer/PlayButton");
        _quitButton = GetNode<Button>("CenterContainer/VBoxContainer/QuitButton");

        _titleLabel.Text = GameInfo.Title;
        _versionLabel.Text = GameInfo.DisplayVersion;

        _playButton.Pressed += OnPlayPressed;
        _quitButton.Pressed += OnQuitPressed;

        // Platforms where the OS owns app lifetime have no business showing a Quit button.
        _quitButton.Visible = OS.GetName() is not ("Android" or "iOS" or "Web");

        _playButton.GrabFocus();
    }

    private void OnPlayPressed()
    {
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
```

The Settings button has no handler on purpose — it ships `disabled = true` in the scene until a settings screen exists.

- [ ] **Step 2: Verify the script compiles against Core**

```bash
dotnet build dimenship/Dimenship.csproj
```

Expected: `Build succeeded`, 0 errors. This is the real gate for this task — it proves `using Dimenship.Core` resolves from inside the Godot assembly.

- [ ] **Step 3: Write the start screen scene**

`dimenship/scenes/StartScreen.tscn`. The `uid` line is deliberately omitted; Godot assigns one on first import.

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/StartScreen.cs" id="1_startscreen"]

[node name="StartScreen" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_startscreen")

[node name="Background" type="ColorRect" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
color = Color(0.0705882, 0.0784314, 0.113725, 1)

[node name="CenterContainer" type="CenterContainer" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2

[node name="VBoxContainer" type="VBoxContainer" parent="CenterContainer"]
layout_mode = 2
theme_override_constants/separation = 24
alignment = 1

[node name="TitleLabel" type="Label" parent="CenterContainer/VBoxContainer"]
layout_mode = 2
theme_override_font_sizes/font_size = 72
text = "Dimenship"
horizontal_alignment = 1

[node name="VersionLabel" type="Label" parent="CenterContainer/VBoxContainer"]
layout_mode = 2
theme_override_colors/font_color = Color(0.6, 0.6, 0.65, 1)
theme_override_font_sizes/font_size = 18
text = "v0.0.0"
horizontal_alignment = 1

[node name="PlayButton" type="Button" parent="CenterContainer/VBoxContainer"]
custom_minimum_size = Vector2(280, 64)
layout_mode = 2
text = "Play"

[node name="SettingsButton" type="Button" parent="CenterContainer/VBoxContainer"]
custom_minimum_size = Vector2(280, 64)
layout_mode = 2
disabled = true
text = "Settings"

[node name="QuitButton" type="Button" parent="CenterContainer/VBoxContainer"]
custom_minimum_size = Vector2(280, 64)
layout_mode = 2
text = "Quit"
```

The `text` values on `TitleLabel` and `VersionLabel` are editor-time placeholders; `_Ready` overwrites both from `GameInfo`. `VersionLabel` deliberately says `v0.0.0` so that a wrong value on screen is obvious if the script ever fails to run.

- [ ] **Step 4: Write the placeholder game scene**

`dimenship/scenes/Game.tscn`:

```
[gd_scene format=3]

[node name="Game" type="Node2D"]

[node name="PlaceholderLabel" type="Label" parent="."]
offset_left = 660.0
offset_top = 500.0
offset_right = 1260.0
offset_bottom = 580.0
theme_override_font_sizes/font_size = 32
text = "Game scene placeholder"
horizontal_alignment = 1
```

- [ ] **Step 5: Delete the old placeholder scene**

```bash
git rm dimenship/start_game_screen.tscn
```

- [ ] **Step 6: Set the main scene and window size**

In `dimenship/project.godot`, add `run/main_scene` under `[application]` so the section reads:

```
[application]

config/name="Dimenship"
run/main_scene="res://scenes/StartScreen.tscn"
config/features=PackedStringArray("4.7", "Mobile")
config/icon="res://icon.svg"
```

and add the viewport size to `[display]`, keeping the existing stretch keys:

```
[display]

window/size/viewport_width=1920
window/size/viewport_height=1080
window/stretch/mode="canvas_items"
window/stretch/aspect="expand"
```

- [ ] **Step 7: Rebuild the solution**

```bash
dotnet build DimenshipGame.sln
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 8: Run the tests once more**

```bash
dotnet test DimenshipGame.sln
```

Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 9: Commit**

```bash
git add dimenship && git commit -m "feat: add start screen scene backed by GameInfo"
```

- [ ] **Step 10: Hand the visual check to the user**

Do not mark the start screen verified. Report exactly this state: the solution builds, tests pass, and the scene files are written but have never been opened by Godot. Ask the user to extract `~/Downloads/Godot_v4.7.1-stable_mono_win64.zip`, open `C:\Work\DimenshipGame\dimenship\project.godot`, and press F5. What they should see: dark background, "Dimenship" title, "v0.1.0" beneath it, a focused Play button that switches to the placeholder scene, a greyed-out Settings button, and a working Quit button. Import errors on the `.tscn` files, if any, surface at that moment and are the follow-up.

---

## Notes for the implementer

- Godot has never imported these scenes, so `.tscn` property names are the highest-risk part of this plan. If the editor reports an unknown property, the fix is to open the scene in the editor, set the property through the inspector, and let Godot re-save the file — hand-editing further is guesswork.
- `dimenship/.godot/` is gitignored and holds the import cache. If the editor behaves oddly after these files land, deleting that directory forces a clean reimport.
- Do not add `dimenship/Dimenship.sln` if Godot offers to create one. The root `DimenshipGame.sln` is the only solution.
