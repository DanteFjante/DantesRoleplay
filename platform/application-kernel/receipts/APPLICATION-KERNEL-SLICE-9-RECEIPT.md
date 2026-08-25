# Application kernel Slice 9 receipt — deterministic catalog navigation

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 9](../APPLICATION-KERNEL-SLICE-9-IMPLEMENTATION.md)

## Delivered

- Added an immutable, validated application catalog-manifest contract with authored logical
  collections/nodes, explicit missing-description status, exact effective record content/version,
  content fingerprint, and redacted source provenance.
- Added an in-memory `ICatalogNavigator` for stable collection listing, root/intermediate browsing,
  direct/subtree kind counts, combined node/record paging, lexical search, and exact inspection.
- Added Unicode-normalized deterministic lexical ranking: qualified ID, alias/match phrase, name,
  prefix, then all-token text; ties use record kind and qualified ID.
- Extended authenticated cursor decoding with static scope validation so continuation page keys are
  preserved while a changed manifest/filter/page/sort scope fails as `CURSOR_STALE`.
- Kept the component vector-free, local-AI-free, game-neutral, and free of database, migration,
  activation, catalog import, authorization, or protocol dependencies.

## Evidence

- Focused catalog-navigation/application/source contract tests: 17 passed, 0 failed.
- Full shared suite: 534 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions

This slice does not parse source files into manifests, persist or retain historical manifests,
activate an application revision, enforce authorization/redaction, attach dependency-impact
citations, register `system.catalog.*` transport kinds, import legacy catalog data, or implement
AI orchestration. Those remain owned by activation/import, projection, protocol, adoption, and AI
slices.
