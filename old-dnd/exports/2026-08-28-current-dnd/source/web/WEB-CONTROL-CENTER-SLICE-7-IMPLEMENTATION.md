# Web Interface Feature 2 Slice 7 implementation — provider-neutral conversations and local assistant

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), assistant integration / provider-neutral conversation store and local LLM  
Related future owner: [Interaction orchestration dependency plan](../platform/interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let the authenticated operator create and continue durable local-assistant conversations through the web control center, with bounded schema-valid replies, exact idempotency, visible failures, and no tools or game/system writes.  
Exclusions: Codex/app-server, approvals, streaming tokens, arbitrary prompts/schemas/task classes/context IDs, model tools, effects, catalog/game/settings/page writes, conversation deletion/archive, automatic provider retry, and remote Ollama endpoints.  
Allowed areas: new assistant-conversations component/domain/persistence/migration/tests; local-AI registration seam/tests; host provider/composition/startup; web assistant projection/routes/panel/tests; component/catalog-coverage/roadmap/receipt documentation.  
Stop point: local conversations work and recover across restart; stop before Slice 8 Codex transport.

## Confirmed decisions

The user's **Continue** on 2026-08-24, immediately after Slice 6 acceptance and identification of
Slice 7 as the next Sol gate, confirms the schema, migration, public routes, task ID, and transition
contract below. The existing `control.ai.message` capability protects both conversation-creation and
turn writes. Reads remain `control.read`.

The first provider value is closed to `local`; `codex` remains reserved but is rejected until Slice
8. The host registers the existing `OllamaStructuredCompletionProvider` from the applied seven
Slice 6 settings. Registration never implies readiness: disabled, missing, unreachable, timeout,
saturation, or invalid output are visible provider/turn states.

## Runtime artifacts

New ruleset-neutral `assistant-conversations` component and EF migration
`20260824111133_AssistantConversations` with three tables:

- `assistant_conversation`: `Id` (`conversation.` plus 32 lowercase hex, PK), `OperatorId`
  (opaque `principal.` SHA-256 identity, max 74), `Provider` (`local` in this slice, max 20),
  `Title` (server-derived first-message excerpt, max 120), `Revision` (positive after creation,
  concurrency token), `Status`, `CreatedAtUtc`, and `UpdatedAtUtc`.
- `assistant_turn`: `Id` (`turn.` plus 32 lowercase hex, PK), `ConversationId` (FK/cascade),
  `OperatorId`, `Provider`, `TurnNumber`, `IdempotencyKey` (max 100), `RequestHash` (64 uppercase
  hex), `Status`, terminal error/model identity fields, elapsed/prompt/output token metadata, and
  created/started/completed timestamps. `(ConversationId, TurnNumber)` and
  `(OperatorId, Provider, IdempotencyKey)` are unique.
- `assistant_message`: `Id` (`message.` plus 32 lowercase hex, PK), `ConversationId` and `TurnId`
  (FK/cascade), `Ordinal`, `Role` (`user` or `assistant`), `Content` (max 8,000), and
  `CreatedAtUtc`. `(ConversationId, Ordinal)` is unique. Messages are immutable.

Stable conversation/turn statuses are `pending`, `running`, `awaiting-approval`, `completed`,
`failed`, and `cancelled`; local turns use only pending, running, completed, failed, and cancelled.
Awaiting-approval is reserved for the later Codex owner and cannot be produced here.

This component is the web conversation/history boundary, not the interaction orchestrator. A stored
message or successful advisory reply is never an intent envelope, resolution receipt, execution
proposal, execution authorization, or learned recipe. Future orchestration may consume explicitly
authorized, bounded conversation facts through its own adapter, but it must create and verify its
own plan/receipt records under the interaction-orchestration owner. This slice therefore does not
pre-empt that plan's confirmation gates or create a parallel execution path.

Public core ports are `IAssistantConversationStore` and `IAssistantConversationService`. The store
owns database transactions; the service owns provider orchestration and never exposes a DbContext.

## Fixed local task and closed input

Permanent task class: `control.assistant.advisory`. The host-owned system prompt states that the
model is an advisory, no-tools assistant and must return only the schema. The fixed response schema
is an object with only required string `reply`, length 1–8,000. The provider receives the latest at
most 20 messages from the same operator-owned conversation, newest bounded transcript at most
20,000 characters, plus the new user message. It receives no database handle, tools, raw paths,
secrets, hidden reasoning request, or caller-selected records.

Browser create body is exactly `{provider:"local", message, idempotencyKey}`. Turn body is exactly
`{expectedRevision, message, idempotencyKey}`. Message length is 1–8,000 after CRLF normalization;
idempotency keys are 1–100 characters matching `[A-Za-z0-9][A-Za-z0-9._:-]*`. Bodies are strict JSON
and at most 16 KiB. The server derives IDs, opaque operator scope, title, revision, history, prompt,
schema, priority, identity, timestamps, metrics, and status.

## Transaction, replay, and recovery contract

One accepted request proceeds as three boundaries because a database transaction cannot include an
external model call:

1. One store transaction checks global operator/provider/idempotency replay, validates owner and
   expected revision, rejects another pending/running turn, creates conversation when needed,
   appends the user message and pending turn, increments conversation revision once, and commits.
2. A short store transaction changes that turn from pending to running. The service then makes
   exactly one provider call outside every database transaction.
3. One store transaction changes running to completed/failed/cancelled, appends the assistant
   message only for schema-valid success, copies server-returned model/elapsed/token metadata, and
   records `control.assistant.local-message` through `IOperationLog` in the same transaction.

The idempotency request hash covers normalized provider and message. Replaying the same key and hash
returns the existing conversation/turn without changing revision or calling the provider. Reusing a
key with different payload or conversation returns `ASSISTANT_IDEMPOTENCY_CONFLICT` (409). A stale
revision returns `ASSISTANT_REVISION_STALE` (409). Only one local turn per conversation may be
pending/running.

Request cancellation reconciles the running turn to cancelled using a non-request token. Provider
failure results reconcile to failed with their bounded stable provider code/message. At startup,
all pending/running turns become failed with `ASSISTANT_PROCESS_INTERRUPTED`; their conversations
become failed and an audit operation is recorded. No interrupted call is automatically retried.

## HTTP and response contract

- `GET /api/control/assistants/local/status` — readiness and redacted provider identity/error.
- `GET /api/control/conversations?provider=local&cursor=&limit=` — operator-scoped newest first;
  default 25, max 100; opaque base64url `(UpdatedAtUtc ticks, Id)` cursor.
- `POST /api/control/conversations` — create plus first local turn.
- `GET /api/control/conversations/{conversationId}` — exact operator-owned conversation with
  ordered messages and turn summaries.
- `POST /api/control/conversations/{conversationId}/turns` — append one local turn.

Unknown/wrong-owner conversation returns 404 without disclosing existence. Invalid ID/body/value
returns stable 400. Provider calls return HTTP 200 with a terminal failed/cancelled turn because the
request was durably accepted; only pre-call validation/conflicts are 4xx. Responses are no-store.

The assistant panel shows provider readiness, paged conversations, ordered user/assistant messages,
visible terminal errors and model identity/metrics, and a message composer. It generates a fresh
client idempotency key per explicit send and retains that key while retrying an uncertain request.
There is no prompt/schema/provider free-form selector, delete/archive button, tools, or Codex UI.

## Failure and acceptance contract

- malformed/oversized messages, keys, cursor, provider, revision, or IDs fail before persistence;
- wrong identity fails in the existing control filter; wrong owner is indistinguishable from unknown;
- injected begin/running/final transaction failure never creates a partial terminal assistant reply;
- unavailable/disabled/missing model, timeout, saturation, invalid response, and schema mismatch are
  durable failed turns with no assistant message and no game/settings/filesystem/page change;
- success stores only the visible `reply`, never raw provider JSON, system prompt, hidden reasoning,
  or credentials; and
- repeated completed/failed/cancelled requests are deterministic reads and never second calls.

## Implementation sequence and verification

1. Add domain/store/service contracts, mappings, registration, migration, catalog exclusion, and
   focused transaction/idempotency/recovery tests.
2. Add host applied-options projection, fixed provider registration/task/schema, coordinator, and
   deterministic fake-provider success/failure/cancellation tests.
3. Add strict bounded routes/projection and replace only `<assistant-panel>` with local conversation
   UI; keep other panels unchanged.
4. Run focused assistant/web tests, local-AI tests, clean isolated solution build, full suite,
   migration drift/catalog coverage, public MCP protocol walk because host dependency registration
   changes, catalog validation for component metadata, disposable HTTP/browser walk, and
   `git diff --check`.

Record acceptance in `WEB-CONTROL-CENTER-SLICE-7-RECEIPT.md`, update the roadmap/dependency status
once, and stop before Codex/app-server, approvals, streaming transport, tools/effects, or any
additional assistant provider/context capability.
