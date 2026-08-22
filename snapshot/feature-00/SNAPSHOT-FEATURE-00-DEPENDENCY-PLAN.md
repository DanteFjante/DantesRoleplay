# Snapshot Feature SP0 dependency plan — ratify immutable package storage and provenance

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Accepted; SP1 is accepted as the immutable SQLite package foundation. Consumer admission remains Session S4 work.**
Last updated: 2026-08-21

## Parent and target

This plan implements the first decision leaf in the
[Snapshot operations roadmap](../../SNAPSHOT_OPERATIONS_PLAN.md). It supplies the generic
snapshot/save owner required by [Session S4](../../session/feature-04/SESSION-FEATURE-04-DEPENDENCY-PLAN.md)
and consumed later by Campaign C11. SP0 is a semantic-ratification feature: it creates no runtime
record, table, component, backend, query, or commit operation.

The target is one accepted, implementable contract for an immutable opaque snapshot package. A
later SP1 implementation must be able to capture one producer-supplied package atomically with its
reference without guessing storage, scope, retention, provenance, or recovery semantics.

## Included and excluded

Included:

- A single storage-medium and atomic-commit decision for the first package fixture.
- A generic package/reference/provenance vocabulary and immutable lifecycle proposal.
- Typed producer/consumer boundaries, scope-contract versioning, integrity evidence, and bounded
  metadata read requirements.
- An explicit compatibility handoff for S4 evidence-only checkpoints and C11 fork preview.

Excluded:

- Implementing capture, storage, package bytes, a database/file/cloud copy, a public tool/query,
  a session checkpoint link, fork creation, restore, data deletion, encryption/key management,
  player access, automatic snapshots, or cross-domain state semantics.

## Existing-owner audit

| Concern | Owner status | SP0 rule |
| --- | --- | --- |
| Session checkpoint/reference | S4 plan exists; runtime blocked | S4 owns the session link and boundary timing, never generic bytes/storage. |
| Campaign fork classification | C11 plan exists; blocked on checkpoint evidence | C11 consumes reference/provenance only and never captures/copies. |
| Session lifecycle/recap | S1–S3 accepted | Snapshot records cannot mutate or stand in for lifecycle/recap. |
| Authorization | CH14 plan exists; restore-only dependency | SP0 remains trusted-host architecture; it grants no reader or restore authority. |
| Domain restore classification | S4/C11 and individual domain owners | SP0 stores opaque content and cannot classify or restore a domain. |
| Generic snapshot storage/provenance | **No owner plan existed** | SP0 owns this missing boundary. |

## Decisions required for ratification

The following decisions are semantic boundaries and require human confirmation before SP1 names
permanent runtime ids or writes a migration.

1. **First storage medium and atomicity.** Choose one of: same database transactional blob/table;
   transactional external object store with a confirmed prepare/commit/compensation protocol; or
   declare capture unavailable. A filesystem/database-file copy, best-effort external write, or
   two independent commits is not acceptable.
2. **Retention/availability.** Decide whether the first fixture is immutable available forever,
   explicitly retired-but-readable, or has a governed expiry/retention owner. It must never
   silently delete a referenced package.
3. **Package visibility.** The first fixture is in-process trusted-owner only. Confirm whether
   bounded metadata is later trusted-host MCP-readable; raw bytes/storage locators remain private.
4. **Scope contract handoff.** Confirm one producer-owned `scopeContractId` and immutable version
   format. S4 may later supply a C3/session evidence profile, but SP0 cannot choose its domains.
5. **Provenance and integrity.** Confirm digest algorithm/version, canonical encoding, producer
   identity/version, capture timestamp source, size limit, and unavailable/corrupt behavior.
6. **Consumer admission.** Confirm that only registered in-process scope/restore/fork consumers
   may open verified content; no generic MCP/browser/download endpoint is authorized.

## Confirmed decision

On 2026-08-21, the host selected **same SQLite database transactional BLOB/table storage** for
the first package fixture. SP1 must persist the immutable captured package and its reference in
one SQLite transaction; it must not use a separate snapshot file, database-file copy, object
store, best-effort upload, or later catalog import for captured runtime bytes.

The feature's contracts, schemas, catalog definitions, migration, and tests remain repository
authored and use the normal validate/import workflow. Only the package produced from a running
campaign is runtime state, created directly in SQLite at capture time.

## Recommended remaining ratification

Repository analysis found that the first package should not be a database copy or an attempted
restorable campaign save. Worlds, campaigns, and sessions coexist in one database; copying that
database would capture unrelated scopes, while selecting restorable rows would make the generic
store decide Campaign, World, Quest, Character, and Item semantics it does not own.

The recommended first fixture is therefore one **ended-session evidence package**. Its producer
reads one immutable S3 session lifecycle record, its single campaign scope link, the campaign's
single world link, and the session's immutable `session.s0.c3-only.v1` recap. This is a real
durable point-in-time artifact useful to S4 checkpoint evidence and C11 classification, but it is
not sufficient or authorized for restore.

The remaining SP0 decisions are recommended as follows and still require one host confirmation:

