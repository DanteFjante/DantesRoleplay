---
id: procedure.game.core.world.knowledge
category: game.core.world.knowledge
name: Govern shared-game world knowledge
governs: commit(kind: "component") declaring game.core.world.fact, game.core.world.rumour, game.core.world.secret, or game.core.world.clue; commit(kind: "effects") recording or correcting knowledge records and links; commit(kind: "action") revealing one clue or confirming one rumour
status: active
---

## Description

Records durable trusted-GM world knowledge. Facts, rumours, secrets, and clues are separate
entities with closed data; scope, subject, and evidentiary links are explicit relationships.

## Instructions

1. Use exactly one knowledge component on a knowledge entity. All records have complete status,
   summary, provenance, and descriptive visibility; provenance is text, never an entity ID.
2. Every knowledge entity has one empty-data `game.core.world.knowledge.in-world` link to its
   world root and one empty-data `game.core.world.knowledge.about` link to the entity it concerns.
3. Every clue additionally has one empty-data `game.core.world.clue.supports` link to one fact or
   secret in that same world. It is evidence, not a copied truth or automatic reveal.
4. Facts are active/archived; rumours are unconfirmed/confirmed/disproved/archived; secrets are
   active/archived and always `gm`; clues are unrevealed/`gm` or revealed/`party`.
5. Record reviewed setting knowledge in one effects list ordered entity creation, components, then
   links. Only `mechanic.game.core.world.clue.reveal` and
   `mechanic.game.core.world.rumour.confirm` change reveal/confirmation state. Each receives the
   record and its claimed world in roles plus input `{}`, and proves the stored scope link itself.

## Constraints

- Summary and provenance are trimmed nonempty bounded text. Knowledge data contains no root,
  target, support, campaign, quest, player-belief, transcript, confidence, or access-control field.
- Scope/about/support links are directed, non-self, exact `{}` records. A clue supports only a fact
  or secret in its scoped world. Reverse, duplicate, cross-world, wrong-endpoint, and nonempty-data
  links violate this feature convention.
- Visibility is descriptive trusted-GM metadata only. This contract does not provide player-safe
  filtering, authorization, a new query surface, an event, subscription, notification, campaign,
  quest, automatic discovery, or prose generation.
- Reveal changes only `unrevealed`/`gm` to `revealed`/`party`; confirmation changes only
  `unconfirmed` to `confirmed`. Neither action accepts a caller-supplied result, rewrites a secret,
  fact, support/about/scope link, summary, provenance, or visibility beyond the clue transition.
