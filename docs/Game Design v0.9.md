# Dimenship

## Game Design Document Foundation

**v0.9 - PC / Steam - Operational Time + SCADA Vessel Schematic + Production Layer**

| | |
|---|---|
| **Document Purpose** | Updated foundation GDD. Reframes the vessel/base layer as a SCADA-like operational schematic: diagnostic, readable, and navigational; not a freeform base-builder and not the primary planning control. |
| **Target Platform** | PC first, Steam distribution. Mouse/keyboard primary. Controller support optional and not MVP-critical. |
| **Business Model** | Premium or Free Demo + Premium Unlock. No ads. No microtransactions. |
| **Core Mode** | Single-player deterministic strategy with autonomous missions, operational-time simulation, storyline-driven progression, and SCADA-style vessel supervision. |
| **Campaign Frame** | Fugitive investigation: reconstruct a fabricated murder case through autonomous strata operations, witness discovery, contradiction analysis, and final return to Native Strata. |
| **Technical Direction** | C# .NET 10 simulation core. UI/engine layer remains replaceable. Deterministic, serializable, testable simulation state. |
| **Last Updated** | 2026-08-12 |

> **Design shift in v0.9**  
> The production layer is named. The schematic shows only the facilities a player can meaningfully schedule, prioritize, automate, or optimize — Mission Docks, Resource Storage, Matter Reactors, Factories. Everything else the vessel runs, including the Power Core and the Stabilization Array, is a state card rather than a node. Missions recover Matter Mix rather than a dozen separate ores, and reactors separate it under selectable processing modes. See §5.8 and §5.9.

> **Design shift in v0.8**  
> Classical base-building remains out of scope. The vessel is shown through a blueprint / SCADA-like schematic that displays facility state, utilization, line load, transport capacity, energy pressure, bottlenecks, alerts, and quest-readiness dependencies. The schematic supports inspection and quick navigation to detailed management pages. It does not become a freeform facility-placement game or a graph-based planning editor.

## Contents

1. High Concept
2. v0.8 Design Decision
3. Preserved Identity
4. Core Gameplay Loop
5. Vessel SCADA Schematic (5.8 Production Facilities and Material Flow, 5.9 Passive Systems and Emergency Recovery)
6. Base Layer Without Base-Building
7. Core Systems Impact
8. UX / UI Information Architecture
9. Quest and Readiness Integration
10. MVP Scope Update
11. Technical Foundation - C# .NET 10
12. Balancing and Telemetry Principles
13. Risks and Mitigations
14. Open Questions
15. Revision Notes
16. Appendix A - Glossary Updates

## 1. High Concept

Dimenship is a PC-first deterministic systems strategy game in which the player operates a dimensional vessel from a remote refuge stratum and dispatches autonomous robot teams into parallel dimensional strata. The player is a fugitive researcher framed for murder by fabricated physical evidence, manipulated records, and AI-processed administrative judgement. Direct action is impossible. Survival and vindication depend on planning, infrastructure, robot doctrines, resource logistics, equipment readiness, and analytical reconstruction.

The PC version uses operational time. Missions, production, refits, research, analysis, verification, and vessel processes advance while the simulation is running. The player can pause at 0x to analyze and configure, then run the vessel at 1x, 2x, or 4x to observe system behavior. The player still does not directly control robots in the field; the player designs systems that operate independently and return with logs, evidence, resources, and failure data.

v0.8 clarifies the vessel/base layer. Dimenship should not become a classical base-builder. The vessel is represented as a SCADA-like operational schematic: a readable blueprint display showing facility performance, transport lines, energy load, throughput, bottlenecks, blocked states, alerts, and quest-readiness dependencies. The schematic is primarily informational and diagnostic. Detailed planning remains in dedicated screens such as Operations, Robotics, Energy, Storage, Research, Strata, Reports, and Case Board.

> **Positioning statement**  
> Dimenship on PC is an engineering control-room strategy game framed as a multidimensional murder investigation. It is not an idle clicker, action combat game, factory-placement game, or colony base-builder. It rewards readable systems, robust automation, and evidence reasoning.

## 2. v0.8 Design Decision

The team considered whether to add a base-building aspect. The updated decision is to avoid a full base-building genre layer and instead present the vessel as a blueprint / SCADA-like graph. This graph shows how the vessel is performing. It is also a navigation hub into dedicated management screens.

