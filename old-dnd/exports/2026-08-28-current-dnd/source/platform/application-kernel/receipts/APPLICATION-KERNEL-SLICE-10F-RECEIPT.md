# Application kernel Slice 10F receipt — exact-preview activation

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10F](../APPLICATION-KERNEL-SLICE-10F-IMPLEMENTATION.md)

## Delivered

- Added the ruleset-neutral `application-activation` component and authenticated
  `commit(kind: "system.application.activate")` without adding a fourth MCP tool.
- Required the exact closed payload, private-operator `Modify` authorization, a successful exact
  dry run, the requested preview fingerprint, and the expected current activation fingerprint.
- Rebuilt preview and dependency evidence at commit time. File, source, application, winner, or
  declared dependency drift invalidates the dry run and leaves the current activation unchanged.
- Persisted immutable activation revisions, retained source and winning-document metadata, one
  current pointer per application, and operation-linked receipts in one SQLite transaction. The
  retained evidence contains hashes and logical metadata, never file content or canonical paths.
- Bound each activation fingerprint to the exact preview, source/winner manifest, dependency graph
  fingerprint, and explicit coverage version. Coverage remains incomplete and activation grants no
  executable, catalog-import, compatibility-certificate, or state-space authority.
- Made exact operation-token replay return the original receipt even after source files or later
  activations change. Reusing a token for another request conflicts; equal content under a new
  token records `unchanged` without duplicating the activation revision.
- Added optimistic active-fingerprint concurrency, atomic audit/receipt rollback, non-destructive
  migration rollback behavior, authorization-before-parse handling, typed recovery guidance, and
  active summaries in authenticated application queries.
- Extended capabilities, protocol documentation, catalog coverage ownership, guards, and the live
  three-verb walk. No state space, application content, external file, source scan receipt,
  projection declaration, or game behavior is changed by activation.

## Evidence

- Combined activation, authorization, migration, guard, bootstrap-contract, and live MCP focused
  acceptance: 46 passed, 0 failed.
- The live MCP walk proved preview → dry run → activation → exact replay → authenticated query-back,
  explicit incomplete coverage, remote denial before invalid payload parsing, unique operation
  evidence, and zero state-space creation.
- Full shared suite: 608 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Solution build: passed with 0 warnings and 0 errors.
- Entity Framework model drift check: no changes since the activation migration. The installed EF
  tool emitted only its existing patch-version advisory.
- `git diff --check`: passed; Git emitted line-ending notices only.
- A concurrent web-interface edit briefly exposed an interface before its implementation was
  written. No web files were changed for this slice; the settled tree then passed the full suite.

## Deliberate exclusions and next gate

This activation retains and selects exact redacted source-overlay evidence only. It does not parse
application documents, import catalog or executable records, decide schema compatibility, claim
complete dependency coverage, create or upgrade state spaces, migrate `dnd2024`, enable remote MCP,
or implement AI orchestration. Slice 10G may now add state-space creation as a separate authorized,
dry-run-bound transaction; upgrades and compatibility remain Slice 10H.
