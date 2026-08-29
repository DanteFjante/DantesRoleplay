# Application-aware workspace dependency plan — shared system components and scoped chat

Status: **complete — Slices A–H accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Ruleset alignment: **ruleset-neutral infrastructure**  
Source: **not applicable for generic infrastructure**

## Outcome

Deliver one application-aware private workspace in which:

1. reviewed applications are installed in the live registry and every application page can host
   its own application-scoped conversation;
2. every trusted page can reuse the same system navigation and system UI components;
3. a general local-AI conversation can answer questions about the system independently of any
   application;
4. that system conversation can prepare safe changes through exact `system.*` contracts and can
   execute them only after explicit operator confirmation; and
5. ordinary buttons and typed forms can invoke the same system contracts without duplicating
   authorization, validation, execution, or receipt logic in browser code.

This is one parent plan for tracking and acceptance. Implementation remains split into coherent
subslices so that UI work cannot accidentally create execution authority and system chat cannot
inherit application state.

## Terminology and scope

“System component” has two meanings in this repository. This plan uses **system web component** for
a reusable browser custom element and **ECS component type** for versioned application data. They
are not interchangeable.

System scope means capabilities that exist independently of a particular application and are
available to authorized pages across applications. Examples include navigation, application and
source registration, activation, state-space administration, system settings, page administration,
operation history, and system documentation. A system capability may administer an application,
but it must still be named `system.*` and implemented by a generic system owner.

Application scope means contracts, ECS state, mechanics, queries, and actions owned by one
registered application such as `dnd2024.*`. General system chat does not read or mutate application
runtime state merely because it is embedded on that application's page. Application chat continues
to require an exact application and state-space binding.

## Non-goals and invariants

- Do not register an application named `system`; `system` remains a reserved platform namespace.
- Browser components are request surfaces, never authority. They do not call MCP, SQL, the local
  model, files, or application mechanics directly.
- No raw URL, method, SQL, JavaScript, file path, prompt, provider/model, authorization decision,
  or effect body is accepted from an element attribute.
- A `system.*` prefix alone does not make a capability safe. Only an explicitly registered,
  schema-valid, currently authorized system capability may be queried or proposed.
- Read-only system queries may run without mutation confirmation. Every write is an inert proposal
  first and requires a separate exact confirmation and idempotency key.
- The system coordinator does not interpret game vocabulary or application rules. C# stays generic;
  JavaScript/catalog owners retain application rule behavior.
- General chat cannot silently cross into an application chat. It may explain that an
  application-scoped request needs an application/state-space selection and hand back a safe link.
- No arbitrary filesystem/database access, secrets, private catalog inference, hidden model
  reasoning, remote-provider fallback, or public-internet hosting is introduced.

## Existing owners and evidence

| Concern | Owner | State | Evidence/consequence |
| --- | --- | --- | --- |
| Application registration, activation, and state spaces | application kernel and existing `system.*` MCP kinds | verified | Reuse services; do not reimplement them in web code. |
| Application-scoped outer chat and execution | interaction orchestration plus `ApplicationConversationService` | verified | Existing `<application-conversation>` remains the application surface. |
| Multi-task/batch planning and receipts | interaction orchestration Slices 12–13 | verified for applications | System orchestration may reuse generic shapes, not application authority or state. |
| Durable operator assistant conversations | `assistant-conversations` | verified but context-free and advisory-only | Extend through an explicit system scope; do not quietly alter old conversations. |
| Local model provider | `local-ai` | verified | Remains schema-only and unaware of web, games, databases, and files. |
| Private operator authorization | `authorization` and web control security | verified but capability-specific | New system read/propose/execute decisions require explicit policy entries and audit names. |
| Existing system query/commit catalog | MCP generic verb surface and underlying generic services | verified but MCP-centered | Extract one reusable capability catalog; web must not depend on MCP adapters. |
| Registered app/state discovery | `ControlStructureExplorer` | verified | Reuse bounded reads for navigation and selectors. |
| Trusted uploaded pages | web interface | verified | Pages may import host-served components but receive no implicit authority. |
| Shared system web-component library | no current owner | missing | Add one web-owned browser-native bundle. |
| Live normal application registrations | normal SQLite database | empty at inspection | Onboard only explicitly selected accepted packages through MCP. |

## Target architecture

```text
Trusted home or application page
│
├─ <system-navigation> ─────────────── read-only application/page discovery
├─ <system-chat> ───────────────────── durable operator system conversation
├─ <system-action-button> ─┐
└─ <system-form> ──────────┴────────── named system interaction prepare/confirm flow
                                      │
                                      ▼
                         web system-interaction adapter
                                      │
                         verified principal + authorization
                                      │
                         system capability catalog
                         ├─ exact descriptor and input/output schemas
                         ├─ read handler OR prepare/execute handler
                         └─ procedure, sensitivity, and confirmation metadata
                                      │
                         system interaction coordinator
                         ├─ bounded system context/retrieval
                         ├─ inert task plan
                         ├─ explicit confirmation
                         └─ audited idempotent receipt

<application-conversation> ───────── existing application interaction coordinator
                                     └─ exact application + state space only
```

