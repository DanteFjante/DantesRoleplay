# Application kernel Slice 11H receipt — activated action-catalog publication

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 11H](../APPLICATION-KERNEL-SLICE-11H-IMPLEMENTATION.md)

## Delivered

- Added a ruleset-neutral adapter from exact active procedure/mechanic winners to the immutable
  catalog navigator. It rechecks current source registrations, configured-root containment, file
  length and hash, and same-source JavaScript sidecars without executing application content.
- Kept publication deny-by-default and host-owned. Registration, activation, source trust, and
  loopback access do not publish an application; the host must explicitly allowlist its ID.
- Published one application-qualified collection with authored application root metadata,
  missing-description legacy directory nodes, exact contract inspection content, redacted source
  provenance, deterministic lexical search, and authenticated snapshot cursors.
- Extended the live three-verb protocol walk to prove the activated `dnd2024` view contains exactly
  its 20 ratified procedures and 14 ratified mechanics, supports list/browse/search/inspect, and
  excludes `system.*` and unrelated records.
- Added no MCP kind, request field, database migration, executable alias, rule calculation, vector
  dependency, or application-specific C# branch. No live database was read or written.

## Evidence

- Focused materializer, publication, component-guard, and live catalog protocol checks: 5 passed,
  0 failed.
- Full shared suite: 690 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Catalog validation: 144 records valid; 21 unchanged advisory near-duplicate warnings; no live
  data touched.
- Isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Deliberate exclusions and next gate

This slice publishes only procedure and mechanic discovery records. Component/event/subscription/
world adapters, projection registration, legacy state adoption, non-empty migration, executable
aliases, action execution, vectors, and AI orchestration remain separate gates. Slice 11 may next
address required projection adoption; incompatible donor/state semantics still require Sol review.
