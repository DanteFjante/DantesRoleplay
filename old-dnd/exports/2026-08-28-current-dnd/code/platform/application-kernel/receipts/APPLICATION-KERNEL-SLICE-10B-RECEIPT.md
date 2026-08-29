# Application kernel Slice 10B receipt — authenticated application/source discovery

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10B](../APPLICATION-KERNEL-SLICE-10B-IMPLEMENTATION.md)

## Delivered

- Added a loopback-only MCP adapter for the accepted private-operator policy. It denies missing,
  non-loopback, and Tailscale-remote markers; remote `/mcp` remains web-boundary `404` in the host.
- Added bounded deterministic application registration list/exact reads and source list/exact reads
  plus latest-scan lookup to the existing registry owners, without changing registration or storage.
- Added authenticated `system.applications` and `system.sources` query kinds under the existing
  `query` verb. No tool or commit kind was added.
- Authorized before identifier parsing, lookup, counts, or result construction. Denials therefore
  reveal no application/source existence and never touch a registry.
- Returned only immutable application metadata/revisions and source allowed-root IDs, relative
  paths/globs, trust/precedence/logical identity, and latest scan evidence. Absolute paths and raw
  operator identity are absent.
- Recorded bounded pseudonymous allow/deny evidence in the existing operation audit and updated the
  authored system-use procedure and component dependency metadata.

## Evidence

- Focused registry, authorization, guard, and live MCP tests: 19 passed, 0 failed.
- Focused final authorization/live protocol tests: 3 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Full shared suite: 556 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Security/game-vocabulary scan found no game or AI vocabulary in the new registry protocol and
  authorization adapter; expected generic `system` identifiers and relative-path validation remain.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions and next gate

This slice does not register an application/source, scan files, preview or activate a manifest,
create/upgrade state spaces, expose dependency impacts, authorize MCP writes, expose remote MCP,
add accounts/grants, or implement game/AI behavior. The next slice may add authenticated,
idempotent, previewable `system.application.register` and `system.source.register` commits while
retaining the same authorization-before-parse/no-change guarantees.