| Question | v0.8 Direction | Reason |
|---|---|---|
| Should the player place rooms or facilities? | No for MVP and likely no for core game. | Room placement would add a second genre, require more assets, and shift attention away from planning, automation, and investigation. |
| Should the vessel be visible? | Yes, as a SCADA-like schematic. | PC players benefit from a strong operational overview and visual system presence. |
| Should the schematic be the main planning control? | No. | The schematic diagnoses and navigates. Detailed planning belongs in dedicated pages with enough room for queues, priorities, doctrines, tables, logs, and contradiction explanations. |
| Should the schematic support interaction? | Yes, lightly. | Click-to-inspect, click-through navigation, filters, pinned warnings, dependency highlighting, and alert acknowledgement are useful without making the graph a full editor. |
| Should the graph have gameplay value? | Yes, through telemetry. | It must show state, cause, bottlenecks, readiness blockers, and TimeFlow-relevant risks, not just decorative status lights. |

## 3. Preserved Identity

- Autonomous systems remain central: robots execute doctrines; the player does not manually pilot them.
- Operational time remains central: the player runs, pauses, accelerates, observes, and revises the simulation during an active PC session.
- Determinism remains central: identical inputs should produce identical outcomes, including mission events, production results, bottlenecks, and AI estimates.
- Failure remains informative: poor preparation costs energy, damage, operational time, missed opportunity, or weak evidence, but produces actionable telemetry.
- Investigation remains systems play: truth is reconstructed from contradictions, leads, witness candidates, verification questions, and cross-stratum comparison.
- Optimization supports the story: optimization improves readiness, stability, autonomy, and evidence yield, but generic efficiency targets are not the quest spine.
- Control-room readability remains a pillar: dashboards, timeline lanes, logs, charts, inspectors, and the SCADA vessel schematic must expose state and cause.

## 4. Core Gameplay Loop

1. Pause at 0x and review the dashboard, SCADA schematic, current case objectives, blocked processes, robot damage, energy status, active missions, and alerts.
2. Read reports and drill into telemetry, contradiction notes, witness leads, process bottlenecks, combat hazards, or doctrine behavior.
3. Use the SCADA schematic to identify where the vessel is underperforming: low utilization, saturated lines, storage pressure, power limits, or blocked evidence flow.
4. Jump from schematic nodes or lines to detailed management screens. Revise production priorities, refits, doctrine rules, resource reservations, power budgets, research queues, or mission preparation.
5. Deploy autonomous missions or story operations by selecting stratum, mission template, robot group, doctrine preset, route, equipment package, and investigation focus.
6. Run operational time at 1x, 2x, or 4x. Watch telemetry, throughput, alerts, mission progress, process lanes, and case discoveries.
7. Respond only to meaningful interruptions: severe damage, anomaly detection, quest-critical findings, cascading system failure, or configured alert thresholds.
8. Pause again to revise systems and continue the story.

| TimeFlow | Player Experience | SCADA Behavior |
|---|---|---|
| 0x | Analysis pause. No simulation advancement. Full inspection and editing. | Full schematic detail, all overlays available, dependency tracing available, no live state changes. |
| 1x | Active monitoring with full telemetry. | Animated line flow, node utilization, live alerts, bottleneck changes, mission readiness changes. |
| 2x | Accelerated supervision with summarized events. | Minor state changes grouped. Warnings and trend changes remain visible. |
| 4x | Routine compression for trusted systems. | Only major alerts, anomalies, configured thresholds, and quest-critical changes interrupt the player. |

## 5. Vessel SCADA Schematic

The Vessel SCADA Schematic is a fixed or authored blueprint-like graph representing the operational state of the dimensional vessel. It is inspired by Supervisory Control and Data Acquisition displays, but it does not need to simulate industrial SCADA with strict realism. Its design purpose is readability: show what is running, what is blocked, what is overloaded, what is underperforming, and what matters to the current story operation.

### 5.1 Primary Purpose

- Provide an at-a-glance operational overview of the vessel.
- Expose bottlenecks and root causes without forcing the player to open every report.
- Make operational time visually legible through live utilization and line-load changes.
- Connect abstract systems to a concrete vessel representation.
- Serve as quick navigation to detailed management pages.
- Highlight systems relevant to current quest readiness and blocked story operations.

### 5.2 What the Schematic Shows

