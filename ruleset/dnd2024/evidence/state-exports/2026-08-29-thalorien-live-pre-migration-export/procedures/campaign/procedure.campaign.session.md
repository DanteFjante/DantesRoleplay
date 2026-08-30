---
id: procedure.campaign.session
category: campaign
name: Validate, start, and end a campaign session
governs: commit(kind: "campaign") operations validate-session, start-session, validate-session-end, end-session, validate-session-checkpoint, and checkpoint-session; query(kind: "campaign-resume") with includeSession and query(kind: "session-recap")
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Session S1 validates whether a C3-readable campaign can start its next session, then atomically
creates its minimal scoped record. S3 validates then atomically ends one active session with an
immutable C3-only factual recap. S4 validates and captures one evidence-only checkpoint for an ended
S3 session. These operations create no gameplay or external owner state.

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
6. To validate closing one session, send exactly
   `{"operation":"validate-session-end","sessionId":"session.*","expectedStatus":"active"}`.
   Campaign scope, current C3 chapter/arc, and up to five C3 milestones are derived at validation;
   the caller cannot provide a campaign id, recap, source, prose, or any other field.
7. To end, send the same closed session-end shape with `"operation":"end-session"`. It repeats
   all resolution inside one root transaction; a closing validation is never an authorization token.
8. Read one retained factual record only with
   `query(kind: "session-recap", id: "session....")`. It is trusted-host-only, requires an ended
   session, and returns only the derived session/campaign identity and immutable bounded recap.
9. To validate checkpoint capture, send exactly
   `{"operation":"validate-session-checkpoint","sessionId":"session.*","expectedStatus":"ended"}`.
   To capture, send the same closed shape with `"operation":"checkpoint-session"`. Capture repeats
   all checks inside one root transaction and callers cannot supply checkpoint/package content or ids.

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
- `validate-session-end` has zero effects and returns only closure metadata: session/campaign ids,
  preview availability, and canonically sorted section keys. It never returns or stores recap
  source text. It blocks when C3 has no complete current active chapter and arc, or has malformed
  or more than five milestones. C3 milestone order is preserved and its event id is never copied.
- `end-session` adds exactly one valid recap component then wholly replaces that session's lifecycle
  with `{"status":"ended","ordinal":...}` in the same transaction. It creates no scope link,
  campaign/world/quest/character/item/action effect, checkpoint, special session event, or
  notification. Any failed/cancelled/replayed end leaves neither recap nor ended lifecycle state.
- `session-recap` accepts only one canonical session id and no filters. It never reads a transcript,
  generic history, graph, or current resume context, and cannot make an ended session active.
- The private `snapshot.producer.campaign-session-evidence` v1 producer may, only inside an
  already-open owner transaction, serialize one valid ended session into the closed
  `dantes.snapshot.campaign-session-evidence` v1 package. It derives campaign/world scope from
  existing links and uses the immutable S3 recap in its existing order. It accepts only a session
  id and copies no current campaign/world state, GM context, quest, character, item, transcript,
  event/audit id, checkpoint, restore instruction, or caller-provided bytes/digest/scope.
- `validate-session-checkpoint` accepts only one valid ended S3 session and makes zero structural
  writes. It is trusted-host-only and returns bounded readiness metadata.
- `checkpoint-session` repeats validation inside one root transaction, asks accepted SP1 to stage
  one opaque package, then creates exactly one `checkpoint.*` entity, its byte-free checkpoint
  component, and exact session link. Any failed/cancelled root leaves neither package nor checkpoint
  durable. It returns no bytes, digest, timestamp, storage locator, root operation id, or current
  domain state.
- S4's checkpoint validator accepts only one valid ended S3 session and
  rejects malformed requests, invalid historical recap/session scope, and any existing, malformed,
  reversed, dangling, cross-scoped, or nonempty-data checkpoint link. It neither stages SP1 content
  nor returns bytes, reference metadata, checkpoint identity, current state, or restore authority.
  S4 reserves one future `checkpoint.*` entity linked from its session by exact empty-data
  `game.core.campaign.session.has-checkpoint`; `game.core.campaign.session-checkpoint` stores only
  the byte-free SP1 reference.
- C4/Q3 quest, C5 participant, character, item, encounter, and checkpoint information is out of
  scope.

