---
id: mechanic.game.core.world.location.register
category: game.core.world.location
name: Register a new world location
status: draft
createdBy: "llm"
changeNote: ""
---

## Description
Creates one new location entity under a world root with a validated kind, summary, and visibility. Additive only -- never edits or removes an existing location.

## Matches
register a location
add a new location
create a location
register a new location

## Requirements
```json
{"roles":{"world":{"components":["game.core.world.root"],"description":"The world root this location belongs to."}}}
```
