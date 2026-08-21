# Session Feature S4 implementation plan — ended-session checkpoint evidence

Status: **In progress; Slices 0–1 are accepted and verified. Slice 2 atomic capture remains pending.**
Last updated: 2026-08-21

## Target and hard boundary

S4 gives a trusted host one named, durable checkpoint for one already-ended S3 session. The
checkpoint points to exactly one accepted SP1 campaign-session evidence package and can later be
read as bounded evidence. It is the stable handoff for Campaign C11's future no-write fork preview.

S4 is not a save-game, database copy, current-state export, restore point, fork, archive, player
feature, package browser, or package-byte reader. It captures no active session, creates no
implicit checkpoint, changes no lifecycle/recap, and mutates no campaign/world/quest/character/item
or action state.

Repository files are authoritative during this work. The plan follows `AGENTS.md`,
`procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, accepted
S0–S3, accepted Snapshot SP0/SP1, and the S4 dependency plan.

## Dependency analysis

| Concern | Existing owner/evidence | S4 boundary |
| --- | --- | --- |
| Ended lifecycle and recap | Accepted S1–S3; `ICampaignSessionRecapReader` | Consume one valid ended S3 session; do not parse/rewrite recap or transition lifecycle. |
| Package bytes and integrity | Accepted SP1 producer/store | Start the outer root, ask SP1 to produce/stage, and retain only its byte-free reference. |
| Transactions/effects/audit | Existing C8 root, `EffectApplier`, `OperationLog` | S4 owns one outer transaction, structural checkpoint effects, and audit. SP1 never commits it. |
| Scope identity | Session scope plus SP1 reference | Copy only derived campaign/world ids and package reference metadata. Never copy world state. |
| Checkpoint reference/read | New S4 owner | Own a checkpoint entity, component, session link, strict trusted-host read, and closed failures. |
| Fork classification | C11 | C11 is downstream: it consumes S4's stable metadata but supplies no blocking classification decision. |
| Restore/open/retention | Future S4 branch and Snapshot SP2–SP4 | Explicitly absent. No temporary API or SQL bypass. |
| Player authorization | C5/CH14 | Trusted host only; labels do not confer player access. |

### Resolved leaves

- SP1 is accepted: immutable BLOB package, SHA-256 integrity, byte-free verification, and
  caller-owned transaction participation.
- SP1's producer accepts only one valid ended S3 session and pins `procedure.campaign.session`.
- S3 supplies an immutable ended-session recap; S2 remains the active-session continuity path.
- C8 already provides the `campaign` commit kind, root transaction/audit pattern, and fixed session
  read precedent.

### Remaining semantic leaves

- Checkpoint component/link IDs, runtime-id namespace, public campaign operation names, fixed query
  kind, and public result schema remain unratified.
- C11 classification is deliberately downstream. Restore lacks every required owner/authorization/
  conflict decision and remains blocked.

## Semantic confirmation gate

Confirm all of these together before Slice 1 authors catalog records or public shapes:

- Component: `game.core.campaign.session-checkpoint`.
- Relationship: `game.core.campaign.session.has-checkpoint`, directed `session.* → checkpoint.*`,
  exact empty `{}` data.
- Runtime id: server-generated `checkpoint.` plus lowercase GUID `N` (43 characters); callers never
  choose it.
- Boundary: exactly one ended S3 session with one valid S3 recap; active/archived/malformed/
  cross-scoped/already-checkpointed sessions fail unchanged.
- Cardinality: one S4 checkpoint per session. A second capture rejects; it never reuses, overwrites,
  or creates another package.
- Existing contract: amend/version `procedure.campaign.session`; cite `procedure.snapshot.package`.
  Do not create a checkpoint procedure.
- Existing `campaign` commit kind gains only `validate-session-checkpoint` and
  `checkpoint-session`; no new tool or commit kind.
- One fixed trusted-host query kind: `session-checkpoint`, exactly one `checkpoint.*` id and no
  filters. It verifies evidence and returns metadata only.
- Evidence-only lifecycle: S4 has no update/delete/retire operation; availability is verified from
  SP1 at capture/read time.
- No restore/fork/list/open/download/player/browser capability, bytes, locator, root operation id,
  transcript, raw graph data, or caller-supplied snapshot metadata.

Confirmation authorizes this permanent vocabulary, schema, and MCP surface change. It does not
authorize Snapshot SP2–SP4, persistent import, restore, a fork, or player access.

## Closed schemas

### Checkpoint component

`game.core.campaign.session-checkpoint` is attached once to the new checkpoint entity. Its closed
camel-case v1 JSON property order is:

~~~json
{
  "protocolVersion": "session.s4.evidence-only.v1",
  "sessionId": "session.*",
  "campaignId": "campaign.*",
  "worldId": "world.*",
  "package": {
    "id": "snapshot.*",
    "scopeContractId": "procedure.campaign.session",
    "scopeContractVersion": 1,
    "producerId": "snapshot.producer.campaign-session-evidence",
    "producerVersion": 1,
    "contentEncoding": "dantes-canonical-json-v1",
    "boundaryFingerprint": "lowercase sha256 hex",
    "digestAlgorithm": "sha256",
    "contentDigest": "lowercase sha256 hex",
    "byteCount": 1,
    "capturedAt": "UTC round-trip timestamp",
    "availability": "available"
  }
}
~~~

The component copies the complete **byte-free** SP1 reference as a durable declaration. It is not a
second payload store. It excludes `Content`, `RootOperationId`, storage names/paths/credentials,
arbitrary metadata, domain classification, restore state, audit/event ids, and current domain state.

The catalog schema requires every field, rejects additional properties, enforces canonical id and
lowercase digest patterns, positive versions/byte count, `availability: "available"`, and UTC
`capturedAt`. C# owns graph/cardinality and cross-row checks.

### Requests, results, and C11 handoff

The existing `commit(kind: "campaign")` accepts only:

~~~json
{"operation":"validate-session-checkpoint","sessionId":"session.*","expectedStatus":"ended"}
{"operation":"checkpoint-session","sessionId":"session.*","expectedStatus":"ended"}
~~~

No other property is accepted: callers cannot supply campaign/world/checkpoint/package ids, package
content/digest/reference, boundary, audience, effect, or restore/fork option.

Validation returns only `sessionId`, derived `campaignId`, `checkpointAvailable: true`, and one
literal next call. Capture returns only `checkpointId`, `sessionId`, derived `campaignId` and
`worldId`, `scopeContractVersion`, verified `availability`, and one literal checkpoint query.

`query(kind: "session-checkpoint", id: "checkpoint.*")` accepts no other field and returns only
checkpoint/session/campaign/world ids; scope contract id/version; producer id/version; encoding;
digest algorithm; boundary fingerprint; content digest; byte count; captured-at; verified
availability; `evidenceStatus`; and literal next action. It never returns bytes, raw component data,
storage details, or operation/event ids.

The internal reader returns the same typed metadata to C11 by checkpoint id. C11 receives no bytes,
current state, package-open method, or write/fork effect. SP1 failure becomes a closed S4 evidence
failure rather than a substitute from current state or history.

## Algorithms and fixed failures

### Zero-effect validation

`validate-session-checkpoint` validates exact request shape/id/status, resolves the existing S3
recap reader for one ended session/campaign, and checks the checkpoint relationship set for existing,
duplicate, reversed, dangling, cross-scoped, or nonempty-data links. It confirms internal SP1
dependencies are registered but does not produce/stage a package. It writes no entity, component,
relationship, package, effect, event, notification, or audit beyond the normal validation envelope.

### Atomic capture root

`checkpoint-session` allocates its root operation id, begins the C8 transaction, then repeats all
validation; it never consumes a preview.

1. Validate using the validation operation; reject before staging when invalid.
2. Call `ICampaignSessionEvidenceProducer.ProduceAsync(sessionId)` inside the transaction. Propagate
   its closed failure; never reconstruct its bytes or accept caller content.
3. Call `ISnapshotPackageStore.StageAsync(proposal, rootOperationId)` inside that transaction.
4. Generate a checkpoint id and derive exactly three effects: entity create, checkpoint component
   add, and session→checkpoint relationship add. Serialize only the staged byte-free reference and
   derived ids into the component.
5. Dry-run then apply the effects under the same root operation id. No special S4 event type or
   notification is introduced; existing structural events are sufficient.
6. Record one successful campaign operation with checkpoint subject and session-procedure citation,
   commit, then return success. A reference is never published before commit.

Any validation/producer/stage/effect/guard/reaction/event/audit/cancellation/timeout failure rolls
back package and graph writes, clears tracking, and never publishes a checkpoint id. Expected
rejection uses the established C8 failed-operation audit only after rollback. Cancellation propagates.

### Read

Resolve one alive `checkpoint.*` entity with one component and one incoming session link. Validate
the component and graph scope, then call `ISnapshotPackageStore.VerifyAsync` with its retained
reference. Read no package bytes, current recap/world/quest state, operation history, or alternate
checkpoint. Map package missing/mismatch/unavailable/corrupt failures to one closed evidence failure.

Use literal codes:

- `INVALID_SESSION_CHECKPOINT_REQUEST` — malformed/unknown request shape;
- `SESSION_CHECKPOINT_REQUIRES_ENDED_SESSION` — invalid ended S3 boundary;
- `SESSION_CHECKPOINT_ALREADY_EXISTS` — second or ambiguous link;
- `SESSION_CHECKPOINT_SCOPE_INVALID` — malformed/reversed/dangling/cross-scope/nonempty graph;
- `SESSION_CHECKPOINT_CAPTURE_FAILED` — closed SP1 producer/store refusal;
- `CHECKPOINT_NOT_FOUND` — no alive canonical checkpoint;
- `CHECKPOINT_EVIDENCE_INVALID` — malformed component/reference; and
- `CHECKPOINT_EVIDENCE_UNAVAILABLE` — SP1 missing/mismatch/unavailable/corrupt evidence.

Each failure contains one literal recovery call and withholds protected reference data.

## Slices

Only one slice may be in progress. A dependent slice starts only when its predecessor exit is true.

### Slice 0 — semantic ratification

**Status: Accepted 2026-08-21.**

Confirm the full gate: IDs, component schema, cardinality/boundary, operations, fixed query,
trusted-host audience, result fields, transaction/audit owner, C11 handoff, and exclusions.

**Exit:** no unresolved choice can change permanent vocabulary, public surface, schema meaning, or
atomicity ownership.

### Slice 1 — checkpoint vocabulary and zero-effect readiness

**Status: Implemented and verified.** See
[Slice 1 validation](SESSION-FEATURE-04-SLICE-1-VALIDATION.md).

**Prerequisites:** Slice 0 accepted; S1–S3/SP1 focused suites green.

Expected files: session procedure revision; two catalog component files; Campaign request/result/
validator types; data-access validator; DI registration; focused tests.

1. Add the confirmed component/schema and procedure boundary. Add no checkpoint entity fixture.
2. Add closed types and `ICampaignSessionCheckpointValidator` in the existing Campaign namespace.
   Snapshot types stay in their own namespace.
3. Reuse the S3 recap reader and exact graph inspection for zero-effect readiness; do not stage a
   package or create persistence state.
4. Test valid ended fixture, active/missing/corrupt recap, malformed request, and every invalid
   checkpoint graph/cardinality case. Prove no mutation/effect/event/audit.
5. Run focused tests and `roleplay validate catalog`.

**Exit:** one eligible ended session is deterministically identified; every invalid/already-
checkpointed case fails closed with zero mutation.

**Excluded:** stage, checkpoint write, public dispatch, C11 reader, restore/fork, retention, player
access.

### Slice 2 — atomic capture and named reference

**Prerequisites:** Slice 1 accepted; SP1 stage behavior remains green.

Expected files: S4 root creator/component serializer; existing campaign-tool and commit dispatch
updates; capability/guard updates; focused tests. It adds no MCP tool or commit kind.

1. Implement the exact capture algorithm and root ownership above.
2. Add only entity-create/component-add/relationship-add effects after successful SP1 stage.
3. Register only typed validator/creator. Extend existing `campaign` dispatch and `VerbSurface`
   for the two approved closed operations; expose bounded envelopes only.
4. Test commit, rollback, cancellation, stage/effect/guard/event/audit failure, id collision,
   duplicate/replay, fresh-context visibility, and isolation from lifecycle/recap/external state.
5. Run focused tests, catalog validation, and the MCP protocol walk because campaign payload
   behavior changed.

**Exit:** one named checkpoint and package commit atomically; a failed root leaves neither durable.

**Excluded:** checkpoint query/C11 reader, restore/fork, list/search/open/download, retention, and
player/browser access.

### Slice 3 — verified readback and C11 handoff

**Prerequisites:** Slice 2 accepted; Slice 0 public-query confirmation remains valid.

Expected files: internal checkpoint reader; query tool dispatch/shape guard/capability entry;
campaign query delegate; focused tests.

1. Implement reader plus SP1 verification mapping with no byte return.
2. Add exactly `session-checkpoint` to the flat query surface with strict one-id validation and a
   matching dispatch. Do not add a generic snapshot query.
3. Expose the same no-write typed result to C11. Do not implement C11 classification or a fork.
4. Test fresh-host readback, missing/wrong id, malformed/duplicate/reversed/cross-scope graph,
   malformed reference, every SP1 evidence failure, strict no-filter behavior, and no C11 write.
5. Run focused tests, catalog validation if text changed, and protocol walk.

**Exit:** a fresh trusted host and C11 can read verified bounded evidence without bytes or fork.

### Slice 4 — evidence-only acceptance

**Prerequisites:** Slices 1–3 green.

1. Rerun S3, SP1, S4, migration, MCP guard/protocol, and C11 handoff focused tests.
2. Run `roleplay validate catalog` and the full solution suite once.
3. Review for bytes/list/open paths, caller-derived checkpoint values, extra owners, transaction
   adaptation, update/delete/retention, restore/fork/domain copy, or player access.
4. Write a concise receipt and request human feature acceptance.

**Exit:** S4 is accepted as evidence-only. C11 may start its read-only classification feature;
restore remains unavailable.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Eligible session | One ended S3 session with recap is eligible; validation writes nothing. |
| Atomicity | Package, checkpoint graph, structural events, and success audit commit together or roll back. |
| Id/cardinality | Server generates one id; replay creates no second package/link. |
| Isolation | Only reference metadata persists; no bytes/current state/transcript/effect/audit id/locator. |
| Integrity | Fresh read derives graph scope and verifies SP1 reference; bad evidence fails closed. |
| C11 handoff | C11 receives deterministic metadata only and makes no write. |
| Surface | Existing campaign kind gains two operations; one strict query kind is advertised and dispatched. |
| Deferred scope | No restore/fork/retention/list/open/download/player/browser behavior exists. |

## Terra High handoff

For each slice, Terra High re-reads `AGENTS.md`, this plan, the S4 dependency plan, accepted SP1
plan/receipt, `procedure.campaign.session`, `procedure.snapshot.package`, and only active-slice
files. It inspects the dirty workspace, preserves unrelated edits, and amends this plan rather than
inventing an owner or permanent meaning. It must not create another snapshot table, substitute
current state for failed evidence, or start the next slice before the current focused exit passes.
