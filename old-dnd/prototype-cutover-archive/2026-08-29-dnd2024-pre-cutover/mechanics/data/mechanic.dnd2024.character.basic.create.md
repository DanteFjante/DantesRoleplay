---
id: mechanic.dnd2024.character.basic.create
category: ruleset.dnd2024.character.creation.basic
name: Create a basic-playable D&D 2024 character
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Composes accepted source-bound creation resolvers, derives a selected SRD background plus selected
SRD class level-1 core, and proposes one atomic actor plus campaign-participation bundle. The
background Origin Feat and every class level-1 feature are stored as identity/provenance grants;
the class's exact armor training is stored through its canonical membership owner;
their unresolved behavior remains explicitly denied. Alert's implemented opt-in Initiative
Proficiency is available to the Initiative owner through its stored identity, so only Alert's
unimplemented Initiative Swap remains in the pending ledger. Exact
fixed background skills and tools, fixed class tools, and Common are applied through their existing
owners. An optional closed origin choice completes two Standard languages and any background tool
choice. An optional `classToolChoices` array completes the exact tool group declared by Bard or
Monk. Omission keeps those grants pending. Deferred choices and behavior grant no behavior. The participation uses
the application-local
`campaign.has-character-participation` and `campaign.character-participation.for-actor` mappings,
which materialize as D&D-owned relationship kinds while retaining the base participation shape.
Complete weapon categories and any property-qualified Martial membership are stored exactly from
the class profile. The latter remains behavior-pending until weapon properties can be evaluated.
An optional exact `equipmentChoices:{background:"cash",class:"cash"}` selection resolves both
cash alternatives from the bound source definitions. When selected, the optional `currency` role
must be the canonical Gold Piece definition; the mechanic creates one physical GP stack, contains
it under the actor in `inventory.currency`, records the created item, and removes only the two
satisfied equipment deferrals in the same atomic transaction. Omitting the selection retains both
equipment deferrals and creates no currency.

## Matches

create basic dnd character
create basic playable fighter
create basic acolyte bard
create soldier with languages and gaming set
create bard with three musical instruments
create monk with an artisan tool
create fighter with cash starting equipment
create character with background and class gold

## Requirements

```json
{"roles":{"world":{"components":["game.core.world.root","game.core.campaign.character-participation","dnd2024.creature.ability-scores","dnd2024.character.class-membership","dnd2024.character-creation-record","dnd2024.character-feature-grants","dnd2024.character.experience","dnd2024.conditions","dnd2024.creature.body","dnd2024.creature.defenses","dnd2024.creature.hit-points","dnd2024.core.definition-link","dnd2024.item.quantity","dnd2024.creature.languages","dnd2024.creature.movement","dnd2024.creature.proficiencies","dnd2024.selected-species"],"description":"The active base world receiving one new character, its class-membership entity, its participation, and, for the cash path, one contained currency item."},"policy":{"components":["dnd2024.character.ability-assignment-policy"],"description":"The exact Standard Array policy."},"background":{"components":["dnd2024.character.content-definition","dnd2024.background.ability-increase-options","dnd2024.background-creation-profile"],"description":"One selected active SRD background model."},"species":{"components":["dnd2024.character.content-definition","dnd2024.species-profile"],"description":"The selected active species."},"class":{"components":["dnd2024.character.content-definition","dnd2024.class-progression","dnd2024.class-creation-profile"],"description":"One selected active SRD level-1 class model."},"currency":{"components":["dnd2024.item-definition"],"optional":true,"description":"The canonical Gold Piece definition, required only when the exact cash equipment choices are supplied."}},"children":{"abilities":{"mechanicId":"mechanic.dnd2024.character-abilities.resolve","roleBindings":{"policy":"policy","background":"background"},"inheritInput":false,"inputFromParentProperty":"ability"},"classProgression":{"mechanicId":"mechanic.dnd2024.class-progression.read","roleBindings":{"class":"class"},"inheritInput":false,"input":"{\"classLevel\":1}"},"speciesSelection":{"mechanicId":"mechanic.dnd2024.species-selection.resolve","roleBindings":{"species":"species"},"inheritInput":false,"inputFromParentProperty":"speciesSelection"}}}
```