| Decision | Recommended v1 choice |
| --- | --- |
| Retention | Available indefinitely. SP1 exposes no retire, delete, purge, overwrite, or correction operation. SP4 must add any later lifecycle. |
| Visibility | No SP1 MCP/browser/list/download surface. S4 may later return its own bounded checkpoint metadata; package bytes and internal storage fields remain private. |
| Scope contract | Reuse and amend the existing owner `procedure.campaign.session`; store the exact positive imported contract version used at capture. Its checkpoint amendment owns the exact payload below. The scope is one ended session and its derived campaign/world identity, not campaign/world state. |
| Producer | Proposed stable producer id `snapshot.producer.campaign-session-evidence`, version `1`, implemented as a typed in-process C# producer. No caller supplies bytes, scope ids, digests, fields, or domain lists. |
| Integrity | `dantes-canonical-json-v1` UTF-8 bytes, SHA-256 lowercase hexadecimal digest, producer-issued boundary fingerprint, 64 KiB producer limit, and 1 MiB generic-store hard limit. Any mismatch or malformed source fails closed. |
| Consumer admission | SP1 admits verification only and returns metadata, never bytes. SP3 must name and register any future in-process payload consumer; S4 and C11 do not gain implicit open permission. |

### Exact first BLOB payload

The BLOB is the UTF-8 encoding of this closed logical shape, emitted with `Utf8JsonWriter` in the
shown property order and with no insignificant whitespace:

~~~json
{
  "format": "dantes.snapshot.campaign-session-evidence",
  "formatVersion": 1,
  "session": {
    "id": "session.*",
    "status": "ended",
    "ordinal": 1
  },
  "scope": {
    "campaignId": "campaign.*",
    "worldId": "world.*"
  },
  "recap": {
    "protocolVersion": "session.s0.c3-only.v1",
    "chapter": {
      "id": "...",
      "status": "active",
      "title": "...",
      "partyQuestion": "..."
    },
    "arc": {
      "id": "...",
      "status": "active",
      "title": "...",
      "partyStake": "..."
    },
    "milestones": [
      {
        "chapterId": "...",
        "title": "...",
        "closingSummary": "...",
        "timestamp": "2026-08-21T12:34:56.0000000Z",
        "sequence": 0
      }
    ]
  }
}
~~~

The producer preserves the already-validated recap value and C3 milestone order. The payload has
no capture timestamp, operation/event/audit id, raw component or relationship row, GM context,
quest, character, item, world state, transcript, generated prose, storage locator, restore flag,
or caller-defined metadata. Capture time and audit provenance belong to the package row/reference,
not the content digest.

`dantes-canonical-json-v1` means this explicit writer and schema, not arbitrary JSON object-key
sorting. IDs and strings retain their validated UTF-8 values, integers use their shortest decimal
form, arrays retain owner-defined canonical order, and the writer emits no BOM or whitespace. The
content digest is SHA-256 over the exact BLOB bytes. The boundary fingerprint is a separate
producer SHA-256 over length-prefixed canonical source facts: session id, complete lifecycle JSON,
campaign scope endpoints/data, campaign world-link endpoints/data, and complete recap JSON. This
lets the producer reject a source change between validation and capture even when the envelope
format itself remains unchanged.

### Recommended persistence boundary

SP1 adds one generic infrastructure row, not a game component:

| Column | Constraint and meaning |
| --- | --- |
| `Id` | Primary key, server-generated canonical `snapshot.*`, maximum 200 characters. A successful capture binds it permanently; callers never select storage identity. |
| `ScopeContractId`, `ScopeContractVersion` | Required stable producer-owned scope semantics; 200-character id and positive integer version. |
| `ProducerId`, `ProducerVersion` | Required stable in-process producer provenance; 200-character id and positive integer version. |
| `ContentEncoding` | Required fixed value `dantes-canonical-json-v1` for this fixture. |
| `BoundaryFingerprint` | Required 64-character lowercase SHA-256 hexadecimal value. |
| `DigestAlgorithm`, `ContentDigest` | Required `sha256` plus 64-character lowercase hexadecimal digest of exact stored bytes. |
| `ByteCount` | Required positive 64-bit count, equal to SQLite `length(Content)` and no greater than the generic 1 MiB limit. |
| `CapturedAt` | Server-generated UTC `DateTime`, outside producer/caller control. |
| `RootOperationId` | Required server-generated root audit/correlation id, private in SP1 metadata output. |
| `Availability` | Required `available`; no SP1 transition exists. |
| `Content` | Required SQLite BLOB. Never returned by an SP1 public or generic read. |

The EF model and migration should enforce required lengths, positive versions/count, digest shape,
and byte-count/content agreement where SQLite supports a check constraint. The application exposes
no update/delete method. A migration-level immutability trigger blocks package update and delete;
SP4 must introduce a separate availability history/state owner rather than weakening immutable
content to implement retirement.

### Transaction and interface rule

The typed producer reads and validates its immutable sources only after the owning root transaction
has begun. The generic store validates the proposal, computes the content digest itself, stages the
package, and requires an existing `DantesRoleplayDbContext` transaction; it never starts or commits
one. S4 will own the outer transaction that composes package insertion, checkpoint entity/link,
ordinary structural evidence, and success audit. This matches the existing EffectApplier rule that
joins a caller-owned transaction and prevents a committed package with a missing checkpoint—or a
checkpoint pointing to rolled-back bytes.

