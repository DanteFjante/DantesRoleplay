# Application-aware workspace Slice C implementation — reusable system read capabilities

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice C  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: introduce one reusable, schema-validated, authorization-aware system capability catalog and adapt the existing `system.applications` read through it for both MCP and web.  
Exclusions: new public routes or kinds, source/settings/page/history descriptors, chat, model context, writes, proposals, execution, persistence, migrations, and live page changes.  
Allowed files/areas: one new `src/system/system-capabilities` component; application registry metadata constants; generic data-access registration; the existing MCP `system.applications` adapter and catalog advertisement; the existing web application-structure adapter; focused system-capability/MCP/web tests; component manifests and Feature 4 documents.  
Stop point: focused tests, protocol/guard verification, receipt, and acceptance request complete; stop before Slice D.

## Confirmed decisions

The confirmed parent plan authorizes a reusable descriptor catalog with closed input/output schemas,
owner, procedure, authorization, sensitivity, confirmation, and idempotency metadata. This slice
creates no new public ID: it adopts the existing permanent `system.applications` query ID as the
first allowlisted descriptor. The current MCP kind and web routes remain unchanged:

- `query(kind: "system.applications")`;
- `GET /api/control/structure/applications`; and
- `GET /api/control/structure/applications/{applicationId}`.

The first descriptor is read-only, requires the existing generic private-operator `Read`
capability, has private-operator metadata sensitivity, and never requires confirmation or an
idempotency key. Later slices must register additional descriptors explicitly; no prefix-based
fallback exists.

## Prerequisite evidence and owners

- [Slice B receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-B-RECEIPT.md) proves the shared navigation
  consumes bounded web application discovery.
- `application-registry` remains authoritative for registration, revision, and cursor order.
- `application-activation` and `state-space-administration` remain authoritative for optional exact
  detail metadata returned by the existing MCP query.
- `schema-validation` owns bounded JSON Schema compilation and value validation.
- `authorization` owns trusted principals, the closed `Read` capability, policy decisions, and safe
  evidence.
- `mcp-protocol` and `web-interface` remain transport adapters and own their existing envelopes,
  cursors, route errors, rate limits, and operation logging.

## Runtime artifacts

New component: `system-capabilities`, depending only on authorization, schema validation, and the
system owners delegated by its registered handlers.

The reusable descriptor contains an exact ID, descriptor version and fingerprint, owner,
description, read/write mode, normalized input/output schemas and hashes, procedure IDs, required
authorization capability and audit name, sensitivity, confirmation requirement, and idempotency
requirement. Registration and descriptor fingerprints are deterministic over canonical metadata.

The `system.applications` v1 input is a closed object:

- optional `applicationId`: one exact non-system application ID;
- optional `afterApplicationId`: registry continuation key used only by the web cursor adapter;
- required `limit`: integer from 1 through 100; and
- `applicationId` and `afterApplicationId` are mutually exclusive.

Its closed result always contains bounded `applications`, optional exact `application`, bounded
`stateSpaces`, optional `nextApplicationId`, and the applied `limit`. Registration/revision,
activation summary, and state-space summary shapes remain generic and provenance-bearing.

No database entity, migration, catalog file, authorization capability, public route, public MCP
kind, or application/game contract is added.

## Authoritative state and closed input

Adapters construct capability input from their existing typed parameters. Callers cannot supply a
handler, schema, owner, authorization decision, principal, sensitivity, descriptor fingerprint,
registry cursor key, result, or recovery text. The web adapter authenticates its opaque cursor and
only then passes the decoded continuation key. MCP continues to accept its existing no-cursor list
or exact-ID shape.

The transport supplies a trusted principal derived from its already-verified authorization
evidence. The catalog re-evaluates current generic `Read` authority before descriptor lookup,
schema validation, or handler access. The handler reads only existing registry/activation/
state-space owners and performs no filesystem, catalog-document, model, ECS-value, or mutation read.

## Behavior and result contract

1. Validate and deterministically index all registered handler descriptors; reject duplicates,
   malformed IDs/owners/procedures, invalid enum combinations, and rejected schemas.
2. Authorized discovery returns descriptors sorted by exact ID. Unauthorized discovery returns no
   descriptors.
