---
id: procedure.mechanic.dnd2024.armor-training
category: ruleset.dnd2024.core.data.armor-training
name: Govern D&D 2024 armor-training state
governs: dnd2024.creature.proficiencies; mechanic.dnd2024.armor-training.read; mechanic.dnd2024.armor-training.write
status: active
---

## Description

Owns a creature's complete known Light, Medium, Heavy, and Shield armor-training membership plus a
closed administrative writer and effect-free diagnostic reader.

## Instructions

Store a canonical duplicate-free subset ordered Light, Medium, Heavy, Shield with source
`source.dnd2024.srd-5.2.1`, locator `Rules Glossary > Armor Training`. Empty means known none;
absence means unknown. `record` requires absence, `correct` requires valid existing state, and the
reader never repairs malformed state.

## Constraints

This owner stores membership only. Class/species/feat aggregation, equipped items, Armor Class,
Shield effects, untrained D20/spellcasting drawbacks, Speed, don/doff timing, and action resources
belong to separate rules.
