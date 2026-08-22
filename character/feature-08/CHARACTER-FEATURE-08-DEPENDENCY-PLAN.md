# Character Feature 8 dependency plan — stateless guided creation and website parity

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; guided MCP flow awaits stable CH6/CH7 evidence, and the website builder additionally awaits the Website/API semantic-write and exposure decisions.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH5–CH7, and [Website and API plan](../../WEBSITE_AND_API_PLAN.md). It writes no runtime artifact.

CH8 improves guidance and presentation only. CH5 remains the sole creation transaction; CH6 remains discovery/transport owner; CH7 remains evidence/correction/expansion gate; and the website remains a loopback trusted-host consumer until CH14 and the website exposure design are accepted.

## Target capability

A host can answer stateless, source-backed creation questions one at a time, abandon at any point without persistent state, preview the one complete CH5 request, and submit that exact request through MCP or a human-facing page. For identical canonical input, both surfaces use the same kernel validation/create path and yield equivalent character-world state and validation outcomes.

### Included

- A non-persistent MCP guide that derives required next questions, allowed choices, cardinalities, exclusions, and completion status from CH0–CH7 contracts/content.
- A full submitted-answer object on every guide call; no server-side wizard/session/draft/temporary actor/choice reservation.
- A server-rendered, progressively enhanced website builder after the website host has an approved semantic write bridge.
- One shared application-level create/validate request/result model consumed by MCP and HTTP, not by browser-to-database or browser-to-MCP calls.
- Normalized parity, abandonment, stale-content, validation, accessibility, and exposure tests.

### Excluded

- A second creation resolver/transaction, client-side rules calculation, browser authority, raw effects/components, direct database CRUD, arbitrary workflow/pipeline, public remote player creation, authentication, collaboration, profile correction, advancement, or source expansion.
- Persisted drafts, resumable wizard state, generated choices, hidden default answers, user-provided derived values, cached validate plans reused for create, and automated retry.
- A requirement to implement the draft executable-workflow feature. If later accepted, it may help navigation only and must not wrap CH5 in another root transaction.

## Ownership and surface boundary

| Concern | Owner and CH8 rule |
| --- | --- |
| Question truth, choices, source identity, completion legality | CH0–CH4/CH7 content and CH5 validation. Guide queries/asks; it never declares rules or accepts a choice outside the root validator. |
| Create/validate effects, receipt, audit, rollback | CH5 ActionRunner coordinator. Both MCP and website call its shared in-process command/service boundary. |
| MCP discovery/action transport | CH6/current three-verb surface. Guide uses current query/action kinds unless a separate `procedure.mcp.add-tool` confirmation proves a need. |
| HTTP host/rendering/assets/SSE/security | Website/API plan. CH8 adds no browser route or write endpoint until its loopback semantic-command and error-envelope decisions are accepted. |
| Identity/permissions/audience | CH14 and website exposure policy. Before then the builder is trusted-host/loopback only; profile visibility grants nothing. |
| Draft/cancel state | None. The client owns an untrusted local answer object; server state is unchanged until CH5 `create` succeeds. |

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Guide contract | `procedure.character.guide`, governing stateless question/answer interpretation and recovery; it creates no actor state. |
| Guide mechanic | `mechanic.dnd2024.character.guide`, a zero-effect mechanism/command that receives a complete current answer object and returns questions/preview only. |
| Shared application boundary | Exact internal interface/DTO IDs are undecided. Confirm after searching CH5 ActionRunner/staged composition and the HTTP host; it must not be an MCP tool handler or expose raw effects. |
| Website route/write names | Deliberately undecided until Website/API Slice 0 and semantic-write confirmation. A route is a public surface decision, not an assumption in this character plan. |

The guide's permanent IDs require confirmation. If CH5 cannot expose a pure, versioned validation result to the guide and HTTP bridge without duplicating resolution, stop and extend the generic command/staged-composition boundary under its owning plan before authoring a guide.

## Stateless guide contract

Each guide request contains only a `mode` (`next` or `preview`), an optional complete current answer object in the same canonical field vocabulary as CH5, and no server-generated session identifier. `next` returns the first unresolved required field or closed choice set plus its stable key, display label, source locator/definition ID, allowed option keys, cardinality, and dependency explanation. `preview` returns all unresolved questions or a canonical create-request preview when complete. Neither returns source-rule prose, raw effects, an actor ID, an item instance, an audit ID, hidden campaign facts, or an authorization claim.

The guide calls the same pure CH5 validation/resolution path in non-writing mode. It must distinguish incomplete from invalid: incomplete input returns only the next legal question(s); invalid present input returns a named correction without guessing/replacing it. Completion never creates a server draft, reserves an option, or makes `create` safe without revalidation. The user may abandon, reload, alter answers, or move between MCP and web with no cleanup operation.

The first guide supports only the complete CH0 fixture and closed choice forms already accepted by CH3–CH5. A future content option appears only after CH7 expansion evidence; an unsupported domain remains a named blocker, not a free-text fallback.

## Website parity boundary

The human builder is a website consumer only after these Website/API prerequisites are accepted: server-rendered host and loopback binding; stable error envelope; narrow semantic command bridge to the CH5 shared service; XSS-safe rendering; request correlation; and explicit trusted-host exposure policy. It must render useful HTML with JavaScript disabled. Progressive enhancement may update the current local answer object and question region, but a normal form submit remains valid.