The two coordinators may share generic plan/status/receipt value objects, but neither may call the
other as a fallback. This prevents a general system conversation from acquiring application action
authority and prevents a player conversation from acquiring system-administration authority.

## Proposed shared browser components

The recommended single module route is `/components/system-workspace.js`. It defines the following
proposed permanent custom-element IDs:

| Element | Purpose | Authority boundary |
| --- | --- | --- |
| `<system-navigation>` | Home, control center, registered application links, pagination, and selected-page state. | Read-only discovery; unavailable/empty states remain navigable. |
| `<system-chat>` | General system questions and confirmed system task plans. | Operator system scope only; never receives application ECS state. |
| `<system-action-button>` | Prepare one exact named system capability from bounded declared input. | The button cannot execute directly; writes display the server proposal and confirmation UI. |
| `<system-form>` | Render schema-driven text, number, boolean, enum, and bounded JSON inputs for one exact capability. | Server schema remains authoritative; client validation is convenience only. |

All components expose ordinary DOM events such as `system-progress`, `system-proposal`,
`system-receipt`, and `system-error`; CSS custom properties and `::part` names allow an application
to theme them without replacing behavior. The initial bundle uses no framework and no application
vocabulary. Existing `<application-conversation>` can later be exported by the same module while
retaining its existing route for compatibility.

An element receives only a capability ID and non-secret bounded values. It never receives or stores
authorization evidence, provider configuration, hidden context, expected current fingerprints, or
server-derived confirmation truth.

## System capability catalog

Add a ruleset-neutral system component that owns reusable capability descriptors and dispatch,
rather than copying the MCP switch into the web interface. Each system subsystem registers its own
descriptors and handlers through dependency injection. A descriptor contains at minimum:

- permanent `system.*` ID and owner;
- read or write mode;
- closed input schema and safe output schema;
- procedure references and user-facing description;
- required authorization capability;
- sensitivity/redaction class;
- whether explicit confirmation and an idempotency key are required; and
- prepare/read and execute handlers supplied by the owning subsystem.

The catalog rejects duplicate IDs at startup. MCP and web adapters resolve the same descriptor and
invoke the same owner. Neither adapter becomes semantic authority. Migrating every legacy MCP kind
at once is not required: the first slice covers the allowlisted capabilities used by the shared UI,
then parity is expanded with compatibility tests.

## General system chat

General chat is an explicit `system` conversation scope, not an application ID and not the existing
unscoped advisory mode. Creation binds the conversation immutably to the verified private operator,
provider identity, and system scope. Old advisory conversations remain advisory-only.

The server may materialize bounded, provenance-bearing context from:

- safe system procedure summaries;
- the registered system capability catalog;
- application registry, activation, source, and state-space summaries as system metadata;
- authorized system settings and page metadata with secret values redacted; and
- safe operation/receipt summaries relevant to the current task.

It does not materialize application ECS values, private catalog content, source-file contents,
credentials, raw database rows, prompts, model reasoning, or unrestricted history. When a question
needs application knowledge, the response identifies the required application/state-space scope
and offers navigation or an explicit application-chat handoff; it does not broaden itself.

The inner local model first searches/inspects the registered system contracts. If it cannot find a
complete contract, it returns `unknown`, `unsupported`, `needs-input`, or `unavailable` with a
receipt. The selected outer model may prepare a system plan from the same allowlisted contracts.
Local outer AI remains supported through the accepted provider-selection boundary.

## System edits and task execution

System chat, action buttons, and forms all use one coordinator:

1. Resolve exact current system capability descriptors and authorize system read or propose scope.
2. Automatically run only safe read steps, retaining bounded result/provenance evidence.
3. Build an inert task agenda for writes. Every step names an exact descriptor/version/fingerprint
   and closed schema-valid input; the model cannot supply effects or authorization.
4. Show the complete safe proposal, affected system owners, expected revisions/fingerprints, and
   whether earlier steps can remain committed if a later step fails.
5. Require explicit operator confirmation for each write batch and a new idempotency key.
6. Rehydrate current authority and contract state, reject drift, and invoke the exact owner.
7. Record one audited receipt per step and an aggregate task receipt. Equal retries replay; conflicts
   and stale state remain inert.
8. Return typed results for the chat narrator or DOM consumer. Narration never changes receipt truth.

Initial write coverage should be limited to already accepted generic services: application/source/
component-type registration, application activation, and state-space create/upgrade/adoption. Page
or settings writes are added only after their existing revision/rollback contracts are adapted.
No generic “call any `system.*` string” endpoint is permitted.

## Dependency tree and ordered subslices

