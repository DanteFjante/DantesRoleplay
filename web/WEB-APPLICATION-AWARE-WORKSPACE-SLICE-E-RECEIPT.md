# Application-aware workspace Slice E receipt — confirmed system task orchestration

Status: **accepted**  
Implemented: **2026-08-26**  
Accepted: **2026-08-26**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Implementation boundary: [Slice E implementation](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-E-IMPLEMENTATION.md)

## Delivered boundary

Slice E adds the ruleset-neutral `system-task-orchestration` component. An authorized private
operator or outer AI can either ask the configured local model to resolve a system intent or submit
an already structured agenda. The host exposes only closed system-capability descriptors and safe
context, validates every model-selected capability and input, performs bounded read-only discovery,
and stores an inert exact plan. Planning cannot write.

Resolve mode permits at most three model/read rounds. Both modes permit at most 12 steps and eight
writes, validate the entire batch before any write preflight, and apply bounded per-item and
aggregate input/read limits. A plan records exact descriptor, context, input, result, precondition,
and affected-reference fingerprints. Unknown, secret, unauthorized, malformed, stale, or
unavailable capabilities fail closed.

Writes require a separate principal-bound confirmation of the exact plan fingerprint. Confirmation
expires after five minutes. Execution re-evaluates current authorization and descriptors, then
commits sequentially through each existing typed owner. There is no global rollback claim: a later
failure produces an explicit partial receipt while earlier owner transactions remain truthful.

Before invoking an owner, the coordinator durably stores a running step with host-derived execution
evidence and a deterministic owner operation token. If the process stops after the owner commits but
before the coordinator writes its receipt, lease recovery replays that exact typed-owner request and
repairs the receipt without a second write. Equal requests replay; conflicting idempotency use,
expired confirmation, descriptor drift, authorization drift, or changed current state stays inert.

## Capabilities and private surface

The common capability catalog now serves the permanent reads `system.applications`,
`system.sources`, `system.application-preview`, and `system.dependencies`. Its initial write
allowlist contains version-1 typed adapters for application/source/component-type registration,
application activation, state-space create/upgrade, and legacy-state adoption. Source discovery
exposes configured safe root IDs only; canonical filesystem paths never enter model context,
descriptors, plans, routes, or receipts.

The private web surface adds:

- `GET/POST /api/control/system/conversations/{conversationId}/tasks`;
- `GET /api/control/system/tasks/{taskId}`;
- `POST /api/control/system/tasks/{taskId}/confirmations`; and
- `POST /api/control/system/tasks/{taskId}/executions`.

Read operations require `control.read`, model planning requires `control.ai.message`, and
confirmation/execution require the existing `modify` capability. Request bodies are closed and
cannot inject authority, provider/model configuration, fingerprints, request tokens, paths,
secrets, effects, tools, SQL, or arbitrary capability strings.

`<system-chat>` now separates ordinary **Ask** from **Plan task**. A prepared task shows exact
steps, versions, typed owners, fingerprints, read/preflight evidence, and explicit confirmation.
**Confirm and run** renders aggregate and per-step receipts and warns clearly about partial commits.
No application ECS/game action surface was added.

## Durable artifacts

- Migration `20260826072351_SystemTaskOrchestration` adds task, planning-round, step,
  confirmation, aggregate execution, and execution-step tables, including durable running claims.
- `src/system/system-task-orchestration` owns planning, confirmation, execution, recovery,
  persistence contracts, context materialization, registration, and focused tests.
- `system-capabilities` remains the generic descriptor/authorization/schema dispatcher; each
  existing administration component remains the sole owner of its mutation and transaction.
- `procedure.system.use` documents Ask, task preparation, confirmation, drift checks, execution,
  partial outcomes, and receipt interpretation.
- Catalog coverage explicitly classifies all six new tables and every column as private runtime
  evidence that catalog import must never manufacture.

No normal host database was initialized, migrated, or intentionally changed during this slice.

## Verification evidence

- `SystemTaskOrchestrationTests|SystemCapabilityCatalogTests|WebInterfaceTests`: **97 passed,
  0 failed**.
- `CatalogCoverageTests|GuardTests|SystemTaskOrchestrationTests`: **25 passed, 0 failed**.
- Local-AI test project: **20 passed, 0 failed**.
- Public protocol walk: **6 passed, 0 failed, 2 deliberately skipped**.
- Catalog validation: **144 records accepted**, 21 existing near-duplicate warnings, no live data
  touched.
- Extracted system-workspace JavaScript passed syntax validation.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- Focused migration verification reports the six new tables and no pending model changes.
- Scoped `git diff --check` passed with only line-ending notices.
- The shared suite with the two separately identified assertions excluded passed **1,085 tests,
  0 failed**; the catalog immutability assertion then passed again in isolation.

The full shared suite reported **1,084 passed and 2 failed**. The catalog immutability assertion
passes alone and was affected only by concurrent repository-writing tests. The remaining
reproducible failure is a pre-existing D&D adoption test that validates condition state-effects
against the unrelated character-sheet result schema; it is recorded in `KNOWN_ISSUES.md` and does
not exercise Slice E. Fixing that cross-owner schema/test contract requires its own confirmed D&D
boundary.

## Deliberate exclusions and exit gate

Application ECS/game actions, arbitrary tools/HTTP/SQL/filesystem execution, raw paths, secrets,
page/settings/Codex/trigger writes, system-task recipes as authority, vector indexing, public
hosting, normal-database migration, and live page activation remain excluded.

Slice E was accepted when the user explicitly directed implementation to continue with Slice F on
2026-08-26. This receipt is accepted evidence; Slice F has its own active boundary.
