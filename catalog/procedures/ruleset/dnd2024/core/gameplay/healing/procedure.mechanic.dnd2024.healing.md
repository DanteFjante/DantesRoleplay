---
id: procedure.mechanic.dnd2024.healing
category: ruleset.dnd2024.core.gameplay.healing
name: Apply D&D 2024 healing
governs: commit(kind: "mechanic") validating D&D 2024 healing; commit(kind: "action") applying one bounded healing instance to authoritative Hit Points
status: active
---

## Description

Owns healing-caused increases to authoritative D&D 2024 Hit Points. It applies a positive requested
amount, clamps at the existing maximum, and declares the auditable fact a later dying rule needs.

## Instructions

1. Require one `subject` role declaring `dnd2024.hit-points`. Input is exactly
   `{"amount":<positive safe integer>}`. Validate the complete stored Hit Point state and its fixed
   source reference before proposing an effect or event.
2. Compute `missing = maximum - beforeCurrent`, `appliedAmount = min(amount, missing)`,
   `afterCurrent = beforeCurrent + appliedAmount`, and `lostToMaximum = amount - appliedAmount`.
   Do not add before validating the bound; valid safe-integer input must clamp safely.
3. Propose exactly one complete target `component.set`, including when the applied amount is zero at
   maximum. Preserve maximum and source reference byte-for-byte.
4. Declare exactly one schema-valid `dnd2024.healing.received` event on every successful action,
   naming the subject and carrying target id, requested/applied/lost amounts, before/after current,
   maximum, and source reference.

## Constraints

- Healing does not alter Temporary Hit Points, maximum Hit Points, conditions, death state, or any
  other entity. It consumes no randomness.
- It accepts no final current value, delta, maximum, source reference, cause, effects, or event
  supplied by the caller.
- Feature 17 owns consequences of regaining Hit Points; Feature 29, 31, 32, and 33 own healing
  sources. This mechanic owns only the bounded healing transition and its event.
