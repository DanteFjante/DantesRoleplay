# Web Interface Feature 2 Slice 2 receipt — committed effect history

Status: **accepted**  
Accepted boundary: [Slice 2 implementation document](WEB-CONTROL-CENTER-SLICE-2-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added `IEventLedger.ListRecentAsync`: a read-only, indexed newest-first event page with exact
  type/entity/root-operation filters and a complete `(timestamp, sequence, id)` continuation key.
- Added `IOperationLog.GetAsync` for one exact operation. The web layer does not scan or infer a
  missing operation.
- Added `GET /api/control/effects` and `GET /api/control/effects/{eventId}` through the existing
  `control.read` boundary. Both return `Cache-Control: no-store` and make no change.
- Added the web-only projection that groups page summaries by root operation and returns canonical
  event payload/before-after evidence only for a selected event. It includes observable operation
  context when present, caps payload/guard-evidence fields at 64 KiB, and intentionally omits
  `ProjectionJson`.
- Updated the `effect-history-panel` to load the committed timeline, group effects, fetch exact
  detail on selection, page toward older effects, and remain independent from the four unavailable
  future panels.

No second effect table, event mutation, operation mutation, raw SQL endpoint, arbitrary JSON
search, world-state reconstruction, ECS/catalog/settings/assistant/Codex work, migration, or MCP
surface change was introduced.

## Verification evidence

- Focused event-ledger, operation-log, and web tests: **77 passed**, 0 failed.
- Solution build: **passed**, 0 warnings and 0 errors.
- Full shared test assembly: **592 passed**, 0 failed.
- Disposable local HTTP/browser walk:
  - uploaded the source bundle into a disposable database only;
  - `effect-history-panel` reached `ready` and truthfully rendered its empty state;
  - the remaining four panels stayed independently unavailable; and
  - no browser-console errors occurred.
  The disposable host was stopped after the check.
- `git diff --check`: no whitespace errors; the existing working copy reports only line-ending
  warnings for tracked files.

## Deliberate exclusions and next gate

Slice 2's accepted-event authority is complete. Slice 3 remains blocked until Sol confirms the
bounded ECS/application/catalog discovery contracts and effective catalog-provider composition.
Slice 4 requires the separate site-editor semantics confirmation; settings and assistant slices
retain their own gates.
