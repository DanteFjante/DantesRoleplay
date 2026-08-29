# System modularization Slice 23 receipt — Standalone local AI and source scanning

Status: **Verified; repository exception recorded**  
Completed: 2026-08-23

## Delivered

- Created the standalone `DantesRoleplay.LocalAI` assembly with no project references.
- Moved provider-neutral embedding/completion contracts, Ollama options/adapters, and provider tests
  into the component; kept compatibility namespaces while changing physical ownership.
- Split game-facing Knowledge vector-index contracts back into the Knowledge consumer adapter.
- Removed consumer task identifiers from provider defaults. The DantesRoleplay host/game adapter now
  supplies the closed task allowlist, including the in-progress generic Information answer task.
- Added a generic, read-only scanner for literal files, recursive directories, and path globs using
  `*`, `?`, and `**`, with canonical ordering, overlap deduplication, allowed roots, no reparse
  traversal, binary/text representation, media types, SHA-256, and bounded file/count/total reads.
- Added an architecture guard that rejects project references and game-system vocabulary in local
  AI production source.

## Evidence

- Local-AI provider/scanner suite: 19 passed, 0 failed.
- Focused Knowledge, routing, Story verifier, Information, and architecture suite: 76 passed,
  0 failed.
- Disposable catalog validation: 426 records valid, 94 advisory warnings; no live data touched.
- Solution build: 0 warnings, 0 errors.
- `git diff --check`: passed (line-ending notices only).
- Full solution: local AI 19/19 passed; shared suite 805 passed and 2 failed.

## Repository-level exception

The two failures are the same independently reproducible `CatalogFeature20Tests` movement/Speed
failures present before the local-AI extraction:

- `Turn_lifecycle_refreshes_remaining_movement_from_each_active_creature_walk_Speed`
- `Missing_or_corrupt_Speed_rejects_refresh_and_normal_movement_without_mutation`

Both tests directly construct their action runner and do not exercise local-AI projects,
registrations, scanners, providers, or consumers. They remain outside this slice rather than being
silently accepted as local-AI regressions.

## Deliberate exclusions

No watcher, automatic ingestion, derived generic index, chunker, OCR/archive reader, MCP operation,
network-path policy, or game-state write was added. The existing Knowledge SQLite-vector index
remains a consumer-owned derived index until a separate generic-index slice is confirmed.
