---
id: mechanic.game.core.world.location.move
category: game.core.world.travel
name: Move an active traveller to an adjacent location
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Validates one active marked traveller, its derived containment, two active sibling locations, and
their stored Feature 1 adjacency. On success it proposes exactly one containment move to the
destination; it has no random outcome, route, time, or topology side effect.

## Matches
move to a connected location
travel from one location to another
move from gate to market
travel to an adjacent location

## Requirements
```json
{"roles":{"traveller":{"components":["game.core.world.traveller"],"description":"The active marked entity that will move."},"origin":{"components":["game.core.world.location"],"includeRelationships":true,"description":"The traveller's claimed current location and the stored adjacency evidence."},"destination":{"components":["game.core.world.location"],"description":"The claimed adjacent destination location."}}}
```
