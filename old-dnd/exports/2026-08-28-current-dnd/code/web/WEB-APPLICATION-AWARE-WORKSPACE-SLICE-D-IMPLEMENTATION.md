# Application-aware workspace Slice D implementation — read-only general system chat

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice D  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: add a durable operator-only general system conversation that uses the configured local
model to answer from bounded, provenance-bearing system context without reading application ECS,
files, secrets, or unrestricted history.  
Exclusions: Codex/remote-provider system chat, application conversation behavior, application ECS
or catalog content, filesystem scanning, vector search, system writes, proposals, confirmations,
action execution, recipes, settings/page/history context, live page activation, and normal-database
conversation creation.  
Allowed files/areas: one new `src/system/system-conversations` component; the generic assistant
conversation scope/context-receipt contracts, store, mapping, focused migration, and tests; generic
host composition; existing web system-workspace module, private route adapter, manifests, and
focused tests; Feature 4 documents.  
Stop point: disposable-database migration and focused read-only chat tests, web/security tests,
build, scoped review, receipt, and acceptance request complete; stop before Slice E.

## Confirmed decisions

The user confirmed implementation by instructing work to continue on 2026-08-25. The parent plan
already confirms the permanent `<system-chat>` element, a distinct operator-only system scope,
local outer AI support, and read-only behavior. The confirmed exact runtime artifacts are:

- add immutable assistant-conversation scope values `advisory` and `system`, migrating every
  existing row to `advisory` and never inferring scope from title, provider, prompt, or route;
- add bounded per-turn context receipt fields and a migration named `SystemConversationScope`;
- add component owner `system-conversations`;
- add private routes `GET/POST /api/control/system/conversations`,
  `GET /api/control/system/conversations/{conversationId}`, and
  `POST /api/control/system/conversations/{conversationId}/turns`; and
- extend the already confirmed `/components/system-workspace.js` module with the already confirmed
  `<system-chat>` element.

No new authorization capability, MCP kind, `system.*` capability, application ID, catalog ID, or
public-internet route is proposed.

## Prerequisite evidence and owners

- [Slice C receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-RECEIPT.md) proves exact,
  authorization-first system capability discovery and the bounded `system.applications` read.
- `assistant-conversations` owns operator-bound durable conversations, immutable messages,
  idempotent turns, provider/model identity, terminal failure reconciliation, and operation audit.
- `local-ai` owns only schema-bound completion. It receives strings and a response schema and
  remains unaware of the web, database, applications, files, and system owners.
- `system-capabilities` owns descriptor discovery, exact application metadata read dispatch, schema
  validation, and current private-operator read authorization.
- `procedures` owns current versioned procedure summaries/details. Only active procedures in the
  `system` category subtree may enter this context.
- `authorization` and web control security own verified principals, `control.read`,
  `control.ai.message`, private-host scope, rate limits, and no-store responses.
- `web-interface` owns route mapping and the host-served custom element. Browser code is never
  conversation, model, retrieval, authorization, or execution authority.
- Governing catalog procedure: `procedure.system.inspect` describes safe system inspection. Its
  current summary/detail may be materialized like any other selected active system procedure; the
  procedure file is not read by the local model.

## Runtime artifacts

### Durable scope and receipt

`AssistantConversation.Scope` is required and immutable. Existing advisory local and Codex
services always request `advisory`; the new system service always requests `system`. Store get,
list, append, replay, and provider dispatch require the exact scope, so a caller cannot open or
continue a conversation through the wrong surface.

Each completed system turn persists a generic bounded context receipt containing:

- context profile `system-read-v1`;
- uppercase SHA-256 fingerprint of the exact normalized context passed to the model;
- sorted exact context references cited by and verified for the response; and
- closed response disposition `answered`, `unknown`, `unsupported`, `needs-input`,
  `needs-application`, or `unavailable`.

Advisory and Codex turns retain no system context receipt. The raw materialized context and hidden
model reasoning are never persisted.

### System context

The new `system-conversations` component materializes one closed, deterministic context snapshot
per fresh turn from only:

- up to 16 currently authorized system-capability descriptors, including exact ID, version,
  fingerprint, owner, description, read/write mode, input/output schemas and hashes, procedures,
  sensitivity, confirmation, and idempotency metadata;
- up to 8 current active procedure details selected from the `system` category by descriptor
  references and bounded query relevance, including exact version and source hash; and
- up to 25 registered application summaries obtained only through the Slice C
  `system.applications` capability, including registration revision/fingerprint but no state-space,
  ECS, source-document, or catalog-record content.

Selection is deterministic and case-insensitive over bounded identifier/description tokens. Exact
descriptor procedure references win before text relevance. The normalized UTF-8 snapshot is capped
at 48 KiB; lower-priority entries are dropped deterministically before failure. Empty owners remain
explicit empty arrays. If an authoritative owner is unavailable or returns rejected data, the turn
fails safely before model dispatch.

### Local response contract

The local model receives the bounded transcript plus the context snapshot in one user prompt and a
fixed system prompt stating that it is read-only, has no tools, must answer only from supplied
evidence, must not infer application state, and must return uncertainty when evidence is
insufficient. Its closed response is:

```json
{
  "disposition": "answered | unknown | unsupported | needs-input | needs-application | unavailable",
  "reply": "1 to 8000 characters",
  "evidence": ["exact source reference from the supplied context"]
}
```

At most 24 distinct evidence references are accepted. The host rejects invented or malformed
references, extra fields, invalid dispositions, and out-of-bound replies before persisting an
assistant message. The plain reply remains the visible assistant message; disposition and verified
references are returned and persisted as the turn's context receipt.

### Private web surface

The four dedicated system-conversation routes never accept provider, scope, application ID,
state-space ID, context, prompt, schema, capability selection, procedure selection, evidence,
authorization, or execution instructions. Create accepts only `message` and `idempotencyKey`;
append accepts only `expectedRevision`, `message`, and `idempotencyKey`. Reads use the existing
bounded opaque conversation cursor.

`<system-chat>` accepts no authority-bearing attributes. It can create, list, open, and continue
only the dedicated system conversations, renders disposition and verified evidence, reports
bounded `system-progress`/`system-error` events, aborts disconnected requests, and exposes stable
CSS parts. It contains no application, state-space, MCP, tool, provider, SQL, path, or execute
input. Page composition and live activation remain Slice G.

## Authoritative state and closed input

The trusted host supplies the opaque operator principal and authorization evidence. The service
re-evaluates `control.ai.message` before conversation lookup, context materialization, or provider
dispatch. The capability catalog independently re-evaluates generic private-operator read access.
The database supplies immutable scope, provider/model identity, revision, transcript, and previous
receipts. The context owners supply all descriptors, procedure versions/hashes, and application
revisions/fingerprints.

The user supplies only a bounded visible message, expected revision for append, and idempotency
key. Context selection is retrieval input only; it never becomes authority and cannot broaden the
closed source allowlist.

## Behavior and transaction ownership

1. Authenticate and authorize the private operator at the web boundary and again in the system
   conversation service.
2. Validate the closed request and claim the exact scoped idempotent assistant turn in the existing
   assistant-store transaction.
3. On replay, return the existing exact system conversation without rematerializing context or
   calling the model.
4. Materialize and normalize current bounded system context, calculate its fingerprint and source
   allowlist, then mark the turn running.
5. Call the generic local structured-completion provider once with the fixed task class, prompt,
   transcript/context, response schema, and interactive priority.
6. Validate the closed response and every cited reference against the supplied allowlist.
7. Complete the assistant turn and its context receipt atomically with the visible assistant
   message and existing operation audit. No system owner or application state is changed.
8. Cancellation, provider failure, invalid output, or context failure completes the already-claimed
   turn as terminal failed/cancelled with a safe error and no assistant message.

Conversation and message persistence is the only root transaction owner. System capabilities,
procedures, applications, ECS, pages, settings, files, and external systems remain read-only.

## Failure, replay, and no-change contract

