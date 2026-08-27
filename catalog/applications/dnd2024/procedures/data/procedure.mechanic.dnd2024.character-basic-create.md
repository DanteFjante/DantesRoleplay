---
id: procedure.mechanic.dnd2024.character-basic-create
category: ruleset.dnd2024.character.creation.basic
name: Create one basic-playable D&D 2024 character
governs: mechanic.dnd2024.character.basic.create; dnd2024.character-creation-record
status: active
---

## Description

Creates one deliberately incomplete but mechanically honest Soldier plus selected SRD class
level-1 actor. The
operation derives core state from active source-bound definitions, records every deferred
entitlement, and attaches the new actor to the bound active world in one application transaction.

## Instructions

1. Bind exactly the active world, Standard Array policy, Soldier background, selected species, and
   one active class with matching progression/profile source. Accept only a host-reserved `actor.*`
   ID, display name, ability child input,
   and species-selection child input.
2. Compose the accepted ability, species-selection, and class-progression readers. Reject any child
   failure, effects, malformed data, source mismatch, inactive definition, or unexpected ID.
3. Derive level-1 Hit Points as the selected class Hit Die maximum plus the Constitution modifier
   (minimum 1) and unequipped Armor Class as 10 plus the Dexterity modifier. Apply Soldier
   Athletics/Intimidation, the profile's fixed legal class-skill choices, class saves, and only
   complete weapon categories that the current state owner can express.
4. Create the actor and campaign participation atomically through typed effects. Store other core
   state in its normal component and store only selections/applied IDs/pending evidence in
   `dnd2024.character-creation-record`. Use D&D-owned participation relationship kinds because
   relationship IDs are scoped to the active application state space.
5. Treat every unresolved entry as informational denial: it grants no feature, trait, proficiency,
   equipment, resource, spell, language, tool, or action.

## Constraints

- `basic-playable` never means source-complete.
- The caller never supplies final scores, modifiers, HP, AC, Speed, level, proficiencies, effects,
  pending entries, participation identity/status, source references, or audit identity.
- The mechanic is the D&D rule owner; the generic action runner is the sole transaction, replay,
  rollback, and audit owner.
- `dnd2024.campaign.has-character-participation` and
  `dnd2024.campaign.character-participation.for-actor` carry exact `{}` payloads and preserve C15's
  world-to-participation-to-actor graph without creating a cross-application relationship ID.
- A duplicate actor/participation, inactive world, stale content, invalid request, or any late
  component/link failure leaves no actor, participation, component, or relationship behind.
- This procedure does not resolve species traits, Savage Attacker, class-feature behavior, armor
  training, equipment, languages, tools, restricted Martial weapon groups, spellcasting, rest
  completion, or advancement.
