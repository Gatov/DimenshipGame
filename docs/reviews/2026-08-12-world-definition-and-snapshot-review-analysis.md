# World Definition and Snapshot Review — Analysis

Date: 2026-08-12
Status: Complete
Reviews: `2026-08-12-world-definition-and-snapshot-review.md`
Amends: `docs/superpowers/specs/2026-08-10-static-content-and-world-state-design.md`

## What this is

The review reads the `WorldDefinition` + `WorldSnapshot` model and lists eight issues, six recommended
directions, and three first steps. This document checks every one of them against the code and against the
specification that already covers most of the ground, and takes a position on each.

The headline is that **six of the eight issues are already decided**, in
`docs/superpowers/specs/2026-08-10-static-content-and-world-state-design.md`, in more detail than the review
asks for — and that spec is **written and entirely unimplemented**. There is no `content/` directory, no
`WorldState`, no save or load of simulation state anywhere in the repository, and no plan under
`docs/superpowers/plans/` for building any of it. The only serialisation that exists is UI layout
(`src/Dimenship.Shell/LayoutSerializer.cs`).

So the review is not wrong. It is largely a rediscovery, arrived at independently, which is a useful
corroboration and a poor use of the next work session. What is worth acting on is the remainder: **two
genuinely new findings**, and **one place where the review's aim is better than the spec's** — issue 5 lands
on a contradiction the spec has been carrying since its second amendment.

## Verdicts

| # | Review issue | Verdict | Where it stands |
| :--- | :--- | :--- | :--- |
| 1 | `WorldDefinition` mixes tiers | **Upheld, already specced** | Four-tier model; the Migration table deletes the record outright |
| 2 | Engine owns live state in private fields | **Upheld, already specced** | Part 2 `WorldState`; *Ownership and the seam* |
| 3 | `SchematicCatalog` stores unlocks | **Upheld, already specced** | *Progress is not catalog* → `WorldState.Progress`. See the sharpening below |
| 4 | Definitions read during execution | **Upheld, already specced** | *Archetype and instance*. The spec names this exact defect in the same terms |
| 5 | Snapshot lacks longer-window telemetry | **Upheld — and it exposes a contradiction** | See *The snapshot contradiction* |
| 6 | Labels must not become save state | **Upheld, already specced** | `NameOverride`; the spec's amendment log records catching this in its own first draft |
| 7 | Million-run tasks, billion-unit transfers | **New, and understated** | Not cosmetic: the count is arithmetic in `Uncommitted`, and the planner is reading wrong numbers today. See *Standing orders* |
| 8 | Emergency extractor is passive by design | **New, and time-sensitive** | See *The passive extractor* |

All six *Recommended Direction* bullets are likewise already decisions in the spec — `ContentCatalog`,
`Scenario`, serialisable `WorldState`, unlocks in `Progress`, the archetype/instance split, the
ids-and-deltas rule, and save/load determinism tests as acceptance criteria. The spec goes further on the
last three than the review does: the split extends to storages and reactors as well as facilities, the
ids-and-deltas rule is written out as a five-row table of what state may hold, and the determinism test is
one of five named guards.

### One sharpening on issue 3

The review calls the unlock set *"player progress inside the static rulebook"*, which is right about the
shape. It is worth adding that the defect is currently **inert**:
`src/Dimenship.Core/Production/SchematicCatalog.cs:17` declares `_unlocked` as a private `HashSet` with **no
mutator** — no `Unlock`, no event, no persistence — and `WorldDefinition.CreateDefault()` unlocks all seven
schematics, so nothing in the shipping world is ever locked.

That makes it latent rather than live, which is an argument for doing it *sooner* rather than later: it is
the one item on the review's list that can be moved without changing a single behaviour, and every week it
stays is another week of planner code reaching through `IWorldView.Schematics` to a catalog that should not
have the answer.

## The snapshot contradiction

Issue 5 is the review's most valuable point, because the specification disagrees with itself about it.

As the spec stood before this analysis, it said the snapshot was untouched, twice:

> This spec changes what is *behind* the snapshot, not the snapshot itself. `WorldSnapshot` keeps its shape
> and its contract: replaced wholesale, never mutated, reference equality is an exact change test. No shell
> code changes because of this spec.
>
> — *Relationship to prior specs*

