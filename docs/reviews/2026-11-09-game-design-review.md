# Dimenship — Game Design Review

Date: 2026-11-09
Status: Recommendations for the project owner. Nothing here is decided, and nothing in `src/`,
`dimenship/` or the other documents is changed by it.
Perspective: game designer

## What was reviewed

- **Read in full:** `Game Design v0.9.md`, `Dimenship Programming v0.1.md`,
  `Dimenship Mission Visualization Proposal.md`,
  `production scheduling and automation - gameplay design.md`, both transcribed specs under
  `docs/specs/`, the three concept images, the bot composition design, the E1, E2 and E3 experiment
  reviews, and the 2026-10-19 UI/UX review with its adjudication and adoption design.
- **Read in the parts that carry design decisions** (goal, decisions, *Not built*, open items):
  every other spec under `docs/superpowers/specs/`, the M3, K1, K2 and K4 reviews, and the
  scheduling ticket plan.
- **Skimmed for status only:** the implementation plans under `docs/superpowers/plans/` and the
  codebase architecture review. They are about code, not about the game.
- **Checked against the shipped content:** `dimenship/content/` (items, schematics, facilities,
  lines, the default vessel), the shell's panel list and speed steps, and the open issue list.

**The game was not run for this review.** Everything about feel, pacing and readability on screen is
inferred from documents, content and replay reports. Those inferences are marked where they matter.

## Verdict

Dimenship has a strong, unusual premise and an unusually well-engineered foundation. It also has a
gap between them that is now the project's main risk.

**The GDD describes an investigation game played through autonomous missions. What has been
designed in detail, built and measured is a production scheduler.** Missions, strata, robots,
doctrines, the program runtime, the Case Board, research, quest readiness and reports have no
design beyond the GDD's own paragraphs. The production layer, which the GDD calls a supporting
system, has about thirty specs and reviews.

That order was defensible: production is the substrate everything else stands on, and the kernel
it produced is deterministic, saveable and measurable. But three things follow from it today:

1. **The playable game has no reason to produce anything.** Nothing brings material in and nothing
   takes finished goods out. Demand exists only in the replay harness's scripts.
2. **The production design passed a simulation test, not a play test.** The E3 report says so
   itself. Its own numbers contain warnings about how many decisions a player actually gets.
3. **The hook is the least designed part.** A player buys this game for "multidimensional murder
   investigation by remote robots". None of that can be evaluated yet, on paper or in play.

My recommendation is to **stop deepening production now**, put the existing scheduling situations
in front of players as playable drills, and spend the next design effort on the thinnest possible
mission loop and a paper prototype of the investigation. The detail is in *Proposals* and
*Recommended sequence*.

## Where the design stands

| Pillar (GDD §1–§4) | Designed | Built | Notes |
| :--- | :--- | :--- | :--- |
| Operational time, pause and speed | Yes | Yes | Speeds are 0, ×1, ×5, ×30. The GDD says 1×, 2×, 4×. |
| Vessel schematic as diagnostic overview | Yes, in depth | Yes | The strongest part of the game today. |
| Production, transport, storage | Yes, in depth | Yes | Planner, belts, claims, priority, hold, cancel, amend, move. |
| Facility construction | Yes | Yes | Five unbuilt slots on a new campaign. |
| Energy as "the global constraint" | Partly | Constant capacity only | Fuel-burning power is issue #17, open since August. |
| Programs and doctrines | Gameplay doc and editor spec | Editor mock only | No runtime. `programs.json` is required to be empty. |
| Robots, fittings, loadouts | Composition spec (Draft) | Composer mock only | No robot exists in the kernel beyond a declared type. |
| Refit, salvage, upgrades | Spec (Draft) | No | |
| Missions and strata | **No** | No | `strata.json` is empty. Both docks are unbuilt and idle. |
| Mission Monitor | Proposal | No | It visualizes a simulation that has no design. |
| Investigation and Case Board | **No** | No | GDD prose and five example quest rows only. |
| Quest readiness and dependency highlight | GDD only | No | |
| Research, reports, dashboard | GDD only | No | |
| Onboarding | Briefing designed | Starts paused only | No objective of any kind in the running game. |
| Save and load | Format built and tested | No UI | Listed as a follow-up of the refactoring plan. |

