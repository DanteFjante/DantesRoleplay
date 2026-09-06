---
id: mechanic.game.core.world.location.knowledge-attach
category: game.core.world.knowledge
name: Attach one scoped knowledge record to a location
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Attaches one already-authored fact, secret, or clue to one location in the same world through the
canonical `knowledge.about` relationship. The location may be a shell created by the same atomic
composite. It does not create, rewrite, reveal, or classify the knowledge.

## Matches
attach existing world knowledge to a location
make a fact secret or clue about a location

## Requirements
```json
{"roles":{"world":{"components":["game.core.world.root"],"includeContents":true,"contentsDepth":4,"contentsRelevantToRoles":["location"],"description":"The exact active world that scopes both records."},"location":{"components":["game.core.world.location"],"optional":true,"description":"The exact existing location; omitted only for a same-composite shell."},"knowledge":{"components":["game.core.world.knowledge.classification"],"optionalComponents":["game.core.world.fact","game.core.world.secret","game.core.world.clue"],"includeRelationships":true,"description":"One exact already-authored scoped fact, secret, or discoverable clue."}},"inputSchema":{"oneOf":[{"type":"object","additionalProperties":false},{"type":"object","additionalProperties":false,"required":["locationId","locationName","knowledgeId"],"properties":{"locationId":{"type":"string","pattern":"^location\\.[a-z0-9][a-z0-9.-]*$","maxLength":200},"locationName":{"type":"string","minLength":1,"maxLength":160},"knowledgeId":{"type":"string","minLength":1,"maxLength":200}}}]}}
```
