
# Dimenship

# Programming and Automation Design

**v0.1 – Programs, Rule Cards, Mission Doctrines, and Vessel Controllers**

| Field                    | Decision                                                                                                                                                                        |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Document purpose**     | Define Dimenship programming as a gameplay system: why it exists, how players use it, what it improves, and what design decisions remain.                                       |
| **Current direction**    | Preset programs are the baseline. Programs can be inspected and modified as rule cards. Advanced players can author programs from scratch. Found programs are optional rewards. |
| **Rejected direction**   | No FBD editor. The SCADA vessel schematic remains diagnostic/navigation focused, not the programming interface.                                                                 |
| **Future direction**     | A Python-like expert editor may be added later, but it is not MVP-critical and requires serious tooling: validation, autocomplete, debugging, documentation, and sandboxing.    |
| **Important constraint** | There is no required conversion from advanced scripting back to rule cards. Advanced programming is its own expert path.                                                        |

> **Core statement**
>
> Dimenship programs are reusable automation objects. They let the player encode intent into bots and vessel systems, then watch those systems execute during operational time.
>
> Programming is not required to start playing, but it is one of the main ways to optimize, personalize, and master the game.

---

# 1. Design Intent

Programming should reinforce Dimenship as an engineering control-room strategy game. The player is not clicking faster, piloting drones directly, or manually solving every process interruption. The player defines behavior, observes outcomes, reads telemetry, and improves automation.

## 1.1 Why programming belongs in Dimenship

* It turns automation from a passive background system into an active source of mastery.
* It gives players a way to express strategy through reusable behavior rather than repeated manual orders.
* It makes failure informative: a bad program creates readable logs, skipped rules, conflicts, and bottlenecks.
* It supports PC session depth without adding action combat or full base-building.
* It creates a new reward category: found programs, corrupted programs, banned AI fragments, and industrial controller templates.

---

## 1.2 What programming should feel like

| Feeling                  | Meaning in play                                                                                                          |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| **Readable**             | A player can understand what a program tried to do and why it failed.                                                    |
| **Optional at first**    | Basic presets are enough for early progress. Custom logic becomes valuable as systems grow.                              |
| **Powerful but bounded** | Programs can improve efficiency and survivability, but cannot solve the mystery automatically or bypass all constraints. |
| **Debuggable**           | Every important program decision can appear in reports: rule fired, skipped, blocked, conflicted, or overridden.         |
| **Rewarding**            | A well-designed program should produce visible benefits on the SCADA display, mission reports, and TimeFlow stability.   |

---

## 1.3 Design boundary

Programming is **not** a puzzle minigame detached from the rest of Dimenship.

It should always connect to one of three practical goals:

1. Make autonomous missions safer, smarter, or more profitable.
2. Make vessel production more stable, efficient, or responsive to shortages.
3. Reduce attention load so the player can run higher TimeFlow with fewer interruptions.

---

# 2. Final Direction and Interface Layers

The system uses multiple layers of accessibility. The same gameplay concept can be approached casually through presets or deeply through custom rules.

The advanced editor is **not required** for non-coding players.

| Layer                         | Player experience                                                                           | Role                                                                                  |
| ----------------------------- | ------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| **Preset program**            | Select a named program and adjust a few parameters.                                         | Default baseline for all players and the primary MVP interface.                       |
| **Rule-card view**            | Inspect and modify the program as readable **WHEN / IF / THEN** rules.                      | Main readable/editable representation for programs.                                   |
| **Program-from-scratch**      | Create new rule-card programs without needing to find a template.                           | Advanced play available whenever the player wants deeper control.                     |
| **Found programs**            | Recover ready-to-use programs from missions, archives, hostile sites, or abandoned systems. | Optional reward path, especially helpful for players who do not like authoring logic. |
| **Python-like expert editor** | Future optional text-based editor with validation and debugging tools.                      | Post-MVP expert feature. No requirement to convert text scripts back to rule cards.   |

---

## 2.1 One-way simplicity rule