## What is strong and should be kept

- **The premise is distinctive.** A fugitive who can act only through machines they prepared is a
  clear fantasy, and it justifies the no-direct-control rule instead of merely asserting it.
- **Cause, not just state.** "What is wrong, why, and where do I fix it" (GDD §5.5) is the right
  rule for this genre, and it has been honoured: every wait names its reason and its other party
  (`WaitCause`), and a facility is "blocked" only when it truly cannot put its output down.
- **Systems reuse instead of parallel systems.** A socket is a storage, a refit is a plan of
  transports, salvage is the build recipe read backwards, commissioning is a tick phase. Each
  of these keeps the player's model small as well as the engine's.
- **The conservation and recovery invariants.** "Recycling is never a source" and "there is always
  a path back to expedition capability" are stated as arithmetic, not balance. That is rare and
  valuable.
- **A fixed, authored vessel.** Rejecting base-building was right. It protects scope and gives the
  player a stable picture to learn.
- **Measurement before commitment.** The replay harness, the held-out situation and the stated
  revisit conditions are a better evidence standard than most projects of any size manage.
- **The same door for player and program.** Manual play and controllers use one command surface.
  A policy that wins in a replay is one a player could carry out. Keep this rule absolutely.

## Issues

Ordered by how much they threaten the game, not by how hard they are to fix.

### 1. The built game and the promised game have diverged

Thirty-odd design documents concern production. None concerns how a mission resolves, what a
stratum is, what evidence is, or how a contradiction is found. The GDD's core loop has eight steps;
steps 2, 5 and 7 (read reports, deploy missions, respond to mission events) cannot happen.

This matters beyond scheduling. Every production decision made so far was made without knowing
what production is *for*. How many frames does an expedition need? How often do they come back
damaged? How much Matter Mix does a mission return? Those numbers set whether changeovers,
buffers and priority matter at all, and none of them exists.

### 2. The economy has no source and no sink

- **No source.** The vessel opens with 3,600 Matter Mix and 40 Basic Metals. Nothing replenishes
  them except emergency hydrogen synthesis, which is designed to be poor.
- **No sink.** Nothing consumes a robot frame, a module or a bulkhead. The bulkhead's own content
  note says "nothing aboard consumes it yet; scripted demand asks for it".
- **No demand.** In the running game the only reason to order anything is to commission the five
  unbuilt slots. After that the vessel is complete and there is nothing to do.

The scheduling experiment's "go" is therefore a go on a mechanic whose inputs the game cannot yet
generate. It is sound evidence that the mechanic *can* create decisions. It says nothing about
whether the real demand pattern will.

### 3. Production depth is shown in simulation, and its own data carries warnings

The E3 report is honest that "session depth is still a playtesting question". Reading its tables
as a designer, four things stand out.

- **Facilities are mostly idle.** In situation A as E1 recorded it, Reactor Alpha works 280 of
  9,000 ticks and Factory Alpha 968. A vessel at 3–11% load does not have a scheduling problem;
  it has a latency problem. Contention appears only in short bursts.
- **A changeover is very expensive against a run.** With 8-tick runs and 120-tick changeovers, one
  setup costs fifteen runs. In E1's situation A, Factory Alpha spent 2,520 ticks switching against
  968 working. K5c later cut that situation's changeovers from 39 to 15 by choosing facilities
  better, so the worst of it is gone under a good plan. The ratio remains, and a player whose
  plan is not good will watch factories mostly reconfigure. Whether that reads as tension or as
  frustration is a feel question only a playtest can answer.
