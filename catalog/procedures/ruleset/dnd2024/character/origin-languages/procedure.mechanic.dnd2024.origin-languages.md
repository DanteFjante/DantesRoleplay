---
id: procedure.mechanic.dnd2024.origin-languages
category: ruleset.dnd2024.character.origin-languages
name: Resolve initial D&D 2024 origin languages
governs: mechanic.dnd2024.origin-languages.resolve
status: active
---

## Description

Owns the universal initial player-character language rule and its internal zero-write staged-world
resolver. It is distinct from species, background, class, feat, and later language grants.

## Instructions

1. The SRD 5.2.1 locator `Character Creation > Step 2: Character Origin > Choose Languages`
   requires `common` plus exactly two distinct selections from `common-sign-language`, `draconic`,
   `dwarvish`, `elvish`, `giant`, `gnomish`, `goblin`, `halfling`, and `orc`.
2. `ICharacterOriginLanguageResolver` accepts only a CH5-bound actor ID and exactly
   `{ "languages": ["<standard>", "<standard>"] }`. It requires valid C15 scope and absent
   language state, then returns one `component.add` fragment for the existing
   `dnd2024.language-proficiencies` component.
3. The fragment has the established canonical vocabulary order and fixed source reference. CH5
   appends and applies it; this procedure opens no transaction and makes no direct write.
4. The resolver rejects Common as a choice, rare/unknown/wrong-case values, duplicates, malformed
   input, pre-existing or corrupt language state, and invalid actor/campaign scope before effects.

## Constraints

- The existing language component remains the sole durable language-state owner. This procedure
  creates no origin receipt, species/background binding, content profile, choice-set, or grant log.
- It does not roll dice, choose languages for a caller, authorize later language grants, or resolve
  communication, reading, writing, translation, checks, or features.
- A future random-character-generation owner may produce a legal selected pair, but must compose
  this resolver rather than alter the universal source rule.
