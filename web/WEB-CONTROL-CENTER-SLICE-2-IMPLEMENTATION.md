# Web Interface Feature 2 Slice 2 implementation — committed effect history

Status: **accepted — delivered by [Slice 2 receipt](WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md)**  
Owner/roadmap: [Web Interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), committed effect history  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let an authorized operator browse immutable accepted effects and inspect one exact event with its linked operation context.  
Exclusions: rejected/dry-run proposals, a parallel effect log, writes, raw-table/file access, arbitrary payload search, ECS/catalog/settings/assistant work, and changes to effect or audit semantics.  
Allowed files/areas: event-ledger and operation-log read contracts/persistence/tests; web effect-history projection, routes, tests, source bundle, and Feature 2 documents.  
Stop point: The `effect-history-panel` reads the bounded timeline and selected-event detail; the other four panels remain unavailable.

## Confirmed decisions

- The user's **continue** on 2026-08-24 confirms that immutable accepted event-ledger entries are the sole past-effects authority.
- An effect list is newest-first by `(timestamp, sequence, id)`, paged by an opaque cursor derived from that complete order. It defaults to 25 events and accepts 1–100.
- The list accepts only exact `type`, `entityId`, and `rootOperationId` filters (each at most 160 characters). It returns summary fields only; payload and before/after evidence are exact-detail data.
- `GET /api/control/effects/{eventId}` returns one immutable event plus its operation only when the event's root operation exists. A missing operation is represented as `operation: null`; it is not a failed/reconstructed substitute.
- Exact detail exposes canonical event payload and the linked operation's observable fields. It excludes `ProjectionJson`; event payload and guard evidence are capped at 64 KiB per returned string and return a stable 413 error when over the bound.

## Prerequisite evidence

- [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md): privileged `control.read` route convention and operator boundary accepted.
- [Slice 1 receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md): source bundle, panel lifecycle, and status route accepted.
- `IEventLedger` and `EventLedger`: append-only accepted event rows already include type, payload, entities, correlation/root-operation identifiers, and a total ascending order.
- `IOperationLog` and `OperationLog`: immutable audit records already own observable intent/result, cited/read procedures, mechanic version, seed, and guard evidence.

## Runtime artifacts

- Revised public owner reads (confirmed by this active slice): `IEventLedger.ListRecentAsync` with a structured cursor request, and `IOperationLog.GetAsync` for one exact operation ID. Neither mutates state or changes stored schema meaning.
- New web-only `CommittedEffectHistory` projection/service and JSON response records.
- New control reads: `GET /api/control/effects` and `GET /api/control/effects/{eventId}` under `control.read`; both are `Cache-Control: no-store`.

## Authoritative state and closed input

The event ledger is authoritative for whether an effect committed and for event payload/before-after receipts. The operation log is authoritative only for the linked operation context. Browser input may supply the listed filters, bounded limit, opaque cursor, and exact event ID. It may not supply event payloads, timestamps, entity memberships, correlation links, operation facts, or any query language.

## Behavior, result, and typed effects

The ledger filters exact indexed values, orders newest-first, reads at most `limit + 1`, and encodes the final returned row as the next opaque cursor. The web projection maps summaries without payloads. Exact detail reads one event and then one exact operation by `RootOperationId`; it never scans operation history or parses/recalculates effects. Event `PayloadJson` contains the receipt evidence, including `beforeJson` and `afterJson` where the accepted event type carries it. This is read-only: no transaction, typed effect, audit write, or event write is created.

## Failure, replay, and rollback contract

- Guard failures remain Slice 0's 403/405 behavior before either owner is called.
- A malformed/oversized cursor, invalid limit, or overlong filter returns 400 with a stable error code and no reads after parsing fails.
- Unknown event ID returns 404. A payload or guard-evidence field over 64 KiB returns 413 for that detail without mutation.
- An unknown linked operation returns `operation: null`; the accepted event remains visible.
- Repeating any request is idempotent and performs no write. Concurrent commits may appear on a new first page; a cursor preserves the older continuation relative to its final returned key.

## Implementation sequence

1. Add owner-level newest-page and exact-operation reads with focused tests.
2. Build the web projection that validates HTTP input and exposes only closed response shapes.
3. Map the two read routes through the established convention and update the one affected custom element.
4. Run focused owner/web tests, build, then full suite; record evidence and leave unrelated panels untouched.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Newest total ordering and opaque continuation | owner/read tests with timestamp/sequence/id ties |
| Exact type/entity/root-operation filters and limits | owner and web projection tests |
| Detail payload plus before/after evidence | event fixture and detail response test |
| Existing/missing linked operation | operation projection tests |
| Invalid cursor/bounds/missing event/oversize detail | stable 400/404/413 tests with no writes |
| Operator/route/security and no-store response | mapped endpoint tests |
| Browser panel isolation | source-bundle test/manual local walk |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~EventLedgerTests|FullyQualifiedName~OperationLogTests|FullyQualifiedName~WebInterfaceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record implementation evidence in `web/WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md`, update the dependency/roadmap status once, and stop before ECS, contracts, settings, assistants, Codex, or site editing work.
