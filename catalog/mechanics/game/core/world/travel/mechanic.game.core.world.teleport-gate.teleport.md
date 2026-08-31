---
id: mechanic.game.core.world.teleport-gate.teleport
category: game.core.world.travel
name: Cross one fixed teleport gate
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Validates one active traveller co-located with one active fixed portal, its explicit root and
destination links, and an unchanged valid root clock. It proposes exactly one effect: move the
traveller to the linked destination. Roads, routes, adjacency, plans, and duration are not read.

## Matches
cross the gate to observatory portal
use the fixed portal

## Requirements
```json
{"roles":{"traveller":{"components":["game.core.world.traveller"],"description":"The active marked traveller to relocate."},"portal":{"components":["game.core.world.teleport-gate"],"includeRelationships":true,"description":"The active fixed portal with direct origin containment and scope/destination links."},"origin":{"components":["game.core.world.location"],"description":"The portal's claimed current location."},"destination":{"components":["game.core.world.location"],"description":"The portal's explicit linked destination."},"world":{"components":["game.core.world.root","game.core.world.clock"],"description":"The active scoped world whose clock remains unchanged."}}}
```
