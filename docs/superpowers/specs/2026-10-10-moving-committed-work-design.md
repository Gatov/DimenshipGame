# Moving Committed Work — Design

Date: 2026-10-10
Status: Decided (K6d, #76)

## Goal

The scheduling design's §5 lists *assign eligible work* as a control. Before approval the
composer's facility picker provides it (`SetExecutor`). After commit, nothing does. A facility built
in the middle of a campaign cannot take work already bound to another. E2 found no command a
controller could use for it, and E3 made it follow-up 2
(`docs/reviews/2026-10-10-e3-experiment-report.md`).

This adds one engine command that moves a committed plan's unstarted production to another
facility, with its legs rerouted.

## Source material

- `docs/production scheduling and automation - gameplay design.md` §5: *assign eligible work* is a
  player control for configurations, capabilities, queues and setup costs.
- `docs/superpowers/specs/2026-10-09-kernel-command-surface-design.md`: commands are values,
  `Execute` is the one door, and every command validates before it mutates.
- `docs/superpowers/specs/2026-09-24-demand-objectives-and-material-claims-design.md` (D3),
  Decisions 6 and 7. Hold leaves what is physically committed alone, and cancel and amend change a
  task only by truncation.
- CLAUDE.md, the hold-only rule. The planner's supply is the hold's free stock, and nothing outside
  it is netted.

## Decisions

### 1. One command: a plan, a schematic and a facility

```csharp
public sealed record MoveWork(PlanId Plan, SchematicId Schematic, ExecutorId To) : Command;
```

It moves the plan's unstarted runs of that schematic from every other facility to `To`.

- **By schematic, not by task.** A task inside a plan is never commanded alone, the rule hold and
  cancel already keep. The schematic is also the granularity of the composer's picker and of a
  script's `assign`, so one decision means the same before and after commit.
- **A count of runs was left out.** Splitting a stage between two facilities is a real move, but
  nothing measured asks for it yet. It is listed under *Not built*.

### 2. Only work whose material has not left its source moves

A run moves only if its inputs have not been loaded toward the old facility, and the leg that would
carry its output away has not been loaded either. For each old facility, the runs moved are the
least of:

- its unstarted runs of the schematic for this plan. A run in progress, or one awaiting deposit,
  never moves.
- for each input, the plan's unloaded inbound remainder to that facility's buffer, divided by the
  input per run;
- for the output, the plan's unloaded outbound remainder from that buffer, divided by the output per
  run.

Two alternatives were rejected:

- **Moving runs whose input is already in the old buffer.** On the shipped vessel no line joins two
  factory buffers that are built, so the input would be stranded. Ordering it again from the hold
  would spend stock twice, outside the hold-only accounting.
- **Moving runs while letting the input follow later.** That would need a buffer-to-buffer leg the
  planner does not make for ordinary items.

The rule is the one hold and cancel keep: what is physically committed finishes where it is.

A facility switching over toward a task whose runs all move still completes the switch-over and
then finishes the task (D1 Decision 4). That changeover is wasted. It is the cost of a late move,
and the player can see it.

### 3. Legs are cut and appended, never redirected

Committed tasks change only by truncation, the rule cancel and amend keep.

- **Old tasks are cut.** The old facility's produce task loses the moved runs. Its inbound and
  outbound transfers lose the moved quantity, last task first, never below what each has loaded.
  A task cut to nothing finishes by the ordinary completion path, as a cancel's does.
- **New tasks are appended to the plan.** They are:
  - one produce task at `To`;
  - one inbound transfer per source and item, from the storage the cut legs came from;
  - one outbound transfer per destination and item, to the storage the cut legs went to.

  The plan keeps its id, priority and held flag.
- **Line choice is the planner's route rule.** It takes the least queued transfers, then the
  highest throughput, then declaration order. Each leg added counts toward its line's load for the
  next.
- **A missing line refuses the command,** naming the route. This is what refuses moving the
  revisit chain's forming or finishing off Factory Alpha, the only factory with treatment lines. It
  also refuses moving a reactor stage that receives a workpiece from a buffer it has no line from.

### 4. Claims need no settling

An amend trims the plan's holdings to its new need afterwards. A move does not need to:

- **At the old buffer**, need and inbound fall by the same quantity, because only runs whose
  material is unloaded move.
- **At the new buffer**, both rise by the same quantity.
- **In the hold**, the new inbound legs draw what the cut ones would have, so neither changes.

No holding can exceed its need afterwards, and no outstanding claim is created. The first draft of
this spec shared amend's settlement. Tests with the settlement removed still kept the invariant on
every tick, including one with free stock already in the old buffer. That is how the argument above
was found, and the call was dropped as dead code.

### 5. Refusals, all before any change

- no active plan;
- `To` is not a production facility, unbuilt, passive, or of the wrong type for the schematic, or
  the schematic is locked. These use the sentences `Enqueue` already uses;
- the plan has no unstarted runs of the schematic anywhere but `To`;
- none of those runs can move, because their material has already been sent. The sentence says
  so;
- a leg has no built line.

### 6. No new state

The command changes task scripts and appends tasks, both of which a save already carries. There is
no save version change. It emits `EventCode.WorkMoved`, appended last, carrying `plan`, `runs` and
`tasks`; its subject is `To`.

### 7. The replay can issue it

A script command `move` names a demand, a `schematic` and a `facility`. As with `assign`, a
facility whose type cannot run the schematic is a parse error. An accepted move is one
intervention.

## Not built

- **A control in the Operations view.** The command is reachable from the harness and from
  controllers. The plan detail's controls (U1) gain it in a shell ticket.
- **Splitting a stage**, that is, moving some runs and not all.
- **Moving material already sent** to the old buffer.
- **Moving transfers alone**, onto another line of the same route.
- **A measurement on C.** C is held out, and scheduling it by hand would be tuning on it.

## Acceptance criteria

- A plan's unstarted runs move, and its legs follow. The moved work completes at the new facility,
  and the plan completes with the same delivered quantity.
- A run in progress stays and finishes at the old facility. So does a run whose input is already
  loaded.
- Every refusal changes nothing, and `ClaimInvariantViolations` is empty after an accepted move.
- Save, load and advance equals advancing straight through after a move.
- The replay's `move` parses, links and counts as an intervention.
