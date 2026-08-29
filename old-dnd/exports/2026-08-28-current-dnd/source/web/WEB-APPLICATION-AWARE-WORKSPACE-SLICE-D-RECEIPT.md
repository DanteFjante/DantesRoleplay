# Application-aware workspace Slice D receipt — read-only general system chat

Status: **accepted**  
Implemented: **2026-08-25**  
Accepted: **2026-08-26**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Implementation boundary: [Slice D implementation](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-D-IMPLEMENTATION.md)

## Delivered boundary

Slice D adds a ruleset-neutral `system-conversations` component and a distinct durable `system`
assistant scope. The private operator can create, list, open, and continue read-only general system
conversations backed by the configured local structured-completion provider. Existing local
advisory and Codex conversations remain in the immutable `advisory` scope and cannot cross the new
surface.

The service constructs one deterministic context snapshot per fresh turn from currently authorized
system-capability descriptors, active referenced system procedures, and bounded application
registration summaries. It does not expose application ECS values, catalog/source contents,
filesystem data, secrets, settings, page contents, raw history, provider configuration, prompts,
or hidden reasoning. The snapshot is capped at 48 KiB and contains at most 16 capabilities, 8
procedure details, and 25 application summaries.

The local model has no tools and returns a closed disposition, visible reply, and optional exact
evidence references. The host validates every reference against the supplied context before it
atomically stores the assistant reply and a generic receipt containing `system-read-v1`, the exact
context fingerprint, verified cited references, and the closed disposition. Invented evidence and
malformed responses fail without an assistant message. Equal idempotent retries replay the original
result without rematerializing context or calling the model again.

## Durable and public artifacts

- Migration `20260825203500_SystemConversationScope` adds immutable closed assistant scope and
  bounded context-receipt columns. Existing rows become `advisory`; the SQLite upgrade remains
  transactional.
- Component owner `src/system/system-conversations` contains the coordinator, bounded context
  materializer, contracts, registration, and focused tests.
- Private routes are `GET/POST /api/control/system/conversations`,
  `GET /api/control/system/conversations/{conversationId}`, and
  `POST /api/control/system/conversations/{conversationId}/turns`.
- `/components/system-workspace.js` now also defines `<system-chat>`. It accepts no authority-bearing
  attributes and uses only the dedicated routes.

The component is intentionally not embedded into the home, control-center, or application pages in
this slice. Page composition remains Slice G. No normal host database was initialized, migrated, or
mutated during verification.

## Authority and no-change evidence

- Every service operation re-evaluates the existing private-operator capability before scope lookup,
  context reads, turn claims, or model dispatch.
- Reads use `control.read`; create/continue operations use `control.ai.message` and may change only
  assistant conversation/message/turn rows plus the existing operation audit.
- Advisory and system create/get/list/append/replay paths require an exact scope. A wrong-scope
  lookup returns not found without revealing the other conversation.
- Request bodies cannot select provider, model, scope, application, state space, prompt, schema,
  context, procedure, capability, evidence, path, tool, or execution instructions.
- Write/action requests can only be answered as unsupported. Slice D creates no proposal,
  confirmation, recipe, application effect, system mutation, or external action.

## Verification evidence

- `AssistantConversationTests|CodexBridgeTests|ApplicationConversationTests|SystemConversationTests|WebInterfaceTests`:
  **130 passed, 0 failed**.
- `MigrationDriftTests|CatalogCoverageTests|SystemConversationTests`: **11 passed, 0 failed**.
- `ApplicationConversationTests|SystemCapabilityCatalogTests|PrivateOperatorAuthorizationTests|GuardTests|CatalogCoverageTests`:
  **55 passed, 0 failed**.
- Local-AI test project: **20 passed, 0 failed**.
- Upgrade from `20260825190327_TriggerSchedulingPhoneCompanion` succeeded and verified an existing
  conversation becomes `advisory`.
- EF pending-model check reported no pending model changes.
- Extracted system-workspace JavaScript passed syntax validation and registered both
  `system-navigation` and `system-chat`.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- `git diff --check`: passed; only existing line-ending notices were reported.

No catalog semantic records or MCP protocol/dependency surface changed, so catalog validation and
the protocol walk were not required for this slice. Live page activation, a real external local-model
call, the normal database migration, system writes, and combined browser/full-suite acceptance remain
deliberately deferred to their ordered slices.

## Exit gate

Slice D was accepted by the user on 2026-08-26 and remains stopped before Slice E. Confirmed system
task planning, execution, and receipt work requires its own active implementation boundary.
