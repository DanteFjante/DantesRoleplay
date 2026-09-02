---
id: procedure.game.core.world.knowledge
category: game.core.world.knowledge
name: Govern shared-game world knowledge
governs: commit(kind: "component") declaring game.core.world.fact, game.core.world.rumour, game.core.world.secret, game.core.world.clue, game.core.world.knowledge.classification, game.core.world.knowledge.validity, game.core.world.interaction, or game.core.world.knowledge.acquisition; commit(kind: "effects") recording or correcting knowledge records and links; commit(kind: "action") revealing one clue or confirming one rumour; the perspective-safe answer returned to a configured authenticated or development audience
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Records durable trusted-GM world knowledge. Facts, rumours, secrets, and clues are separate
entities with closed data; scope, subject, and evidentiary links are explicit relationships.

## Instructions
1. Use exactly one knowledge component on a knowledge entity. All records have complete status,
   summary, provenance, and descriptive visibility; provenance is text, never an entity ID.
2. Every knowledge entity has one empty-data `game.core.world.knowledge.in-world` link to its
   world root and one empty-data `game.core.world.knowledge.about` link to the entity it concerns.
   It also has one closed `game.core.world.knowledge.classification` companion component with a
   subject kind and descriptive sensitivity.
3. Every clue additionally has one empty-data `game.core.world.clue.supports` link to one fact or
   secret in that same world. It is evidence, not a copied truth or automatic reveal.
4. Facts are active/archived; rumours are unconfirmed/confirmed/disproved/archived; secrets are
   active/archived and always `gm`; clues are unrevealed/`gm` or revealed/`party`.
5. Record reviewed setting knowledge in one effects list ordered entity creation, components, then
   links. Only `mechanic.game.core.world.clue.reveal` and
   `mechanic.game.core.world.rumour.confirm` change reveal/confirmation state. Each receives the
   record and its claimed world in roles plus input `{}`, and proves the stored scope link itself.
6. Slice 1 records dissemination through a `game.core.world.knowledge.baseline` relationship from
   a world, region, or faction to one knowledge record with exact
   `{"inheritance":"current-scope"}` data. It records one actor's current explicit epistemic
   position through `game.core.world.knowledge.state` with exact `{"state":...}` data. The only
   initial states are `known`, `familiar`, `suspected`, `believed`, `doubted`, `disbelieved`, and
   `unknown`; `unknown` explicitly overrides a baseline. The trusted host must use the Slice 1
   knowledge coordinator to validate scope, endpoints, and precedence before writing either link.
7. Slice 2 records learning only through an accepted `game.core.world.interaction` with one empty
   `interaction.in-world` link and zero or more empty `interaction.participant` links. Each learned
   result is a separate `game.core.world.knowledge.acquisition` entity with exact closed method and
   resulting-state data, plus one empty-data link each to its world, actor knower, knowledge record,
   and source interaction. The trusted host must use the knowledge-acquisition coordinator so that
   interaction and acquisitions commit atomically and the `(source, knower, knowledge)` triple is
   replay-safe.
8. Slice 3 optionally records one closed `game.core.world.knowledge.validity` companion with
   inclusive `validFromMinute` and optional exclusive `validUntilMinute`, both measured against the
   scoped world root's existing clock. A timed successor may supersede exactly one timed prior
   through `game.core.world.knowledge.supersedes` only when both scoped world and `about` subject
   match and the prior end exactly equals the successor start. A contradiction is one empty-data
   `game.core.world.knowledge.contradicts` link stored in lexical stable-ID order between two
   same-world/same-subject knowledge records. The trusted host timeline coordinator validates all
   interval, endpoint, ordering, branching, and cycle rules before it writes.

## Constraints
- Summary and provenance are trimmed nonempty bounded text. Knowledge data contains no root,
  target, support, campaign, quest, player-belief, transcript, confidence, or access-control field.
- Scope/about/support links are directed, non-self, exact `{}` records. A clue supports only a fact
  or secret in its scoped world. Reverse, duplicate, cross-world, wrong-endpoint, and nonempty-data
  links violate this feature convention.
- Visibility is descriptive trusted-GM metadata only. It never authorizes a caller. Player-safe
  filtering and prose generation exist only through the separately bounded `knowledge-answer`
  query and its configured audience policy; this contract does not create an event, subscription,
  notification, campaign, quest, or automatic discovery system.
- Reveal changes only `unrevealed`/`gm` to `revealed`/`party`; confirmation changes only
  `unconfirmed` to `confirmed`. Neither action accepts a caller-supplied result, rewrites a secret,
  fact, support/about/scope link, summary, provenance, or visibility beyond the clue transition.
- Effective state is resolved as explicit actor state, then applicable faction or containing-region
  baseline, then world baseline, then derived `unknown`. Baselines do not materialize actor rows.
  Interaction acquisition is trusted-host-only: it does not introduce a dialogue, combat, quest,
  discovery engine, transcript, event-ledger inference, world clock, player authorization, public
  querying, or search surface. A participant never automatically learns every interaction result.
  Timed historical/current and contested projections are trusted-host-only. Validity never changes
  whether an actor knows a record, which canonical claim wins, rumour confirmation, secret
  sensitivity, or authorization. Scheduled future truth and acquisition timestamps remain outside
  this contract; the only player-safe read is the bounded perspective-safe answer surface.
- The perspective-safe answer is a separate bounded read surface with no query kind of its own;
  `query(kind: "system.audience-context")` reports the audience binding it requires. It
  accepts campaign ID, question, optional kind/subject filters, and optional world minute only;
  it never accepts a principal, actor, role, world ID, visibility override, canonical knowledge ID,
  or include-hidden flag. A host must supply an audience policy before this query can answer. The
  temporary development policy is disabled by default, permits one configured GM or actor seat,
  and must run only over loopback; it is not authentication and must be replaced before publishing.
