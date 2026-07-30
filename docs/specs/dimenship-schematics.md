# Dimenship Schematics Specification

> Transcribed from handwritten source pages. The source photographs are not held in this
> repository; this transcription is the authoritative copy.

## 1. Purpose

A schematic defines how a material, component, or item is produced.

The player can create a production plan only for outputs whose schematics have been unlocked. Schematics are normally discovered during missions; research may become another source later.

## 2. Schematic Definition

```csharp
public sealed record SchematicDefinition
{
    public required SchematicId Id { get; init; }

    // One execution of the schematic produces this quantity.
    public required ItemAmount Output { get; init; }

    // Materials and components consumed by one execution.
    public required IReadOnlyList<ItemAmount> Inputs { get; init; }

    // Determines production time together with the executor's work rate:
    // execution time = effort / facility work rate
    public required WorkAmount EffortPerRun { get; init; }

    // Base total energy consumed by one execution.
    // Energy and effort are independent: a low-energy item may take a long time.
    public required EnergyAmount EnergyPerRun { get; init; }

    // Determines whether the schematic is executed by a factory,
    // refinery, or another production facility type.
    public required FacilityType RequiredFacilityType { get; init; }
}
```

Example:

```text
Control Chip

Output: 1 Control Chip
Inputs: 1 Refined Silicon, 1 Conductive Material
Effort: 90 work units
Energy: 12 energy units
Facility: Factory
```

Facility upgrades may change work rate or energy efficiency without modifying the schematic.

## 3. Use in Planning

When the player requests an item, the planner expands the selected schematic recursively.

```text
Produce 4 Armor Plates
  ↳ use available Armor Plates
  ↳ determine missing quantity
  ↳ expand Armor Plate inputs
      ↳ use available Alloy and Chips
      ↳ create transport requirements
      ↳ expand known Alloy and Chip schematics for remaining deficits
```

Expansion stops when an input:

- is already available or allocated;
- can be produced using an unlocked schematic;
- is a raw resource that must be acquired;
- requires a schematic the player has not unlocked.

A resource shortage allows partial execution and may suggest an expedition. An unknown schematic prevents the planner from creating that production branch.

## 4. Production Task

```csharp
public sealed class ProductionTask
{
    public required TaskId Id { get; init; }

    // References the authoritative production instructions.
    // Inputs, output, effort, and energy are not copied into the task.
    public required SchematicId SchematicId { get; init; }

    // Number of schematic executions required.
    public required int RequestedRuns { get; init; }

    // The task is injected directly into a compatible executor's queue.
    public required ExecutorId ExecutorId { get; init; }

    public TaskState State { get; set; }
}
```

A facility can execute only schematics matching its `FacilityType`.

The facility remains configured for its last schematic while idle. Continuing the same schematic requires no reconfiguration; selecting any different schematic causes a full standard reconfiguration.

Multiple schematics may produce the same output. The player may select one directly, or later delegate the choice to an unlocked AI assistant.
