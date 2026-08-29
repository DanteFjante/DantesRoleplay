---
id: procedure.trail-survival.simulation
category: trail-survival.simulation
name: Execute one Trail Survival command
governs: deterministic Trail Survival root command evaluation and state transition
status: active
---

## Description

Defines how an exact Trail Survival mechanic derives and proposes one atomic canonical transition
from a pinned scenario, declared ECS state, closed player input, and the recorded action seed.

## Instructions

1. Resolve the exact active mechanic fingerprint and exact application/state-space component types.
2. Materialize only declared state and the scenario referenced by the canonical scenario pin.
3. Validate phase, pending/terminal state, closed command input, scenario parity, and expected seed.
4. Derive all costs, rolls, changes, arrivals, and outcomes in the catalog JavaScript mechanic.
5. Apply the complete generic typed-effect proposal through one root transaction and audit record.

## Constraints

- Callers may choose only values explicitly admitted by the command; they never provide a resolved
  roll, price, cost, delta, distance, arrival, eligibility result, or outcome.
- A pending choice blocks every mechanic except event choice; a finished run blocks all commands.
- Never place Trail Survival identifiers, formulas, or branching in generic C#.
- A failed, stale, replay-conflicting, or partially invalid proposal changes no canonical state.
