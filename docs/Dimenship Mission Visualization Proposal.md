# Dimenship – Mission Visualization Proposal

Draft design proposal – v0.2
Scope: MVP-focused, PC / Steam, Operational Time
Technical direction: C# .NET 10 deterministic simulation core

## 1. Purpose

The Vessel SCADA screen should remain visually restrained. Stable systems should look stable; movement or warnings should attract attention only when something meaningful changes.

This creates a presentation gap during Operational Time. Missions are the natural place for continuous visible activity while the vessel itself remains calm.

The proposed solution is a **Mission Monitor**: an optional, watch-only panel that visualizes an autonomous mission already being resolved by the simulation.

Its purpose is to:

- make mission execution more satisfying to watch;
- make robot behavior and program-branch changes easier to understand;
- give missions visible activity without introducing direct control;
- provide more dynamic but still truthful material for screenshots, trailers, and videos.

The Mission Monitor must not become a tactical game or a second simulation.

## 2. Core Principle

The player prepares the mission, starts it, and then observes the result during Operational Time.

The Mission Monitor has no influence on mission execution. The simulation remains authoritative and deterministic; the monitor only presents its current state and events.

This preserves the existing Dimenship loop:

prepare -> launch -> observe -> review -> improve

## 3. Panel, Not Main View

The Mission Monitor should normally run as a docked panel inside the existing operational console, not replace the current SCADA, programming, or management screen.

The player should be able to keep an eye on an active mission while:

- supervising the vessel through SCADA;
- reviewing production or storage;
- working on robot programs and templates;
- reading logs or preparing later missions.

The panel can occupy a substantial strip at the side or bottom of the workspace, but the main game screen remains usable. A larger temporary view may be added only if it proves useful; it is not required for MVP.

## 4. Visual Direction

The Mission Monitor should use a flat left-to-right presentation, not a tactical map and not a true 2D battlefield simulation.

Bots visually advance through the mission from left to right. As mission events occur, they may:

- move or change relative formation;
- encounter resources, objectives, structures, or hostiles;
- scan or interact with a target;
- fire or receive fire;
- activate shields or show damage;
- stop, fall back, or retreat;
- reach extraction and leave the scene.

These positions are presentation positions only. The game does not simulate tactical coordinates, pathfinding, cover geometry, or player-directed movement. The visualization translates mission state into a readable staged scene.

A simple logical sequence is enough:

Entry -> Travel -> Encounter -> Objective -> Extraction

Different missions can skip, repeat, or replace stages without requiring a physical level.

### Recommended art style

Use a sensor-reconstruction / telemetry aesthetic that matches the control-room interface while keeping asset requirements low.

- simple dark filled robot silhouettes with luminous line-art details;
- a few clear visual differences for frames, weapons, sensors, cargo, and defensive equipment;
- flat hostile silhouettes or simplified machine shapes;
- simple objective/resource props;
- terrain and structures mainly as dark foreground/background silhouettes;
- limited parallax and scrolling to create motion;
- reusable effects for scans, beams/projectiles, shields, impacts, collection, and extraction.

Pure wireframe should be avoided as the main style because multiple units and terrain elements would become difficult to read in a small panel. Full detailed cel-shaded characters and environments would create unnecessary asset and animation cost. The preferred style is therefore flat silhouettes plus restrained technical line art.

## 5. Interaction Model

The Mission Monitor is watch-only. The player cannot influence or change the running mission from the panel.

The player may select a robot to access its setup and program/template information through the normal game UI.

A deployed robot cannot be reconfigured during the mission. Any program or setup changes are made to the template and affect later missions only after the robot returns and goes through the required upgrade/refit process.

The monitor should show a compact execution log. For MVP it is enough to report important mission events and changes of the active program branch, rather than individual rule evaluation.

Example:

```text
00:18  Contact detected
00:18  SEARCH -> DEFENSIVE
00:22  Evidence secured
00:22  DEFENSIVE -> EXTRACTION
00:31  Group returned
```

This lets the player connect program design with visible behavior without requiring event-to-rule inspection tooling.

## 6. MVP Mission Presentation

The MVP should answer one question: after configuring a mission, is it interesting to watch the autonomous system execute it?

The first version should support only the initial mission types.

### Mining

Travel -> scan -> resource encountered -> extract -> cargo increases -> react to hazard if present -> return.

### Scavenging

Travel -> site/object encountered -> inspect -> collect salvage -> react to contact or capacity limit -> return.

### Investigation

Travel -> scan -> objective/evidence encountered -> approach -> secure or analyze -> react to danger -> extract.

The same renderer should support all three. Mission-specific content should come mainly from simulation data and simple reusable scene elements.

### MVP visual components

- docked Mission Monitor panel;
- flat left-to-right scene;
- reusable robot silhouettes;
- simple movement and formation changes;
- basic terrain/structure silhouettes with limited parallax;
- scan effect;
- resource, objective, evidence, and hostile representations;
- simple weapon, shield, hit, and damage effects;
- extraction/return presentation;
- mission progress/status;
- compact event and active-branch log;
- robot selection with access to setup/program/template information.

No tactical movement or direct mission control is added.

## 7. Technical Model

The visualization consumes simulation state rather than owning mission logic.

```text
Mission Simulation
    -> Mission State + Telemetry Events
    -> Mission Visualization Model
    -> Docked Mission Monitor
```

Useful mission events may include:

```text
ContactDetected
ObjectiveDetected
ObjectiveSecured
FormationChanged
DamageReceived
WeaponFired
ProgramBranchChanged
ResourceCollected
ExtractionStarted
MissionCompleted
```

The visualization may interpolate cosmetic movement, scrolling, formation placement, and effects between deterministic simulation updates. These visual positions never feed results back into the simulation.

This keeps the C# .NET 10 simulation core deterministic, testable, and independent from the presentation layer.

## 8. Scope and Asset Strategy

The feature is worth adding only if the visual vocabulary remains small and reusable.

Prefer:

- a small number of robot frame silhouettes with module variations;
- simple animation states such as travel, idle/scan, attack, hit, interact, disabled, and retreat;
- reusable effects rather than bespoke combat animation;
- modular terrain and structure silhouettes rather than unique levels;
- presentation stages and scripted placement instead of tactical pathfinding;
- visual differences between strata created largely through background shapes, effects, and environment combinations.

The SCADA screen remains the calm diagnostic view. The Mission Monitor becomes the place where continuous motion is expected and where the consequences of planning are visible.

## 9. Later Enhancement

- Post-MVP: mission replay/reconstruction and richer visual variety can be considered later if players actually use and enjoy the Mission Monitor.

## 10. Recommendation

Implement the Mission Monitor as a docked, watch-only, left-to-right visualization layer for MVP.

It should look like a remote sensor reconstruction rather than a literal battlefield. The simulation decides what happens; the monitor stages those events with simple silhouettes, movement, formation changes, targets, weapon effects, and branch-change logging.

This gives Dimenship a visually active counterpart to the deliberately calm SCADA interface without introducing tactical simulation, direct control, or a large environment/animation asset burden.
