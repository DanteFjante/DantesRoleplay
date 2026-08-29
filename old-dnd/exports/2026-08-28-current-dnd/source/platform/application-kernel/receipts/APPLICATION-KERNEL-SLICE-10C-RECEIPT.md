# Application kernel Slice 10C receipt — authenticated registration writes

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10C](../APPLICATION-KERNEL-SLICE-10C-IMPLEMENTATION.md)

## Delivered

- Added `system.application.register` and `system.source.register` beneath the existing `commit`
  verb; the MCP surface remains exactly `orient`, `query`, and `commit`.
- Added a standalone `registry-administration` system component. It owns the one SQLite
  transaction joining an immutable application/source registration to its successful operation
  receipt; application and source registries remain their own components.
- Required private-operator `Modify` authorization before JSON parsing or registry access. Direct
  loopback MCP is accepted; missing context and Tailscale-remote markers deny without payload or
  existence leakage.
- Added closed, bounded payloads with 32-character lowercase hexadecimal request tokens, exact
  current/absent fingerprint expectations, safe allowed-root-relative source paths/globs, trust,
  precedence, and logical identity.
- Enforced a successful dry run for the exact canonical request before mutation. The dry-run
  receipt suggests the identical payload; a write without it returns `DRY_RUN_REQUIRED`.
- Reused the operation primary key as the durable idempotency constraint and receipt ID. An exact
  retry returns the same operation ID and immutable result with no duplicate row; different token
  reuse returns `REQUEST_TOKEN_CONFLICT`.
- Added source-registration SHA-256 fingerprints to authenticated source reads so callers can make
  safe exact-current confirmations.
- Preserved no-change behavior for denial, malformed/extra fields, unsafe paths, stale
  expectations, immutable conflicts, token conflicts, and injected audit failure. No migration,
  canonical host path, directory creation, scan, overlay, or activation was added.

## Evidence

- Focused registration, authorization, guard, bootstrap-contract, and live MCP tests: passed.
- Live JSON-RPC walk proved dry-run-required recovery, application/source writes, exact replay,
  query-back confirmation, remote denial before invalid JSON parsing, redaction, and three tools.
- Full shared suite: 580 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions and next gate

This slice does not configure allowed roots, create directories, resolve paths, scan files,
materialize overlays, preview or activate an application manifest, create/upgrade state spaces,
expose dependency impacts, enable remote MCP, add accounts/grants, migrate `dnd2024`, or implement
AI orchestration. The next application-kernel slice should add candidate application preview over
registered sources before any activation work.
