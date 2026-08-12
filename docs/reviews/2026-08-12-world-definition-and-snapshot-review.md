# World Definition and Snapshot Review

Date: 2026-08-12

## Summary

The current `WorldDefinition` plus `WorldSnapshot` model is good enough for the prototype UI, but it is carrying too many responsibilities for the intended game. The main issue is not the snapshot shape; it is the absence of an explicit serializable `WorldState` between static content and the UI projection.

## Issues

- `WorldDefinition` mixes catalog data, scenario seed data, opening inventory, opening queues, ids, and player-progress-like unlock state.
- `SimulationEngine` owns live world state in private fields, so stock, queues, task progress, tick, event history, and counters are not directly serializable.
- `SchematicCatalog` stores unlocked schematics, which makes player progress part of the static rulebook.
- Extractor and transport definitions are read during execution, which makes upgrades and per-save instance changes awkward.
- `WorldSnapshot` is a useful read model, but it lacks longer-window telemetry the GDD expects: utilization, blocked-cycle percentages, root-cause priority, alerts, readiness, damage/degradation, compute/fuel causes.
- Labels in snapshots are fine as projections, but labels must not become save state except as player name overrides.
- The million-run initial tasks and billion-unit transfers are standing-order placeholders; they should eventually become explicit state or authored automation.
- The emergency extractor is modeled as an ordinary queued executor, but design says it is passive and not commandable by player programs.

## Recommended Direction

- Keep `WorldSnapshot` as the immutable UI projection.
- Introduce `ContentCatalog`, `Scenario`, and serializable `WorldState`.
- Move unlocks to `WorldState.Progress`.
- Split facilities, storages, and transport lines into archetypes plus instances.
- Store only ids, deltas, player overrides, topology, and genuinely dynamic values in state.
- Add save/load determinism tests before expanding programming, missions, upgrades, or alerts.

## Suggested First Steps

- Add `WorldView.IsUnlocked(SchematicId)` so planner code stops depending on catalog-owned progress.
- Sketch a `WorldState` DTO around current engine fields before changing behavior.
- Add guard tests for snapshot reconstruction and save/load determinism.

---

Transcription note: a few words were inferred from the photo where glare and blur reduced legibility.
