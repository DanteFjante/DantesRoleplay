# Application kernel Slice 10B implementation — authenticated application/source discovery

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel H application/source queries](APPLICATION-KERNEL-DEPENDENCY-PLAN.md) and [E9 private-host MCP parity](../e9/E9-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Authenticate loopback MCP as the accepted private operator and expose bounded exact/list
inspection through `system.applications` and `system.sources`.  
Exclusions: Registration/preview/activation/state-space commits, dependency queries, remote MCP,
accounts/tokens/roles, new migration, application activation, filesystem scanning, game content,
and AI orchestration.  
Allowed files/areas: authorization/application/source registry contracts and persistence/tests,
MCP server composition/tools/surface/guards/protocol tests, system-use procedure/component metadata,
this document/receipt, and status-only dependency/roadmap updates.  
Stop point: Stop when both read kinds authorize before lookup, return bounded redacted results,
deny non-loopback/missing context without existence leakage, audit safe evidence, and pass a live
three-verb protocol walk.

## Confirmed decisions

- On 2026-08-24 the user said “Continue” after being told the next slice would adapt loopback MCP
  into the accepted private-operator policy before administrative `system.*` operations. This
  confirms the already proposed permanent read kinds `system.applications` and `system.sources`.
- MCP stays unavailable through the Tailscale remote web hostname. Direct loopback MCP is the only
  authenticated MCP profile in this slice and maps to the same pseudonymous private operator.
- `system.applications` accepts optional `applicationId` and `limit` (default 50, maximum 100).
  `system.sources` requires `applicationId`, accepts optional source `id` and `limit`, and returns
  the latest scan receipt only. There is no absolute path, raw identity, or hidden conflict detail.

## Prerequisite evidence and runtime artifacts

- [E9 Slice 1 receipt](../e9/E9-SLICE-1-RECEIPT.md) proves the shared deny-default policy and
  pseudonymous evidence contract.
- [Application-kernel Slice 3 receipt](receipts/APPLICATION-KERNEL-SLICE-3-RECEIPT.md) proves the
  immutable application/source/scan persistence and path-redaction contract.
- Add bounded application list/description, bounded source list/exact read, and latest-scan ports
  without changing registration semantics or database shape.
- Add an ASP.NET MCP request authorizer that accepts only a loopback peer at `/mcp`, derives no
  authority from tool input, and evaluates the shared private-host read capability.
- Add two query specs/dispatchers and a thin adapter returning uniform envelopes and literal calls.

## Behavior and failure contract

Authorization runs before ID parsing, registry lookup, counts, or response construction. Denial
returns the stable shared authorization code, a callable recovery, no registry data, and bounded
guard evidence in the operation audit. Malformed application/limit/source input returns
`INVALID_APPLICATION` or `INVALID_PAYLOAD`; an authenticated missing record returns
`APPLICATION_UNKNOWN` or `SOURCE_UNKNOWN`. Reads never mutate registry or scan state.

Results are deterministically ordered. Applications include immutable registration metadata and
revision/fingerprint/base IDs. Sources include only allowed-root ID, relative path/glob, trust,
precedence, logical identity, and latest immutable scan receipt. List results are capped at 100.

## Implementation sequence and acceptance

1. Extend existing registry ports/persistence with bounded deterministic reads and focused tests.
2. Add the loopback MCP authorizer and register the shared policy/context adapter.
3. Add the two query kinds, adapter, system-use contract, guard agreement, direct denial tests, and
   live JSON-RPC success/audit walk.
4. Run focused tests, catalog validation, full shared/local-AI suites, warning-free build, and
   `git diff --check`; write the receipt and update owner status.

Acceptance requires exactly three MCP tools, authorization-before-lookup proof, loopback success,
missing/remote context denial parity, no absolute path or raw principal leakage, bounded ordering,
safe audit evidence, no commit kind, no migration, and no game/AI dependency.

## Completion receipt and exit gate

Acceptance evidence is recorded in
[the Slice 10B receipt](receipts/APPLICATION-KERNEL-SLICE-10B-RECEIPT.md). Stop before any
administrative commit, preview, activation, dependency-impact query, or remote MCP exposure.
