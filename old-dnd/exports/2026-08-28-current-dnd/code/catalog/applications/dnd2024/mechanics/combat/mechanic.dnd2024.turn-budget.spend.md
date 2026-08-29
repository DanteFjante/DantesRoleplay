---
id: mechanic.dnd2024.turn-budget.spend
category: ruleset.dnd2024.core.combat.economy
name: Spend explicit turn-budget resource
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Spends one counted resource from the subject participation's latest explicit turn budget. Action, Bonus Action, interaction, and movement require the active turn; Reaction may use the latest budget off turn. Movement is accumulated as exact rational metres and bounded by authoritative walk Speed and Exhaustion.

## Matches

spend my action
use my bonus action
use my reaction
use my free interaction
spend movement

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.movement","dnd2024.conditions"],"includeRelationships":true,"relationshipComponents":[{"kind":"encounter.participation.for-actor","direction":"incoming","targetComponentIds":["dnd2024.encounter.participation"]}],"description":"The actor whose encounter participation spends a resource."},"encounter":{"components":[],"includeRelationships":true,"relationshipComponents":[{"kind":"encounter.has-round","direction":"outgoing","targetComponentIds":["dnd2024.encounter.round"]},{"kind":"encounter.has-turn","direction":"outgoing","targetComponentIds":["dnd2024.encounter.turn","dnd2024.combat.turn-budget"]},{"kind":"encounter.active-turn","direction":"outgoing","targetComponentIds":["dnd2024.encounter.turn","dnd2024.combat.turn-budget"]}],"description":"The active encounter whose explicit lifecycle authorizes the spend."}},"children":{"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