Simpler interfaces may compile downward into the execution model.

More advanced interfaces do **not** need to convert back upward into simpler interfaces.

```text
Preset program
        ↓
Rule-card representation
        ↓
Deterministic execution model
```

## 2.2 Recommended internal representation

The implementation should not depend on raw Python as the simulation foundation. A safer model is a deterministic program AST or bytecode-like command list that can be generated by presets, rule cards, and future scripting.

Program
: Deterministic command object

**Fields**

- **Scope:** Bot / Group / Facility / Reactor / Mission Dock / Storage / AI Analysis
- **Trigger:** Tick / Resource Changed / Mission Event / Alert / Queue Changed
- **Conditions:** Structured predicates
- **Actions:** Bounded commands
- **Priority:** Numeric or tiered
- **Cooldown:** Operational time interval
- **Stop Condition:** Optional
- **Telemetry:** What to report when fired, skipped, blocked, or conflicted

---

# 3. Programs as Gameplay Objects

Programs should exist as first-class game objects. They can be installed, copied, upgraded, edited, compared, recovered, corrupted, repaired, or used as quest rewards.

## 3.1 Program sources

| Source | Example | Design value |
|---------|---------|--------------|
| Starting library | Basic Miner, Basic Repair Bot, Balanced Refinery, Basic Reactor Fuel Conversion | Lets every player begin without programming knowledge. |
| Scavenged industrial sites | Adaptive Refinery Balancer, Factory Queue Stabilizer | Makes production optimization a mission reward. |
| Abandoned mission logs | Evidence-Safe Retreat, Hazard-Aware Survey | Connects exploration failure/success to better future mission logic. |
| Forbidden AI fragments | Aggressive Breach Optimizer, Contradiction Cluster | Creates powerful but risky or high-complexity automation rewards. |
| Story quests | Witness Trace Preservation, Native Verification Prep | Ties programming to narrative progression without making quests generic throughput goals. |
| Player-authored | Custom controller or doctrine | Lets advanced players surpass found programs through skill. |

## 3.2 Program properties

| Property | Possible values | Gameplay effect |
|----------|-----------------|-----------------|
| Scope | Bot, group, facility, facility array, reactor, storage, mission dock, analysis system | Defines where the program can be installed. |
| Complexity | 1–10 or Basic / Industrial / Expert | Controls how many rules, conditions, and actions are allowed. |
| Editability | Parameter-only, rule-card editable, locked, corrupted, expert-only | Controls how much the player can modify. |
| Reliability | Verified, experimental, corrupted, unstable | Determines warnings, risk, or need for testing. |
| Compute cost | None, low, medium, high | Gives stronger automation an operational cost. |
| Telemetry quality | Minimal, normal, verbose | Affects how clearly reports explain behavior. |
| Origin | Starting, scavenged, forbidden, AI-derived, player-written | Supports flavor, rarity, and balance. |

## 3.3 Found programs should be optional, not mandatory

Found programs should save time, teach patterns, and offer interesting automation styles.

They should **not** be the only way to progress.

A player who enjoys programming should be able to build an equivalent or better program manually.

A player who dislikes programming should be able to rely on discovered and preset programs with parameter tuning.

---

# 4. What Programming Helps With in Missions

Mission programming should focus on autonomous field behavior. The player cannot directly pilot drones during missions, so doctrine quality becomes one of the main preparation tools.

## 4.1 Mission programming targets

| Target | What can be programmed | Benefit |
|--------|------------------------|---------|
| Individual bot | Repair logic, retreat threshold, scan priority, resource selection, emergency behavior | Makes each robot role more reliable. |
| Robot group | Formation priorities, shared retreat rules, evidence handling, hauler coordination | Improves mission survival and output quality. |
| Mission dock | Launch conditions, repeat safe routes, pause if storage high, reserve drones for story operation | Prevents bad launches and supports long stable TimeFlow runs. |
| Investigation package | When to scan, sample, preserve, retreat, or request verification | Improves evidence quality and reduces sample loss. |
| Combat-as-hazard response | When to suppress, avoid, retreat, shield, or continue objective | Turns combat into a preparation and automation test rather than reflex gameplay. |

