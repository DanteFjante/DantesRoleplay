# E8 trigger scheduling Slice 3 implementation — private observation ingestion

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, E. Private observation ingestion](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Expose the confirmed private application-scoped observation route with authorization-first
bounded parsing, exact schema validation, source/principal throttling, durable replay-safe evidence,
and the closed safe HTTP response.
Exclusions: Source/structure/trigger administration, device credentials or phone identity, workers,
matching, notification writing/status, recurrence, conditions, polling, action/effect/event writes,
MCP kinds, public hosting, or live database import.
Allowed files/areas: `src/system/{authorization,trigger-scheduling,web-interface}`, trigger/web/
authorization focused tests, `DantesRoleplay.DataAccess` composition/model plus additive migrations,
catalog-coverage declarations required by new persistence fields/tables, and E8 status/receipt docs.
Stop point: One authenticated private-host request can record or replay one schema-valid observation;
no runtime consumer acts on it.

## Confirmed decisions

- Slice 0 confirmed `POST /api/applications/{applicationId}/observations`, the exact request and
  response shapes, status mapping, bounds, private loopback/Tailscale exposure, the
  `trigger.observation.submit` capability, exact source-principal binding, a 10/minute principal
  ceiling, source-configured lower rate, and two concurrent requests per principal.
- Slice 3 adds the missing durable source-principal binding required by that accepted contract.
  Existing source revisions receive no implicit principal permission and therefore fail closed;
  an explicitly appended source revision must grant an opaque verified principal before use.
- Historical observations created before this migration retain nullable principal provenance.
  Every observation inserted after the migration must have an opaque principal and an exact
  source-revision permission, enforced by the store and SQLite.
- Authentication/capability evaluation precedes body reading. Route input cannot provide a
  principal, source version, schema/hash, receive time, fingerprint, disposition, or result ID.

## Prerequisite evidence

- [Slice 0](E8-TRIGGER-SCHEDULING-SLICE-0-IMPLEMENTATION.md) owns the confirmed public/security
  contract and HTTP status table.
- [Slice 2A receipt](E8-TRIGGER-SCHEDULING-SLICE-2A-RECEIPT.md) proves trusted-clock admission,
  current revisions, replay concurrency, database immutability, and permission linkage.
- [E9 Slice 1](../e9/E9-SLICE-1-IMPLEMENTATION.md) and its
  [receipt](../e9/E9-SLICE-1-RECEIPT.md) prove the loopback/Tailscale trusted-context adapter and
  deny-default private-host authorization policy.
- `IBoundedJsonSchemaValidator` owns offline exact-profile value validation; it is reused rather
  than copied into the endpoint.

## Runtime artifacts

- Add `PrivateOperatorCapability.TriggerObservationSubmit` with audit name
  `trigger.observation.submit`.
- Extend source revisions with an immutable bounded allowlist of opaque principal IDs.
- Add nullable historical `PrincipalId` observation evidence, exact principal-permission foreign
  key, and a SQLite insert guard requiring principal evidence for all new rows.
- Add an observation-ingestion service that resolves current source/structure registrations,
  verifies principal permission, acquires rate/concurrency capacity, validates canonical data
  against the exact stored schema, then invokes the hardened store.
- Add a singleton in-memory fixed-window limiter: at most 10 accepted attempts/minute per principal,
  at most the current source's configured lower bound per principal/application/source, no queue,
  and at most two concurrent requests per principal.
- Add the exact HTTP route, authorization-first filter, bounded strict request reader, safe result
  mapper, and exact remote-path allowlist entry.
- Register the trigger-scheduling store, trusted system UTC clock, ingestion service, and limiter in
  generic data-access composition. Add no MCP registration or tool kind.

## Authoritative state and closed input

The request has exactly `requestId`, `source`, `structure`, `observedAt`, and object-root `data`.
`source` has exactly `id`, `instanceId`, and `occurrenceId`; `structure` has exactly `id` and
positive `version`. All required fields must occur once. The route owns `applicationId`; the
authorization filter supplies the opaque verified principal.

The reader rejects a non-JSON content type before body access, a declared or streamed body above
65,536 bytes, invalid UTF-8/JSON, depth above 16, more than 512 nodes, 256 properties, 256 array
items, strings above 16 KiB, duplicate properties, unknown envelope properties, non-object `data`,
and non-`Z` RFC 3339 timestamps. Canonicalization remains owned by the pure Slice 1 contracts.

## Behavior, result, and transaction ownership

Authorization runs first. After strict parsing, ingestion resolves the current source and structure
and fails closed if either is missing, stale, disabled, not permitted, or not bound to the exact
principal. It then acquires non-queued rate/concurrency capacity, validates canonical `data` against
the stored profile/schema, and calls the hardened transactional store, which revalidates current
state and the trusted clock before committing.

An append returns `202` with `duplicate: false`; an exact replay returns the same ID with
`duplicate: true`; both use `accepted: true` and `status: "recorded"`. A conflicting identity
returns `409`. No response exposes source version, principal, schema/hash, canonical data, transport
headers, or internal diagnostics.

## Failure, replay, and rollback contract

- Authentication/capability denial: `403`, body never read, owner never invoked.
- Wrong content type: `415`; malformed/unknown/duplicate/invalid ID or UTC time: `400`.
- Resource bound: `413`; unknown application/source/structure: `404`; principal/source permission
  denial or disabled source: `403`; conflicting replay: `409`; schema-invalid data: `422`; rate or
  concurrency rejection: `429`; transient database recording failure: `503`.
- Error bodies are bounded `{error,message}` objects and never echo input or principal evidence.
- Every failure creates no observation, trigger, action, effect, event, or notification row.
- Source/structure revision races are rechecked by the store; injected persistence failure rolls
  back. Rate capacity may be consumed by rejected schema or persistence attempts, by design, to
  prevent invalid-input bypass.

## Implementation sequence

1. Add principal-binding contracts, persistence model/migration, trusted-clock store signature,
   limiter/ingestion ports, and focused domain/persistence tests.
2. Register the generic component services.
3. Add capability, private route/filter, strict reader, result mapping, and web security tests.
4. Run focused migration/trigger/authorization/web tests, full build/suites, migration drift, and
   diff checks; write the receipt and update E8 status.

## Acceptance matrix

| Concern | Required proof |
| --- | --- |
| Authorization first | Denied filter does not read the body or invoke ingestion; capability is server-selected. |
| Exact input | Valid request parses; content type, size, UTF-8, shape, duplicate/unknown fields, UTC, and all resource bounds fail safely. |
| Principal binding | Allowed current source revision accepts its exact opaque principal; another principal and legacy unbound revisions fail with no row. |
| Schema | Exact stored schema accepts matching canonical data and rejects well-formed mismatches with no row. |
| Rate/concurrency | Principal 10/min, lower source rate, two active requests, no queue, and UTC window rollover are deterministic. |
| Replay | Append/replay return one ID and safe `202`; changed request/occurrence identity returns `409` and one row. |
| Persistence | New observation principal FK and insert guard reject forged/unbound rows; historical nullable evidence survives upgrade. |
| Exposure | Exact observation path is allowed through private Tailscale web boundary; sibling application routes and `/mcp` remain excluded. |
| Compatibility | Existing web/control/MCP surfaces and all shared/local-AI tests pass; no worker or downstream mutation occurs. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~TriggerScheduling|FullyQualifiedName~ObservationHttp|FullyQualifiedName~PrivateOperatorAuthorization"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --no-build`
- `dotnet test DantesRoleplay.slnx --no-build --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record the capability, migration IDs, principal/schema/rate/route evidence, full verification, and
deliberate exclusions in `E8-TRIGGER-SCHEDULING-SLICE-3-RECEIPT.md`. Mark Slice 3 accepted only when
the exact private endpoint passes hostile and positive tests. Stop before Slice 4 worker/lease work.
