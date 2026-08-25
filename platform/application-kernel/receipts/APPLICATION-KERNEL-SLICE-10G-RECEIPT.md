# Application kernel Slice 10G receipt — exact-activation state-space creation

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10G](../APPLICATION-KERNEL-SLICE-10G-IMPLEMENTATION.md)

## Delivered

- Added the ruleset-neutral `state-space-administration` component and authenticated
  `commit(kind: "system.state-space.create")` without adding a public tool or query kind.
- Required private-operator `Modify` authorization before payload parsing or state access, the
  exact closed payload, a successful exact dry run, a new globally unique state-space ID, the
  current activation fingerprint, and `expectedFingerprint: null`.
- Bound each new empty state space to the authoritative registered application revision and exact
  active activation fingerprint. A deterministic binding fingerprint provides confirmation and
  future concurrency evidence without becoming caller-authored or separately stored authority.
- Reused the existing immutable `system_state_space` owner and schema. The ECS registry now safely
  participates in an ambient transaction while retaining its standalone transaction behavior; no
  migration or duplicate state-space table was added.
- Made state-space creation and its successful operation audit one atomic transaction. Audit or
  persistence failure rolls both back, and creation writes no entity, component, effect, source,
  activation, projection, external file, or legacy state.
- Made exact operation-token replay return the original immutable binding after later activation
  drift. Token reuse for another request conflicts, while a new token cannot adopt or rebind an
  existing state-space ID.
- Extended exact authenticated `system.applications` results with bounded state-space summaries so
  callers can confirm application/revision/activation/binding evidence using the existing query.
- Extended capabilities, dispatcher, operating procedure, component metadata, authorization-first
  tests, guards, and the live three-verb protocol walk. State-space upgrade, compatibility, and
  migration remain absent.

## Evidence

- Combined state-space administration, ECS isolation, activation, authorization, migration, guard,
  bootstrap-contract, and live MCP acceptance: 52 passed, 0 failed.
- The live MCP walk proved activation → create-without-dry-run rejection → exact dry run → creation
  → replay → authenticated query-back, remote denial before invalid JSON, one immutable binding,
  and zero entities/components.
- Full shared suite: 617 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Self-contained solution build in isolated temporary output: passed with 0 warnings and 0 errors.
  The normal output executable was already running, so it was deliberately neither stopped nor
  overwritten; a framework-dependent compile also passed with 0 warnings and 0 errors.
- Entity Framework model drift check: no changes since the existing migrations. The installed EF
  tool emitted only its existing patch-version advisory.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions and next gate

This slice creates only a new empty immutable binding to exact active application evidence. It does
not upgrade/rebind an existing state space, decide schema or dependency compatibility, migrate or
backfill data, import executable/catalog records, create entities/components, migrate `dnd2024`,
enable remote MCP, or implement AI orchestration. Slice 10H may now own explicit state-space upgrade
and compatibility evidence as the final Slice 10 transaction.
