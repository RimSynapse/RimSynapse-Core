# Future Issue: Centralize Opportunistic Task Triggering in Core

## Problem
Currently, opportunistic task triggering logic is split between `RimSynapse-Core` (the `OpportunisticTaskManager`) and `RimSynapse-Psychology` (which defines and registers the individual tasks like `TriggerOpportunisticMemory` and `TriggerOpportunisticVisitorBackstory`). This means each companion mod has its own scheduling logic, leading to duplicated patterns.

## Proposed Solution
1. **Centralize in Core:** Move all opportunistic scheduling, priority weighting, and execution into `RimSynapse-Core`'s `OpportunisticTaskManager`. Companion mods should only *register* their task callbacks and metadata.
2. **XML-Defined Tasks:** Each opportunistic task should be definable in XML (e.g., `OpportunisticTaskDef`) with fields for:
   - `defName` (unique identifier)
   - `priority` (numeric, higher = more important)
   - `isOpportunistic` (bool tag)
   - `cooldownTicks` (minimum interval between executions)
   - `weight` (relative chance of being selected when multiple tasks are eligible)
3. **In-Game Editor:** Build an in-game settings panel (similar to the mod settings menu) where users can adjust the `priority` and `weight` of each registered opportunistic task without restarting the game. This allows fine-tuning of how often backstory generation vs. memory processing vs. other tasks fire.

## Benefits
- **One codebase** for scheduling instead of two.
- **User-tunable** via XML defaults and in-game sliders.
- **Extensible** — any future companion mod just drops in an XML def and registers a callback.

## Status
**Deferred** — tackle after the current refactor and subsequent testing.
