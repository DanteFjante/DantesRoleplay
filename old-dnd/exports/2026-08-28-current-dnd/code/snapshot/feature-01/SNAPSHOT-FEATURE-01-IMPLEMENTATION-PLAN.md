# Snapshot Feature SP1 implementation plan — immutable SQLite evidence package

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Accepted.**
Last updated: 2026-08-21

## Execution rule and target

This is a repository-mode feature plan governed by `AGENTS.md`,
`procedure.system.create-feature`, `procedure.system.inspect`, `procedure.system.modify`, the
[Snapshot operations roadmap](../../SNAPSHOT_OPERATIONS_PLAN.md),
[SP0](../feature-00/SNAPSHOT-FEATURE-00-DEPENDENCY-PLAN.md), and the existing
`procedure.campaign.session` contract. Repository files remain authoritative while implementing;
catalog changes use `roleplay validate catalog` against a disposable database, and no persistent
catalog import is part of SP1 acceptance.

SP1 lets one typed in-process Campaign/session producer turn one valid ended S3 session into a
bounded canonical evidence package, then stage that package as one immutable SQLite BLOB inside a
caller-owned transaction and return a byte-free reference. A fresh store can verify the reference
against durable bytes and metadata without exposing the bytes. SP1 creates no checkpoint entity,
public MCP/query/commit kind, restore, fork, file, database copy, or player-visible capability.

The accepted implementation must be small enough that Session S4 can compose it into its own
outer root without transaction adaptation or game semantics leaking into the generic store.

## Semantic confirmation gate

Before Slice 1 creates or revises a permanent contract, the host must confirm this set together:

- New generic contract id: `procedure.snapshot.package`.
- Existing scope owner: amend and version `procedure.campaign.session`; do not create a parallel
  snapshot scope contract.
- Stable internal producer id/version:
  `snapshot.producer.campaign-session-evidence`, version `1`.
- First payload: the exact ended-session evidence envelope ratified in SP0.
- SQLite BLOB storage, indefinite v1 retention, private bytes, SHA-256, canonical JSON, and the
  64 KiB producer / 1 MiB store limits.
- No SP1 public surface and no payload consumer/open capability.

Confirmation authorizes those meanings and the required forward EF migration. It does not
authorize S4 checkpoint vocabulary, a new MCP operation, restore, retirement, deletion, or import
into the persistent game database.

## Existing-owner and dependency analysis

| Concern | Existing owner/evidence | SP1 boundary |
| --- | --- | --- |
| Generic package persistence/integrity | Snapshot SP0; no runtime implementation exists | SP1 owns the generic row, store, digest, immutable constraints, and byte-free verification. |
| Session lifecycle and recap validity | Accepted S1–S3; `CampaignSessionRecapReader`; `procedure.campaign.session` | The producer consumes this owner. It does not duplicate recap validation or change session state. |
| Campaign/world scope identity | Existing `game.core.campaign.has-session` and `game.core.campaign.in-world` relationships | The producer resolves exactly one of each and copies only endpoint identities into the evidence envelope. |
| Transaction/event/audit root | Existing DataAccess root pattern and `EffectApplier` ambient-transaction behavior | The generic store requires an existing transaction and never commits. S4 later owns structural effects and audit. |
| Contract version | `IProcedureStore.GetAsync` returns the imported current version | Producer pins the exact positive `procedure.campaign.session` version in its proposal/reference. |
| Checkpoint entity/link/read | Session S4 plan | Excluded. SP1 returns a reference for S4; it does not create or expose checkpoint state. |
| Fork classification | Campaign C11 plan | Excluded. C11 later consumes S4 metadata; it does not receive SP1 bytes. |
| Restore and domain classification | S4/C11 plus every affected domain owner | Excluded. The evidence package is deliberately insufficient and unauthorized for restore. |
| Authorization/player access | CH14/identity plans | Excluded. SP1 is in-process infrastructure only. |
| Retention transition | Snapshot SP4 | SP1 writes `available` only and has no update/delete API. |

