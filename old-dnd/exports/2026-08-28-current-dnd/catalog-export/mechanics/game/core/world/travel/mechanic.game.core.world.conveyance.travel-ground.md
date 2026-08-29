---
id: mechanic.game.core.world.conveyance.travel-ground
category: game.core.world.travel
name: Travel a ground conveyance with its driver
scope: ""
status: active
createdBy: "seed"
changeNote: "Re-seeded: the embedded catalog mechanic changed."
---

## Description
Validates one active driver and one active ground conveyance co-located at the directed ground
route's origin. It derives elapsed minutes with exact integer ceiling division of the route's
stored distance by the conveyance's stored speed. On success it proposes exactly three ordered
effects: move the conveyance, move the driver, then replace that route-scoped root clock. The
action does not infer a vehicle type or travel mode from a name.

## Matches
travel the horse cart to market
take the ground conveyance to market

## Requirements
```json
{"roles":{"driver":{"components":["game.core.world.traveller"],"description":"The active marked entity that drives and moves with the conveyance."},"conveyance":{"components":["game.core.world.conveyance"],"description":"The active ground conveyance that supplies speed."},"origin":{"components":["game.core.world.location"],"includeRelationships":true,"description":"The shared current location and canonical adjacency evidence."},"destination":{"components":["game.core.world.location"],"description":"The route's declared destination."},"conveyanceRoute":{"components":["game.core.world.conveyance-route"],"includeRelationships":true,"description":"The active directed ground route with its world, origin, and destination links."},"world":{"components":["game.core.world.root","game.core.world.clock"],"description":"The active route-scoped world whose clock advances."}}}
```

