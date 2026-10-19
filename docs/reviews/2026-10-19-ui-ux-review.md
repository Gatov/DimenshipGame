# Dimenship UI/UX Recommendations

Date: 2026-10-19
Status: Recommendations for prioritization — adjudicated below
Perspectives: UI/UX designer and player

> Recorded as received. The adjudication, checked against the code at `5e2af29`, is the last
> section. What was built and what was designed for later is in
> `docs/superpowers/specs/2026-10-19-ux-review-adoption-design.md`.

## Assessment

Dimenship has a coherent control-room identity. Its graph, contextual inspector, and dedicated management screens support inspection and optimization. The main opportunity is helping the player turn system information into a confident next action.

An experienced automation player can find depth in the telemetry. A newcomer needs clearer answers to three questions: **What am I trying to achieve? What should I do next? How will I know it worked?**

This review covers the game design document, overview and programming concept images, and current UI source. Interaction findings are based on source inspection. The game was not run for this review; layout, screen scaling, and actual player behaviour require live validation. Concept images represent visual direction and are not evidence of the current running interface.

## Preserve

- The fixed vessel schematic and stable facility positions, which support spatial learning.
- The division between overview, contextual inspection, and detailed planning.
- State words, icons, and dashed outlines alongside colour.
- Readable explanations of waiting, blocking, and resource competition.
- A restrained schematic appearance, with atmosphere and luminous imagery supporting the operational information.

## Recommendation list

Priorities describe the proposed order of work: P1 addresses player trust or basic comprehension; P2 improves decision-making and navigation; P3 improves polish and reward.

| ID | Priority | Recommendation | Main player benefit |
|---|---|---|---|
| UX-01 | P1 | Preserve unfinished work across navigation | Exploring the vessel does not erase edits |
| UX-02 | P1 | Improve default text readability | Players can comfortably read essential information |
| UX-03 | P1 | Introduce one meaningful opening objective | Players understand their first useful action |
| UX-04 | P1 | Separate time state from vessel health | Players can trust the overall status and alerts |
| UX-05 | P2 | Connect diagnosis directly to intervention | Players can resolve a problem without repeated searching |
| UX-06 | P2 | Apply consistent colour meanings | Expected waiting is distinguishable from trouble |
| UX-07 | P2 | Keep unbuilt facilities readable and discoverable | Players recognize opportunities to expand |
| UX-08 | P2 | Explain concept previews before substantial editing | Players understand feature and persistence limits |
| UX-09 | P2 | Use player-facing terminology | Players understand messages without learning internal names |
| UX-10 | P2 | Organize the overview around current decisions | Players can identify what matters now |
| UX-11 | P3 | Explain the benefit of completed actions | Optimization produces understandable, satisfying outcomes |

## UX-01 — Preserve unfinished work across navigation

Designer perspective: Navigation should retain editing context. Currently, changing views destroys and recreates the panel, while production drafts, program edits, and loadout edits live inside those panels.

Player perspective: “I checked my resources, came back, and my work was gone.”

- [ ] Retain unfinished production drafts, program edits, and loadout edits when changing views.
- [ ] Retain the selected object, relevant scroll position, and undo history with the edit.
- [ ] Give explicit discard actions a clear scope.
- [ ] Distinguish session retention from saving across application restarts.

Completion check: Edit each affected view, visit another view, and return. The edit and its relevant context remain available.

Evidence: `dimenship/scripts/ui/Zone.cs`, `focus/operations/OperationsFocus.cs`, `focus/programs/ProgramsFocus.cs`, and `focus/loadouts/LoadoutsFocus.cs`. The `focus/` paths here are relative to `dimenship/scripts/ui/`.

## UX-02 — Improve default text readability

Designer perspective: The current palette defines 9 px captions and 11 px body text. Against the solid panel colour, secondary text colours have calculated contrast ratios of approximately 2.79:1 and 2.18:1. Transparency and background imagery require additional checking.

Player perspective: “The numbers are visible, but reading the explanations takes effort.”

