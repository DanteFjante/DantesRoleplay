---
id: mechanic.game.core.world.faction.agenda
category: game.core.world.faction
name: Advance one ready faction agenda
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Advances one active faction's confirmed agenda exactly once from `ready` to `advanced`. It returns
one complete component replacement and has no random result, relationship, motive, or topology
side effect.

## Matches
advance faction agenda
advance the lantern compact agenda
move faction agenda forward

## Requirements
```json
{"roles":{"faction":{"components":["game.core.world.faction"],"description":"The active faction whose ready agenda may advance once."}}}
```