| Element | Examples | Displayed State |
|---|---|---|
| Facility node | Mission Dock, Resource Storage, Matter Reactor, Factory — the schedulable set of §5.8, and nothing else | Idle, active, blocked, degraded, overloaded, damaged, starved, waiting for input, blocked, under maintenance. |
| Transport line | Materials line, energy conduit, data/evidence channel, drone deployment path, stabilization link | Current load, capacity, congestion, interruptions, priority pressure, criticality, risk of overflow or degradation. |
| Performance metric | Factory utilization, reactor load, storage fullness, analysis queue, repair throughput | Percent utilization with reason: resource shortage, power cap, storage full, facility busy, compute deferred, safety lock. |
| Alert marker | Critical damage, line saturation, storage overflow, energy brownout, evidence degradation, quest blocker | Severity, affected systems, root cause, suggested detailed page. |
| Quest dependency highlight | Required module, drone group, evidence chain, stabilization route, verification channel | Ready, blocked, risky, unknown, missing prerequisite, needs survey, insufficient capacity. |

### 5.3 What the Schematic Does Not Do

- It does not provide freeform facility placement.
- It does not require the player to draw transport routes in MVP.
- It does not replace the scheduler, production queue, robotics editor, doctrine editor, research queue, storage screen, or case board.
- It does not become a programming surface for automation logic.
- It does not force the player to micromanage line routing during normal play.
- It does not introduce workers, room interiors, pathfinding, decoration, or base-defense gameplay.

### 5.4 Interaction Model

| Interaction | Allowed in MVP? | Purpose |
|---|---|---|
| Click facility node | Yes | Open inspector for selected system. |
| Double-click or button from inspector | Yes | Navigate to the detailed management page. |
| Click transport line | Yes | Inspect throughput, load, blocked cycles, recent interruptions, and affected queues. |
| Filter overlay | Yes | Switch between energy, materials, data/evidence, drones, and stabilization views. |
| Highlight dependency chain | Yes | Show why a selected quest, process, or facility is blocked. |
| Acknowledge or pin warning | Yes | Manage player attention without hiding unresolved problems. |
| Edit production priorities directly on graph | No for MVP | Use Operations page instead. |
| Draw new lines or move facilities | No | Avoid graph-editor scope and layout complexity. |
| Edit robot doctrines on graph | No | Use Robotics / Doctrine page instead. |

### 5.5 Required Diagnostic Quality

The schematic must show cause, not only state. A weak version says “Factory 70%”. A useful version says “Factory 70% because refined alloy supply is intermittent; root cause: refinery output reserved by Stabilizer upgrade; affected: Breach Drone Refit and Guarded Archive Access.”

> **Design rule**  
> Every serious warning on the SCADA schematic should answer three questions: What is wrong? Why is it happening? Where can the player fix it?

### 5.6 Example Node Inspector

| Field | Example Value |
|---|---|
| Selected system | Fabricator Alpha |
| Status | Active, underperforming |
| Utilization | 70% |
| Primary bottleneck | Refined alloy shortage |
| Input wait time | 31% of recent operational window |
| Power throttling | 0% |
| Output blocked | 4% |
| Current job | Drone Armor Plate x3 |
| Affected operations | Breach Drone Refit; Quest Operation: Guarded Archive Access |
| Suggested page | Operations > Production Priorities |

### 5.7 Visual Language

- Blueprint base style: thin lines, schematic silhouettes, grid or hull-outline background, subdued colors, high contrast alerts.
- Animated pulses show flow direction and approximate throughput during active TimeFlow.
- Line thickness, saturation, pulse rate, or segmented markers communicate load and capacity pressure.
- Nodes show utilization ring or bar, blocked-state icon, severity marker, and current task abbreviation.
- Layer toggles prevent visual spaghetti. The player should never be forced to read every line at once.
- The Case Board should use a different visual language from the Vessel Schematic to avoid confusing investigation nodes with vessel systems.

### 5.8 Production Facilities and Material Flow

> **Design rule**  
> The operational schematic shows only facilities the player can meaningfully schedule, prioritize, automate, or optimize. Passive vessel systems are shown as compact state cards instead of graph nodes.

