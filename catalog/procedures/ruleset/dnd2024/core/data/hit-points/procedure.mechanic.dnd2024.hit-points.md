---
id: procedure.mechanic.dnd2024.hit-points
category: ruleset.dnd2024.core.data.hit-points
name: Record authoritative Hit Points
governs: commit(kind: "component") introducing Hit Point storage; commit(kind: "mechanic") validating Hit Point records; commit(kind: "action") recording or correcting Hit Points
status: active
---

## Description
Defines one creature's authoritative current and maximum Hit Point state and its closed administrative writer. The pair is always written as one component so no record can contain an unbounded current value or an orphaned maximum.

## Instructions

Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locator `Playing the Game > Damage and Healing > Hit Points` in System Reference Document 5.2.1.
- The source distinguishes current and maximum Hit Points, says healing cannot raise current above maximum, and gives later rules for zero Hit Points. This feature records the bounded state only.
- Damage, healing, temporary Hit Points, resistance, immunity, vulnerability, unconsciousness, death, massive damage, death saves, class advancement, and creature-building formulas are out of scope.

Creation order and data

1. Create this contract.
2. Declare `dnd2024.hit-points` as a closed object containing exactly `current`, `maximum`, and `sourceRef`.
3. Fix `sourceRef` to `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Hit Points"}`.
4. Create active `mechanic.dnd2024.hit-points.write` in scope `dnd2024-srd-5.2.1`. It declares role `subject` and may inspect that component when present.
5. Use that mechanic for every normal write. It applies exactly one `component.add` for `record` or one `component.set` for `correct`; callers never author source references, deltas, damage types, or effects directly.

Action input and result

- Input is exactly `{"mode":"record"|"correct","current":<safe integer>,"maximum":<positive safe integer>}` with `0 <= current <= maximum`.
- It rejects missing, fractional, non-finite, negative, zero maximum, out-of-order, over-safe-integer, extra, source, delta, damage, healing, and effect fields before proposing effects.
- `record` requires absence. `correct` requires a valid existing component. A corrupt existing record is rejected, never silently repaired.
- The result reports mode, final current/maximum, previous pair (null for record), and the fixed source reference. It uses no dice and changes no other component.

Deterministic verification

- Record `(0,1)`, `(1,1)`, an ordinary partial pair, a full pair, and a safe-integer maximum; correct an existing pair; query each resulting component and prove its exact three-field shape.
- Reject duplicate recording, correction while absent, `current > maximum`, every wrong input root/type/range/mode/extra-key/source shape, and corrupt stored component data without state change.
- Confirm intent routing selects this writer rather than Armor Class, ability, saving-throw, or Initiative rules; replay equivalent actions against equivalent state; query the contract and mechanic back.
- Run catalog dry-run, import, catalog verify, the fresh-database catalog test, the full repository suite, and `git diff --check`.

## Constraints

- `dnd2024.hit-points` contains exactly current, maximum, and the fixed `sourceRef`; maximum is a positive safe integer and current is a safe integer in `0..maximum`.
- This component owns only state, not why it changed. Feature 9 will own damage-caused Hit Point loss; a future healing feature will own healing-caused increase.
- `record` never overwrites; `correct` never creates; a corrupt existing record is not silently repaired.
- The writer accepts only the closed mode/current/maximum input and produces exactly one component effect on `subject`.
- Armor Class is already complete and must not be changed by this writer.
