---
id: mechanic.dnd2024.rest.clock-reconcile
category: ruleset.dnd2024.core.gameplay.rest
name: Reconcile one rest episode after a scoped clock advance
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reaction-only rest timing reconciliation. E8 selects each episode holder from the accepted event's
world scope. At its policy duration the episode becomes `ready`; this creates no recovery effect.

## Matches

reconcile rest duration
advance active rest

## Requirements

```json
{"event":{"mode":"reaction","types":["game.core.world.clock.advanced"],"components":["game.core.world.clock"]},"roles":{"creature":{"components":["dnd2024.rest-episode"],"includeRelationships":true,"description":"The selected active rest-episode holder."},"policy":{"components":["dnd2024.rest-policy"],"description":"The fixed immutable standard-rest policy."}}}
```
