# Interaction orchestration Slice 12D implementation — append-only interaction receipts

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Slice 12D](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Completion evidence: [Slice 12D receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Add authoritative, append-only main-SQLite persistence for interaction-resolution evidence and future execution evidence, with race-safe replay and an authorized redacted projection.  
Exclusions: Planner/model calls; proposal verification; action execution; recipes; catalog, application, source, game-state, assistant-message, public MCP/web, or live-database changes.  
Allowed files/areas after confirmation: interaction-orchestration domain/persistence/hosting/tests/manifest; minimal `DantesRoleplay.DataAccess` entities, mappings, EF migration/designer/model snapshot, and DI composition; this document, receipt, and concise owner-status links.  
Stop point: Stop after internal receipt contracts, migration, persistence/read adapters, and tests prove immutable storage, redaction, replay, and rollback. Do not add a planner, executor, recipe, transport, or UI caller.

## Confirmation package

Implementation is blocked until the following permanent storage decisions are confirmed together.

1. Add interaction-owned tables `interaction_resolution_receipt`, `interaction_execution_receipt`, and `interaction_execution_receipt_step` to the existing main host SQLite database. Existing `Operation` rows remain authoritative for detailed action audit; an execution step stores only a linked operation ID and safe interaction disposition. The migration changes no existing table, catalog, or game state.
2. A permanent receipt ID has form `interaction-receipt.<32 lowercase hexadecimal characters>`. A resolution row stores only server-derived opaque provenance: principal; application ID/revision/fingerprint; state-space/session/state revision/effective-set fingerprint; fixed role profile; optional conversation/delegation references; authorization-evidence reference; idempotency key; canonical envelope SHA-256; terminal status/code; optional proposal fingerprint; safe summary/evidence; and UTC creation. It never stores raw intent/query text, player/assistant messages, prompts, traces/chain-of-thought, paths, state projections, effects, or copied catalog bodies.
3. An execution row names one resolution receipt, preserves its principal/application/state-space scope, and stores execute idempotency plus execution-request/proposal fingerprints, safe summary/evidence, creation time, and one closed disposition: `succeeded`, `failed`, `partial`, `skipped`, `stale`, `unauthorized`, `cancelled`, or `timed-out`. Children store ordinal, proposal-step ID, disposition (`succeeded`, `failed`, or `skipped`), and optional existing `Operation.Id`. Slice 12D persists this future-shaped record only in isolation; Slice 12F alone may author one during execution.
4. Evidence is canonical JSON with 0–16 strings, each at most 1,000 characters, plus a 1,000-character safe summary. It may contain only safe status/code, opaque references, exact contract versions/fingerprints, retrieval counts, and operation IDs. A query fingerprint is stored instead of query or intent text. Domain constructors and SQLite constraints both enforce IDs, SHA-256 values, enums, ordinals, bounds, and canonical JSON.
5. Resolution replay identity is `(principal, application, state-space, idempotency key)`: an equal envelope fingerprint returns the original row; a different fingerprint conflicts without writing. Execution adds resolution receipt ID and requires equal execution-request and proposal fingerprints. Unique indexes make this safe under races. Callers never choose receipt IDs.
6. Reading requires a fresh host-derived `ReadReceipt` decision from `IInteractionAuthorizationPolicy`; its allowed principal/application/state-space must exactly match the row. Missing, denied, or mismatched reads return the same absent result. Even an authorized reader receives only the safe projection. The private single-operator policy may be used initially, but the generic store assumes no particular policy or game role.
7. Receipts are indefinitely retained, append-only runtime audit evidence. This slice exposes no update/delete/purge/retention job; a later confirmed retention feature may add a linked tombstone, never rewrite a receipt or reuse its idempotency identity. A receipt and its steps commit in one receipt-store transaction; failure rolls them all back. Slice 12D creates no world/action transaction and makes no cross-transaction atomicity claim. It adds no `IOperationLog` row, because a receipt is interaction-level evidence rather than duplicate public-operation evidence.
8. No public surface changes. Existing three-verb MCP behavior, catalog navigation, assistant conversations, control-center UI, and local-AI boundaries are unchanged. Public receipt access remains a Slice 12F confirmation gate.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Receipt metadata | No D&D rule is interpreted. | Interaction host/application kernel | Values remain opaque provenance. |
| Action outcome | Existing action/mechanic owners calculate and audit it. | `IActionRunner`, operations/audit | Receipts link operation IDs without duplicating outcomes. |
| Authorization | Host derives scope and role. | Application/game authorization adapter | Storage compares exact opaque scope only. |

## Prerequisite evidence

- [Slice 12B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md) establishes envelope/proposal/status contracts, the main-database receipt decision, two-phase consent, and generic authorization.
- [Slice 12C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md) establishes retrieval but deliberately no authoritative receipt storage.
- `IOperationLog` lacks interaction-resolution/rejected-plan evidence; `AssistantConversationStore` owns message text. Receipt rows may carry only an opaque conversation ID and no foreign key that gives this owner control of conversation retention.

## Runtime artifacts after confirmation

- Immutable receipt draft, record, projection, replay, and store contracts.
- Main-SQLite append/read adapter with append-only resolution/execution/step records and replay indexes.
- Minimal EF entities/mappings, one migration, internal DI registration, and focused tests.

## Authoritative state and behavior

The main host SQLite database is authoritative for receipt rows. An authorized envelope supplies all scope/provenance. A caller may supply only a validated terminal result and bounded safe evidence; it may not provide receipt ID, principal, scope, authorization evidence, timestamp, raw model data, result JSON, or arbitrary JSON. An execution draft binds an existing resolution receipt and exact proposal fingerprint; a receipt never authorizes execution.

1. Validate/canonicalize a resolution draft, then decide replay before allocating an ID.
2. Return its existing safe projection on equal replay; return typed conflict on divergent reuse; otherwise append one immutable row.
3. Validate an execution draft against its parent, scope, and proposal fingerprint; append it and ordered steps atomically. No Slice 12D caller executes an action.
4. On read, obtain a fresh `ReadReceipt` decision and return either a scope-matched safe projection or an indistinguishable absent result.

The store owns only its receipt transaction; it has no game-state, catalog, directory, recipe, or derived-index effect.

## Failure, replay, and rollback contract

Invalid/oversized evidence; invalid status/hash/ID; mismatched scope; absent parent receipt; wrong proposal fingerprint; duplicate/non-contiguous step ordinal; forged operation link; denied read; divergent idempotency reuse; and injected database failure change no existing receipt. Equal replay adds no row. A write failure rolls back the receipt and all its steps. Storage invokes no planner, model, catalog search, action, mechanic, or operation mutation.

## Implementation sequence

1. Add pure bounded receipt/replay/projection contracts and focused domain tests.
2. Add entities, mappings, migration, and SQLite adapter; prove fresh migration and rollback.
3. Add exact-scope authorization/redaction reads and replay/race tests.
4. Register internal ports, run full evidence, write the receipt, update status, and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Resolved and every closed non-resolution persist; a future-shaped execution receipt persists ordered operation links without action invocation. |
| Redaction | No raw intent/query, message, prompt, trace, path, projection, effect, result JSON, or copied catalog body is stored/projected. |
| Authorization | Exact allowed scope reads; each denied/mismatched scope receives the same absent result. |
| Replay | Equal replay returns the original row; divergent reuse conflicts; concurrent equal writes leave one row. |
| Integrity | IDs, hashes, statuses, evidence, ordinals, parent/proposal links, and operation links are validated by domain and database. |
| Rollback | Injected failure leaves no partial rows and alters no action/catalog/application/conversation record. |
| Compatibility | Existing operation, conversation, retrieval, local-AI, action, MCP, and web behavior stays unchanged. |

## Verification commands

- Focused receipt-domain, SQLite, authorization/redaction, replay/race, migration, and component-boundary tests; fresh disposable-database migration/round-trip and EF model-drift checks.
- Full shared and standalone local-AI suites; isolated-output solution build; `git diff --check`.
- No catalog validation or protocol walk: no catalog or public protocol change is allowed.

## Completion receipt and exit gate

After explicit confirmation and passing implementation, write `platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md`, mark 12D accepted in the master plan/roadmap, and stop. Slice 12E separately owns the first planner loop and role-bound provider integration.