### Verified implementation leaves

- EF Core SQLite and forward migrations already exist, including migration-drift and existing-
  database upgrade tests.
- `DantesRoleplayDbContext.Database.CurrentTransaction` is already the composition signal used by
  `EffectApplier`; caller-owned transactions remain caller-owned.
- Session S3 stores one immutable, strictly parsed `session.s0.c3-only.v1` recap and retains ended
  sessions under one campaign scope link.
- `IProcedureStore` can resolve and pin one exact active contract version.
- SHA-256 and `System.Text.Json` are already platform dependencies; SP1 needs no new package.

### Unverified or deliberately absent leaves

- No generic snapshot table, model, store, contract, producer, migration, or DI registration exists.
- No current contract authorizes checkpoint creation or payload reads.
- `EnsureCreated` does not install custom migration SQL triggers; trigger behavior therefore needs
  a migrated-file integration test rather than relying only on the in-memory fixture.
- S4 cannot compose SP1 until SP1 is accepted. SP1 must not implement a temporary checkpoint API
  to bypass that dependency.

## Dependency graph and implementation order

~~~text
SP0 semantic confirmation
├─ existing S3 ended-session recap + scope readers
├─ existing procedure version reader
└─ Slice 1: contracts, generic models, canonical session producer
   └─ Slice 2: SQLite row, constraints, migration, transactional stage
      └─ Slice 3: byte-free verification, corruption/immutability proof, DI
         └─ Slice 4: feature acceptance and S4 handoff receipt
            ├─ S4 named checkpoint/reference composition
            └─ C11 read-only classification (later)
~~~

Only one slice may be in progress. A slice is complete only after its focused exit tests pass.
Do not begin a dependent slice when its predecessor is partly implemented or locally unverified.

## Permanent and runtime vocabulary

| Name | Kind | Exact meaning |
| --- | --- | --- |
| `procedure.snapshot.package` | New catalog procedure | Governs internal immutable package staging and byte-free verification. It describes no public call. |
| `procedure.campaign.session` | Existing catalog procedure, new version | Adds the internal ended-session evidence producer boundary while preserving all S1–S3 behavior. |
| `snapshot.producer.campaign-session-evidence`, v1 | Stable internal producer identity | Produces only the closed v1 envelope from one valid ended session. It is provenance, not an MCP mechanic or caller-selectable mode. |
| `snapshot.*` | Server-generated runtime package id | `snapshot.` plus one lowercase 32-hex GUID. Callers never choose or derive it. |
| `dantes-canonical-json-v1` | Content-encoding vocabulary | Exact explicit UTF-8 writer described below; not a generic JSON canonicalization claim. |
| `dantes.snapshot.campaign-session-evidence`, v1 | Payload format marker | Identifies the closed first producer envelope. |

Do not add a snapshot component definition, mechanic, event type, subscription, world entity
fixture, `snapshot.scope.*` contract, storage locator, or public verb/kind. They would duplicate an
owner or expand the confirmed surface.

## Core types and interface contract

Place generic types under `DantesRoleplay/Snapshots/`; they contain no Campaign, World, Quest,
Character, Item, EF, or SQLite vocabulary.

### `SnapshotCaptureProposal`

Closed construction input from a trusted typed producer:

~~~text
ScopeContractId       nonempty canonical id, max 200
ScopeContractVersion  positive Int32
ProducerId            confirmed exact id, max 200
ProducerVersion       positive Int32
ContentEncoding       confirmed exact encoding, max 100
BoundaryFingerprint   lowercase 64-character SHA-256 hex
Content               defensively copied nonempty bytes, max 1 MiB
~~~

The proposal does not contain snapshot id, digest, byte count, capture time, availability,
operation id, file/URI, credentials, checkpoint id, restore flag, arbitrary metadata, raw effects,
or domain list. Construction must copy the provided bytes so a producer cannot mutate content
after validation and before persistence.

### `SnapshotPackageReference`

