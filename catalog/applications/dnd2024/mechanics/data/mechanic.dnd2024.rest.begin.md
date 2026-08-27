---
id: mechanic.dnd2024.rest.begin
category: ruleset.dnd2024.core.gameplay.rest
name: Begin a standard Short or Long Rest episode
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Starts one source-backed rest episode for an eligible creature in an active world, using the
immutable standard policy and authoritative base-world clock. It records timing evidence only.

## Matches

begin short rest
begin long rest
start a rest

## Requirements

```json
{"roles":{"creature":{"components":["dnd2024.hit-points","dnd2024.rest-episode"],"description":"Creature starting one rest episode."},"world":{"components":["game.core.world.root","game.core.world.clock"],"includeRelationships":true,"description":"Active base-application root whose clock supplies the start coordinate."},"policy":{"components":["dnd2024.rest-policy"],"description":"Canonical immutable standard-rest policy."}}}
```