| Facility | Purpose | Optimization / Automation | Scale |
|---|---|---|---|
| Mission Dock | Receives expedition cargo and stages outbound missions. | Dock queue; launch/recovery priority; storage-block handling. | 1-3 |
| Resource Storage | Single shared buffer for raw, refined and manufactured resources. | Capacity; reservations; allocation; incoming-cargo priority. | 1 global |
| Matter Reactor | Separates/converts recovered Matter Mix into standardized resources. Distinct from the Power Core. | Processing mode; input selection; reactor assignment; queue/priority; yield vs effort. | 1-3 |
| Factory | Builds components, robot frames/modules, equipment and facility upgrades. | Production queues; job assignment; priorities; resource reservation. | 1-4 |
| Emergency Hydrogen Extractor | Passive orbital collection of hydrogen. See §5.9. | None. It is not an automation node and cannot be disabled by player programs. | 1, passive |

The extractor is listed here so the vessel's material sources are readable in one place, and it is the one entry in this table that the player does not schedule. It appears on the schematic as a read-only source; every other passive system stays a state card.

**Resource model.** Expeditions recover **Matter Mix** with a composition profile determined by the site/stratum, rather than many individual ores. Matter Reactors use selectable processing modes to favor particular outputs; processing time, energy use and yield create the optimization tradeoff.

**MISSIONS -> DOCKS -> STORAGE -> MATTER REACTORS -> STORAGE -> FACTORIES -> STORAGE / MISSION LOADOUTS**

Standard reactor outputs:

- **Basic Metals** - frames/structure
- **Rare Metals** - advanced mechanisms/weapons
- **Technical Materials** - electronics, sensors and precision components
- **Chemical Feedstock** - polymers, batteries, coolants and consumables
- **Phase Materials** - dimensional and other high-tier technology

One shared Resource Storage sits between every stage. A factory never draws from a reactor directly except across an authored factory interconnect; docks connect to storage and to nothing else.

### 5.9 Passive Systems and Emergency Recovery

**State-card systems.** Power Core, Stabilization Array, drives, research systems and similar passive/support systems are not shown as optimization nodes. Their current state, capacity and warnings are exposed through bars/text and relevant dedicated screens.

**Emergency Hydrogen Extractor.** One passive orbital extractor continuously gathers hydrogen at a very low rate. Hydrogen can be consumed by a Matter Reactor in an intentionally inefficient emergency-synthesis process to recreate enough low-tier material for a minimal expedition-capable robot. Rare and Phase resources remain expedition-dependent. The extractor is not part of the automation graph and cannot be disabled by player programs.

> **Recovery invariant**  
> From any recoverable game state, operational time + energy + the hydrogen source must provide a path back to basic expedition capability.

## 6. Base Layer Without Base-Building

Dimenship still has a base layer: the dimensional vessel. The updated design removes placement routine and room-construction gameplay while preserving infrastructure progression, logistics, facility upgrades, capacity problems, and operational readiness. The player should feel that the vessel is becoming more capable, but the expression of that growth is through systems and telemetry rather than spatial decoration.

| Classical Base-Builder Feature | Dimenship Replacement | Reason |
|---|---|---|
| Room placement | Facility unlocks and upgrades in authored schematic locations | Avoids building-placement scope while preserving progression. |
| Corridor/path layout | Transport line load and capacity telemetry | Shows logistics pressure without pathfinding or construction editing. |
| Workers walking between buildings | Process queues and transport throughput | Keeps focus on deterministic systems and scheduling. |
| Adjacency bonuses | Facility modules, power budget, queue priority, capacity, and readiness dependencies | Supports planning without forcing spatial optimization. |
| Decorative expansion | New schematic nodes, active overlays, upgraded system states, player unlocks | Provides PC visual feedback with lower asset cost. |
| Base attack | Operational stress events: energy brownout, line saturation, anomaly, sabotage trace, damaged drone recovery | Maintains systems pressure without changing genre. |

## 7. Core Systems Impact

### 7.1 Operations and Scheduler

Production, refit, research, analysis, verification, repair, and mission preparation remain expressed as high-level orders. The scheduler and detailed Operations screen remain the main planning surfaces. The SCADA schematic visualizes the result of those decisions: which facilities are busy, which lines are saturated, which queues are blocked, and which processes are starved by missing inputs, power caps, compute deferral, storage limits, route safety, or quest prerequisites.

### 7.2 Resources, Storage, and Transport

