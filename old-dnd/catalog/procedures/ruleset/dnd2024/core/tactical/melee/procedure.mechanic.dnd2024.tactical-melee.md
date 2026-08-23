---
id: procedure.mechanic.dnd2024.tactical-melee
category: ruleset.dnd2024.core.tactical.melee
name: Govern D&D 2024 tactical melee admission
governs: dnd2024 tactical melee admission and composition with the effect-free Feature 8 weapon attack resolver
status: active
---

Defines the source-backed tactical precondition for a base-reach melee weapon attack.

## Instructions

1. Require valid bounded encounter-space, direct participant roster membership, Size-derived
   placements in that encounter, and attacker base melee reach before an attack child executes.
2. Accept only a closed kind melee root and an existing Feature-8 attack-input object. Map
   distance, reach, target legality, dice, Armor Class, hit, and damage are never caller input.
3. Require a canonical dnd2024.weapon-profile with kind melee. Weapon Reach properties, thrown
   use, and ranged rules remain Feature 25/21 work.
4. Use E6 inputFromChildData only: the effect-free admission child returns the complete closed
   Feature-8 input, and Feature 8 receives no other parent metadata.
5. Return frozen Feature-8 evidence and child provenance with zero effects. Do not spend an Action,
   create damage, move a creature, or write an outcome.

## Constraints

- Feature 8 remains the sole weapon-attack arithmetic and d20 owner.
- Feature 20 remains the sole tactical position/reach owner.
- This is not a general attack authorization, equipment-selection, or Action-economy system.
