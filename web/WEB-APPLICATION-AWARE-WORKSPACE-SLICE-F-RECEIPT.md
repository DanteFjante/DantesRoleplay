# Application-aware workspace Slice F receipt — reusable system action and form controls

Status: **accepted**  
Implemented: **2026-08-26**  
Accepted: **2026-08-26**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Implementation boundary: [Slice F implementation](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-F-IMPLEMENTATION.md)

## Delivered boundary

Slice F adds browser-native `<system-action-button>` and `<system-form>` controls to the existing
`/components/system-workspace.js` module. Both controls load one exact current `system.*`
capability contract from the server, submit one reviewed step through the accepted Slice E task
coordinator, and render coordinator truth. They contain no application, game, MCP, filesystem,
database, provider/model, route, method, authorization, effect, or hidden fingerprint authority.

`<system-action-button>` accepts only a bounded plain JSON object through `input-json` or a
defensively cloned `input` property. Its light-DOM text remains its accessible button label.
`<system-form>` accepts no caller-authored schema: it renders labeled controls from the current
closed server input schema, including required values, strings, numbers, integers, booleans, enums,
constants, and bounded JSON object/array fields. Client validation is convenience only; the server
validates the exact object and current descriptor again.

Both controls internally select the latest authorized local system conversation. They do not
accept a conversation ID and do not create a hidden conversation. A missing conversation produces
an accessible no-change message. Read capabilities complete during task preparation and show their
result without confirmation. Write capabilities show the exact version, typed owner, descriptor
and plan fingerprints, affected references, reviewed input, and partial-commit warning, then stop at
a separate **Confirm and run** button. Only that later click can create a confirmation and execution
request. Progress, proposal, receipt, and error events bubble and cross the shadow boundary.

Legitimate coordinator rejections with no planned step—such as closed-schema input failure—remain
visible as a bounded failure card with the server message and durable task reference. They are not
misreported as a malformed HTTP response.

## Safe descriptor projection

The private web surface adds:

- `GET /api/control/system/capabilities/{capabilityId}`.

The route uses the existing `control.read` boundary and common capability catalog. It returns only
the exact current non-secret ID, version, descriptor fingerprint, owner, description, mode, input
schema and hash, procedure IDs, and confirmation/idempotency requirements. Malformed IDs are
rejected; unknown and secret capabilities are hidden; denied discovery returns 403; unavailable
discovery returns 503. Output schemas, authorization evidence, required-capability internals,
sensitivity metadata, handlers, paths, secrets, and execution state are omitted.

## Verification evidence

- `WebInterfaceTests|SystemCapabilityCatalogTests|SystemTaskOrchestrationTests`: **99 passed,
  0 failed**.
- Broader `GuardTests|CatalogCoverageTests|WebInterfaceTests|SystemCapabilityCatalogTests|
  SystemTaskOrchestrationTests`: **116 passed, 0 failed**.
- Extracted `SystemWorkspaceElement` JavaScript passed `node --check`.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- A real browser loaded a disposable fixture from a dedicated temporary SQLite database. An exact
  `system.applications` action completed and displayed its typed result without confirmation.
- The action-button write and generated form each prepared `system.application.register`, showing
  exact owner/fingerprint/input/affected-reference evidence and **Confirm and run**. No receipt was
  present and neither confirmation button was clicked.
- The generated form exposed associated accessible labels and required/constraint help. A rejected
  invalid read input displayed the closed-schema error and task reference rather than executing or
  claiming success.
- The disposable host was stopped and its database and SQLite sidecars were removed. The normal
  host database was not initialized, migrated, or intentionally changed.

No catalog records, database migrations, MCP registration, or dependency registration changed in
this slice, so catalog validation and the public protocol walk were not required.

## Deliberate exclusions and exit gate

Application ECS/game actions, application-page composition, live page activation, new system
capability IDs, multi-capability forms, arbitrary execution, automatic confirmation, new
persistence, vector search, and local-AI changes remain excluded.

Slice F was accepted when the user explicitly directed implementation to continue with Slice G on
2026-08-26. This receipt is accepted prerequisite evidence; Slice G has its own active boundary.