- [ ] Increase default text size for meaningful labels, explanations, and controls.
- [ ] Brighten secondary text that carries information needed for a decision.
- [ ] Reserve faint text for optional or inactive information.
- [ ] Use tabular numerals for changing quantities, timers, and aligned numeric columns.
- [ ] Verify default and enlarged interface scales with backdrop and frost both enabled and disabled.

Completion check: Essential text remains readable and controls remain usable at the chosen minimum supported window size and enlarged interface scale. Use 4.5:1 as a benchmark for ordinary informative text; verify the actual rendered backgrounds.

Evidence: `dimenship/scripts/ui/ShellPalette.cs`, `ShellTheme.cs`, and `SettingsOverlay.cs`. Benchmark: W3C contrast minimum guidance.

## UX-03 — Introduce one meaningful opening objective

Designer perspective: The opening exposes systems before establishing a task. A short objective can teach the interaction model through an actual result.

Player perspective: “I can see the vessel, but I do not know what I should accomplish first.”

- [ ] Introduce one achievable objective using currently playable systems.
- [ ] Explain why the objective matters and what completion changes.
- [ ] Provide a direct route to its first action.
- [ ] Start the introductory scenario paused while the player learns the controls.
- [ ] Allow experienced players to dismiss guidance.

Example: Commission Factory Beta to add production capacity, then show the player how another order can use it. Treat this as a scenario proposal, not an existing objective.

Completion check: A new player can identify the objective, begin the required action, and recognize completion without external instructions.

Evidence: `dimenship/scripts/StartScreen.cs`, `dimenship/scripts/ui/ShellRoot.cs`, and `dimenship/content/scenarios/default_vessel.json`.

## UX-04 — Separate time state from vessel health

Designer perspective: “NOMINAL” currently describes a running simulation without a kernel fault, even when facilities are blocked. The status-bar alert count counts blocked executors rather than the simulation’s full alert list.

Player perspective: “The interface says everything is normal, but an operation needs attention.”

- [ ] Display time state explicitly, such as “Paused” or “Running ×5.”
- [ ] Display operational health separately, using conditions relevant to player intervention.
- [ ] Reconcile simulation alerts and blocked-system conditions into a clearly defined attention summary.
- [ ] Make the summary open the affected operations or facilities.
- [ ] Avoid double-counting the same underlying problem.

Completion check: A waiting-plan alert and a blocked facility are discoverable from the overview. The overall health message accurately reflects those conditions.

Evidence: `dimenship/scripts/ui/StatusBar.cs` and `src/Dimenship.Core/Simulation/WorldSnapshot.cs`.

## UX-05 — Connect diagnosis directly to intervention

Designer perspective: Cause explanations and clickable queue rows provide a foundation. The next step is a consistent path from a problem to the control that can address it.

Player perspective: “I understand why it stopped; take me to the thing I can change.”

- [ ] Show the affected goal, immediate cause, and relevant dependency together.
- [ ] Link a resource claim or priority conflict to the plan responsible.
- [ ] Highlight relevant facilities and routes when inspecting a dependency.
- [ ] Offer contextual navigation such as “Open competing plan” or “Inspect destination storage.”
- [ ] Preserve graph position and selection during the round trip; extend the existing camera retention.

Completion check: A player can diagnose and reach the appropriate control for a stalled plan without manually searching every facility or retaining several identifiers in memory.

Evidence: `dimenship/scripts/ui/panels/FacilityInspectorPanel.cs`, `focus/operations/OperationsFocus.cs`, and `ShellContext.cs`, under `dimenship/scripts/ui/`.

## UX-06 — Apply consistent colour meanings

Designer perspective: Active navigation uses amber, while the log colours several routine postponements red. This weakens the distinction between selection, waiting, and failure.

Player perspective: “Is this red message an emergency, or is the factory simply waiting?”

- [ ] Define shared meanings for selection, expected waiting, attention required, and failure.
- [ ] Apply those meanings across navigation, graph cards, inspector, log, and alerts.
- [ ] Pair colour with words or symbols.
- [ ] Explain that high transport utilization can be productive; it is not automatically a fault.

