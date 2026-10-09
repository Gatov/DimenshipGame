# Kernel Command Surface — Design

Date: 2026-10-09
Status: Decided (C0)

## Goal

Give every way of changing the world, other than the passage of time, **one entry point** that the
shell, the replay harness and every future controller call in the same way. A player must have no
command a program can never have, and a program must have none the player lacks.

The output is this document, `SimulationEngine.Execute`, the shell's route through it, and command
actions in replay scripts. It gates U1 (Operations controls) and E2 (reference controllers).

## Source material

`docs/superpowers/plans/2026-09-24-production-scheduling-and-automation.md`, Decision 5:

> **One command surface for players and controllers (C0).** Set priority, hold, release, allocate,
> amend demand and recover are kernel commands with one entry point. `ShellActions` calls it and a
> future controller calls the same thing. Building the Operations controls before this would give
> the player commands a program can never have, which is the parity the design's §5 forbids.

`docs/production scheduling and automation - gameplay design.md`, §5: *"Both interfaces need the
same bounded commands and explanations."* And: *"Manual overrides, competing program commands,
tie-breaking, and starvation treatment must be deterministic and visible."*

`docs/superpowers/specs/2026-09-24-demand-objectives-and-material-claims-design.md` (D3), *What
changes, by ticket*, C0: the surface gains `SetPlanPriority`, `HoldPlan`, `ReleasePlan`,
`CancelPlan`, `AmendPlan`, `Relinquish` and `Reassign`.

## What exists today

Every command K2 to K6c added is a public method on `SimulationEngine`: `Enqueue`, `Commit`,
`SetPriority` (task and plan), `Hold`, `Release`, `Cancel` (plan and task), `Amend`,
`Relinquish` and `Reassign`. A bad argument throws `ArgumentException`. The shell reaches exactly
one of them, `Commit`, through `SimulationDriver.Commit`, which turns any exception into a kernel
fault that stops the simulation. The replay harness reaches `Commit` and `SetPriority(PlanId)`
directly.

That is two problems. A stale command, such as cancelling a plan that completed a tick earlier, is
an ordinary event for a controller, and today it would fault the game. And nothing makes the
shell, the harness and a controller go through the same door.

## Decisions

### 1. A command is a value, and `Execute` is the one door

`Command` is an abstract record with one sealed record per command, in
`Dimenship.Core/Simulation/Commands.cs`. `SimulationEngine.Execute(Command)` dispatches it to the
existing method and returns a `CommandResult`. The methods stay public, because the kernel suite
tests them directly. The rule is about callers outside the kernel: **the shell, the harness and a
controller call `Execute` and nothing else** to change the world.

| Command | Does |
| :--- | :--- |
| `OrderGoal(goal, destination?, assemblyTarget?)` | Plans the goal against the live world through the draft path, then commits it. A controller's way to add demand. |
| `CommitPlan(plan)` | Commits a plan already approved. The composer's way, because a player's draft may carry edits. |
| `QueueTask(script, executor)` | Queues one task by hand. |
| `SetPlanPriority(plan, priority)` / `SetTaskPriority(task, priority)` | Priority (K2, K6a). |
| `HoldPlan`, `ReleasePlan`, `CancelPlan` (plan) | K6c. |
| `HoldTask`, `ReleaseTask`, `CancelTask` (task) | K6c, for a task queued by hand. |
| `AmendPlan(plan, quantity)` | K6c. |
| `RelinquishStock(plan, storage, item, quantity)` | K6b. |
| `ReassignStock(from, to, storage, item, quantity)` | K6b. |

*Rejected: a string verb with an argument bag*, the shape a console or a future program language
would parse. It defers every type error to run time, and the parser belongs to whichever of those
lands first. Each can map its text onto these records.

*Rejected: an interface the engine implements*, `ICommands`. It is the same list of methods with a
second name, and it cannot be put in a script, logged or compared. A record can.

### 2. A refusal is an answer, not a fault

