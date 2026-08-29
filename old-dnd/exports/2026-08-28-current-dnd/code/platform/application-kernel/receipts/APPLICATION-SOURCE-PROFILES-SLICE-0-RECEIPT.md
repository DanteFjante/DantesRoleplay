# Application source profiles Slice 0 receipt — exact source-subset activation

Status: **accepted**  
Completed: 2026-08-26  
Accepted implementation: [Slice 0](../APPLICATION-SOURCE-PROFILES-SLICE-0-IMPLEMENTATION.md)

## Delivered

- Added ruleset-neutral exact registered-source selection to disposable application previews and
  immutable application activations. Null selection retains the legacy all-source behavior.
- Canonicalized explicit selections by ordinal source ID and rejected empty, blank, duplicate,
  excessive, and unknown selections before activation changes.
- Required every application-preview provider to implement the exact-source overload explicitly;
  alternate providers cannot silently fall back to all-source behavior.
- Excluded unselected source documents, scan problems, overlay results, and fingerprints from the
  candidate profile. Core-only and core-plus-extension candidates therefore have separate stable
  activation fingerprints.
- Made the canonical selected profile part of activation request identity, replay, audit evidence,
  summaries, and application inspection.
- Extended the existing query/commit and system-capability surfaces with optional `sourceIds` while
  retaining the three public verbs and existing callers.
- Reused immutable activation manifests and exact state-space fingerprint binding, so no database
  migration was required. Non-empty state spaces retain the existing `MIGRATION_REQUIRED` boundary.
- Recorded the D&D authoring rule that `dnd2024-core` stays SRD-faithful and optional or altered
  content is packaged as a separately selectable pre-campaign source.

## Evidence

- Final focused preview, activation, state-space, authorization, system catalog protocol, and public
  MCP walk checks: 34 passed, 0 failed.
- Updated exact capability output-schema hash and descriptor fingerprint; snapshot check passed.
- D&D 2024 conformance suite: 80 passed, 0 failed.
- Full shared suite: 1,096 passed, 0 failed.
- Release solution build: 0 warnings, 0 errors.
- Catalog validation: 144 records valid with 21 existing near-duplicate advisories; no live data was
  touched.
- `git diff --check`: passed; only existing line-ending advisories were emitted.

## Deliberate exclusions and next gate

This slice does not package optional D&D content, invent extension IDs or schemas, automatically
discover extensions, modify source trust, migrate a non-empty campaign, or remove old behavior.
The next coherent leaf is extension catalog packaging: define a separately registered optional D&D
source that depends on the SRD-faithful core without altering it.