SP1 core interfaces contain only generic snapshot vocabulary. The campaign-specific producer
interface belongs to the Campaign/session owner. MCP handlers receive neither interface directly;
the later S4 coordinator is the only public-operation composition point.

SP1 also proposes one new authored generic contract id, `procedure.snapshot.package`, governing
immutable package staging and verification. It does not replace the session scope contract or add
a public operation. The permanent generic contract id and producer id require confirmation with
the remainder of this ratification.

## Proposed SP1 contract after ratification

This vocabulary is intentionally proposed, not created:

| Role | Proposed contract |
| --- | --- |
| Package identity | Server-generated canonical `snapshot.*` id, permanently bound after successful capture. A failed rolled-back capture creates no package identity; the operation/checkpoint owner handles safe retry through its own stable request identity. |
| Capture request | Internal closed typed call from the owning coordinator to the already registered producer, containing only its typed subject. The producer re-reads and derives the boundary fingerprint inside the root transaction. It accepts no producer selector, expected fingerprint from an earlier preview, caller-selected snapshot id, bytes, path, URI, credentials, raw effects, domain list, restore flag, or caller digest. |
| Producer result | In-process `SnapshotCaptureProposal`: canonical opaque content, scope contract id/version, producer id/version, boundary fingerprint, and declared bounded metadata. The producer—not the generic store—creates content. |
| Stored package | Immutable id, scope contract id/version, producer id/version, boundary fingerprint, canonical digest algorithm/value, byte count, capture time, and availability state. Backend locator/content are private implementation state. |
| Public metadata | If later confirmed, only id, scope contract id/version, producer id/version, availability, capture time, digest, and bounded declared metadata. It never returns package bytes, locator, credentials, raw domain values, event/audit ids, or an assertion that restore is allowed. |
| Consumer open | Typed internal call requires an admitted consumer id plus exact expected package digest/scope version. Mismatch/unavailability fails closed. |

`available → retired` is the only proposed package availability transition; deletion, overwrite,
re-capture under the same id, correction, and restore are excluded. Exact status vocabulary,
metadata allowlist, maximum size, and digest algorithm remain part of ratification rather than
implementation guesses.

## Dependency graph and slices

~~~text
SP0 storage/retention + producer/consumer + provenance decisions
└─ SP1 immutable package capture/reference
   ├─ S4 Slice 1 named evidence-only session checkpoint
   ├─ C11 no-write checkpoint selector/classification
   └─ SP2 metadata inspection / SP3 admitted consumer open
      └─ S4 optional restore only after every domain owner is classified
~~~

### Slice 0 — ratification

**Prerequisites:** none beyond this audit.

1. Record the six decisions above, one chosen first capture fixture, and the precise S4/C11
   producer/consumer handoff.
2. Amend this plan with concrete ids, schemas, storage provider boundary, transaction strategy,
   availability lifecycle, bounded output, and failure semantics.
3. Confirm the semantic boundary before SP1 implementation.

**Exit:** SP1 can implement one immutable opaque capture package without making a storage,
retention, scope, visibility, or restore decision implicitly.

### Slice 1 — immutable package capture/reference

**Prerequisites:** Slice 0 accepted; one approved typed producer and atomic storage strategy.

Implementation detail is owned by
[Snapshot Feature SP1](../feature-01/SNAPSHOT-FEATURE-01-IMPLEMENTATION-PLAN.md); this section keeps
only SP0's dependency exit.

1. Add the confirmed package persistence, producer interface, integrity verification, and closed
   capture/read surfaces as one coherent slice.
2. Test id collision/replay, producer/scope/version/digest mismatch, corrupt/unavailable package,
   cancellation/timeout/rollback, size bounds, metadata redaction, and fresh-owner readback.
3. Prove one capture/reference and ordinary structural/audit evidence commit atomically; no generic
   state copy, restore, raw bytes, or public storage locator is exposed.

**Exit:** S4 can obtain one immutable typed package reference with verifiable provenance, while
restore remains unavailable.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Missing storage decision | No runtime artifact or id is authored; capture remains unavailable. |
| Capture identity | One id names one immutable package; duplicate/replay/collision never overwrites it. |
| Integrity | Stored bytes/content match the canonical declared digest and size; mismatch/corruption fails closed. |
| Scope | The package retains exactly one producer-issued scope-contract id/version and capture boundary fingerprint. |
| Atomicity | The approved storage/reference/evidence/audit strategy commits fully or reports no available package. |
| Isolation | Generic storage does not inspect, copy by guessing, mutate, restore, fork, list, or expose any game-domain state. |
| Fresh use | An admitted fresh in-process consumer can verify the same package from durable metadata/content alone. |

## Change control

Amend SP0 before naming an actual backend, adding bytes/metadata fields, exposing a public query or
commit, admitting a new producer/consumer, supporting storage migration/replication, setting
retention expiry, or allowing restore/fork. Each changes storage, security, or domain ownership.
