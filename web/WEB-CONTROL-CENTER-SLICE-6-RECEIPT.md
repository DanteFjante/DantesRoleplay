# Web Interface Feature 2 Slice 6 receipt — versioned host setting overrides

Status: **accepted**  
Accepted boundary: [Slice 6 implementation document](WEB-CONTROL-CENTER-SLICE-6-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

Retrospective evidence: [Sol xhigh ratification](WEB-CONTROL-CENTER-SLICE-6-SOL-RATIFICATION.md)
closed the simultaneous first-write conflict path and reverified the accepted boundary.

## Delivered boundary

- Added the ruleset-neutral `host-settings` component and migration
  `20260824105304_HostSettingOverrides`. Its current/applied heads and immutable value revisions are
  constrained, operation-linked, and explicitly excluded from game catalog import/export.
- Added one transactional store for stage, reset, rollback, optimistic revision conflicts, bounded
  history, and startup apply markers. Each accepted revision and each non-empty startup apply is
  committed atomically with its operation record; failed/no-change requests write nothing.
- Extended the host definition provider with closed validation and normalization for the existing
  seven keys. Durable heads are validated before the host listens. Reset inherits the original
  configuration/default; invalid or unknown durable values abort startup.
- Added version/current/applied/pending projection, history, PUT, reset, and rollback routes under
  the confirmed `control.settings.write` boundary. Bodies are 16 KiB, strict JSON; identity is
  server-derived; all responses remain no-store.
- Made the settings panel editable for public values with explicit stage, reset, history, and
  confirmed rollback actions. It explains restart-only behavior and contains no restart control.

The local-completion provider remains unregistered and no model is called. No arbitrary
configuration, secret, database/listen/MCP/Tailscale setting, live refresh, process restart,
assistant, Codex, catalog/game, or page-authority change was added.

## Verification evidence

- Focused host-settings/web tests: **63 passed**, 0 failed. Store coverage proves append-only
  update/reset/rollback, stale/no-change behavior, audit rows, history ordering, and idempotent
  startup marking; provider tests cover normalization and reset/override application.
- Local-AI tests: **19 passed**, 0 failed.
- Migration drift/catalog coverage/host-settings selection: **9 passed**, 0 failed.
- Clean isolated solution build: **passed**, 0 warnings and 0 errors.
- Public MCP protocol walk: **2 passed**, 0 failed.
- Full solution run: local-AI **19/19** and shared tests **632/633**. The one failure is the concurrent,
  unrelated `GuardTests.Both_dispatchers_name_every_kind_in_the_description_a_client_reads`, where
  `GenericCommitTool.cs` does not name ten kinds served by other in-progress MCP dispatcher work.
- Catalog validation: **passed**, 144 records and 17 existing near-duplicate warnings; no live data
  was touched.
- Disposable HTTP/restart walk: staged `local-completion.enabled` revision 1, observed pending value
  and restart flag, restarted the host, then observed value `true`, source `override`, revision and
  applied revision 1, no pending value, and provider runtime still `not-registered`.
- Disposable browser walk: all seven values rendered; exact Profile detail exposed an enabled edit
  field and stage action, disabled reset before history existed, showed revision/apply/restart
  evidence, and logged no browser errors.
- `git diff --check`: no whitespace errors; working-copy line-ending warnings only.

The user's running MCP process was not interrupted. Build/test output and the restart walk used the
ignored `.tmp/slice6-artifacts` and `.tmp/slice6-live` paths.

## Deliberate exclusions and next gate

The UI never restarts the server and the host still does not register local completion. Slice 7 is
next and retains its Sol gate for conversation persistence, local-provider registration,
idempotency, external-call transaction boundaries, and failure recovery.