Completion check: A healthy production chain with temporary input waits does not appear to be in an emergency state. Selection is distinguishable from warning.

Evidence: `dimenship/scripts/ui/Rail.cs`, `Zone.cs`, `panels/EventLogPanel.cs`, and `ShellPalette.cs`.

## UX-07 — Keep unbuilt facilities readable and discoverable

Designer perspective: Reducing an entire card to 40% opacity dims its name and explanation as well as its structure. Yet these cards are interactive construction opportunities.

Player perspective: “I assumed the faded facility was unavailable.”

- [ ] Keep facility names and purpose readable.
- [ ] Retain dashed outlines and an explicit “Unbuilt” state.
- [ ] Make selection and construction affordances apparent.
- [ ] Explain any unavailable construction action beside the action.

Completion check: A new player can distinguish an unbuilt facility that can be ordered from one whose construction cannot currently proceed.

Evidence: `dimenship/scripts/ui/ShellPalette.cs`, `focus/NodeCard.cs`, and `panels/FacilityInspectorPanel.cs`.

## UX-08 — Explain concept previews before substantial editing

Designer perspective: Programs and Robotics already disclose their concept status inside their views. Navigation and opening explanations should also establish expectations about execution and retention.

Player perspective: “Will this control my vessel, and will my edits still exist when I return?”

- [ ] Mark the navigation entries as previews while their underlying systems remain unavailable.
- [ ] Explain execution limits in plain language before the player invests substantial effort.
- [ ] State whether edits survive navigation and application restart, reflecting the actual behaviour.
- [ ] Keep unavailable activation or construction controls accompanied by a clear reason.

Completion check: A player can explain what each preview does and what happens to their edits before beginning a substantial edit.

Evidence: `dimenship/scripts/ui/focus/programs/ProgramsFocus.cs`, `focus/loadouts/LoadoutsFocus.cs`, and `Rail.cs`, under `dimenship/scripts/ui/`.

## UX-09 — Use player-facing terminology

Designer perspective: Some messages expose internal names or describe how commands reach the engine. This adds interpretation work at the point of decision.

Player perspective: “Tell me what is missing and what my action will do.”

- [ ] Replace enum fallbacks such as `NOEXECUTORONLINE` with readable explanations.
- [ ] Use catalog display names instead of raw item or schematic identifiers where possible.
- [ ] Replace “injects these tasks into the vessel’s queues” with a clear description of starting the order.
- [ ] Prefer seconds, minutes, and hours in ordinary explanations; retain ticks in optional technical detail.
- [ ] Ensure refusals always include a meaningful reason.

Completion check: Every issue and refusal reachable from the composer and inspector is understandable without knowledge of internal identifiers.

Evidence: `dimenship/scripts/ui/Labels.cs`, `focus/ResourceStrip.cs`, `focus/operations/OperationsFocus.cs`, and `panels/FacilityInspectorPanel.cs`.

## UX-10 — Organize the overview around current decisions

Designer perspective: System-level detail needs an attention hierarchy above it. The overview concept also repeats some summaries and gives many panels similar visual weight.

Player perspective: “Which of these numbers matters to what I am doing now?”

- [ ] Show a compact summary of the current objective, main blocker, and next useful action.
- [ ] Prioritize relevant resources and facilities when an objective or plan is selected.
- [ ] Keep advanced telemetry accessible through inspection or expansion.
- [ ] Consolidate duplicate summaries and reduce equally prominent competing panels.
- [ ] Preserve the vessel’s stable spatial layout as attention changes.

Completion check: After a brief look at the overview, a player can identify their current goal and the most important condition affecting it.

Evidence: `docs/Dimenship Base Overview.png`, `dimenship/scripts/ui/ShellRoot.cs`, and `focus/BaseGraphFocus.cs` and `focus/ResourceStrip.cs` under `dimenship/scripts/ui/`.

## UX-11 — Explain the benefit of completed actions

Designer perspective: Completion events establish that something happened. A useful outcome message also connects the result to the player’s intention.

