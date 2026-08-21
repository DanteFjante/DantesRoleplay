# Session Feature S7 dependency plan — attributed narrative recaps and table artifacts

Status: **Planned; implementation awaits accepted S3 factual recap, an explicit trusted-host storage/retention decision, and real audience policy before any player exposure.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), S3, Campaign C5, the audit/history contract, and [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md). It writes no runtime artifact.

S7 owns durable, explicitly attributed **noncanonical** narrative artifacts. Its first fixture publishes one trusted-host narrative recap after a session ends, bound to the immutable factual recap that S3 owns. It may make that factual source easier to read, but can never change, replace, infer, or overrule it. S7 does not require S6: a useful narrative recap can be sourced from S3’s factual record even when gameplay actions were not session-correlated.

## Target capability

A trusted host can publish one bounded narrative recap for one ended session. The service resolves the session’s S3 factual recap itself, binds the artifact to the recap’s canonical source/version or digest, records unambiguous attribution, and stores the prose as an immutable artifact. A fresh host can retrieve the artifact together with its factual-source reference and clear noncanonical status.

The first fixture proves a reusable artifact-and-attribution boundary, not AI authorship, editable GM notes, a chat transcript, a player handout, scene art, an activity feed, or a public/party publication channel. Additional table-artifact kinds need their own declared source, visibility, retention, and lifecycle rules before they use the same boundary.

### Included

- One append-only `session-recap` narrative artifact for an ended session that has exactly one valid S3 factual recap.
- Server-resolved factual source binding, explicit trusted-host attribution, bounded title/body/language metadata, deterministic noncanonical labeling, and a bounded trusted-host readback.
- Safe content/size/encoding validation, duplicate publication protection, audit/event evidence, failure/rollback/fresh-host proof, and a retention/redaction decision before durable prose is accepted.
- A deliberate future extension seam for additional artifact kinds, only after their own owner and policy are confirmed.

### Excluded

- Mutating campaign/world/quest/character/item/action state; converting narrative statements into facts; auto-advancing a session; or generating/repairing an S3 factual recap.
- Chat, raw prompt/model context, hidden projections, internal reasoning, free-form GM note workspace, generic document storage/search, transcript retention, or an activity/event log.
- AI/model integration, unsourced automated recap generation, editable/overwritten prose, correction/withdrawal semantics, player/party/public visibility, browser write, notifications, or live collaboration.

## Ownership and source boundary

| Concern | Owner and S7 rule |
| --- | --- |
| Session lifecycle and factual recap | S1/S3/C8. S7 requires an ended session and reads its immutable recap; it writes neither session state nor factual recap data. |
| Factual campaign/world/quest/character/action data | Their existing owners. S7 may only present the already bounded S3 factual source and must not read additional raw state to embellish prose. |
| Narrative prose, artifact identity, attribution, source binding | S7. Narrative remains noncanonical even if it accurately paraphrases the source. |
| Author identity and audience | Identity/authorization policy, C5, and CH14. Initial attribution is the fixed trusted-host actor, not user-supplied identity; no party/public reader exists before real policy. |
| Model-generated content | A separate AI/provider and data-retention owner. S7 has no model, prompt, or model-attribution field in its first fixture. |
| Audit/events/history | Audit/history owner. S7 records the source/artifact relationship in its one root operation; it creates no parallel activity history. |
| Storage retention/redaction | Confirmed data-lifecycle owner. S7 cannot persist prose until retention, export, redaction, and deletion/withdrawal authority are explicitly decided. |

The artifact’s source binding is evidence of what it was based on, not a claim that every sentence is entailed by the source. Every reader must receive the artifact’s noncanonical label and a bounded factual-source reference. A stale, missing, malformed, inaccessible, or mismatched source fails closed; it is never silently substituted with current state or regenerated prose.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed boundary |
| --- | --- |
| Artifact component | `game.core.campaign.session.narrative-artifact` on a dedicated artifact entity. Its exact schema requires confirmation but must include fixed `kind: "session-recap"`, canonical source session/recap version-or-digest, server-set publication time, fixed trusted-host attribution, bounded language/title/body, and `noncanonical: true`. |
| Session-to-artifact relationship | `game.core.campaign.session.has-narrative-artifact`, directed session → artifact with empty relationship data. It supplies scope; the component carries source integrity metadata. |
| Procedure/mechanic | `procedure.campaign.session.narrative-artifact` and `mechanic.game.core.campaign.session.narrative-artifact.publish` for one validate-or-publish root. Exact names, entity allocator, component schema, relationship registration, event type, and audit shape require semantic-boundary confirmation. |
| Reader | A fixed trusted-host S7 projection or an approved C8 extension. It returns no arbitrary artifact ID, query/filter/search/format options, raw component data, model data, or alternate audience selector. |

Confirm the durable artifact IDs and schema, factual-recap version/digest representation, one-artifact uniqueness rule, canonical attribution vocabulary, text/encoding/locale limits, prohibited content handling, retention/export/redaction authority, audit/event correlation, error vocabulary, and read exposure before implementation. If the existing S3 recap cannot supply a stable source version or digest, amend S3 first; S7 must not invent one.

## Closed publication boundary