- **Stockpiling beats scheduling.** In the sustained situation the E3 report says it outright:
  "stock still beats scheduling in A". After K5c the gap is wider: the improved controller, which
  makes stock ahead, scores 853 against plain queue order's 2,910. Stock has no holding cost, so
  the dominant strategy is to over-produce everything, and that makes most scheduling decisions
  unnecessary.
- **The decisions are few and small.** In situation B, one priority command gets within 7 ticks of
  the eight-decision controller. In A, the improved controller makes about one decision per 230
  ticks. At ×30 that is one decision every eight seconds of play; at ×1 it is one every four
  minutes.

There is a pattern underneath. **Each time the experiment found a decision a player could make
well, the follow-up taught the planner to make it.** E3 found that assigning hardening to Reactor
Beta by hand saved 352 ticks. K5c then made the planner's estimate count changeovers. Its review
records the result: the hand-scheduled fixture is now 12 ticks *behind* doing nothing (2,922
against 2,910). That is good engineering and it shrinks the player's job. If the planner keeps
getting smarter, what remains for the player is *what to order and what matters most*, which is
strategy, and strategy comes from missions and the case, not from the factory floor.

That is a legitimate destination for a supervision game. But it means production cannot carry the
game's depth alone, which makes issue 1 more urgent, not less.

### 4. Programming is the stated mastery path, and its feasibility is unproven

- **The winning policies were written in C#.** The reference controllers learn targets from past
  demand, count outstanding plans as coverage, rank three classes of work and hold plans only at
  contested facilities. The programming view's language has **no variables, no functions and no
  loops**, by a recorded decision. Nobody has checked that the improved controller can be written
  in rule cards. If it cannot, the experiment proved depth exists in a language the player will
  never have.
