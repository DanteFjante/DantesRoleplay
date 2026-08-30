---
id: mechanic.game.core.world.aerial-conveyance.travel
category: game.core.world.travel
name: Travel an aerial conveyance with its rider
scope: ""
status: active
createdBy: "seed"
changeNote: "Re-seeded: the embedded catalog mechanic changed."
---

## Description
Validates one active rider and one active aerial conveyance co-located at the directed aerial
route's explicit launch location. It derives elapsed minutes with exact integer ceiling division
of stored route distance by stored conveyance speed. On success it proposes exactly three ordered
effects: move the conveyance, move the rider, then replace that route-scoped root clock. Ground
adjacency, roads, routes, and map links are neither read nor required.

## Matches
fly the dragon to the observatory
take the aerial conveyance to the observatory

## Requirements
```json
{"roles":{"rider":{"components":["game.core.world.traveller"],"description":"The active marked entity that rides and moves with the aerial conveyance."},"conveyance":{"components":["game.core.world.aerial-conveyance"],"description":"The active aerial conveyance that supplies speed."},"origin":{"components":["game.core.world.location"],"description":"The explicit aerial-route launch location."},"destination":{"components":["game.core.world.location"],"description":"The explicit aerial-route landing location."},"aerialRoute":{"components":["game.core.world.aerial-route"],"includeRelationships":true,"description":"The active directed aerial route with exact world, origin, and destination links."},"world":{"components":["game.core.world.root","game.core.world.clock"],"description":"The active route-scoped world whose clock advances."}}}
```