## 4.2 Mission examples

### Example A: Evidence-Safe Investigator

**Program:** Evidence-Safe Investigator

**Scope:** Investigator Drone Group

#### Rule 1

```text
WHEN evidence_sample_secured == true
AND signal_integrity < 45%

THEN return_to_dock

Priority: Critical
```

#### Rule 2

```text
WHEN hostile_detected == true
AND evidence_sample_secured == false

THEN retreat_to_safe_distance
AND continue passive scan

Priority: High
```

#### Rule 3

```text
WHEN storage_capacity > 80%

THEN prioritize case fragments
over industrial salvage

Priority: Medium
```

**Benefit**

The player loses fewer quest-critical samples. Missions may return earlier with less raw loot, but evidence confidence and survival improve.

This creates a real design decision:

> Greedy exploration versus safe extraction.## 2.2 Recommended internal representation

The implementation should not depend on raw Python as the simulation foundation. A safer model is a deterministic program AST or bytecode-like command list that can be generated by presets, rule cards, and future scripting.

Program
: Deterministic command object

**Fields**

- **Scope:** Bot / Group / Facility / Reactor / Mission Dock / Storage / AI Analysis
- **Trigger:** Tick / Resource Changed / Mission Event / Alert / Queue Changed
- **Conditions:** Structured predicates
- **Actions:** Bounded commands
- **Priority:** Numeric or tiered
- **Cooldown:** Operational time interval
- **Stop Condition:** Optional
- **Telemetry:** What to report when fired, skipped, blocked, or conflicted

---

# 3. Programs as Gameplay Objects

Programs should exist as first-class game objects. They can be installed, copied, upgraded, edited, compared, recovered, corrupted, repaired, or used as quest rewards.

## 3.1 Program sources

| Source | Example | Design value |
|---------|---------|--------------|
| Starting library | Basic Miner, Basic Repair Bot, Balanced Refinery, Basic Reactor Fuel Conversion | Lets every player begin without programming knowledge. |
| Scavenged industrial sites | Adaptive Refinery Balancer, Factory Queue Stabilizer | Makes production optimization a mission reward. |
| Abandoned mission logs | Evidence-Safe Retreat, Hazard-Aware Survey | Connects exploration failure/success to better future mission logic. |
| Forbidden AI fragments | Aggressive Breach Optimizer, Contradiction Cluster | Creates powerful but risky or high-complexity automation rewards. |
| Story quests | Witness Trace Preservation, Native Verification Prep | Ties programming to narrative progression without making quests generic throughput goals. |
| Player-authored | Custom controller or doctrine | Lets advanced players surpass found programs through skill. |

## 3.2 Program properties

| Property | Possible values | Gameplay effect |
|----------|-----------------|-----------------|
| Scope | Bot, group, facility, facility array, reactor, storage, mission dock, analysis system | Defines where the program can be installed. |
| Complexity | 1–10 or Basic / Industrial / Expert | Controls how many rules, conditions, and actions are allowed. |
| Editability | Parameter-only, rule-card editable, locked, corrupted, expert-only | Controls how much the player can modify. |
| Reliability | Verified, experimental, corrupted, unstable | Determines warnings, risk, or need for testing. |
| Compute cost | None, low, medium, high | Gives stronger automation an operational cost. |
| Telemetry quality | Minimal, normal, verbose | Affects how clearly reports explain behavior. |
| Origin | Starting, scavenged, forbidden, AI-derived, player-written | Supports flavor, rarity, and balance. |

## 3.3 Found programs should be optional, not mandatory

Found programs should save time, teach patterns, and offer interesting automation styles.

They should **not** be the only way to progress.

A player who enjoys programming should be able to build an equivalent or better program manually.

A player who dislikes programming should be able to rely on discovered and preset programs with parameter tuning.

---

# 4. What Programming Helps With in Missions

