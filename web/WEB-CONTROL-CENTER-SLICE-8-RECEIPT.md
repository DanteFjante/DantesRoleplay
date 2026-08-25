# Web Interface Feature 2 Slice 8 receipt — read-only Codex conversations

Status: **accepted**  
Accepted boundary: [Slice 8 implementation document](WEB-CONTROL-CENTER-SLICE-8-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added the ruleset-neutral `codex-bridge` owner around the pinned
  `codex-cli 0.149.0-alpha.4.1` app-server v2 stdio seam. Each active turn gets a bounded child
  process; initialization, start/resume, streamed messages/activity, interruption, request denial,
  malformed input, process exit, timeout, and saturation have stable outcomes.
- Fixed the server-selected working directory to this repository and every turn to approval policy
  `never` with a read-only, no-network sandbox. The pinned schema does not offer restricted read
  roots, so this boundary guarantees no Codex writes and no Codex network access but does not claim
  OS-level isolation of reads outside the working directory.
- Extended durable assistant conversations with immutable external thread/turn identity and bounded,
  idempotent activity summaries through migration `20260824115228_CodexConversationBridge`.
  Interrupted host processes reconcile to a visible terminal failure and never silently replay an
  old prompt.
- Added operator-scoped Codex status, conversation streaming, history, and cancel routes beneath the
  existing control capabilities. NDJSON exposes only bounded visible agent text and normalized
  activity; hidden reasoning, raw command output, patches, credentials, and protocol frames are not
  persisted or sent to the browser.
- Added the Local/Codex switch, readiness and safety presentation, streamed progress, durable history,
  and explicit cancel control to the uploadable control-center bundle. There are no approval,
  permission, network, path, model, system-prompt, or autonomous-run controls.

## Verification evidence

- Focused Codex/assistant/web/settings tests: **83 passed**, 0 failed. Coverage includes exact JSONL
  frames, start/resume, same-key replay, external-ID immutability, activity idempotency, streaming,
  explicit interruption, process/restart recovery, request denial, malformed/oversized input,
  reasoning exclusion, and bounded concurrency.
- Migration drift and catalog coverage selection: **7 passed**, 0 failed. The new operational table
  and fields are deliberately excluded from game catalog import/export.
- Clean solution build: **passed**, 0 warnings and 0 errors.
- Public MCP app-server-independent protocol walk: **2 passed**, 0 failed; host dependency
  registration did not change the three public MCP verbs.
- Full solution run: local AI **20/20** and shared tests **656/657**. The sole failure is the already
  documented, unrelated
  `GuardTests.Both_dispatchers_name_every_kind_in_the_description_a_client_reads`: concurrent MCP
  dispatcher work serves ten kinds not named in `GenericCommitTool.cs`. It does not exercise the
  Codex bridge, conversation persistence, web routes, or this migration.
- Catalog validation: **passed**, 144 records and the existing 17 near-duplicate warnings; no live
  data was touched.
- Disposable HTTP/browser walk on a fresh database showed the provider switch, exact pinned version,
  repository working directory, read-only/no-network notice, empty Codex history, and no browser
  warnings or errors. A real executable status probe succeeded; no model turn was submitted.
- `git diff --check`: no whitespace errors; working-copy line-ending warnings only.

The installed desktop-package executable could be inspected only after copying it to an ignored
disposable path because Windows denied direct child-process access to its `WindowsApps` location.
Production use therefore requires an independently accessible pinned Codex CLI path through
`Codex__ExecutablePath`. The user's running MCP process and live database were not changed.

## Deliberate exclusions and next gate

No Codex approval, command execution, file change, network grant, MCP side effect, credential store,
browser-selected configuration, interaction-orchestration intent/proposal/execution, background
autonomy, or live model smoke call was added. Slice 9 remains a separate **Sol, xhigh** boundary for
explicit, expiring, turn-scoped side-effect approvals and reconciliation.
