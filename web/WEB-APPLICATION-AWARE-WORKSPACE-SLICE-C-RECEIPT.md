# Application-aware workspace Slice C receipt — reusable system read capabilities

Status: **accepted by user instruction to continue on 2026-08-25**  
Completed: **2026-08-25**  
Implementation: [Slice C](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-IMPLEMENTATION.md)  
Parent: [Application-aware workspace dependency plan](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral system infrastructure**

## Delivered boundary

- Added the `system-capabilities` component with deterministic descriptors, closed input/output
  JSON Schemas, authorization-first discovery and exact dispatch, bounded safe errors, and startup
  rejection of duplicate or malformed registrations.
- Registered the existing permanent `system.applications` ID as the first read-only capability.
  Its owner remains `application-registry`; activation and state-space owners supply optional
  current detail without transferring persistence authority.
- Routed the existing MCP `query(kind: "system.applications")` operation and the existing web
  application list/detail routes through the same handler. Missing registration now fails closed;
  neither transport retains an application-registry semantic fallback.
- Preserved the existing web cursor envelope and MCP operation audit behavior. Direct web structure
  reads no longer expose a second application-list implementation.
- Added focused descriptor, duplicate, authorization-order, unknown-ID, input/output-schema,
  unavailable-handler, no-fallback, no-change, and MCP/web parity tests.

## Exact descriptor evidence

The registered descriptor is `system.applications` version 1, mode `read`, sensitivity
`private-operator-metadata`, required authorization `read`, procedure
`procedure.system.inspect`, and requires neither confirmation nor an idempotency key.

- Input schema hash: `DCCDBAFDCCC8CAC4F8BC626F3523A3FDBF85BC29024F9443130C1FFD52CAC304`
- Output schema hash: `D1BC7457593F4E3B365D539D730E65BB8D5B79B912AA45B1FCFE872986CF1AF5`
- Descriptor fingerprint: `4F7A22DCE44B1CBA5B64E974AD73B02E4AC0BF0169533E1E75B820B707C20009`

The catalog authorizes generic private-operator read access before descriptor lookup, input
validation, or handler access. Unknown IDs use exact matching only. Handler output is validated
before it can reach either adapter, and unexpected exception text is not returned.

## Verification

- System-capability, system-registry authorization, web-interface, and application-conversation
  compatibility set: **119 passed, 0 failed**.
- Guard and catalog-coverage set: **17 passed, 0 failed**.
- Complete `SystemCatalogProtocolTests`: **2 passed, 0 failed**.
- MCP protocol walk: **6 passed, 2 deliberately skipped, 0 failed**.
- Solution build: **0 warnings, 0 errors**.
- `git diff --check` passed; line-ending notices were informational only.

## No-change and exclusions

Successful web reads leave persistence unchanged. MCP parity tests observe only the pre-existing
query audit row per call. This slice adds no database entity, migration, route, MCP kind,
authorization capability, live page revision, application/game rule, chat behavior, model context,
write proposal, confirmation, execution, or normal-database mutation.

The user accepted Slice C by instructing implementation to continue. Slice D may add the
separately scoped, read-only general system conversation and bounded system context; Slice C grants
it no write or application-state authority.