Resource movement should be legible through the schematic. The player should see when Matter Mix, standardized reactor outputs, components, evidence packets, or data outputs cannot reach their destination efficiently. Transport line display is informational in MVP: it reports load, capacity, queue pressure, blocked cycles, and affected systems. Fine-grained routing changes, if any, should occur in a dedicated logistics screen.

### 7.3 Energy and Phase Stability

Energy is the global constraint. The schematic should make energy attribution readable: reactor load, subsystem draw, emergency spikes, starved systems, and stabilization risk. Near-native operations can create temporary high-energy windows that appear as visible stress on the power and stabilization layers.

### 7.4 Robots, Groups, and Loadouts

The schematic should show robot readiness only at a summary level: Robotics Bay queue, repair pressure, Mission Dock availability, deployment route status, and blocked refit reasons. Detailed loadout editing and doctrine management remain in the Robotics screen.

### 7.5 Reports and Logs

Reports remain primary gameplay surfaces. The schematic should link to relevant reports rather than replacing them. Selecting a node or line should expose recent telemetry and provide shortcuts to mission reports, process reports, readiness reports, energy breakdowns, doctrine analytics, or case reports when relevant.

## 8. UX / UI Information Architecture

The PC UI should feel like an operational console. The SCADA vessel schematic becomes the default central overview for many sessions, but it is not the only screen. It works best when paired with a right inspector, bottom timeline/log, top TimeFlow bar, and left navigation rail.

| Area | Function |
|---|---|
| Top Bar | TimeFlow controls, operational timestamp, energy reserve, alert severity, pause state, save indicator. |
| Left Navigation | Dashboard, Vessel Schematic, Operations, Strata, Robotics, Case Board, Reports, Research. |
| Central Work Area | Selected screen. On Vessel Schematic, this shows the SCADA blueprint graph with overlays. |
| Right Inspector | Selected node, line, quest blocker, process, robot group, or case item details. |
| Bottom Timeline / Log | Operational events, active missions, process lanes, alerts, filters, recent bottleneck changes. |
| Context Jump Buttons | Open detailed management page for selected system, such as Energy, Production, Storage, Robotics, or Reports. |

### 8.1 Primary Screens - Updated

| Screen | Purpose |
|---|---|
| Dashboard | Current status, active story objectives, blocked processes, major alerts, readiness warnings, next useful actions. |
| Vessel Schematic | SCADA-like blueprint overview of facility state, line load, utilization, bottlenecks, alerts, and quest dependencies. |
| Operations | Production chains, refits, repair, research jobs, verification queues, process scheduler, priorities, dependencies. |
| Strata | Strata map/list, target details, hazards, energy cost, information reliability, mission deployment. |
| Robotics | Robot inventory, groups, loadouts, refits, modules, doctrine editor, templates. |
| Case Board | Investigation graph, quest nodes, leads, contradictions, witnesses, verification questions, reconstruction packet. |
| Reports | Mission reports, process reports, logs, TimeFlow summaries, AI estimates, charts. |
| Research | Schematics, technology requirements, equipment unlocks, research queue, story relevance. |

### 8.2 Screen Separation Rule

> **SCADA rule**  
> The Vessel Schematic answers “What is happening and why?” Dedicated management pages answer “What do I change?” This separation keeps the overview readable and prevents the schematic from becoming an overloaded planning editor.

## 9. Quest and Readiness Integration

Quests remain storyline objects. Technical, firepower, equipment, energy, stabilization, compute, or logistics requirements exist to enable story operations, not to become generic optimization targets. The SCADA schematic improves quest readability by showing where a story operation is blocked inside the vessel.

| Quest Example | SCADA Highlight | Detailed Fix Page |
|---|---|---|
| Recover Witness Trace | Stabilization Array insufficient; Evidence Channel at 95% load; Forensic Analyzer ready. | Energy / Stabilization, Operations, Storage |
| Guarded Archive Access | Breach Drone Refit delayed by Fabricator underperformance; Robotics Bay waiting for armor plates. | Operations, Robotics |
| Verify Native Contradiction | Case Graph AI ready; Archive Decoder blocked by compute reservation; Evidence Vault near capacity. | Research / Analysis, Storage |
| Recover Phase-Safe Object | Mission Dock ready; Hauler group available; phase-safe containment route missing. | Robotics, Storage, Operations |
| Final Reconstruction Packet | Witness chain complete; two verification questions unresolved; analysis queue power-throttled. | Case Board, Reports, Energy |

### 9.1 Readiness Display Pattern

