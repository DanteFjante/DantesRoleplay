# Application-aware workspace Slice F implementation — reusable system action and form controls

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice F  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: add reusable browser-native `<system-action-button>` and `<system-form>` elements that
load one exact safe system-capability descriptor, collect only bounded declared input, prepare a
single-step task through the accepted Slice E coordinator, display the server proposal, require a
separate confirmation for writes, and render durable receipts.  
Exclusions: new system capability IDs, capability execution outside system-task orchestration,
application ECS/game actions, arbitrary routes/methods/URLs/tools/SQL/files, hidden authorization or
fingerprint inputs, client-authoritative schemas, automatic confirmation, multi-capability forms,
new persistence/migrations, page composition, live page activation, and changes to local AI.  
Allowed files/areas: the web-interface component manifest; one private read adapter and exact route
for a safe capability descriptor; `SystemWorkspaceElement`; focused web tests; Feature 4 documents
and receipt.  
Stop point: exact route/component tests, extracted JavaScript syntax validation, real-browser
accessibility/event/confirmation smoke, focused shared tests, build, receipt, and acceptance request
complete; stop before Slice G.

## Confirmed decisions

The parent plan already confirms the permanent public module route
`/components/system-workspace.js`, custom-element IDs `system-action-button` and `system-form`, the
single accepted system-task coordinator, server-authoritative schemas, the shared DOM event names,
and the rule that writes must prepare before a separate explicit confirmation. The user's direction
to continue implementing Slice F on 2026-08-26 confirms this leaf and its necessary exact private
descriptor route:

- `GET /api/control/system/capabilities/{capabilityId}`.

No new capability ID, authorization capability, database record kind, or migration is introduced.

The custom-element interface is:

- both elements require `capability-id`, a bounded exact `system.*` ID;
- `<system-action-button>` accepts declared input through a bounded `input-json` attribute or its
  `input` object property; text content remains the visible button label;
- `<system-form>` accepts no caller schema and renders the current server input schema;
- neither element accepts conversation, provider/model, route, method, authorization, descriptor
  fingerprint, confirmation, operation token, expected current state, effect, or output values.

## Prerequisite evidence and owners

- [Slice C receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-RECEIPT.md) proves the common
  authorization-first capability catalog and closed compiled schemas.
- [Slice E receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-E-RECEIPT.md) proves inert submit plans,
  current-state preflight, separate five-minute confirmation, typed-owner sequential execution,
  replay, recovery, and receipts.
- `system-capabilities` owns descriptor compilation and fingerprints. The web adapter may project a
  safe descriptor but cannot alter or recompile it.
- `system-task-orchestration` remains the only coordinator used for preparation, confirmation, and
  execution. The elements call only its existing private routes.
- `web-interface` owns the module, custom-element API, safe descriptor projection, rendering,
  accessibility, and DOM events. Browser code is never authority.

## Runtime artifacts

### Safe descriptor projection

The new read route requires existing `control.read` authorization and returns one exact currently
registered non-secret descriptor:

```json
{
  "id": "system.example",
  "version": 1,
  "fingerprint": "<uppercase sha256>",
  "owner": "component-owner",
  "description": "safe text",
  "mode": "read | write",
  "inputSchema": {},
  "inputSchemaHash": "<uppercase sha256>",
  "procedureIds": ["procedure.system.use"],
  "requiresConfirmation": true,
  "requiresIdempotencyKey": true
}
```

Unknown, malformed, unauthorized, or secret descriptors return a bounded error or not-found result.
The response omits output schemas, authorization evidence, required-capability internals, sensitivity
labels, handler identity, host configuration, paths, secrets, and execution state. Cache headers
remain private/no-store through the existing control boundary.

### Conversation binding

Task persistence currently and deliberately belongs to an exact operator system conversation. A
component internally selects the most recently updated authorized system conversation through the
existing paged route. It does not accept a conversation ID from markup and does not create a
synthetic conversation or invoke a model. If none exists, preparation stays inert and tells the
operator to start a system chat first. This preserves the accepted task foreign key and avoids a
new hidden conversation kind or migration.

## Authoritative state and closed input

The server descriptor is authoritative. Client rendering supports the bounded profile needed by
the current catalog:

- a closed root object;
- string and enum controls;
- integer/number controls with declared bounds;
- boolean checkboxes; and
- bounded object/array/large-string JSON text areas.

Required fields are visibly marked. `const` fields, when present, are server-declared values rather
than editable caller authority. Unsupported root schemas, references that cannot be safely rendered,
or oversized schemas fail closed. Client validation is convenience only; the task coordinator
validates the serialized object again against the exact descriptor schema and fingerprint.

`input-json` and the `input` property must hold one plain JSON object and remain within the accepted
96 KiB per-step limit. Accessors defensively clone values. Prototype-bearing objects, functions,
non-finite numbers, malformed JSON, arrays at the root, and additional markup-derived authority are
rejected before a request.

