---
id: procedure.trail-survival.run-state
category: trail-survival.run
name: Inspect Trail Survival run state
governs: authoritative component state for a Trail Survival run
status: active
---

## Description

Identifies the application-owned components that form canonical Trail Survival run state and the
boundary between stored facts and later rule-derived projections or transitions.

## Instructions

1. Resolve component types from the exact application and state-space binding.
2. Read scenario, run, clock, route, party, asset, decision, and outcome components as canonical
   only when they validate against their exact registered schema versions.
3. Treat absent optional components according to their authored meaning instead of inventing empty
   sentinel values.

## Constraints

- Do not infer or apply a game transition from these state shapes alone.
- Do not store derived summaries, percentages, totals, prices, rates, or available actions as
  canonical run-domain facts.
- Do not use another application's component type, entity, or state space as Trail Survival
  authority.

