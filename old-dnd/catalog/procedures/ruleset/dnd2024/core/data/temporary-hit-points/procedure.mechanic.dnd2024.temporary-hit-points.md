---
id: procedure.mechanic.dnd2024.temporary-hit-points
category: ruleset.dnd2024.core.data.temporary-hit-points
name: Grant or expire D&D 2024 Temporary Hit Points
governs: commit(kind: "component") declaring temporary-hit-points storage; commit(kind: "mechanic") validating Temporary Hit Point transitions; commit(kind: "action") granting or expiring a creature's Temporary Hit Point buffer
status: active
---

## Description

Owns the positive Temporary Hit Point buffer and its administrative game transition. Temporary Hit
Points are a separate buffer, not healing and not part of the authoritative Hit Point pair.

## Instructions

1. Declare closed `dnd2024.temporary-hit-points` state containing exactly positive safe-integer
   `amount` and fixed source reference
   `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Temporary Hit Points"}`.
   Absence is the only representation of no buffer; `amount: 0` is invalid.
2. `mechanic.dnd2024.temporary-hit-points.write` has one required `subject` role declaring that
   component. Its closed grant input is `{"mode":"grant","amount":<positive safe integer>}`
   when absent, or additionally `"onExisting":"keep"|"replace"` when present. Its closed expiry
   input is exactly `{"mode":"expire"}` and requires a present valid buffer.
3. A first grant proposes one `component.add`. With an existing buffer, `keep` proposes no effect
   and retains its bytes; `replace` proposes one `component.set`. The SRD permits the creature to
   choose which set to keep, so a lower replacement is legal. Expiry proposes one `component.remove`.
4. Return the prior, granted, resulting, and discarded amounts; whether the result was kept or
   replaced; and source attribution. Consume no randomness and declare no event in this slice.

## Constraints

- This transition never changes `dnd2024.hit-points`, maximum Hit Points, conditions, mitigation,
  another entity, or a clock. Granting Temporary Hit Points is not healing.
- It accepts no source reference, duration, source-of-grant, damage amount/type, current/maximum
  Hit Points, event, or effects field.
- `mechanic.dnd2024.weapon-damage.apply` is the sole current damage consumer: it may spend a
  valid buffer after mitigation, but it never grants one. Expiry timing belongs to Feature 33.
