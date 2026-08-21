---
id: procedure.mechanic.dnd2024.d20-test.state-effects
category: ruleset.dnd2024.core.gameplay.d20-tests
name: Derive D&D 2024 condition effects for D20 Tests
governs: commit(kind: "mechanic") authoring mechanic.dnd2024.d20-test.state-effects; commit(kind: "action") reading a creature's condition-derived D20 Test effects
status: active
---

## Description

Owns the effect-free translation from stored D&D 2024 creature condition state to condition-derived
D20 Test inputs. Consumers compose this one resolver and select their branch; they do not duplicate
condition tables or accept condition-derived circumstances from callers.

## Instructions

1. The resolver accepts only `{}` and one subject role. It reports absent condition state as
   `conditionsKnown: false`, with otherwise empty closed branches, rather than treating absence as
   an error or as explicit known-empty state.
2. It returns stored entries, effective conditions, source identities by condition, `byTest`,
   `derivedModifiers`, `prohibitions`, and fixed `Rules Glossary` provenance. It proposes no
   effects, consumes no randomness, rolls no dice, and makes no outcome decision.
3. Effective conditions include stored conditions plus these derived implications: Paralyzed,
   Petrified, and Stunned imply Incapacitated; Unconscious implies Incapacitated and Prone. An
   implied condition inherits every source identity of its stored parent. The derived conditions are
   never written back into `dnd2024.conditions`.
4. `byTest` carries the condition effects implemented by Feature 13's verified consumers: Poisoned
   gives ability checks disadvantage; Restrained gives Dexterity saves disadvantage; Paralyzed,
   Petrified, Stunned, and Unconscious automatically fail Strength and Dexterity saves; the
   attack-roll and attack-against branches carry the applicable non-positional attack
   circumstances; and Incapacitated gives Initiative disadvantage while Invisible gives Initiative
   advantage. Every entry has the closed `{kind,source:"condition:<id>"}` shape.
5. `prohibitions` is an ordered, resource-unique list: effective Incapacitated prohibits `action`,
   `bonusAction`, and `reaction` with reason `condition:incapacitated`; the first effective member
   of Grappled, Paralyzed, Petrified, Restrained, Stunned, and Unconscious prohibits `movement`
   with its own `condition:<id>` reason. It never prohibits `freeInteraction`.
6. Each implemented D20 Test owner composes this resolver with static `{}` input, merges its
   relevant branch with caller circumstances under the existing Advantage/Disadvantage rule, and
   reports caller, derived, and merged inputs separately. Callers may not use the reserved
   `condition:` source prefix.

## Constraints

- A malformed present `dnd2024.conditions` component fails rather than silently producing a
  condition-free result. Missing and empty remain distinct in the result.
- This contract does not apply, clear, time, or cause a condition; those writes belong to
  procedure.mechanic.dnd2024.conditions.
- It does not yet change movement, resource spending, damage, or death. Those are distinct
  Feature 13 consumer slices.
