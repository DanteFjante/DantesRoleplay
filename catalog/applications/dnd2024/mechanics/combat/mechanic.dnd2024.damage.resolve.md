---
id: mechanic.dnd2024.damage.resolve
category: ruleset.dnd2024.core.gameplay.damage
name: Read damage mitigation profile
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Combines a defender's stored mitigation memberships with its declared Condition state-effects child
as an effect-free profile for a later damage cause. It receives no damage instance and changes no
state.

## Matches

inspect damage mitigation
read damage mitigation profile

## Requirements

```json
{"roles":{"defender":{"components":["dnd2024.damage-mitigation"],"description":"The creature whose absent, known-empty, or valid mitigation state is reported."}},"children":{"stateEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"defender"},"inheritInput":true}}}
```