The initial request is exactly `{ operation: "validate" | "publish", sessionId, title, body, language }`. `sessionId` identifies an ended session; `title`, `body`, and `language` have confirmed bounded formats and limits. The caller cannot supply campaign/world/character/action IDs, factual summary fields, source digest/version, artifact ID, author/principal/role, visibility, model/prompt data, timestamps, audit/event IDs, lifecycle state, retention flag, correction target, or arbitrary metadata.

On validation or publish, the service resolves the trusted host, exact session, its S3 factual recap, and its source version/digest. It verifies same-root scope, ended lifecycle, a valid immutable source, confirmed retention policy, and the initial one-`session-recap`-per-session uniqueness rule before allocating an artifact. The system sets attribution and source metadata; it does not accept a caller claim that prose is factual, generated, safe for players, or authoritative.

Publish creates the artifact entity, attached component, session link, event/notification where confirmed, and success audit in one root transaction. Replayed/stale/duplicate publication, source mutation, missing policy, malformed text, cancellation, timeout, or any audit/event/link failure leaves no artifact, link, partial prose, or success record. There is no update operation: changed prose requires a future correction/withdrawal contract, not overwriting history.

## Dependency graph and slices

~~~text
S3 ended session + immutable factual recap
├─ stable factual-recap source version/digest                              [source leaf]
├─ trusted-host storage, retention, export, and redaction decision         [data-lifecycle leaf]
├─ artifact entity/component/link registration and one-root audit evidence  [S7 core]
├─ fixed trusted-host reader                                                 [S7 core]
└─ C5/CH14 authenticated audience policy                                    [later player exposure gate]
   ├─ Slice 1: source-bound trusted-host narrative recap
   └─ Slice 2: bounded fresh-host readback and failure/retention proof
      └─ Later artifact kinds, corrections, AI authorship, and S8 views require new plans
~~~

### Slice 1 — one immutable, source-bound trusted-host recap

**Prerequisites:** accepted S3; confirmed source version/digest and one-artifact rule; approved durable text retention; confirmed artifact vocabulary and trusted-host attribution.

1. Register the artifact entity/component/link and one validate-or-publish root only after permanent-ID confirmation.
2. Resolve the ended session and its factual source server-side; validate bounded prose and set fixed trusted-host attribution plus noncanonical label.
3. Publish exactly one source-bound `session-recap` artifact with its link, event/audit evidence, and no mutation of the session or factual source.
4. Test missing/active/unknown/cross-scope session, absent/malformed/changed factual source, duplicate/replay/stale publish, malformed/oversized content, policy absence, and all transactional failures.

**Exit:** one ended session with one valid factual recap can yield one immutable, attributed, visibly noncanonical narrative recap; nothing else becomes authoritative or changes.

### Slice 2 — bounded trusted-host readback and retention proof

**Prerequisites:** Slice 1; approved fixed reader and data-lifecycle policy; representative fixture with no hidden/player-sensitive source copied into prose fields by the service.

1. Return one bounded artifact summary with noncanonical label, attribution vocabulary, and factual-source reference from a fresh host.
2. Prove it does not return raw source components, hidden state, prompt/model data, arbitrary artifacts, generic search/history, or player/party/public data.
3. Exercise rollback, retention/export/redaction-policy denial, source mismatch, duplicate publication, and fresh-host source-reference readback. Run focused tests, full suite at acceptance, catalog validation if catalog changes, and protocol/security walks when their surfaces change.

**Exit:** trusted-host consumers can identify a narrative artifact, its attribution, and its factual source without mistaking it for a factual recap or gaining a generic narrative-data surface.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Source integrity | Artifact publish requires one ended S3 session and its exact immutable factual source/version-or-digest. Missing, stale, changed, or inaccessible source denies unchanged. |
| Noncanonical truth | Artifact is permanently labeled noncanonical and attributed. It does not modify, replace, supplement, or become input to campaign/world/quest/character/action truth. |
| Attribution | Initial attribution is server-set trusted host only. No caller-supplied author, player identity, model claim, or visibility label is trusted. |
| Publication atomicity | Entity, component, session link, event/notification where confirmed, and audit commit together once; every failure/replay/duplicate leaves no partial artifact. |
| Durable prose safety | No write occurs without confirmed retention/redaction/export policy, bounded content validation, and a fixed first-kind/one-artifact rule. |
| Read safety | Fixed trusted-host readback is bounded and source-aware, with no transcript, raw source data, arbitrary search, or unauthorised audience exposure. |
| Future breadth | Player-facing publication, AI creation, corrections, notes, handouts, and other artifact kinds require an amended/child plan with their own source, lifecycle, and policy contracts. |

## Evidence and change control

The implementation receipt records accepted IDs/schema, source digest/version proof, chosen retention and redaction authority, trusted-host attribution rule, canonical fixtures, noncanonical labeling/readback, duplicate/stale/source-mismatch denials, transaction rollback, and fresh-host evidence. It stores no raw hidden source projection, player data, transcript, prompt/model context, or duplicate factual recap.

Amend S7 before accepting AI-generated prose, storing prompts/models, making artifacts editable/correctable/withdrawable, adding another artifact kind, attaching images/files, exposing party/public/player reads, permitting browser writes, adding notifications or search, using session action history as recap input, or treating narrative as a game fact. Those changes belong respectively to an AI/data-lifecycle owner, a new artifact lifecycle plan, visual/file storage, C5/CH14/S8, Website/API, audit/history, S6/action owners, and the authoritative domain owner.