`CommandResult` is either `CommandAccepted` or `CommandRefused`. A refusal carries the sentence the
method would have thrown and, for a draft the planner refused, its issues. `Execute` turns an
`ArgumentException` (the kernel's way of saying "this command makes no sense against this world")
into a refusal. Any other exception is a kernel fault and propagates, as it does from `Advance`.

That is safe only if **a refused command changed nothing**. Every command therefore validates
before it mutates:

- The K6 commands already do. They look up an active plan or a hand-queued task, then act.
- `Commit` validated each task as it enqueued it. A plan whose third task was invalid left the first
  two queued with no plan. It now validates every task first, and enqueues only if all pass. The
  same split serves `Enqueue`.
- `Amend` cuts the plan back before planning. If the replanned tasks fail validation, it restores
  the cut scripts and refuses, as it already did for a refused draft.

*Rejected: catching every exception.* A kernel invariant broken mid-tick is not a refused command,
and reporting it as one would let the game run on in a state nobody can trust.

### 3. Commands are inputs, not state

A command is applied between ticks, at the tick the caller chooses, exactly like `Advance`. The
kernel does not log commands, and a save does not carry them. The world they leave behind is what
the save holds, and that is enough to resume. The journal already records each accepted command's
effect (`PlanCommitted`, `PriorityChanged`, `Held`, `Cancelled` and the rest), and a refused
command emits nothing, because nothing happened.

Replay is where the sequence of commands is kept: the script is the input. Two runs of one script
apply the same commands at the same ticks, and stay byte-identical.

### 4. The shell routes through the driver, and only through it

`SimulationDriver.Execute(Command)` is the shell's door. It refuses while the kernel is faulted,
turns a kernel fault into the driver's existing fault state, and checks for a plan completed by the
command, as `Commit` did. `SimulationDriver.Commit` goes, and APPROVE becomes
`Execute(new CommitPlan(plan))`. `ShellActions.Execute` binds it, so a panel built by U1 calls one
action whatever the command. Drafts stay outside the surface: `Draft`, `Adjust` and `Approve` change
nothing, and a proposal is not a command.

### 5. Replay scripts can issue commands

A script gains `commands`, each at a tick, naming the demand whose plan it acts on. Plan ids are
never written in a script, because they depend on how many plans came before:

```json
{ "tick": 300, "command": "hold", "demand": "upgrade-components" }
{ "tick": 900, "command": "release", "demand": "upgrade-components" }
{ "tick": 300, "command": "priority", "demand": "expedition-3", "priority": "High" }
{ "tick": 400, "command": "amend", "demand": "upgrade-components", "quantity": 500 }
{ "tick": 400, "command": "cancel", "demand": "upgrade-components" }
{ "tick": 400, "command": "reassign", "demand": "upgrade-components", "to": "expedition-3",
  "storage": "resource_storage", "item": "component", "quantity": 200 }
{ "tick": 400, "command": "relinquish", "demand": "upgrade-components",
  "storage": "resource_storage", "item": "component", "quantity": 200 }
```

Demands are applied before commands at the same tick, so a command may act on a demand committed
that tick. The report's **interventions** count accepted demands plus accepted commands, as the M2
table defines. A refused command is listed with its reason, and is not counted. A demand's
`priority` field is kept, and it now goes through `SetPlanPriority`.

This is not a controller. A controller reads the world and decides. A script decides in advance,
which is what lets E1 pin a situation and E2 compare policies against it.

## Not built

- **Recovery** (D4, K7). The plan's Decision 5 lists it; it joins the surface when K7 lands.
- **Protecting a reserve** (D3, *Not built*). It joins when a controller needs it.
- **Pre-emptive setup**, switching a facility toward work whose inputs have not arrived (D1, *Not
  built*). It is a command by D1's decision, and nothing asks for it yet.
- **A controller hook in the harness.** E2 adds one, written against `Execute`.
- **Shell controls.** U1 builds them on `ShellActions.Execute`.

## Acceptance criteria

- A refused command leaves the world byte-identical, including a `CommitPlan` whose last task is
  invalid.
- A stale command, such as cancelling a completed plan, is refused, and the shell keeps running.
- APPROVE reaches the kernel through `Execute`.
- A replay script holds and releases a plan by demand id, and its report counts the interventions.