- **The first program a player writes fails badly.** Simple replenishment ("if stock is low,
  order more") is the obvious first rule. In situation B it causes 85 changeovers and the
  expedition never arrives; in C six of seven repairs never arrive. The design calls this the
  first rung of a learning ladder. A first attempt whose result is "nothing gets delivered" is a
  cliff unless the game explains, in one line, "this rule ordered the same shortage 28 times".
- **"Optional" and "central" pull against each other.** The programming doc says programs are not
  required and also that they are "one of the central optimization paths". If presets are good
  enough, authoring is flavour. If they are not, the game requires programming. The intended
  balance point is not stated anywhere.
- **Programs as loot depends on missions.** Found programs, corrupted programs and forbidden AI
  fragments are among the most appealing ideas in the documents. They have no delivery mechanism
  until missions exist.

### 5. Nothing makes time matter

The fiction promises pressure: a fugitive, a fabricated case, a net closing. The design promises
"pacing pressure inside the simulation, not on the player in real life". But no mechanism creates
any. The recovery invariant guarantees there is no dead end, the hydrogen extractor never stops,
and nothing decays, pursues or expires. With unlimited time, patience solves every production
problem and optimization is optional.

Automation games usually answer this with a goal gated on throughput, or with upkeep. Dimenship
has neither yet. Without one, the scheduling depth of issue 3 has no reason to be used.

### 6. Energy is called the global constraint and never binds

GDD §7.3 says "energy is the global constraint". In every measured run, in every situation and
under every policy, the *Throttled* column is zero. Capacity is a constant 10,000 and the
Stabilization Array is a constant draw of 4,000. Fuel-burning power was designed in August
(issue #17) and has not moved. Today energy is a number on a card.

### 7. The investigation is a hook without a mechanic

"Truth is reconstructed from contradictions, leads, witness candidates, verification questions and
cross-stratum comparison" is the most interesting sentence in the GDD. Every concrete quest
example that follows it is a capability gate: build the breach drone, raise stabilization, free
the evidence channel. If that is all the investigation becomes, it is a tech tree with a story
skin, and the Case Board is a checklist.

The hard design question has not been asked yet: **what does the player actually deduce, and how
does the game know they deduced it?** A deterministic systems game and an authored mystery can fit
together well, but the fit has to be designed. It will not emerge from the production layer.

### 8. Time scale and pacing are undefined

- One tick is one second. A run is 8 seconds, a changeover two minutes, a bulkhead order eight to
  ten minutes at ×1.
- The GDD specifies 1×, 2× and 4×. The shell ships ×1, ×5 and ×30. That is a sevenfold difference
  in the top speed, and it changes what "supervising at high TimeFlow" means.
- The concept art shows "07d 14:32:18". Seven operational days is about 650,000 ticks, or six
  real hours at ×30.
- No document says how long a mission lasts, how long a session should be, or how often the
  player should be making a decision.

"A good system earns longer stable runs at higher TimeFlow" is the game's central reward loop
(GDD §12). It cannot be tuned, or even evaluated, without these numbers.

### 9. The equipment rules punish the experimentation the game asks for

GDD §5.10 says fitted equipment is never stockpiled: no spares, nothing built ahead of a refit,
and a removed part must be recycled at a loss. The stated purpose is that "upgrade downtime cannot
be softened by preparation".

The player-facing consequence is that **every loadout change destroys the old part.** The
programming doc asks players to "build aggressive, cautious or evidence-safe variants and compare
outcomes". Under this rule each comparison costs a lossy salvage and a rebuild. Players respond to
that by not experimenting.

The original technical reason has also gone. The refit spec rejected a spares inventory because
worn parts would need per-instance identity. The bot composition spec then decided that fittings
are fungible and carry no per-instance state. What remains is only the design reason, and it
should be weighed again on its own.

### 10. The mission model's promise of determinism is unresolved

GDD §3 says identical inputs produce identical outcomes, "including mission events". The world
state spec declares seeded random streams for missions, hazards and salvage. Both can be true, but
they lead to different games:

- If a mission's outcome depends on a stream that advances with every draw, then relaunching the
  same robots with the same doctrine gives a different result. The player cannot tell a better
  doctrine from a better roll, and "failure is informative" stops being true.
- If the site's truth is fixed and the outcome is a pure function of site, loadout and doctrine,
  then changing one rule and relaunching is a real experiment.

The second is what the rest of the design assumes. It is not written down.

### 11. The vocabulary is heavier than the game needs

The documents police vocabulary carefully, and the fact that they must is a signal.

| Word | Meanings a player meets |
| :--- | :--- |
| **hold** | Resource Storage ("the hold"); a Launch Pad's hold; the HOLD command on a plan; stock "held" for a plan by a claim. |
| **reactor** | The Matter Reactor (separates material) and the Power Core's reactor (makes energy). The GDD says three times that they differ. `reactors.json` is the power one. |
| **Mission Dock / Launch Pad** | One facility. The catalog and GDD say Mission Dock; the vessel's cards say Launch Pad 1 and 2. |
| **module / fitting / socket / slot** | Settled in the specs, at the cost of four words for neighbouring ideas. The GDD glossary still says *Fitted Module*. |
| **claim / reservation** | A claim is material; a reservation is room. The GDD still uses "reservation" for the first. |
| **refinery** | Withdrawn by GDD v0.9, still used throughout Programming v0.1 and the concept art. |

A name that needs a disclaimer every time it appears is the wrong name. *Matter Reactor* is the
clearest case.

### 12. The authoritative document trails the game

The GDD is the stated tiebreaker, and several of its rules are no longer true of the shipped game
or of later specs:

- §5.3 and §10.1 keep production control out of the schematic view. Its inspector now quotes and
  builds a slot and lists a facility's queue (U4). That was a good decision. The GDD has not
  caught up.
- §4 and §10 give speeds of 1×, 2× and 4×. The shell has ×1, ×5 and ×30.
- §5.2 and §10 promise overlays (energy, materials, data), line types and a quest dependency
  highlight. None exists, and with one line type there is nothing to overlay.
- §10's robot row and the *Fitted Module* glossary entry predate the bot composition spec.
- §11 says .NET 10; the repository targets .NET 8 on purpose.
- Programming v0.1 still speaks the vocabulary v0.9 withdrew: refineries, Refined Alloy, Isogen,
  reactor fuel.

None of these is serious alone. Together they mean a new reader of the "authoritative" document
gets a wrong picture of the game.

### 13. The MVP is far larger than the pace supports

GDD §10's MVP includes three mission types, two quest operations, two robot frames with a fitting
set, doctrines, the program runtime, the Case Board, research, reports and the schematic's
overlays. In roughly three and a half months the project has built the production layer and the
shell around it, to a high standard. At that standard, the MVP as written is a year or more away,
and nothing in it is ordered by what would test the concept soonest.

### 14. No one outside the project has played it

The UI/UX review of 2026-10-19 was a source inspection. The E-series was replay. The adoption
design defers player sessions until more is built. After this much work, **the most valuable
missing information is what a fresh player does in the first twenty minutes**, and none of the
planned work produces it.

### Smaller observations

- **Orders are fractions of a thing.** One `assemble_frames` run makes 0.025 of a robot frame, and
  the experiment's expeditions order a quarter of a frame. Bulk flow suits materials. Robots will be
  whole objects. The chain's numbers will need rebalancing into units a player can picture ("three
  frames for the expedition") before missions consume them.
- **Factories are identical.** All three share one archetype. They differ only in which lines
  reach them and how fast. "Specialisation through upgrades" was listed as proposed core in the
  scheduling design and then left out of the experiment, so the most natural source of standing
  differences between machines is untested.
- **The revisit chain strains the fiction.** A machine that separates matter also hardens plates,
  because two facility kinds were all the experiment had. It works mechanically. A player may
  find it arbitrary.
- **The planner ignores stock outside the main hold.** That is a deliberate, well-argued rule. It
  will still surprise players who can see frames sitting in a Launch Pad and watch the vessel
  build more. It needs one sentence of explanation at the point of ordering.
- **The schematic is calm by design, and the vessel is idle most of the time.** The Mission
  Monitor proposal correctly identifies that the game needs something to watch. Until it exists,
  the running game at ×1 is a mostly still picture.

## Proposals

Numbered for reference. Each says what it is for and what it costs.

### P1. Operations Drills: put the existing situations in front of players

The replay harness already holds what the game lacks: authored demand. Situations A, B and C are
scenario scripts with timed orders and a measured outcome, and the reference controllers give
ready-made par scores.

Make them playable. A drill is a scenario plus a demand script, played in the real shell, ending in
a score card: readiness, changeovers, material tied up and interventions, set against the
reference controllers as par scores (plain queue order as the floor, the improved controller as
the mark to beat).

- **Why:** it is the cheapest way to answer E3's open question, whether making these decisions is
  fun. It gives the game demand without a mission system. It is a natural demo. Later it is where
  programs prove themselves, because a drill is a fixed problem with a comparable score.
- **Cost:** demand events fired from the scenario inside the game (the harness's format can be
  reused), a consumer for delivered goods (see P2), and a results screen. No new kernel rule.
- **Risk:** it could harden into a puzzle mode that distracts from the campaign. Treat it as a
  test bench and a tutorial ladder first.

### P2. Close the loop with an abstract expedition

Before robots, doctrines or strata exist, a Launch Pad can run the simplest possible mission:

1. The pad takes a required set of goods from its hold (this is the sink).
2. After a fixed number of ticks, it returns Matter Mix with a composition profile (this is the
   source).
3. The result is a pure function of the site and what was sent.

No robot instances, no doctrine, no monitor. One content file of sites.

- **Why:** it gives production a purpose, makes storage pressure real (returning cargo needs
  room, which the GDD's "storage-block handling" already anticipates), and produces the first real
  numbers for how much equipment a mission needs. Every later mission feature replaces a part of
  this stub instead of arriving all at once.
- **Cost:** one new tick phase and the dock's first schematic-like behaviour. The world state spec
  already asks whether a dock is a fourth facility type; this forces that answer.

### P3. One clock: the vessel burns fuel to stay hidden

Make the Stabilization Array's draw depend on fuel, as issue #17 already designs for the Power
Core. Fuel comes from mission returns. An idle vessel slowly spends its reserve; an efficient one
turns the same fuel into more investigative reach.

- **Why:** it answers issues 5 and 6 together and fits the fiction exactly. The fugitive is safe
  only while the vessel holds its phase. Time has a cost without a real-time timer.
- **Guard:** the recovery invariant must hold. Hydrogen alone should sustain a minimal
  stabilization level at which the vessel survives but cannot launch far. Falling to that floor is
  the fail-soft state.
- **Cost:** issue #17, plus a decision on what low stabilization takes away.

### P4. Give stock a cost, so stockpiling stops being the answer

The measured dominant strategy is to make everything ahead of need. Two levers already exist in
the design and need only tuning:

- **Shared hold volume.** The hold opens about 80% full of Matter Mix. If returning mission cargo
  needs that room, every stored bulkhead is Matter Mix that could not be unloaded.
- **Workpieces.** They already tie up a facility's buffer until finished.

Avoid decay and spoilage. They add bookkeeping and feel punitive. Space is enough.

### P5. Write the reference controllers in rule cards before building the runtime

Take the programming view's actual vocabulary and write, on paper, simple replenishment and the
improved controller. This is a day's work and it decides the language.

Expected findings, to confirm or refute:

- The language needs **derived readings as operands**: "stock plus what is already on order", "is
  this facility contested by a higher-ranked plan". With those, a program can stay stateless, and
  the no-variables decision survives.
- A **standing order** ("keep N in the hold, counting what is coming") should be a built-in
  action or a shipped preset. The production spec already carries it as an open item. A correct
  standing order as the starting preset teaches coverage by example, and opening its rule cards
  shows how it avoids duplicates.
- The naive rule should fail **with an explanation**. A duplicate-order notice ("this rule ordered
  bulkheads 28 times while 500 were already being made") turns the cliff of issue 4 into a lesson.

Also state the intended balance in one sentence, for example: *presets finish the campaign;
authored programs finish it with fewer interruptions and at higher speed.*

### P6. Decide that a mission is a pure function of site, loadout and doctrine

Write the rule into the GDD: a site's contents are fixed when the site is generated or authored.
Randomness may decide what a site *is*; it never decides how one attempt at it goes. Unknown site
properties, revealed by survey missions, supply the uncertainty that dice would otherwise supply.

- **Why:** it makes doctrine debugging honest (issue 10), gives survey missions a job, and makes
  the Mission Monitor's log a true record instead of a narration of rolls.

### P7. Prototype the investigation on paper, and route evidence through the kernel

Two parts.

**The deduction.** Write one small case as cards on a table: six to ten evidence items across two
strata, with three planted contradictions. Play it with someone who has not seen it. The questions
to answer: what does the player assert, what does the game check, and what happens when they are
wrong? A workable shape is that the player proposes a link between two items ("this record
contradicts that sample"), and a verification mission or analysis job confirms or refutes it at a
cost. That keeps the game deterministic and makes being wrong cost time and fuel, not a game over.

**The plumbing.** Evidence can be an item. A forensic analyzer can be a facility with schematics
("analyze sample" produces a lead). Samples can be workpieces, held only in the analyzer's buffer
and never in Resource Storage, which is the rule the workpiece tier already has. Analysis then
competes with production for power and, later, compute. The investigation would use the same
planner, priority and diagnostics the player already knows.

- **Why:** this is the cheapest route to a Case Board that is more than a checklist, and it makes
  the three months of production work serve the story directly.
- **Risk:** evidence as bulk quantities may feel wrong. Evidence wants to be discrete and named.
  The construction unit (one whole unit, consumed whole) is the existing precedent for a discrete
  item.

### P8. Allow a small fitting rack

Permit a limited store of spare fittings: a storage with a count capacity, which is what a socket
already is. Keep the rest of the rule: nothing fitted lives in Resource Storage, and a robot's
sockets are reachable only while docked.

- **Why:** players can keep the old drill while trying the new one, so comparing loadouts stops
  costing a salvage (issue 9). Rack size becomes an upgrade worth wanting.
- **What is kept:** refit downtime is still real, because the swap is still transports and the
  robot must still be docked.
- **If declined:** at least make salvage of a *just-removed, unworn* fitting lossless or nearly
  so, so that reverting an experiment is cheap.

### P9. Write a pacing budget

One table, owned by the GDD, with target values for:

| Quantity | To decide |
| :--- | :--- |
| Session length | e.g. 45–90 minutes |
| Real time per mission at normal speed | |
| Real time between meaningful decisions | at each speed |
| Speed steps | the GDD's 1/2/4 or the shell's 1/5/30, and why |
| Operational time for the whole campaign | |
| What auto-pauses, and how often that should happen in a healthy vessel | |

Then check the shipped numbers (8-tick runs, 120-tick changeovers, line lengths) against it. The
changeover figure in particular was chosen by replay sweep, not by how two minutes of
reconfiguration feels.

### P10. Differentiate the machines

Run the experiment the scheduling design left out: give facilities standing differences. One fast
factory with slow changeovers, one flexible one. It needs no sockets to test; two archetypes in
content would do. If different machines make assignment a lasting decision that the planner
cannot fully automate, that is the missing source of ongoing choices from issue 3.

### P11. Reconcile the documents: a GDD v1.0 pass

- Bring §5.3, §5.4 and §10.1 in line with the inspector's build and queue.
- Fix the speed steps one way or the other (P9).
- Replace *Fitted Module* with *fitting* throughout, and add *claim* to the glossary.
- Rewrite §10's MVP table against P13.
- Mark `Dimenship Programming v0.1.md` as predating v0.9 and update its examples: no refineries,
  no Isogen, the real item names.
- Remove the superseded duplicates at the `docs/` root
  (`Dimenship_Planning_and_Task_Execution_Spec.md`, `dimenship_schematics_specification.md`),
  which CLAUDE.md already says are replaced by `docs/specs/`.

### P12. Rename the worst collisions before more players see them

- **Matter Reactor.** Consider *Separator*, *Matter Forge* or *Transmuter*. Any of them ends the
  confusion with the Power Core's reactor.
- **Mission Dock or Launch Pad.** Pick one for the catalog, the GDD and the cards.
- **Hold.** Keep "the hold" for Resource Storage if wanted, and rename the plan command (PAUSE,
  SUSPEND) so that "held stock", "hold a plan" and "the hold" stop sharing a word.

Renames are cheap in labels and expensive in ids. Change the labels now and leave the ids alone,
which the layout file's historical panel ids already show is an accepted practice here.

### P13. Redefine the MVP as three milestones with a question each

| Milestone | Contains | The question it answers |
| :--- | :--- | :--- |
| **1. The drill bench** | P1, the briefing from the UX adoption design, save and load in the shell | Is supervising and scheduling this vessel fun for twenty minutes? |
| **2. The closed loop** | P2, P3, P4, P9 | Does the vessel-and-expedition cycle sustain a session, and does time matter? |
| **3. The first case** | P6, P7, one robot frame with a few fittings, one doctrine preset with parameters, one short case with one contradiction | Is the investigation a game, and does preparation feel like the way to play it? |

Programs as authored rule cards, the Mission Monitor, refit and salvage, research and a second
frame come after milestone 3, when there is something for them to improve. Each milestone should
have a stated result that would stop or redirect the next.

### P14. Playtest now

Run the UI/UX review's eight-task checklist with three people who have never seen the project, on
the build as it is. It will mostly find onboarding problems that are already known. It will also
show whether the base graph reads, whether a build order is understood, and how long a fresh
player stays curious, and that is information no replay can produce. Repeat at each milestone.

## Possible changes of concept

Four directions, from least to most change. They are not exclusive.

### A. Stay the course, change the order (recommended)

Keep the GDD's concept whole. Freeze production at what the contract covers. Follow the three
milestones of P13. This spends the next effort on the unproven half of the game and treats the
scheduler as finished enough.

### B. The control room is the game

Drop the investigation to framing and make the product an automation and scheduling game: drills,
expedition readiness, programs, scores. This is what the project has actually been building, and
it could ship soonest.

- **For:** it matches the team's demonstrated strengths and the existing evidence.
- **Against:** it gives up the one idea no other game has. On the evidence of issue 3, the
  scheduler alone may not hold a player for long, and the genre's established games offer far
  more spatial and combinatorial freedom than a fixed nine-node vessel.

I would not choose this, but P1 is its first step, so the option stays open at no cost.

### C. Investigation first

Make the case the spine and let production shrink to what the case needs. The vessel stays; the
experiment's candidate mechanics (substitutable materials, direction-switching lines, tooling) are
dropped for good; evidence flows through the kernel as in P7.

- **For:** it puts effort where the hook is, and P7 shows the kernel can carry it.
- **Against:** it depends on a deduction mechanic that does not exist yet. Commit only after the
  paper prototype works.

This is where A leads if milestone 3 succeeds.

### D. Smaller vessel, shorter campaign

Whatever the direction, consider cutting the campaign's intended length and the facility count's
ceiling. A four-hour game with one tight case that players finish is a stronger first release than
a twenty-hour one whose systems thin out. The fixed, authored vessel already suits this.

## Recommended sequence

1. **Now, in parallel, mostly on paper:** P5 (controllers in rule cards), P6 (the mission
   determinism rule), P9 (pacing budget), P7's paper case, and P14 (a first playtest of the
   current build).
2. **Next build:** milestone 1, the drill bench (P1), with save and load and the briefing.
3. **Then:** milestone 2, the closed loop (P2, P3, P4). Re-run the drills afterwards; their
   scores will move, and that movement is the tuning data.
4. **Then:** milestone 3, the first case.
5. **Alongside, as documents only:** P11 and P12.

Deliberately *not* next: the program runtime, upgrades and sockets, the candidate scheduling
mechanics, and further planner intelligence. Each improves a game that does not yet have its
other half.

## Questions for the project owner

1. Is the investigation still the reason this game exists? If yes, is direction A or C the plan?
   If it has become a frame for an automation game, B should be chosen openly.
2. How long is the game meant to be, and how long is a session?
3. Should the player ever be unable to recover, or is the worst case always "slower"? P3 assumes
   the second.
4. Is programming meant to be necessary to finish, helpful, or a separate mastery tier?
5. Is the no-spare-parts rule a design conviction or an inherited engine constraint? (Issue 9.)
6. Is the top speed ×4 or ×30? The answer changes what a "long stable run" is.
7. Who is the player? Someone who enjoys programming puzzles, someone who enjoys management
   sims, or someone who enjoys detective games? The documents currently promise all three, and
   the three tolerate very different amounts of friction.

## What this review did not do

- It did not run the game. Feel, readability and pacing claims are inferences.
- It did not re-run any replay. Every figure quoted is from the dated reviews. E1, E2 and E3
  recorded situations A and C before K5c changed them; where a figure is from before K5c the text
  says so, and the K5c review's own figures are used for what came after. The proportions matter
  here, not the exact ticks.
- It did not review code quality, which `2026-10-20-codebase-architecture-review.md` covers.
- It did not cost the proposals in days. The cost lines are relative.
