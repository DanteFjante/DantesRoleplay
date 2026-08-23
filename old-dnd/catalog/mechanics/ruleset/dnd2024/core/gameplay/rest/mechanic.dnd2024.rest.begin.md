---
id: mechanic.dnd2024.rest.begin
category: ruleset.dnd2024.core.gameplay.rest
name: Begin a standard Short or Long Rest episode
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Starts one source-backed rest episode for an eligible creature in a selected active world. It reads
the immutable standard policy and the root clock, then records timing evidence only. It never
recovers or spends anything.

## Matches

begin short rest
begin long rest
start a rest

## Requirements

```json
{"roles":{"creature":{"components":["dnd2024.hit-points","dnd2024.rest-episode"],"description":"The creature starting one rest episode."},"world":{"components":["game.core.world.root","game.core.world.clock"],"includeRelationships":true,"description":"The active root world whose one clock supplies the start coordinate."},"policy":{"components":["dnd2024.rest-policy"],"description":"The canonical immutable standard-rest policy content entity."}}}
```
