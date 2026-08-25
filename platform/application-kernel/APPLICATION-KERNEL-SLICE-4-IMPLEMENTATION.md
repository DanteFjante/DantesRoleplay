# Application kernel Slice 4 implementation — deterministic source overlays and candidate manifests

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), D  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Scan registered application sources only through host-configured allowed roots, normalize
generic source documents to redacted logical paths, choose deterministic trust-aware overlay winners,
and return an immutable candidate application manifest/fingerprint without activation.  
Exclusions: Database migrations or new persistent state; source registration changes; catalog import
or parsing; declared record/component/procedure/mechanic schemas; executable contracts; vector
indexing; local/remote model prompting; application activation; protocol kinds; authorization;
state-space binding; and all application-specific branches.  
Allowed files/areas: `src/system/source-registry/{domain,persistence,hosting,tests}/`,
`src/system/application-registry/{domain,tests}/`, existing local-AI document scanner only if a
generic contract correction is necessary, focused tests, this document, its receipt, and
status/link-only plan/roadmap updates. Existing catalog records, migrations, MCP code, and
application host composition are read-only.  
Stop point: Candidate scan/materialization tests prove deterministic winner, shadow, conflict,
trust, removal, path-redaction, and fingerprint behavior; record the receipt and stop before
catalog parsing, persistence, activation, or any executable application contract.

## Confirmed decisions

- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) fixes allowed-root-relative source
  specifications, greater-precedence wins, equal precedence conflicts, equal-trust eligibility,
  no lower-trust overrides, redacted remote paths, immutable candidate manifests, and explicit
  activation as a later operation.
- [Slice 3](APPLICATION-KERNEL-SLICE-3-IMPLEMENTATION.md) persists immutable registrations and
  scan receipts but intentionally provides no scanner, winner, or activation behavior.
- The existing `ILocalDocumentScanner` is the reusable generic file/glob reader. This slice may
  adapt it, but does not give the local AI ownership of application registration or overlays.

## Prerequisite evidence

- `SourceRegistration` validates only relative specifications and persists an allowed-root ID; it
  has no canonical host path and cannot read the filesystem itself.
- `ILocalDocumentScanner` already enforces bounded file/glob scanning, allowed roots, reparse-point
  exclusion, deterministic path ordering, and content hashing, while remaining application-agnostic.
- Slice 3's [receipt](receipts/APPLICATION-KERNEL-SLICE-3-RECEIPT.md) proves the SQLite registry
  boundary and requires this slice to remain before persistence changes or activation.

## Runtime artifacts

- An internal host-facing allowed-root resolver that maps an existing allowed-root ID to a canonical
  path. Its mapping is supplied in process and is never accepted from a model or protocol caller.
- A generic source scanner adapter that combines a registered relative path/glob with the resolved
  root, invokes `ILocalDocumentScanner`, and emits only source ID, media type, content hash, length,
  text/binary indication, and normalized relative path—never an absolute path.
- A pure overlay resolver that receives scanned generic documents and returns one immutable
  `CandidateApplicationManifest` with winners, diagnostic shadow records, problems, and a stable
  fingerprint.

At this slice every scanned document uses the generic logical identity
`file:<normalized-relative-path>`. A later catalog/application adapter may provide the accepted
declared-record identity `(record kind, declared qualified ID)` for trusted parsed files. Generic
documents remain non-executable regardless of whether they win an overlay.

## Authoritative state and closed input

SQLite remains authoritative for application/source registrations. Canonical allowed-root paths are
trusted host configuration; only the resolver can supply them. The scanner receives registered
application/source IDs and relative path/glob specifications. The overlay resolver receives a
closed collection of normalized scanned documents, their registered source trust/precedence, and
their content hashes. It does not read files, databases, configuration, a network, catalog, or a
model. Neither callers nor documents can claim a winner, active revision, executable status, or
application ownership.

## Behavior, result, and typed effects

1. For each requested registered source, resolve its allowed-root ID. An unknown root creates a
   typed candidate problem; no fallback path is guessed.
2. Combine only that canonical root with the already validated relative path/glob, scan with that
   root as the scanner's allowed root, and convert every discovered path to a normalized relative
   path. A traversal/reparse/out-of-root result becomes a problem and never enters a manifest.
3. Group candidate generic documents by application ID and `file:<normalized-relative-path>`.
   Among equally trusted sources, the greatest precedence wins. Equal-precedence competitors make
   the candidate invalid. A lower-trust document cannot displace a higher-trust document.
4. Produce ordinally ordered winners and shadows. Hash the canonical application/source/document
   metadata and problems to produce the manifest fingerprint. Equal inputs produce equal output;
   any source precedence, trust, source ID, logical path, content hash, or problem change changes
   the fingerprint.

No database write, typed effect, active-manifest change, catalog import, index update, or action
transaction occurs. The transaction owner is **none**.

## Failure, replay, and rollback contract

- Unknown application/source registrations, unknown allowed roots, invalid source-relative paths,
  scanner problems, a document outside its resolved root, and equal-precedence candidates return a
  failed candidate manifest with diagnostic codes and no winner for the affected identity.
- A lower-trust document is shadowed by a higher-trust document even when it has higher precedence.
- Re-running equal inputs is byte/ordering-equivalent. Removing an override only changes the new
  candidate; it never exposes a lower source through an already returned candidate or active state.
- Absolute canonical paths, scanner exception details, and raw binary payloads never enter a
  manifest, problem, or fingerprint input.

## Implementation sequence

1. Add pure document, problem, manifest, root-resolver, and overlay contracts plus focused tests.
2. Implement the pure resolver and verify deterministic/failure/no-change behavior without files.
3. Add the `ILocalDocumentScanner` adapter and temporary-directory glob tests, retaining path
   redaction and existing scanner bounds.
4. Register only internal generic services needed by tests; do not alter host/MCP composition.
5. Run focused/full tests, write the receipt, update status once, and stop.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Scanner boundary | A resolved root plus relative glob finds only allowed files; unknown root, traversal, reparse, scanner error, and absolute-path leak fail safely. |
| Winner | Higher-precedence equal-trust document wins; ordering is stable; its source/hash are cited. |
| Trust | Lower-trust content cannot override higher-trust content; it remains diagnostic-only. |
| Conflict | Equal-precedence competitors make the candidate invalid with neither record appearing as a winner. |
| Removal | A new input without the former winner exposes the lower document only in its new candidate. |
| Manifest | Winner/shadow/problem output is immutable, redacted, application-scoped, and fingerprinted deterministically. |
| Safety | Generic scanned documents have no executable/catalog authority; no database row, active revision, index, effect, or protocol surface changes. |
| Repository | Focused tests, solution build, full suite, and `git diff --check` pass. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter SourceOverlay
dotnet test src\system\local-ai\DantesRoleplay.LocalAI.Tests\DantesRoleplay.LocalAI.Tests.csproj --no-restore
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --no-build
git diff --check
```

## Completion receipt and exit gate

Acceptance evidence is recorded in [the Slice 4 receipt](receipts/APPLICATION-KERNEL-SLICE-4-RECEIPT.md).
Do not begin Slice 5 or add catalog parsing, declared-record identity, schema/component registration,
source persistence, activation, system protocol kinds, or legacy application adoption.