3. For invocation, authorize generic read before revealing descriptor existence or validating input.
4. Resolve only an exact registered ID; an unknown `system.*` ID has no fallback.
5. Validate bounded input against the descriptor's normalized schema, then invoke the single owner.
6. Validate the owner's serialized output against the output schema before returning any data.
7. Return only safe closed errors, descriptor fingerprint, schema diagnostics where applicable,
   and authorization evidence. Unexpected handler details are not exposed.
8. MCP maps the common result into the unchanged tool envelope and records the existing
   `query:system.applications` operation. Web maps the same result into the unchanged page/detail
   response and retains its opaque cursor and cache/security behavior.

There is no transaction and no state change. Equal reads observe current owner state; no replay or
idempotency semantics are introduced.

## Failure and no-change contract

| Failure | Required behavior |
| --- | --- |
| Unauthorized/wrong scope | Deny before lookup, schema validation, or handler touch; return safe authorization evidence. |
| Duplicate ID | Catalog construction fails closed and no ambiguous handler can be invoked. |
| Invalid descriptor/schema | Catalog construction fails with a bounded configuration error. |
| Unknown ID | Return `SYSTEM_CAPABILITY_UNKNOWN`; never dispatch by prefix or reflection. |
| Malformed/extra/out-of-range input | Return `SYSTEM_CAPABILITY_INPUT_INVALID` with bounded schema diagnostics; handler untouched. |
| Mutually exclusive fields | Return the same closed input error; registry untouched. |
| Unknown application | Return `APPLICATION_UNKNOWN` with no partial detail. |
| Stale web cursor | Existing `CURSOR_STALE` response remains unchanged before capability invocation. |
| Handler unavailable/throws | Return `SYSTEM_CAPABILITY_UNAVAILABLE`; do not expose exception text. |
| Output violates schema | Return `SYSTEM_CAPABILITY_OUTPUT_INVALID` and no data. |
| Missing catalog registration in an adapter | Fail closed as unavailable; do not fall back to an MCP switch implementation. |

Every case is read-only and must preserve database total-change counts.

## Implementation sequence

1. Accept Slice B and activate this exact document.
2. Add generic contracts, deterministic descriptor validation/fingerprinting, authorization-first
   catalog dispatch, and the `system.applications` handler.
3. Register the new component in the existing generic host composition.
4. Adapt existing MCP and web application reads to the catalog while preserving their public shapes.
5. Add focused duplicate/unknown/authorization/schema/output/no-change/parity tests.
6. Run focused tests, MCP protocol/guard verification, scoped diff review, and write the receipt.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Descriptor | Exact existing ID, owner, version/fingerprint, normalized schema hashes, procedure, read capability, sensitivity, and false write flags. |
| Registration | Duplicate, invalid metadata, invalid schema, and unsupported mode combinations fail closed. |
| Authorization | Denial precedes ID/input/handler access and exposes no registry or exception detail. |
| Validation | Extra fields, malformed JSON, bad limits/IDs, mutual exclusion, and invalid output are rejected. |
| Dispatch | Only exact registered IDs invoke one handler; unknown IDs and absent registrations never fall back. |
| Web/MCP parity | List and exact reads return the same registration/revision/application facts from one handler while retaining transport envelopes/cursors. |
| Compatibility | Existing routes, MCP kind, navigation response, operation subject, cache/security behavior, and source-query behavior remain intact. |
| No change | Successful and failed reads leave persistent total-change counts unchanged except the MCP adapter's existing operation audit row. |

## Verification commands

- Focused `SystemCapabilityCatalogTests`, relevant `SystemRegistryAuthorizationTests`, and web
  structure/navigation tests.
- Existing `SystemCatalogProtocolTests` system-application cases and protocol/guard tests because
  MCP dependency dispatch changes.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` with focused filters.
- `git diff --check` over Slice C files.

No catalog validation, migration, live host startup, normal database mutation, browser activation,
or full suite is required. Combined full-suite/browser/live acceptance remains Slice H.

## Completion receipt and exit gate

Write `WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-RECEIPT.md` with exact descriptor evidence, adapter
parity, verification results, and exclusions. Mark Slice C implemented and awaiting user acceptance,
then stop before Slice D.
