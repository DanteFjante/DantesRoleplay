---
id: procedure.mechanic.dnd2024.conditions.guard
category: ruleset.dnd2024.core.state.conditions
name: Guard D&D 2024 creature-condition state
governs: commit(kind: "mechanic") authoring mechanic.dnd2024.conditions.guard; commit(kind: "subscription") registering its guard subscription
status: active
---

## Description

Owns the pre-commit structural validation of every proposed add or replacement of
`dnd2024.conditions`. It makes the closed condition-list invariant available to normal writers and
event reactions without giving a guard authority to alter the proposed world change.

## Instructions

1. Declare guard mode for exactly `world.component.added` and `world.component.replaced`, projecting
   `dnd2024.conditions` from the entity named by the structural event.
2. Return only allow, or deny with a stable code and a concise reason. It produces no effect, event,
   notification, child result, random result, or rewritten condition list.
3. For this component, require the fixed outer shape and source reference; the Feature 13 condition
   vocabulary; canonical ordering; non-Exhaustion uniqueness by `(condition, sourceEntityId)`;
   mutually exclusive Petrified and Poisoned state; and at most one source-free Exhaustion entry at
   integer level 1 through 6.
4. A non-Exhaustion entry may have a nonempty, trimmed source identity but never a level. An
   Exhaustion entry has only its condition and level. Source identity is historical provenance:
   do not require its entity to remain available.

## Constraints

- Two global-scope subscriptions (one for each structural event type) filter on
  `definitionId: "dnd2024.conditions"`, so changes to another component do not invoke this guard.
- Any malformed or unavailable guard denies the whole enclosing transaction under the kernel's
  fail-closed event-guard rules.
- This guard validates state only. Feature 13's normal writer remains the owner of action inputs and
  source-role admission; Feature 17's later reactions may rely on this guard instead of duplicating
  the complete list invariant.