Returned only after the package row has been staged and `SaveChangesAsync` has succeeded inside
the still-open outer transaction:

~~~text
Id, ScopeContractId, ScopeContractVersion,
ProducerId, ProducerVersion, ContentEncoding,
BoundaryFingerprint, DigestAlgorithm, ContentDigest,
ByteCount, CapturedAt, Availability
~~~

It excludes `Content`, `RootOperationId`, database/backend names, locators, and arbitrary metadata.
The reference does not claim that the outer transaction has committed; only its owner can return
success after committing. S4 must never publish a reference if its root rolls back.

### `ISnapshotPackageStore`

Use two methods only:

~~~csharp
Task<SnapshotPackageStageResult> StageAsync(
    SnapshotCaptureProposal proposal,
    string rootOperationId,
    CancellationToken cancellationToken = default);

Task<SnapshotPackageVerificationResult> VerifyAsync(
    SnapshotPackageReference expected,
    CancellationToken cancellationToken = default);
~~~

`StageAsync` validates and computes all store-owned values. `VerifyAsync` reads by expected id,
recomputes byte count and SHA-256 from stored bytes, compares every expected reference field, and
returns status plus byte-free metadata. Neither method lists packages or returns content. There is
no update/delete/open method in SP1.

Use fixed, non-secret failure codes in result models rather than exceptions for expected invalid
input/state. Cancellation remains `OperationCanceledException`; unexpected storage exceptions are
translated only by the later owning root, which also decides audit behavior.

### Campaign/session producer

Place the typed interface/result models in a new focused Campaign file rather than adding more
unrelated records to `CampaignBlueprint.cs`:

~~~csharp
Task<CampaignSessionEvidenceProductionResult> ProduceAsync(
    string sessionId,
    CancellationToken cancellationToken = default);
~~~

The request has only one canonical `session.*` id. It accepts no campaign/world id, payload,
fields, scope/version, expected digest, operation id, snapshot id, raw component, or restore data.
The result contains either one `SnapshotCaptureProposal` plus derived session/campaign/world ids,
or one closed problem with a literal recovery action. It never returns raw source JSON or bytes to
an MCP handler.

## Exact producer source and payload

The producer executes only while `DantesRoleplayDbContext.Database.CurrentTransaction` is non-null.
It performs these reads in this order:

1. Call the existing `ICampaignSessionRecapReader` for the canonical session id. Require `found`,
   one ended lifecycle, one valid recap, and one valid campaign/session history graph.
2. Read the session entity once for complete lifecycle and recap source JSON used by the boundary
   fingerprint. Require exact component uniqueness; do not reinterpret an error from the recap owner.
3. Read incoming `game.core.campaign.has-session` scope. Require exactly one link from the same
   returned campaign, empty `{}` data, correct direction, and no second campaign scope.
4. Read the campaign outgoing `game.core.campaign.in-world` links. Require exactly one empty-data
   link to one live canonical `world.*` entity. Do not copy or inspect world components.
5. Resolve current active `procedure.campaign.session` through `IProcedureStore.GetAsync`; require
   its exact id and a positive version. Pin that version in the proposal.
6. Re-read no mutable projection and query no event/audit history. Build the boundary fingerprint
   and payload from the already validated source values.

The canonical BLOB logical shape and property order are exactly:

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
        "timestamp": "UTC round-trip format",
        "sequence": 0
      }
    ]
  }
}
~~~

Implement one explicit `Utf8JsonWriter` path with no indentation, BOM, or optional properties.
Write `DateTime` milestone values after requiring `Kind == Utc` or normalizing the existing
validated UTC value with `ToUniversalTime()`, using the round-trip `O` representation. Preserve
the recap owner's milestone order. Do not serialize anonymous objects and then sort arbitrary
keys; that would make the advertised encoding depend on serializer defaults.

The producer maximum is 65,536 bytes. A larger result is
`SNAPSHOT_PRODUCER_CONTENT_TOO_LARGE`, not a request to truncate milestones or strings.

