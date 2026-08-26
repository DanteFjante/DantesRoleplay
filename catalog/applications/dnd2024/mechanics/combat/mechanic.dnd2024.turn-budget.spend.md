---
id: mechanic.dnd2024.turn-budget.spend
category: ruleset.dnd2024.core.combat.economy
name: Spend turn-budget resource
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Spends exactly one available resource for an admitted participant in an active encounter. Reaction
may be spent off turn; every other resource requires the active participant. Conditions can prohibit
a resource before ordinary availability is considered.

## Matches

spend my action
use my bonus action
use my reaction
use my free interaction
move 15 feet

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.turn-budget","dnd2024.speed","dnd2024.conditions"],"description":"The admitted participant whose resource is spent."},"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The active encounter authorizing the spend."}},"children":{"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
