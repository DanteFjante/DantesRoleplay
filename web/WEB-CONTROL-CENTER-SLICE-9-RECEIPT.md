# Web Interface Feature 2 Slice 9 receipt — explicit Codex approvals

Status: **accepted**  
Accepted boundary: [Slice 9 implementation document](WEB-CONTROL-CENTER-SLICE-9-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added operator/turn-scoped approval evidence through migration
  `20260824122703_CodexTurnApprovals`. The conversation owner persists bounded normalized details,
  fingerprint, optimistic revision, decision, and lifecycle state while excluding raw protocol,
  output, patches, credentials, and absolute repository paths from the web document.
- Changed every pinned `codex-cli 0.149.0-alpha.4.1` turn to approval policy `on-request` while
  retaining the read-only, no-network baseline. Command, repository file-change, managed-network,
  and supported explicit permission requests are normalized against the active thread/turn and a
  closed repository boundary; malformed, unsupported, or outside-root requests fail closed.
- Added the durable decision-before-dispatch lifecycle. The operator may accept one request,
  decline it, cancel the turn, or let the two-minute timer expire. Exact app-server responses are
  sent once, permission accepts use `scope:"turn"`, and resolution, process loss, terminal turns,
  and startup recovery close unresolved evidence without claiming external rollback.
- Added the protected `control.codex.approve` route and the control-center approval presentation.
  The browser supplies only route IDs, expected approval revision, and `accept|decline|cancel`;
  it cannot edit the request, select a model/path/sandbox, grant a session, or emit policy
  amendments.

## Verification evidence

- Focused Codex/web/migration/catalog tests: **85 passed**, 0 failed. Coverage includes one-request
  accept/decline/cancel, exact turn-scoped permission responses, non-approvable requests, request
  fingerprint replay/mismatch, expiry, simultaneous decisions, missing-process failure,
  resolution, strict request bodies, route authorization, and conditional UI controls.
- Clean solution build: **passed**, 0 warnings and 0 errors.
- Full solution run: local AI **20/20** and shared tests **663/664**. The sole failure is the already
  documented, unrelated
  `GuardTests.Both_dispatchers_name_every_kind_in_the_description_a_client_reads`: concurrent MCP
  dispatcher work serves ten kinds not named in `GenericCommitTool.cs`. It does not exercise Codex
  approvals, their persistence, the approval route, or the control-center panel.
- Public MCP protocol walk: **2 passed**, 0 failed. Catalog validation passed for **144 records**
  with the existing **17** near-duplicate warnings and touched no live data.
- A disposable fresh-database HTTP/browser walk loaded the complete control center, switched to the
  Codex panel, showed the read-only/no-network and one-time-decision boundary, preserved the other
  panels, and reported no browser warnings or errors. The conditional pending-approval controls are
  covered by deterministic web tests; no live Codex model turn was submitted.
- `git diff --check`: no whitespace errors; working-copy line-ending warnings only.

## Deliberate exclusions

There is no `acceptForSession`, session-scoped permission, workspace-write or danger-full-access
default, caller-edited command/path/host/permission, exec/network-policy amendment, generic MCP
elicitation, dynamic tool, credential store, hidden-reasoning persistence, interaction-planning or
execution authority, background autonomy, or live model smoke call. The pinned schema still does
not provide OS-level read-root isolation; the repository remains the fixed working directory, not
a claim that Codex cannot read anything else permitted by the host operating system.
