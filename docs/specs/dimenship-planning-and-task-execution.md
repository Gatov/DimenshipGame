# Dimenship Planning and Task Execution Specification

## 1. Purpose

Production planning converts a player goal, such as **Produce 4 Armor Plates**, into a coordinated set of production, transport, and acquisition requirements.

Planning determines what must be done. Runtime executors—factories, refineries, transport lines, mission docks, and similar systems—decide which queued task they can perform at each moment.

## 2. Production Planning

When the player creates a production goal, the planner recursively expands the selected recipe and determines:

- Required materials and components.
- Quantities already available in storage.
- Quantities expected from existing production.
- Quantities that must be produced.
- Transfers required between storage and facilities.
- Missing resources that cannot currently be supplied.

Example:

Goal: Produce 4 Armor Plates

Required:
- Alloy: 25
- Chips: 15

Available:
- Alloy: 5
- Chips: 10

Additional production:
- Alloy: 20
- Chips: 5

Raw material required for alloy: 60  
Raw material available: 19  
Raw material missing: 41

The planner then selects compatible executors and creates runtime tasks:

Refinery A
- Produce 20 Alloy

Factory C
- Produce 5 Chips

Armor Factory
- Produce 4 Armor Plates

Transport system
- Transfer 60 Raw Material: Storage -> Refinery A
- Transfer 20 Alloy: Refinery A -> Storage
- Transfer 25 Alloy: Storage -> Armor Factory
- Transfer 15 Chips: Storage -> Armor Factory

The plan may be accepted even when it cannot currently be completed. In that case, the player is notified of unresolved shortages.

Warning:

41 Raw Material is missing.

Suggested action:

Add a raw-material expedition to this plan.

An expedition is optional. The available portion of the plan may begin immediately.

## 3. Plan Versus Task

A plan is the high-level description of how the goal may be fulfilled.

A task exists only after it has been assigned and injected into a particular executor’s queue.

Planning data may contain proposed requirements before commitment, but these are not runtime tasks and do not have execution states.

Once the player commits the plan, all generated tasks are inserted into their corresponding executor queues. Each executor then independently selects work according to its availability, configuration, priorities, and task requirements.

## 4. Task States

Tasks use four primary states:

- Not Started
- Running
- Postponed
- Complete

### Not Started

The task is present in an executor’s queue but has not begun execution.

### Running

The executor is actively performing the task.

For production, this means a batch is currently being processed. For transport, it means material is currently being transferred.

### Postponed

The executor attempted to start or continue the task but could not perform it.

A postponed task records one or more reasons, such as:

- Insufficient input material.
- Required component unavailable.
- Destination storage full.
- Insufficient energy.
- Output route unavailable.
- Safety lock active.

The task also records when it was postponed and may retain a history of execution attempts.

Example log:

14:31:20 - Task selected  
14:31:20 - Task postponed  
Reason: 3 Raw Material available; 5 required for one batch

When conditions change, the executor may attempt the task again.

### Complete

The requested quantity or operation has been fully completed.

## 5. Executor State

Executor state is separate from task state.

A facility or transport executor may report:

- Running task X
- Switching over to task X
- No tasks queued
- All queued tasks blocked

A transport task therefore never reports "waiting for transport." The transport executor itself determines whether the task can run. If the required material is unavailable, the task becomes **Postponed** with an `InsufficientSourceMaterial` reason.

## 6. Runtime Task Selection

When an executor becomes available, finishes a batch, or encounters a blocked task, it evaluates its queue.

Default facility behavior:

1. Continue the current task when its next batch can run.
2. Otherwise, examine other queued tasks.
3. Prefer a runnable task using the current production configuration.
4. If another schematic is selected, enter switch-over.
5. After switch-over completes, start the selected task.
6. If no queued task can run, report `All queued tasks blocked`.

Task selection belongs to the executor, not to a globally fixed plan sequence.

## 7. Batch and Partial Execution

A production task does not require all materials for the entire requested quantity before starting. It begins when enough input exists for one production batch.

For example, a task requesting 20 Alloy may:

- Produce 6 Alloy
- Postpone because Raw Material is unavailable
- Resume when new Raw Material arrives
- Complete the remaining 14 Alloy

Transport tasks behave similarly. A request to transfer 60 Raw Material may immediately transfer the 19 units currently available and postpone the remaining 41 units until more material reaches the source.

This allows production and transport to operate concurrently rather than waiting for complete upstream jobs.

## 8. Switch-Over

Factories retain their current production configuration. Continuing the same schematic has no switch-over cost.

Changing to another schematic requires the executor to enter a **Switching over** state for a defined operational-time period.

The target task does not become **Running** until switch-over is complete.

This encourages continuous production:

- The current runnable task is preferred.
- Tasks using the same configuration are preferred.
- Switching occurs only when current work is complete or blocked.
- Unnecessary switching reduces facility throughput.

The planner creates and coordinates demand, while executors determine actual real-time execution. All decisions, postponements, switch-overs, shortages, and task attempts should produce structured telemetry for reports and SCADA diagnostics.