---
id: mechanic.game.core.world.location.place
category: game.core.world.topology
name: Place one unplaced location beneath its parent
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Places one existing unplaced location, or a location shell being created by the same atomic
composite, under one existing active world root or location. It derives the canonical containment
slot from the child kind and does not create or alter either endpoint.

## Matches
place an existing location under its parent
put a location in its world hierarchy

## Requirements
```json
{"roles":{"location":{"components":["game.core.world.location"],"optional":true,"description":"The exact existing unplaced location shell; omitted only when the shell is created in the same atomic composite."},"parent":{"components":[],"optionalComponents":["game.core.world.root","game.core.world.location"],"description":"The exact active world root or parent location."}},"inputSchema":{"oneOf":[{"type":"object","additionalProperties":false},{"type":"object","additionalProperties":false,"required":["locationId","name","kind","status","summary","visibility"],"properties":{"locationId":{"type":"string","maxLength":200,"pattern":"^location\\.[a-z0-9][a-z0-9.-]*$"},"name":{"type":"string","minLength":1,"maxLength":160},"kind":{"enum":["region","settlement","site","interior"]},"status":{"enum":["draft","active"]},"summary":{"type":"string","minLength":1,"maxLength":1000},"visibility":{"enum":["public","party","gm"]}}}]}}
```
