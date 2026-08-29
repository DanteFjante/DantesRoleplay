# Application kernel Slice 10D implementation — authenticated application preview

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel H application preview](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose authenticated `system.application-preview` through `query`; scan one registered
application's allowed-root-relative directory/glob sources, resolve a deterministic candidate
overlay, and return a bounded redacted preview plus activation-grade fingerprint without changing
active state.  
Exclusions: Source/application registration, allowed-root mutation, directory creation, scan-receipt
persistence, catalog parsing/import, executable authority, vectors/models, dependency impacts,
activation, state-space changes, remote MCP, application migration, and game behavior.  
Allowed files/areas: source-registry scanner/overlay contracts, a new application-preview system
component, MCP host configuration/composition/tools/surface/tests, system-use procedure/component
metadata, this document/receipt, and status-only roadmap/dependency updates.  
Stop point: Stop when loopback MCP can preview registered wildcard sources with stable bounded
redacted results, denial occurs before application parsing/scanning, and no application/source/scan
or active-manifest row changes.

## Confirmed decisions

- Slice 0 reserves `query(kind: "system.application-preview")`, fixes configured allowed roots,
  relative path/glob registration, deterministic trust/precedence overlays, redaction, and explicit
  later activation.
- Slice 4 accepted the generic registered-source scanner and candidate overlay resolver; this slice
  adapts those owners rather than introducing another scanner or overlay algorithm.
- On 2026-08-24 the user said “Continue” after being told Slice 10D would scan registered relative
  wildcard sources through the generic scanner, return a candidate fingerprint, and keep local AI
  and activation outside the component. This confirms the reserved query kind and bounded purpose.
- Allowed-root canonical paths are host configuration (`Sources:AllowedRoots:<id>`), never protocol
  input or output. With no configured matching root, preview returns a redacted invalid candidate.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Slice 4 receipt](receipts/APPLICATION-KERNEL-SLICE-4-RECEIPT.md) proves bounded allowed-root
  wildcard scanning, path redaction, trust-aware winner/shadow/conflict resolution, and stable
  candidate fingerprints.
- [Slice 10B receipt](receipts/APPLICATION-KERNEL-SLICE-10B-RECEIPT.md) proves loopback MCP
  authorization-before-parse and redacted application/source discovery.
- [Slice 10C receipt](receipts/APPLICATION-KERNEL-SLICE-10C-RECEIPT.md) proves authenticated source
  registration and source fingerprints without filesystem or activation authority.

## Runtime artifacts

- Add an `application-preview` system component with a provider-neutral preview port and one
  coordinator over application registry, source registry/scanner, and overlay resolver.
- Add configured and empty allowed-root resolvers. IDs must use bounded lowercase segments;
  configured paths are canonicalized once and never returned.
- Register the existing generic local document scanner only as a scanner dependency. It receives
  opaque file specifications and has no application/game dependency.
- Add `system.application-preview` to the query catalog/dispatcher. Inputs are required
  `applicationId` and optional `limit` (default 100, range 1–250) for each winner/shadow/problem
  detail list. Counts and fingerprints always describe the full bounded scan, not the truncated
  response.
- Add no table, migration, source/application record, scan receipt, active manifest, catalog record,
  application fixture, vector index, or AI prompt.

## Authoritative state and closed input

SQLite application/source registrations select what may be scanned. Host configuration alone maps
an `allowedRootId` to a canonical path. The scanner derives file hashes/metadata under those roots;
the overlay resolver derives winners, shadows, problems, and candidate fingerprint. The preview
coordinator adds the immutable application revision fingerprint, ordered source-registration
fingerprints, and a complete ordered scanned-document metadata fingerprint to produce the full
preview fingerprint, including documents excluded by an overlay conflict.

Callers supply only `applicationId` and `limit`. They cannot supply a root/path/glob, source stack,
trust, precedence, document metadata/hash, winner, problem, application revision, candidate
fingerprint, principal, or activation request.

## Behavior, result, and typed effects

Authorization for private-operator `Read` runs before application-ID parsing, registry lookup,
root resolution, or scanning. For an authenticated registered application, scan every registered
source in deterministic order through its configured allowed root, resolve the full candidate,
and calculate a SHA-256 preview fingerprint over application revision, ordered source
registration fingerprints, every scanned document's redacted metadata/hash, and candidate manifest
fingerprint.

Return application/revision/fingerprints, validity, full counts, and bounded ordered winner,
shadow, and problem details. Details contain only opaque IDs, logical relative paths, media type,
hash, size/text flag, trust/precedence, and closed diagnostic messages. Absolute paths, file
content, scanner exception text, and raw identity never appear. Preview writes only the normal
operation audit; no registry, scan-receipt, candidate, catalog, or active-state mutation occurs.

## Failure, replay, and rollback contract

Unauthorized calls return the shared private-operator code without parsing/scanning. Invalid IDs or
limits return `INVALID_APPLICATION`/`INVALID_PAYLOAD`; unknown applications return
`APPLICATION_UNKNOWN`; missing roots, absent sources, unsafe/disappearing files, scanner bounds,
and overlay conflicts return `Ok=true` with `IsValid=false` and closed problems. Unexpected service
absence/failure returns `PREVIEW_UNAVAILABLE`/`PREVIEW_FAILED` with no path detail.

Equal registered state and file bytes produce the same preview fingerprint/order. Any application
revision, source registration, discovered file hash/metadata, winner/shadow, or problem change
changes it. Cancellation/failure creates no state change beyond an audited failure.

## Implementation sequence

1. Extract the registered-scanner port/result into source-registry domain and register its existing
   implementation plus overlay resolver and generic document scanner.
2. Add configured-root resolution and the application-preview component/coordinator with pure
   fingerprinting and focused filesystem/no-change/redaction tests.
3. Add the authenticated query kind, bounded protocol adapter, procedure/component metadata,
   denial-before-scan test, and live loopback/remote JSON-RPC walk.
4. Run focused tests, fresh catalog validation, full shared/local-AI suites, warning-free build,
   and `git diff --check`; record the receipt and update owner status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Registered relative wildcard finds files and returns a valid deterministic candidate. |
| Authorization | Missing/remote context denies before invalid ID parsing or scanner access. |
| Root safety | Unknown/out-of-root/reparse paths produce closed invalid problems with no path leak. |
| Overlay | Trust, precedence, conflict, winners, and shadows agree with the accepted Slice 4 owner. |
| Bounds | Detail limit truncates lists only; full counts and fingerprints remain unchanged. |
| Replay | Equal state/files produce identical fingerprint and ordering. |
| No change | Application/source/scan/active state is unchanged; only query audit rows append. |
| Surface | Capabilities, dispatcher, descriptions, examples, docs, guards, and three-tool walk agree. |

## Verification commands

- Focused source-overlay/application-preview/authorization/protocol test filters.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog`
- Full `DantesRoleplay.Tests` and local-AI suites.
- Warning-free solution build, live three-verb JSON-RPC walk, and `git diff --check`.

## Completion receipt and exit gate

Accepted evidence: [Slice 10D receipt](receipts/APPLICATION-KERNEL-SLICE-10D-RECEIPT.md).

The slice stopped before dependency-impact queries, candidate persistence, activation, state-space
administration, application migration, or AI orchestration.