### Boundary fingerprint

Build one unambiguous byte stream with a fixed prefix
`dantes.snapshot.boundary.campaign-session-evidence.v1` followed by each source field as a
four-byte big-endian byte length and its UTF-8 bytes, in this exact order:

1. session id;
2. raw complete lifecycle component JSON;
3. campaign-scope from id, to id, kind, and raw data;
4. world-scope from id, to id, kind, and raw data;
5. raw complete recap component JSON;
6. decimal `procedure.campaign.session` version.

Hash the complete stream with SHA-256 and lowercase hex. Never use delimiter concatenation: ids
and JSON may contain delimiter characters. Never use row ids, timestamps, audit ids, or database
file state as the boundary.

## SQLite persistence model

Add a generic `SnapshotPackage` persistence entity and `DbSet` with table name
`snapshot_package`. Its exact columns are:

| Column | SQLite/EF contract |
| --- | --- |
| `Id` | `TEXT` primary key, max 200, required. Store generates `snapshot.` + lowercase GUID `N`. |
| `ScopeContractId` | `TEXT`, max 200, required. |
| `ScopeContractVersion` | `INTEGER`, required, greater than zero. |
| `ProducerId` | `TEXT`, max 200, required. |
| `ProducerVersion` | `INTEGER`, required, greater than zero. |
| `ContentEncoding` | `TEXT`, max 100, required. |
| `BoundaryFingerprint` | `TEXT`, max 64, required lowercase SHA-256 hex. |
| `DigestAlgorithm` | `TEXT`, max 20, required and equal to `sha256`. |
| `ContentDigest` | `TEXT`, max 64, required lowercase SHA-256 hex. |
| `ByteCount` | `INTEGER` mapped from `long`, required, 1 through 1,048,576. |
| `CapturedAt` | `TEXT` mapped from UTC `DateTime`, required and server-generated in the store. |
| `RootOperationId` | `TEXT`, max 40, required canonical existing operation id format. |
| `Availability` | `TEXT`, max 20, required and equal to `available`. |
| `Content` | `BLOB`, required. |

The generated forward migration must add named check constraints for positive versions, fixed
algorithm/availability, lowercase hexadecimal digest/fingerprint shape, byte bounds, and
`ByteCount = length(Content)`. The model must express every portable constraint so
`EnsureCreated` tests match it. After generation, add SQLite migration SQL for two triggers:

- before any `UPDATE` on `snapshot_package`, abort with a fixed immutable-package error;
- before any `DELETE` on `snapshot_package`, abort with the same fixed immutable-package class.

The migration `Down` removes triggers before dropping the table. Do not edit an applied migration,
the initial migration, generated designer contents, or the model snapshot by hand except where EF
requires adding the reviewed trigger SQL to the new migration body. Generate one forward delta.

No foreign key points from the package to mutable world rows or operation history. Scope identity
and root operation provenance must survive independently; S4's later checkpoint relationship owns
game-graph reachability. Add no speculative listing index. The primary-key lookup is the only SP1
package query.

## Store algorithms and failure behavior

### Stage

1. Reject null/malformed proposal or root operation id with zero database writes.
2. Require an existing current EF transaction. Return `SNAPSHOT_TRANSACTION_REQUIRED` if absent;
   do not begin one.
3. Defensively copy proposal content again at the persistence boundary.
4. Enforce the 1 MiB generic limit and all identifiers/versions/encoding/fingerprint bounds.
5. Compute SHA-256 from the copied exact bytes; lowercase hex. Compute byte count internally.
6. Generate a `snapshot.*` id from a new lowercase GUID. The primary key is the collision guard;
   a database uniqueness failure is a storage failure, never a retry that overwrites/updates.
7. Add one row and call `SaveChangesAsync`; this enrolls in but does not commit the outer transaction.
8. Return one byte-free reference. Do not log, create events, create world entities, or mark success.

