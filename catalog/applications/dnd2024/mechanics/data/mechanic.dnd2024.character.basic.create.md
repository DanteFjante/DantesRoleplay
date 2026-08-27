---
id: mechanic.dnd2024.character.basic.create
category: ruleset.dnd2024.character.creation.basic
name: Create a basic-playable D&D 2024 character
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Composes accepted source-bound creation resolvers, derives a fixed Soldier plus selected SRD class
level-1 core,
and proposes one atomic actor plus campaign-participation bundle. Deferred entitlements are recorded
and grant no behavior. The participation uses the application-local
`campaign.has-character-participation` and `campaign.character-participation.for-actor` mappings,
which materialize as D&D-owned relationship kinds while retaining the base participation shape.

## Matches

create basic dnd character
create basic playable fighter

## Requirements

```json
{"roles":{"world":{"components":["game.core.world.root","game.core.campaign.character-participation","dnd2024.abilities","dnd2024.armor-class","dnd2024.character-creation-record","dnd2024.character-experience","dnd2024.character-level","dnd2024.conditions","dnd2024.creature-size","dnd2024.hit-points","dnd2024.saving-throw-proficiencies","dnd2024.selected-species","dnd2024.skill-proficiencies","dnd2024.speed","dnd2024.turn-budget","dnd2024.weapon-proficiencies"],"description":"The active base world receiving one new character participation."},"policy":{"components":["dnd2024.character.ability-assignment-policy"],"description":"The exact Standard Array policy."},"background":{"components":["dnd2024.character.content-definition","dnd2024.background.ability-increase-options"],"description":"The exact Soldier background."},"species":{"components":["dnd2024.character.content-definition","dnd2024.species-profile"],"description":"The selected active species."},"class":{"components":["dnd2024.character.content-definition","dnd2024.class-progression","dnd2024.class-creation-profile"],"description":"One selected active SRD level-1 class model."}},"children":{"abilities":{"mechanicId":"mechanic.dnd2024.character-abilities.resolve","roleBindings":{"policy":"policy","background":"background"},"inheritInput":false,"inputFromParentProperty":"ability"},"classProgression":{"mechanicId":"mechanic.dnd2024.class-progression.read","roleBindings":{"class":"class"},"inheritInput":false,"input":"{\"classLevel\":1}"},"speciesSelection":{"mechanicId":"mechanic.dnd2024.species-selection.resolve","roleBindings":{"species":"species"},"inheritInput":false,"inputFromParentProperty":"speciesSelection"}}}
```