## Behavior, receipts, and DOM events

1. On connection or `capability-id` change, load the exact safe descriptor and render an accessible
   ready/unavailable state.
2. On activation/submission, validate bounded values, select an existing system conversation, and
   submit a one-step `submit` agenda with a fresh idempotency key.
3. A read capability completes during preparation. Render its safe result and emit
   `system-receipt`; never show a confirmation button.
4. A write capability must return `prepared`. Render capability version, owner, plan fingerprint,
   safe summary, affected references, and the no-global-rollback warning; emit `system-proposal`.
5. Only a later operator click on **Confirm and run** creates the five-minute confirmation and then
   execution request. Render aggregate/per-step receipt truth and emit `system-receipt`.
6. Emit `system-progress` for descriptor loading, preparing, prepared, confirming, executing, and
   completion. Emit `system-error` with a bounded code for every rejected/unavailable state.

All events bubble and are composed. Visible statuses use live regions, every generated control has
an associated label, errors are readable without color, and proposal/receipt details remain
keyboard reachable. CSS custom properties and stable `::part` names permit theming without
replacing behavior.

## Failure, replay, and no-change contract

- A missing/invalid/secret capability, invalid schema projection, malformed/oversized input,
  unsupported field shape, absent system conversation, denied read/AI/modify authority, or network
  failure performs no write.
- Attribute changes abort stale descriptor reads and clear the previous proposal/receipt.
- Double activation while busy is ignored. Every new preparation/confirmation/execution uses a
  fresh browser idempotency key; server-side equal replay and conflicts remain authoritative.
- The component never treats a prepared proposal as execution, never manufactures success from an
  HTTP status, and never narrates over receipt truth.
- Stale descriptor/current-state failures and partial/indeterminate execution are rendered as such.
  A retry never silently starts a newly confirmed batch.

## Implementation sequence

1. Mark Slice E accepted and activate this exact document.
2. Add the safe descriptor DTO/adapter and one `control.read` route over the existing catalog.
3. Add shared bounded fetch, conversation selection, task preparation, proposal, confirmation, and
   receipt helpers inside the existing module.
4. Define `<system-action-button>` with closed declared input and no direct execution path.
5. Define `<system-form>` with server-schema-driven accessible controls over the same helper.
6. Add focused route/projection/security and module contract tests, plus real-browser smoke.
7. Update the component manifest and Feature 4 documents, write the receipt, and stop before G.

## Acceptance matrix

| Case | Required evidence |
| --- | --- |
| Descriptor | Exact non-secret descriptor is returned only after `control.read`; malformed, unknown, secret, and denied requests expose no schema. |
| Action input | Plain bounded object prepares one exact step; malformed/array/oversized/prototype-bearing input sends no request. |
| Form rendering | Current string, enum, integer/number, boolean, array/object, required, const, and large-string shapes receive labeled controls and bounded values. |
| Read | Read task completes without confirmation, displays result evidence, and emits one receipt event. |
| Write | Preparation displays exact plan/owner/fingerprint/affected references and cannot execute before a distinct click. |
| Confirmation | Confirm-and-run uses the returned plan fingerprint/confirmation ID only; stale/expired/denied stays inert. |
| Receipt | Success, partial, failed, stale, unauthorized, and indeterminate aggregate/step truth remains visible and event payloads are bounded. |
| Conversation | Most recent authorized system conversation is selected internally; none produces an accessible no-change recovery message. |
| Lifecycle | Capability changes/ disconnect abort stale reads, reset state, and do not duplicate listeners or requests. |
| Isolation | No application route/state, MCP, path, SQL, provider/model, authorization, request token, expected fingerprint, effect, or arbitrary URL exists in either element. |
| Accessibility | Keyboard activation, associated labels, live status/error text, proposal warning, and receipt details work in a real browser. |
| Compatibility | Existing navigation/chat markup and routes remain unchanged; module registers all four permanent elements once. |

## Verification commands

- focused `WebInterfaceTests`, capability catalog tests, and Slice E orchestration tests;
- extracted `SystemWorkspaceElement` JavaScript syntax validation;
- real-browser fixture smoke for descriptor load, labels, events, proposal/confirmation separation,
  empty conversation, and error states;
- `dotnet build DantesRoleplay.slnx --no-restore`;
- scoped `git diff --check`.

No catalog record, migration, MCP registration, or protocol dependency changes in this slice, so
catalog validation and the protocol walk are not required. Full combined browser/privacy/full-suite
acceptance remains Slice H; focused compatibility is mandatory here.

## Completion receipt and exit gate

Record the delivered elements, safe descriptor route, schema subset, authority/no-change behavior,
browser evidence, focused counts, and exclusions in
`WEB-APPLICATION-AWARE-WORKSPACE-SLICE-F-RECEIPT.md`. Mark F implemented awaiting acceptance and
stop before page composition or Slice G.