```text
Application-aware workspace with shared system components                    [complete]
├─ A. Live reviewed application onboarding                                   [accepted]
├─ B. Shared system web-component foundation and navigation                  [accepted]
├─ C. Reusable system capability catalog and read adapter                    [accepted]
├─ D. Context-aware general system chat, read-only                           [accepted]
├─ E. Confirmed system task planning, execution, and receipts                [accepted]
├─ F. Reusable system action button and schema-driven form                   [accepted]
├─ G. Application-page composition and application chat adoption             [accepted]
└─ H. Combined privacy, authority, compatibility, and live acceptance         [accepted]
```

| Slice | Delivery | Model | Exit gate |
| --- | --- | --- | --- |
| A | Export live evidence; install selected accepted apps/state spaces through existing MCP; verify homepage picker. | Terra High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-RECEIPT.md). Exact registrations, fingerprints, state bindings, and MCP receipts read back; one generic soft-delete adoption defect corrected without a new protocol. |
| B | Add `/components/system-workspace.js`, `<system-navigation>`, common styling/events, and empty/paged/unavailable behavior. | Terra High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-B-RECEIPT.md). Home, control center, and fixture app share identical authoritative navigation without inline duplication. |
| C | Add system capability descriptors/registry/read dispatch; adapt a small allowlist and prove MCP/web parity. | Sol High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-RECEIPT.md). Duplicate/unknown/unauthorized/schema-invalid calls fail closed; MCP and web use the same exact handler. |
| D | Add immutable system conversation scope and bounded context materializer; deliver read-only `<system-chat>`. | Sol High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-D-RECEIPT.md). Relevant system questions work; application data, files, secrets, and old advisory scopes cannot leak. |
| E | Add inert multi-step system planning, separate confirmation, current-authority execution, idempotency, audit, and receipts. | Sol xhigh | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-E-RECEIPT.md). Read steps and confirmed write batches behave exactly; stale/replay/partial/denied cases cannot double-write or overclaim. |
| F | Add `<system-action-button>` and `<system-form>` over the same coordinator. | Terra High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-F-RECEIPT.md). Components cannot bypass prepare/confirmation; schema/error/receipt accessibility and DOM-event tests pass. |
| G | Register shared components on home/control/app pages; host existing application chat with exact app/state binding and system chat without that binding. | Terra High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-G-RECEIPT.md). Every selected app is reachable and can chat; system and application scope cannot cross. |
| H | Run combined protocol, migration, browser, catalog, privacy, full-suite, and live smoke acceptance. | Sol High | [Accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md). The receipt maps every invariant, live revision, model smoke, and deliberate exclusion. |

Terra is suitable for the contained browser composition and onboarding slices. Sol is required for
capability authority, AI context, confirmation, replay, and the combined security audit. Slice E is
the only xhigh recommendation because it creates generic system-write orchestration.

## Implementation instructions for AI agents

1. Treat this document as the parent dependency plan, not permission to implement every slice at
   once. Author one active slice document with exact allowed areas and stop point.
2. Before each slice, read only its named owners and prerequisite receipts. Search for existing IDs
   and services before proposing new ones.
3. Obtain confirmation for permanent IDs/routes, capability meanings, authorization entries,
   migrations, live registrations, and completed feature acceptance.
4. Keep web components in the web-interface directory. Put generic capability catalog/coordinator
   code in separately owned `src/system/` component directories; do not grow one web or MCP switch.
5. Keep local-AI adapters unaware of system/app data. The host constructs closed context and passes
   schemas; the model never scans the workspace or database.
6. Tests use disposable databases and fake providers. Do not initialize, migrate, import, or mutate
   the normal host database except Slice A and the final explicitly confirmed live smoke boundary.
7. Test negative authority and no-change behavior before positive mutation behavior. Every denied,
   stale, malformed, cross-scope, replay-conflict, or unavailable result must prove no write.
8. After catalog changes run `roleplay validate catalog`; after public/MCP dependency changes run
   the protocol walk; at feature acceptance run the full suite and browser verification.
9. Record a focused receipt for every accepted slice, remove completed prospective prose only when
   the authoritative contracts and receipt retain the delivered meaning, and update the roadmap.

## Confirmed decisions

The user confirmed this parent boundary before Slice A implementation, including:

- which accepted applications and initial state spaces to install in the normal database;
- public module route `/components/system-workspace.js` and element IDs `system-navigation`,
  `system-chat`, `system-action-button`, and `system-form`;
- general chat is a distinct operator-only system scope, while existing application chat remains
  application/state-space scoped;
- system chat may read the bounded system context listed above but not application ECS or files;
- system writes always use inert proposals, explicit per-batch confirmation, current-authority
  revalidation, idempotency, and receipts;
- initial writes are limited to the already accepted registry/activation/state-space services; and
- application navigation initially targets the existing control-center application workspace until
  an application-to-page association contract is separately confirmed.

## Planning receipt

- Runtime artifacts created: none.
- Existing live page revision 4 and normal database remain unchanged by this plan.
- Exact stop: no app was registered, no route/element/system capability/authorization/migration was
  created, and no assistant context or execution authority changed.
