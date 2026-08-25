# Web Interface Feature 2 Slice 7 receipt — durable local advisory conversations

Status: **accepted**  
Accepted boundary: [Slice 7 implementation document](WEB-CONTROL-CENTER-SLICE-7-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added the ruleset-neutral `assistant-conversations` component and migration
  `20260824111133_AssistantConversations`. Conversations are scoped to one opaque operator;
  messages are immutable; revisions, active-turn exclusion, and global operator/provider request
  idempotency are database constrained and transactionally enforced.
- Added three bounded persistence boundaries around the external call: durable pending input,
  running provider dispatch outside every database transaction, and terminal result plus operation
  audit. Startup recovery changes interrupted pending/running turns to visible failures without
  replaying the provider.
- Registered the existing no-tools, schema-bound Ollama adapter from the seven applied Slice 6 host
  settings. The sole task is `control.assistant.advisory`; the server owns its prompt, response
  schema, transcript bounds, model choice, priority, and identity/usage evidence. The local request
  queue is bounded and reports saturation rather than growing without limit.
- Added operator-scoped provider-status, conversation list/read/create, and append-turn routes under
  the existing control read/local-AI capabilities. Bodies are strict JSON and at most 16 KiB;
  messages, IDs, cursors, revisions, and idempotency keys are bounded; responses are no-store.
- Replaced the assistant placeholder with a browser-native local conversation panel. It shows
  readiness, durable history, ordered messages, terminal failures, model/timing/token evidence, and
  immediately refreshed revisions without leaving stale waiting or empty-list states.

## Compatibility with the AI plans

This is a web conversation/history layer, not an implementation of the
[interaction-orchestration dependency plan](../platform/interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md).
A conversation message or advisory reply is never an intent envelope, resolution receipt,
execution proposal, execution authorization, or learned recipe. The future orchestration owner must
consume only explicitly authorized bounded facts through its own adapter and must create, verify,
and persist its own plans/receipts. No parallel planner, feature search, action execution, or recipe
store was added.

## Verification evidence

- Focused assistant/web/settings/migration/catalog tests: **81 passed**, 0 failed. Coverage includes
  successful replay without a second call, target-crossing idempotency rejection, stale revisions,
  unavailable/timeout/saturation failures, cancellation, unexpected provider exceptions, startup
  recovery, strict web bodies, the fixed local-only UI, and applied provider options.
- Standalone local-AI provider suite: **20 passed**, 0 failed, including a bounded-queue saturation
  case and the existing priority/schema/no-tools checks.
- Migration drift and catalog coverage selection: **7 passed**, 0 failed.
- Clean isolated solution build: **passed**, 0 warnings and 0 errors.
- Public MCP three-verb protocol walk: **2 passed**, 0 failed; no MCP kind or verb changed.
- Full shared suite: **647 passed**, **1 failed**. The sole failure is the already documented,
  unrelated `GuardTests.Both_dispatchers_name_every_kind_in_the_description_a_client_reads` issue:
  concurrent MCP dispatcher work serves ten kinds not named in `GenericCommitTool.cs`. It does not
  exercise assistant conversations, local AI, web routes, or this migration.
- Catalog validation: **passed**, 144 records and the existing 17 near-duplicate warnings; no live
  data was touched.
- Disposable HTTP/browser walk on a fresh migrated database: disabled provider status rendered;
  first and second requests became durable failed turns; list revision advanced from 1 to 2;
  selecting history showed ordered messages and failures; no stale waiting/empty-list state or
  browser warning/error remained.
- `git diff --check`: no whitespace errors; working-copy line-ending warnings only.

The user's running MCP process and live database were not changed. Build/test and browser evidence
used ignored `.tmp/slice7-*` artifacts and a disposable server on port 6227.

## Deliberate exclusions and next gates

No Codex/app-server process, approvals, tool/command/file/network authority, streaming transport,
interaction intent resolution, trusted feature search, execution proposal/verifier, action/effect
write, recipe learning, arbitrary prompt/schema/context selector, automatic provider retry, or
conversation deletion/archive was added.

Codex web integration remains Slice 8 behind its own Sol protocol/lifecycle gate. The separate AI
dependency plan retains all of its confirmation gates and can later integrate through an explicit
adapter without changing this conversation layer into execution authority.
