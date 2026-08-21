---
id: procedure.campaign.character-participation
category: campaign
name: Resolve campaign-owned character participation
governs: internal active-scope verification; later campaign attach and withdrawal operations
status: active
---

## Description

C15 owns the single campaign-owned participation scope for a pre-existing actor. Its read-only
verifier returns scope only when the campaign, participation state, and both canonical empty-data
links are structurally valid and active. Its Slice 2 attach request creates that complete structure
in one campaign transaction and never changes the actor.

## Instructions

1. Character and campaign consumers resolve an actor only through the internal active-scope
   verifier; they never read a campaign ID from a profile or accept one as a caller assertion.
2. A valid result requires one active campaign root, one active participation component, and the
   two canonical empty-data relationships. Any other graph shape has no usable campaign scope.
3. `commit(kind: "campaign")` accepts exactly `{ "operation": "attach-character-participation",
   "campaignId": "campaign.*", "actorId": "actor.*" }` for a trusted-host C15 attachment. The
   existing campaign kind is the public route; no new tool or kind exists.
4. The server derives the participation id as `campaignId + ".participation." + actorId`.
   Callers cannot provide it. An invalid/overlong derived id, collision, any prior participation
   history for the actor, inactive campaign, or absent actor rejects with no structural effects.

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
- Withdrawal remains a later CH13 composition seam; CH5 consumes the attachment planner rather
  than calling a nested campaign commit.
