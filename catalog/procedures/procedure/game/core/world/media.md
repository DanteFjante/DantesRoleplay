---
id: procedure.game.core.world.media
category: game.core.world.media
name: Govern reviewed entity visual media
governs: commit(kind: "component") declaring game.core.world.media.visual; commit(kind: "effects") adding, replacing, or removing reviewed visual-media bindings on existing entities; commit(kind: "system.blob-upload.begin"); commit(kind: "system.blob-upload.finalize"); query(kind: "system.blobs")
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Defines one reusable, entity-owned visual-media record for portraits, location settings, current
scenes, and clue or handout plates. The entity and its existing owner determine identity,
authorization, reveal state, and current-situation meaning. This component supplies reviewed
presentation metadata only.

## Matches

## Instructions
1. Attach `game.core.world.media.visual` only to an existing entity governed by its normal owner.
   Never create an actor, location, clue, interaction, encounter, session, or item merely to carry
   an image.
2. Use `portrait` for a person or creature likeness, `setting` for general location atmosphere,
   `scene` for artwork specific to an existing selectable situation, and `handout` for reviewed
   evidence or a player-facing document/object plate. A geographical map remains exclusively under
   `game.core.world.map.visual`.
3. Each slot has one closed `variants` object. Select only the exact requested `player` or `dm`
   variant. Never fall back between audiences and never reveal that another variant exists.
4. Each variant records only a bounded asset key, useful alt text, MIME type, pixel dimensions, and
   lowercase SHA-256. Admit new bytes with `commit(kind: "system.blob-upload.begin")`, transfer the
   raw body through its short-lived HTTP capability, and complete admission with
   `commit(kind: "system.blob-upload.finalize")`. The component never stores a URL, upload token,
   or filesystem path.
5. Record review evidence once per slot in `provenance`: origin kind, credit, authored source
   reference, review date, and positive version. Provenance and hashes are audit material and are
   not part of the ordinary Player read model.
6. Project media only after the owning entity and slot are authorized by the existing audience
   owner. An unrevealed clue, hidden person, denied location, or unauthorized scene contributes no
   URL, asset key, alt text, hash, identifier, count, placeholder, or existence signal.
7. For a current situation, prefer an authorized `scene` slot on the exact selected conversation or
   encounter, then a `scene` slot on the exact location, then that location's `setting` slot. Never
   infer a selector or participant from prose.
8. Replace the complete component after reading the current entity revision. Use `draft`, `active`,
   and `archived` only for media lifecycle; component revisions retain history and rollback evidence.

## Constraints
- The component contains no owner/entity/campaign ID, URL, path, map geometry, coordinates,
  discovery state, current-scene selector, participant list, reveal transition, mechanic, rule,
  effect, or generated story text.
- A valid descriptive visibility label on another component is not authorization. Readers must use
  the existing server-issued audience projection and fail closed on malformed, inactive, unknown,
  mismatched, or unregistered media.
- The generic blob store maps verified SHA-256 keys to immutable bytes only. It owns no identity,
  hierarchy, selection, audience, or fallback rule. `query(kind: "system.blobs", id: ...)`, the
  matching MCP resource, and the raw HTTP download are private-operator transfer surfaces; player
  delivery still passes through the owning entity's authorized projection.
- Adding, replacing, or removing media uses the generic effects transaction and normal audit
  evidence. Blob transfer adds no fourth MCP tool: discovery stays under `query` and lifecycle
  coordination stays under `commit`.
