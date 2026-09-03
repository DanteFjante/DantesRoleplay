---
id: mechanic.game.core.world.location.shell-create
category: game.core.world.topology
name: Create one unplaced location shell
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Creates exactly one unplaced entity carrying one closed `game.core.world.location` record. The
caller supplies every authored field; this mechanic does not invent scenery, purpose, placement,
connections, knowledge, furnishings, people, or media.

## Matches
create an empty location shell
author a bare location record

## Requirements
```json
{"roles":{},"inputSchema":{"type":"object","additionalProperties":false,"required":["locationId","name","kind","status","summary","visibility"],"properties":{"locationId":{"type":"string","maxLength":200,"pattern":"^location\\.[a-z0-9][a-z0-9.-]*$"},"name":{"type":"string","minLength":1,"maxLength":160},"kind":{"enum":["region","settlement","site","interior"]},"status":{"enum":["draft","active","archived"]},"summary":{"type":"string","minLength":1,"maxLength":1000},"visibility":{"enum":["public","party","gm"]}}},"effectComponentIds":["game.core.world.location"]}
```
