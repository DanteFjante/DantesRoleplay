---
id: mechanic.game.core.world.location.furnishing-attach
category: game.core.world.topology
name: Attach one existing furnishing to a location
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Places one existing unplaced furnishing, or one being created by the same atomic composite, inside
an existing or same-composite location using the canonical `furnishing` containment slot. It
changes no component data.

## Matches
attach an existing furnishing to a location
place a furnishing inside a location

## Requirements
```json
{"roles":{"location":{"components":["game.core.world.location"],"optional":true,"description":"The exact existing location; omitted only for a same-composite location shell."},"furnishing":{"components":["game.core.world.location.furnishing"],"optional":true,"description":"The exact existing furnishing; omitted only for a same-composite furnishing."}},"inputSchema":{"oneOf":[{"type":"object","additionalProperties":false},{"type":"object","additionalProperties":false,"required":["locationId","locationName","locationStatus","furnishingId","furnishingName","furnishingStatus"],"properties":{"locationId":{"type":"string","pattern":"^location\\.[a-z0-9][a-z0-9.-]*$","maxLength":200},"locationName":{"type":"string","minLength":1,"maxLength":160},"locationStatus":{"enum":["draft","active"]},"furnishingId":{"type":"string","pattern":"^furnishing\\.[a-z0-9][a-z0-9.-]*$","maxLength":200},"furnishingName":{"type":"string","minLength":1,"maxLength":160},"furnishingStatus":{"enum":["draft","active"]}}}]}}
```