Mission programming should focus on autonomous field behavior. The player cannot directly pilot drones during missions, so doctrine quality becomes one of the main preparation tools.

## 4.1 Mission programming targets

| Target | What can be programmed | Benefit |
|--------|------------------------|---------|
| Individual bot | Repair logic, retreat threshold, scan priority, resource selection, emergency behavior | Makes each robot role more reliable. |
| Robot group | Formation priorities, shared retreat rules, evidence handling, hauler coordination | Improves mission survival and output quality. |
| Mission dock | Launch conditions, repeat safe routes, pause if storage high, reserve drones for story operation | Prevents bad launches and supports long stable TimeFlow runs. |
| Investigation package | When to scan, sample, preserve, retreat, or request verification | Improves evidence quality and reduces sample loss. |
| Combat-as-hazard response | When to suppress, avoid, retreat, shield, or continue objective | Turns combat into a preparation and automation test rather than reflex gameplay. |

## 4.2 Mission examples

### Example A: Evidence-Safe Investigator

**Program:** Evidence-Safe Investigator

**Scope:** Investigator Drone Group

#### Rule 1

```text
WHEN evidence_sample_secured == true
AND signal_integrity < 45%

THEN return_to_dock

Priority: Critical
```

#### Rule 2

```text
WHEN hostile_detected == true
AND evidence_sample_secured == false

THEN retreat_to_safe_distance
AND continue passive scan

Priority: High
```

#### Rule 3

```text
WHEN storage_capacity > 80%

THEN prioritize case fragments
over industrial salvage

Priority: Medium
```

**Benefit**

The player loses fewer quest-critical samples. Missions may return earlier with less raw loot, but evidence confidence and survival improve.

This creates a real design decision:

> Greedy exploration versus safe extraction.

## 4.3 Fun mission motivations

- **Risk tuning:** Build aggressive, cautious, or evidence-safe doctrine variants and compare outcomes.
- **Specialized identities:** Players can create named doctrine families such as *Silent Witness*, *Greedy Miner*, *Archive Thief*, or *Cowardly Genius*.
- **Telemetry payoff:** A clever rule should visibly reduce damage, improve confidence, or prevent a failed mission.
- **Program archaeology:** Scavenged programs reveal how another stratum solved similar operational problems.
- **Corrupted rewards:** A powerful found program may contain one dangerous rule the player must identify and fix.
- **Story flavor:** Forbidden AI programs can be effective but ethically or operationally questionable.

---

# 5. What Programming Helps With in Base / Vessel Control

Base programming should focus on production stability and bottleneck response. The SCADA-like schematic shows what is happening; programs decide how selected systems react to defined conditions.

## 5.1 Vessel control targets

| System | Programmable behavior | Benefit |
|--------|------------------------|---------|
| Factories | Queue priority, recipe switching, pause low-priority jobs, reserve output for quest operations | Reduces idle time and keeps story-critical production moving. |
| Refineries | Balance refined materials, respond to shortages, feed reactor fuel chain, preserve reserves | Prevents factory starvation and reactor fuel collapse. |
| Resource storage | Reserve thresholds, overflow reactions, mission launch limits, input/output priority | Prevents wasted mission returns and protects critical resources. |
| Power core / reactor | Fuel conversion, emergency mode, stabilization priority, consumption limits | Keeps energy stable without modeling power lines. |
| Mission docks | Auto-repeat low-risk missions, pause launches if storage is full, reserve docks for story operations | Reduces manual management during operational time. |
| Analysis systems | Prioritize contradiction clustering, witness trace processing, or verification prep | Supports narrative progress while competing with industrial compute needs. |

---

## 5.2 Base control examples

### Example D: Refinery Shortage Recovery

**Program:** Refinery Shortage Recovery

**Scope:** Refinery Array

#### Rule 1

```text
WHEN Storage.RefinedAlloy < 300

THEN set RefineryAlpha.recipe = RefinedAlloy
AND set RefineryAlpha.priority = High

Priority: High
```

#### Rule 2

