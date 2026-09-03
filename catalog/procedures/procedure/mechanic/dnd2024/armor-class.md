---
id: procedure.mechanic.dnd2024.armor-class
category: ruleset.dnd2024.core.data.armor-class
name: Record authoritative Armor Class
governs: commit(kind: "component") introducing Armor Class storage; commit(kind: "mechanic") validating Armor Class records; commit(kind: "action") recording or correcting Armor Class
status: active
createdBy: "import"
changeNote: "Imported from the catalog."
---

## Description
Defines the authoritative final Armor Class component and its closed administrative writer. It records one final value for a creature with fixed SRD attribution; it does not construct Armor Class from armor, Dexterity, or any other rule input.

## Matches

## Instructions
Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locator `Playing the Game > D20 Tests > Attack Rolls > Armor Class` in System Reference Document 5.2.1.
- That source says an attack meets or exceeds Armor Class to hit and describes a base Armor Class modified by other rules. This feature stores a final, already-authoritative value only.
- Armor, shields, Dexterity, class features, spells, magic items, natural armor, attacks, hit/miss resolution, damage, and Hit Points are out of scope.

Creation order and data

1. Create this contract.
2. Declare `dnd2024.armor-class` as a closed object containing exactly `value` and `sourceRef`.
3. Fix `sourceRef` to `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}`.
4. Create active `mechanic.dnd2024.armor-class.write` in scope `dnd2024-srd-5.2.1`. It declares role `subject` and may inspect that component when present.
5. Use that mechanic for every normal write. It applies exactly one `component.add` for `record` or one `component.set` for `correct`; callers never author source references or effects directly.

Action input and result

- Input is exactly `{"mode":"record"|"correct","value":<positive safe integer>}`. It rejects missing, fractional, non-finite, zero, negative, over-safe-integer, extra, derived, source, and effect fields.
- `record` requires the component to be absent. `correct` requires a valid existing component. Both failures make no change.
- The result reports mode, final value, previous value (null for record), and the fixed source reference. It uses no dice and changes no other component.

Deterministic verification

- Record values 1, 10, 14, and 9007199254740991; correct an existing value; query each resulting component and prove its exact two-field shape.
- Reject duplicate recording, correction while absent, every wrong input root/type/range/mode/extra-key/source shape, and corrupt stored component data without state change.
- Confirm intent routing selects this writer rather than ability, saving-throw, or Initiative rules; replay equivalent actions against equivalent state; query the contract and mechanic back.
- Run catalog dry-run, import, catalog verify, the fresh-database catalog test, the full repository suite, and `git diff --check`.

## Constraints
- `dnd2024.armor-class` contains exactly positive safe-integer `value` and the fixed `sourceRef`.
- This component owns only final Armor Class. No other Feature 6 artifact may duplicate or derive it.
- `record` never overwrites; `correct` never creates; a corrupt existing record is not silently repaired.
- The writer accepts only the closed mode/value input and produces exactly one component effect on `subject`.
- Hit Point state is a separate, blocked slice. Do not add it, damage, healing, temporary Hit Points, or zero-HP consequences here.
