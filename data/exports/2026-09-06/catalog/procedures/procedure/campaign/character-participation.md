---
id: procedure.campaign.character-participation
category: campaign
name: Resolve campaign-owned character participation
governs: internal active-scope verification; campaign attachment; root-composable participation withdrawal
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
C15 owns the single campaign-owned participation scope for a pre-existing actor. Its read-only
verifier returns scope only when the campaign, participation state, and both canonical empty-data
links are structurally valid and active. Its Slice 2 attach request creates that complete structure
in one campaign transaction and never changes the actor. Its internal withdrawal planner returns
only a complete active-to-withdrawn component replacement for a lifecycle root to apply.

## Matches

## Instructions
1. Character and campaign consumers resolve an actor only through the internal active-scope
   verifier; they never read a campaign ID from a profile or accept one as a caller assertion.
2. A valid result requires one active campaign root, one active participation component, and the
   two canonical empty-data relationships. Any other graph shape has no usable campaign scope.
3. The attachment accepts exactly `{ "operation": "attach-character-participation",
   "campaignId": "campaign.*", "actorId": "actor.*" }` for a trusted-host C15 attachment. There
   is no `campaign` commit kind and no dedicated tool for it. An application supplies this operation as a mechanic: resolve it with
  `query(kind: "system.interaction-plan")` and run it with
  `commit(kind: "system.interaction-execute")`.
4. The server derives the participation id as `campaignId + ".participation." + actorId`.
   Callers cannot provide it. An invalid/overlong derived id, collision, any prior participation
   history for the actor, inactive campaign, or absent actor rejects with no structural effects.
5. A character-lifecycle root may call the internal withdrawal planner with only an actor id. It
   resolves the exact active scope and returns one `component.set` to `{ "status": "withdrawn" }`.
   The planner accepts no campaign assertion, does not create a public operation, and never
   opens a transaction, applies effects, emits events, or writes an audit record.

## Constraints
- The participation component is exactly `{ "status": "active" | "withdrawn" }`; it never
  carries a campaign ID, actor ID, profile, item, source, account, authorization, reason, or date.
- `game.core.campaign.has-character-participation` points from a campaign root to one
  participation; `game.core.campaign.character-participation.for-actor` points from that
  participation to one actor. Both relationship payloads are exactly `{}`.
- Only one structurally valid active participation may provide scope for an actor. A missing,
  withdrawn, malformed, duplicate, or inactive-campaign graph provides no scope.
- Attachment creates exactly one participation entity, one complete active state component, and
  the two empty-data links in one transaction. It writes no actor component, character profile,
  source choice, item, XP, authorization, or lifecycle state.
- Withdrawal is an internal C15 composition seam, not a standalone campaign command. CH13 must
  append its typed fragment to the lifecycle root transaction; CH5 similarly consumes the
  attachment planner rather than calling a nested campaign commit.
