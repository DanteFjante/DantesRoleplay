---
id: mechanic.dnd2024.turn-budget.spend
category: ruleset.dnd2024.core.combat.economy
name: Spend turn budget resource
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Spends exactly one available D&D 2024 turn-budget resource for a participant in an active encounter.
Action, Bonus Action, free interaction, and movement are available only to the active participant.
Reaction is deliberately exempt from that equality check: a roster participant can spend it on another
participant's turn, then regains it when its own next turn begins. Effective conditions can prohibit
Action, Bonus Action, Reaction, or movement before ordinary budget availability is considered.

## Matches

spend my action
use my bonus action
use my reaction
use my free interaction
move 15 feet

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.turn-budget","dnd2024.speed","dnd2024.conditions"],"description":"The encounter participant whose one resource is spent; normal movement also validates its base Speed and condition state may prohibit a resource."},"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The active encounter whose validated roster and derived active participant authorize the spend."}},"children":{"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```
