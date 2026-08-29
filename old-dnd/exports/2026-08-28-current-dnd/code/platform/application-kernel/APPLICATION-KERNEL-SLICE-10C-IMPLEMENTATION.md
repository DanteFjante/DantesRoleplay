# Application kernel Slice 10C implementation — authenticated registration writes

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel H administrative registration](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Add authenticated, dry-runnable, immutable, replay-safe application and relative-source
registration through the existing public `commit` verb.  
Exclusions: Activation, application preview assembly, state spaces, directory creation, allowed-root
configuration, path resolution, filesystem access/scanning, overlay materialization, dependency
queries, remote MCP, accounts/roles/tokens, game content, and AI orchestration.  
Allowed files/areas: application/source registry domain/persistence/tests, operation-log reuse,
MCP composition/tools/surface/guards/protocol tests, the system-use procedure/component metadata,
this document/receipt, and status-only roadmap/dependency updates.  
Stop point: Stop when both registration kinds authorize before parsing, require dry-run-capable
closed requests, atomically persist registry state and one replay receipt, and pass the live
three-verb protocol walk.

## Confirmed decisions

- Slice 0 accepted the permanent kinds `system.application.register` and
  `system.source.register`, trusted administrative authorization, exact expectation checks,
  idempotency keys, dry-run support, redaction, append-only evidence, and prior-receipt replay.
- On 2026-08-24 the user said “Continue” after the next slice was identified as those two
  authenticated registration writes with mandatory dry-run, durable idempotency, and immutable
  replay behavior.
- The accepted E9 private-host profile remains unchanged: direct loopback MCP may modify and the
  Tailscale web route remains unable to reach MCP.
- Registration selects an existing host-configured `allowedRootId` and a safe relative path/glob.
  It cannot accept an absolute path, create a directory, resolve a path, scan files, or activate an
  overlay.
- The existing operation primary key is the durable idempotency key and receipt ID. A 32-character
  lowercase hexadecimal `requestToken` fits its accepted identity shape; the operation subject
  binds it to a canonical request fingerprint. This reuses the accepted schema and requires no
  migration or repurposed projection/guard fields.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Slice 3 receipt](receipts/APPLICATION-KERNEL-SLICE-3-RECEIPT.md) proves immutable application,
  source, and scan persistence plus safe relative-source validation.
- [Slice 10B receipt](receipts/APPLICATION-KERNEL-SLICE-10B-RECEIPT.md) proves private loopback MCP
  authorization, redacted registry reads, and authorization-before-lookup behavior.
- [E9 Slice 1 receipt](../e9/E9-SLICE-1-RECEIPT.md) proves the shared deny-default private-operator
  policy and pseudonymous audit evidence.

## Runtime artifacts

- Add commit specs `system.application.register` and `system.source.register`; retain exactly three
  public MCP tools.
- Application payload is the closed object `{requestToken, applicationId, displayName,
  description, baseApplications, expectedFingerprint}`. `expectedFingerprint` is null when the
  application must be absent, or the exact current uppercase SHA-256 fingerprint when confirming
  an identical existing registration.
- Source payload is the closed object `{requestToken, applicationId, sourceId, allowedRootId,
  relativePathOrGlob, trust, precedence, logicalIdentity, expectedFingerprint}` with the same
  absence/exact-current expectation rule.
- Add a ruleset-neutral source-registration fingerprint and one registry-administration port whose
  SQLite implementation owns the application/source mutation plus success audit transaction in
  its own system component directory.
- Do not add a table, migration, app record, source record, allowed root, or filesystem artifact.

## Authoritative state and closed input

The application/source registries remain registration authority. The operation ledger owns the
idempotency receipt. The transport-derived private operator owns authorization evidence. Caller
input may select only opaque IDs, immutable metadata, a configured root ID, a relative path/glob,
trust, precedence, exact expectation, request token, intent, and cited procedures. It may not
supply a principal, canonical path, scan result, effective winner, revision, result fingerprint,
operation ID separate from the token, or activation state.

## Behavior, result, and typed effects

Authorization for `Modify` runs before JSON parsing or registry access. Dry-run validates the
closed request, replay binding, exact expectation, bases, source safety/trust/precedence, and
immutable target without reserving the token or changing registry state. A real write opens one
database transaction, rechecks replay/expectation, performs the immutable registration, records
the successful public `commit` audit using `requestToken` as its operation ID, and commits both.

An identical token/request replay returns the same operation ID and reconstructed immutable
receipt without a new row. A different canonical request using the token fails. A new token may
confirm an existing identical registration only by supplying its exact current fingerprint.
Results expose opaque/redacted registration fields and fingerprints, never a canonical host path
or raw principal identity.

## Failure, replay, and rollback contract

Denial returns the shared private-operator code and safe evidence without parsing malformed JSON
or touching a registry. Malformed/extra/missing fields return `INVALID_PAYLOAD`; invalid IDs return
`INVALID_APPLICATION`; unsafe source input returns `INVALID_SOURCE`; unknown bases/application
return `APPLICATION_UNKNOWN`; expectation mismatch returns `REGISTRY_STALE`; immutable conflict
returns `REGISTRATION_CONFLICT`; different request reuse returns `REQUEST_TOKEN_CONFLICT`; and a
write without successful evidence for the exact dry-run request returns `DRY_RUN_REQUIRED`.
Registry or audit failure rolls back both successful registration and receipt. Dry-run and every
failure leave application/source/scan state unchanged.

## Implementation sequence

1. Add the source fingerprint, administration port, transaction implementation, and focused
   persistence tests including replay and rollback.
2. Add both closed commit specs and the authorization-first MCP adapter with direct denial/dry-run
   tests.
3. Update the system-use contract, capability guards, and live loopback/remote protocol walk.
4. Run focused tests, fresh catalog validation, full shared/local-AI suites, warning-free build,
   and `git diff --check`; write the receipt and update owner status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | New application then relative source are dry-run and committed through `commit`. |
| Authorization | Missing/remote context denies before invalid JSON parsing and registry access. |
| Closed input | Missing and extra fields, absolute/traversal paths, invalid trust/IDs/tokens fail. |
| Expectation | Null means absent; exact current fingerprint permits identical confirmation; stale fails. |
| Replay | Same token/request returns the same receipt ID with one registry row and one success audit. |
| Conflict | Same token/different request and same immutable ID/different metadata fail without change. |
| Rollback | Injected audit failure leaves no registration or replay receipt. |
| Redaction | Results/audit expose no absolute path, raw trusted subject, or caller-supplied authority. |
| Surface | Capabilities, dispatcher, examples, docs, and guards agree; tools remain orient/query/commit. |

## Verification commands

- Focused application/source/authorization/protocol test filters.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog`
- Full `DantesRoleplay.Tests` and local-AI test suites.
- Warning-free solution build, live three-verb JSON-RPC walk, and `git diff --check`.

## Completion receipt and exit gate

Acceptance evidence is recorded in
[the Slice 10C receipt](receipts/APPLICATION-KERNEL-SLICE-10C-RECEIPT.md), and the owning Slice 10
roadmap/dependency status is updated. Stop before any preview assembly, activation,
state-space operation, root administration, filesystem scan, overlay change, dependency query,
application migration, or AI integration.
