---
id: mechanic.game.core.world.faction.front.advance
category: game.core.world.faction
name: Advance the Lantern Compact observatory front
scope: ""
status: active
---

## Description

Advances one active scoped front exactly one deterministic pressure phase. It preserves all front
text/lifecycle fields and records the projected root clock minute; it never changes territory,
agenda, locations, or time.

## Matches

advance the observatory front
increase lantern compact pressure

## Requirements
```json
{"roles":{"front":{"components":["game.core.world.faction.front"],"includeRelationships":true,"description":"Active front with exact root, owner, and contested-location links."},"faction":{"components":["game.core.world.faction"],"includeRelationships":true,"description":"Active scoped owner faction."},"location":{"components":["game.core.world.location"],"description":"Active location contested by the front."},"world":{"components":["game.core.world.root","game.core.world.clock"],"description":"Matching active root whose current minute timestamps the phase."}}}
```
