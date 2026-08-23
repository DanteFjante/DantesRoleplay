---
id: procedure.mechanic.dnd2024.armor-class
category: ruleset.dnd2024.core.data.armor-class
name: Derive D&D 2024 Armor Class
governs: mechanic.dnd2024.armor-class.read and the retired historical dnd2024.armor-class record
status: active
---

## Description
Defines derived D&D 2024 Armor Class from authoritative Dexterity and Feature 24 direct equipment
selection. The old `dnd2024.armor-class` component and writer are deprecated historical records;
they are never a normal combat input or fallback.

## Instructions
Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locators `Rules Glossary > Armor Class and Armor Training` and `Equipment > Armor` in System Reference Document 5.2.1.
- Default base AC is 10 plus Dexterity modifier. Light armor adds full Dexterity modifier, Medium caps it at +2, Heavy adds none, and a trained Shield adds +2.
- Class, species, natural armor, spells, magic items, D20 drawbacks, Speed, spellcasting, attacks, damage, and Hit Points remain separate owners.

Creation order and data

1. `mechanic.dnd2024.armor-class.read` accepts exactly `{}` with `subject` ability scores and exactly one `mechanic.dnd2024.armor-equipment.read` child result.
2. It derives the selected base and any valid Shield bonus; callers cannot supply any AC input.
3. A selected Shield requires valid explicit armor-training state. Missing/invalid training fails rather than guessing the +2 bonus.
4. The legacy manual component is not read. Its deprecated writer does not route actions.

Action input and result

- Input is exactly `{}`. The result reports final AC, selected default/armor base, applied Dexterity modifier, Shield selection/training/bonus, and fixed source attribution. It uses no dice and changes no component.

Deterministic verification

- Prove default/light/medium/heavy calculations, Shield training/absence/corruption, selected direct equipment failures, legacy-state non-fallback, weapon/unarmed consumer migration, fixture AC preservation, replay, and zero effects.
- Run catalog validation, fresh-database tests, the full repository suite, and `git diff --check`.

## Constraints
- The derived reader is the only normal final-AC source for current supported default/armor/Shield bases.
- Do not default absent/corrupt direct equipment or Shield training, and do not retain a manual/natural-armor fallback.
- The legacy component is historical-only and must not be updated by this slice. Hit Point state remains separate.
