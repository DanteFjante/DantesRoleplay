---
id: procedure.mechanic.dnd2024.unarmed-strike.damage
category: ruleset.dnd2024.core.gameplay.unarmed-strikes
name: Resolve diagnostic unarmed strike damage
governs: commit(kind: "mechanic") resolving seeded D&D unarmed-strike Damage evidence
status: active
---

## Description

Defines the effect-free D&D 2024 Unarmed Strike Damage resolver. It derives a Strength-based
attack and fixed Bludgeoning damage evidence from canonical attacker, target, and condition state;
it does not establish reach, spend an Action, or change Hit Points.

## Instructions

Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locator `Rules Glossary > Unarmed Strike`, PDF pages
  189–190 in *System Reference Document 5.2.1*.
- The Damage option adds the attacker's Strength modifier and derived Proficiency Bonus to its D20
  attack total, and deals `max(0, 1 + Strength modifier)` Bludgeoning damage on a hit.
- A natural 20 always hits and is critical; a natural 1 always misses. Because Unarmed Strike's
  initial damage is fixed rather than dice, a critical does not increase `damageOnHit`.
- Tactical reach/position, Action spending, target selection authorization, HP application,
  Grapple, Shove, item/equipment use, and attack-history recording are out of scope.

Required state and input

1. Require subject `dnd2024.abilities` and `dnd2024.character-level`; target
   `dnd2024.armor-class`; both roles provide optional known condition state only through exactly
   one `mechanic.dnd2024.d20-test.state-effects` child each.
2. Validate complete ability, total-level, Armor Class, and child-result state before randomness.
   Proficiency Bonus derives as `2 + floor((level - 1) / 4)`; callers cannot supply it.
3. Input is exactly `{}` or `{"rollCircumstances":[...]}`. Each caller circumstance has only
   `kind: "advantage"|"disadvantage"` and a nonempty trimmed `source`; the `condition:` prefix
   is reserved for Feature 13-derived facts.
4. Merge caller, attacker `attackRoll`, and target `attackAgainst` circumstances using the
   established non-stacking/cancellation convention. Apply only attacker-derived exhaustion
   modifiers to the attack total.
5. Return closed D20 and fixed-damage evidence with `effects: []`. `damageOnHit` is always the
   derived fixed value; `potentialDamage` is that value only when the result hits, otherwise zero.

Verification

- Prove all Proficiency Bonus bands, Strength-modifier floor, Armor Class equality, normal/
  Advantage/Disadvantage/cancelled modes, natural 20/1 precedence, and a critical's unchanged
  fixed damage.
- Prove caller-forged conditions, AC, Proficiency Bonus, modifier, dice, outcome, damage,
  position, hand, weapon/profile, HP, and effect fields reject before a die.
- Prove absent/known/corrupt condition state, bad roles, fixed-seed replay, routing, zero effects,
  and byte-identical state on accepted and rejected reads.

## Constraints

- This resolver owns transient diagnostic Unarmed Strike Damage evidence only. It creates no
  component, item, condition, Action-budget, Hit Point, position, or attack-history effect.
- It must never model an unarmed strike as a `dnd2024.weapon-profile`, accept a caller-selected
  ability, or duplicate Feature 9's weapon-damage/HP application path.
- A later tactical parent must independently prove reach, authorization, Action spend, and typed
  damage application before consuming this evidence.