> | Snapshot | Unchanged. No shell change results from this spec |
>
> — *Decisions*

And then, in *Utilization is measured, so it is saved*:

> `WorldSnapshot` gains the derived percentages, not the buckets. The shell should never see a ring.

(All three quotations are from the spec as of commit `f6d8474`. The first two no longer read that way — the
amendment described below is what changed them.)

Both cannot hold, and the history explains which one gave way. The "unchanged" claim was true for the spec's
**original three-tier scope**: move data out from behind the projection, change nothing in front of it. It
stopped being true in the **2026-08-10 amendment**, which added utilization windows, a compute ledger,
facility integrity and an alert ledger. Every one of those exists *in order to be shown*. §5.6's node
inspector reads *utilization 70%, input wait 31%, power throttling 0%, output blocked 4%*; §12 makes it a
rule that a percentage without a cause category is not enough. State that is measured, saved, and never
projected is dead state, and the amendment that added it did not go back and correct the two sentences that
said the projection would not move.

The review's list is accurate against the code. `src/Dimenship.Core/Simulation/WorldSnapshot.cs` has no
utilization, no compute, no integrity, no alerts, no readiness, and a bare `PostponeReason?` with no
declared priority — so nothing on it can answer "why is this factory stalled" with the *highest-priority*
true reason rather than merely a true one.

**The state halves are all specified. The projection half is unowned.** That is the gap, and it is a gap in
the spec rather than in the review.

### Resolution

The distinction the spec needs is between the snapshot's *contract* and its *shape*:

> The snapshot's **contract** is unchanged — immutable, replaced wholesale, reference equality is an exact
> change test, derived values allowed and encouraged. Its **shape is extended, additively.** No existing
> field changes type or meaning, so no existing panel changes; new fields are read by new surfaces.

Stated that way, both of the spec's claims survive in their true form, and the promise becomes checkable
rather than aspirational. The amendment names what the snapshot gains — utilization percentages and
integrity on `ExecutorState`, a `ComputeState` beside `EnergyState`, an alert list, and the declared total
order on `PostponeReason` that makes every surface agree on root cause — and leaves the panels that read
them to the diagnostics work.

## Standing orders

`src/Dimenship.Core/Simulation/WorldDefinition.cs` seeds six tasks of 1,000,000 runs and ten transfers of
1,000,000,000 units, and says what they are (line 474):

> A stand-in for the standing order the specifications do not yet describe: enough runs that the vessel keeps
> working far longer than any session.

The spec carried them forward **unchanged** — renamed `ScenarioTask` and `ScenarioTransfer`, *"renamed for
the tier they belong to and otherwise unchanged"*, under *The scenario*.

The review files this under presentation: these *"should eventually become explicit state or authored
automation"*. **It is worse than presentation.** The run count is load-bearing arithmetic, and it is already
producing wrong answers.

`SimulationEngine.Uncommitted` (line 430) charges every unfinished task's remaining runs against the vessel —
`RequestedRuns - CompletedRuns`, times the schematic's inputs — and credits its remaining output the same
way. `ProductionPlanner.Spend` (line 128) takes `Math.Max(0, world.Uncommitted(item))` as what is available.
With a million runs outstanding on six facilities, using the default vessel's own numbers:

| Item | Aboard | `Uncommitted` computes | Consequence |
| :--- | ---: | ---: | :--- |
| Matter Mix | 3,600,000 | 3.6M − 2 × (1,000,000 × 4,000) ≈ **−8.0 billion** | Clamped to 0. Every plan that needs it reports `ShortageKind.RawResource` |
| Robot Frame | 0 | 1,000,000 × 50 = **+50,000,000** | A goal of *4 robot frames* is met from phantom stock; the plan returns no runs and no transfers |

Both directions are wrong and both are the same mistake: a task that means *forever* counted as a finite
claim on material. Nothing catches it — `ProductionPlannerTests` and `WorkedExampleTests` build worlds
through `WorldBuilder`, and the four `DefaultVesselTests` exercise production rather than planning, so no
test ever asks the default vessel to plan anything.

> **Caveat on this finding.** There is no .NET SDK in the environment this analysis was written in, so this
> is derived from the code and the content numbers rather than observed from a run. The arithmetic is small
> enough to check by eye and the citations are exact, but it should be confirmed by the test named below
> before anyone acts on it — and that test is worth having permanently either way.

