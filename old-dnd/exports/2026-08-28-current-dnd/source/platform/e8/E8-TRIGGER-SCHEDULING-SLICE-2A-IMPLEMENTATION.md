# E8 trigger scheduling Slice 2A implementation — persistence security hardening

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling dependency tree, D. Persistence](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Close the seven findings from the Slice 2 security review before any observation endpoint or worker can use the store.
Exclusions: No HTTP/MCP surface, authentication implementation, rate limiter, schema-validation invocation, worker, notification writer, action/effect/event write, external adapter, or live database import.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,tests}`, `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs`, additive EF hardening migrations/snapshot, E8 status/receipt documents, and catalog-coverage declarations only if the current-pointer tables require them.
Stop point: Persistence is security-cleared for use by a later separately reviewed endpoint, but remains unreachable from the host.

## Confirmed decisions

- The user explicitly requested all security-review findings be fixed.
- The store owns admission and fire revalidation. Public callers supply a scoped observation submission or registered trigger definition, never an admission proof, fingerprint, fire ID, disposition, or per-call clock.
- One trusted clock is constructor-injected into the store. Tests may inject a fake; later host composition must inject the system UTC clock.
- Mutable current-revision pointers are separate operational rows. Source, structure, trigger revisions and all observation/fire evidence remain immutable.
- A newly appended revision must be greater than the current revision. It atomically advances the pointer. New observations and fires require the exact current revision; existing evidence keeps historical foreign keys.
- A current source may allow only current active structure revisions. Disabled/retired current revisions revoke new use without rewriting history.
- SQLite independently enforces observation-to-source-permission linkage and update/delete immutability. The DbContext enforces the same immutability for `EnsureCreated` tests and ordinary EF writes.
- A concurrent unique-key loser rereads the committed winner and returns replay/conflict when the store owns the transaction; unrelated database failures still bubble.
- Replay windows are integral seconds from 1 through 604800.

## Prerequisite evidence

- [Slice 2 implementation](E8-TRIGGER-SCHEDULING-SLICE-2-IMPLEMENTATION.md) and [receipt](E8-TRIGGER-SCHEDULING-SLICE-2-RECEIPT.md) own the existing migration/store boundary.
- The 2026-08-25 security review identified forged admission/fire evidence, missing current-revision revocation, replay races, unenforced immutability, a missing permission FK, and lossy replay-window persistence.

## Runtime artifacts

- Revise `ITriggerSchedulingStore` so observation admission and fire evaluation occur inside persistence using its trusted clock.
- Add current-pointer records for source, structure, and one-time-trigger revisions.
- Add composite observation-to-permission foreign-key enforcement.
- Add two ordered migrations: the first adds current pointers, the permission FK, and ten SQLite update/delete rejection triggers; the second reinstalls the observation triggers after SQLite's permission-FK table rebuild.
- Add DbContext change-tracker immutability enforcement and focused hostile/concurrency/migration tests.
- No new public route, tool kind, catalog-authored ID, or ruleset mechanic is introduced.

## Authoritative state and closed input

For observations the caller supplies only `ApplicationIdentifier` and `ObservationSubmission`.
The store loads current source/structure rows, validates enabled/active status and permission, checks
the current UTC replay/future window, and derives the fingerprint and observation ID. For fires the
caller supplies only the registered one-time definition; the store loads the current revision and
derives the disposition, occurrence, and fire ID from its clock.

## Behavior, failure, replay, and rollback

- Stale/superseded revisions fail before evidence changes.
- Forged proof records are no longer accepted by the persistence interface.
- Exact concurrent duplicates resolve to the committed record; conflicting duplicates return the closed conflict result.
- Updates/deletes of immutable rows fail through EF and through migrated SQLite.
- Observation inserts lacking the exact five-part source/structure permission fail at the database.
- Sub-second or fractional-second replay windows fail in the domain before persistence.
- Pointer advancement and immutable revision insertion share one transaction; injected failure rolls back both.

## Acceptance matrix

| Concern | Required proof |
| --- | --- |
| Forged observation/fire | Interface no longer accepts derived evidence; store recomputes with its clock and current rows. |
| Revocation | New disabled/retired/superseding revisions reject old-version submissions while old evidence remains readable. |
| Replay concurrency | Two contexts racing an exact request return append/replay and one row; changed identity returns conflict. |
| Append-only | EF update/delete and migrated raw SQL update/delete both fail. |
| Permission FK | Direct valid-shape observation with an unallowed structure fails in SQLite. |
| Time precision | 1 second and 7 days pass; sub-second/fractional/over-limit values fail. |
| Compatibility | Existing focused and full suites pass; no route/component registration appears. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~TriggerScheduling`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --no-build`
- `dotnet test DantesRoleplay.slnx --no-build --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record fixes, migration identity, hostile/concurrency evidence, and deliberate exclusions in
`E8-TRIGGER-SCHEDULING-SLICE-2A-RECEIPT.md`. Do not mark the hardening accepted until all seven
findings have focused evidence and the full suite passes. Stop before Slice 3 route work.
