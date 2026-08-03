# Top Focus Navigation — Design

Date: 2026-08-03
Status: Approved

## Goal

Move focused-view navigation from the vertical rail at the left of the shell to a horizontal navigation rail at the top, following the supplied operational-console concept. The workspace reclaims the left-rail width.

## Scope

- Keep the existing `Vessel` menu and debug menu in the menu bar.
- Replace the existing `View` menu with a top focus-navigation rail immediately below the menu bar.
- Render one horizontal button per registered `ZoneKind.Focus` descriptor, in the current alphabetical order.
- Preserve focus switching, selected-state feedback, saved active focus, keyboard traversal, and the existing `Ctrl+1` through `Ctrl+N` shortcuts.
- Remove the left rail entirely.
- Remove the old View-menu actions: Inspector toggle, Console toggle, and Reset layout. The Inspector and Console remain available through their existing splitter-driven layout and programmatic interactions.

## Architecture

`Rail` changes from a `VBoxContainer` that combines focus navigation and zone toggles into an `HBoxContainer` responsible only for focus navigation. `ShellRoot.BuildTree()` places it between the menu bar and the body, rather than as the first child inside the body.

`ShellRoot` no longer builds the `View` menu. The menu bar retains Vessel and its conditional Debug menu. No persistence or action contract changes are needed: `Rail.SetActive`, `ShellActions.FocusRequested`, and `LayoutState.ActiveFocus` already describe the required behavior.

## Visual behavior

The rail uses the existing frosted-glass treatment, with compact, horizontally sized buttons. The active focus view uses the existing warning accent; inactive buttons use dim text. It spans the window width beneath the menu bar and sits above the centre/inspector/console split layout.

## Error handling

The focus list is still sourced from the panel registry, so adding or removing focus descriptors automatically updates the top navigation. The existing saved-layout fallback remains responsible for an unavailable active focus.

## Testing and verification

No engine-free behavior changes, serialization changes, or new pure functions are introduced, so no new unit test is required. Run `dotnet build DimenshipGame.sln` and `dotnet test` to catch compilation and regression failures.

Manual verification in the Godot editor:

- The vertical left rail is absent and the central workspace uses its former width.
- The View menu is absent.
- A horizontal frosted focus rail appears below the menu bar.
- Selecting each focus button changes the centre view and its active treatment.
- `Ctrl+1` through `Ctrl+N`, keyboard traversal, and restart persistence still select the expected view.
- Inspector and Console retain their current splitter behavior.

## Out of scope

Adding new focus views, changing the menu content other than removing View, adding top-bar telemetry, redesigning the Inspector or Console, and mobile-layout work.