The review's remedy is right, in the right order: **explicit state is the mechanism, authored automation is
the policy.** A standing order is what an executor does absent instruction; a program is what changes it.
Building it the other way round — a preset program whose job is to re-enqueue a finite task forever — makes
the vessel's baseline behaviour depend on a program being installed and enabled, which is the fragility the
GDD's recovery invariant exists to rule out.

**Recommendation:** `Runs` becomes `int?` and `Quantity` becomes `long?`, null meaning indefinite. Null then
needs a defined answer in the three places the count is arithmetic: `Uncommitted` counts an indefinite task's
run in flight and nothing beyond it; planner load treats the facility as occupied rather than summing a run
count; and the snapshot's task projections carry the null through, where it is the difference between a
progress bar and a running total.

That last one is a **breaking change to two existing snapshot fields** and to the one panel that renders them
(`FacilityInspectorPanel.cs:120,160`). It is the single place where this work is not additive, and it is
worth it: the alternative is a progress bar that is permanently at 0.4%.

## The passive extractor

The GDD says this three times, and once as a design rule:

> The operational schematic shows only facilities the player can meaningfully schedule, prioritize, automate,
> or optimize.
>
> — §5.8, design rule

> | Emergency Hydrogen Extractor | Passive orbital collection of hydrogen. | **None. It is not an automation
> node and cannot be disabled by player programs.** | 1, passive |
>
> — §5.8, facility table. The paragraph beneath adds that it is *"the one entry in this table that the player
> does not schedule"* and appears *"as a read-only source"*.

> The extractor is not part of the automation graph and cannot be disabled by player programs.
>
> — §5.9

The code models it as an ordinary queued facility with a schematic, a work rate, a switch-over time and a
million-run task, and the comment at `WorldDefinition.cs:357` knows it:

> Passive by the GDD's rule — no player program may disable it — and modelled as an ordinary facility because
> nothing yet can command any facility at all. What keeps it honest is that it is configured once, here, and
> never reconfigured.

**That defence was correct when written and is not correct now.** `2026-08-11-programming-view-design.md`
has since landed, and `ProgramInstance.TargetId` is *"the facility, array, dock or robot group it runs on"*.
The moment programs become installable, the extractor is commandable **by construction** — not through
anyone deciding it should be, but through it being a facility in a list of facilities. The rule then breaks
silently, which is the failure mode worth spending something to avoid: an invariant that fails loudly gets
fixed, and one that fails by a target-picker listing one extra row does not.

**Recommendation:** `FacilityArchetype` gains a commandable flag, false for the extractor, enforced at the
two points the spec already runs validation — a scenario may not queue a task on a passive facility, and a
`ProgramInstance.TargetId` may not name one.

The alternative, a separate passive-source type outside the executor family, is worse. The extractor still
needs a work rate, a buffer, an energy draw, a status and a block reason — everything an executor has — so a
parallel type duplicates the whole executor to carry one boolean, and the two would drift. The GDD's
constraint is about **who may command it**, not about what it is, and a flag is the shape of that constraint.

### The two findings resolve together

They are one finding seen from two sides. A passive source running an indefinite standing order **is** the
GDD's read-only source: it gathers, always, and nobody schedules it. Expressed as a player-scheduled
million-run job on an ordinary facility, it is two misstatements that happen to cancel — the task never
finishes, so the facility never idles, so nothing ever reveals that the player was supposedly the one who
ordered it.

Fixing either alone leaves the other visible. `Runs: null` on a commandable facility still puts the extractor
in the program target list. A passive flag with a million-run task still shows a player order the player did
not give. Together they produce what the GDD describes, and neither is expensive.

## Beyond the review

Two defects the review did not reach, both of which the spec **inherits** rather than fixes.

**Task lists are append-only.** `_tasks` and `_transfers` (`SimulationEngine.cs:30–31`) are only ever added
to — lines 244 and 312 — and nothing removes a completed task. Three consequences compound:
`BuildSnapshot` projects **every task ever queued** (line 1119) on every rebuild; `Uncommitted` scans all of
them on every call (line 434), and the planner calls it per item per expansion step; and the spec's
`TaskRegistry` is specified as *"every task, by id"* with no retirement policy, so a save inherits the growth
permanently.

