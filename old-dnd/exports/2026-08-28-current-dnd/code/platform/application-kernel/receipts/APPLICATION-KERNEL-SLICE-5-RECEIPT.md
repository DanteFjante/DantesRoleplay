# Application kernel Slice 5 receipt — versioned component types and bounded JSON Schema

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 5](../APPLICATION-KERNEL-SLICE-5-IMPLEMENTATION.md)

## Delivered

- Added the closed `system-json-schema-2020-12/v1` profile with deterministic normalization and
  SHA-256 identity, same-document fragment references only, explicit keyword allowlisting, and no
  network, filesystem, CLR loading, or dynamic-code resolver.
- Enforced schema and value byte, depth, node, definition, reference, and diagnostic limits. Every
  JSON kind remains valid when its schema allows it; malformed or resource-excess input returns a
  closed rejection rather than leaking parser or host detail.
- Added application-owned qualified component type identities and append-only schema versions in
  `system_component_type` and `system_component_type_version`. Callers cannot supply the version,
  profile, normalized schema, or hash. Whitespace replay returns the original version; changed
  normalized content appends the next version.
- Added dependency-injection registration, an additive EF migration/model snapshot, live-data
  catalog classifications, and a forward-only downgrade policy that refuses to delete immutable
  schema history.
- Kept the new registry detached from component values, legacy component definitions, catalog
  parsing/import, application activation, protocol kinds, effects, and AI integration.

## Previous-slice evaluation and corrections

Sol reviewed Slices 0–4 against their receipts, current contracts, persistence, tests, and component
boundaries. The overall architecture, reserved namespace, persistence isolation, and non-executable
candidate-manifest boundary matched their accepted contracts. The review corrected these gaps:

- Slice 2/3 application fingerprints now preserve ordered base-application identity and use a
  canonical JSON envelope, avoiding both order loss and delimiter collisions in authored metadata.
- Slice 4 candidate fingerprints now carry winner and shadow trust/precedence plus shadow content
  identity. Scan diagnostics are closed/redacted before entering the manifest, invalid trust values
  are rejected, and an application with no available registered sources returns a failed scan.
- Slice 2 projection validation now rejects hidden cross-application dependencies unless the owner
  is an explicitly permitted base application, and validates exact component/projection identities.
- Slice 2 state-space bindings now require exact SHA-256 application/manifest evidence and copy
  revision bases defensively; catalog cursors reject undersized authentication keys.

Regression tests prove both corrections. No game-specific contract or legacy C# reference was
introduced.

## Evidence

- Focused application-kernel/schema/component/source tests: 40 passed, 0 failed.
- Focused migration and catalog-coverage tests: 9 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Full shared suite: 495 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions

No component instance write or read path uses this registry yet. No active type selection,
state-space binding/isolation, source declaration parser, catalog import, activation, compiled-schema
cache, projection materialization, public protocol kind, application content, or legacy type/value
backfill was added. Slice 6 must own application-scoped ECS ports and state-space semantics in a new
confirmed implementation document.