Any validation failure adds no tracked package. Any `SaveChangesAsync` exception must detach/clear
the attempted package consistently with the owning transaction strategy before returning/throwing;
it must not commit or roll back a transaction it does not own. Cancellation propagates.

### Verify

1. Validate the expected reference shape before querying.
2. Load exactly one package by primary key with `AsNoTracking`; missing is
   `SNAPSHOT_NOT_FOUND`.
3. Require every stored metadata field to equal the expected reference. Mismatch is
   `SNAPSHOT_REFERENCE_MISMATCH`; do not reveal which protected field differed outside tests/logs.
4. Require availability `available`; any other value is `SNAPSHOT_UNAVAILABLE`.
5. Recompute stored byte length and SHA-256, then compare with stored count/digest and expected
   count/digest using fixed-time digest comparison where practical. Failure is
   `SNAPSHOT_CORRUPT`.
6. Return verified byte-free metadata. Never return, deserialize, or interpret content.

Expected failures never substitute the current recap, current campaign/world graph, event history,
or chat. There is no recovery-by-recapture under the same package id.

## Slices

### Slice 0 — semantic ratification

**Prerequisite:** SP0 recommendation reviewed.

1. Confirm the permanent generic contract id, internal producer id, existing scope owner reuse,
   exact payload, limits, retention, visibility, and migration boundary as one semantic decision.
2. Mark SP0 accepted and this plan ready; do not create a runtime/artifact id before confirmation.
3. Record any rejected choice by revising SP0 and this plan before implementation.

**Exit:** no unresolved choice can change a permanent id, payload meaning, table schema, transaction
owner, or public surface.

### Slice 1 — contracts, core models, and canonical ended-session producer

**Status: Implemented and verified.** See
[Slice 1 validation](SNAPSHOT-FEATURE-01-SLICE-1-VALIDATION.md).

**Prerequisites:** Slice 0 accepted; S3 focused tests green.

Files expected:

- new `catalog/procedures/snapshot/procedure.snapshot.package.md`;
- revised `catalog/procedures/campaign/procedure.campaign.session.md`;
- new `DantesRoleplay/Snapshots/SnapshotPackageModels.cs`;
- new `DantesRoleplay/Campaign/CampaignSessionEvidenceSnapshot.cs`;
- new `DantesRoleplay.DataAccess/CampaignSessionEvidenceProducer.cs`;
- new focused cases in `DantesRoleplay.Tests/SnapshotFeature1Tests.cs`.

Implementation:

1. Author the generic procedure and append the internal producer constraint to the existing
   session procedure without changing S1–S3 calls or outputs.
2. Add the closed generic proposal/reference/result models and store interface; do not implement
   storage yet.
3. Implement the typed producer with the exact owner reuse, source reads, fingerprint, canonical
   writer, and 64 KiB bound above.
4. Test deterministic byte equality and fingerprint equality across fresh DbContexts; one source
   fact change must change the boundary fingerprint. Reordering JSON object properties in stored
   raw source may change the boundary fingerprint but must not change the canonical evidence
   payload when semantic values are equal.
5. Test invalid/non-ended/missing-recap/corrupt-recap session, missing/multiple/reversed/cross-
   campaign scope, missing/multiple/dangling world link, invalid procedure version, oversized
   content, cancellation, and zero mutation of session/campaign/world state.
6. Run the S3 focused tests and `roleplay validate catalog`.

**Exit:** inside an existing transaction, exactly one valid ended S3 fixture produces byte-for-byte
deterministic bounded content and provenance; invalid fixtures produce no proposal or mutation.

**Do not include:** DbContext model, migration, package store implementation, DI, checkpoint, MCP,
restore, or raw package read.

### Slice 2 — transactional immutable SQLite staging

**Status: Implemented and verified.** See
[Slice 2 validation](SNAPSHOT-FEATURE-01-SLICE-2-VALIDATION.md).

**Prerequisites:** Slice 1 exit green; migration boundary already confirmed in Slice 0.

Files expected:

- new `DantesRoleplay/Snapshots/SnapshotPackage.cs` persistence model, unless the reviewed codebase
  convention places the generic entity beside the other snapshot models;