When a quest is selected, the schematic should be able to highlight all vessel systems that contribute to its readiness. This includes facilities, transport lines, evidence channels, drone preparation, storage, energy, stabilization, compute, and analysis queues.

> **Example readiness message**  
> Quest Operation: Recover Witness Trace. Readiness: Blocked. Blocking systems: Stabilization Array output insufficient; Evidence Transport Line at 95% load; Forensic Analyzer ready; Investigator Drone Group ready. Suggested next pages: Energy / Stabilization and Operations.

## 10. MVP Scope Update

The MVP should validate that operational time, autonomous missions, process scheduling, reports, quest readiness, and the SCADA schematic work together. The schematic should be fixed-layout and diagnostic-only in the first playable.

| MVP Feature | Scope |
|---|---|
| Operational Time v1 | 0x pause, 1x run, 2x fast run, auto-pause for critical alerts. 4x can be deferred if needed. |
| SCADA Vessel Schematic v1 | Fixed blueprint layout. 7-9 nodes. Energy/material/data overlays. Animated line load. Node utilization. Click-to-inspect. Click-through to detailed pages. Quest dependency highlight. |
| Facility nodes v1 | Resource Storage, Matter Reactors, Factories, Mission Docks, and the Emergency Hydrogen Extractor as a read-only source. Power Core, Stabilization Array, Robotics Bay, Research / Analysis Lab, Forensic Analyzer and Case Graph AI are state cards, not nodes (§5.8). |
| Line types v1 | Energy, materials, data/evidence. Drone deployment and stabilization links can be visualized but simplified. |
| Mission types | Mining, Scavenging, Investigation. |
| Quest operations | One operation requiring a specific investigation module and producing the first contradiction lead. One capability-gated story operation requiring improved sensors, stabilization, or basic drone defense. |
| Production chain | Matter Mix -> standardized resources -> components -> robot frames/modules. |
| Robots and modules | Two robot frames plus tool, sensor, storage, power/defense, basic investigation module, basic weapon/armor package. |

### 10.1 Explicit MVP Exclusions

- No room placement.
- No freeform facility graph editing.
- No player-drawn logistics lines.
- No worker pathfinding or animated crew simulation.
- No interior base art requirement beyond schematic presentation.
- No schematic-based doctrine editor.
- No direct production queue editing inside the schematic, except possible shortcut buttons to dedicated screens.
- No base-defense mode.

## 11. Technical Foundation - C# .NET 10

The simulation core should remain a pure C# .NET 10 library independent from the presentation layer. The SCADA schematic should read deterministic simulation state and telemetry rather than becoming a separate simulation. UI state may be engine-specific, but vessel state, process state, facility metrics, line metrics, quest readiness, and telemetry should come from the core model.

| Concept | Responsibility |
|---|---|
| VesselGraphDefinition | Static content definition of facility nodes, visual grouping, line definitions, overlay categories, and navigation targets. |
| FacilityState | Runtime state for each node: active job, utilization, power draw, blocked reason, damage/degradation, queue pressure. |
| TransportLineState | Runtime state for each line: current load, capacity, saturation, blocked cycles, carried category, affected systems. |
| OperationalClock | Tracks simulation time and TimeFlow state. |
| ProcessQueue | Holds production, refit, repair, research, verification, analysis, and logistics jobs. |
| QuestNode | Storyline node with case, capability, equipment, access, and operation prerequisites. |
| ReadinessEvaluator | Deterministic evaluator that maps current state to quest readiness, blockers, risk reasons, and highlight targets. |
| TelemetryEvent | Atomic explanation of what happened and why. |
| Report | Player-facing summary generated from telemetry, including schematic inspector excerpts. |
| SchematicViewModel | Presentation-layer projection of simulation state for the UI: node badges, line load, alerts, overlays, selected item inspector data. |

### 11.1 Data Flow

1. Simulation core advances operational time deterministically.
2. Processes consume resources, power, compute, storage, facility time, and transport capacity.
3. Subsystems emit `TelemetryEvents` with causes and affected entities.
4. Reports aggregate telemetry into player-facing summaries.
5. `ReadinessEvaluator` checks selected quests and story operations against current state.
6. `SchematicViewModel` exposes visual state to the UI without owning simulation authority.
7. The UI renders facility nodes, line load, alerts, dependency highlights, and navigation targets.

### 11.2 Testing Requirements

