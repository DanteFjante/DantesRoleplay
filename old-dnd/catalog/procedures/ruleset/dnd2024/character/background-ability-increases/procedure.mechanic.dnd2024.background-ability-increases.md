---
id: procedure.mechanic.dnd2024.background-ability-increases
category: ruleset.dnd2024.character.background-ability-increases
name: Resolve source-cited D&D 2024 background ability increases
governs: dnd2024.background.ability-increase-options and mechanic.dnd2024.background-ability-increases.resolve
status: active
---

## Description

Owns immutable background ability-increase declarations and the internal zero-write resolver that
turns one CH5-bound selection into a merge fragment for existing raw ability-score state.

## Instructions

1. Attach `dnd2024.background.ability-increase-options` only to an active CH1 background content
   definition. Its key, version, and registered SRD source reference must agree exactly.
2. The declaration contains only three canonical eligible ability ids and one or both closed
   patterns: `plus-2-plus-1` or `plus-1-each`. It contains no selected actor value, feat, skill,
   tool, language, item, class, prose, or executable payload.
3. `IBackgroundAbilityScoreIncreaseResolver` accepts a root-bound actor and background definition
   plus a closed JSON selection. It requires valid C15 scope and existing CH2 base abilities, then
   returns exactly one `component.merge` fragment containing changed raw scores only.
4. The resolver rejects an increase that is not source-declared, changes an ineligible ability,
   replaces the six-score object, or raises any raw score above 20. CH5 applies the fragment;
   this procedure never opens a transaction, writes state, or exposes a public action.

## Constraints

- CH1 owns content identity and CH3 owns background selection/provenance/receipts. This procedure
  neither selects a background nor records why an actor received an increase.
- CH2 owns base allocation and the existing ability component remains the sole raw-score owner.
  Modifiers and all other derived values remain derived.
- A correction is a new versioned background definition; do not mutate an approved source profile
  or permit caller-supplied final ability scores.