```text
WHEN FactoryBeta.blocked_reason == MissingRefinedAlloy

THEN reserve RefinedAlloy
for FactoryBeta
until queue_unblocked

Priority: High
```

#### Rule 3

```text
WHEN Storage.RefinedAlloy > 800

THEN restore balanced_refining

Priority: Medium
```

**Benefit**

Factories spend less time idle because the refinery array reacts before the player manually notices the shortage.

---

### Example E: Reactor Fuel Balancer

**Program:** Reactor Fuel Balancer

**Scope:** Power Core

#### Rule 1

```text
WHEN ReactorFuel < 200

THEN convert
most_abundant_refined_material
to ReactorFuel

UNLESS that material is below reserve_threshold

Priority: Critical
```

#### Rule 2

```text
WHEN StabilizationOperation.active == true

THEN preserve ReactorFuel reserve >= 500

Priority: Critical
```

#### Rule 3

```text
WHEN Storage.TotalUsage > 90%

THEN prefer fuel conversion
from overflow_materials

Priority: Medium
```

**Benefit**

The simplified power model stays strategically interesting even without power lines. The player still manages fuel, reserves, and tradeoffs between industry and stabilization.

---

### Example F: Factory Quest Priority

**Program:** Factory Quest Priority

**Scope:** Factory Array

#### Rule 1

```text
WHEN QuestOperation.requires_item is missing

THEN prioritize production
of required_item

Priority: High
```

#### Rule 2

```text
WHEN required_item.inputs are missing

THEN request refinery_support_program

Priority: Medium
```

#### Rule 3

```text
WHEN story_item_completed == true

THEN restore previous queue

Priority: Medium
```

**Benefit**

The system connects storyline readiness to production without turning quests into generic throughput targets. The program helps the player prepare for a narrative operation.

---

### Example G: Storage Overflow Governor

**Program:** Storage Overflow Governor

**Scope:** Resource Storage + Mission Docks

#### Rule 1

```text
WHEN Storage.TotalUsage > 92%

THEN pause low_priority_mining_launches

Priority: High
```

#### Rule 2

```text
WHEN incoming_mission_cargo > free_storage_space

THEN reserve storage
for rare_components
and case_items

Priority: Critical
```

#### Rule 3

```text
WHEN Storage.TotalUsage < 80%

THEN resume paused_missions

Priority: Medium
```

**Benefit**

This makes the central storage node in the fixed production schematic meaningful. Storage is not just a number; it becomes an automation trigger hub.

---

## 5.3 Fun base-control motivations

- Visible payoff on the SCADA screen: lines become less saturated, factories run closer to full efficiency, and alerts decrease.
- **Attention load reduction:** Good controllers let the player run 2× or 4× operational time longer without interruptions.
- **Automation personality:** Players can build conservative, aggressive, quest-first, resource-first, or research-first vessel behavior.
- **Optimization experiments:** Compare reports before and after installing a controller.
- **Recovery from chaos:** After a bad mission, smart programs help the vessel stabilize without requiring dozens of manual clicks.
- **Soft failure stories:** A bad controller may drain reactor fuel, starve factories, or launch missions into full storage, producing memorable debugging moments.

# 6. Player Decisions Created by Programming

Programming should create strategic decisions rather than merely automate chores. The best choices involve tradeoffs.

## 6.1 Decision matrix

| Decision type | Question for the player | Example |
|---------------|-------------------------|---------|
| Safety versus yield | Should drones retreat early or push deeper for more resources? | Evidence-Safe Investigator returns with lower loot but stronger case data. |
| Quest priority versus economy | Should production focus on a story item or maintain industrial balance? | Factory Quest Priority delays drone upgrades to finish a phase-safe container. |
| Energy reserve versus throughput | Should reactor fuel be preserved or spent to keep production fast? | Fuel Balancer reduces factory output during stabilization windows. |
| Local versus global optimization | Should each facility optimize itself, or should a controller coordinate the entire array? | Local refinery logic may conflict with global quest readiness. |
| Manual control versus automation | Should the player tune every queue or trust a controller? | A trusted controller lowers attention load but can hide mistakes until reports are reviewed. |
| Found program versus custom program | Use a recovered template or write a cleaner custom version? | A banned AI-derived program is powerful but ignores retreat thresholds. |