- Facility utilization must be deterministic for identical process queues and resource states.
- Line saturation must be derived from simulation state and reproducible after save/load.
- Blocked reasons must be stable, explainable, and prioritized consistently.
- Quest readiness highlights must match actual readiness evaluation.
- Fast-forward event grouping must not hide critical schematic alerts.
- Save files must preserve active process queues, facility state, line state, mission state, case graph state, alert state, random seeds if any, and operational timestamp.

## 12. Balancing and Telemetry Principles

- Operational time should create pacing pressure inside the simulation, not pressure on the player in real life.
- The schematic should help the player supervise complexity without turning into an alarm flood.
- A good system should earn longer stable runs at higher TimeFlow by producing fewer interruptions, fewer bottlenecks, and lower Attention Load.
- A bad system at high TimeFlow should fail faster, waste energy faster, and generate clearer telemetry, not simply become optimal through speed.
- Facility utilization percentages must always be paired with cause categories. A percent without explanation is not enough.
- Line capacity must be readable in operational terms: what is delayed, what is blocked, and which story or production objective is affected.
- Quest blockers should appear early enough for planning. The player should not discover a missing vessel dependency only after a long operation fails.
- Avoid click-speed actions. The player should pause, inspect, reprioritize, refit, research, redeploy, or change doctrine.

## 13. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| The SCADA graph becomes decorative only. | Players ignore it after novelty fades. | Require root-cause explanations, quest dependency highlights, click-through navigation, and useful inspectors. |
| The graph becomes overloaded. | Unreadable spaghetti undermines control-room clarity. | Use overlay filters, authored layout, grouping, severity tiers, and context-dependent highlights. |
| Players expect full base-building. | Steam store positioning and screenshots may mislead. | Avoid marketing language like base-builder. Use vessel operations, schematic, control room, automation strategy. |
| Two graphs confuse players: Vessel Schematic and Case Board. | Players mix system nodes with investigation nodes. | Use distinct visual language, labels, icon families, and screen purposes. |
| Informational graph feels non-interactive. | PC users may see it as passive. | Support selection, inspector, dependency tracing, pinned alerts, filters, timeline correlation, and navigation. |
| Root-cause analysis is hard to implement. | Warnings become vague or misleading. | Start with explicit blocked-reason categories from scheduler and simple deterministic priority rules. |
| Performance metrics create false precision. | Players may distrust estimates if numbers are opaque. | Explain calculation windows and reasons: recent 10 operational minutes, input wait, power cap, output block. |
| MVP scope creeps into graph editor. | UI and simulation complexity expand quickly. | Explicitly exclude line drawing, facility movement, and direct queue editing from MVP. |

## 14. Open Questions

- Should the Vessel Schematic be the default landing screen after the dashboard, or should the dashboard remain primary?
- How many overlay layers are required for MVP: energy/material/data only, or also drones and stabilization?
- Should line capacity be a real simulation constraint in MVP, or initially a derived diagnostic metric from process delays?
- How precise should utilization metrics be: exact percentages, qualitative bands, or both?
- Should the player be able to set alert thresholds from the schematic inspector, or only from a dedicated Alerts settings page?
- Should quest readiness highlighting originate from the Case Board and project onto the schematic, or from the schematic selection and project back to quests?
- How much animation is needed for PC feel without reducing readability?
- Should damaged or degraded facilities be represented mechanically in MVP, or only later?
- Should the static schematic layout be hand-authored per campaign stage, or generated from data definitions?
- What is the minimum visual fidelity required for Steam screenshots to communicate control-room fantasy without promising a base-builder?

## 15. Revision Notes

- v0.9: Added the production layer: the four schedulable facility kinds on the schematic, and the rule that passive systems are state cards rather than nodes.
- v0.9: Replaced per-ore raw materials with **Matter Mix** and the five standardized reactor outputs.
- v0.9: Named the **Matter Reactor** as the separating tier and stated that it is not the Power Core. It supersedes the refineries of Appendix 1.
- v0.9: Added the **Emergency Hydrogen Extractor**, the emergency-synthesis path, and the recovery invariant.
- v0.9: Updated the facility-node lists in §5.2 and §10 and the production chain in the MVP table to match.
- v0.8: Added SCADA-like Vessel Schematic as a core PC presentation and diagnostic layer.
- v0.8: Explicitly rejected classical base-building as an MVP/core direction.
- v0.8: Clarified that the schematic is informational, diagnostic, and navigational, not the primary planning control.
- v0.8: Added node/line status, utilization, line capacity, bottleneck explanation, root-cause display, and quest-readiness highlighting.
- v0.8: Updated UX information architecture to include Vessel Schematic as a primary screen.
- v0.8: Updated MVP scope and exclusions to prevent graph-editor and room-placement scope creep.
- v0.8: Added technical concepts for VesselGraphDefinition, FacilityState, TransportLineState, ReadinessEvaluator, and SchematicViewModel.
- v0.8: Preserved operational time, storyline-driven quests, deterministic systems, failure-as-telemetry, autonomous missions, and C# .NET 10 simulation-core direction from v0.7.

