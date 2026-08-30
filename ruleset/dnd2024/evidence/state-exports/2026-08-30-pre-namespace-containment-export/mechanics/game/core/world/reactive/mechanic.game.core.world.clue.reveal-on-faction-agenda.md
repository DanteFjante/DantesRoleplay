---
id: mechanic.game.core.world.clue.reveal-on-faction-agenda
category: game.core.world.reactive
name: Reveal Oren's letter when the Compact advances
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Reaction-only fixture consequence of the Compact's ready-to-advanced agenda transition.

## Matches
apply the automatic subscription reaction

## Requirements
```json
{"event":{"mode":"reaction","types":["world.component.replaced"],"components":["game.core.world.faction"]},"roles":{"clue":{"components":["game.core.world.clue"],"description":"Fixed Oren-letter clue revealed only by the matching faction transition."}}}
```

