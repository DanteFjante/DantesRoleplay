---
id: mechanic.game.core.world.location.connection-add
category: game.core.world.topology
name: Add one canonical adjacency connection
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Connects two distinct active locations with one canonical empty-data undirected adjacency record.
The left endpoint may be a shell created by the same atomic composite. It stores the lexically
smaller endpoint as `from` and rejects an existing reverse or duplicate edge when both endpoints
already exist.

## Matches
connect two existing locations as adjacent
add a canonical exit between locations

## Requirements
```json
{"roles":{"left":{"components":["game.core.world.location"],"optional":true,"includeRelationships":true,"description":"One exact existing active location and its adjacency evidence."},"right":{"components":["game.core.world.location"],"optional":true,"description":"The other exact existing active location."},"world":{"components":["game.core.world.root"],"optional":true,"includeContents":true,"contentsDepth":4,"contentComponentIds":["game.core.world.location"],"description":"The active world used only to resolve a pending shell's existing target."}},"inputSchema":{"oneOf":[{"type":"object","additionalProperties":false},{"type":"object","additionalProperties":false,"required":["locationId","locationName","locationStatus","targetLocationId"],"properties":{"locationId":{"type":"string","pattern":"^location\\.[a-z0-9][a-z0-9.-]*$","maxLength":200},"locationName":{"type":"string","minLength":1,"maxLength":160},"locationStatus":{"const":"active"},"targetLocationId":{"type":"string","pattern":"^location\\.[a-z0-9][a-z0-9.-]*$","maxLength":200}}}]}}
```