Player perspective: “I built it. What can I do now?”

- [ ] Show restrained completion feedback naming the result and its available capability.
- [ ] Link completion feedback to the affected facility or plan.
- [ ] Explain that approving an order queues work; it does not immediately complete construction.
- [ ] Where telemetry supports it, show actual improvements such as reduced waiting or increased output.
- [ ] Keep feedback selective so repeated routine events do not interrupt supervision.

Example: “Factory Beta commissioned — another production facility is available. View facility.”

Completion check: Players can distinguish an accepted order from a completed outcome and explain the benefit of their completed action.

Evidence: `dimenship/scripts/ui/SimulationDriver.cs`, `panels/EventLogPanel.cs`, and `focus/operations/OperationsFocus.cs`, under `dimenship/scripts/ui/`.

## Suggested delivery order

1. Preserve edits and improve default readability: UX-01 and UX-02.
2. Establish the opening task and trustworthy status: UX-03 and UX-04.
3. Improve diagnosis, colour consistency, construction discovery, and wording: UX-05 through UX-09.
4. Refine overview hierarchy and completion feedback: UX-10 and UX-11.

## Player validation checklist

Use a fresh player unfamiliar with the project. Observe hesitation, wrong turns, requests for help, and whether the player can explain the outcome.

| Task | Successful outcome |
|---|---|
| Identify the opening objective | Player states the goal and finds its first action |
| Order a facility build | Player understands the cost, acceptance, progress, and completion |
| Diagnose a stalled plan | Player identifies the cause and reaches a relevant control |
| Leave and resume an unfinished edit | Work remains intact and the player resumes without reconstruction |
| Interpret vessel status | Player distinguishes time speed, expected waiting, and attention required |
| Explore Programs or Robotics | Player understands preview and retention limits |
| Read at an enlarged interface scale | Important information and controls remain accessible |
| Explain a completed action | Player describes what changed and why it helps |

## Adjudication (2026-10-19)

Every claim with a file named was checked against the source at `5e2af29`. The review holds up.
One finding is understated and one is misquoted.

- **UX-04 is a bug, not a polish item.** No line in `dimenship/scripts` reads
  `WorldSnapshot.Alerts`. `AlertCode.PlanWaiting` is raised by `ReviewWaitingPlans` (K8), is the
  only alert the kernel raises, and reached no screen at all. Accepted, and built first.
- **UX-09's `NOEXECUTORONLINE` is `NOEXECUTORORLINE`.** `Labels.Issue` had no case for four
  `DraftIssueKind`s (`GoalShortfall`, `LockedSchematic`, `NoExecutorOrLine`, `CyclicSchematic`),
  which fell through to the enum name. The same finding covers `Conditions.Describe`, which
  printed every postpone reason in screaming snake case (`MISSING_INPUT`). Accepted.
- **UX-06 is confirmed, and two of the cases break rules the repository already states.** The
  rail's active entry and every zone heading used `StateWarn`, although `ShellPalette.Accent`'s
  own comment says amber stopped doubling as selection. The event log and the inspector's task
  rows coloured every postponement red, against U3's rule that only blocked output takes the
  fault colour. Accepted.
- **UX-02's figures reproduce exactly**: 2.79:1 and 2.18:1 over `BgPanel`, with `FontMicro` = 9
  used at 93 call sites. Accepted. The font step is held to one pixel, because the graph's cards
  are fixed cells.
- **UX-01 is real for Operations.** The inspector's VIEW PLAN round trip destroyed a
  half-composed order, and so did the composer's own BACK button. For the two concept mocks it
  is real but low value: they persist nothing by design. Accepted for Operations; designed for
  the mocks.
- **UX-03 and UX-10 need game design, not a shell pass.** There is no objective system, and the
  word *objective* is reserved for the GDD's story objectives. Starting paused is accepted now.
  The rest is designed.
- **UX-05, UX-07 and UX-11** are accepted and designed. UX-07 reverses a recorded decision (the
  whole-card alpha), so the project owner decides it.