Nothing today makes this visible — the default vessel queues sixteen tasks that never complete — but the
first long session with a player committing plans is a snapshot rebuild that grows without bound. The spec
already has the precedent for the fix: it bounds the journal at 512 and names the trigger for revisiting
that bound. Completed tasks need the same treatment, and the decision has to be made before version 1 of the
save format rather than after.

**`IWorldView.Hold` is an array index.** `SimulationEngine.cs:373` reads `_definition.Storages[0].Id` — the
storage every plan routes material through, selected by being declared first. Nothing enforces it and no test
covers it. Appendix 1 fixes the vessel at exactly one global Resource Storage, which makes this a genuine
named concept, so it should be a named field on `Scenario` rather than a convention that a content author can
break by reordering a JSON array.

## First steps, adjudicated

**1. `IWorldView.IsUnlocked` — endorse.** Buildable today, and already in the spec's Migration table. Worth
being clear that it is a **seam, not a fix**: until `ProgressLedger` exists it delegates straight to the
catalog, and the unlock set is still in the wrong place. What it buys is that every call site stops naming
the catalog, so when progress does move, the change is one method body rather than a search across the
planner. That is the whole value, and it is worth having — but it should not be mistaken for closing issue 3.

**2. "Sketch a `WorldState` DTO around current engine fields" — reject as written.** `WorldState` is already
specified, across roughly four hundred lines, in far more detail than a sketch would add. Worse, sketching it
*around current engine fields* would reproduce the defect the spec exists to remove: the engine's fields hold
`Definition` references and read `WorkRatePerTick` at tick 500, and a DTO shaped around them inherits that
coupling into the save format, where it is expensive to remove. The archetype/instance split is not a
refinement of the current field layout; it is a different one.

The right first move is the **content layer**, because `State` references content ids and cannot be built
against a rulebook that does not exist yet. That is also the order the spec's own Layering section implies:
`Content` must not reference `State`, so `Content` can be finished first and `State` cannot.

**1a. Ahead of all three: plan something against the default vessel.** One test, buildable today, needing
nothing from this spec:

```
var engine = new SimulationEngine(WorldDefinition.CreateDefault());
var plan = ProductionPlanner.Plan(new ItemAmount(WorldDefinition.RobotFrame, 4), engine);
```

If the *Standing orders* arithmetic above is right, that plan comes back empty — the goal satisfied from
fifty million robot frames that do not exist — and a Matter Mix goal comes back as a raw-resource shortage
against a hold containing 3.6 million of it. The test is cheap, it either confirms a live defect or retires
a claim in this document, and the gap it covers is real regardless of the outcome: **nothing currently
plans against the shipping world.** Every planner test builds its own.

**3. Guard tests — endorse in part.** Save/load determinism tests cannot precede save and load; the review's
own recommended direction says to add them *before expanding programming, missions, upgrades or alerts*,
which is the right gate but not something available on day one. The spec's five acceptance tests are the
right set, and one of them is free early: **guard test 4**, a reflection test asserting that no type under
`State/` has a field whose type is declared under `Content/`, can be written the day those two namespaces
exist and costs nearly nothing. It is also the test that would have caught the `CapacityPermille` and three
`Label` leaks the spec's own first draft shipped with.

## What this changes

The spec is amended in four places — the snapshot contradiction resolved, standing orders and passive
facilities decided, and the two inherited defects recorded. Nothing here changes code.

The order the analysis implies, shortest to longest:

1. **The default-vessel planning test** (*1a*). Today, no dependencies, and it settles whether the standing
   orders are a live defect or only a latent one.
2. **`IWorldView.IsUnlocked`** (*1*). Today, one method, a seam rather than a fix.
3. **The content layer.** `Content` cannot reference `State`, so it can be finished first and `State`
   cannot.
4. **`WorldState` and the seeder**, then **save/load**, then the two guard tests that keep the tiers honest.
5. **The projection fields issue 5 asks for**, once there is state behind them to project.

Nothing in 3–5 should start before 1 answers its question, because a planner that reads wrong numbers today
will read the same wrong numbers through a `ContentCatalog`.
