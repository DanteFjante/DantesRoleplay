---
id: mechanic.dnd2024.rest.interrupt
category: ruleset.dnd2024.core.gameplay.rest
name: Record a standard rest interruption
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Records one exact source interruption at an active episode's fully observed current clock
coordinate. Short Rest is stopped with no benefit; Long Rest remains active with one added hour.

## Matches

interrupt a rest
stop a short rest
resume an interrupted long rest

## Requirements

```json
{"roles":{"creature":{"components":["dnd2024.rest-episode"],"description":"Creature whose active rest is interrupted."},"world":{"components":["game.core.world.root","game.core.world.clock"],"includeRelationships":true,"description":"Matching active base world whose clock authenticates the interruption coordinate."},"policy":{"components":["dnd2024.rest-policy"],"description":"Canonical immutable standard-rest policy."}}}
```
