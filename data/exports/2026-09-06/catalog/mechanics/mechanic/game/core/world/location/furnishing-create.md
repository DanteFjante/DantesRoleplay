---
id: mechanic.game.core.world.location.furnishing-create
category: game.core.world.topology
name: Create one unplaced location furnishing
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Creates one unplaced furnishing or fixed feature with caller-authored state. It does not choose a
location, attach itself, create interactions, or infer any world detail.

## Matches
create an unplaced location furnishing
author one furnishing record

## Requirements
```json
{"roles":{},"inputSchema":{"type":"object","additionalProperties":false,"required":["furnishingId","name","status","summary","visibility"],"properties":{"furnishingId":{"type":"string","maxLength":200,"pattern":"^furnishing\\.[a-z0-9][a-z0-9.-]*$"},"name":{"type":"string","minLength":1,"maxLength":160},"status":{"enum":["draft","active","archived"]},"summary":{"type":"string","minLength":1,"maxLength":1000},"visibility":{"enum":["public","party","gm"]}}},"effectComponentIds":["game.core.world.location.furnishing"]}
```