---

## 6.2 Conflict as gameplay

Programs should be able to conflict, but conflicts must be readable.

A conflict should **not** feel like a bug.

It should generate a useful report.

### Example conflict report

```text
Conflict Report

Factory Quest Priority requested:
    Produce Phase-Safe Container

Refinery Shortage Recovery requested:
    Produce Refined Alloy

Reactor Fuel Balancer requested:
    Preserve Refined Isogen for fuel

Resolution:
    Reactor reserve rule won due to Critical priority

Result:
    Quest item delayed by 00:18:40 operational time
```

This turns complex automation into understandable systems gameplay.

---

# 7. Progression and Unlock Structure

Programming depth should scale with campaign progression.

Early automation is simple and safe.

Later automation becomes more powerful, broader in scope, and more risky.

## 7.1 Programming progression

| Stage | Available programming | Purpose |
|-------|------------------------|---------|
| Early game | Basic preset programs with parameter tuning | Let players survive, mine, refine, repair, and complete first investigation operations. |
| Early-mid game | Rule-card inspection and limited edits | Teach how programs work without requiring programming from scratch. |
| Mid game | Player-authored rule-card programs, scavenged industrial programs | Create real optimization and personalization. |
| Late game | Global controllers, multi-facility programs, forbidden AI-derived programs | Support complex operational-time runs and high-risk story operations. |
| Post-MVP | Python-like expert editor with validation, autocomplete, and debugging | Serve power users without forcing code on the main audience. |

---

## 7.2 Program complexity gates

- Controller slots: each facility or bot group has limited installed programs.
- Rule count: early controllers support few rules; advanced controllers support more.
- Condition depth: early rules use one condition; later rules allow AND/OR groups.
- Scope: local programs unlock before array-wide or vessel-wide controllers.
- Compute cost: advanced logic may consume analysis/AI capacity.
- Certification: found corrupted programs may need analysis before safe installation.

---

## 7.3 Program rewards

| Reward type | Example | Why it is useful |
|-------------|---------|------------------|
| New preset | Balanced Refinery II | Non-coders get a better tool immediately. |
| Rule-card template | Storage Overflow Governor | Coders can inspect and improve it. |
| Rare conditional block | Predict shortage from trend | Expands the programming vocabulary. |
| New action | Reserve output for quest operation | Adds new control possibilities. |
| Debugging module | Verbose Doctrine Trace | Improves reports and reduces frustration. |
| Forbidden program | Aggressive Breach Optimizer | Tempting high-risk tool with story flavor. |

# 8. Debugging, Reports, and Feedback

Programming is only fun if players understand why their programs work or fail.

Debugging should be integrated into reports rather than hidden behind a developer-style console in MVP.

## 8.1 Required report outputs

- Rule fired: which rule executed and what condition caused it.
- Rule skipped: why a rule did not execute, such as cooldown, missing resource, invalid target, or lower priority.
- Rule blocked: action was requested but could not be performed.
- Conflict resolved: which competing rule won and why.
- Impact summary: estimated resource saved, delay reduced, damage prevented, or confidence preserved.
- Trend comparison: before/after metrics for factory utilization, line saturation, mission survival, and attention load.

---

## 8.2 Example report

```text
Program Report:
    Refinery Shortage Recovery

Operational Window:
    07d 13:00:00 – 07d 15:00:00

Rules fired:      12
Rules skipped:     4
Conflicts:         1

Primary benefit:
    Factory Beta idle time reduced
    from 31% to 9%

Secondary effect:
    Reactor fuel reserve dropped by 6%

Attention Load:
    -2 alerts per operational hour

Recommended review:
    Reactor Fuel Balancer is competing
    for Refined Isogen.
```

---

## 8.3 Future expert debugging

