---
id: procedure.mechanic.dnd2024.armor-training
category: ruleset.dnd2024.core.data.armor-training
name: Govern D&D 2024 armor-training state
governs: commit(kind: "component") introducing armor-training storage; commit(kind: "mechanic") validating armor-training records or diagnostics; commit(kind: "action") recording, correcting, or reading armor-training state
status: active
---

## Description

Defines a creature's complete known D&D 2024 armor-training categories and the closed administrative
writer and diagnostic reader. It records membership only; later owners derive class/species/monster
grants, equipped armor, Armor Class, untrained drawbacks, Speed, spellcasting, and timing.

## Instructions

Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locator `Rules Glossary > Armor Class and Armor Training`, PDF page 176 in System Reference Document 5.2.1.
- Armor training categories are Light, Medium, Heavy, and Shield. The source later makes training relevant to armor drawbacks and Shield AC, but this contract records no such consequence.
- Class, species, monster, feat, and temporary grants; equipped items; AC; D20 Tests; Speed; spellcasting; don/doff; actions; and item changes are out of scope.

Creation order and data

1. Declare `dnd2024.armor-training` as a closed object containing `categories` and `sourceRef`.
2. Fix `sourceRef` to `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary > Armor Class and Armor Training"}`.
3. `categories` is a duplicate-free canonical subset ordered `light`, `medium`, `heavy`, `shield`; `[]` means known training with none. Missing state is unknown, never inferred as untrained.
4. Create active writer and reader mechanics in scope `dnd2024-srd-5.2.1`; each declares role `subject` and may inspect this component.
5. The writer applies exactly one `component.add` for `record` or `component.set` for `correct`. The reader accepts `{}`, reports diagnostics, and emits no effects.

Action input and result

- Writer input is exactly `{"mode":"record"|"correct","categories":["light"|"medium"|"heavy"|"shield"...]}`. Callers never supply source attribution, grant provenance, class, species, armor, Shield, AC, D20 circumstance, Speed, spellcasting, action, or effects.
- `record` requires absence. `correct` requires a valid existing component. A corrupt record is rejected rather than repaired silently.
- The writer reports mode, canonical categories, previous categories/null, and fixed source attribution. The reader returns present/valid diagnostics and valid categories/source attribution only. Neither uses dice.

## Constraints

- This component is the only Feature 24 owner of persistent armor-training category state.
- No reader may infer a grant, perform item aggregation, or calculate/apply an armor consequence from this state alone.
- Missing, malformed, or invalid state remains unknown; normal later consumers must fail closed rather than treat it as an empty training set.
