# Application kernel Slice 10D receipt — authenticated application preview

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10D](../APPLICATION-KERNEL-SLICE-10D-IMPLEMENTATION.md)

## Delivered

- Added authenticated `query(kind: "system.application-preview")` without expanding the MCP
  surface beyond `orient`, `query`, and `commit`.
- Added a standalone `application-preview` system component. It coordinates the application and
  source registries, registered-source scanner, and overlay resolver without knowing any game,
  ruleset, or application-specific data.
- Added host-configured allowed-root resolution. Registrations continue to contain only an opaque
  root ID and relative path/glob; canonical host paths never enter protocol input or output.
- Reused the generic bounded document scanner and accepted overlay rules. Preview works when local
  model providers are disabled and does not invoke a local LLM, embeddings, or vector search.
- Bound the preview fingerprint to the application revision, ordered source-registration
  fingerprints, every scanned document's redacted metadata and content hash, and the candidate
  manifest fingerprint. Competing files therefore cannot change invisibly behind an unchanged
  conflict summary.
- Returned validity, complete counts/fingerprints, and independently bounded winner, shadow, and
  problem details. Truncation does not change the full result fingerprint.
- Enforced private-operator `Read` authorization before application-ID parsing, registry access, or
  scanning. Missing roots, empty sources, scanner problems, and overlay conflicts produce a closed
  invalid preview without leaking paths or exception text.
- Kept preview read-only apart from normal query audit: it writes no registration, scan receipt,
  candidate, catalog, or active-application state and adds no migration.

## Evidence

- Focused application-preview, source-overlay, authorization, guard, bootstrap-contract, and live
  MCP tests: 34 passed, 0 failed.
- Live JSON-RPC walk proved a configured wildcard scan, valid redacted winner, bounded limit
  validation, denial before invalid application parsing, unchanged source-scan receipts, and the
  unchanged three-tool surface.
- Full shared suite: 587 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions and next gate

This slice does not persist or activate a candidate, expose dependency impacts, create or upgrade
state spaces, register applications/sources, mutate allowed-root configuration, enable remote MCP,
migrate `dnd2024`, or implement AI orchestration. Slice 10E should expose the deterministic
dependency-impact query needed before activation; Slice 10F can then own activation as a separate
transaction boundary.
