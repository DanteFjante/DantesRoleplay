---
id: mechanic.game.core.world.route.travel-on-foot
category: game.core.world.travel
name: Travel one declared on-foot route
scope: ""
status: active
---

## Description

Validates one active marked traveller, its derived origin containment, one active directed on-foot
route, explicit open route availability, canonical adjacency, and the route's scoped root clock.
Incoming links owned by another world feature do not alter the route's three route-owned scope
links. On success it proposes exactly two ordered effects: move the traveller and advance that root
clock by the stored route duration.

## Matches
take the named gate-to-market route
travel the gate-to-market road
walk the named route from gate to market

## Requirements
```json
{"roles":{"traveller":{"components":["game.core.world.traveller"],"description":"The active marked entity that will travel."},"origin":{"components":["game.core.world.location"],"includeRelationships":true,"description":"The traveller's claimed current location and canonical adjacency evidence."},"destination":{"components":["game.core.world.location"],"description":"The claimed directed route destination."},"route":{"components":["game.core.world.route","game.core.world.route.availability"],"includeRelationships":true,"description":"The active route with its scope, origin, destination, and condition-owned availability."},"world":{"components":["game.core.world.root","game.core.world.clock"],"description":"The active route-scoped world whose one clock advances."}}}
```
