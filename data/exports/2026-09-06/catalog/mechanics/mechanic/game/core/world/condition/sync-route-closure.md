---
id: mechanic.game.core.world.condition.sync-route-closure
category: game.core.world.reactive
name: Reconcile the gate-to-market closure from its root clock
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Reaction-only reconciliation for the fixed Feature 10 route closure. It reads an accepted root-clock
replacement and returns either no effects or exactly the complete condition then availability
replacements required by the resulting minute.

## Matches
apply the automatic route-closure reaction

## Requirements
```json
{"event":{"mode":"reaction","types":["world.component.replaced"],"components":["game.core.world.clock"]},"roles":{"condition":{"components":["game.core.world.condition"],"includeRelationships":true,"description":"Fixed scheduled route-closure condition with explicit root and route scope."},"route":{"components":["game.core.world.route","game.core.world.route.availability"],"includeRelationships":true,"description":"Fixed affected route with its existing route scope and current availability."}}}
```