If a Python-like editor is added later, it should not arrive alone.

It needs supporting systems:

- Autocomplete / IntelliSense for available resources, facilities, bot groups, events, and actions.
- Static validation before installation.
- Simulation preview using a recorded operational window.
- Step-through or trace mode for selected events.
- Runtime safety limits to prevent infinite loops or excessive command spam.
- Clear separation between supported game API and internal engine code.

---

# 9. Technical Notes for C# .NET 10

The simulation core should remain deterministic, serializable, testable, and independent from the presentation layer.

Programs should be data-driven and replayable.

## 9.1 Technical recommendations

| Concern | Recommended approach |
|---------|----------------------|
| Determinism | Program evaluation must be tick/order stable. Same state and same player inputs produce the same results. |
| Serialization | Save installed programs, parameter values, rule cards, compiled representation, cooldown state, and version. |
| Versioning | Program definitions need stable IDs and migration rules when game updates change resources or actions. |
| Sandboxing | Future scripts should not execute arbitrary host code. They should call a bounded game API. |
| Performance | Programs should have budgeted evaluation frequency and event-driven triggers where possible. |
| Telemetry | Program execution should emit structured telemetry events, not just text logs. |
| Testing | Headless tests should verify program outcomes, conflict resolution, serialization, and replay. |

---

## 9.2 Suggested program pipeline

```text
ProgramDefinition
        ↓
ProgramInstance + parameters
        ↓
Rule-card list or Script AST
        ↓
Validation
        ↓
Compiled deterministic command model
        ↓
Execution during operational time
        ↓
Telemetry events
        ↓
Player-facing report
```

---

## 9.3 Recommended MVP implementation

1. Implement program objects and installation slots.
2. Implement preset programs with parameter tuning.
3. Implement rule-card view/edit for a limited condition/action vocabulary.
4. Implement mission doctrine programs and a small set of vessel control programs.
5. Implement program reports: fired, skipped, blocked, conflict, and impact summary.
6. Implement found programs as mission rewards, but keep them optional.
7. Do **not** implement FBD.
8. Do **not** implement the Python-like editor until validation, autocomplete, and debugging are planned.

---

# 10. Open Design Decisions

| Decision | Recommended default | Why it matters |
|----------|---------------------|----------------|
| Can programs be installed while TimeFlow is running? | No for MVP; require pause/0× for edits. | Avoids confusing mid-tick behavior and supports careful planning. |
| Are found programs always safe? | No. Some can be corrupted, locked, or risky. | Creates fun inspection and repair gameplay. |
| Can players share programs? | Post-MVP only. | Potential Steam Workshop value, but requires versioning and safety. |
| Can programs affect story evidence directly? | Only through allowed operations and analysis priorities. | Prevents automation from solving the mystery automatically. |
| Can programs consume compute/energy? | Yes for advanced/global controllers. | Gives powerful automation a balancing cost. |
| Can programs conflict? | Yes, but conflicts must be reported clearly. | Conflict creates depth only if explainable. |
| Can a bad program cause damage? | Yes, but avoid permanent dead ends. | Failure should remain telemetry-rich and recoverable. |
| Will code convert back to rule cards? | No. | Avoids impossible reverse representation of advanced scripts. |

---

# 11. Final Design Summary

Programming in **Dimenship** should be one of the central optimization paths.

It should let the player express strategy through reusable automation, but it should not require every player to become a programmer.

Preset programs form the accessible foundation.

Rule cards provide inspection and customization.

Found programs transform scavenging and exploration into automation rewards.

Advanced players can write controllers from scratch.

A Python-like expert editor may appear later as a separate expert tool, but **no FBD editor is planned**, and **Python-to-rule-card conversion is not required**.

---

> **Best short formulation**
>
> **Programs are loot, tools, doctrines, and player-authored strategies.**
>
> They make bots smarter, factories more stable, reactors safer, missions less wasteful, and TimeFlow less noisy.
>
> Good programming should feel like engineering a vessel that can think in the player's style while still producing clear logs when it fails.