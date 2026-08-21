---
id: procedure.campaign.session
category: campaign
name: Validate and start a campaign session
governs: commit(kind: "campaign") operations validate-session and start-session
status: active
---

## Description

Session S1 validates whether a C3-readable campaign can start its next session, then atomically
creates its minimal scoped record. It creates no gameplay or context state.

## Instructions

1. Send exactly `{"operation":"validate-session","campaignId":"campaign.*","sessionId":"session.*"}`.
2. The campaign must expose the trusted-host C3 resume; use that context rather than copying it
   into a session record.
3. To start, send the same closed shape with `"operation":"start-session"`. The start repeats
   readiness checks inside its transaction; a prior preview never authorizes it.
4. Read the returned session entity after a successful start. Resume/context remains a later
   session feature.
5. To resume the current trusted-host context, call
   `query(kind: "campaign-resume", id: "campaign....", includeSession: true)`. It reads the
   active-session header and current C3 projection without changing either.

## Constraints

- `validate-session` never creates a session, relationship, structural event, notification,
  recap, checkpoint, or quest effect. Its ordinary zero-effect `commit` operation record remains
  protocol history, not session state.
- `start-session` atomically creates exactly one entity, its lifecycle component, and its empty
  campaign scope relationship, plus the ordinary structural events and successful audit. Any
  failed start leaves none of those session artifacts.
- The `includeSession` resume mode requires exactly one complete active scoped session and returns
  only its header plus C3's existing bounded trusted-host view. Quest, audience, participant,
  recap, checkpoint, and gameplay sections remain out of scope.
- A campaign has at most one active session. Retained ended sessions use positive, contiguous,
  append-only ordinals.
- Each session is scoped only by one empty-data `game.core.campaign.has-session` relationship
  from its campaign; the session component holds only `status` and `ordinal`.
- C4/Q3 quest, C5 participant, character, item, encounter, and checkpoint information is out of
  scope.