- modified `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs`;
- new `DantesRoleplay.DataAccess/SnapshotPackageStore.cs`;
- one generated forward migration plus designer/model-snapshot updates;
- expanded `DantesRoleplay.Tests/SnapshotFeature1Tests.cs` and `MigrationDriftTests.cs` only where
  the existing generic migration gates cannot observe the new table/constraints.

Implementation:

1. Configure the exact table and portable checks; generate one forward migration using EF tooling.
2. Review generated migration/model snapshot, then add only the two required immutable triggers to
   the new migration.
3. Implement `StageAsync` exactly as above. It requires and joins an existing transaction.
4. Test missing transaction, invalid proposal/root id, computed rather than caller digest/count/
   timestamp/id, successful stage still invisible to a second connection before commit, visible
   after commit, outer rollback removes it, cancellation rollback, duplicate id protection, and
   no world/event/notification/audit rows caused by the store.
5. Test migrated empty database and forward upgrade from the previous migration. Run all existing
   migration drift gates.

**Exit:** one proposal stages one immutable row and byte-free reference under a caller-owned SQLite
transaction; commit and rollback are controlled solely by the caller; the application starts from
both fresh and upgraded schemas.

**Do not include:** verification read, DI registration, S4 checkpoint graph, public handler, or
retention mutation.

### Slice 3 — durable byte-free verification and registration

**Status: Implemented and verified.** See
[Slice 3 validation](SNAPSHOT-FEATURE-01-SLICE-3-VALIDATION.md).

**Prerequisites:** Slice 2 exit green.

Files expected:

- modified `DantesRoleplay.DataAccess/SnapshotPackageStore.cs`;
- modified `DantesRoleplay.DataAccess/DataAccessServiceCollectionExtensions.cs`;
- completed focused verification/immutability cases in
  `DantesRoleplay.Tests/SnapshotFeature1Tests.cs`.

Implementation:

1. Implement exact-reference verification without exposing or parsing content.
2. Register only `ISnapshotPackageStore` and the typed campaign-session producer. Do not register a
   producer registry, generic payload consumer, endpoint, or MCP handler.
3. Prove fresh-DbContext verification succeeds after commit and fails closed for wrong id, each
   expected metadata mismatch, unavailable value, wrong byte count/digest, malformed digest, and
   missing package.
4. Run trigger tests against a migrated file database: direct fault-injection `UPDATE` and `DELETE`
   attempts must fail. Any raw SQL used here is test-only corruption injection and must never be
   copied into a production path.
5. For verifier corruption detection, construct a dedicated test database/schema without the
   immutability trigger or insert a deliberately corrupt fixture before enabling the trigger; do
   not weaken the production migration to make corruption testing easy.
6. Resolve services through the production DI registration and perform one producer → stage →
   commit → fresh-scope verify walk with no MCP invocation.

**Exit:** a fresh in-process owner can verify durable identity, metadata, size, and digest from a
reference alone; no caller can update/delete/list/open/download package bytes through SP1.

### Slice 4 — SP1 feature acceptance and handoff

**Status: Accepted.** See
[SP1 validation](SNAPSHOT-FEATURE-01-VALIDATION.md).

**Prerequisites:** Slices 1–3 green.

1. Run the complete `SnapshotFeature1Tests`, S3 session tests, migration tests, and catalog
   validation.
2. Run the full solution suite once. No protocol walk is required because SP1 adds no MCP surface;
   if implementation accidentally changes the MCP/DI host surface beyond internal registrations,
   stop and revise the plan rather than silently expanding acceptance.
3. Inspect the diff for game vocabulary in the generic snapshot namespace/store, caller-supplied
   bytes/digests/ids, transaction ownership, byte-return paths, update/delete paths, arbitrary
   metadata, new dependencies, or persistent catalog import.
4. Write `snapshot/feature-01/SNAPSHOT-FEATURE-01-VALIDATION.md` with concise command outcomes,
   migration name, confirmed ids/versions, and remaining exclusions.