The builder posts a complete canonical request to the approved semantic endpoint, which invokes the same CH5 validation/create service as MCP and returns the same normalized result/error envelope. It does not call MCP transport handlers, send a tool invocation from the browser, retain answers in a database/session, or call `commit(kind: "effects")`. A successful create uses post/redirect/get to the read-only character result; an invalid submission re-renders submitted untrusted values and named corrections without creating state. SSE is optional freshness only after the Website/API plan's post-commit bridge exists; it never confirms a creation before the command response/audit does.

Parity comparison uses identical campaign role, character ID, content versions, choices, and profile/ability input. Compare the CH5 canonical validation result, normalized proposed effect bundle, and committed actor/containment/receipt projection; ignore ordinary root audit/event IDs and transport metadata. Any difference is a defect in the consumer boundary, not a browser-specific rule exception.

## Dependency graph and slices

~~~text
Played CH6 fixture + accepted CH7 regression/evidence gate                    [blocked parent]
├─ CH5 pure non-writing validation/result boundary                             [required composition gate]
├─ current MCP surface/guide ID confirmation                                   [semantic gate]
└─ Website/API host, loopback write bridge, error/security/rendering decisions [external website gate]
   ├─ Slice 1: stateless MCP guide
   ├─ Slice 2: shared command boundary and parity harness
   └─ Slice 3: server-rendered website builder
      └─ CH9 advancement and CH14 authenticated control
~~~

### Slice 1 — stateless MCP guide

**Prerequisites:** CH6 protocol walk and CH7 evidence accepted; CH5 exposes the same read-only resolution used by create; guide IDs and action routing are confirmed.

1. Add guide contract/mechanic with exact answer/question schemas and zero effects.
2. Derive questions from active immutable content, choice declarations, and CH5 validation only; expose stable keys/locators/cardinality, never source prose or defaults.
3. Test empty start, each incremental answer, incomplete versus invalid, all valid completion preview, stale/archived content, unsupported domain, altered answer, cross-surface answer object, and no actor/item/audit success on every guide call.
4. Run catalog validation and focused tests; run protocol walk only if action routing/surface changes.

**Exit:** an MCP host can reach a complete legal preview from questions alone and abandon safely at every intermediate point, while the final CH5 create remains the only write.

### Slice 2 — shared command boundary and parity harness

**Prerequisites:** Slice 1 accepted; CH5 service/result boundary and Website/API semantic-command decision are confirmed.

1. Extract or verify one in-process validate/create application boundary that MCP and HTTP invoke without transport recursion or duplicated business logic.
2. Define normalized result/effect/projection comparisons and correlation/error mapping at that boundary.
3. Test identical valid/invalid inputs through MCP adapter and HTTP adapter with matching correction code, preview, source set, normalized effects, actor projection, and rollback outcome.
4. Test cancellation, timeout, duplicate ID, stale content, guard/reaction failure, and no browser/session state dependency.

**Exit:** two thin transports demonstrably consume one semantic command; neither has a character-specific rule, transaction, or database write path.

### Slice 3 — server-rendered builder

**Prerequisites:** Slice 2 accepted; Website/API host, loopback-only binding, semantic endpoint, rendering/error/security conventions, and test infrastructure are accepted.

1. Add the confirmed builder page and semantic form endpoint with server-rendered question, review, error, empty, and success states.
2. Add optional native-module enhancement for local answer editing and question refresh; disabled JavaScript remains fully usable.
3. Submit validate/create through the shared boundary; use PRG after success and a read-only result page. Never persist an abandoned form.
4. Test HTML encoding/XSS, keyboard/screen-reader field labels/errors, no-JS form flow, progressive enhancement, invalid/reload/abandon, MCP/HTTP parity, loopback restriction, and no notification on rollback.
5. Run focused browser/endpoint tests and full suite at CH8 acceptance. Run protocol walk only if MCP changes; website tests do not substitute for it.

**Exit:** a trusted local human and an MCP host can complete the same build with equivalent committed state and errors; the browser is helpful but never authoritative or required.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Statelessness | Every guide request is self-contained. Abandon/reload/back/cross-surface change leaves no server draft, entity, item, choice reservation, or cleanup work. |
| Question fidelity | Questions/options/cardinalities derive from current versioned supported content; incomplete is not silently defaulted and invalid is not silently repaired. |
| Create authority | Guide/browser preview has zero effects. Only CH5 create can make an actor, receipt, item, containment, event, or success audit. |
| MCP/HTTP parity | Identical canonical input yields matching validation codes, normalized effects, and post-commit character-world projection; mismatched transport metadata is ignored. |
| Website safety | Server-rendered no-JS form is complete; browser data is encoded/untrusted; route is loopback trusted-host until an exposure/auth decision. |
| Rollback/cancellation | Failed, stale, duplicate, guard/reaction, cancelled, or timed-out submission leaves no partial character-world state or browser-confirmed success. |
| Scope/auth boundary | Campaign scope is verified only by CH5/campaign owner. CH8 grants no identity, player control, or audience access. |
| Workflow boundary | No executable workflow is required or created. A later workflow may not duplicate CH5's root transaction. |

## Evidence and change control

The receipt records confirmed guide/command/route decisions, CH7 fixture versions, question/preview fixtures, cross-transport comparison results, browser accessibility/security evidence, catalog validation, and full-suite result. It does not store user drafts or copy source rules.

Amend CH8 before adding persisted drafts/resume, remote exposure, account/player binding, collaborative creation, workflow execution, a new MCP/HTTP surface, client-side rules, correction/advancement UI, source expansion, or a different transaction owner. Those boundaries belong to CH14, Website/API plan, executable workflow plan, `procedure.mcp.add-tool`, CH7/CH9, or CH5.