## Appendix A - Glossary Updates

| Term | Meaning | Used For |
|---|---|---|
| Vessel SCADA Schematic | Blueprint-like operational display inspired by SCADA systems. Shows vessel state, line load, utilization, bottlenecks, alerts, and quest dependencies. | PC control-room overview. |
| Facility Node | A schematic node representing a vessel system such as reactor, fabricator, storage, robotics bay, forensic analyzer, or stabilization array. | Status display and navigation target. |
| Transport Line | A schematic edge representing flow of energy, materials, data/evidence, drones, or stabilization support. | Throughput and capacity visualization. |
| Utilization | Recent operational percentage showing how much of a facility or line capacity was actually used. | Performance diagnosis. |
| Blocked Reason | Explicit reason a process or system did not advance: missing input, facility busy, power cap, compute deferred, storage full, safety lock, route unsafe, or prerequisite missing. | Player feedback and root-cause analysis. |
| Root Cause | Highest-priority explanation for an observed underperformance or blocked state. | SCADA inspector and reports. |
| Dependency Highlight | Temporary visual trace showing which vessel systems affect a selected quest, process, facility, or alert. | Readiness diagnosis. |
| Navigation Hub | A UI surface that lets the player jump from a summary problem to the screen where it can be fixed. | Schematic interaction model. |
| Attention Load | Report metric describing how much supervision a system requires. High load means more interruptions, alerts, manual intervention, and unresolved bottlenecks. | TimeFlow and automation quality feedback. |
| Matter Mix | Bulk material recovered by expeditions, carrying a composition profile determined by the site or stratum. | The single raw input to the production chain. |
| Matter Reactor | Facility that separates Matter Mix into standardized resources under a selectable processing mode. Not the Power Core. | Production node and optimization decision. |
| Processing Mode | The selected conversion a Matter Reactor runs, trading processing time, energy and yield against which output is favored. | Reactor optimization. |
| Mission Dock | Facility that receives expedition cargo and stages outbound missions. Connected to Resource Storage only. | Production node; mission staging. |
| State-Card System | A passive or support system shown as a compact status card rather than a schematic node: Power Core, Stabilization Array, drives, research systems. | Keeps the schematic to what can be optimized. |
| Emergency Hydrogen Extractor | Passive orbital collector gathering hydrogen at a very low rate, outside the automation graph. | The recovery floor. |
| Emergency Synthesis | Intentionally inefficient reactor process converting hydrogen into low-tier material. | Path back to expedition capability. |

> **Final design recommendation**  
> Build the SCADA vessel schematic as a diagnostic and navigational overview first. Let it reveal the health of the system, not become the system editor. The planning depth should remain in the scheduler, doctrine editor, production priorities, robotics screen, research queue, storage management, and Case Board.

## Appendix 1

> **Superseded in part by §5.8.** The list below is the original post-document review. Its Resource Storage, factory array, dock and fixed-layout decisions all stand. Its *Array of Refineries* is now the **Matter Reactor** array, and its *Power Core* is a state card rather than a node — the Power Core and the Matter Reactor are different systems.

**Post-document review:**  
I think this graph should look a bit different. Maybe we should have a fixed layout (revealing lines and facilities as they are built), only facilities involved in production should be on the graph. I have in mind the following list: Resource Storage (1, global, makes sense to put in center), Interconnected array of factories (I believe 3 or 4 should be the limit), Array of Refineries (2-3, connected), Array of Mission Docks (only connected to Storage), Power Core (probably will need refined materials as a fuel). We will assume simplified power delivery ignoring lines (assuming enough capacity and instant delivery).

## Appendix 2