5. Present the completed feature for the required human acceptance boundary. Mark SP1 accepted only
   after confirmation.

**Exit:** SP1 is accepted, its migration and contracts are verified, and S4's next plan can depend
on a typed package reference without guessing storage, integrity, or transaction behavior.

## Focused acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Valid source | One ended session yields deterministic canonical bytes, producer/scope versions, and boundary fingerprint. |
| Source isolation | Payload contains only the closed envelope; no GM context, raw graph, quest, character, item, transcript, event/audit id, or other campaign/world data. |
| Source invalid | Missing/malformed/active/cross-scoped session or recap/world/procedure dependency returns one fixed problem and no proposal/write. |
| Determinism | Same committed source in fresh contexts yields identical bytes and fingerprint. |
| Source drift | Any fingerprint source fact change changes fingerprint; capture never consumes a cached validation result. |
| Canonical time/order | Milestones preserve owner order and emit UTC round-trip timestamps; zero to five are accepted, six is rejected by the recap owner. |
| Size | Producer rejects over 64 KiB; store rejects over 1 MiB; neither truncates. |
| Transaction required | Staging without an outer transaction fails with zero row. |
| Atomic commit | Row is durable only after owner commit; owner rollback/cancellation leaves no package. |
| Store ownership | ID, digest, count, capture time, availability, and root provenance are computed/validated by the store, never accepted from package content/caller. |
| Immutability | Update/delete fail at the SQLite migration boundary and no application API exposes either. |
| Verification | Fresh context recomputes exact byte length/digest and compares the entire expected reference. |
| Corruption | Missing/mismatched/corrupt data fails closed and returns no bytes/current-state substitute. |
| No side effects | Producer/store create no entity/component/link/containment/event/notification/operation row except an operation later composed by S4. |
| No public surface | Verb surface, `CommitTool`, `QueryTool`, and protocol capability announcements are byte-for-byte/API unchanged by SP1. |
| Migration | Current model matches migrations; fresh database and upgrade from the prior migration both work transactionally. |

## Verification commands

Use the repository's normal command wrapper/environment. Expected focused commands are:

~~~powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~SnapshotFeature1Tests
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~SessionFeature1Tests
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~MigrationDriftTests
.\roleplay validate catalog
dotnet test DantesRoleplay.slnx
~~~

Run the full solution only at Slice 4 acceptance. `roleplay validate catalog` is required after the
new/revised procedure files; it imports a disposable database and does not authorize or perform a
persistent import. Do not hand-edit `catalog/manifest.json` unless the catalog tooling's documented
workflow updates it as part of the reviewed catalog change.

## Terra High implementation handoff

Terra High should begin by re-reading only `AGENTS.md`, the four governing system/session
contracts named at the top, SP0, this plan, and the exact existing files named by the active slice.
It should inspect current workspace changes before every edit and preserve unrelated dirty work.

For each slice:

1. Restate the slice boundary and verify its prerequisites from tests/artifacts.
2. Modify only the expected files unless inspection proves one additional owner file is necessary.
3. If a required choice differs from this plan, stop and amend the plan; do not improvise a second
   authority, public surface, raw SQL production path, or caller-supplied field.
4. Run the focused tests listed in that slice until green.
5. Record a short validation receipt only when the slice requests one; do not copy completed
   evidence back into every contract/plan.
6. Do not begin the next slice until the current exit statement is literally true.

## Change control

Amend SP0 and this plan before changing the payload scope or format, adding active-session capture,
including mutable campaign/world/quest/character/item state, raising limits, compressing/encrypting
content, adding arbitrary metadata, choosing caller package ids, exposing metadata/bytes publicly,
adding list/search/open, allowing update/delete/retirement, using external/file storage, starting a
transaction inside the store, adding restore/fork, or adding any MCP/browser surface. Those are new
storage, scope, security, lifecycle, or public-surface decisions—not SP1 implementation details.