| Failure | Required behavior |
| --- | --- |
| Unauthenticated/wrong authority | Deny before scope lookup, context reads, store claim, or model call. |
| Advisory/system route mismatch | Return not found without revealing the other scope. |
| Codex/provider injection | Reject the closed body; system chat remains fixed to local completion. |
| Application/state/context/schema injection | Reject the closed body before any store or owner access. |
| Stale revision or active turn | Return existing assistant conflict; no context/model call. |
| Equal replay | Return the original result and receipt; no context rematerialization or second model call. |
| Changed replay payload/target/scope | Return idempotency conflict; no model call. |
| Capability/procedure owner unavailable | Terminal safe failure; no assistant message or fallback to files/ECS. |
| Oversized context | Drop lower-priority entries deterministically; if still oversized, fail before model dispatch. |
| Model unavailable/timeout/cancelled | Preserve existing terminal reconciliation and safe error behavior. |
| Invalid model JSON/schema/disposition | Terminal `SYSTEM_CHAT_RESPONSE_INVALID`; no assistant message. |
| Invented evidence reference | Terminal `SYSTEM_CHAT_EVIDENCE_INVALID`; do not store the claim as evidence. |
| Application-scoped question | Return `needs-application` and navigation guidance only; do not query application state. |
| Write/action request | Return `unsupported`; no proposal, confirmation, action, or system mutation in Slice D. |

Failed and successful turns may change only assistant conversation/message/turn/context-receipt rows
and the existing assistant operation audit. They must not change application, ECS, catalog, page,
settings, source, activation, state-space, event, notification, or external state.

## Implementation sequence

1. Confirm the exact scope, component, migration, private routes, and element extension above.
2. Activate this document and update the parent dependency leaf.
3. Extend generic assistant scope and context-receipt contracts/store mapping; add the focused
   migration with legacy rows defaulted to `advisory`.
4. Add the bounded system-context materializer and local system-conversation coordinator without
   changing `local-ai` contracts.
5. Register the component and add dedicated private web adapters.
6. Extend the existing system-workspace module with `<system-chat>`.
7. Add focused scope isolation, authorization order, selection/bounds, provenance, response,
   replay, terminal failure, no-change, route, security, and DOM tests.
8. Run focused migration/runtime/web checks, scoped diff review, and write the Slice D receipt.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Migration | Fresh database and upgrade preserve old conversations as `advisory`; checks/indexes enforce closed bounded values. |
| Scope | Create/get/list/append/replay require exact immutable scope; advisory, Codex, system, and application surfaces cannot cross. |
| Authorization | Denial precedes conversation existence, context reads, and model dispatch. |
| Context | Only current bounded descriptors, active system procedures, and system-level application metadata appear with exact provenance. |
| Privacy | No ECS values, catalog/source content, files, secrets, raw history, prompts, provider configuration, or hidden reasoning enter output/context receipts. |
| Response | Closed dispositions and exact allowlisted evidence survive persistence/reload; invented evidence fails closed. |
| Read-only | Questions may complete assistant/audit rows only; write/action requests create no proposal or system/application effect. |
| Replay | Equal retry returns the original model identity, answer, fingerprint, and evidence without a second call; conflicts remain inert. |
| Compatibility | Existing local advisory and Codex conversation contracts/tests retain behavior under explicit `advisory` scope. |
| Browser | Dedicated routes are private/rate-limited/no-store; `<system-chat>` has bounded accessible states and no authority-bearing inputs. |

## Verification commands

- Focused `AssistantConversationTests`, new `SystemConversationTests`, and migration-model tests.
- Focused `WebInterfaceTests` for route metadata/body closure/security and system-workspace DOM
  contract; existing application-conversation isolation tests.
- Existing local-AI structured-completion tests to prove the provider remains schema-only.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- `git diff --check` over Slice D files.

No catalog validation, MCP protocol walk, browser live activation, normal database migration, full
suite, or external Ollama call is required. Combined full-suite/browser/live acceptance remains
Slice H.

## Completion receipt and exit gate

Write `WEB-APPLICATION-AWARE-WORKSPACE-SLICE-D-RECEIPT.md` with exact migration, scope-isolation,
context/provenance, local-model, route/DOM, no-change, and verification evidence. Mark Slice D
implemented and awaiting user acceptance, then stop before Slice E.
