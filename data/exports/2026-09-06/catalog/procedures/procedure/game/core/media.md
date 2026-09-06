---
id: procedure.game.core.media
category: game.core.media
name: Govern reviewed entity visual media
governs: commit(kind: "system.component-type.register") declaring game.core.media.visual; commit(kind: "system.world-state.sync") adding or replacing reviewed visual-media bindings on existing entities; commit(kind: "system.blob-upload.begin"); commit(kind: "system.blob-upload.finalize"); query(kind: "system.blobs")
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Defines reusable, entity-owned visual attachments for people, creatures, locations, maps, scenes,
items, clues, handouts, and other existing entities. The entity and its normal owner determine
identity and authorization. SQLite owns attachment metadata and association; verified immutable
bytes live only in the adjacent content-addressed blob store.

## Matches

## Instructions
1. Attach `game.core.media.visual` only to an existing entity governed by its normal owner. Never
   create an actor, location, clue, item, encounter, or session merely to carry an image.
2. Store a closed ordered `attachments` array. Every attachment declares one semantic role,
   explicit `player` and/or `dm` visibility, lowercase SHA-256, MIME type, pixel dimensions,
   useful alt text, optional caption, unique order, and reviewed provenance.
3. Admit bytes with `commit(kind: "system.blob-upload.begin")`, transfer the raw body through the
   short-lived HTTP capability, then finalize with `commit(kind: "system.blob-upload.finalize")`.
   Verify the returned digest before writing or migrating the entity component.
4. Never store a URL, upload token, filesystem path, asset key, or base64 image in an ECS component.
5. Select only attachments allowed for the server-issued audience. Never fall back from Player to
   DM or reveal that a filtered attachment exists.
6. Deliver bytes only through owner-bound media discovery or an authorized private-operator blob
   resource. A public website route must re-read the owner and attachment before opening the blob.
7. Replace the complete component after reading its current revision. Disabled/deleted owners and
   draft/archived media contribute no ordinary search or website result.

## Constraints
- The component contains no owner/entity/campaign ID, URL, path, upload capability, rule, effect,
  generated story state, map geometry, route, or discovery state.
- Blob metadata never grants audience access. A raw hash cannot substitute for owner-bound media
  authorization.
- Provenance and hashes are diagnostic/audit material; ordinary presentation may omit them.
- The direct LocalAI media capability is in-process and host-authorized. It does not call MCP.
